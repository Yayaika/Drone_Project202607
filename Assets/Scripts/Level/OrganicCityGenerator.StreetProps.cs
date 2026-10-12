using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 有機城市生成器：街道設施 (路燈、紅綠燈、路牌、長椅、消防栓、行道樹…)。
/// 每條「設施規則」指定模型、放置方式與密度，可在 Inspector 自行新增。
/// </summary>
public partial class OrganicCityGenerator
{
    public enum PropPlacement
    {
        [InspectorName("沿人行道")] Sidewalk,
        [InspectorName("路口轉角")] Intersection,
        [InspectorName("公園內")] Park
    }

    public enum PropFacing
    {
        [InspectorName("面向道路")] FaceRoad,
        [InspectorName("順著道路")] AlongRoad,
        [InspectorName("隨機")] Random
    }

    [System.Flags]
    public enum RoadTierMask
    {
        [InspectorName("主幹道")] Main = 1,
        [InspectorName("支路")] Secondary = 2,
        [InspectorName("巷道")] Lane = 4
    }

    [System.Serializable]
    public class StreetPropRule
    {
        public string name = "新設施";
        public bool enabled = true;
        [Tooltip("隨機挑選其中一個模型")]
        public GameObject[] prefabs;
        public PropPlacement placement = PropPlacement.Sidewalk;
        [Tooltip("適用的道路等級 (公園內放置時不使用)")]
        public RoadTierMask roads = RoadTierMask.Main | RoadTierMask.Secondary | RoadTierMask.Lane;
        [Tooltip("平均間距 (公尺)。路口轉角不使用")]
        [Min(2f)] public float spacing = 30f;
        [Tooltip("每個候選位置實際放置的機率")]
        [Range(0f, 1f)] public float chance = 1f;
        [Tooltip("在人行道上的位置：0 = 靠路緣，1 = 靠建築")]
        [Range(0f, 1f)] public float sidewalkPosition = 0.2f;
        [Tooltip("道路兩側都放 (沿人行道時)")]
        public bool bothSides = true;
        public PropFacing facing = PropFacing.FaceRoad;
        [Tooltip("額外旋轉 (度)，模型方向不對時調整")]
        public float yawOffset;
        public Vector2 scaleRange = Vector2.one;
        [Tooltip("與其他設施保持的最小距離 (公尺)")]
        [Min(0f)] public float clearance = 1.5f;
    }

    [Header("街道設施 (LowpolyStreetPack，可自行新增規則)")]
    [SerializeField] private bool placeStreetProps = true;
    [Tooltip("移除設施內建的燈光 (數百盞即時光源會嚴重拖慢效能)")]
    [SerializeField] private bool disablePropLights = true;
    [Tooltip("隱藏路燈的光錐模型 (白天看起來像半透明圓錐)")]
    [SerializeField] private bool hideLightCones = true;
    [SerializeField] private StreetPropRule[] propRules;

    [System.Serializable]
    public struct MaterialRemap
    {
        public Material from;
        public Material to;
    }

    [Tooltip("生成時替換設施/樹木的材質 (LowpolyStreetPack 原本是 Built-in 著色器，在 URP 會顯示洋紅色)")]
    [SerializeField] private MaterialRemap[] propMaterialRemaps;

    [Header("樹木模型 (留空則使用簡易樹)")]
    [SerializeField] private GameObject[] treePrefabs;
    [SerializeField] private Vector2 treeScaleRange = new Vector2(1.5f, 2.3f);

    private class Approach
    {
        public Vector2 Direction; // 駛入路口的方向
        public float HalfWidth;
        public RoadTier Tier;
    }

    private class Junction
    {
        public Vector2 Point;
        public readonly List<Approach> Approaches = new List<Approach>();
    }

    private readonly List<Vector2> propPoints = new List<Vector2>();
    private readonly List<float> propRadii = new List<float>();
    private readonly SpatialGrid propGrid = new SpatialGrid(16f);
    private int propCount;

    private readonly Dictionary<Material, Material> remapLookup = new Dictionary<Material, Material>();

    private void PrepareMaterialRemaps()
    {
        remapLookup.Clear();
        if (propMaterialRemaps == null) return;
        foreach (MaterialRemap remap in propMaterialRemaps)
        {
            if (remap.from != null && remap.to != null) remapLookup[remap.from] = remap.to;
        }
    }

    private void RemapMaterials(GameObject target)
    {
        if (remapLookup.Count == 0) return;
        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null && remapLookup.TryGetValue(materials[i], out Material replacement))
                {
                    materials[i] = replacement;
                    changed = true;
                }
            }
            if (changed) renderer.sharedMaterials = materials;
        }
    }

    private void PlaceStreetProps()
    {
        propPoints.Clear();
        propRadii.Clear();
        propGrid.Clear();
        propCount = 0;
        if (!placeStreetProps || propRules == null) return;

        List<Junction> junctions = null;
        foreach (StreetPropRule rule in propRules)
        {
            if (rule == null || !rule.enabled || !HasAnyPrefab(rule.prefabs)) continue;

            switch (rule.placement)
            {
                case PropPlacement.Sidewalk:
                    PlaceSidewalkProps(rule);
                    break;
                case PropPlacement.Intersection:
                    if (junctions == null) junctions = FindJunctions();
                    PlaceIntersectionProps(rule, junctions);
                    break;
                case PropPlacement.Park:
                    PlaceParkProps(rule);
                    break;
            }
        }
    }

    private static bool TierAllowed(StreetPropRule rule, RoadTier tier) => (rule.roads & (RoadTierMask)(1 << (int)tier)) != 0;

    private float SidewalkOffset(StreetPropRule rule) => sidewalkWidth * Mathf.Lerp(0.15f, 0.85f, rule.sidewalkPosition);

    private void PlaceSidewalkProps(StreetPropRule rule)
    {
        foreach (Road road in roads)
        {
            if (road.Removed || road.Points.Count < 2 || !TierAllowed(rule, road.Tier)) continue;

            float total = RoadLength(road);
            foreach (float side in rule.bothSides ? new[] { -1f, 1f } : new[] { 1f })
            {
                float distance = RandomRange(0f, rule.spacing);
                while (distance < total)
                {
                    if (random.NextDouble() < rule.chance)
                    {
                        PointOnRoad(road, distance, out Vector2 point, out Vector2 tangent);
                        Vector2 normal = new Vector2(-tangent.y, tangent.x) * side;
                        Vector2 position = point + normal * (road.HalfWidth + SidewalkOffset(rule));
                        TryPlaceProp(rule, position, -normal, tangent);
                    }
                    distance += rule.spacing * RandomRange(0.8f, 1.2f);
                }
            }
        }
    }

    private void PlaceIntersectionProps(StreetPropRule rule, List<Junction> junctions)
    {
        foreach (Junction junction in junctions)
        {
            float crossHalf = 0f;
            foreach (Approach approach in junction.Approaches) crossHalf = Mathf.Max(crossHalf, approach.HalfWidth);

            foreach (Approach approach in junction.Approaches)
            {
                if (!TierAllowed(rule, approach.Tier) || random.NextDouble() > rule.chance) continue;

                // 靠右行駛：放在駛入方向的右前方轉角
                Vector2 right = new Vector2(approach.Direction.y, -approach.Direction.x);
                Vector2 position = junction.Point
                                   - approach.Direction * (crossHalf + sidewalkWidth * 0.6f)
                                   + right * (approach.HalfWidth + SidewalkOffset(rule));
                TryPlaceProp(rule, position, -right, approach.Direction);
            }
        }
    }

    private void PlaceParkProps(StreetPropRule rule)
    {
        const float cell = 6f;
        float chancePerCell = Mathf.Clamp01(cell * cell / (rule.spacing * rule.spacing)) * rule.chance;
        float extent = cityRadius * (1f + boundaryIrregularity);
        for (float x = -extent; x < extent; x += cell)
        {
            for (float z = -extent; z < extent; z += cell)
            {
                Vector2 center = new Vector2(x + cell * 0.5f, z + cell * 0.5f);
                if (!IsPark(center) || !InsideBoundary(center, 1f) || random.NextDouble() > chancePerCell) continue;

                Vector2 position = center + new Vector2(RandomRange(-2.5f, 2.5f), RandomRange(-2.5f, 2.5f));
                if (RoadClearance(position) < 1f) continue;
                TryPlaceProp(rule, position, Direction(RandomRange(0f, 360f)), Direction(RandomRange(0f, 360f)));
            }
        }
    }

    private void TryPlaceProp(StreetPropRule rule, Vector2 position, Vector2 towardRoad, Vector2 along)
    {
        if (!InsideBoundary(position, 1.05f)) return;
        if (centralPlaza && position.magnitude < plazaRadius - mainRoadWidth * 0.5f) return;
        if (OnAsphalt(position, 0.3f)) return;
        if (OverlapsBuildings(PointFootprint(position, 0.6f))) return;
        if (NearOtherProp(position, rule.clearance)) return;

        GameObject prefab = PickPrefab(rule.prefabs);
        if (prefab == null) return;

        Vector2 facing;
        switch (rule.facing)
        {
            case PropFacing.FaceRoad: facing = towardRoad; break;
            case PropFacing.AlongRoad: facing = along; break;
            default: facing = Direction(RandomRange(0f, 360f)); break;
        }
        float yaw = Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg + rule.yawOffset;

        GameObject prop = Instantiate(prefab, root);
        prop.name = prefab.name;
        prop.transform.localPosition = new Vector3(position.x, 0.04f, position.y);
        // 保留模型根節點自身的旋轉 (LowpolyStreetPack 多為 X 270°)
        prop.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * prefab.transform.localRotation;
        prop.transform.localScale = prefab.transform.localScale * RandomRange(rule.scaleRange.x, Mathf.Max(rule.scaleRange.x, rule.scaleRange.y));
        StripLights(prop);
        RemapMaterials(prop);

        RegisterProp(position, rule.clearance);
        propCount++;
    }

    private void StripLights(GameObject prop)
    {
        if (disablePropLights)
        {
            foreach (Light light in prop.GetComponentsInChildren<Light>(true)) DestroyObject(light);
        }

        if (hideLightCones)
        {
            foreach (Renderer renderer in prop.GetComponentsInChildren<Renderer>(true))
            {
                bool allCones = renderer.sharedMaterials.Length > 0;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || !material.name.Contains("LightShape")) allCones = false;
                }
                if (allCones) renderer.enabled = false;
            }
        }
    }

    private bool OnAsphalt(Vector2 point, float margin)
    {
        Vector2 extent = Vector2.one * (mainRoadWidth + margin);
        foreach (int index in segmentGrid.Query(point - extent, point + extent))
        {
            Segment segment = segments[index];
            Road road = roads[segment.Road];
            if (road.Removed) continue;
            if (Vector2.Distance(point, ClosestPointOnSegment(point, segment.A, segment.B)) < road.HalfWidth + margin) return true;
        }
        return false;
    }

    private bool NearOtherProp(Vector2 point, float radius)
    {
        Vector2 extent = Vector2.one * (radius + 6f);
        foreach (int index in propGrid.Query(point - extent, point + extent))
        {
            if (Vector2.Distance(point, propPoints[index]) < Mathf.Max(radius, propRadii[index])) return true;
        }
        return false;
    }

    private void RegisterProp(Vector2 point, float radius)
    {
        propPoints.Add(point);
        propRadii.Add(radius);
        propGrid.Insert(propPoints.Count - 1, point, point);
    }

    /// <summary>找出三條以上道路方向交會的路口 (道路端點接到其他道路、或道路互相穿越)</summary>
    private List<Junction> FindJunctions()
    {
        List<Junction> junctions = new List<Junction>();

        for (int r = 0; r < roads.Count; r++)
        {
            Road road = roads[r];
            if (road.Removed || road.Closed || road.Points.Count < 2) continue;

            List<Vector2> points = road.Points;
            AddEndJunction(junctions, r, points[0], (points[0] - points[1]).normalized);
            AddEndJunction(junctions, r, points[points.Count - 1], (points[points.Count - 1] - points[points.Count - 2]).normalized);
        }

        // 環狀道路與其他道路互相穿越
        for (int r = 0; r < roads.Count; r++)
        {
            Road road = roads[r];
            if (road.Removed || !road.Closed) continue;

            for (int i = 1; i < road.Points.Count; i++)
            {
                Vector2 a = road.Points[i - 1];
                Vector2 b = road.Points[i];
                foreach (int index in segmentGrid.Query(Vector2.Min(a, b), Vector2.Max(a, b)))
                {
                    Segment other = segments[index];
                    if (other.Road == r || roads[other.Road].Removed) continue;
                    if (!SegmentIntersection(a, b, other.A, other.B, out _, out Vector2 point)) continue;

                    Vector2 tangent = (b - a).normalized;
                    Vector2 otherTangent = (other.B - other.A).normalized;
                    Junction junction = GetJunction(junctions, point);
                    AddApproach(junction, tangent, road);
                    AddApproach(junction, -tangent, road);
                    AddApproach(junction, otherTangent, roads[other.Road]);
                    AddApproach(junction, -otherTangent, roads[other.Road]);
                }
            }
        }

        junctions.RemoveAll(junction => junction.Approaches.Count < 3);
        return junctions;
    }

    private void AddEndJunction(List<Junction> junctions, int roadIndex, Vector2 end, Vector2 inward)
    {
        Vector2 extent = Vector2.one * 2f;
        foreach (int index in segmentGrid.Query(end - extent, end + extent))
        {
            Segment other = segments[index];
            if (other.Road == roadIndex || roads[other.Road].Removed) continue;
            if (Vector2.Distance(end, ClosestPointOnSegment(end, other.A, other.B)) > 1f) continue;

            Junction junction = GetJunction(junctions, end);
            AddApproach(junction, inward, roads[roadIndex]);
            Vector2 otherTangent = (other.B - other.A).normalized;
            AddApproach(junction, otherTangent, roads[other.Road]);
            AddApproach(junction, -otherTangent, roads[other.Road]);
            return;
        }
    }

    private static Junction GetJunction(List<Junction> junctions, Vector2 point)
    {
        foreach (Junction junction in junctions)
        {
            if (Vector2.Distance(junction.Point, point) < 4f) return junction;
        }
        Junction created = new Junction { Point = point };
        junctions.Add(created);
        return created;
    }

    private static void AddApproach(Junction junction, Vector2 direction, Road road)
    {
        foreach (Approach existing in junction.Approaches)
        {
            if (Vector2.Dot(existing.Direction, direction) > 0.9f) return; // 同方向只記一次
        }
        junction.Approaches.Add(new Approach { Direction = direction, HalfWidth = road.HalfWidth, Tier = road.Tier });
    }

    private GameObject PickPrefab(GameObject[] prefabs)
    {
        if (prefabs == null || prefabs.Length == 0) return null;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            GameObject candidate = prefabs[random.Next(prefabs.Length)];
            if (candidate != null) return candidate;
        }
        return null;
    }

    private static bool HasAnyPrefab(GameObject[] prefabs)
    {
        if (prefabs == null) return false;
        foreach (GameObject prefab in prefabs)
        {
            if (prefab != null) return true;
        }
        return false;
    }

    /// <summary>素材包樹木；沒有指定時回傳 false，改用簡易樹</summary>
    private bool TryCreateTreeModel(Vector2 position, float scale)
    {
        GameObject prefab = PickPrefab(treePrefabs);
        if (prefab == null) return false;

        GameObject tree = Instantiate(prefab, root);
        tree.name = prefab.name;
        tree.transform.localPosition = new Vector3(position.x, 0f, position.y);
        tree.transform.localRotation = Quaternion.Euler(0f, RandomRange(0f, 360f), 0f) * prefab.transform.localRotation;
        tree.transform.localScale = prefab.transform.localScale * scale * RandomRange(treeScaleRange.x, Mathf.Max(treeScaleRange.x, treeScaleRange.y));
        RemapMaterials(tree);
        return true;
    }
}

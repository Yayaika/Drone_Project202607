using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 有機城市生成器 (獨立測試用，與關卡的 <see cref="CityGenerator"/> 互不相依)。
/// 以「道路生長」產生路網：主幹道自中央圓環向外彎曲延伸，沿途分岔出支路與巷道，
/// 支路遇到其他道路會接合或截止，自然形成大小不一的街廓；另可加入不規則環狀道路。
/// 建築沿道路切線方向排列並填補街廓內部，高度依市中心距離與噪聲分區變化，
/// 公園由噪聲劃出，城市外框為不規則形狀。
/// 在 Inspector 調整參數會自動重新生成 (見 OrganicCityGeneratorEditor)。
/// </summary>
public partial class OrganicCityGenerator : MonoBehaviour
{
    private const string RootName = "GeneratedOrganicCity";

    private enum RoadTier { Main = 0, Secondary = 1, Lane = 2 }

    [Header("生成")]
    [SerializeField] private int seed = 2026;
    [Tooltip("在 Inspector 修改參數後自動重新生成 (編輯模式與 Play 模式皆可)")]
    [SerializeField] private bool autoRegenerate = true;
    [Tooltip("進入 Play 時重新生成；關閉則沿用場景中已生成的城市")]
    [SerializeField] private bool regenerateOnPlay = false;

    [Header("城市外形")]
    [Range(80f, 600f)]
    [SerializeField] private float cityRadius = 260f;
    [Tooltip("外框不規則程度 (0 = 正圓)")]
    [Range(0f, 0.6f)]
    [SerializeField] private float boundaryIrregularity = 0.3f;
    [Tooltip("外框起伏頻率 (越大凹凸越多)")]
    [Range(0.5f, 6f)]
    [SerializeField] private float boundaryFrequency = 2.2f;
    [Tooltip("在城市中心建立圓環廣場與起飛平台；關閉時主幹道直接從中心點放射")]
    [SerializeField] private bool centralPlaza = false;
    [Tooltip("中央圓環廣場半徑 (只在開啟圓環時使用)")]
    [Range(10f, 60f)]
    [SerializeField] private float plazaRadius = 22f;

    [Header("路網：主幹道")]
    [Range(2, 8)]
    [SerializeField] private int mainRoadCount = 5;
    [Range(6f, 24f)]
    [SerializeField] private float mainRoadWidth = 13f;
    [Tooltip("不規則環狀道路數量")]
    [Range(0, 4)]
    [SerializeField] private int ringRoadCount = 2;
    [Tooltip("環狀道路的半徑起伏")]
    [Range(0f, 0.5f)]
    [SerializeField] private float ringIrregularity = 0.18f;

    [Header("路網：支路與巷道")]
    [Range(4f, 16f)]
    [SerializeField] private float secondaryRoadWidth = 8.5f;
    [SerializeField] private bool enableLanes = true;
    [Range(3f, 10f)]
    [SerializeField] private float laneWidth = 5.5f;
    [Tooltip("每段可分岔時實際分岔的機率")]
    [Range(0f, 1f)]
    [SerializeField] private float branchChance = 0.55f;
    [Tooltip("兩次分岔之間的最短距離 (公尺)")]
    [Range(15f, 120f)]
    [SerializeField] private float branchSpacing = 38f;
    [Tooltip("分岔角度偏離垂直的最大值 (度)")]
    [Range(0f, 50f)]
    [SerializeField] private float branchAngleJitter = 22f;
    [Tooltip("支路最多延伸的段數")]
    [Range(3, 40)]
    [SerializeField] private int secondaryMaxSegments = 16;
    [Tooltip("巷道最多延伸的段數")]
    [Range(2, 20)]
    [SerializeField] private int laneMaxSegments = 7;

    [Header("路網：彎曲與接合")]
    [Range(6f, 30f)]
    [SerializeField] private float segmentLength = 13f;
    [Tooltip("道路受噪聲影響的轉彎幅度 (每段最多轉幾度)")]
    [Range(0f, 40f)]
    [SerializeField] private float curvature = 14f;
    [Tooltip("彎曲噪聲的尺度 (越小彎得越平緩、越大越扭)")]
    [Range(0.001f, 0.03f)]
    [SerializeField] private float curveNoiseScale = 0.006f;
    [Tooltip("每段的隨機抖動 (度)")]
    [Range(0f, 15f)]
    [SerializeField] private float wobble = 3f;
    [Tooltip("接近其他道路節點時自動接上的距離")]
    [Range(4f, 40f)]
    [SerializeField] private float snapDistance = 14f;
    [Tooltip("平行道路之間的最小間距")]
    [Range(10f, 80f)]
    [SerializeField] private float minRoadSpacing = 26f;
    [Range(200, 6000)]
    [SerializeField] private int maxSegments = 2500;
    [Tooltip("道路外觀的平滑次數 (消除折角)")]
    [Range(0, 4)]
    [SerializeField] private int roadSmoothing = 2;

    [Header("建築")]
    [Range(0f, 1f)]
    [SerializeField] private float buildingDensity = 0.9f;
    [Range(0f, 6f)]
    [SerializeField] private float sidewalkWidth = 2.5f;
    [Tooltip("建築退縮距離 (人行道外)")]
    [Range(0f, 8f)]
    [SerializeField] private float setback = 1.2f;
    [SerializeField] private Vector2 frontageRange = new Vector2(9f, 20f);
    [SerializeField] private Vector2 depthRange = new Vector2(9f, 22f);
    [Tooltip("相鄰建築的間隔")]
    [Range(0f, 10f)]
    [SerializeField] private float buildingGap = 2f;
    [Tooltip("街廓內部補建築的嘗試次數 (越多越密)")]
    [Range(0, 10000)]
    [SerializeField] private int infillAttempts = 3000;
    [Tooltip("內部建築距離道路的上限 (避免出現孤立的建築)")]
    [Range(10f, 120f)]
    [SerializeField] private float infillMaxRoadDistance = 45f;

    [Header("建築高度")]
    [SerializeField] private float minHeight = 6f;
    [SerializeField] private float maxHeight = 75f;
    [Tooltip("市中心高樓區佔城市半徑的比例")]
    [Range(0.05f, 1f)]
    [SerializeField] private float downtownRadius = 0.4f;
    [Tooltip("高度隨離開市中心而遞減的曲線 (越大高樓越集中)")]
    [Range(0.3f, 4f)]
    [SerializeField] private float heightFalloff = 1.6f;
    [Tooltip("噪聲對高度的影響 (形成高低不同的街區)")]
    [Range(0f, 1f)]
    [SerializeField] private float heightNoise = 0.45f;
    [Range(0.002f, 0.05f)]
    [SerializeField] private float heightNoiseScale = 0.012f;

    [Header("公園與樹木")]
    [Tooltip("公園面積比例 (0 = 沒有公園)")]
    [Range(0f, 1f)]
    [SerializeField] private float parkAmount = 0.3f;
    [Range(0.002f, 0.03f)]
    [SerializeField] private float parkNoiseScale = 0.008f;
    [Range(0f, 1f)]
    [SerializeField] private float treeDensity = 0.35f;
    [Tooltip("城市外圍的樹木數量")]
    [Range(0, 1500)]
    [SerializeField] private int outskirtTrees = 350;

    [Header("建築模型：POLYGON city pack (由選單自動填入)")]
    [Tooltip("使用素材包模型的比例；其餘為程序化方塊大樓")]
    [Range(0f, 1f)]
    [SerializeField] private float modelRatio = 1f;
    [Tooltip("模型放不下時改用程序化方塊大樓填補空地")]
    [SerializeField] private bool boxFallback = true;
    [Tooltip("模型正面朝向的額外修正 (度)。正面已依門口方向自動偵測，通常保持 0")]
    [SerializeField] private float modelYawOffset = 0f;
    [Tooltip("模型最小/最大等比縮放 (素材包為真實公尺比例，接近 1 最自然)")]
    [Range(0.4f, 1f)]
    [SerializeField] private float minModelScale = 0.65f;
    [Range(1f, 1.5f)]
    [SerializeField] private float maxModelScale = 1.1f;
    [Tooltip("模型垂直拉伸上限，避免窗戶過度變形")]
    [Range(1f, 2f)]
    [SerializeField] private float maxVerticalStretch = 1.25f;
    [SerializeField] private GameObject[] buildingModels;

    [Header("素材包材質 (由選單自動填入)")]
    [Tooltip("有專屬貼圖的模型 (商店、超市、消防局…)：貼圖只套用在招牌子網格")]
    [SerializeField] private ModelMaterialOverride[] texturedModels;
    [Tooltip("無貼圖的大樓：牆面隨機配色")]
    [SerializeField] private Material[] facadeMaterials;
    [SerializeField] private Material[] roofMaterials;
    [SerializeField] private Material[] windowMaterials;
    [SerializeField] private Material trimMaterial;
    [SerializeField] private Material doorMaterial;
    [SerializeField] private Material accentMaterial;

    [Header("材質 (由選單自動建立，可留空)")]
    [SerializeField] private Material roadMaterial;
    [SerializeField] private Material sidewalkMaterial;
    [SerializeField] private Material roadLineMaterial;
    [SerializeField] private Material groundMaterial;
    [SerializeField] private Material parkMaterial;
    [SerializeField] private Material plazaMaterial;
    [SerializeField] private Material roofMaterial;
    [SerializeField] private Material treeTrunkMaterial;
    [SerializeField] private Material[] treeLeafMaterials;
    [SerializeField] private Material[] buildingMaterials;

    [SerializeField, HideInInspector] private string lastStats = "";

    [System.Serializable]
    private struct ModelMaterialOverride
    {
        public GameObject Model;
        public Material Material;
    }

    private class Road
    {
        public RoadTier Tier;
        public float HalfWidth;
        public bool Removed;
        public bool Closed;
        public readonly List<Vector2> Points = new List<Vector2>();
    }

    private struct Segment
    {
        public Vector2 A;
        public Vector2 B;
        public int Road;
        public int Index; // 在該道路中的第幾段
    }

    private class Agent
    {
        public Vector2 Position;
        public float Angle; // 度，0 = +Z
        public RoadTier Tier;
        public int Road;
        public int ParentRoad = -1;
        public int StepsLeft;
        public float Travelled;
        public float SinceBranch;
    }

    private struct Footprint
    {
        public Vector2 Center;
        public Vector2 AxisX; // 沿道路
        public Vector2 AxisZ; // 面向道路
        public float HalfX;
        public float HalfZ;
    }

    private readonly List<Road> roads = new List<Road>();
    private readonly List<Segment> segments = new List<Segment>();
    private readonly SpatialGrid segmentGrid = new SpatialGrid(32f);
    private readonly List<Footprint> footprints = new List<Footprint>();
    private readonly SpatialGrid footprintGrid = new SpatialGrid(32f);

    private System.Random random;
    private Vector2 boundaryNoiseOffset;
    private Vector2 curveNoiseOffset;
    private Vector2 heightNoiseOffset;
    private Vector2 parkNoiseOffset;
    private Transform root;
    private int buildingCount;
    private int treeCount;
    private int rejectedBuildings;
    private int boxBuildingCount;
    private readonly List<ModelInfo> modelInfos = new List<ModelInfo>();
    private readonly List<KeyValuePair<float, ModelInfo>> candidateBuffer = new List<KeyValuePair<float, ModelInfo>>();
    private bool regenerateRequested;

    public string LastStats => lastStats;
    public Vector3 SpawnPoint => transform.position + Vector3.up * 0.6f;
    public float CityRadius => cityRadius;

    public int Seed
    {
        get => seed;
        set => seed = value;
    }

    public bool AutoRegenerate => autoRegenerate;

    private void Awake()
    {
        if (regenerateOnPlay || FindRoot() == null)
        {
            Generate();
        }
    }

    private void Update()
    {
        if (regenerateRequested)
        {
            regenerateRequested = false;
            Generate();
        }
    }

    /// <summary>Play 模式中由 Inspector 或測試相機要求在下一幀重新生成</summary>
    public void RequestRegenerate()
    {
        regenerateRequested = true;
    }

    [ContextMenu("重新生成")]
    public void Generate()
    {
        float startTime = Time.realtimeSinceStartup;
        Clear();

        random = new System.Random(seed);
        boundaryNoiseOffset = RandomOffset();
        curveNoiseOffset = RandomOffset();
        heightNoiseOffset = RandomOffset();
        parkNoiseOffset = RandomOffset();

        roads.Clear();
        segments.Clear();
        segmentGrid.Clear();
        footprints.Clear();
        footprintGrid.Clear();
        buildingCount = 0;
        treeCount = 0;
        rejectedBuildings = 0;
        boxBuildingCount = 0;
        PrepareModels();
        PrepareMaterialRemaps();

        root = new GameObject(RootName).transform;
        root.SetParent(transform, false);

        BuildRoadNetwork();

        BuildGround();
        BuildRoadMeshes();
        if (centralPlaza) BuildPlaza();
        BuildParks();
        PlaceFrontageBuildings();
        PlaceInfillBuildings();
        PlaceStreetProps();
        PlaceOutskirtTrees();

        int roadCount = 0;
        foreach (Road road in roads) if (!road.Removed) roadCount++;
        lastStats = $"道路 {roadCount} 條 / {segments.Count} 段，建築 {buildingCount} 棟 (素材包模型 {buildingCount - boxBuildingCount}、方塊 {boxBuildingCount})，樹木 {treeCount} 棵，街道設施 {propCount} 個，" +
                    $"耗時 {(Time.realtimeSinceStartup - startTime) * 1000f:0} ms";
    }

    [ContextMenu("清除")]
    public void Clear()
    {
        Transform existing = FindRoot();
        while (existing != null)
        {
            existing.SetParent(null);
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
            existing = FindRoot();
        }
        root = null;
    }

    private Transform FindRoot() => transform.Find(RootName);

    /// <summary>主幹道起點離中心的距離 (有圓環時為圓環半徑)</summary>
    private float CenterRadius => centralPlaza ? plazaRadius : 0f;

    #region 路網生長

    private void BuildRoadNetwork()
    {
        // 中央圓環 (可關閉)
        if (centralPlaza) AddRingRoad(plazaRadius, 0f, RoadTier.Main, mainRoadWidth * 0.5f, false);

        List<Agent> mainAgents = new List<Agent>();
        List<Agent> secondaryAgents = new List<Agent>();
        List<Agent> laneAgents = new List<Agent>();

        float baseAngle = RandomRange(0f, 360f);
        float sector = 360f / mainRoadCount;
        for (int i = 0; i < mainRoadCount; i++)
        {
            float angle = baseAngle + i * sector + RandomRange(-0.3f, 0.3f) * sector;
            Vector2 start = Direction(angle) * CenterRadius;
            int roadIndex = NewRoad(RoadTier.Main, start);
            mainAgents.Add(new Agent
            {
                Position = start, Angle = angle, Tier = RoadTier.Main, Road = roadIndex,
                StepsLeft = 400, SinceBranch = branchSpacing * 0.5f
            });
        }

        RunAgents(mainAgents, secondaryAgents);

        // 環狀道路：在主幹道之後建立，分岔出的支路會與主幹道一起競爭空間
        for (int i = 1; i <= ringRoadCount; i++)
        {
            float radius = Mathf.Lerp(CenterRadius + minRoadSpacing * 1.5f, cityRadius * 0.85f, i / (float)(ringRoadCount + 0.6f));
            int ringIndex = AddRingRoad(radius, ringIrregularity, RoadTier.Secondary, secondaryRoadWidth * 0.5f, true);
            if (ringIndex >= 0) SpawnBranchesAlong(ringIndex, RoadTier.Secondary, secondaryAgents);
        }

        RunAgents(secondaryAgents, enableLanes ? laneAgents : null);
        if (enableLanes) RunAgents(laneAgents, null);
    }

    /// <summary>所有代理輪流前進一段，讓道路公平地競爭空間</summary>
    private void RunAgents(List<Agent> agents, List<Agent> spawned)
    {
        List<Agent> active = new List<Agent>(agents);
        int guard = 0;
        while (active.Count > 0 && segments.Count < maxSegments && guard++ < 100000)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Agent agent = active[i];
                if (!StepAgent(agent, spawned))
                {
                    FinishRoad(agent);
                    active.RemoveAt(i);
                }
            }
        }

        foreach (Agent agent in active) FinishRoad(agent);
    }

    /// <summary>前進一段；回傳 false 代表這條路結束</summary>
    private bool StepAgent(Agent agent, List<Agent> spawned)
    {
        if (agent.StepsLeft <= 0 || segments.Count >= maxSegments) return false;

        float length = agent.Tier == RoadTier.Lane ? segmentLength * 0.8f : segmentLength;
        float curveMultiplier = agent.Tier == RoadTier.Main ? 0.7f : agent.Tier == RoadTier.Secondary ? 1f : 1.3f;
        float noise = Mathf.PerlinNoise(agent.Position.x * curveNoiseScale + curveNoiseOffset.x,
                                        agent.Position.y * curveNoiseScale + curveNoiseOffset.y) - 0.5f;
        agent.Angle += noise * 2f * curvature * curveMultiplier + RandomRange(-wobble, wobble);

        Vector2 direction = Direction(agent.Angle);
        Vector2 next = agent.Position + direction * length;

        if (!InsideBoundary(next, 0.92f)) return false;

        if (agent.Tier != RoadTier.Main)
        {
            // 1. 穿越其他道路：在交點接上並結束
            if (FindCrossing(agent, agent.Position, next, out Vector2 crossing))
            {
                AddPoint(agent, crossing);
                return false;
            }

            // 2. 接近其他道路的節點：直接接上，形成環路
            if (FindSnapNode(agent, next, out Vector2 node))
            {
                AddPoint(agent, node);
                return false;
            }

            // 3. 太靠近其他道路：斜向接近就接上，平行則截止
            if (FindNearbySegment(agent, next, minRoadSpacing * 0.5f, out Segment nearby, out Vector2 closest))
            {
                Vector2 nearbyDirection = (nearby.B - nearby.A).normalized;
                if (Mathf.Abs(Vector2.Dot(nearbyDirection, direction)) < 0.7f)
                {
                    AddPoint(agent, closest);
                }
                return false;
            }
        }

        AddPoint(agent, next);
        agent.Position = next;
        agent.StepsLeft--;
        agent.Travelled += length;
        agent.SinceBranch += length;

        TryBranch(agent, spawned);
        return true;
    }

    private void TryBranch(Agent agent, List<Agent> spawned)
    {
        if (spawned == null || agent.Tier == RoadTier.Lane) return;

        float spacing = agent.Tier == RoadTier.Main ? branchSpacing : branchSpacing * 0.8f;
        if (agent.SinceBranch < spacing || random.NextDouble() > branchChance) return;

        agent.SinceBranch = 0f;
        RoadTier childTier = agent.Tier + 1;
        foreach (float side in new[] { -90f, 90f })
        {
            if (random.NextDouble() > 0.75) continue;
            SpawnAgent(agent.Position, agent.Angle + side + RandomRange(-branchAngleJitter, branchAngleJitter),
                childTier, agent.Road, spawned);
        }
    }

    private void SpawnBranchesAlong(int roadIndex, RoadTier childTier, List<Agent> spawned)
    {
        List<Vector2> points = roads[roadIndex].Points;
        float since = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            since += Vector2.Distance(points[i - 1], points[i]);
            if (since < branchSpacing || random.NextDouble() > branchChance) continue;
            since = 0f;

            Vector2 tangent = (points[i] - points[i - 1]).normalized;
            float angle = Mathf.Atan2(tangent.x, tangent.y) * Mathf.Rad2Deg;
            foreach (float side in new[] { -90f, 90f })
            {
                if (random.NextDouble() > 0.6) continue;
                SpawnAgent(points[i], angle + side + RandomRange(-branchAngleJitter, branchAngleJitter), childTier, roadIndex, spawned);
            }
        }
    }

    private void SpawnAgent(Vector2 start, float angle, RoadTier tier, int parentRoad, List<Agent> spawned)
    {
        int maxSteps = tier == RoadTier.Lane ? laneMaxSegments : secondaryMaxSegments;
        spawned.Add(new Agent
        {
            Position = start,
            Angle = angle,
            Tier = tier,
            Road = NewRoad(tier, start),
            ParentRoad = parentRoad,
            StepsLeft = Mathf.Max(2, Mathf.RoundToInt(maxSteps * RandomRange(0.55f, 1f)))
        });
    }

    private void FinishRoad(Agent agent)
    {
        Road road = roads[agent.Road];
        if (road.Points.Count >= 2 && RoadLength(road) >= segmentLength * 1.5f) return;

        // 太短的殘段視為無效
        road.Removed = true;
    }

    private int NewRoad(RoadTier tier, Vector2 start)
    {
        Road road = new Road { Tier = tier, HalfWidth = HalfWidthOf(tier) };
        road.Points.Add(start);
        roads.Add(road);
        return roads.Count - 1;
    }

    private void AddPoint(Agent agent, Vector2 point)
    {
        Road road = roads[agent.Road];
        Vector2 previous = road.Points[road.Points.Count - 1];
        if ((point - previous).sqrMagnitude < 0.25f) return;

        road.Points.Add(point);
        AddSegment(previous, point, agent.Road, road.Points.Count - 2);
    }

    private void AddSegment(Vector2 a, Vector2 b, int roadIndex, int index)
    {
        segments.Add(new Segment { A = a, B = b, Road = roadIndex, Index = index });
        segmentGrid.Insert(segments.Count - 1, Vector2.Min(a, b), Vector2.Max(a, b));
    }

    /// <summary>以噪聲擾動半徑的封閉環狀道路</summary>
    private int AddRingRoad(float radius, float irregularity, RoadTier tier, float halfWidth, bool clipToBoundary)
    {
        Road road = new Road { Tier = tier, HalfWidth = halfWidth, Closed = true };
        roads.Add(road);
        int roadIndex = roads.Count - 1;

        Vector2 ringOffset = RandomOffset();
        int count = Mathf.Max(12, Mathf.CeilToInt(2f * Mathf.PI * radius / segmentLength));
        for (int i = 0; i <= count; i++)
        {
            float angle = (i % count) * 360f / count;
            Vector2 direction = Direction(angle);
            float noise = Mathf.PerlinNoise(direction.x * 1.6f + ringOffset.x, direction.y * 1.6f + ringOffset.y) * 2f - 1f;
            float r = radius * (1f + irregularity * noise);
            if (clipToBoundary) r = Mathf.Min(r, BoundaryRadius(angle) * 0.85f);
            road.Points.Add(direction * r);
        }

        for (int i = 1; i < road.Points.Count; i++)
        {
            AddSegment(road.Points[i - 1], road.Points[i], roadIndex, i - 1);
        }
        return roadIndex;
    }

    private bool IgnoreSegment(Agent agent, Segment segment)
    {
        if (roads[segment.Road].Removed) return true;
        if (segment.Road == agent.Road && segment.Index >= roads[agent.Road].Points.Count - 4) return true;
        if (segment.Road == agent.ParentRoad && agent.Travelled < minRoadSpacing) return true;
        return false;
    }

    private bool FindCrossing(Agent agent, Vector2 a, Vector2 b, out Vector2 crossing)
    {
        crossing = default;
        float bestT = float.MaxValue;
        foreach (int index in segmentGrid.Query(Vector2.Min(a, b), Vector2.Max(a, b)))
        {
            Segment segment = segments[index];
            if (IgnoreSegment(agent, segment)) continue;
            if (SegmentIntersection(a, b, segment.A, segment.B, out float t, out Vector2 point) && t > 0.02f && t < bestT)
            {
                bestT = t;
                crossing = point;
            }
        }
        return bestT < float.MaxValue;
    }

    private bool FindSnapNode(Agent agent, Vector2 point, out Vector2 node)
    {
        node = default;
        float best = snapDistance * snapDistance;
        Vector2 extent = Vector2.one * snapDistance;
        foreach (int index in segmentGrid.Query(point - extent, point + extent))
        {
            Segment segment = segments[index];
            if (IgnoreSegment(agent, segment)) continue;
            foreach (Vector2 candidate in new[] { segment.A, segment.B })
            {
                float distance = (candidate - point).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    node = candidate;
                }
            }
        }
        return best < snapDistance * snapDistance;
    }

    private bool FindNearbySegment(Agent agent, Vector2 point, float radius, out Segment nearest, out Vector2 closest)
    {
        nearest = default;
        closest = default;
        float best = radius;
        Vector2 extent = Vector2.one * radius;
        foreach (int index in segmentGrid.Query(point - extent, point + extent))
        {
            Segment segment = segments[index];
            if (IgnoreSegment(agent, segment)) continue;
            Vector2 candidate = ClosestPointOnSegment(point, segment.A, segment.B);
            float distance = Vector2.Distance(candidate, point);
            if (distance < best)
            {
                best = distance;
                nearest = segment;
                closest = candidate;
            }
        }
        return best < radius;
    }

    #endregion

    #region 地面、道路、廣場、公園

    private void BuildGround()
    {
        float size = cityRadius * (1f + boundaryIrregularity) * 2f + 2000f; // 夠大讓邊界隱沒在霧中
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.SetParent(root, false);
        ground.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(size, 1f, size);
        ground.GetComponent<Renderer>().sharedMaterial = Mat(groundMaterial, new Color(0.38f, 0.47f, 0.32f));
    }

    private void BuildRoadMeshes()
    {
        MeshData sidewalk = new MeshData();
        MeshData asphalt = new MeshData();
        MeshData lines = new MeshData();

        foreach (Road road in roads)
        {
            if (road.Removed || road.Points.Count < 2) continue;

            List<Vector2> points = Smooth(road.Points, roadSmoothing);
            sidewalk.AddRibbon(points, road.HalfWidth + sidewalkWidth, 0.03f);
            asphalt.AddRibbon(points, road.HalfWidth, 0.06f);
            if (road.Tier == RoadTier.Main) lines.AddRibbon(points, 0.15f, 0.08f);

            if (!road.Closed)
            {
                // 端點加圓盤，補滿路口接縫
                foreach (Vector2 end in new[] { road.Points[0], road.Points[road.Points.Count - 1] })
                {
                    sidewalk.AddDisc(end, road.HalfWidth + sidewalkWidth, 0.03f);
                    asphalt.AddDisc(end, road.HalfWidth, 0.06f);
                }
            }
        }

        CreateMeshObject("Sidewalks", sidewalk, Mat(sidewalkMaterial, new Color(0.62f, 0.61f, 0.58f)));
        CreateMeshObject("Roads", asphalt, Mat(roadMaterial, new Color(0.17f, 0.17f, 0.19f)));
        CreateMeshObject("RoadLines", lines, Mat(roadLineMaterial, new Color(0.95f, 0.8f, 0.2f)));
    }

    private void BuildPlaza()
    {
        MeshData plaza = new MeshData();
        plaza.AddDisc(Vector2.zero, plazaRadius - mainRoadWidth * 0.5f, 0.05f, 40);
        CreateMeshObject("Plaza", plaza, Mat(plazaMaterial, new Color(0.56f, 0.53f, 0.48f)));

        // 起飛平台 (H 標誌)
        Transform pad = CreateBox("TakeoffPad", new Vector3(0f, 0.25f, 0f), new Vector3(8f, 0.4f, 8f),
            Mat(null, new Color(0.25f, 0.27f, 0.3f)), true).transform;
        Material white = Mat(null, Color.white);
        CreateBox("Pad_H_Left", new Vector3(-1.5f, 0.46f, 0f), new Vector3(0.5f, 0.02f, 4f), white, false).transform.SetParent(pad, true);
        CreateBox("Pad_H_Right", new Vector3(1.5f, 0.46f, 0f), new Vector3(0.5f, 0.02f, 4f), white, false).transform.SetParent(pad, true);
        CreateBox("Pad_H_Bar", new Vector3(0f, 0.46f, 0f), new Vector3(3f, 0.02f, 0.5f), white, false).transform.SetParent(pad, true);

        // 廣場四周的樹
        int ringTrees = Mathf.RoundToInt(plazaRadius * 0.6f);
        for (int i = 0; i < ringTrees; i++)
        {
            Vector2 position = Direction(i * 360f / ringTrees) * (plazaRadius - mainRoadWidth * 0.5f - 2.5f);
            CreateTree(position, 0.8f);
        }
    }

    private void BuildParks()
    {
        if (parkAmount <= 0f) return;

        const float cell = 6f;
        MeshData grass = new MeshData();
        float extent = cityRadius * (1f + boundaryIrregularity);
        for (float x = -extent; x < extent; x += cell)
        {
            for (float z = -extent; z < extent; z += cell)
            {
                Vector2 center = new Vector2(x + cell * 0.5f, z + cell * 0.5f);
                if (!IsPark(center) || !InsideBoundary(center, 1f)) continue;
                if (RoadClearance(center) < cell * 0.75f) continue;

                grass.AddQuad(new Vector2(x, z), new Vector2(x + cell, z + cell), 0.02f);

                if (random.NextDouble() < treeDensity)
                {
                    Vector2 position = center + new Vector2(RandomRange(-2f, 2f), RandomRange(-2f, 2f));
                    if (RoadClearance(position) > 3f) CreateTree(position, RandomRange(0.8f, 1.4f));
                }
            }
        }
        CreateMeshObject("Parks", grass, Mat(parkMaterial, new Color(0.3f, 0.55f, 0.26f)));
    }

    private bool IsPark(Vector2 point)
    {
        if (parkAmount <= 0f || point.magnitude < CenterRadius + 10f) return false;
        float noise = Mathf.PerlinNoise(point.x * parkNoiseScale + parkNoiseOffset.x, point.y * parkNoiseScale + parkNoiseOffset.y);
        return noise > Mathf.Lerp(0.85f, 0.35f, parkAmount);
    }

    #endregion

    #region 建築

    private void PlaceFrontageBuildings()
    {
        // 主幹道先放，讓大型建築優先佔據路邊
        for (int tier = 0; tier <= (int)RoadTier.Lane; tier++)
        {
            foreach (Road road in roads)
            {
                if (road.Removed || (int)road.Tier != tier) continue;
                foreach (float side in new[] { -1f, 1f })
                {
                    PlaceAlongRoad(road, side);
                }
            }
        }
    }

    private void PlaceAlongRoad(Road road, float side)
    {
        float total = RoadLength(road);
        float distanceAlong = RandomRange(0f, frontageRange.x);

        while (distanceAlong < total)
        {
            float start = distanceAlong;
            // 依建築實際寬度決定它沿道路的中心位置
            System.Func<float, Anchor> anchorFor = width =>
            {
                PointOnRoad(road, start + width * 0.5f, out Vector2 onRoad, out Vector2 tangent);
                Vector2 normal = new Vector2(-tangent.y, tangent.x) * side;
                return new Anchor
                {
                    Front = onRoad + normal * (road.HalfWidth + sidewalkWidth + setback),
                    AxisX = tangent,
                    AxisZ = -normal
                };
            };

            PointOnRoad(road, start, out Vector2 here, out _);
            float sizeScale = Mathf.Lerp(0.75f, 1.25f, CenterFactor(here));
            float placedWidth = TryPlaceBuilding(anchorFor,
                RandomRange(frontageRange.x, frontageRange.y) * sizeScale,
                RandomRange(depthRange.x, depthRange.y) * sizeScale);

            distanceAlong += placedWidth > 0f ? placedWidth + buildingGap : 4f;
        }
    }

    private void PlaceInfillBuildings()
    {
        float searchRadius = infillMaxRoadDistance;
        for (int i = 0; i < infillAttempts; i++)
        {
            float angle = RandomRange(0f, 360f);
            float radius = BoundaryRadius(angle) * Mathf.Sqrt((float)random.NextDouble()) * 0.95f;
            Vector2 point = Direction(angle) * radius;

            if (!NearestRoad(point, searchRadius, out Segment segment, out Vector2 closest)) continue;

            Vector2 tangent = (segment.B - segment.A).normalized;
            Vector2 toRoad = closest - point;
            Vector2 facing = toRoad.sqrMagnitude > 0.01f ? toRoad.normalized : new Vector2(-tangent.y, tangent.x);
            // 讓建築方正地面向道路
            Vector2 axisZ = Vector2.Dot(facing, new Vector2(-tangent.y, tangent.x)) >= 0f
                ? new Vector2(-tangent.y, tangent.x)
                : new Vector2(tangent.y, -tangent.x);

            Anchor anchor = new Anchor { Front = point, AxisX = tangent, AxisZ = axisZ };
            float sizeScale = Mathf.Lerp(0.7f, 1.15f, CenterFactor(point));
            TryPlaceBuilding(_ => anchor,
                RandomRange(frontageRange.x, frontageRange.y) * sizeScale,
                RandomRange(depthRange.x, depthRange.y) * sizeScale);
        }
    }

    /// <summary>建築正面 (面向道路那一側) 的中點與方向</summary>
    private struct Anchor
    {
        public Vector2 Front;
        public Vector2 AxisX;
        public Vector2 AxisZ;
    }

    /// <summary>嘗試放置一棟建築；回傳沿道路方向佔用的寬度 (0 = 沒放)</summary>
    /// <param name="anchorFor">依建築寬度取得放置基準點</param>
    private float TryPlaceBuilding(System.Func<float, Anchor> anchorFor, float width, float depth)
    {
        if (random.NextDouble() > buildingDensity) return 0f;

        Anchor probe = anchorFor(width);
        if (!InsideBoundary(probe.Front, 0.97f) || IsPark(probe.Front)) return 0f;

        float height = BuildingHeight(probe.Front);

        if (modelInfos.Count > 0 && random.NextDouble() < modelRatio)
        {
            float modelWidth = TryPlaceModel(anchorFor, height);
            if (modelWidth > 0f) return modelWidth;
            if (!boxFallback)
            {
                rejectedBuildings++;
                return 0f;
            }
        }

        // 程序化方塊大樓：放不下時縮小地塊再試，讓狹窄街廓也能排滿
        const float minimumSize = 5f;
        for (int attempt = 0; attempt < 4 && width >= minimumSize && depth >= minimumSize; attempt++)
        {
            if (TryReserve(anchorFor(width), width, depth, out Footprint footprint))
            {
                CreateBoxBuilding(footprint, height);
                buildingCount++;
                boxBuildingCount++;
                return width;
            }

            width *= 0.78f;
            depth *= 0.72f;
        }

        rejectedBuildings++;
        return 0f;
    }

    /// <summary>挑選高度接近、放得下的素材包模型；以接近原始比例放置</summary>
    private float TryPlaceModel(System.Func<float, Anchor> anchorFor, float height)
    {
        // 依「原始高度與目標高度的接近程度 + 隨機」排序候選
        candidateBuffer.Clear();
        foreach (ModelInfo info in modelInfos)
        {
            float score = Mathf.Abs(Mathf.Log(info.Size.y / Mathf.Max(1f, height))) + RandomRange(0f, 0.8f);
            candidateBuffer.Add(new KeyValuePair<float, ModelInfo>(score, info));
        }
        candidateBuffer.Sort((a, b) => a.Key.CompareTo(b.Key));

        float[] scales = { maxModelScale, 1f, 0.85f, 0.72f, minModelScale };
        int tries = Mathf.Min(6, candidateBuffer.Count);
        for (int i = 0; i < tries; i++)
        {
            ModelInfo info = candidateBuffer[i].Value;
            float along = ModelAlong(info);
            float deep = ModelDepth(info);

            foreach (float scale in scales)
            {
                if (scale > maxModelScale || scale < minModelScale) continue;
                float width = along * scale;
                if (TryReserve(anchorFor(width), width, deep * scale, out Footprint footprint))
                {
                    CreateModelBuilding(info, footprint, scale, height);
                    buildingCount++;
                    return width;
                }
            }
        }
        return 0f;
    }

    /// <summary>檢查地塊並登記佔用</summary>
    private bool TryReserve(Anchor anchor, float width, float depth, out Footprint footprint)
    {
        Vector2 center = anchor.Front - anchor.AxisZ * depth * 0.5f;
        footprint = new Footprint
        {
            Center = center, AxisX = anchor.AxisX, AxisZ = anchor.AxisZ, HalfX = width * 0.5f, HalfZ = depth * 0.5f
        };

        if (centralPlaza && center.magnitude < plazaRadius + mainRoadWidth * 0.5f + sidewalkWidth + Mathf.Max(width, depth) * 0.5f) return false;
        if (!InsideBoundary(center, 0.97f) || IsPark(center)) return false;
        if (!ClearOfRoads(footprint) || OverlapsBuildings(footprint)) return false;

        footprints.Add(footprint);
        GetFootprintBounds(footprint, out Vector2 min, out Vector2 max);
        footprintGrid.Insert(footprints.Count - 1, min, max);
        return true;
    }

    private float BuildingHeight(Vector2 point)
    {
        float centerFactor = Mathf.Pow(CenterFactor(point), heightFalloff);
        float noise = Mathf.PerlinNoise(point.x * heightNoiseScale + heightNoiseOffset.x, point.y * heightNoiseScale + heightNoiseOffset.y);
        float t = centerFactor * Mathf.Lerp(1f, noise * 1.6f, heightNoise);
        float height = Mathf.Lerp(minHeight, maxHeight, Mathf.Clamp01(t)) * RandomRange(0.75f, 1.15f);
        return Mathf.Clamp(height, minHeight * 0.75f, maxHeight);
    }

    /// <summary>1 = 市中心，0 = 市郊</summary>
    private float CenterFactor(Vector2 point)
    {
        float normalized = point.magnitude / Mathf.Max(1f, cityRadius);
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(downtownRadius * 0.4f, Mathf.Max(downtownRadius, 0.1f) * 2.2f, normalized));
    }

    #endregion

    #region 素材包模型

    private class ModelInfo
    {
        public GameObject Prefab;
        public Vector3 Size;
        public Vector3 Pivot; // 底部中心 (含模型根節點的旋轉)
        public Material Override;
        public float FrontYaw; // 模型正面 (門口) 的方向，0 = +Z，為 90 的倍數
    }

    /// <summary>讓模型正面朝向道路所需的額外旋轉</summary>
    private float ModelYaw(ModelInfo info) => modelYawOffset - info.FrontYaw;

    private bool IsSideways(ModelInfo info)
    {
        float yaw = ModelYaw(info);
        return Mathf.Abs(Mathf.DeltaAngle(yaw, 90f)) < 45f || Mathf.Abs(Mathf.DeltaAngle(yaw, 270f)) < 45f;
    }

    private float ModelAlong(ModelInfo info) => IsSideways(info) ? info.Size.z : info.Size.x;

    private float ModelDepth(ModelInfo info) => IsSideways(info) ? info.Size.x : info.Size.z;

    /// <summary>
    /// 以「門」子網格的法線平均方向判斷模型正面 (沒有門時改用招牌貼圖面)，取最接近的 90 度
    /// </summary>
    private static float DetectFrontYaw(GameObject probe)
    {
        foreach (string keyword in new[] { "door", "texture", "sign" })
        {
            Vector3 sum = Vector3.zero;
            foreach (MeshFilter filter in probe.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                Renderer renderer = filter.GetComponent<Renderer>();
                if (mesh == null || renderer == null || (!mesh.isReadable && !Application.isEditor)) continue;

                Vector3[] normals = mesh.normals;
                if (normals.Length == 0) continue;
                Material[] materials = renderer.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount && sub < materials.Length; sub++)
                {
                    if (materials[sub] == null || !materials[sub].name.ToLowerInvariant().Contains(keyword)) continue;
                    foreach (int index in mesh.GetTriangles(sub))
                    {
                        sum += filter.transform.TransformDirection(normals[index]);
                    }
                }
            }

            sum.y = 0f;
            if (sum.sqrMagnitude > 1e-4f)
            {
                return Mathf.Round(Mathf.Atan2(sum.x, sum.z) * Mathf.Rad2Deg / 90f) * 90f;
            }
        }
        return 0f;
    }

    /// <summary>量測每個模型的尺寸與底部中心；保留 FBX 根節點的旋轉 (此素材包為 X 270°)</summary>
    private void PrepareModels()
    {
        modelInfos.Clear();
        if (buildingModels == null) return;

        foreach (GameObject prefab in buildingModels)
        {
            if (prefab == null) continue;

            GameObject probe = Instantiate(prefab);
            probe.transform.position = Vector3.zero;
            Bounds bounds = WorldBounds(probe);
            float frontYaw = DetectFrontYaw(probe);
            DestroyImmediate(probe);
            if (bounds.size.x < 0.5f || bounds.size.y < 0.5f || bounds.size.z < 0.5f) continue;

            Material textured = null;
            if (texturedModels != null)
            {
                foreach (ModelMaterialOverride entry in texturedModels)
                {
                    if (entry.Model == prefab) textured = entry.Material;
                }
            }

            modelInfos.Add(new ModelInfo
            {
                Prefab = prefab,
                Size = bounds.size,
                Pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z),
                Override = textured,
                FrontYaw = frontYaw
            });
        }
    }

    private void CreateModelBuilding(ModelInfo info, Footprint footprint, float scale, float height)
    {
        Vector3 forward = new Vector3(footprint.AxisZ.x, 0f, footprint.AxisZ.y);
        float verticalStretch = Mathf.Clamp(height / (info.Size.y * scale), 1f / maxVerticalStretch, maxVerticalStretch);

        // 外層只負責位置、朝向與縮放；模型保留自身根節點旋轉，垂直拉伸才不會變形成斜的
        GameObject building = new GameObject(info.Prefab.name);
        building.transform.SetParent(root, false);
        building.transform.localPosition = new Vector3(footprint.Center.x, 0f, footprint.Center.y);
        building.transform.localRotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, ModelYaw(info), 0f);
        building.transform.localScale = new Vector3(scale, scale * verticalStretch, scale);

        GameObject model = Instantiate(info.Prefab, building.transform);
        model.name = info.Prefab.name;
        model.transform.localPosition = -info.Pivot;

        ApplyModelMaterials(model, info);
        EnsureColliders(model);
    }

    /// <summary>
    /// 素材包 FBX 匯入後材質皆為預設灰色；依子網格材質名稱改用素材包提供的配色 (只改實例，不改素材包資產)
    /// </summary>
    private void ApplyModelMaterials(GameObject model, ModelInfo info)
    {
        Material facade = PickMaterial(facadeMaterials, new Color(0.8f, 0.78f, 0.72f));
        Material roof = PickMaterial(roofMaterials, new Color(0.45f, 0.45f, 0.47f));
        Material window = PickMaterial(windowMaterials, new Color(0.65f, 0.75f, 0.82f));

        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;

                // 素材包只有名為 Texture 的子網格 (招牌、看板) 使用模型專屬貼圖，其他面為純色
                if (info.Override != null && materials[i].name.StartsWith("Texture", System.StringComparison.OrdinalIgnoreCase))
                {
                    materials[i] = info.Override;
                    continue;
                }

                switch (ClassifyMaterial(materials[i].name))
                {
                    case MaterialSlot.Grass: materials[i] = Mat(parkMaterial, new Color(0.3f, 0.55f, 0.26f)); break;
                    case MaterialSlot.Pavement: materials[i] = Mat(sidewalkMaterial, new Color(0.62f, 0.61f, 0.58f)); break;
                    case MaterialSlot.Window: materials[i] = window; break;
                    case MaterialSlot.Roof: materials[i] = roof; break;
                    case MaterialSlot.Door: materials[i] = Mat(doorMaterial, new Color(0.3f, 0.26f, 0.2f)); break;
                    case MaterialSlot.Trim: materials[i] = Mat(trimMaterial, new Color(0.25f, 0.25f, 0.25f)); break;
                    case MaterialSlot.Accent: materials[i] = Mat(accentMaterial, new Color(0.7f, 0.3f, 0.3f)); break;
                    default: materials[i] = facade; break;
                }
            }
            renderer.sharedMaterials = materials;
        }
    }

    private enum MaterialSlot { Facade, Window, Roof, Door, Trim, Accent, Grass, Pavement }

    private static MaterialSlot ClassifyMaterial(string materialName)
    {
        string name = materialName.ToLowerInvariant();
        if (name.Contains("grass")) return MaterialSlot.Grass;
        if (name.Contains("street") || name.Contains("sideway") || name.Contains("sidewalk")) return MaterialSlot.Pavement;
        if (name.Contains("frame") || name.Contains("board") || name.Contains("metal") || name.Contains("pole") ||
            name.Contains("rail") || name.Contains("fire") || name.Contains("stair") || name.Contains("grid") ||
            name.Contains("holder") || name.Contains("lines") || name.Contains("filter") || name.Contains("black") ||
            name.Contains("platt") || name.Contains("box") || name.Contains("goals") || name.Contains("sign"))
        {
            return MaterialSlot.Trim;
        }
        if (name.Contains("window")) return MaterialSlot.Window;
        if (name.Contains("roof") || name.Contains("floor")) return MaterialSlot.Roof;
        if (name.Contains("door") || name.Contains("wood") || name.Contains("garage") || name.Contains("drive")) return MaterialSlot.Door;
        if (name.Contains("red") || name.Contains("blue") || name.Contains("green") || name.Contains("brown")) return MaterialSlot.Accent;
        return MaterialSlot.Facade;
    }

    #endregion

    #region 程序化方塊大樓

    private void CreateBoxBuilding(Footprint footprint, float height)
    {
        Vector3 position = new Vector3(footprint.Center.x, 0f, footprint.Center.y);
        Quaternion rotation = Quaternion.LookRotation(new Vector3(footprint.AxisZ.x, 0f, footprint.AxisZ.y), Vector3.up);
        float width = footprint.HalfX * 2f;
        float depth = footprint.HalfZ * 2f;
        Material material = PickMaterial(buildingMaterials, new Color(0.7f, 0.68f, 0.64f));
        GameObject building = new GameObject("Building");
        building.transform.SetParent(root, false);
        building.transform.localPosition = position;
        building.transform.localRotation = rotation;

        // 高樓分段退縮，避免整排都是方柱
        int tiers = height > 30f && random.NextDouble() < 0.6 ? (height > 55f ? 3 : 2) : 1;
        float baseY = 0f;
        float tierWidth = width;
        float tierDepth = depth;
        for (int i = 0; i < tiers; i++)
        {
            float tierHeight = i == tiers - 1 ? height - baseY : height * RandomRange(0.45f, 0.65f) / (i + 1);
            tierHeight = Mathf.Max(3f, tierHeight);
            GameObject part = CreateBox(i == 0 ? "Body" : "Tier" + i, Vector3.zero, new Vector3(tierWidth, tierHeight, tierDepth), material, true);
            part.transform.SetParent(building.transform, false);
            part.transform.localPosition = new Vector3(0f, baseY + tierHeight * 0.5f, 0f);
            baseY += tierHeight;
            tierWidth *= RandomRange(0.6f, 0.85f);
            tierDepth *= RandomRange(0.6f, 0.85f);
        }

        // 屋頂設備
        if (random.NextDouble() < 0.7)
        {
            Vector3 size = new Vector3(tierWidth * RandomRange(0.25f, 0.5f), RandomRange(1f, 2.5f), tierDepth * RandomRange(0.25f, 0.5f));
            GameObject unit = CreateBox("RoofUnit", Vector3.zero, size, Mat(roofMaterial, new Color(0.4f, 0.4f, 0.42f)), true);
            unit.transform.SetParent(building.transform, false);
            unit.transform.localPosition = new Vector3(RandomRange(-0.2f, 0.2f) * tierWidth, baseY + size.y * 0.5f, RandomRange(-0.2f, 0.2f) * tierDepth);
        }
    }

    private bool ClearOfRoads(Footprint footprint)
    {
        GetFootprintBounds(footprint, out Vector2 min, out Vector2 max);
        float margin = mainRoadWidth * 0.5f + sidewalkWidth;
        Vector2 pad = Vector2.one * margin;
        foreach (int index in segmentGrid.Query(min - pad, max + pad))
        {
            Segment segment = segments[index];
            Road road = roads[segment.Road];
            if (road.Removed) continue;
            if (SegmentFootprintDistance(segment.A, segment.B, footprint) < road.HalfWidth + sidewalkWidth + 0.3f) return false;
        }
        return true;
    }

    private bool OverlapsBuildings(Footprint footprint)
    {
        GetFootprintBounds(footprint, out Vector2 min, out Vector2 max);
        Vector2 pad = Vector2.one * buildingGap;
        foreach (int index in footprintGrid.Query(min - pad, max + pad))
        {
            if (FootprintsOverlap(footprint, footprints[index], buildingGap * 0.5f)) return true;
        }
        return false;
    }

    #endregion

    #region 樹木

    private void PlaceOutskirtTrees()
    {
        for (int i = 0; i < outskirtTrees; i++)
        {
            float angle = RandomRange(0f, 360f);
            float radius = BoundaryRadius(angle) * RandomRange(0.95f, 1.6f);
            Vector2 position = Direction(angle) * radius;
            if (RoadClearance(position) < 4f) continue;
            if (InsideBoundary(position, 1f) && OverlapsBuildings(PointFootprint(position, 2f))) continue;
            CreateTree(position, RandomRange(0.9f, 1.6f));
        }
    }

    private void CreateTree(Vector2 position, float scale)
    {
        treeCount++;
        if (TryCreateTreeModel(position, scale)) return;

        GameObject tree = new GameObject("Tree");
        tree.transform.SetParent(root, false);
        tree.transform.localPosition = new Vector3(position.x, 0f, position.y);
        tree.transform.localRotation = Quaternion.Euler(0f, RandomRange(0f, 360f), 0f);

        float trunkHeight = 2.2f * scale;
        GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trunk.name = "Trunk";
        trunk.transform.SetParent(tree.transform, false);
        trunk.transform.localPosition = new Vector3(0f, trunkHeight * 0.5f, 0f);
        trunk.transform.localScale = new Vector3(0.35f * scale, trunkHeight * 0.5f, 0.35f * scale);
        trunk.GetComponent<Renderer>().sharedMaterial = Mat(treeTrunkMaterial, new Color(0.4f, 0.28f, 0.18f));

        float canopy = RandomRange(2.6f, 3.8f) * scale;
        GameObject leaves = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        leaves.name = "Canopy";
        leaves.transform.SetParent(tree.transform, false);
        leaves.transform.localPosition = new Vector3(0f, trunkHeight + canopy * 0.35f, 0f);
        leaves.transform.localScale = new Vector3(canopy, canopy * RandomRange(0.85f, 1.2f), canopy);
        leaves.GetComponent<Renderer>().sharedMaterial = PickMaterial(treeLeafMaterials, new Color(0.25f, 0.5f, 0.22f));
    }

    #endregion

    #region 幾何工具

    private float BoundaryRadius(float angle)
    {
        Vector2 direction = Direction(angle);
        float noise = Mathf.PerlinNoise(direction.x * boundaryFrequency + boundaryNoiseOffset.x,
                                        direction.y * boundaryFrequency + boundaryNoiseOffset.y) * 2f - 1f;
        return cityRadius * (1f + boundaryIrregularity * noise);
    }

    private bool InsideBoundary(Vector2 point, float scale)
    {
        float angle = Mathf.Atan2(point.x, point.y) * Mathf.Rad2Deg;
        return point.magnitude <= BoundaryRadius(angle) * scale;
    }

    private float RoadClearance(Vector2 point)
    {
        float best = float.MaxValue;
        Vector2 extent = Vector2.one * (mainRoadWidth + sidewalkWidth * 2f + 10f);
        foreach (int index in segmentGrid.Query(point - extent, point + extent))
        {
            Segment segment = segments[index];
            Road road = roads[segment.Road];
            if (road.Removed) continue;
            float distance = Vector2.Distance(point, ClosestPointOnSegment(point, segment.A, segment.B)) - road.HalfWidth - sidewalkWidth;
            best = Mathf.Min(best, distance);
        }
        return best;
    }

    private bool NearestRoad(Vector2 point, float radius, out Segment nearest, out Vector2 closest)
    {
        nearest = default;
        closest = default;
        float best = radius;
        Vector2 extent = Vector2.one * radius;
        foreach (int index in segmentGrid.Query(point - extent, point + extent))
        {
            Segment segment = segments[index];
            if (roads[segment.Road].Removed) continue;
            Vector2 candidate = ClosestPointOnSegment(point, segment.A, segment.B);
            float distance = Vector2.Distance(point, candidate);
            if (distance < best)
            {
                best = distance;
                nearest = segment;
                closest = candidate;
            }
        }
        return best < radius;
    }

    /// <summary>道路上弧長 distance 處的位置與切線</summary>
    private static void PointOnRoad(Road road, float distance, out Vector2 point, out Vector2 tangent)
    {
        List<Vector2> points = road.Points;
        float travelled = 0f;
        for (int i = 0; i < points.Count - 1; i++)
        {
            float length = Vector2.Distance(points[i], points[i + 1]);
            if (travelled + length >= distance || i == points.Count - 2)
            {
                tangent = length > 1e-4f ? (points[i + 1] - points[i]) / length : Vector2.up;
                point = points[i] + tangent * Mathf.Clamp(distance - travelled, 0f, length);
                return;
            }
            travelled += length;
        }
        point = points[0];
        tangent = Vector2.up;
    }

    /// <summary>Chaikin 切角平滑，保留端點位置 (路口仍能接上)</summary>
    private static List<Vector2> Smooth(List<Vector2> points, int iterations)
    {
        List<Vector2> current = points;
        for (int iteration = 0; iteration < iterations && current.Count > 2; iteration++)
        {
            List<Vector2> next = new List<Vector2>(current.Count * 2) { current[0] };
            for (int i = 0; i < current.Count - 1; i++)
            {
                Vector2 a = current[i];
                Vector2 b = current[i + 1];
                if (i > 0) next.Add(Vector2.Lerp(a, b, 0.25f));
                if (i < current.Count - 2) next.Add(Vector2.Lerp(a, b, 0.75f));
            }
            next.Add(current[current.Count - 1]);
            current = next;
        }
        return current;
    }

    private static Vector2 Direction(float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
    }

    private static float RoadLength(Road road)
    {
        float length = 0f;
        for (int i = 1; i < road.Points.Count; i++) length += Vector2.Distance(road.Points[i - 1], road.Points[i]);
        return length;
    }

    private float HalfWidthOf(RoadTier tier)
    {
        switch (tier)
        {
            case RoadTier.Main: return mainRoadWidth * 0.5f;
            case RoadTier.Secondary: return secondaryRoadWidth * 0.5f;
            default: return laneWidth * 0.5f;
        }
    }

    private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.sqrMagnitude;
        if (lengthSquared < 1e-6f) return a;
        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
        return a + ab * t;
    }

    private static bool SegmentIntersection(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2, out float t, out Vector2 point)
    {
        t = 0f;
        point = default;
        Vector2 r = p2 - p1;
        Vector2 s = q2 - q1;
        float denominator = r.x * s.y - r.y * s.x;
        if (Mathf.Abs(denominator) < 1e-6f) return false;

        Vector2 qp = q1 - p1;
        t = (qp.x * s.y - qp.y * s.x) / denominator;
        float u = (qp.x * r.y - qp.y * r.x) / denominator;
        if (t < 0f || t > 1f || u < 0f || u > 1f) return false;

        point = p1 + r * t;
        return true;
    }

    private static Vector2 ToLocal(Footprint footprint, Vector2 point)
    {
        Vector2 offset = point - footprint.Center;
        return new Vector2(Vector2.Dot(offset, footprint.AxisX), Vector2.Dot(offset, footprint.AxisZ));
    }

    /// <summary>線段到旋轉矩形的最短距離 (相交為 0)</summary>
    private static float SegmentFootprintDistance(Vector2 a, Vector2 b, Footprint footprint)
    {
        Vector2 localA = ToLocal(footprint, a);
        Vector2 localB = ToLocal(footprint, b);
        float hx = footprint.HalfX;
        float hz = footprint.HalfZ;

        if (SegmentIntersectsBox(localA, localB, hx, hz)) return 0f;

        float best = Mathf.Min(PointBoxDistance(localA, hx, hz), PointBoxDistance(localB, hx, hz));
        Vector2[] corners = { new Vector2(-hx, -hz), new Vector2(hx, -hz), new Vector2(hx, hz), new Vector2(-hx, hz) };
        foreach (Vector2 corner in corners)
        {
            best = Mathf.Min(best, Vector2.Distance(corner, ClosestPointOnSegment(corner, localA, localB)));
        }
        return best;
    }

    private static float PointBoxDistance(Vector2 point, float hx, float hz)
    {
        float dx = Mathf.Max(Mathf.Abs(point.x) - hx, 0f);
        float dz = Mathf.Max(Mathf.Abs(point.y) - hz, 0f);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Liang–Barsky 線段裁切，判斷線段是否進入軸對齊矩形</summary>
    private static bool SegmentIntersectsBox(Vector2 a, Vector2 b, float hx, float hz)
    {
        float t0 = 0f, t1 = 1f;
        Vector2 d = b - a;
        float[] p = { -d.x, d.x, -d.y, d.y };
        float[] q = { a.x + hx, hx - a.x, a.y + hz, hz - a.y };
        for (int i = 0; i < 4; i++)
        {
            if (Mathf.Abs(p[i]) < 1e-6f)
            {
                if (q[i] < 0f) return false;
                continue;
            }
            float r = q[i] / p[i];
            if (p[i] < 0f) t0 = Mathf.Max(t0, r);
            else t1 = Mathf.Min(t1, r);
            if (t0 > t1) return false;
        }
        return true;
    }

    /// <summary>分離軸測試</summary>
    private static bool FootprintsOverlap(Footprint a, Footprint b, float margin)
    {
        Vector2 delta = b.Center - a.Center;
        foreach (Vector2 axis in new[] { a.AxisX, a.AxisZ, b.AxisX, b.AxisZ })
        {
            float radiusA = a.HalfX * Mathf.Abs(Vector2.Dot(a.AxisX, axis)) + a.HalfZ * Mathf.Abs(Vector2.Dot(a.AxisZ, axis));
            float radiusB = b.HalfX * Mathf.Abs(Vector2.Dot(b.AxisX, axis)) + b.HalfZ * Mathf.Abs(Vector2.Dot(b.AxisZ, axis));
            if (Mathf.Abs(Vector2.Dot(delta, axis)) > radiusA + radiusB + margin) return false;
        }
        return true;
    }

    private static void GetFootprintBounds(Footprint footprint, out Vector2 min, out Vector2 max)
    {
        Vector2 extent = new Vector2(
            footprint.HalfX * Mathf.Abs(footprint.AxisX.x) + footprint.HalfZ * Mathf.Abs(footprint.AxisZ.x),
            footprint.HalfX * Mathf.Abs(footprint.AxisX.y) + footprint.HalfZ * Mathf.Abs(footprint.AxisZ.y));
        min = footprint.Center - extent;
        max = footprint.Center + extent;
    }

    private static Footprint PointFootprint(Vector2 position, float halfSize)
    {
        return new Footprint { Center = position, AxisX = Vector2.right, AxisZ = Vector2.up, HalfX = halfSize, HalfZ = halfSize };
    }

    #endregion

    #region 物件與材質工具

    private float RandomRange(float min, float max) => min + (float)random.NextDouble() * (max - min);

    private Vector2 RandomOffset() => new Vector2(RandomRange(0f, 1000f), RandomRange(0f, 1000f));

    private Material PickMaterial(Material[] materials, Color fallback)
    {
        if (materials != null && materials.Length > 0)
        {
            Material material = materials[random.Next(materials.Length)];
            if (material != null) return material;
        }
        return RuntimeMaterials.Opaque(fallback);
    }

    private static Material Mat(Material material, Color fallback) => material != null ? material : RuntimeMaterials.Opaque(fallback);

    private GameObject CreateBox(string boxName, Vector3 localPosition, Vector3 size, Material material, bool withCollider)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = boxName;
        box.transform.SetParent(root, false);
        box.transform.localPosition = localPosition;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().sharedMaterial = material;
        if (!withCollider) DestroyObject(box.GetComponent<Collider>());
        return box;
    }

    private void CreateMeshObject(string objectName, MeshData data, Material material)
    {
        if (data.IsEmpty) return;

        GameObject meshObject = new GameObject(objectName, typeof(MeshFilter), typeof(MeshRenderer));
        meshObject.transform.SetParent(root, false);
        meshObject.GetComponent<MeshFilter>().sharedMesh = data.ToMesh(objectName);
        meshObject.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static void DestroyObject(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    /// <summary>以網格包圍盒建立 BoxCollider (素材包網格面數高，MeshCollider 烘焙太慢)</summary>
    private static void EnsureColliders(GameObject target)
    {
        if (target.GetComponentInChildren<Collider>() != null) return;

        foreach (MeshFilter filter in target.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            Bounds bounds = filter.sharedMesh.bounds;
            BoxCollider box = filter.gameObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
        }
    }

    private static Bounds WorldBounds(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(target.transform.position, Vector3.zero);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    #endregion

    #region 內部資料結構

    /// <summary>以格子索引加速範圍查詢</summary>
    private class SpatialGrid
    {
        private readonly float cellSize;
        private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
        private readonly HashSet<int> results = new HashSet<int>();

        public SpatialGrid(float cellSize)
        {
            this.cellSize = cellSize;
        }

        public void Clear() => cells.Clear();

        public void Insert(int item, Vector2 min, Vector2 max)
        {
            for (int x = Cell(min.x); x <= Cell(max.x); x++)
            {
                for (int y = Cell(min.y); y <= Cell(max.y); y++)
                {
                    long key = Key(x, y);
                    if (!cells.TryGetValue(key, out List<int> list))
                    {
                        list = new List<int>();
                        cells[key] = list;
                    }
                    list.Add(item);
                }
            }
        }

        /// <summary>回傳的集合會在下次查詢時重用，請勿保存</summary>
        public HashSet<int> Query(Vector2 min, Vector2 max)
        {
            results.Clear();
            for (int x = Cell(min.x); x <= Cell(max.x); x++)
            {
                for (int y = Cell(min.y); y <= Cell(max.y); y++)
                {
                    if (cells.TryGetValue(Key(x, y), out List<int> list)) results.UnionWith(list);
                }
            }
            return results;
        }

        private int Cell(float value) => Mathf.FloorToInt(value / cellSize);

        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
    }

    /// <summary>累積平面網格 (道路帶狀、圓盤、方格)，最後合併成單一 Mesh</summary>
    private class MeshData
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();

        public bool IsEmpty => vertices.Count == 0;

        public void AddRibbon(List<Vector2> points, float halfWidth, float y)
        {
            int count = points.Count;
            if (count < 2) return;

            int start = vertices.Count;
            float distance = 0f;
            bool closed = count > 3 && (points[0] - points[count - 1]).sqrMagnitude < 0.01f;
            for (int i = 0; i < count; i++)
            {
                Vector2 previous = i > 0 ? (points[i] - points[i - 1]).normalized
                    : closed ? (points[0] - points[count - 2]).normalized : (points[1] - points[0]).normalized;
                Vector2 next = i < count - 1 ? (points[i + 1] - points[i]).normalized
                    : closed ? (points[1] - points[0]).normalized : previous;
                Vector2 tangent = previous + next;
                tangent = tangent.sqrMagnitude < 1e-6f ? next : tangent.normalized;
                Vector2 normal = new Vector2(-tangent.y, tangent.x);
                float miter = 1f / Mathf.Max(0.4f, Vector2.Dot(normal, new Vector2(-next.y, next.x)));
                Vector2 offset = normal * halfWidth * Mathf.Min(miter, 2.5f);

                if (i > 0) distance += Vector2.Distance(points[i - 1], points[i]);
                Vector2 point = points[i];
                vertices.Add(new Vector3(point.x + offset.x, y, point.y + offset.y));
                vertices.Add(new Vector3(point.x - offset.x, y, point.y - offset.y));
                float v = distance / Mathf.Max(0.1f, halfWidth * 2f);
                uvs.Add(new Vector2(0f, v));
                uvs.Add(new Vector2(1f, v));
            }

            for (int i = 0; i < count - 1; i++)
            {
                int left = start + i * 2;
                triangles.Add(left);
                triangles.Add(left + 2);
                triangles.Add(left + 1);
                triangles.Add(left + 1);
                triangles.Add(left + 2);
                triangles.Add(left + 3);
            }
        }

        public void AddDisc(Vector2 center, float radius, float y, int sides = 18)
        {
            int start = vertices.Count;
            vertices.Add(new Vector3(center.x, y, center.y));
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vertices.Add(new Vector3(center.x + direction.x * radius, y, center.y + direction.y * radius));
                uvs.Add(direction * 0.5f + Vector2.one * 0.5f);
            }
            for (int i = 0; i < sides; i++)
            {
                triangles.Add(start);
                triangles.Add(start + 1 + (i + 1) % sides);
                triangles.Add(start + 1 + i);
            }
        }

        public void AddQuad(Vector2 min, Vector2 max, float y)
        {
            int start = vertices.Count;
            vertices.Add(new Vector3(min.x, y, min.y));
            vertices.Add(new Vector3(min.x, y, max.y));
            vertices.Add(new Vector3(max.x, y, max.y));
            vertices.Add(new Vector3(max.x, y, min.y));
            uvs.Add(new Vector2(min.x, min.y) * 0.1f);
            uvs.Add(new Vector2(min.x, max.y) * 0.1f);
            uvs.Add(new Vector2(max.x, max.y) * 0.1f);
            uvs.Add(new Vector2(max.x, min.y) * 0.1f);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        public Mesh ToMesh(string meshName)
        {
            Mesh mesh = new Mesh { name = meshName, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            Vector3[] normals = new Vector3[vertices.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    #endregion
}

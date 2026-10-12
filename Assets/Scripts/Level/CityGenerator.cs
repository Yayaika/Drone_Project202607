using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 程序化城市生成器：棋盤式街道 + 街區地塊，地塊放入城市素材包的建築 (自動縮放、加碰撞體)，
/// 中央保留起飛廣場。依遊戲模式自動建立競速檢查圈路線或搬運任務管理器。
/// 參數可由 <see cref="LevelCatalog"/> 的關卡設定覆寫 (種子、街區數、密度、最高樓高)。
/// </summary>
[DefaultExecutionOrder(-200)] // 需早於 SpawnCtrl.Awake，先把出生點移到廣場
public class CityGenerator : MonoBehaviour, IDeliverySpotProvider
{
    private const string RootName = "GeneratedCity";

    [Header("生成時機")]
    [SerializeField] private bool generateOnAwake = true;
    [Tooltip("使用關卡目錄的參數 (種子/規模/密度/高度) 覆寫下方設定")]
    [SerializeField] private bool useLevelCatalog = true;

    [Header("城市規模")]
    [SerializeField] private int seed = 1207;
    [Range(2, 10)]
    [SerializeField] private int blocks = 5;
    [SerializeField] private float blockSize = 44f;
    [SerializeField] private float roadWidth = 12f;
    [SerializeField] private float sidewalkHeight = 0.2f;
    [Range(0f, 1f)]
    [SerializeField] private float buildingDensity = 0.85f;
    [SerializeField] private float minBuildingHeight = 8f;
    [SerializeField] private float maxBuildingHeight = 40f;
    [Tooltip("建築模型垂直拉伸上限，避免窗戶過度變形")]
    [SerializeField] private float maxVerticalStretch = 2f;

    [Header("素材 (由「無人機遊戲/建立程序化城市關卡」自動填入)")]
    [SerializeField] private GameObject[] buildingPrefabs;
    [SerializeField] private GameObject[] smallBuildingPrefabs;
    [SerializeField] private GameObject[] propPrefabs;
    [SerializeField] private Material roadMaterial;
    [SerializeField] private Material sidewalkMaterial;
    [SerializeField] private Material groundMaterial;

    [Header("玩法")]
    [SerializeField] private bool buildRaceCourseInRaceMode = true;
    [SerializeField] private bool addDeliveryManagerInDeliveryMode = true;
    [SerializeField] private GameObject cargoPrefab;
    [SerializeField] private int defaultCheckpointCount = 8;
    [SerializeField] private float checkpointRadius = 4.5f;

    private readonly List<Vector3> rooftopSpots = new List<Vector3>();
    private readonly List<Vector3> streetSpots = new List<Vector3>();
    private System.Random random;
    private Transform root;
    private Vector3 plazaCenter;
    private int plazaBlock;

    public IReadOnlyList<Vector3> RooftopSpots => rooftopSpots;
    public IReadOnlyList<Vector3> StreetSpots => streetSpots;
    public Vector3 PlazaCenter => plazaCenter;

    private float Pitch => blockSize + roadWidth;
    private float HalfSpan => (blocks * blockSize + (blocks + 1) * roadWidth) * 0.5f;

    private void Awake()
    {
        if (generateOnAwake)
        {
            Generate();
            MoveSpawnerToPlaza();
        }
    }

    private void Start()
    {
        if (!generateOnAwake) return;

        if (LevelCatalog.SelectedMode == GameModeType.Delivery)
        {
            if (addDeliveryManagerInDeliveryMode && FindFirstObjectByType<DeliveryMissionManager>() == null)
            {
                DeliveryMissionManager manager = new GameObject("DeliveryMissionManager").AddComponent<DeliveryMissionManager>();
                LevelDefinition level = LevelCatalog.CurrentLevel;
                manager.Configure(level?.DeliveryOrders ?? 3, level?.DeliveryTimeLimit ?? 300f, cargoPrefab);
            }
        }
        else if (buildRaceCourseInRaceMode)
        {
            BuildRaceCourse();
        }
    }

    [ContextMenu("生成城市 (預覽)")]
    public void Generate()
    {
        ApplyLevelSettings();
        Clear();

        random = new System.Random(seed);
        rooftopSpots.Clear();
        streetSpots.Clear();
        plazaBlock = blocks / 2;

        root = new GameObject(RootName).transform;
        root.SetParent(transform, false);

        BuildGround();
        BuildRoads();
        for (int bx = 0; bx < blocks; bx++)
        {
            for (int bz = 0; bz < blocks; bz++)
            {
                BuildBlock(bx, bz);
            }
        }

        Physics.SyncTransforms();
        CollectRooftops();
        // 註：不在執行時呼叫 StaticBatchingUtility.Combine，FBX 網格預設未開啟 Read/Write 會失敗；URP 的 SRP Batcher 已足夠
    }

    [ContextMenu("清除城市")]
    public void Clear()
    {
        Transform existing = transform.Find(RootName);
        while (existing != null)
        {
            existing.SetParent(null);
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
            existing = transform.Find(RootName);
        }
    }

    private void ApplyLevelSettings()
    {
        if (!useLevelCatalog || !Application.isPlaying) return;

        LevelDefinition level = LevelCatalog.CurrentLevel;
        if (level == null || level.SceneName != gameObject.scene.name) return;

        seed = level.CitySeed;
        blocks = Mathf.Clamp(level.CityBlocks, 2, 10);
        buildingDensity = level.BuildingDensity;
        maxBuildingHeight = Mathf.Max(minBuildingHeight + 2f, level.MaxBuildingHeight);
        defaultCheckpointCount = level.CheckpointCount;
    }

    #region 地面與道路

    private void BuildGround()
    {
        float size = HalfSpan * 2f + 120f;
        GameObject ground = CreateBox("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(size, 1f, size),
            groundMaterial, new Color(0.32f, 0.42f, 0.28f));
        ground.isStatic = true;
    }

    private void BuildRoads()
    {
        float span = HalfSpan * 2f;
        for (int i = 0; i <= blocks; i++)
        {
            float offset = -HalfSpan + roadWidth * 0.5f + i * Pitch;

            CreateBox($"Road_X_{i}", new Vector3(0f, 0.01f, offset), new Vector3(span, 0.02f, roadWidth), roadMaterial, new Color(0.18f, 0.18f, 0.2f));
            CreateBox($"Road_Z_{i}", new Vector3(offset, 0.012f, 0f), new Vector3(roadWidth, 0.02f, span), roadMaterial, new Color(0.18f, 0.18f, 0.2f));

            // 中央分隔線
            CreateBox($"Line_X_{i}", new Vector3(0f, 0.03f, offset), new Vector3(span, 0.01f, 0.25f), null, new Color(0.95f, 0.8f, 0.2f), false);
            CreateBox($"Line_Z_{i}", new Vector3(offset, 0.032f, 0f), new Vector3(0.25f, 0.01f, span), null, new Color(0.95f, 0.8f, 0.2f), false);
        }
    }

    #endregion

    #region 街區

    private Vector3 BlockCenter(int bx, int bz)
    {
        float start = -HalfSpan + roadWidth + blockSize * 0.5f;
        return transform.position + new Vector3(start + bx * Pitch, 0f, start + bz * Pitch);
    }

    private void BuildBlock(int bx, int bz)
    {
        Vector3 center = BlockCenter(bx, bz);
        Vector3 local = center - transform.position;
        bool isPlaza = bx == plazaBlock && bz == plazaBlock;

        CreateBox($"Sidewalk_{bx}_{bz}", local + new Vector3(0f, sidewalkHeight * 0.5f, 0f),
            new Vector3(blockSize, sidewalkHeight, blockSize), sidewalkMaterial, new Color(0.62f, 0.62f, 0.6f));

        // 街角人行道作為取貨點候選
        float corner = blockSize * 0.5f - 2.5f;
        streetSpots.Add(center + new Vector3(corner, sidewalkHeight, corner));
        streetSpots.Add(center + new Vector3(-corner, sidewalkHeight, -corner));

        if (isPlaza)
        {
            BuildPlaza(center);
            return;
        }

        // 每個街區切成 2x2 地塊
        float margin = 2.5f;
        float lotSize = (blockSize - margin * 3f) * 0.5f;
        float centerFactor = 1f - Mathf.Clamp01(Vector2.Distance(new Vector2(bx, bz), new Vector2(plazaBlock, plazaBlock)) / (blocks * 0.75f));

        for (int lx = 0; lx < 2; lx++)
        {
            for (int lz = 0; lz < 2; lz++)
            {
                Vector3 lotCenter = center + new Vector3((lx - 0.5f) * (lotSize + margin), sidewalkHeight, (lz - 0.5f) * (lotSize + margin));
                if (random.NextDouble() < buildingDensity)
                {
                    // 越靠近市中心越高
                    float heightBias = Mathf.Lerp(0.15f, 1f, centerFactor);
                    float targetHeight = Mathf.Lerp(minBuildingHeight, maxBuildingHeight, (float)random.NextDouble() * heightBias);
                    Vector3 roadSide = new Vector3(lx == 0 ? -1f : 1f, 0f, lz == 0 ? -1f : 1f);
                    PlaceBuilding(lotCenter, lotSize, targetHeight, roadSide, centerFactor);
                }
                else
                {
                    PlaceProps(lotCenter, lotSize);
                    streetSpots.Add(lotCenter);
                }
            }
        }
    }

    private void BuildPlaza(Vector3 center)
    {
        Vector3 local = center - transform.position;
        CreateBox("Plaza_Grass", local + new Vector3(0f, sidewalkHeight + 0.02f, 0f), new Vector3(blockSize - 6f, 0.04f, blockSize - 6f),
            null, new Color(0.35f, 0.6f, 0.3f));

        // 起飛平台 (H 標誌)
        float padTop = sidewalkHeight + 0.4f;
        CreateBox("TakeoffPad", local + new Vector3(0f, sidewalkHeight + 0.2f, 0f), new Vector3(8f, 0.4f, 8f), null, new Color(0.25f, 0.27f, 0.3f));
        CreateBox("Pad_H_Left", local + new Vector3(-1.5f, padTop + 0.01f, 0f), new Vector3(0.5f, 0.02f, 4f), null, Color.white, false);
        CreateBox("Pad_H_Right", local + new Vector3(1.5f, padTop + 0.01f, 0f), new Vector3(0.5f, 0.02f, 4f), null, Color.white, false);
        CreateBox("Pad_H_Bar", local + new Vector3(0f, padTop + 0.01f, 0f), new Vector3(3f, 0.02f, 0.5f), null, Color.white, false);

        plazaCenter = center + new Vector3(0f, padTop, 0f);

        // 廣場四周的取貨點
        streetSpots.Add(center + new Vector3(10f, sidewalkHeight + 0.04f, 10f));
        streetSpots.Add(center + new Vector3(-10f, sidewalkHeight + 0.04f, 10f));
    }

    private void PlaceBuilding(Vector3 lotCenter, float lotSize, float targetHeight, Vector3 roadSide, float centerFactor)
    {
        bool preferSmall = targetHeight < (minBuildingHeight + maxBuildingHeight) * 0.35f;
        GameObject prefab = PickPrefab(preferSmall ? smallBuildingPrefabs : buildingPrefabs)
                            ?? PickPrefab(buildingPrefabs) ?? PickPrefab(smallBuildingPrefabs);

        GameObject building;
        if (prefab == null)
        {
            building = CreateFallbackBuilding(lotSize, targetHeight);
        }
        else
        {
            building = Instantiate(prefab, root);
            building.name = prefab.name;
            FitBuilding(building, lotSize, targetHeight);
            EnsureColliders(building);
        }

        // 朝向最近的道路 (轉 90 度的倍數)
        float yaw = Mathf.Abs(roadSide.x) > Mathf.Abs(roadSide.z) ? (roadSide.x > 0 ? 90f : 270f) : (roadSide.z > 0 ? 0f : 180f);
        if (random.NextDouble() < 0.5) yaw = roadSide.z > 0 ? 0f : 180f;
        building.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        // 底部貼齊人行道、水平置中於地塊
        Bounds bounds = WorldBounds(building);
        Vector3 offset = lotCenter - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        building.transform.position += offset;
        building.isStatic = true;
    }

    private void FitBuilding(GameObject building, float lotSize, float targetHeight)
    {
        building.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        building.transform.localScale = Vector3.one;
        Bounds bounds = WorldBounds(building);
        float footprint = Mathf.Max(bounds.size.x, bounds.size.z);
        if (footprint < 0.01f || bounds.size.y < 0.01f) return;

        float horizontalScale = lotSize * 0.92f / footprint;
        float naturalHeight = bounds.size.y * horizontalScale;
        float verticalScale = horizontalScale * Mathf.Clamp(targetHeight / naturalHeight, 1f / maxVerticalStretch, maxVerticalStretch);
        building.transform.localScale = new Vector3(horizontalScale, verticalScale, horizontalScale);
    }

    private GameObject CreateFallbackBuilding(float lotSize, float height)
    {
        float width = lotSize * (0.75f + (float)random.NextDouble() * 0.2f);
        float depth = lotSize * (0.75f + (float)random.NextDouble() * 0.2f);
        float shade = 0.55f + (float)random.NextDouble() * 0.35f;
        GameObject building = CreateBox("Building_Box", Vector3.zero, new Vector3(width, height, depth), null,
            new Color(shade, shade * 0.97f, shade * 0.93f));
        // 屋頂設備，讓屋頂降落點更有辨識度
        GameObject roofUnit = CreateBox("RoofUnit", Vector3.zero, new Vector3(width * 0.25f, 1.5f, depth * 0.25f), null, new Color(0.4f, 0.4f, 0.42f));
        roofUnit.transform.SetParent(building.transform, true);
        roofUnit.transform.localPosition = new Vector3(0.3f, 0.5f + 0.75f / height, 0.3f);
        roofUnit.transform.localScale = new Vector3(0.25f, 1.5f / height, 0.25f);
        return building;
    }

    private void PlaceProps(Vector3 lotCenter, float lotSize)
    {
        // 空地塊：草地 + 少量樹籬/圍欄
        CreateBox("Lot_Grass", lotCenter - transform.position + new Vector3(0f, 0.02f, 0f), new Vector3(lotSize, 0.04f, lotSize),
            null, new Color(0.33f, 0.55f, 0.28f));

        GameObject propPrefab = PickPrefab(propPrefabs);
        if (propPrefab == null) return;

        int count = 1 + random.Next(3);
        for (int i = 0; i < count; i++)
        {
            Vector3 position = lotCenter + new Vector3(((float)random.NextDouble() - 0.5f) * lotSize * 0.7f, 0.04f,
                ((float)random.NextDouble() - 0.5f) * lotSize * 0.7f);
            GameObject prop = Instantiate(propPrefab, position, Quaternion.Euler(0f, random.Next(4) * 90f, 0f), root);
            Bounds bounds = WorldBounds(prop);
            if (bounds.size.y > 4f) prop.transform.localScale *= 4f / bounds.size.y; // 避免道具比例過大
            EnsureColliders(prop);
            prop.isStatic = true;
        }
    }

    #endregion

    #region 降落點與出生點

    private void CollectRooftops()
    {
        foreach (Transform child in root)
        {
            if (!child.name.StartsWith("Building") && child.GetComponentInChildren<MeshCollider>() == null) continue;
            if (child.name.StartsWith("Sidewalk") || child.name.StartsWith("Road") || child.name == "Ground") continue;

            Bounds bounds = WorldBounds(child.gameObject);
            if (bounds.size.y < 4f) continue;

            Vector3 top = new Vector3(bounds.center.x, bounds.max.y + 5f, bounds.center.z);
            if (Physics.Raycast(top, Vector3.down, out RaycastHit hit, bounds.size.y + 10f, ~0, QueryTriggerInteraction.Ignore)
                && hit.normal.y > 0.9f && hit.collider.transform.IsChildOf(child)
                && hit.point.y > bounds.min.y + 3f)
            {
                rooftopSpots.Add(hit.point);
            }
        }
    }

    private void MoveSpawnerToPlaza()
    {
        SpawnCtrl spawner = FindFirstObjectByType<SpawnCtrl>();
        if (spawner == null) return;

        // SpawnCtrl 會再往上抬 0.8m
        spawner.transform.position = plazaCenter + Vector3.up * 0.3f;
        spawner.transform.rotation = Quaternion.LookRotation(Vector3.forward);
    }

    #endregion

    #region 競速路線

    private void BuildRaceCourse()
    {
        int nodes = blocks + 1;
        // 起點：廣場東北角的路口
        Vector2Int current = new Vector2Int(plazaBlock + 1, plazaBlock + 1);
        Vector2Int direction = new Vector2Int(0, 1);
        HashSet<Vector2Int> visited = new HashSet<Vector2Int> { current };
        List<Vector3> path = new List<Vector3>();
        System.Random courseRandom = new System.Random(seed * 31 + 7);

        int count = Mathf.Max(3, defaultCheckpointCount);
        for (int i = 0; i < count; i++)
        {
            float height = 5f + (float)courseRandom.NextDouble() * 10f;
            path.Add(IntersectionPosition(current) + Vector3.up * height);

            List<Vector2Int> options = new List<Vector2Int>();
            foreach (Vector2Int step in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
            {
                Vector2Int next = current + step;
                if (next.x < 0 || next.y < 0 || next.x >= nodes || next.y >= nodes || visited.Contains(next)) continue;
                options.Add(step);
                if (step == direction) options.Add(step); // 偏好直行，路線更流暢
            }

            if (options.Count == 0)
            {
                visited.Clear(); // 走進死路時允許回頭重用路口
                options.Add(-direction);
            }

            direction = options[courseRandom.Next(options.Count)];
            current += direction;
            visited.Add(current);
        }

        for (int i = 0; i < path.Count; i++)
        {
            Vector3 forward = i + 1 < path.Count ? path[i + 1] - path[i] : path[i] - path[i - 1];
            forward.y = 0f;
            Quaternion rotation = Quaternion.LookRotation(forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward);
            CourseBuilder.CreateCheckpointRing(path[i], i, rotation, checkpointRadius);
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.totalCheckpoints = path.Count;
        }
        RingTrigger.SetNavigationTo(0);
    }

    private Vector3 IntersectionPosition(Vector2Int node)
    {
        float start = -HalfSpan + roadWidth * 0.5f;
        return transform.position + new Vector3(start + node.x * Pitch, 0f, start + node.y * Pitch);
    }

    #endregion

    #region 工具

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

    private GameObject CreateBox(string boxName, Vector3 localPosition, Vector3 size, Material material, Color fallbackColor, bool withCollider = true)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = boxName;
        box.transform.SetParent(root, false);
        box.transform.localPosition = localPosition;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().sharedMaterial = material != null ? material : RuntimeMaterials.Opaque(fallbackColor);
        if (!withCollider)
        {
            Collider boxCollider = box.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(boxCollider);
            else DestroyImmediate(boxCollider);
        }
        box.isStatic = true;
        return box;
    }

    private static void EnsureColliders(GameObject target)
    {
        if (target.GetComponentInChildren<Collider>() != null) return;

        foreach (MeshFilter filter in target.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            MeshCollider meshCollider = filter.gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = filter.sharedMesh;
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
}

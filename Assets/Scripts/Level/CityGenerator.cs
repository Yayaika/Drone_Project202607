using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 程序化城市生成器：棋盤式街道 + 街區地塊，地塊放入城市素材包的建築 (自動縮放、加碰撞體)，
/// 中央保留起飛廣場。依遊戲模式自動建立競速檢查圈路線或搬運任務管理器。
/// 參數可由 <see cref="LevelCatalog"/> 的關卡設定覆寫 (種子、街區數、密度、最高樓高)。
/// </summary>
[DefaultExecutionOrder(-200)] // 需早於 SpawnCtrl.Awake，先把出生點移到廣場
public partial class CityGenerator : MonoBehaviour, IDeliverySpotProvider
{
    private const string RootName = "GeneratedCity";

    [Header("生成時機")]
    [SerializeField] private bool generateOnAwake = true;
    [Tooltip("已在編輯器烘焙成固定場景內容：執行時不再生成，只建立車流/路人與競速檢查點")]
    [SerializeField] private bool baked;
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
    [SerializeField] private float maxVerticalStretch = 1.3f;
    [Tooltip("建築模型最小等比縮放；素材包為真實公尺比例，太大的模型縮不進地塊時改用其他模型")]
    [Range(0.3f, 1f)]
    [SerializeField] private float minModelScale = 0.55f;

    [Header("素材 (由「無人機遊戲/建立程序化城市關卡」自動填入)")]
    [SerializeField] private GameObject[] buildingPrefabs;
    [SerializeField] private GameObject[] smallBuildingPrefabs;
    [SerializeField] private GameObject[] propPrefabs;
    [SerializeField] private Material roadMaterial;
    [SerializeField] private Material sidewalkMaterial;
    [SerializeField] private Material groundMaterial;

    [Header("素材包配色 (由選單自動填入；留空則保留模型原材質)")]
    [Tooltip("有專屬貼圖的模型：建築只在招牌子網格套用，道具整個套用")]
    [SerializeField] private TexturedModel[] texturedModels;
    [SerializeField] private Material[] facadeMaterials;
    [SerializeField] private Material[] roofMaterials;
    [SerializeField] private Material[] windowMaterials;
    [SerializeField] private Material trimMaterial;
    [SerializeField] private Material doorMaterial;
    [SerializeField] private Material accentMaterial;

    [System.Serializable]
    private struct TexturedModel
    {
        public GameObject Model;
        public Material Material;
    }

    [Header("玩法")]
    [SerializeField] private bool buildRaceCourseInRaceMode = true;
    [SerializeField] private bool addDeliveryManagerInDeliveryMode = true;
    [SerializeField] private GameObject cargoPrefab;
    [SerializeField] private int defaultCheckpointCount = 8;
    [SerializeField] private float checkpointRadius = 3.5f;

    private readonly Dictionary<GameObject, PolygonCityModels.Measurement> modelMeasurements =
        new Dictionary<GameObject, PolygonCityModels.Measurement>();
    // 生成結果 (烘焙後隨場景保存)
    [SerializeField, HideInInspector] private List<Vector3> rooftopSpots = new List<Vector3>();
    [SerializeField, HideInInspector] private List<Vector3> streetSpots = new List<Vector3>();
    [SerializeField, HideInInspector] private List<Vector3> racePath = new List<Vector3>();
    [SerializeField, HideInInspector] private Vector3 plazaCenter;
    private System.Random random;
    private Transform root;
    private int plazaBlock;

    public IReadOnlyList<Vector3> RooftopSpots => rooftopSpots;
    public IReadOnlyList<Vector3> StreetSpots => streetSpots;
    public Vector3 PlazaCenter => plazaCenter;

    private float Pitch => blockSize + roadWidth;
    private float HalfSpan => (blocks * blockSize + (blocks + 1) * roadWidth) * 0.5f;

    public bool IsBaked => baked;

    private void Awake()
    {
        if (baked)
        {
            // 固定場景：內容已存在，只接回參照
            root = transform.Find(RootName);
            plazaBlock = blocks / 2;
            MoveSpawnerToPlaza();
            if (root != null) SpawnCityLife();
        }
        else if (generateOnAwake)
        {
            Generate();
            MoveSpawnerToPlaza();
            SpawnCityLife();
        }
    }

    private void Start()
    {
        if (!generateOnAwake && !baked) return;

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
        signRoot = null;
        BeginSurfaces();

        BuildGround();
        BuildRoads();
        for (int bx = 0; bx < blocks; bx++)
        {
            for (int bz = 0; bz < blocks; bz++)
            {
                BuildBlock(bx, bz);
            }
        }

        // 競速路線在生成時就決定 (固定)，台灣街景沿路線佈置障礙物
        ComputeRacePath();
        if (taiwanStreetStyle) BuildRaceObstacles();
        EndSurfaces();

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
        CreateSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(size, 1f, size),
            groundMaterial, new Color(0.32f, 0.42f, 0.28f), 8f);
    }

    private void BuildRoads()
    {
        float span = HalfSpan * 2f;
        for (int i = 0; i <= blocks; i++)
        {
            float offset = -HalfSpan + roadWidth * 0.5f + i * Pitch;

            CreateSurface($"Road_X_{i}", new Vector3(0f, 0.01f, offset), new Vector3(span, 0.02f, roadWidth), roadMaterial, new Color(0.18f, 0.18f, 0.2f), 6f);
            CreateSurface($"Road_Z_{i}", new Vector3(offset, 0.012f, 0f), new Vector3(roadWidth, 0.02f, span), roadMaterial, new Color(0.18f, 0.18f, 0.2f), 6f);

            if (taiwanStreetStyle) continue;

            // 中央分隔線
            CreateBox($"Line_X_{i}", new Vector3(0f, 0.03f, offset), new Vector3(span, 0.01f, 0.25f), null, new Color(0.95f, 0.8f, 0.2f), false);
            CreateBox($"Line_Z_{i}", new Vector3(offset, 0.032f, 0f), new Vector3(0.25f, 0.01f, span), null, new Color(0.95f, 0.8f, 0.2f), false);
        }

        if (taiwanStreetStyle) BuildTaiwanRoadMarkings();
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

        CreateSurface($"Sidewalk_{bx}_{bz}", local + new Vector3(0f, sidewalkHeight * 0.5f, 0f),
            new Vector3(blockSize, sidewalkHeight, blockSize), sidewalkMaterial, new Color(0.62f, 0.62f, 0.6f), 2f);

        // 街角人行道作為取貨點候選
        float corner = blockSize * 0.5f - 2.5f;
        streetSpots.Add(center + new Vector3(corner, sidewalkHeight, corner));
        streetSpots.Add(center + new Vector3(-corner, sidewalkHeight, -corner));

        if (isPlaza)
        {
            BuildPlaza(center);
            return;
        }

        float centerFactor = 1f - Mathf.Clamp01(Vector2.Distance(new Vector2(bx, bz), new Vector2(plazaBlock, plazaBlock)) / (blocks * 0.75f));
        if (taiwanStreetStyle)
        {
            // 台北式街區：窄巷 + 騎樓 + 高密度
            BuildTaiwanBlock(bx, bz, center, centerFactor);
            return;
        }

        // 每個街區切成 2x2 地塊
        float margin = 2.5f;
        float lotSize = (blockSize - margin * 3f) * 0.5f;
        CityMeshBuilder blockMesh = null;

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
                    if (blockMesh == null)
                    {
                        PlaceBuilding(lotCenter, lotSize, targetHeight, roadSide, centerFactor);
                    }
                    else if (!Chance(modelBuildingChance) || !TryPlaceModelBuilding(lotCenter, lotSize, targetHeight, roadSide))
                    {
                        // 台灣街景：優先使用素材包建築，放不下或少量變化時改為沿街騎樓街屋
                        BuildShophouseLot(blockMesh, lotCenter, lotSize, targetHeight, (int)roadSide.x, (int)roadSide.z);
                    }
                }
                else if (blockMesh != null && Chance(parkingLotChance) && BuildParkingLot(blockMesh, lotCenter, lotSize, lx == 0 ? -1 : 1))
                {
                    // 台灣街景：空地多為平面停車場
                }
                else
                {
                    PlaceProps(lotCenter, lotSize);
                    streetSpots.Add(lotCenter);
                }
            }
        }

        blockMesh?.Build($"Block_{bx}_{bz}_Shophouses", root);
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

        if (taiwanStreetStyle)
        {
            // 廣場綠地：四角大樹、灌木與草叢 (避開中央起飛平台)
            float half = (blockSize - 6f) * 0.5f;
            foreach (Vector2 cornerSign in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
            {
                Vector3 cornerCenter = center + new Vector3(cornerSign.x * half * 0.6f, sidewalkHeight + 0.04f, cornerSign.y * half * 0.6f);
                DecoratePark(cornerCenter, half * 0.7f, half * 0.7f);
            }
        }

        // 廣場四周的取貨點
        streetSpots.Add(center + new Vector3(10f, sidewalkHeight + 0.04f, 10f));
        streetSpots.Add(center + new Vector3(-10f, sidewalkHeight + 0.04f, 10f));
    }

    /// <summary>只使用素材包建築；模型放不進地塊時回傳 false (不建立方塊大樓)</summary>
    private bool TryPlaceModelBuilding(Vector3 lotCenter, float lotSize, float targetHeight, Vector3 roadSide)
    {
        float yaw = Mathf.Abs(roadSide.x) > Mathf.Abs(roadSide.z) ? (roadSide.x > 0 ? 90f : 270f) : (roadSide.z > 0 ? 0f : 180f);
        if (random.NextDouble() < 0.5) yaw = roadSide.z > 0 ? 0f : 180f;

        bool preferSmall = targetHeight < (minBuildingHeight + maxBuildingHeight) * 0.35f;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            GameObject prefab = PickPrefab(preferSmall && attempt < 3 ? smallBuildingPrefabs : buildingPrefabs)
                                ?? PickPrefab(buildingPrefabs) ?? PickPrefab(smallBuildingPrefabs);
            if (prefab == null) return false;
            GameObject building = CreateModelBuilding(prefab, lotCenter, lotSize, targetHeight, yaw);
            if (building != null)
            {
                SetStaticRecursively(building);
                return true;
            }
        }
        return false;
    }

    private static void SetStaticRecursively(GameObject target)
    {
        foreach (Transform part in target.GetComponentsInChildren<Transform>(true)) part.gameObject.isStatic = true;
    }

    private void PlaceBuilding(Vector3 lotCenter, float lotSize, float targetHeight, Vector3 roadSide, float centerFactor)
    {
        // 朝向最近的道路 (轉 90 度的倍數)
        float yaw = Mathf.Abs(roadSide.x) > Mathf.Abs(roadSide.z) ? (roadSide.x > 0 ? 90f : 270f) : (roadSide.z > 0 ? 0f : 180f);
        if (random.NextDouble() < 0.5) yaw = roadSide.z > 0 ? 0f : 180f;

        bool preferSmall = targetHeight < (minBuildingHeight + maxBuildingHeight) * 0.35f;
        GameObject building = null;
        for (int attempt = 0; attempt < 3 && building == null; attempt++)
        {
            GameObject prefab = PickPrefab(preferSmall ? smallBuildingPrefabs : buildingPrefabs)
                                ?? PickPrefab(buildingPrefabs) ?? PickPrefab(smallBuildingPrefabs);
            if (prefab == null) break;
            building = CreateModelBuilding(prefab, lotCenter, lotSize, targetHeight, yaw);
        }

        if (building == null)
        {
            building = CreateFallbackBuilding(lotSize, targetHeight);
            building.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            // 底部貼齊人行道、水平置中於地塊
            Bounds bounds = WorldBounds(building);
            building.transform.position += lotCenter - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }
        building.isStatic = true;
    }

    /// <summary>
    /// 放置素材包建築：外層物件負責位置、朝向與縮放，模型保留自身根節點旋轉 (X 270°)，
    /// 正面依門口方向自動朝向道路。模型縮不進地塊時回傳 null。
    /// </summary>
    private GameObject CreateModelBuilding(GameObject prefab, Vector3 lotCenter, float lotSize, float targetHeight, float yaw)
    {
        if (!modelMeasurements.TryGetValue(prefab, out PolygonCityModels.Measurement measurement))
        {
            measurement = PolygonCityModels.Measure(prefab);
            modelMeasurements[prefab] = measurement;
        }
        if (!measurement.Valid) return null;

        float scale = Mathf.Min(lotSize * 0.92f / Mathf.Max(measurement.Size.x, measurement.Size.z), 1.1f);
        if (scale < minModelScale) return null;
        float stretch = Mathf.Clamp(targetHeight / (measurement.Size.y * scale), 1f / maxVerticalStretch, maxVerticalStretch);

        GameObject building = new GameObject(prefab.name);
        building.transform.SetParent(root, false);
        building.transform.position = lotCenter;
        building.transform.rotation = Quaternion.Euler(0f, yaw - measurement.FrontYaw, 0f);
        building.transform.localScale = new Vector3(scale, scale * stretch, scale);

        GameObject model = Instantiate(prefab, building.transform);
        model.name = prefab.name;
        model.transform.localPosition = -measurement.Pivot;

        ApplyPackMaterials(model, prefab);
        EnsureColliders(model);
        return building;
    }

    /// <summary>素材包 FBX 匯入後材質皆為預設灰色，依子網格名稱改套素材包配色；未設定配色時保留原材質</summary>
    private void ApplyPackMaterials(GameObject model, GameObject prefab)
    {
        if (facadeMaterials == null || facadeMaterials.Length == 0) return;

        PolygonCityModels.ApplyMaterials(model, new PolygonCityModels.Palette
        {
            Facade = PickMaterial(facadeMaterials),
            Roof = PickMaterial(roofMaterials),
            Window = PickMaterial(windowMaterials),
            Door = doorMaterial,
            Trim = trimMaterial,
            Accent = accentMaterial,
            Pavement = sidewalkMaterial,
            Sign = TexturedMaterialFor(prefab)
        });
    }

    private Material TexturedMaterialFor(GameObject prefab)
    {
        if (texturedModels == null) return null;
        foreach (TexturedModel entry in texturedModels)
        {
            if (entry.Model == prefab) return entry.Material;
        }
        return null;
    }

    private Material PickMaterial(Material[] materials)
    {
        if (materials == null || materials.Length == 0) return null;
        return materials[random.Next(materials.Length)];
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
            // 保留模型根節點自身的旋轉 (素材包為 X 270°)，否則樹籬/圍欄會倒在地上
            GameObject prop = Instantiate(propPrefab, position, Quaternion.Euler(0f, random.Next(4) * 90f, 0f) * propPrefab.transform.rotation, root);
            Material propMaterial = TexturedMaterialFor(propPrefab);
            if (propMaterial != null)
            {
                foreach (Renderer propRenderer in prop.GetComponentsInChildren<Renderer>())
                {
                    Material[] materials = propRenderer.sharedMaterials;
                    for (int m = 0; m < materials.Length; m++) materials[m] = propMaterial;
                    propRenderer.sharedMaterials = materials;
                }
            }
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

    /// <summary>依種子決定競速路線 (路口序列 + 高度)，生成時計算並隨烘焙場景保存</summary>
    private void ComputeRacePath()
    {
        racePath.Clear();
        int nodes = blocks + 1;
        // 起點：廣場東北角的路口
        Vector2Int current = new Vector2Int(plazaBlock + 1, plazaBlock + 1);
        Vector2Int direction = new Vector2Int(0, 1);
        HashSet<Vector2Int> visited = new HashSet<Vector2Int> { current };
        System.Random courseRandom = new System.Random(seed * 31 + 7);

        int count = Mathf.Max(3, defaultCheckpointCount);
        for (int i = 0; i < count; i++)
        {
            float height = 5f + (float)courseRandom.NextDouble() * 10f;
            racePath.Add(IntersectionPosition(current) + Vector3.up * height);

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
    }

    private void BuildRaceCourse()
    {
        if (racePath.Count == 0) ComputeRacePath();

        // 路口序列是直角轉彎：先在路口內圓角 (不切過街角建築)，再沿路線連續擺放、錯落配置檢查圈
        float gateRadius = checkpointRadius * CourseBuilder.RingSizeScale;
        List<Vector3> route = RaceCourseLayout.RoundCorners(racePath, roadWidth * 1.3f, roadWidth * 0.55f, 0.3f);
        List<RaceCourseLayout.Gate> gates = RaceCourseLayout.PlaceGates(route, 0f, 18f, 30f, gateRadius, seed * 17 + 3);
        for (int i = 0; i < gates.Count; i++)
        {
            CourseBuilder.CreateCheckpointRing(gates[i].Position, i, gates[i].Rotation, checkpointRadius);
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.totalCheckpoints = gates.Count;
        }
        RingTrigger.SetNavigationTo(0);
    }

    /// <summary>
    /// 低空飛機航線用：沿街區四周道路中心線的封閉迴圈 (1×1、2×1、1×2 個街區，世界座標、貼地高度，未圓角)。
    /// 道路上方沒有建築，飛機可以壓低到無人機的飛行高度。
    /// </summary>
    public List<Vector3[]> GetStreetLoops()
    {
        List<Vector3[]> loops = new List<Vector3[]>();
        Vector2Int[] sizes = { new Vector2Int(1, 1), new Vector2Int(2, 1), new Vector2Int(1, 2) };
        for (int bx = 0; bx < blocks; bx++)
        {
            for (int bz = 0; bz < blocks; bz++)
            {
                foreach (Vector2Int size in sizes)
                {
                    if (bx + size.x > blocks || bz + size.y > blocks) continue;
                    loops.Add(new[]
                    {
                        IntersectionPosition(new Vector2Int(bx, bz)),
                        IntersectionPosition(new Vector2Int(bx + size.x, bz)),
                        IntersectionPosition(new Vector2Int(bx + size.x, bz + size.y)),
                        IntersectionPosition(new Vector2Int(bx, bz + size.y))
                    });
                }
            }
        }
        return loops;
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

using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 程序化城市：台灣街景風格。
/// 1. 道路：雙黃線、車道虛線、斑馬線、停止線、機車停等區、路口紅線與路緣黃線。
/// 2. 建築：優先使用素材包建築模型；放不下的地塊改為沿街騎樓透天厝/公寓 (鐵窗、陽台、冷氣機、直立招牌、頂樓水塔與鐵皮加蓋)。
/// 3. 街道設施：LowpolyStreetPack 路燈、行道樹、電線桿與電線、消防栓/長椅等；地面使用有貼圖的材質。
/// 4. 沿固定競速路線佈置障礙物 (天橋、跨街電線、布條、施工塔吊、門架標誌)。
/// 車輛與路人一律使用專案提供的素材 (見 CityTraffic / CityPedestrians)。
/// 幾何以 <see cref="CityMeshBuilder"/> 合併，每個街區只有少數繪製呼叫；碰撞體為獨立 BoxCollider。
/// </summary>
public partial class CityGenerator
{
    [Header("台灣街景 (Taiwan Street Style)")]
    [Tooltip("關閉時恢復原本的棋盤道路 + 素材包建築")]
    [SerializeField] private bool taiwanStreetStyle = true;
    [SerializeField] private float floorHeight = 3.2f;
    [Tooltip("一樓 (騎樓) 高度")]
    [SerializeField] private float arcadeHeight = 3.6f;
    [Tooltip("騎樓深度 (人行空間)")]
    [SerializeField] private float arcadeDepth = 3f;
    [Tooltip("沒有建築的空地改為平面停車場的機率 (停放的車輛使用 vehiclePrefabs)")]
    [Range(0f, 1f)]
    [SerializeField] private float parkingLotChance = 0.6f;

    [Tooltip("有建築的地塊使用素材包建築模型的機率 (放不下時自動改為騎樓街屋)")]
    [Range(0f, 1f)]
    [SerializeField] private float modelBuildingChance = 0.5f;

    [Header("台北式街區 (窄巷 / 騎樓 / 高密度)")]
    [Tooltip("每個街區中央開一條沒有人行道的巷子 (公尺)")]
    [SerializeField] private float alleyWidth = 5f;
    [Tooltip("巷內公寓有騎樓的機率")]
    [Range(0f, 1f)]
    [SerializeField] private float alleyArcadeChance = 0.35f;
    [Tooltip("二丁掛磁磚貼圖 (立面使用，留空則為純色)")]
    [SerializeField] private Texture facadeTileTexture;
    [SerializeField] private float facadeTileSize = 0.9f;

    [Header("植物 (Anime Nature，由選單自動填入)")]
    [Tooltip("行道樹 / 公園樹")]
    [SerializeField] private GameObject[] treeModelPrefabs;
    [SerializeField] private GameObject[] bushPrefabs;
    [SerializeField] private GameObject[] grassPrefabs;
    [Tooltip("行道樹縮放後的高度 (公尺)")]
    [SerializeField] private float streetTreeHeight = 6.5f;

    [Header("街道設施素材 (LowpolyStreetPack，由選單自動填入)")]
    [SerializeField] private GameObject[] streetLampPrefabs;
    [SerializeField] private GameObject[] powerPolePrefabs;
    [Tooltip("消防栓、長椅、郵筒、電箱、報攤等")]
    [SerializeField] private GameObject[] sidewalkPropPrefabs;
    [SerializeField] private GameObject[] roadBlockPrefabs;
    [SerializeField] private GameObject[] overpassPrefabs;
    [Tooltip("LowpolyStreetPack 原為 Built-in 材質，放置時替換為 URP 版本")]
    [SerializeField] private MaterialRemap[] propMaterialRemaps;

    [System.Serializable]
    private struct MaterialRemap
    {
        public Material from;
        public Material to;
    }

    [Header("城市活力 (車流 / 路人)")]
    [SerializeField] private bool spawnTraffic = true;
    [SerializeField] private bool spawnPedestrians = true;
    [Tooltip("汽車模型 (由選單自動填入 Awbmecreations 低多邊形車輛)")]
    [SerializeField] private GameObject[] vehiclePrefabs;
    [Tooltip("路人模型 (由選單自動填入 CityPeople)")]
    [SerializeField] private GameObject[] pedestrianPrefabs;
    [Tooltip("每條車道平均汽車數 (每個行車方向有內、外兩條車道)")]
    [SerializeField] private float carsPerLane = 0.3f;
    [SerializeField] private int pedestriansPerBlock = 4;

    // --- 道路橫向配置 (以道路中心線為 0，往右為正，單位 m；道路寬 12m，每向兩車道) ---
    /// <summary>內側車道中心 (直行、左轉)</summary>
    public const float InnerLaneOffset = 1.5f;
    /// <summary>外側車道中心 (直行、右轉)</summary>
    public const float OuterLaneOffset = 4.4f;
    private const float LaneDividerOffset = 3f;
    private const float StopLineHalfWidth = 5.6f;
    private const float ZebraStart = 0.5f;
    private const float ZebraEnd = 3.5f;
    /// <summary>汽車停止線 (距路口邊緣)，停止線與斑馬線之間為機車停等區</summary>
    public const float CarStopDistance = 6.4f;
    private const float ScooterBoxFront = 3.9f;

    private static readonly string[] ShopNames =
    {
        "早餐店", "牛肉麵", "便當", "藥局", "眼鏡", "機車行", "茶飲", "五金行", "診所", "補習班",
        "水電行", "通訊行", "小吃", "美髮", "牙醫", "房屋仲介", "麵包店", "水餃", "滷肉飯", "自助餐",
        "中醫", "文具", "洗衣店", "電器行", "鹹酥雞", "豆花", "咖啡", "火鍋", "旅社", "書局",
        "銀樓", "刈包", "粥品", "鐘錶", "寵物", "按摩", "租車", "冰品", "燒臘", "果汁"
    };

    private static readonly Color[] FacadeColors =
    {
        new Color(0.86f, 0.82f, 0.74f), new Color(0.92f, 0.92f, 0.9f), new Color(0.72f, 0.72f, 0.7f),
        new Color(0.62f, 0.4f, 0.32f), new Color(0.85f, 0.72f, 0.68f), new Color(0.7f, 0.8f, 0.77f),
        new Color(0.78f, 0.68f, 0.52f), new Color(0.56f, 0.57f, 0.6f), new Color(0.9f, 0.86f, 0.62f)
    };

    private static readonly Color[] SignColors =
    {
        new Color(0.82f, 0.1f, 0.1f), new Color(0.98f, 0.82f, 0.12f), new Color(0.1f, 0.3f, 0.72f),
        new Color(0.08f, 0.55f, 0.3f), new Color(0.96f, 0.96f, 0.92f), new Color(0.96f, 0.5f, 0.08f),
        new Color(0.45f, 0.15f, 0.55f)
    };

    private readonly Dictionary<Color, Material> matteCache = new Dictionary<Color, Material>();
    private TMP_FontAsset signFont;
    private Transform signRoot;

    /// <summary>道路網格資訊 (車流/路人系統使用)</summary>
    public CityGrid Grid => new CityGrid
    {
        Origin = transform.position + new Vector3(-HalfSpan + roadWidth * 0.5f, 0f, -HalfSpan + roadWidth * 0.5f),
        Pitch = Pitch,
        RoadWidth = roadWidth,
        BlockSize = blockSize,
        SidewalkHeight = sidewalkHeight,
        Blocks = blocks
    };

    /// <summary>低反光材質 (Lit 預設光滑度太高，牆面會像塑膠)</summary>
    private Material Matte(Color color)
    {
        if (matteCache.TryGetValue(color, out Material cached) && cached != null) return cached;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material material = new Material(shader) { name = $"Taiwan_{ColorUtility.ToHtmlStringRGB(color)}" };
        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.12f);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.12f);
        matteCache[color] = material;
        return material;
    }

    private readonly Dictionary<Color, Material> facadeCache = new Dictionary<Color, Material>();

    /// <summary>外牆：二丁掛磁磚貼圖 × 牆面顏色 (沒有貼圖時為純色)</summary>
    private Material Facade(Color color)
    {
        if (facadeTileTexture == null) return Matte(color);
        if (facadeCache.TryGetValue(color, out Material cached) && cached != null) return cached;

        Material material = new Material(Matte(color)) { name = $"Taiwan_Tile_{ColorUtility.ToHtmlStringRGB(color)}" };
        material.SetTexture("_BaseMap", facadeTileTexture);
        material.mainTexture = facadeTileTexture;
        // 磁磚貼圖本身偏灰 (約 0.55)，提高色調讓牆色接近原本設定
        Color tint = new Color(Mathf.Min(color.r * 1.7f, 1.6f), Mathf.Min(color.g * 1.7f, 1.6f), Mathf.Min(color.b * 1.7f, 1.6f), 1f);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
        material.color = tint;
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.25f);
        facadeCache[color] = material;
        return material;
    }

    private Material Glass(Color color)
    {
        Material material = Matte(color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.85f);
        return material;
    }

    private T Pick<T>(T[] values) => values[random.Next(values.Length)];
    private float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
    private bool Chance(float probability) => random.NextDouble() < probability;

    #region 道路標線

    /// <summary>在每段道路 (兩路口之間) 畫上台灣常見標線</summary>
    private void BuildTaiwanRoadMarkings()
    {
        CityMeshBuilder markings = new CityMeshBuilder();
        CityMeshBuilder furniture = new CityMeshBuilder();
        Transform colliders = new GameObject("StreetColliders").transform;
        colliders.SetParent(root, false);
        colliders.gameObject.isStatic = true;

        Material white = Matte(new Color(0.93f, 0.93f, 0.9f));
        Material yellow = Matte(new Color(0.95f, 0.75f, 0.12f));
        Material red = Matte(new Color(0.8f, 0.12f, 0.1f));
        CityGrid grid = Grid;
        bool suburban = maxBuildingHeight < 40f;

        for (int axis = 0; axis < 2; axis++)
        {
            Vector3 dir = axis == 0 ? Vector3.right : Vector3.forward;
            for (int line = 0; line <= blocks; line++)
            {
                for (int seg = 0; seg < blocks; seg++)
                {
                    Vector2Int a = axis == 0 ? new Vector2Int(seg, line) : new Vector2Int(line, seg);
                    Vector3 start = grid.Node(a) + dir * (roadWidth * 0.5f);
                    // 道路頂面約 y = 0.02 (X 向) / 0.022 (Z 向)
                    Matrix4x4 frame = Matrix4x4.TRS(start + Vector3.up * (0.03f + axis * 0.004f), Quaternion.LookRotation(dir), Vector3.one);
                    bool outerRoad = line == 0 || line == blocks;
                    BuildSegmentMarkings(markings, frame, blockSize, white, yellow, red);

                    Matrix4x4 sidewalkFrame = Matrix4x4.TRS(start + Vector3.up * sidewalkHeight, Quaternion.LookRotation(dir), Vector3.one);
                    BuildSegmentFurniture(furniture, colliders, sidewalkFrame, blockSize, suburban, outerRoad);
                }
            }
        }

        markings.Build("RoadMarkings", root, false);
        furniture.Build("StreetFurniture", root);
    }

    private void BuildSegmentMarkings(CityMeshBuilder builder, Matrix4x4 frame, float length,
        Material white, Material yellow, Material red)
    {
        const float thin = 0.01f;
        float centerLength = length - CarStopDistance * 2f;
        float mid = length * 0.5f;

        // 雙黃線 (禁止跨越)
        builder.AddBox(frame, new Vector3(-0.15f, 0f, mid), new Vector3(0.12f, thin, centerLength), yellow);
        builder.AddBox(frame, new Vector3(0.15f, 0f, mid), new Vector3(0.12f, thin, centerLength), yellow);

        // 內外車道分隔虛線
        for (float u = CarStopDistance + 1f; u + 3f < length - CarStopDistance; u += 6f)
        {
            builder.AddBox(frame, new Vector3(-LaneDividerOffset, 0f, u + 1.5f), new Vector3(0.12f, thin, 3f), white);
            builder.AddBox(frame, new Vector3(LaneDividerOffset, 0f, u + 1.5f), new Vector3(0.12f, thin, 3f), white);
        }

        // 兩端斑馬線 (外環道路只有單側有街區，沒有行人穿越，但仍畫上讓路口完整)
        float half = roadWidth * 0.5f - 0.6f;
        float zebraLength = ZebraEnd - ZebraStart;
        for (float v = -half; v <= half + 0.01f; v += 0.9f)
        {
            builder.AddBox(frame, new Vector3(v, 0f, (ZebraStart + ZebraEnd) * 0.5f), new Vector3(0.45f, thin, zebraLength), white);
            builder.AddBox(frame, new Vector3(v, 0f, length - (ZebraStart + ZebraEnd) * 0.5f), new Vector3(0.45f, thin, zebraLength), white);
        }

        // 停止線 + 機車停等區 (+u 方向車流在右側 v > 0；-u 方向在 v < 0)
        for (int side = -1; side <= 1; side += 2)
        {
            float stopU = side > 0 ? length - CarStopDistance : CarStopDistance;
            float boxNear = side > 0 ? length - (CarStopDistance - 0.2f) : CarStopDistance - 0.2f;
            float boxFar = side > 0 ? length - ScooterBoxFront : ScooterBoxFront;
            float boxCenterU = (boxNear + boxFar) * 0.5f;
            float boxLengthU = Mathf.Abs(boxFar - boxNear);
            float laneCenterV = side * StopLineHalfWidth * 0.5f;

            builder.AddBox(frame, new Vector3(laneCenterV, 0f, stopU), new Vector3(StopLineHalfWidth - 0.1f, thin, 0.4f), white);
            builder.AddBox(frame, new Vector3(laneCenterV, 0f, boxFar), new Vector3(StopLineHalfWidth - 0.3f, thin, 0.12f), white);
            builder.AddBox(frame, new Vector3(side * 0.35f, 0f, boxCenterU), new Vector3(0.12f, thin, boxLengthU), white);
            builder.AddBox(frame, new Vector3(side * (StopLineHalfWidth - 0.15f), 0f, boxCenterU), new Vector3(0.12f, thin, boxLengthU), white);
        }

        // 路緣：路口附近紅線 (禁止臨停)，中段黃線 (禁止停車)
        float curb = roadWidth * 0.5f - 0.12f;
        float redLength = 12f - ZebraEnd;
        for (int side = -1; side <= 1; side += 2)
        {
            builder.AddBox(frame, new Vector3(side * curb, 0f, ZebraEnd + redLength * 0.5f), new Vector3(0.15f, thin, redLength), red);
            builder.AddBox(frame, new Vector3(side * curb, 0f, length - ZebraEnd - redLength * 0.5f), new Vector3(0.15f, thin, redLength), red);
            builder.AddBox(frame, new Vector3(side * curb, 0f, mid), new Vector3(0.15f, thin, length - 24f), yellow);
        }
    }

    #endregion

    #region 街道設施

    private Transform propRoot;
    private readonly Dictionary<Material, Material> remapLookup = new Dictionary<Material, Material>();

    private void BuildSegmentFurniture(CityMeshBuilder builder, Transform colliders, Matrix4x4 frame, float length,
        bool suburban, bool outerRoad)
    {
        float sideV = roadWidth * 0.5f + 0.45f;

        for (int s = 0; s < 2; s++)
        {
            int side = s == 0 ? -1 : 1;
            // 外環道路外側沒有街區，不放設施
            if (outerRoad && !IsInsideCity(frame.MultiplyPoint3x4(new Vector3(side * sideV, 0f, length * 0.5f))))
            {
                continue;
            }

            // 路燈：兩側交錯 (使用 LowpolyStreetPack 路燈，沒有素材時以方塊組成)
            float[] lampPositions = side > 0 ? new[] { 10f, length - 10f } : new[] { length * 0.5f };
            foreach (float u in lampPositions)
            {
                Vector3 basePoint = new Vector3(side * sideV, 0f, u);
                Vector3 toRoad = frame.MultiplyVector(new Vector3(-side, 0f, 0f));
                if (!PlaceStreetPrefab(PickPrefab(streetLampPrefabs), frame.MultiplyPoint3x4(basePoint), ArmToward(toRoad), 1f))
                {
                    BuildProceduralLamp(builder, colliders, frame, basePoint, side);
                }
            }

            // 行道樹 (住宅區較多)
            if (suburban || Chance(0.4f))
            {
                float[] treePositions = { length * 0.3f, length * 0.7f };
                foreach (float u in treePositions)
                {
                    Vector3 basePoint = new Vector3(side * (sideV + 0.2f), 0f, u + Range(-1.5f, 1.5f));
                    if (!PlaceTree(frame.MultiplyPoint3x4(basePoint), streetTreeHeight * Range(0.85f, 1.15f)))
                    {
                        BuildProceduralTree(builder, colliders, frame, basePoint);
                    }
                }
            }

            // 街邊設施：消防栓、長椅、郵筒、電箱等 (靠路緣)
            if (sidewalkPropPrefabs != null && sidewalkPropPrefabs.Length > 0)
            {
                for (float u = 6f; u < length - 6f; u += 9f)
                {
                    if (!Chance(0.3f)) continue;
                    float position = u + Range(-1.5f, 1.5f);
                    if (Mathf.Abs(position - length * 0.5f) < 2.5f || Mathf.Abs(position - 10f) < 2f || Mathf.Abs(position - (length - 10f)) < 2f) continue;
                    Vector3 facing = frame.MultiplyVector(new Vector3(-side, 0f, 0f));
                    PlaceStreetPrefab(PickPrefab(sidewalkPropPrefabs), frame.MultiplyPoint3x4(new Vector3(side * (sideV + 0.1f), 0f, position)),
                        Quaternion.LookRotation(facing), 1f);
                }
            }
        }

        // 台灣巷弄的電線桿與電線 (單側)
        if (!outerRoad && powerPolePrefabs != null && powerPolePrefabs.Length > 0 && Chance(0.5f))
        {
            BuildPowerLine(builder, colliders, frame, length, Chance(0.5f) ? 1 : -1);
        }
    }

    /// <summary>LowpolyStreetPack 路燈的燈臂沿模型 +X：讓 +X 指向車道</summary>
    private static Quaternion ArmToward(Vector3 direction)
    {
        return Quaternion.LookRotation(Vector3.Cross(direction, Vector3.up), Vector3.up);
    }

    private void BuildProceduralLamp(CityMeshBuilder builder, Transform colliders, Matrix4x4 frame, Vector3 basePoint, int side)
    {
        Material pole = Matte(new Color(0.45f, 0.47f, 0.5f));
        Material lampLight = RuntimeMaterials.Unlit(new Color(1f, 0.93f, 0.75f));
        builder.AddCylinder(frame, basePoint, Vector3.up, 0.09f, 7.6f, 6, pole);
        builder.AddBox(frame, basePoint + new Vector3(-side * 0.8f, 7.5f, 0f), new Vector3(1.7f, 0.1f, 0.1f), pole);
        builder.AddBox(frame, basePoint + new Vector3(-side * 1.6f, 7.42f, 0f), new Vector3(0.65f, 0.14f, 0.3f), pole);
        builder.AddBox(frame, basePoint + new Vector3(-side * 1.6f, 7.34f, 0f), new Vector3(0.5f, 0.03f, 0.2f), lampLight);
        AddCollider(colliders, frame, basePoint + new Vector3(0f, 3.8f, 0f), new Vector3(0.2f, 7.6f, 0.2f));
    }

    private void BuildProceduralTree(CityMeshBuilder builder, Transform colliders, Matrix4x4 frame, Vector3 basePoint)
    {
        Material trunk = Matte(new Color(0.36f, 0.26f, 0.18f));
        Material leaves = Matte(new Color(0.24f, 0.5f, 0.22f));
        Material leavesLight = Matte(new Color(0.34f, 0.6f, 0.26f));
        float height = Range(2.4f, 3.2f);
        float radius = Range(1.1f, 1.5f);
        builder.AddCylinder(frame, basePoint, Vector3.up, 0.14f, height + 0.6f, 6, trunk);
        builder.AddCylinder(frame, basePoint + Vector3.up * height, Vector3.up, radius, 1.5f, 7, leaves);
        builder.AddCylinder(frame, basePoint + Vector3.up * (height + 1.5f), Vector3.up, radius * 0.65f, 0.9f, 7, leavesLight);
        AddCollider(colliders, frame, basePoint + Vector3.up * (height * 0.5f), new Vector3(0.3f, height, 0.3f));
        AddCollider(colliders, frame, basePoint + Vector3.up * (height + 1.2f), new Vector3(radius * 1.6f, 2.4f, radius * 1.6f));
    }

    /// <summary>沿人行道立電線桿，桿頂之間拉三條電線 (飛行時要注意的低空障礙)</summary>
    private void BuildPowerLine(CityMeshBuilder builder, Transform colliders, Matrix4x4 frame, float length, int side)
    {
        float v = side * (roadWidth * 0.5f + 0.5f);
        float[] positions = { 4f, length * 0.5f + 5f, length - 4f };
        Material wire = Matte(new Color(0.08f, 0.08f, 0.09f));
        Vector3? previousTop = null;

        foreach (float u in positions)
        {
            GameObject prefab = PickPrefab(powerPolePrefabs);
            Vector3 world = frame.MultiplyPoint3x4(new Vector3(v, 0f, u));
            float height = 8.5f;
            if (prefab != null)
            {
                PolygonCityModels.Measurement measurement = MeasurePrefab(prefab);
                if (measurement.Valid) height = measurement.Size.y;
                PlaceStreetPrefab(prefab, world, Quaternion.LookRotation(frame.MultiplyVector(Vector3.forward)), 1f);
            }

            Vector3 top = world + Vector3.up * (height - 0.6f);
            if (previousTop.HasValue)
            {
                for (int k = 0; k < 3; k++)
                {
                    Vector3 offset = frame.MultiplyVector(new Vector3((k - 1) * 0.35f, -k * 0.25f, 0f));
                    AddWire(builder, colliders, previousTop.Value + offset, top + offset, 0.35f, wire);
                }
            }
            previousTop = top;
        }
    }

    /// <summary>兩點之間的下垂電線 (以 4 段直線近似)，附細長碰撞體</summary>
    private void AddWire(CityMeshBuilder builder, Transform colliders, Vector3 a, Vector3 b, float sag, Material material)
    {
        const int segments = 4;
        Vector3 previous = a;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            Vector3 point = Vector3.Lerp(a, b, t) + Vector3.down * (sag * 4f * t * (1f - t));
            Vector3 delta = point - previous;
            if (delta.sqrMagnitude > 0.0001f)
            {
                Matrix4x4 wireFrame = Matrix4x4.TRS(previous, Quaternion.LookRotation(delta.normalized), Vector3.one);
                builder.AddBox(wireFrame, new Vector3(0f, 0f, delta.magnitude * 0.5f), new Vector3(0.04f, 0.04f, delta.magnitude), material);

                GameObject wireCollider = new GameObject("WireCollider");
                wireCollider.transform.SetParent(colliders, false);
                wireCollider.transform.SetPositionAndRotation(previous + delta * 0.5f, Quaternion.LookRotation(delta.normalized));
                wireCollider.isStatic = true;
                wireCollider.AddComponent<BoxCollider>().size = new Vector3(0.12f, 0.12f, delta.magnitude);
            }
            previous = point;
        }
    }

    /// <summary>
    /// 放置 LowpolyStreetPack 設施：套用 URP 材質替換、移除內建燈光與光錐、以單一 BoxCollider 取代網格碰撞。
    /// prefab 為 null 時回傳 false。
    /// </summary>
    private bool PlaceStreetPrefab(GameObject prefab, Vector3 position, Quaternion rotation, float scale)
    {
        if (prefab == null) return false;
        if (propRoot == null)
        {
            propRoot = new GameObject("StreetProps").transform;
            propRoot.SetParent(root, false);
        }
        if (remapLookup.Count == 0 && propMaterialRemaps != null)
        {
            foreach (MaterialRemap remap in propMaterialRemaps)
            {
                if (remap.from != null && remap.to != null) remapLookup[remap.from] = remap.to;
            }
        }

        GameObject prop = Instantiate(prefab, position, rotation * prefab.transform.rotation, propRoot);
        prop.name = prefab.name;
        prop.transform.localScale = prefab.transform.localScale * scale;

        foreach (Light light in prop.GetComponentsInChildren<Light>(true)) DestroyNow(light);
        foreach (Collider existing in prop.GetComponentsInChildren<Collider>(true)) DestroyNow(existing);
        foreach (Renderer propRenderer in prop.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = propRenderer.sharedMaterials;
            bool cone = materials.Length > 0;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;
                if (remapLookup.TryGetValue(materials[i], out Material replacement)) materials[i] = replacement;
                if (!materials[i].name.Contains("LightShape")) cone = false;
            }
            propRenderer.sharedMaterials = materials;
            if (cone) propRenderer.enabled = false; // 白天看起來像半透明圓錐
            propRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        FitPropColliders(prop);
        SetStaticRecursively(prop);
        return true;
    }

    /// <summary>
    /// 街道設施的碰撞體改為與模型相同形狀的 MeshCollider (低面數網格)。
    /// 原本以整體包圍盒近似，路燈 (LampPost_D / J 等) 的燈臂伸到馬路上方，方塊會擋住一大片街道。
    /// 光錐 (LightShape) 與已隱藏的網格不產生碰撞。烘焙場景的舊路燈可用同一方法修正。
    /// </summary>
    public static void FitPropColliders(GameObject prop)
    {
        foreach (Collider existing in prop.GetComponentsInChildren<Collider>(true)) DestroyNow(existing);
        foreach (MeshFilter filter in prop.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || mesh.name.Contains("LightShape")) continue;
            Renderer filterRenderer = filter.GetComponent<Renderer>();
            if (filterRenderer != null && !filterRenderer.enabled) continue;

            MeshCollider meshCollider = filter.gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
        }
    }

    private PolygonCityModels.Measurement MeasurePrefab(GameObject prefab)
    {
        if (!modelMeasurements.TryGetValue(prefab, out PolygonCityModels.Measurement measurement))
        {
            measurement = PolygonCityModels.Measure(prefab);
            modelMeasurements[prefab] = measurement;
        }
        return measurement;
    }

    private static void DestroyNow(Object target)
    {
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    /// <summary>點是否落在外環道路以內 (街區範圍)</summary>
    private bool IsInsideCity(Vector3 worldPoint)
    {
        Vector3 local = worldPoint - transform.position;
        float limit = HalfSpan - roadWidth;
        return Mathf.Abs(local.x) <= limit && Mathf.Abs(local.z) <= limit;
    }

    private static void AddCollider(Transform colliders, Matrix4x4 frame, Vector3 localCenter, Vector3 localSize)
    {
        // 街道 frame 只有 90 度倍數的旋轉，換算為世界座標的軸對齊方塊
        Vector3 worldCenter = frame.MultiplyPoint3x4(localCenter);
        Vector3 worldSize = frame.MultiplyVector(localSize);
        worldSize = new Vector3(Mathf.Abs(worldSize.x), Mathf.Abs(worldSize.y), Mathf.Abs(worldSize.z));

        BoxCollider box = colliders.gameObject.AddComponent<BoxCollider>();
        box.center = colliders.InverseTransformPoint(worldCenter);
        box.size = worldSize;
    }

    #endregion

    #region 有貼圖的地面 (道路 / 人行道 / 草地)

    private CityMeshBuilder surfaceBuilder;
    private Transform surfaceColliders;

    private void BeginSurfaces()
    {
        surfaceBuilder = taiwanStreetStyle ? new CityMeshBuilder() : null;
        surfaceColliders = null;
        propRoot = null;
        vegetationRoot = null;
    }

    /// <summary>
    /// 地面類方塊：台灣街景時視覺併入一個網格 (世界座標 UV，貼圖依 tiling 公尺平鋪不拉伸)，碰撞體另外保留；
    /// 否則沿用原本的 Cube。
    /// </summary>
    private void CreateSurface(string surfaceName, Vector3 localPosition, Vector3 size, Material material, Color fallbackColor, float tiling)
    {
        if (surfaceBuilder == null)
        {
            CreateBox(surfaceName, localPosition, size, material, fallbackColor);
            return;
        }

        if (surfaceColliders == null)
        {
            surfaceColliders = new GameObject("SurfaceColliders").transform;
            surfaceColliders.SetParent(root, false);
            surfaceColliders.gameObject.isStatic = true;
        }

        Material surfaceMaterial = material != null ? material : Matte(fallbackColor);
        surfaceBuilder.SetTiling(surfaceMaterial, tiling);
        Vector3 center = transform.position + localPosition;
        surfaceBuilder.AddBox(center, size, surfaceMaterial);

        BoxCollider box = surfaceColliders.gameObject.AddComponent<BoxCollider>();
        box.center = surfaceColliders.InverseTransformPoint(center);
        box.size = size;
    }

    private void EndSurfaces()
    {
        surfaceBuilder?.Build("Surfaces", root);
        surfaceBuilder = null;
    }

    #endregion

    #region 競速路線障礙物

    private static readonly string[] BannerTexts =
    {
        "前方施工 減速慢行", "歡迎光臨", "夜市 往前 200 公尺", "無人機競速大賽", "週年慶 全館八折", "廟會活動 注意安全"
    };

    /// <summary>
    /// 沿固定的競速路線，在每段道路中途放一種障礙 (天橋、跨街電線、布條、施工塔吊、門架標誌)，
    /// 引導玩家穿越、俯衝或爬升。第一段不放，讓起飛後有緩衝。
    /// </summary>
    private void BuildRaceObstacles()
    {
        if (racePath.Count < 2) return;

        Transform obstacleRoot = new GameObject("RaceObstacles").transform;
        obstacleRoot.SetParent(root, false);
        obstacleRoot.gameObject.isStatic = true;
        CityMeshBuilder builder = new CityMeshBuilder();
        System.Random obstacleRandom = new System.Random(seed * 97 + 13);
        int lastType = -1;

        for (int i = 1; i < racePath.Count; i++)
        {
            Vector3 a = racePath[i - 1];
            Vector3 b = racePath[i];
            Vector3 flat = b - a;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1f) continue;

            Vector3 dir = flat.normalized;
            Vector3 mid = new Vector3((a.x + b.x) * 0.5f, transform.position.y, (a.z + b.z) * 0.5f);
            // 以道路段中心為原點、+Z 沿路線方向的 frame
            Matrix4x4 frame = Matrix4x4.TRS(mid, Quaternion.LookRotation(dir), Vector3.one);

            int type = obstacleRandom.Next(5);
            if (type == lastType) type = (type + 1) % 5;
            lastType = type;

            switch (type)
            {
                case 0:
                    if (!BuildOverpass(frame, obstacleRoot)) BuildBanner(builder, obstacleRoot, frame, obstacleRandom);
                    break;
                case 1: BuildCrossWires(builder, obstacleRoot, frame); break;
                case 2: BuildBanner(builder, obstacleRoot, frame, obstacleRandom); break;
                case 3: BuildConstructionCrane(builder, obstacleRoot, frame, obstacleRandom); break;
                default: BuildSignGantry(builder, obstacleRoot, frame); break;
            }
        }

        builder.Build("RaceObstacleMeshes", obstacleRoot);
    }

    /// <summary>行人天橋 (LowpolyStreetPack Overpass)，長軸對齊為橫跨道路</summary>
    private bool BuildOverpass(Matrix4x4 frame, Transform parent)
    {
        GameObject prefab = PickPrefab(overpassPrefabs);
        if (prefab == null) return false;

        PolygonCityModels.Measurement measurement = MeasurePrefab(prefab);
        if (!measurement.Valid) return false;

        bool alongX = measurement.Size.x >= measurement.Size.z;
        float span = Mathf.Max(measurement.Size.x, measurement.Size.z);
        float scale = Mathf.Clamp((roadWidth + 5f) / span, 0.5f, 2f);
        Vector3 across = frame.MultiplyVector(Vector3.right);
        Quaternion rotation = Quaternion.LookRotation(alongX ? Vector3.Cross(across, Vector3.up) : across, Vector3.up);
        Vector3 position = frame.MultiplyPoint3x4(new Vector3(0f, sidewalkHeight, 6f));
        bool placed = PlaceStreetPrefab(prefab, position, rotation, scale);
        if (placed) propRoot.GetChild(propRoot.childCount - 1).SetParent(parent, true);
        return placed;
    }

    /// <summary>跨越道路的電線 (兩側電線桿 + 三條高低不同的電線)</summary>
    private void BuildCrossWires(CityMeshBuilder builder, Transform colliders, Matrix4x4 frame)
    {
        Material wire = Matte(new Color(0.08f, 0.08f, 0.09f));
        Material pole = Matte(new Color(0.55f, 0.56f, 0.55f));
        float half = roadWidth * 0.5f + 0.6f;
        float[] heights = { 6.5f, 8f, 9.5f };
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 basePoint = new Vector3(side * half, sidewalkHeight, 4f);
            builder.AddCylinder(frame, basePoint, Vector3.up, 0.16f, 10.5f, 8, pole);
            builder.AddBox(frame, basePoint + new Vector3(0f, 10f, 0f), new Vector3(1.4f, 0.12f, 0.12f), pole);
            AddCollider(colliders, frame, basePoint + Vector3.up * 5.25f, new Vector3(0.35f, 10.5f, 0.35f));
        }
        foreach (float h in heights)
        {
            Vector3 left = frame.MultiplyPoint3x4(new Vector3(-half, sidewalkHeight + h, 4f));
            Vector3 right = frame.MultiplyPoint3x4(new Vector3(half, sidewalkHeight + h, 4f + (h - 8f) * 0.6f));
            AddWire(builder, colliders, left, right, 0.6f, wire);
        }
    }

    /// <summary>跨街布條：兩支竹竿撐起的紅布 (高度 4.2–5.8m，可從上方或下方穿過)</summary>
    private void BuildBanner(CityMeshBuilder builder, Transform colliders, Matrix4x4 frame, System.Random rng)
    {
        Material pole = Matte(new Color(0.62f, 0.55f, 0.35f));
        Color clothColor = rng.NextDouble() < 0.5 ? new Color(0.78f, 0.1f, 0.1f) : new Color(0.95f, 0.75f, 0.1f);
        Material cloth = Matte(clothColor);
        float half = roadWidth * 0.5f + 0.4f;
        float bottom = 4.2f + (float)rng.NextDouble() * 0.8f;
        const float clothHeight = 1.5f;

        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 basePoint = new Vector3(side * half, sidewalkHeight, -3f);
            builder.AddCylinder(frame, basePoint, Vector3.up, 0.1f, bottom + clothHeight + 0.6f, 6, pole);
            AddCollider(colliders, frame, basePoint + Vector3.up * ((bottom + clothHeight) * 0.5f), new Vector3(0.25f, bottom + clothHeight + 0.6f, 0.25f));
        }

        Vector3 center = new Vector3(0f, sidewalkHeight + bottom + clothHeight * 0.5f, -3f);
        Vector3 size = new Vector3(half * 2f, clothHeight, 0.06f);
        builder.AddBox(frame, center, size, cloth);
        AddCollider(colliders, frame, center, new Vector3(size.x, size.y, 0.3f));

        string text = BannerTexts[rng.Next(BannerTexts.Length)];
        Color textColor = TextColorFor(clothColor);
        CreateSignText(text, frame, center + new Vector3(0f, 0f, -0.05f), Vector3.back, new Vector2(size.x - 1f, clothHeight - 0.3f), textColor);
        CreateSignText(text, frame, center + new Vector3(0f, 0f, 0.05f), Vector3.forward, new Vector2(size.x - 1f, clothHeight - 0.3f), textColor);
    }

    /// <summary>施工塔吊：立在防火巷口，吊臂橫越道路 (高空障礙)；人行道邊放施工圍籬與交通錐</summary>
    private void BuildConstructionCrane(CityMeshBuilder builder, Transform colliders, Matrix4x4 frame, System.Random rng)
    {
        Material yellow = Matte(new Color(0.95f, 0.72f, 0.1f));
        Material concrete = Matte(new Color(0.55f, 0.55f, 0.55f));
        Material fence = Matte(new Color(0.9f, 0.9f, 0.88f));
        Material stripe = Matte(new Color(0.85f, 0.15f, 0.1f));
        int side = rng.NextDouble() < 0.5 ? 1 : -1;
        float mastHeight = 18f + (float)rng.NextDouble() * 6f;
        Vector3 mastBase = new Vector3(side * (roadWidth * 0.5f + 3.6f), sidewalkHeight, 0f);

        // 塔身 (四根立柱 + 斜撐感的橫桿)
        for (int cx = -1; cx <= 1; cx += 2)
        {
            for (int cz = -1; cz <= 1; cz += 2)
            {
                builder.AddBox(frame, mastBase + new Vector3(cx * 0.55f, mastHeight * 0.5f, cz * 0.55f), new Vector3(0.14f, mastHeight, 0.14f), yellow);
            }
        }
        for (float y = 1.5f; y < mastHeight; y += 1.5f)
        {
            builder.AddBox(frame, mastBase + new Vector3(0f, y, 0.55f), new Vector3(1.2f, 0.08f, 0.08f), yellow);
            builder.AddBox(frame, mastBase + new Vector3(0f, y, -0.55f), new Vector3(1.2f, 0.08f, 0.08f), yellow);
            builder.AddBox(frame, mastBase + new Vector3(0.55f, y, 0f), new Vector3(0.08f, 0.08f, 1.2f), yellow);
            builder.AddBox(frame, mastBase + new Vector3(-0.55f, y, 0f), new Vector3(0.08f, 0.08f, 1.2f), yellow);
        }
        AddCollider(colliders, frame, mastBase + Vector3.up * (mastHeight * 0.5f), new Vector3(1.3f, mastHeight, 1.3f));

        // 吊臂往道路另一側延伸、配重臂往後
        float jibLength = roadWidth + 10f;
        Vector3 jibCenter = mastBase + new Vector3(-side * jibLength * 0.5f, mastHeight + 0.5f, 0f);
        builder.AddBox(frame, jibCenter, new Vector3(jibLength, 0.9f, 0.9f), yellow);
        AddCollider(colliders, frame, jibCenter, new Vector3(jibLength, 0.9f, 0.9f));
        Vector3 counterCenter = mastBase + new Vector3(side * 4f, mastHeight + 0.5f, 0f);
        builder.AddBox(frame, counterCenter, new Vector3(8f, 0.7f, 0.7f), yellow);
        builder.AddBox(frame, mastBase + new Vector3(side * 7f, mastHeight - 0.4f, 0f), new Vector3(1.8f, 1.6f, 1.4f), concrete);
        builder.AddBox(frame, mastBase + new Vector3(0f, mastHeight + 1.6f, 0f), new Vector3(1.4f, 1.4f, 1.4f), yellow);

        // 吊鉤垂到半空 (鋼索 + 吊塊)
        Vector3 hookTop = mastBase + new Vector3(-side * (roadWidth * 0.5f + 3.6f), mastHeight, 0f);
        float cable = 6f + (float)rng.NextDouble() * 4f;
        builder.AddBox(frame, hookTop + Vector3.down * (cable * 0.5f), new Vector3(0.05f, cable, 0.05f), concrete);
        builder.AddBox(frame, hookTop + Vector3.down * (cable + 0.6f), new Vector3(1.6f, 1.2f, 1.6f), concrete);
        AddCollider(colliders, frame, hookTop + Vector3.down * (cable + 0.6f), new Vector3(1.6f, 1.2f, 1.6f));

        // 施工圍籬 (人行道外緣)
        for (float u = -4f; u <= 4f; u += 2f)
        {
            Vector3 panel = new Vector3(side * (roadWidth * 0.5f + 0.25f), sidewalkHeight + 0.55f, u);
            builder.AddBox(frame, panel, new Vector3(0.06f, 1.1f, 1.9f), fence);
            builder.AddBox(frame, panel + Vector3.up * 0.2f, new Vector3(0.07f, 0.18f, 1.9f), stripe);
        }
        AddCollider(colliders, frame, new Vector3(side * (roadWidth * 0.5f + 0.25f), sidewalkHeight + 0.55f, 0f), new Vector3(0.2f, 1.1f, 10f));

        if (roadBlockPrefabs != null && roadBlockPrefabs.Length > 0)
        {
            for (float u = -6f; u <= 6f; u += 3f)
            {
                Vector3 cone = frame.MultiplyPoint3x4(new Vector3(side * (roadWidth * 0.5f + 0.8f), sidewalkHeight, u));
                PlaceStreetPrefab(PickPrefab(roadBlockPrefabs), cone, Quaternion.Euler(0f, Range(0f, 360f), 0f), 1f);
            }
        }
    }

    /// <summary>道路門架標誌 (綠底白字，高 5.6m 起)：從下方或上方穿越</summary>
    private void BuildSignGantry(CityMeshBuilder builder, Transform colliders, Matrix4x4 frame)
    {
        Material steel = Matte(new Color(0.6f, 0.62f, 0.64f));
        Color green = new Color(0.05f, 0.42f, 0.22f);
        Material board = Matte(green);
        float half = roadWidth * 0.5f + 0.5f;
        const float beamHeight = 6.4f;

        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 basePoint = new Vector3(side * half, sidewalkHeight, 8f);
            builder.AddBox(frame, basePoint + Vector3.up * (beamHeight * 0.5f + 0.4f), new Vector3(0.35f, beamHeight + 0.8f, 0.35f), steel);
            AddCollider(colliders, frame, basePoint + Vector3.up * (beamHeight * 0.5f + 0.4f), new Vector3(0.35f, beamHeight + 0.8f, 0.35f));
        }
        Vector3 beam = new Vector3(0f, sidewalkHeight + beamHeight + 0.6f, 8f);
        builder.AddBox(frame, beam, new Vector3(half * 2f, 0.35f, 0.35f), steel);
        builder.AddBox(frame, beam + Vector3.up * 0.7f, new Vector3(half * 2f, 0.2f, 0.2f), steel);

        Vector3 boardCenter = new Vector3(roadWidth * 0.2f, sidewalkHeight + beamHeight, 7.75f);
        Vector3 boardSize = new Vector3(6f, 1.8f, 0.12f);
        builder.AddBox(frame, boardCenter, boardSize, board);
        AddCollider(colliders, frame, new Vector3(0f, sidewalkHeight + beamHeight + 0.3f, 8f), new Vector3(half * 2f, 2.4f, 0.6f));
        CreateSignText("市區  ↑", frame, boardCenter + new Vector3(0f, 0f, -0.08f), Vector3.back, new Vector2(5.4f, 1.4f), Color.white);
    }

    #endregion

    #region 騎樓透天厝 / 公寓

    /// <summary>
    /// 街區角落地塊：沿兩側道路蓋一排 L 形連棟街屋 (騎樓 + 鐵窗/陽台)，內側留中庭與鐵皮屋，
    /// 地塊之間的 2.5m 間隙即是防火巷。
    /// </summary>
    private void BuildShophouseLot(CityMeshBuilder builder, Vector3 lotCenter, float lotSize, float targetHeight, int sx, int sz)
    {
        float h = lotSize * 0.5f;
        float depth = Mathf.Min(10f, lotSize * 0.5f);
        int baseFloors = Mathf.Clamp(Mathf.RoundToInt((targetHeight - arcadeHeight) / floorHeight) + 1, 2, 14);

        // A 排：面向 z 側道路，橫跨整個地塊寬
        Vector3 facingA = new Vector3(0f, 0f, sz);
        Vector3 rightA = Vector3.Cross(Vector3.up, facingA);
        Vector3 startA = lotCenter + facingA * h - rightA * h;
        BuildShophouseRow(builder, startA, facingA, lotSize, depth, baseFloors);

        // B 排：面向 x 側道路，長度扣掉 A 排的深度
        float lengthB = lotSize - depth;
        if (lengthB >= 3.5f)
        {
            Vector3 facingB = new Vector3(sx, 0f, 0f);
            Vector3 rightB = Vector3.Cross(Vector3.up, facingB);
            float zInner = lotCenter.z - sz * h;
            float zOuter = lotCenter.z + sz * (h - depth);
            float zStart = zInner * rightB.z < zOuter * rightB.z ? zInner : zOuter;
            Vector3 startB = new Vector3(lotCenter.x + sx * h, lotCenter.y, zStart);
            BuildShophouseRow(builder, startB, facingB, lengthB, depth, baseFloors);
        }

        // 中庭：低矮鐵皮屋或空地
        float yard = lotSize - depth;
        if (yard > 2f && Chance(0.6f))
        {
            Vector3 yardCenter = lotCenter + new Vector3(-sx * depth * 0.5f, 0f, -sz * depth * 0.5f);
            float shedHeight = Range(2.6f, 3.4f);
            Vector3 size = new Vector3(yard - 0.6f, shedHeight, yard - 0.6f);
            builder.AddBox(yardCenter + Vector3.up * (shedHeight * 0.5f), size, Matte(Pick(new[]
            {
                new Color(0.32f, 0.45f, 0.62f), new Color(0.78f, 0.8f, 0.82f), new Color(0.42f, 0.55f, 0.44f)
            })));
            Transform shedCollider = new GameObject("Shophouse_YardShed").transform;
            shedCollider.SetParent(root, false);
            shedCollider.position = yardCenter;
            shedCollider.gameObject.isStatic = true;
            BoxCollider box = shedCollider.gameObject.AddComponent<BoxCollider>();
            box.center = Vector3.up * (shedHeight * 0.5f);
            box.size = size;
        }
    }

    private void BuildShophouseRow(CityMeshBuilder builder, Vector3 start, Vector3 facing, float length, float depth, int baseFloors,
        bool arcade = true)
    {
        Quaternion rotation = Quaternion.LookRotation(facing);
        Matrix4x4 frame = Matrix4x4.TRS(start, rotation, Vector3.one);

        Transform colliderHost = new GameObject("Shophouse_Row").transform;
        colliderHost.SetParent(root, false);
        colliderHost.SetPositionAndRotation(start, rotation);
        colliderHost.gameObject.isStatic = true;

        int units = Mathf.Max(1, Mathf.RoundToInt(length / 5.4f));
        float width = length / units;
        int floors = baseFloors;

        for (int k = 0; k < units; k++)
        {
            // 相鄰街屋常是同一建案 (同樓高)，偶爾不同
            if (k == 0 || Chance(0.45f))
            {
                floors = Mathf.Clamp(baseFloors + random.Next(-1, 2), 2, 15);
            }
            BuildShophouseUnit(builder, colliderHost, frame, k * width, width, depth, floors, k == units - 1, arcade);
        }
    }

    private void BuildShophouseUnit(CityMeshBuilder builder, Transform colliderHost, Matrix4x4 frame,
        float x0, float w, float depth, int floors, bool lastInRow, bool arcade = true)
    {
        float height = arcadeHeight + (floors - 1) * floorHeight;
        float xMid = x0 + w * 0.5f;
        // 沒有騎樓的街屋：店面直接貼齊建築線
        float recess = arcade ? arcadeDepth : 0.15f;

        Color facadeColor = Pick(FacadeColors);
        Material facade = Facade(facadeColor);
        Material column = Facade(facadeColor * 0.85f);
        builder.SetTiling(facade, facadeTileSize);
        builder.SetTiling(column, facadeTileSize);
        Material trim = Matte(new Color(0.88f, 0.88f, 0.86f));
        Material glass = Glass(Chance(0.5f) ? new Color(0.2f, 0.27f, 0.33f) : new Color(0.28f, 0.42f, 0.5f));
        Material metal = Matte(Pick(new[] { new Color(0.76f, 0.77f, 0.79f), new Color(0.9f, 0.9f, 0.9f), new Color(0.42f, 0.34f, 0.28f) }));
        Material concrete = Matte(new Color(0.6f, 0.6f, 0.58f));
        int style = random.Next(3); // 0 鐵窗公寓、1 陽台、2 華廈玻璃帶

        // --- 一樓：騎樓柱 + 店面 ---
        if (arcade)
        {
            builder.AddBox(frame, new Vector3(x0 + 0.25f, arcadeHeight * 0.5f, -0.25f), new Vector3(0.5f, arcadeHeight, 0.5f), column);
            AddLocalCollider(colliderHost, new Vector3(x0 + 0.25f, arcadeHeight * 0.5f, -0.25f), new Vector3(0.5f, arcadeHeight, 0.5f));
        }
        if (arcade && lastInRow)
        {
            builder.AddBox(frame, new Vector3(x0 + w - 0.25f, arcadeHeight * 0.5f, -0.25f), new Vector3(0.5f, arcadeHeight, 0.5f), column);
            AddLocalCollider(colliderHost, new Vector3(x0 + w - 0.25f, arcadeHeight * 0.5f, -0.25f), new Vector3(0.5f, arcadeHeight, 0.5f));
        }

        Vector3 shopCenter = new Vector3(xMid, arcadeHeight * 0.5f, -(depth + recess) * 0.5f);
        Vector3 shopSize = new Vector3(w, arcadeHeight, depth - recess);
        builder.AddBox(frame, shopCenter, shopSize, facade);
        AddLocalCollider(colliderHost, shopCenter, shopSize);

        float shopfront = (float)random.NextDouble();
        Material front = shopfront < 0.5f
            ? RuntimeMaterials.Unlit(Pick(new[] { new Color(0.72f, 0.6f, 0.42f), new Color(0.62f, 0.66f, 0.7f), new Color(0.78f, 0.58f, 0.36f) }))
            : shopfront < 0.8f ? Matte(new Color(0.6f, 0.62f, 0.64f)) : glass;
        builder.AddBox(frame, new Vector3(xMid, (arcadeHeight - 0.5f) * 0.5f, -recess + 0.04f), new Vector3(w - 0.4f, arcadeHeight - 0.5f, 0.06f), front);
        if (shopfront >= 0.5f && shopfront < 0.8f)
        {
            // 鐵捲門橫紋
            Material groove = Matte(new Color(0.48f, 0.5f, 0.52f));
            for (float y = 0.3f; y < arcadeHeight - 0.6f; y += 0.35f)
            {
                builder.AddBox(frame, new Vector3(xMid, y, -recess + 0.08f), new Vector3(w - 0.45f, 0.04f, 0.02f), groove);
            }
        }

        // --- 二樓以上主體 (同時是騎樓頂) ---
        Vector3 bodyCenter = new Vector3(xMid, (arcadeHeight + height) * 0.5f, -depth * 0.5f);
        Vector3 bodySize = new Vector3(w, height - arcadeHeight, depth);
        builder.AddBox(frame, bodyCenter, bodySize, facade);
        AddLocalCollider(colliderHost, bodyCenter, bodySize);
        builder.AddBox(frame, new Vector3(xMid, arcadeHeight + 0.1f, 0.05f), new Vector3(w, 0.2f, 0.12f), trim);
        if (x0 > 0.01f)
        {
            builder.AddBox(frame, new Vector3(x0, (arcadeHeight + height) * 0.5f, 0.04f), new Vector3(0.14f, height - arcadeHeight, 0.1f), trim);
        }

        for (int floor = 1; floor < floors; floor++)
        {
            float baseY = arcadeHeight + (floor - 1) * floorHeight;
            BuildFacadeFloor(builder, frame, x0, w, baseY, style, glass, metal, trim, facade);
        }

        // --- 屋頂：女兒牆、水塔、鐵皮加蓋 ---
        Material parapet = Facade(facadeColor * 0.92f);
        builder.SetTiling(parapet, facadeTileSize);
        builder.AddBox(frame, new Vector3(xMid, height + 0.45f, -0.08f), new Vector3(w, 0.9f, 0.16f), parapet);
        builder.AddBox(frame, new Vector3(xMid, height + 0.45f, -depth + 0.08f), new Vector3(w, 0.9f, 0.16f), parapet);
        builder.AddBox(frame, new Vector3(x0 + 0.08f, height + 0.45f, -depth * 0.5f), new Vector3(0.16f, 0.9f, depth), parapet);
        builder.AddBox(frame, new Vector3(x0 + w - 0.08f, height + 0.45f, -depth * 0.5f), new Vector3(0.16f, 0.9f, depth), parapet);
        AddLocalCollider(colliderHost, new Vector3(xMid, height + 0.45f, -0.08f), new Vector3(w, 0.9f, 0.16f));

        bool shed = Chance(0.32f);
        float tankBase = height;
        if (shed)
        {
            float shedDepth = depth * 0.45f;
            Vector3 shedCenter = new Vector3(xMid, height + 1.2f, -depth + shedDepth * 0.5f + 0.2f);
            Vector3 shedSize = new Vector3(w - 0.4f, 2.4f, shedDepth);
            builder.AddBox(frame, shedCenter, shedSize, Matte(new Color(0.82f, 0.84f, 0.86f)));
            builder.AddBox(frame, shedCenter + new Vector3(0f, 1.24f, 0.1f), new Vector3(w - 0.2f, 0.08f, shedDepth + 0.4f),
                Matte(Pick(new[] { new Color(0.3f, 0.45f, 0.65f), new Color(0.4f, 0.56f, 0.45f), new Color(0.7f, 0.3f, 0.25f) })));
            AddLocalCollider(colliderHost, shedCenter, shedSize);
            tankBase = height + 2.48f;
        }

        if (Chance(0.6f))
        {
            Vector3 tankPos = new Vector3(x0 + w - 1.05f, tankBase, -depth + 1.1f);
            builder.AddBox(frame, tankPos + Vector3.up * 0.25f, new Vector3(1.2f, 0.5f, 1.2f), concrete);
            Material tankMaterial = Chance(0.5f) ? Matte(new Color(0.2f, 0.42f, 0.78f)) : Glass(new Color(0.82f, 0.84f, 0.88f));
            builder.AddCylinder(frame, tankPos + Vector3.up * 0.5f, Vector3.up, 0.55f, 1.2f, 10, tankMaterial);
            AddLocalCollider(colliderHost, tankPos + Vector3.up * 0.85f, new Vector3(1.2f, 1.7f, 1.2f));
        }

        // 屋頂前半部作為送貨降落點
        if (floors >= 2 && w >= 4f)
        {
            rooftopSpots.Add(frame.MultiplyPoint3x4(new Vector3(xMid, height + 0.02f, -depth * 0.28f)));
        }

        // --- 招牌 ---
        if (Chance(0.6f))
        {
            BuildHorizontalSign(builder, frame, xMid, w);
        }
        if (floors >= 3 && Chance(0.5f))
        {
            BuildVerticalSign(builder, colliderHost, frame, x0 + 0.4f, height);
        }
    }

    private void BuildFacadeFloor(CityMeshBuilder builder, Matrix4x4 frame, float x0, float w, float baseY, int style,
        Material glass, Material metal, Material trim, Material facade)
    {
        float xMid = x0 + w * 0.5f;
        float windowWidth = w * 0.72f;

        switch (style)
        {
            case 0: // 鐵窗：玻璃 + 外推鐵窗框
            {
                builder.AddBox(frame, new Vector3(xMid, baseY + 1.6f, 0.025f), new Vector3(windowWidth, 1.5f, 0.05f), glass);
                float cageWidth = windowWidth + 0.2f;
                builder.AddBox(frame, new Vector3(xMid, baseY + 0.82f, 0.2f), new Vector3(cageWidth, 0.07f, 0.4f), metal);
                builder.AddBox(frame, new Vector3(xMid, baseY + 2.42f, 0.2f), new Vector3(cageWidth, 0.07f, 0.4f), metal);
                builder.AddBox(frame, new Vector3(xMid, baseY + 1.62f, 0.39f), new Vector3(cageWidth, 0.04f, 0.04f), metal);
                int bars = Mathf.Clamp(Mathf.RoundToInt(cageWidth / 0.5f), 3, 8);
                for (int i = 0; i <= bars; i++)
                {
                    float x = xMid - cageWidth * 0.5f + cageWidth * i / bars;
                    builder.AddBox(frame, new Vector3(x, baseY + 1.62f, 0.39f), new Vector3(0.035f, 1.6f, 0.035f), metal);
                }
                break;
            }
            case 1: // 陽台：樓板 + 矮牆 + 落地窗
            {
                builder.AddBox(frame, new Vector3(xMid, baseY + 1.2f, 0.025f), new Vector3(w * 0.8f, 2.2f, 0.05f), glass);
                builder.AddBox(frame, new Vector3(xMid, baseY + 0.08f, 0.5f), new Vector3(w - 0.2f, 0.16f, 1f), trim);
                builder.AddBox(frame, new Vector3(xMid, baseY + 0.62f, 0.96f), new Vector3(w - 0.2f, 0.95f, 0.08f), facade);
                builder.AddBox(frame, new Vector3(x0 + 0.14f, baseY + 0.62f, 0.5f), new Vector3(0.08f, 0.95f, 1f), facade);
                builder.AddBox(frame, new Vector3(x0 + w - 0.14f, baseY + 0.62f, 0.5f), new Vector3(0.08f, 0.95f, 1f), facade);
                break;
            }
            default: // 華廈：橫向玻璃帶
            {
                builder.AddBox(frame, new Vector3(xMid, baseY + 1.55f, 0.025f), new Vector3(w - 0.3f, 1.9f, 0.05f), glass);
                builder.AddBox(frame, new Vector3(xMid, baseY + 0.3f, 0.06f), new Vector3(w, 0.22f, 0.12f), trim);
                break;
            }
        }

        // 冷氣室外機
        if (Chance(0.35f))
        {
            float x = Chance(0.5f) ? x0 + 0.5f : x0 + w - 0.5f;
            builder.AddBox(frame, new Vector3(x, baseY + 0.45f, 0.22f), new Vector3(0.75f, 0.55f, 0.32f), Matte(new Color(0.86f, 0.86f, 0.84f)));
        }
    }

    private void BuildHorizontalSign(CityMeshBuilder builder, Matrix4x4 frame, float xMid, float w)
    {
        Color background = Pick(SignColors);
        Vector3 center = new Vector3(xMid, arcadeHeight + 0.55f, 0.16f);
        Vector3 size = new Vector3(w - 0.35f, 0.85f, 0.14f);
        builder.AddBox(frame, center, size, Matte(background));

        CreateSignText(Pick(ShopNames), frame, center + new Vector3(0f, 0f, size.z * 0.5f + 0.02f), Vector3.forward,
            new Vector2(size.x - 0.3f, size.y - 0.15f), TextColorFor(background));
    }

    private void BuildVerticalSign(CityMeshBuilder builder, Transform colliderHost, Matrix4x4 frame, float x, float buildingHeight)
    {
        string name = Pick(ShopNames);
        int chars = Mathf.Min(name.Length, 4);
        float signHeight = Mathf.Min(chars * 1.05f + 0.5f, buildingHeight - arcadeHeight - 1.2f);
        if (signHeight < 1.8f) return;

        Color background = Pick(SignColors);
        float bottom = arcadeHeight + 1.2f;
        Vector3 center = new Vector3(x, bottom + signHeight * 0.5f, 0.8f);
        Vector3 size = new Vector3(0.24f, signHeight, 1.05f);
        builder.AddBox(frame, center, size, Matte(background));
        builder.AddBox(frame, center + new Vector3(0f, 0f, -0.6f), new Vector3(0.08f, signHeight * 0.6f, 0.2f), Matte(new Color(0.3f, 0.3f, 0.32f)));
        AddLocalCollider(colliderHost, center, size);

        string vertical = string.Join("\n", name.Substring(0, chars).ToCharArray());
        Vector2 textArea = new Vector2(size.z - 0.2f, signHeight - 0.3f);
        Color textColor = TextColorFor(background);
        CreateSignText(vertical, frame, center + new Vector3(size.x * 0.5f + 0.02f, 0f, 0f), Vector3.right, textArea, textColor);
        CreateSignText(vertical, frame, center - new Vector3(size.x * 0.5f + 0.02f, 0f, 0f), Vector3.left, textArea, textColor);
    }

    private static Color TextColorFor(Color background)
    {
        float luminance = background.r * 0.3f + background.g * 0.59f + background.b * 0.11f;
        return luminance > 0.65f ? new Color(0.75f, 0.08f, 0.08f) : Color.white;
    }

    /// <summary>建立 3D 招牌文字 (TextMeshPro)，normal 為招牌面朝外的方向 (frame 區域座標)</summary>
    private void CreateSignText(string text, Matrix4x4 frame, Vector3 localPosition, Vector3 normal, Vector2 area, Color color)
    {
        if (signFont == null) signFont = RuntimeUI.ResolveFont();
        if (signRoot == null)
        {
            signRoot = new GameObject("ShopSigns").transform;
            signRoot.SetParent(root, false);
        }

        GameObject signObject = new GameObject("Sign_" + text.Replace("\n", string.Empty));
        signObject.transform.SetParent(signRoot, false);
        signObject.transform.position = frame.MultiplyPoint3x4(localPosition);
        // TextMeshPro 文字正面朝 -Z，讓 -Z 對準招牌外側
        signObject.transform.rotation = Quaternion.LookRotation(-frame.MultiplyVector(normal), frame.MultiplyVector(Vector3.up));

        TextMeshPro label = signObject.AddComponent<TextMeshPro>();
        if (signFont != null) label.font = signFont;
        label.text = text;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.enableAutoSizing = true;
        label.fontSizeMin = 1f;
        label.fontSizeMax = 12f;
        label.lineSpacing = -20f;
        label.rectTransform.sizeDelta = area;
        label.fontStyle = FontStyles.Bold;
        MeshRenderer textRenderer = signObject.GetComponent<MeshRenderer>();
        if (textRenderer != null) textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private static void AddLocalCollider(Transform host, Vector3 localCenter, Vector3 size)
    {
        BoxCollider box = host.gameObject.AddComponent<BoxCollider>();
        box.center = localCenter;
        box.size = size;
    }

    #endregion

    #region 台北式街區：窄巷 + 騎樓

    /// <summary>
    /// 台灣街景街區：中央一條沒有人行道的窄巷 (交錯沿 X / Z)，巷子兩側各兩個地塊。
    /// 地塊面大馬路一側為連續騎樓街屋、面巷子一側為公寓 (多半沒有騎樓)；
    /// 少數地塊使用素材包建築 (市中心較多)，空地則為小公園或停車場。
    /// </summary>
    private void BuildTaiwanBlock(int bx, int bz, Vector3 center, float centerFactor)
    {
        CityMeshBuilder blockMesh = new CityMeshBuilder();
        bool alleyAlongX = ((bx + bz) & 1) == 0;
        Vector3 axisA = alleyAlongX ? Vector3.right : Vector3.forward;   // 沿巷子
        Vector3 axisN = alleyAlongX ? Vector3.forward : Vector3.right;   // 垂直巷子
        const float edge = 2.5f;      // 路緣到建築線 (人行道)
        const float fireLane = 1.5f;  // 防火巷
        float lotDepth = (blockSize - edge * 2f - alleyWidth) * 0.5f;
        float lotLength = (blockSize - edge * 2f - fireLane) * 0.5f;

        BuildAlley(blockMesh, center, axisA, axisN);

        for (int side = -1; side <= 1; side += 2)
        {
            for (int end = -1; end <= 1; end += 2)
            {
                Vector3 lotCenter = center + axisN * (side * (alleyWidth * 0.5f + lotDepth * 0.5f))
                                    + axisA * (end * (fireLane * 0.5f + lotLength * 0.5f)) + Vector3.up * sidewalkHeight;
                Vector3 roadSide = axisN * side + axisA * end;

                if (random.NextDouble() >= buildingDensity)
                {
                    float square = Mathf.Min(lotLength, lotDepth);
                    if (!(Chance(parkingLotChance) && BuildParkingLot(blockMesh, lotCenter, square, Mathf.RoundToInt(roadSide.x == 0f ? 1f : Mathf.Sign(roadSide.x)))))
                    {
                        BuildPocketPark(blockMesh, lotCenter, lotLength, lotDepth, axisA);
                    }
                    streetSpots.Add(lotCenter);
                    continue;
                }

                float heightBias = Mathf.Lerp(0.3f, 1f, centerFactor);
                float targetHeight = Mathf.Lerp(minBuildingHeight, maxBuildingHeight, (float)random.NextDouble() * heightBias);
                // 越高的地塊越可能是素材包大樓 (市中心商辦)，一般地塊以騎樓街屋為主
                float modelChance = Mathf.Lerp(modelBuildingChance * 0.35f, modelBuildingChance, Mathf.InverseLerp(minBuildingHeight, maxBuildingHeight, targetHeight));
                if (Chance(modelChance) && TryPlaceModelBuilding(lotCenter, Mathf.Min(lotLength, lotDepth), targetHeight, new Vector3(Mathf.Sign(roadSide.x), 0f, Mathf.Sign(roadSide.z))))
                {
                    continue;
                }
                BuildAlleyShophouseLot(blockMesh, lotCenter, lotLength, lotDepth, axisN * side, targetHeight);
            }
        }

        blockMesh.Build($"Block_{bx}_{bz}_Shophouses", root);
    }

    /// <summary>地塊：面大馬路一排騎樓街屋、背面 (面巷子) 一排公寓</summary>
    private void BuildAlleyShophouseLot(CityMeshBuilder builder, Vector3 lotCenter, float lotLength, float lotDepth, Vector3 outward, float targetHeight)
    {
        int baseFloors = Mathf.Clamp(Mathf.RoundToInt((targetHeight - arcadeHeight) / floorHeight) + 1, 3, 14);
        float outerDepth = Mathf.Min(10f, lotDepth * 0.55f);
        float innerDepth = lotDepth - outerDepth;

        Vector3 facingOuter = outward.normalized;
        Vector3 right = Vector3.Cross(Vector3.up, facingOuter);
        Vector3 startOuter = lotCenter + facingOuter * (lotDepth * 0.5f) - right * (lotLength * 0.5f);
        BuildShophouseRow(builder, startOuter, facingOuter, lotLength, outerDepth, baseFloors, true);

        if (innerDepth >= 4f)
        {
            Vector3 facingInner = -facingOuter;
            Vector3 rightInner = Vector3.Cross(Vector3.up, facingInner);
            Vector3 startInner = lotCenter + facingInner * (lotDepth * 0.5f) - rightInner * (lotLength * 0.5f);
            int alleyFloors = Mathf.Clamp(baseFloors - 1 - random.Next(3), 3, 7); // 巷內多為 4~5 樓公寓
            BuildShophouseRow(builder, startInner, facingInner, lotLength, innerDepth, alleyFloors, Chance(alleyArcadeChance));
        }
    }

    /// <summary>巷子：柏油路面、紅黃邊線、單側電線桿與交錯的電線、少量路邊停車與盆栽</summary>
    private void BuildAlley(CityMeshBuilder builder, Vector3 center, Vector3 axisA, Vector3 axisN)
    {
        Material asphalt = roadMaterial != null ? roadMaterial : Matte(new Color(0.2f, 0.2f, 0.22f));
        Material red = Matte(new Color(0.8f, 0.12f, 0.1f));
        Material yellow = Matte(new Color(0.95f, 0.75f, 0.12f));
        Material wire = Matte(new Color(0.08f, 0.08f, 0.09f));
        builder.SetTiling(asphalt, 6f);

        Vector3 surfaceCenter = center + Vector3.up * (sidewalkHeight + 0.012f);
        Vector3 alongSize = axisA * blockSize;
        Vector3 acrossSize = axisN * alleyWidth;
        builder.AddBox(surfaceCenter, Abs(alongSize + acrossSize) + Vector3.up * 0.02f, asphalt);

        // 邊線：巷口紅線、其餘黃線
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 lineCenter = surfaceCenter + Vector3.up * 0.012f + axisN * (side * (alleyWidth * 0.5f - 0.3f));
            Vector3 thin = axisN * 0.12f + Vector3.up * 0.01f;
            builder.AddBox(lineCenter, Abs(axisA * (blockSize - 12f)) + Abs(thin), yellow);
            for (int end = -1; end <= 1; end += 2)
            {
                builder.AddBox(lineCenter + axisA * (end * (blockSize * 0.5f - 3f)), Abs(axisA * 6f) + Abs(thin), red);
            }
        }

        streetSpots.Add(center + Vector3.up * sidewalkHeight + axisA * 8f);
        streetSpots.Add(center + Vector3.up * sidewalkHeight - axisA * 8f);

        // 電線桿：沿巷子單側，桿頂相連並有拉到對面建築的電線 (巷弄低空障礙)
        Transform colliders = new GameObject("AlleyColliders").transform;
        colliders.SetParent(root, false);
        colliders.gameObject.isStatic = true;
        int poleSide = Chance(0.5f) ? 1 : -1;
        Vector3? previousTop = null;
        for (float a = -blockSize * 0.5f + 5f; a <= blockSize * 0.5f - 4.9f; a += 11.5f)
        {
            Vector3 basePoint = center + Vector3.up * sidewalkHeight + axisA * a + axisN * (poleSide * (alleyWidth * 0.5f - 0.35f));
            float height = 8.5f;
            GameObject pole = PickPrefab(powerPolePrefabs);
            if (pole != null)
            {
                PolygonCityModels.Measurement measurement = MeasurePrefab(pole);
                if (measurement.Valid) height = measurement.Size.y;
                PlaceStreetPrefab(pole, basePoint, Quaternion.LookRotation(axisA), 1f);
            }
            Vector3 top = basePoint + Vector3.up * (height - 0.6f);
            if (previousTop.HasValue)
            {
                for (int k = 0; k < 3; k++)
                {
                    Vector3 offset = axisN * ((k - 1) * 0.3f) + Vector3.down * (k * 0.3f);
                    AddWire(builder, colliders, previousTop.Value + offset, top + offset, 0.45f, wire);
                }
            }
            // 拉到對面建築外牆的電線
            Vector3 opposite = center + Vector3.up * (sidewalkHeight + Range(5.5f, 7.5f)) + axisA * (a + Range(-3f, 3f))
                               + axisN * (-poleSide * (alleyWidth * 0.5f + 0.1f));
            AddWire(builder, colliders, top + Vector3.down * 0.8f, opposite, 0.3f, wire);
            previousTop = top;
        }

        // 巷內路邊停車 (單側平行停放)
        if (vehiclePrefabs != null && vehiclePrefabs.Length > 0 && Chance(0.55f))
        {
            int cars = 1 + random.Next(2);
            for (int i = 0; i < cars; i++)
            {
                GameObject prefab = PickPrefab(vehiclePrefabs);
                if (prefab == null) continue;
                float a = (i == 0 ? -1f : 1f) * Range(6f, 12f);
                Vector3 position = center + Vector3.up * (sidewalkHeight + 0.02f) + axisA * a + axisN * (-poleSide * (alleyWidth * 0.5f - 1.15f));
                GameObject car = CityTraffic.CreateCarModel(prefab, root, out _, out Vector3 size);
                car.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Chance(0.5f) ? axisA : -axisA));
                BoxCollider box = car.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, size.y * 0.5f, 0f);
                box.size = size;
                SetStaticRecursively(car);
            }
        }

        // 門口盆栽
        for (int i = 0; i < 4; i++)
        {
            float a = Range(-blockSize * 0.4f, blockSize * 0.4f);
            int side = Chance(0.5f) ? 1 : -1;
            Vector3 position = center + Vector3.up * sidewalkHeight + axisA * a + axisN * (side * (alleyWidth * 0.5f - 0.35f));
            PlaceBush(position, Range(0.8f, 1.2f), false);
        }
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    #endregion

    #region 植物 (Anime Nature)

    private Transform vegetationRoot;

    /// <summary>放一棵樹並縮放到指定高度；樹木模型保留自身碰撞體，並加上遠距剔除的 LODGroup</summary>
    private bool PlaceTree(Vector3 position, float height)
    {
        GameObject prefab = PickPrefab(treeModelPrefabs);
        if (prefab == null) return false;
        PolygonCityModels.Measurement measurement = MeasurePrefab(prefab);
        if (!measurement.Valid) return false;
        float scale = height / measurement.Size.y;
        return PlaceVegetation(prefab, position, scale, 0.035f) != null;
    }

    /// <summary>灌木 / 盆栽，size 為縮放後的寬度 (公尺)</summary>
    private bool PlaceBush(Vector3 position, float size, bool withCollider)
    {
        GameObject prefab = PickPrefab(bushPrefabs);
        if (prefab == null) return false;
        PolygonCityModels.Measurement measurement = MeasurePrefab(prefab);
        if (!measurement.Valid) return false;
        float scale = size / Mathf.Max(measurement.Size.x, measurement.Size.z);
        GameObject bush = PlaceVegetation(prefab, position, scale, 0.05f);
        if (bush != null && withCollider)
        {
            Bounds bounds = WorldBounds(bush);
            BoxCollider box = bush.AddComponent<BoxCollider>();
            box.center = bush.transform.InverseTransformPoint(bounds.center);
            box.size = bush.transform.InverseTransformVector(bounds.size * 0.8f);
        }
        return bush != null;
    }

    private void PlaceGrass(Vector3 position, float scale)
    {
        GameObject prefab = PickPrefab(grassPrefabs);
        if (prefab == null) return;
        GameObject grass = PlaceVegetation(prefab, position, scale, 0.08f);
        if (grass == null) return;
        foreach (Renderer grassRenderer in grass.GetComponentsInChildren<Renderer>())
        {
            grassRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    private GameObject PlaceVegetation(GameObject prefab, Vector3 position, float scale, float cullScreenHeight)
    {
        if (vegetationRoot == null)
        {
            vegetationRoot = new GameObject("Vegetation").transform;
            vegetationRoot.SetParent(root, false);
        }

        Quaternion yaw = Quaternion.Euler(0f, Range(0f, 360f), 0f);
        GameObject plant = Instantiate(prefab, position, yaw * prefab.transform.rotation, vegetationRoot);
        plant.name = prefab.name;
        plant.transform.localScale = prefab.transform.localScale * scale;

        // 沒有 LOD 的模型加上 LODGroup：在畫面上很小時不繪製 (樹木面數高)
        if (plant.GetComponent<LODGroup>() == null)
        {
            LODGroup group = plant.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(cullScreenHeight, plant.GetComponentsInChildren<Renderer>()) });
            group.RecalculateBounds();
        }
        else
        {
            // 既有 LOD：只調整最後一級的剔除比例
            LODGroup group = plant.GetComponent<LODGroup>();
            LOD[] lods = group.GetLODs();
            if (lods.Length > 0)
            {
                lods[lods.Length - 1].screenRelativeTransitionHeight = Mathf.Max(lods[lods.Length - 1].screenRelativeTransitionHeight, cullScreenHeight);
                group.SetLODs(lods);
            }
        }
        SetStaticRecursively(plant);
        return plant;
    }

    /// <summary>口袋公園：草地 + 草叢 + 一兩棵樹 + 灌木 + 長椅</summary>
    private void BuildPocketPark(CityMeshBuilder builder, Vector3 lotCenter, float lotLength, float lotDepth, Vector3 axisA)
    {
        Vector3 size = Abs(axisA * lotLength) + Abs(Vector3.Cross(Vector3.up, axisA) * lotDepth) + Vector3.up * 0.04f;
        Material grassMaterial = groundMaterial != null ? groundMaterial : Matte(new Color(0.33f, 0.55f, 0.28f));
        builder.SetTiling(grassMaterial, 8f);
        builder.AddBox(lotCenter + Vector3.up * 0.02f, size, grassMaterial);
        DecoratePark(lotCenter, size.x, size.z);
    }

    /// <summary>在一塊綠地上散佈樹、灌木與草叢</summary>
    private void DecoratePark(Vector3 center, float sizeX, float sizeZ)
    {
        int trees = Mathf.Clamp(Mathf.RoundToInt(sizeX * sizeZ / 160f), 1, 4);
        for (int i = 0; i < trees; i++)
        {
            Vector3 p = center + new Vector3(Range(-0.3f, 0.3f) * sizeX, 0.04f, Range(-0.3f, 0.3f) * sizeZ);
            PlaceTree(p, Range(7f, 10f));
        }
        int bushes = Mathf.Clamp(Mathf.RoundToInt(sizeX * sizeZ / 60f), 2, 8);
        for (int i = 0; i < bushes; i++)
        {
            Vector3 p = center + new Vector3(Range(-0.42f, 0.42f) * sizeX, 0.04f, Range(-0.42f, 0.42f) * sizeZ);
            PlaceBush(p, Range(1.2f, 2.2f), false);
        }
        int grassCount = Mathf.Clamp(Mathf.RoundToInt(sizeX * sizeZ / 25f), 4, 20);
        for (int i = 0; i < grassCount; i++)
        {
            Vector3 p = center + new Vector3(Range(-0.45f, 0.45f) * sizeX, 0.04f, Range(-0.45f, 0.45f) * sizeZ);
            PlaceGrass(p, Range(0.5f, 0.8f));
        }
        if (sidewalkPropPrefabs != null && Chance(0.6f))
        {
            foreach (GameObject prefab in sidewalkPropPrefabs)
            {
                if (prefab != null && prefab.name.StartsWith("Bench"))
                {
                    PlaceStreetPrefab(prefab, center + new Vector3(Range(-0.3f, 0.3f) * sizeX, 0.04f, 0f), Quaternion.Euler(0f, Range(0f, 360f), 0f), 1f);
                    break;
                }
            }
        }
    }

    #endregion

    #region 平面停車場

    /// <summary>
    /// 空地塊改為平面停車場：柏油地面、白色停車格、收費亭，兩排車格停放專案提供的汽車模型。
    /// 沒有車輛模型時回傳 false (改回草地)。
    /// </summary>
    private bool BuildParkingLot(CityMeshBuilder builder, Vector3 lotCenter, float lotSize, int roadSideX)
    {
        if (vehiclePrefabs == null || vehiclePrefabs.Length == 0) return false;

        Material asphalt = Matte(new Color(0.24f, 0.24f, 0.26f));
        Material white = Matte(new Color(0.93f, 0.93f, 0.9f));
        Material booth = Matte(new Color(0.85f, 0.85f, 0.8f));
        Material boothRoof = Matte(new Color(0.15f, 0.45f, 0.3f));
        const float stallWidth = 2.6f;
        const float stallDepth = 5.2f;

        builder.AddBox(lotCenter + Vector3.up * 0.02f, new Vector3(lotSize, 0.04f, lotSize), asphalt);

        Transform lot = new GameObject("ParkingLot").transform;
        lot.SetParent(root, false);
        lot.position = lotCenter;
        lot.gameObject.isStatic = true;

        int stalls = Mathf.FloorToInt((lotSize - 1f) / stallWidth);
        float rowStart = -stalls * stallWidth * 0.5f;
        float half = lotSize * 0.5f;
        for (int row = 0; row < 2; row++)
        {
            int side = row == 0 ? -1 : 1;
            float stallCenterZ = side * (half - 0.5f - stallDepth * 0.5f);
            for (int i = 0; i <= stalls; i++)
            {
                float x = rowStart + i * stallWidth;
                builder.AddBox(lotCenter + new Vector3(x, 0.05f, stallCenterZ), new Vector3(0.1f, 0.01f, stallDepth), white);
            }
            builder.AddBox(lotCenter + new Vector3(0f, 0.05f, stallCenterZ - side * stallDepth * 0.5f), new Vector3(stalls * stallWidth, 0.01f, 0.1f), white);

            for (int i = 0; i < stalls; i++)
            {
                if (!Chance(0.7f)) continue;
                GameObject prefab = PickPrefab(vehiclePrefabs);
                if (prefab == null) continue;

                // 車頭朝走道或朝外 (倒車入庫)，略帶歪斜
                bool noseIn = Chance(0.6f);
                float yaw = (noseIn ? (side > 0 ? 0f : 180f) : (side > 0 ? 180f : 0f)) + Range(-4f, 4f);
                GameObject car = CityTraffic.CreateCarModel(prefab, lot, out _, out Vector3 size);
                car.transform.position = lotCenter + new Vector3(rowStart + (i + 0.5f) * stallWidth, 0.04f, stallCenterZ);
                car.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                foreach (Transform part in car.GetComponentsInChildren<Transform>()) part.gameObject.isStatic = true;

                BoxCollider box = car.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, size.y * 0.5f, 0f);
                box.size = size;
            }
        }

        // 靠道路一側的收費亭
        Vector3 boothCenter = lotCenter + new Vector3(roadSideX * (half - 1.2f), 0f, 0f);
        builder.AddBox(boothCenter + Vector3.up * 1.2f, new Vector3(1.4f, 2.4f, 1.6f), booth);
        builder.AddBox(boothCenter + Vector3.up * 2.5f, new Vector3(1.8f, 0.2f, 2f), boothRoof);
        BoxCollider boothCollider = lot.gameObject.AddComponent<BoxCollider>();
        boothCollider.center = boothCenter - lotCenter + Vector3.up * 1.3f;
        boothCollider.size = new Vector3(1.8f, 2.6f, 2f);
        return true;
    }

    #endregion

    #region 車流與路人

    private void SpawnCityLife()
    {
        if (!Application.isPlaying || !taiwanStreetStyle) return;

        CityGrid grid = Grid;
        int lifeSeed = seed * 17 + 3;

        if (spawnTraffic)
        {
            CityTraffic traffic = root.gameObject.AddComponent<CityTraffic>();
            traffic.Initialize(grid, vehiclePrefabs, carsPerLane, lifeSeed);
        }

        if (spawnPedestrians && pedestrianPrefabs != null && pedestrianPrefabs.Length > 0)
        {
            CityPedestrians pedestrians = root.gameObject.AddComponent<CityPedestrians>();
            pedestrians.Initialize(grid, pedestrianPrefabs, pedestriansPerBlock, lifeSeed + 1);
        }
    }

    #endregion
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 空氣牆外圍景觀 (選單：無人機遊戲/12)：在各關卡可遊玩範圍 (MapBoundary / City2 BoundWall) 外面建立
/// 低多邊形地形 (近郊平地 → 丘陵 → 山脈，部分方向為海岸)、外環道路、郊區房屋/大樓、農田與樹林，讓地圖外不再空蕩。
/// - 純裝飾：不建立碰撞體 (空氣牆已擋住無人機)，網格合併成少數物件，遠處不投影
/// - 物件只放在空氣牆外 ≥12m，不影響場內玩法
/// - 地形最遠距離依場地大小限制在相機遠裁切面 (1000m) 內，避免山被切掉
/// - 可重複執行：每次刪除舊的「OutskirtsScenery」與其網格資產後重建 (固定亂數種子，結果相同)
/// </summary>
public static class OutskirtsSceneryBuilder
{
    public const string RootName = "OutskirtsScenery";
    private const string MaterialFolder = "Assets/Materials/Outskirts";
    private const string MeshRoot = "Assets/GeneratedLevels";
    private const string SurfaceFolder = "Assets/Materials/OrganicCity";
    private const float WallClearance = 12f;

    public static readonly string[] ScenePaths =
    {
        "Assets/Scenes/SampleScene.unity",
        "Assets/Scenes/City2.unity",
        "Assets/Scenes/CityResidential.unity",
        "Assets/Scenes/CityDowntown.unity",
        "Assets/Scenes/StrikePort.unity"
    };

    #region 場景設定

    /// <summary>海岸/陸地扇區：中心方位角 (度，0 = +X、90 = +Z)、半寬 (度)、海岸線距離空氣牆 (m)</summary>
    private struct Sector
    {
        public float Angle, HalfWidth, Coast;
        public Sector(float angle, float halfWidth, float coast) { Angle = angle; HalfWidth = halfWidth; Coast = coast; }
    }

    private class Profile
    {
        public int Seed = 1;
        public bool SeaDefault;                 // true：預設為海，扇區內才是陸地 (港區)
        public Sector[] Sectors = new Sector[0];
        public bool AddWater = true;            // 場景已有海面時不另建
        public float WaterLevel = -1.2f;        // 相對地面高度
        public float BaseOffset = -0.3f;        // 外圍地面相對場內地面的高度 (略低，避免與場內地面重疊閃爍)
        public float MountainHeight = 200f;
        public float MountainStart = 0.38f;     // 佔地形最遠距離的比例
        public float MountainPeak = 0.78f;
        public float HillHeight = 9f;
        /// <summary>地形改用 Unity Terrain (高度圖 + PBR 圖層混合)，取代低多邊形的平面著色網格</summary>
        public bool UnityTerrain;
        /// <summary>直線海岸 (世界 Z)：大於此值為海、小於為陸地 (港區碼頭)；離場地越遠海岸線越彎曲。NaN = 不使用</summary>
        public float CoastZ = float.NaN;
        /// <summary>從空氣牆上這些點 (XZ) 往外直直延伸的聯外道路 (例如港區大門)</summary>
        public Vector2[] GateRoads = new Vector2[0];

        public bool RingRoad = true;
        public float RoadOffset = 18f;
        public float RoadWidth = 9f;
        public int RadialRoads = 6;

        public int Buildings;
        public float BuildMin = 24f, BuildMax = 200f;
        public Vector2 NearHeight = new Vector2(9f, 20f), FarHeight = new Vector2(6f, 12f);
        public Vector2 Footprint = new Vector2(8f, 18f);
        public float TankChance = 0.5f;

        public int Fields;
        public float FieldMin = 80f, FieldMax = 260f;
        public int NearTrees = 150;
        public int HillTrees = 1000;
    }

    private static Profile ProfileFor(string sceneName)
    {
        switch (sceneName)
        {
            case "CityResidential": // 台北盆地式：四周低矮透天厝與公寓 → 農田 → 環繞的山
                return new Profile
                {
                    Seed = 11, MountainHeight = 190f, Buildings = 240, BuildMax = 230f,
                    NearHeight = new Vector2(9f, 22f), FarHeight = new Vector2(6f, 13f), TankChance = 0.65f,
                    Fields = 24, FieldMin = 120f, FieldMax = 300f, NearTrees = 170, HillTrees = 1100
                };
            case "CityDowntown": // 市中心外圍仍是高樓，南側為海灣
                return new Profile
                {
                    Seed = 22, Sectors = new[] { new Sector(-90f, 48f, 70f) }, MountainHeight = 230f,
                    MountainStart = 0.45f, MountainPeak = 0.85f, Buildings = 230, BuildMax = 230f,
                    NearHeight = new Vector2(26f, 70f), FarHeight = new Vector2(10f, 26f), Footprint = new Vector2(12f, 26f),
                    TankChance = 0.25f, Fields = 6, FieldMin = 180f, FieldMax = 280f, NearTrees = 130, HillTrees = 900
                };
            case "City2": // 中型城鎮，東側臨海
                return new Profile
                {
                    Seed = 33, Sectors = new[] { new Sector(0f, 34f, 110f) }, MountainHeight = 210f,
                    Buildings = 180, BuildMax = 240f, NearHeight = new Vector2(12f, 38f), FarHeight = new Vector2(7f, 16f),
                    Footprint = new Vector2(9f, 20f), Fields = 18, FieldMin = 120f, FieldMax = 320f, NearTrees = 170, HillTrees = 1100
                };
            case "SampleScene": // 訓練場：鄉間農田、零星農舍與森林
                return new Profile
                {
                    Seed = 44, BaseOffset = 0.05f, MountainHeight = 170f, MountainStart = 0.4f, MountainPeak = 0.85f,
                    RoadOffset = 14f, RoadWidth = 6f, RadialRoads = 4,
                    Buildings = 30, BuildMin = 30f, BuildMax = 280f, NearHeight = new Vector2(6f, 10f),
                    FarHeight = new Vector2(5f, 8f), Footprint = new Vector2(8f, 13f), TankChance = 0.35f,
                    Fields = 46, FieldMin = 20f, FieldMax = 300f, NearTrees = 200, HillTrees = 1400
                };
            case "StrikePort": // 沿海港區：北面是港池與海 (海岸線接碼頭岸壁)，南、東、西為港口城市與工業區，後方丘陵
                return new Profile
                {
                    Seed = 55, AddWater = false, WaterLevel = -2f, BaseOffset = -0.05f, UnityTerrain = true, CoastZ = 78f,
                    GateRoads = new[] { new Vector2(0f, -100f) },
                    MountainHeight = 150f, MountainStart = 0.5f, MountainPeak = 0.9f, HillHeight = 7f, RingRoad = false, RadialRoads = 4,
                    Buildings = 260, BuildMin = 22f, BuildMax = 320f, NearHeight = new Vector2(8f, 26f),
                    FarHeight = new Vector2(7f, 18f), Footprint = new Vector2(10f, 24f), TankChance = 0.55f,
                    Fields = 0, NearTrees = 140, HillTrees = 1000
                };
            case "StrikeFactory": // 內陸工業區：周圍工業郊區與農地，後方丘陵；南側大門聯外道路
                return new Profile
                {
                    Seed = 66, BaseOffset = -0.05f, UnityTerrain = true, MountainHeight = 160f, MountainStart = 0.5f, MountainPeak = 0.9f,
                    HillHeight = 6f, RoadOffset = 16f, RadialRoads = 4, GateRoads = new[] { new Vector2(-25f, -160f) },
                    Buildings = 180, BuildMin = 30f, BuildMax = 300f, NearHeight = new Vector2(8f, 20f), FarHeight = new Vector2(6f, 14f),
                    Footprint = new Vector2(12f, 28f), TankChance = 0.6f, Fields = 14, FieldMin = 120f, FieldMax = 320f, NearTrees = 160, HillTrees = 1000
                };
            default:
                return new Profile { Buildings = 120, Fields = 10 };
        }
    }

    #endregion

    #region 選單

    [MenuItem("無人機遊戲/12. 建立空氣牆外圍景觀 (所有關卡)", priority = 12)]
    private static void BuildAllFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorUtility.DisplayDialog("外圍景觀", BuildAll(), "確定");
    }

    /// <summary>為所有關卡建立外圍景觀 (完成後重新開啟原本的場景)，回傳摘要</summary>
    public static string BuildAll()
    {
        string previous = SceneManager.GetActiveScene().path;
        List<string> lines = ScenePaths.Select(Build).ToList();
        if (!string.IsNullOrEmpty(previous) && File.Exists(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
        return string.Join("\n", lines);
    }

    /// <summary>開啟場景、重建外圍景觀並存檔</summary>
    public static string Build(string scenePath)
    {
        if (!File.Exists(scenePath)) return $"找不到 {scenePath}";
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        if (!TryFindPlayArea(scene, out Rect area, out float groundY))
        {
            return $"{scene.name}：找不到可遊玩範圍 (CityGenerator / StrikeArena / CourseBuilder / BoundWall)";
        }

        GameObject old = scene.GetRootGameObjects().FirstOrDefault(r => r.name == RootName);
        if (old != null) Object.DestroyImmediate(old);

        string meshFolder = $"{MeshRoot}/{scene.name}/Outskirts";
        EnsureFolder(meshFolder);
        foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { meshFolder }))
        {
            AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
        }

        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        Generator generator = new Generator(ProfileFor(scene.name), area, groundY, meshFolder);
        GameObject root = generator.Run();
        SceneManager.MoveGameObjectToScene(root, scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return $"{scene.name}：範圍 {area.xMin:0}~{area.xMax:0} × {area.yMin:0}~{area.yMax:0}，{generator.Summary} ({watch.ElapsedMilliseconds}ms)";
    }

    /// <summary>空氣牆內側的可遊玩範圍 (XZ) 與場內地面高度，與各關卡建立 MapBoundary 的算式一致</summary>
    private static bool TryFindPlayArea(Scene scene, out Rect area, out float groundY)
    {
        area = default;
        groundY = 0f;
        GameObject[] roots = scene.GetRootGameObjects();

        CityGenerator city = roots.SelectMany(r => r.GetComponentsInChildren<CityGenerator>(true)).FirstOrDefault();
        if (city != null)
        {
            SerializedObject so = new SerializedObject(city);
            int blocks = so.FindProperty("blocks").intValue;
            float blockSize = so.FindProperty("blockSize").floatValue;
            float roadWidth = so.FindProperty("roadWidth").floatValue;
            float limit = (blocks * blockSize + (blocks + 1) * roadWidth) * 0.5f + so.FindProperty("boundaryMargin").floatValue;
            Vector3 center = city.transform.position;
            area = Rect.MinMaxRect(center.x - limit, center.z - limit, center.x + limit, center.z + limit);
            groundY = center.y;
            return true;
        }

        StrikeArena arena = roots.SelectMany(r => r.GetComponentsInChildren<StrikeArena>(true)).FirstOrDefault();
        if (arena != null)
        {
            SerializedObject so = new SerializedObject(arena);
            Vector2 min = so.FindProperty("min").vector2Value;
            Vector2 max = so.FindProperty("max").vector2Value;
            area = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            groundY = so.FindProperty("groundY").floatValue;
            return true;
        }

        CourseBuilder course = roots.SelectMany(r => r.GetComponentsInChildren<CourseBuilder>(true)).FirstOrDefault();
        if (course != null)
        {
            area = Rect.MinMaxRect(-course.boundaryHalfWidth, -course.boundaryEndMargin,
                course.boundaryHalfWidth, course.courseLength + course.boundaryEndMargin);
            return true;
        }

        // City2：場景內手動放置的 BoundWall (四面牆 + 天花板)，取各面牆靠內側的面
        GameObject boundWall = roots.FirstOrDefault(r => r.name == "BoundWall");
        if (boundWall == null) return false;
        List<Bounds> walls = boundWall.GetComponentsInChildren<Collider>(true).Select(c => c.bounds)
            .Where(b => b.size.y > Mathf.Min(b.size.x, b.size.z)) // 排除天花板
            .ToList();
        if (walls.Count < 4) return false;
        Bounds all = walls[0];
        foreach (Bounds b in walls) all.Encapsulate(b);
        float minX = all.min.x, maxX = all.max.x, minZ = all.min.z, maxZ = all.max.z;
        foreach (Bounds b in walls)
        {
            if (b.size.x < b.size.z)
            {
                if (b.center.x < all.center.x) minX = Mathf.Max(minX, b.max.x); else maxX = Mathf.Min(maxX, b.min.x);
            }
            else
            {
                if (b.center.z < all.center.z) minZ = Mathf.Max(minZ, b.max.z); else maxZ = Mathf.Min(maxZ, b.min.z);
            }
        }
        area = Rect.MinMaxRect(minX, minZ, maxX, maxZ);
        return true;
    }

    #endregion

    #region 生成

    private class Generator
    {
        private readonly Profile profile;
        private readonly Rect area;
        private readonly float groundY;
        private readonly string meshFolder;
        private readonly System.Random random;
        private readonly Vector2 center, half;
        private readonly float baseY, waterY, dEnd, mountainStart, mountainPeak;
        private readonly Vector2 noiseOffset;
        private readonly bool hasSea;

        private readonly List<(Vector2 a, Vector2 b, float halfWidth)> roads = new List<(Vector2, Vector2, float)>();
        private readonly List<(Vector2 position, float radius)> occupied = new List<(Vector2, float)>();
        private Transform root;
        private int buildingCount, fieldCount, nearTreeCount, hillTreeCount;

        private Material grass, grassLight, forest, rock, snow, sand, water, glass, trunk, white, tankBlue, roof, asphalt;
        private Material[] facades, crops, leaves, shopfronts;

        public string Summary =>
            $"建築 {buildingCount}、農田 {fieldCount}、近景樹 {nearTreeCount}、山林樹 {hillTreeCount}";

        public Generator(Profile profile, Rect area, float groundY, string meshFolder)
        {
            this.profile = profile;
            this.area = area;
            this.groundY = groundY;
            this.meshFolder = meshFolder;
            random = new System.Random(profile.Seed);
            center = area.center;
            half = area.size * 0.5f;
            baseY = groundY + profile.BaseOffset;
            waterY = groundY + profile.WaterLevel;
            // 從場地另一側看過來也要在 1000m 遠裁切面內
            dEnd = Mathf.Clamp(940f - 2f * Mathf.Max(half.x, half.y), 450f, 800f);
            mountainStart = dEnd * profile.MountainStart;
            mountainPeak = dEnd * profile.MountainPeak;
            noiseOffset = new Vector2(profile.Seed * 37.1f, profile.Seed * 91.7f);
            hasSea = profile.SeaDefault || profile.Sectors.Length > 0 || !float.IsNaN(profile.CoastZ);
        }

        public GameObject Run()
        {
            LoadMaterials();
            root = new GameObject(RootName).transform;

            BuildTerrain();
            if (hasSea && profile.AddWater) BuildWater();
            BuildRoads();
            BuildBuildings();
            BuildFields();
            BuildNearTrees();
            BuildHillTrees();
            return root.gameObject;
        }

        #region 材質

        private void LoadMaterials()
        {
            EnsureFolder(MaterialFolder);
            grass = Mat("Terrain_Grass", new Color(0.42f, 0.55f, 0.28f));
            grassLight = Mat("Terrain_GrassLight", new Color(0.53f, 0.61f, 0.31f));
            forest = Mat("Terrain_Forest", new Color(0.23f, 0.38f, 0.2f));
            rock = Mat("Terrain_Rock", new Color(0.47f, 0.45f, 0.42f));
            snow = Mat("Terrain_Snow", new Color(0.92f, 0.93f, 0.95f));
            sand = Mat("Terrain_Sand", new Color(0.82f, 0.76f, 0.58f));
            glass = Mat("Window_Glass", new Color(0.2f, 0.26f, 0.32f), 0.8f);
            trunk = Mat("Tree_Trunk", new Color(0.36f, 0.26f, 0.18f));
            white = Mat("Tank_White", new Color(0.88f, 0.88f, 0.86f), 0.5f);
            tankBlue = Mat("Tank_Blue", new Color(0.24f, 0.44f, 0.68f), 0.4f);
            crops = new[]
            {
                Mat("Field_Wheat", new Color(0.74f, 0.67f, 0.34f)),
                Mat("Field_Green", new Color(0.36f, 0.53f, 0.22f)),
                Mat("Field_Soil", new Color(0.47f, 0.36f, 0.25f)),
                Mat("Field_Rice", new Color(0.58f, 0.68f, 0.3f))
            };
            leaves = new[]
            {
                Mat("Leaves_Dark", new Color(0.19f, 0.36f, 0.18f)),
                Mat("Leaves_Mid", new Color(0.28f, 0.48f, 0.2f)),
                Mat("Leaves_Pine", new Color(0.16f, 0.3f, 0.2f)),
                Mat("Leaves_Light", new Color(0.46f, 0.56f, 0.22f))
            };

            water = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/StrikePort/Water.mat")
                    ?? Mat("Water", new Color(0.12f, 0.3f, 0.42f), 0.9f);
            roof = Surface("Roof", new Color(0.38f, 0.39f, 0.41f));
            asphalt = Surface("Road_Asphalt", new Color(0.22f, 0.22f, 0.24f));
            facades = new[] { "Building_A", "Building_B", "Building_C", "Building_D", "Building_E", "Building_F", "Building_G" }
                .Select(n => AssetDatabase.LoadAssetAtPath<Material>($"{SurfaceFolder}/{n}.mat"))
                .Where(m => m != null).ToArray();
            // 台灣常見外牆：米白/淺灰磁磚、粉橘、淡藍綠、紅磚
            facades = facades.Concat(new[]
            {
                Mat("Facade_Cream", new Color(0.9f, 0.86f, 0.76f)),
                Mat("Facade_Peach", new Color(0.88f, 0.7f, 0.58f)),
                Mat("Facade_Mint", new Color(0.66f, 0.8f, 0.76f)),
                Mat("Facade_Sky", new Color(0.66f, 0.76f, 0.86f)),
                Mat("Facade_Brick", new Color(0.62f, 0.34f, 0.27f)),
                Mat("Facade_Tile", new Color(0.82f, 0.82f, 0.8f))
            }).ToArray();
            shopfronts = new[]
            {
                Mat("Shop_Red", new Color(0.72f, 0.2f, 0.18f)),
                Mat("Shop_Yellow", new Color(0.9f, 0.72f, 0.2f)),
                Mat("Shop_Teal", new Color(0.15f, 0.5f, 0.52f)),
                Mat("Shop_Dark", new Color(0.2f, 0.2f, 0.22f))
            };
        }

        private static Material Surface(string name, Color fallback)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>($"{SurfaceFolder}/{name}.mat");
            return material != null ? material : Mat(name, fallback);
        }

        private static Material Mat(string name, Color color, float smoothness = 0.1f)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.color = color;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        #endregion

        #region 地形函式

        /// <summary>從場地中心沿方向 dir 到空氣牆的距離</summary>
        private float EdgeDistance(Vector2 dir)
        {
            float tx = Mathf.Abs(dir.x) > 1e-5f ? half.x / Mathf.Abs(dir.x) : float.MaxValue;
            float tz = Mathf.Abs(dir.y) > 1e-5f ? half.y / Mathf.Abs(dir.y) : float.MaxValue;
            return Mathf.Min(tx, tz);
        }

        private Vector2 RayPoint(float angle, float d)
        {
            Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            return center + dir * (EdgeDistance(dir) + d);
        }

        /// <summary>點的方位角與「沿射線」離空氣牆的距離 (地形網格的座標系)</summary>
        private void Polar(Vector2 p, out float angle, out float d)
        {
            Vector2 v = p - center;
            float length = v.magnitude;
            if (length < 1e-3f) { angle = 0f; d = -1000f; return; }
            Vector2 dir = v / length;
            angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            d = length - EdgeDistance(dir);
        }

        /// <summary>點到空氣牆範圍的實際距離 (範圍內為 0)</summary>
        private float WallDistance(Vector2 p)
        {
            float dx = Mathf.Max(area.xMin - p.x, 0f, p.x - area.xMax);
            float dz = Mathf.Max(area.yMin - p.y, 0f, p.y - area.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private float Noise(Vector2 p, float scale)
        {
            return Mathf.PerlinNoise(noiseOffset.x + p.x / scale, noiseOffset.y + p.y / scale);
        }

        private float LandAmount(Vector2 p, float angle, float d)
        {
            if (!float.IsNaN(profile.CoastZ))
            {
                // 場地兩側的海岸線與碼頭岸壁對齊，越往外越彎 (海灣與岬角)
                float offset = Mathf.Max(0f, Mathf.Abs(p.x - center.x) - half.x);
                float bend = Mathf.SmoothStep(0f, 1f, offset / 180f) * (Noise(p, 220f) - 0.35f) * 120f;
                float coast = profile.CoastZ + bend;
                return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(coast - 6f, coast + 6f, p.y));
            }

            float sea = 0f;
            foreach (Sector sector in profile.Sectors)
            {
                float delta = Mathf.Abs(Mathf.DeltaAngle(angle, sector.Angle));
                float inside = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(sector.HalfWidth, sector.HalfWidth + 18f, delta));
                float coast = sector.Coast + (Noise(p, 90f) - 0.5f) * 50f;
                float beyond = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(coast - 20f, coast + 20f, d));
                sea = Mathf.Max(sea, inside * beyond);
            }
            return profile.SeaDefault ? sea : 1f - sea;
        }

        private float Height(Vector2 p)
        {
            Polar(p, out float angle, out float d);
            return Height(p, angle, d);
        }

        private float Height(Vector2 p, float angle, float d)
        {
            float land = LandAmount(p, angle, d);
            float hills = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(60f, 260f, d)) * profile.HillHeight * (Noise(p, 140f) * 2f - 0.3f);
            float ridge = 1f - Mathf.Abs(Noise(p, 230f) * 2f - 1f);
            float mountain = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(mountainStart, mountainPeak, d)) *
                             profile.MountainHeight * (0.5f + 0.5f * ridge) * (0.7f + 0.5f * Noise(p, 420f));
            float landHeight = baseY + Mathf.Max(hills, -1f) + mountain;
            float seaFloor = waterY - 7f;
            return Mathf.Lerp(seaFloor, landHeight, Mathf.SmoothStep(0f, 1f, land));
        }

        private bool IsLand(Vector2 p)
        {
            Polar(p, out float angle, out float d);
            return LandAmount(p, angle, d) > 0.99f;
        }

        private Vector3 Ground(Vector2 p, float lift = 0f)
        {
            return new Vector3(p.x, Height(p) + lift, p.y);
        }

        #endregion

        #region 地形與海

        private List<float> Rings()
        {
            List<float> rings = new List<float> { -3f, 0f };
            float d = 0f;
            while (true)
            {
                d += 8f + d * 0.11f;
                if (d >= dEnd - 10f) break;
                rings.Add(d);
            }
            rings.Add(dEnd);
            return rings;
        }

        private void BuildTerrain()
        {
            if (profile.UnityTerrain)
            {
                BuildUnityTerrain();
                return;
            }
            const int angles = 240;
            List<float> rings = Rings();
            Vector3[,] grid = new Vector3[angles + 1, rings.Count];
            for (int i = 0; i <= angles; i++)
            {
                float angle = -180f + 360f * i / angles;
                for (int j = 0; j < rings.Count; j++)
                {
                    Vector2 p = RayPoint(angle, rings[j]);
                    grid[i, j] = new Vector3(p.x, Height(p, angle, rings[j]), p.y);
                }
            }

            FacetBuilder builder = new FacetBuilder();
            float hidden = waterY - 0.4f;
            for (int i = 0; i < angles; i++)
            {
                for (int j = 0; j < rings.Count - 1; j++)
                {
                    Vector3 a = grid[i, j], b = grid[i + 1, j], c = grid[i + 1, j + 1], e = grid[i, j + 1];
                    if (hasSea && Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, e.y)) < hidden) continue; // 整格在海面下
                    AddTerrainTriangle(builder, a, b, c);
                    AddTerrainTriangle(builder, a, c, e);
                }
            }
            Finish(builder.Build("Terrain", root, false), "Terrain");
        }

        /// <summary>
        /// Unity Terrain 版地形：與網格版使用同一個 <see cref="Height(Vector2)"/>，建築、樹木與離島位置不變。
        /// 正方形範圍涵蓋空氣牆外 dEnd 的距離；場內壓到地面以下避免穿出。
        /// 圖層 (沙、草、森林、岩石、雪) 依高度、坡度與雜訊混合，貼圖由 <see cref="ProceduralTextures"/> 產生。
        /// 純裝飾：不加 TerrainCollider、不投影。
        /// </summary>
        private void BuildUnityTerrain()
        {
            const int resolution = 1025, alphaResolution = 512;
            float extent = Mathf.Max(half.x, half.y) + dEnd;
            float minY = waterY - 8f;
            float sizeY = baseY + profile.MountainHeight * 1.3f + profile.HillHeight + 5f - minY;
            Vector2 origin = center - new Vector2(extent, extent);

            float[,] heights = new float[resolution, resolution];
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    Vector2 p = origin + new Vector2(x, z) * (2f * extent / (resolution - 1));
                    float h = Height(p);
                    // 場內壓到地面以下 (內縮 2m，與場內地面重疊的邊緣略低於地面，不會在空氣牆下露出溝縫)
                    Rect inner = new Rect(area.xMin + 2f, area.yMin + 2f, area.width - 4f, area.height - 4f);
                    if (inner.Contains(p)) h = Mathf.Min(h, groundY - 3f);
                    heights[z, x] = Mathf.Clamp01((h - minY) / sizeY);
                }
            }

            // 先存成資產再寫入高度與圖層混合：混合用的 splat 貼圖是 TerrainData 的子資產，資產建立前寫入的不會被存檔
            string dataPath = $"{meshFolder}/TerrainData.asset";
            AssetDatabase.DeleteAsset(dataPath);
            TerrainData data = new TerrainData { heightmapResolution = resolution };
            data.size = new Vector3(2f * extent, sizeY, 2f * extent);
            AssetDatabase.CreateAsset(data, dataPath);
            data.SetHeights(0, 0, heights);
            data.alphamapResolution = alphaResolution;
            data.baseMapResolution = 1024;
            data.terrainLayers = new[]
            {
                Layer("Sand", ProceduralTextures.TerrainSand(), 6f),
                Layer("Grass", ProceduralTextures.TerrainGrass(), 8f),
                Layer("Forest", ProceduralTextures.TerrainForest(), 14f),
                Layer("Rock", ProceduralTextures.TerrainRock(), 18f),
                Layer("Snow", ProceduralTextures.TerrainSnow(), 10f)
            };

            float[,,] alpha = new float[alphaResolution, alphaResolution, 5];
            float mountain = profile.MountainHeight;
            for (int z = 0; z < alphaResolution; z++)
            {
                for (int x = 0; x < alphaResolution; x++)
                {
                    float u = x / (alphaResolution - 1f), v = z / (alphaResolution - 1f);
                    Vector2 p = origin + new Vector2(u, v) * (2f * extent);
                    float h = data.GetInterpolatedHeight(u, v) + minY;
                    float slope = data.GetSteepness(u, v);
                    float relative = h - baseY;
                    float n = Noise(p, 60f);

                    // 沙灘只在海岸邊：周圍 14m 內有海、且高度接近海面
                    float coastal = 0f;
                    if (hasSea)
                    {
                        foreach (Vector2 offset in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right })
                        {
                            Vector2 q = p + offset * 14f;
                            Polar(q, out float qa, out float qd);
                            coastal = Mathf.Max(coastal, 1f - LandAmount(q, qa, qd));
                        }
                    }
                    float sand = coastal * (1f - Smooth(waterY + 2.5f, waterY + 5f, h));
                    // 亞熱帶海港丘陵：以森林為主，陡坡與高處露出岩石，只有最高的峰頂有少量積雪
                    float rock = Smooth(34f, 50f, slope) + Smooth(mountain * 0.75f, mountain * 0.95f, relative) * n * 0.7f;
                    float snow = Smooth(mountain * 1.05f, mountain * 1.2f, relative + (n - 0.5f) * 15f) * (1f - Smooth(40f, 60f, slope));
                    float forest = Smooth(6f, 22f, relative) * Smooth(0.2f, 0.45f, Noise(p, 45f)) * Mathf.Clamp01(1f - rock);
                    float grass = Mathf.Max(0f, 1f - sand - rock - snow - forest);
                    float total = Mathf.Max(1e-4f, sand + grass + forest + rock + snow);
                    alpha[z, x, 0] = sand / total;
                    alpha[z, x, 1] = grass / total;
                    alpha[z, x, 2] = forest / total;
                    alpha[z, x, 3] = rock / total;
                    alpha[z, x, 4] = snow / total;
                }
            }
            data.SetAlphamaps(0, 0, alpha);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            GameObject terrainObject = new GameObject("Terrain");
            terrainObject.transform.SetParent(root, false);
            terrainObject.transform.position = new Vector3(origin.x, minY, origin.y);
            Terrain terrain = terrainObject.AddComponent<Terrain>();
            terrain.terrainData = data;
            terrain.materialTemplate = TerrainMaterial();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 5f;
            terrain.basemapDistance = 1500f;
            terrain.shadowCastingMode = ShadowCastingMode.Off;
            terrain.treeDistance = 0f;
            terrain.detailObjectDistance = 0f;
            GameObjectUtility.SetStaticEditorFlags(terrainObject, StaticEditorFlags.BatchingStatic);
        }

        private static float Smooth(float from, float to, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));

        private static TerrainLayer Layer(string name, ProceduralTextures.Set set, float tileSize)
        {
            string folder = $"{MaterialFolder}/TerrainLayers";
            EnsureFolder(folder);
            string path = $"{folder}/{name}.terrainlayer";
            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = set.Albedo;
            layer.normalMapTexture = set.Normal;
            layer.normalScale = 1f;
            layer.tileSize = new Vector2(tileSize, tileSize);
            layer.smoothness = name == "Snow" ? 0.35f : 0.05f;
            layer.metallic = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        private static Material TerrainMaterial()
        {
            string path = $"{MaterialFolder}/Terrain_URP.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            Shader shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (shader == null) return null;
            material = new Material(shader) { name = "Terrain_URP" };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private void AddTerrainTriangle(FacetBuilder builder, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 centroid = (a + b + c) / 3f;
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            if (normal.y < 0f) normal = -normal;
            float slope = Vector3.Angle(normal, Vector3.up);
            float relative = centroid.y - baseY;
            Vector2 p = new Vector2(centroid.x, centroid.z);

            Material material;
            if (hasSea && centroid.y < waterY + 0.6f && relative < 1.5f) material = sand;
            else if (relative > profile.MountainHeight * 0.8f) material = snow;
            else if (slope > 46f) material = rock;
            else if (relative > profile.MountainHeight * 0.55f) material = Noise(p, 60f) > 0.5f ? rock : forest;
            else if (relative > 22f) material = Noise(p, 45f) > 0.35f ? forest : grass;
            else material = Noise(p, 70f) > 0.55f ? grassLight : grass;
            builder.AddTriangle(a, b, c, material, Vector3.up);
        }

        /// <summary>海面：只鋪在有海的扇區，延伸到遠裁切面外，地平線不會露出天空盒</summary>
        private void BuildWater()
        {
            const int angles = 120;
            List<float> rings = Rings().Where(d => d > 20f).ToList();
            rings.Insert(0, 0f);
            rings.Add(dEnd + 250f);
            rings.Add(1600f);
            FacetBuilder builder = new FacetBuilder();
            for (int i = 0; i < angles; i++)
            {
                float a0 = -180f + 360f * i / angles, a1 = -180f + 360f * (i + 1) / angles;
                for (int j = 0; j < rings.Count - 1; j++)
                {
                    bool sea = false;
                    foreach (float angle in new[] { a0, a1 })
                    {
                        foreach (float d in new[] { rings[j], rings[j + 1] })
                        {
                            Vector2 sample = RayPoint(angle, d);
                            if (LandAmount(sample, angle, d) < 0.999f) sea = true;
                        }
                    }
                    if (!sea) continue;
                    Vector3 p00 = Flat(RayPoint(a0, rings[j]), waterY), p10 = Flat(RayPoint(a1, rings[j]), waterY);
                    Vector3 p11 = Flat(RayPoint(a1, rings[j + 1]), waterY), p01 = Flat(RayPoint(a0, rings[j + 1]), waterY);
                    builder.AddTriangle(p00, p10, p11, water, Vector3.up);
                    builder.AddTriangle(p00, p11, p01, water, Vector3.up);
                }
            }
            Finish(builder.Build("Sea", root, false), "Sea");
        }

        private static Vector3 Flat(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        #endregion

        #region 道路

        private void BuildRoads()
        {
            CityMeshBuilder builder = new CityMeshBuilder();
            builder.SetTiling(asphalt, 6f);
            float top = Mathf.Max(baseY, groundY) + 0.06f;
            float w = profile.RoadWidth;

            if (profile.RingRoad)
            {
                Rect ring = Rect.MinMaxRect(area.xMin - profile.RoadOffset, area.yMin - profile.RoadOffset,
                    area.xMax + profile.RoadOffset, area.yMax + profile.RoadOffset);
                Vector2[] corners =
                {
                    new Vector2(ring.xMin, ring.yMin), new Vector2(ring.xMax, ring.yMin),
                    new Vector2(ring.xMax, ring.yMax), new Vector2(ring.xMin, ring.yMax)
                };
                for (int i = 0; i < 4; i++)
                {
                    Vector2 a = corners[i], b = corners[(i + 1) % 4];
                    Vector2 mid = (a + b) * 0.5f;
                    bool alongX = Mathf.Abs(a.y - b.y) < 0.01f;
                    Vector3 size = alongX ? new Vector3(Mathf.Abs(b.x - a.x) + w, 0.6f, w) : new Vector3(w, 0.6f, Mathf.Abs(b.y - a.y) + w);
                    if (!IsLand(mid)) continue;
                    builder.AddBox(new Vector3(mid.x, top - 0.3f, mid.y), size, asphalt);
                    roads.Add((a, b, w * 0.5f));
                }
            }

            // 往外延伸的聯外道路 (沿地形起伏，遇到海岸或山腳停止)
            for (int i = 0; i < profile.RadialRoads; i++)
            {
                int side = i % 4;
                Vector2 normal = side == 0 ? Vector2.right : side == 1 ? Vector2.up : side == 2 ? Vector2.left : Vector2.down;
                Vector2 tangent = new Vector2(-normal.y, normal.x);
                float along = Range(-0.75f, 0.75f) * (side % 2 == 0 ? half.y : half.x);
                float edge = side % 2 == 0 ? half.x : half.y;
                Vector2 start = center + normal * (edge + profile.RoadOffset + w * 0.5f) + tangent * along;
                Vector2 previous = start;
                float maxLength = mountainStart * 0.9f;
                for (float t = 12f; t <= maxLength; t += 12f)
                {
                    Vector2 next = start + normal * t + tangent * (Mathf.Sin(t * 0.012f + i) * 10f);
                    if (!IsLand(next) || Height(next) - baseY > 14f) break;
                    Vector3 a = Ground(previous), b = Ground(next);
                    a.y = Mathf.Max(a.y, top); b.y = Mathf.Max(b.y, top);
                    Vector3 mid = (a + b) * 0.5f;
                    Matrix4x4 frame = Matrix4x4.TRS(mid, Quaternion.LookRotation(b - a), Vector3.one);
                    builder.AddBox(frame, new Vector3(0f, -0.3f, 0f), new Vector3(w, 0.6f, Vector3.Distance(a, b) + 0.6f), asphalt);
                    roads.Add((previous, next, w * 0.5f));
                    previous = next;
                }
            }
            // 大門聯外道路：從空氣牆上的點沿牆面法線往外直直延伸，遇到海岸或山腳停止
            foreach (Vector2 gate in profile.GateRoads)
            {
                Vector2 normal = Mathf.Abs(gate.x - center.x) / half.x > Mathf.Abs(gate.y - center.y) / half.y
                    ? new Vector2(Mathf.Sign(gate.x - center.x), 0f)
                    : new Vector2(0f, Mathf.Sign(gate.y - center.y));
                Vector2 previous = gate;
                for (float t = 10f; t <= mountainStart; t += 10f)
                {
                    Vector2 next = gate + normal * t;
                    if (!IsLand(next) || Height(next) - baseY > 14f) break;
                    Vector3 a = Ground(previous), b = Ground(next);
                    a.y = Mathf.Max(a.y, top); b.y = Mathf.Max(b.y, top);
                    Matrix4x4 frame = Matrix4x4.TRS((a + b) * 0.5f, Quaternion.LookRotation(b - a), Vector3.one);
                    builder.AddBox(frame, new Vector3(0f, -0.3f, 0f), new Vector3(w, 0.6f, Vector3.Distance(a, b) + 0.6f), asphalt);
                    roads.Add((previous, next, w * 0.5f));
                    previous = next;
                }
            }
            Finish(builder.Build("Roads", root, false), "Roads");
        }

        private bool TouchesRoad(Vector2 p, float radius)
        {
            foreach (var road in roads)
            {
                if (DistanceToSegment(p, road.a, road.b) < road.halfWidth + radius + 2f) return true;
            }
            return false;
        }

        private bool TouchesOccupied(Vector2 p, float radius, float gap)
        {
            foreach (var item in occupied)
            {
                if ((item.position - p).sqrMagnitude < (item.radius + radius + gap) * (item.radius + radius + gap)) return true;
            }
            return false;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
            return Vector2.Distance(p, a + ab * t);
        }

        #endregion

        #region 建築

        private void BuildBuildings()
        {
            CityMeshBuilder[] quadrants = Enumerable.Range(0, 4).Select(_ => new CityMeshBuilder()).ToArray();
            int attempts = profile.Buildings * 15;
            for (int n = 0; n < attempts && buildingCount < profile.Buildings; n++)
            {
                float angle = Range(-180f, 180f);
                float t = Mathf.Pow((float)random.NextDouble(), 1.5f); // 靠近城市較密
                float d = Mathf.Lerp(profile.BuildMin, profile.BuildMax, t);
                Vector2 p = RayPoint(angle, d);

                bool tall = Mathf.Lerp(profile.NearHeight.y, profile.FarHeight.y, t) > 30f;
                float width = Range(profile.Footprint.x, profile.Footprint.y);
                float depth = Range(profile.Footprint.x, profile.Footprint.y) * (tall ? 1f : Range(0.7f, 1.1f));
                float radius = new Vector2(width, depth).magnitude * 0.5f;
                if (WallDistance(p) - radius < WallClearance) continue;
                if (!IsLand(p) || TouchesRoad(p, radius) || TouchesOccupied(p, radius, 3f)) continue;

                float minH = float.MaxValue, maxH = float.MinValue;
                foreach (Vector2 offset in new[] { Vector2.zero, new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
                {
                    float h = Height(p + Vector2.Scale(offset, new Vector2(width, depth) * 0.5f));
                    minH = Mathf.Min(minH, h);
                    maxH = Mathf.Max(maxH, h);
                }
                if (maxH - minH > 4f || minH < waterY + 0.5f) continue;

                float height = Mathf.Lerp(Range(profile.NearHeight.x, profile.NearHeight.y),
                    Range(profile.FarHeight.x, profile.FarHeight.y), t);
                AddBuilding(quadrants[Quadrant(p)], p, width, depth, Mathf.Max(maxH, groundY), minH - 1f, height);
                occupied.Add((p, radius));
                buildingCount++;
            }
            for (int i = 0; i < 4; i++) Finish(quadrants[i].Build($"Buildings_{i}", root, true), $"Buildings_{i}");
        }

        /// <summary>方塊建築：外牆 + 每層一圈窗帶 + 屋頂；部分加頂樓水塔/樓梯間，高樓加退縮頂層</summary>
        private void AddBuilding(CityMeshBuilder builder, Vector2 p, float width, float depth, float floor, float bottom, float height)
        {
            Material facade = facades[random.Next(facades.Length)];
            float top = floor + height;
            bool grid = random.NextDouble() < 0.55;
            AddBlock(builder, p, width, depth, bottom, floor, top, facade, grid);

            // 一樓店面：顏色較深的帶狀立面 + 招牌
            if (height < 30f && random.NextDouble() < 0.45)
            {
                Material shop = shopfronts[random.Next(shopfronts.Length)];
                builder.AddBox(new Vector3(p.x, floor + 1.6f, p.y), new Vector3(width + 0.3f, 3.2f, depth + 0.3f), glass);
                builder.AddBox(new Vector3(p.x, floor + 3.6f, p.y), new Vector3(width + 0.5f, 0.8f, depth + 0.5f), shop);
            }

            if (height > 30f && random.NextDouble() < 0.5)
            {
                float upper = Range(6f, 14f);
                AddBlock(builder, p, width * 0.7f, depth * 0.7f, top, top, top + upper, facade, grid);
                top += upper;
            }

            if (random.NextDouble() < profile.TankChance)
            {
                Vector3 tank = new Vector3(p.x + Range(-0.25f, 0.25f) * width, top + 0.4f, p.y + Range(-0.25f, 0.25f) * depth);
                builder.AddCylinder(Matrix4x4.identity, tank, Vector3.up, 0.9f, 1.7f, 8, random.NextDouble() < 0.5 ? white : tankBlue);
                builder.AddBox(new Vector3(p.x - width * 0.25f, top + 1.5f, p.y + depth * 0.2f), new Vector3(3f, 2.6f, 3f), facade);
            }
        }

        /// <param name="grid">true：窗帶上再加直向窗間柱，形成格子窗；false：連續橫向窗帶</param>
        private void AddBlock(CityMeshBuilder builder, Vector2 p, float width, float depth, float bottom, float floor, float top, Material facade, bool grid)
        {
            builder.AddBox(new Vector3(p.x, (bottom + top) * 0.5f, p.y), new Vector3(width, top - bottom, depth), facade);
            for (float y = floor + 1.3f; y + 1.4f < top - 0.6f; y += 3.2f)
            {
                builder.AddBox(new Vector3(p.x, y + 0.65f, p.y), new Vector3(width + 0.12f, 1.3f, depth + 0.12f), glass);
            }
            if (grid)
            {
                float columnHeight = top - floor - 1f;
                float y = floor + 1f + columnHeight * 0.5f;
                for (float x = -width * 0.5f + 1.2f; x < width * 0.5f - 0.6f; x += 3f)
                {
                    builder.AddBox(new Vector3(p.x + x, y, p.y), new Vector3(0.7f, columnHeight, depth + 0.3f), facade);
                }
                for (float z = -depth * 0.5f + 1.2f; z < depth * 0.5f - 0.6f; z += 3f)
                {
                    builder.AddBox(new Vector3(p.x, y, p.y + z), new Vector3(width + 0.3f, columnHeight, 0.7f), facade);
                }
            }
            builder.AddBox(new Vector3(p.x, top + 0.2f, p.y), new Vector3(width + 0.3f, 0.4f, depth + 0.3f), roof);
        }

        #endregion

        #region 農田

        private void BuildFields()
        {
            FacetBuilder builder = new FacetBuilder();
            FacetBuilder hedges = new FacetBuilder();
            int attempts = profile.Fields * 20;
            for (int n = 0; n < attempts && fieldCount < profile.Fields; n++)
            {
                Vector2 p = RayPoint(Range(-180f, 180f), Range(profile.FieldMin, profile.FieldMax));
                Vector2 size = new Vector2(Range(26f, 60f), Range(20f, 44f));
                float radius = size.magnitude * 0.5f;
                if (WallDistance(p) - radius < WallClearance) continue;
                if (!IsLand(p) || TouchesRoad(p, radius * 0.8f) || TouchesOccupied(p, radius * 0.8f, 2f)) continue;
                if (Height(p) - baseY > 12f) continue;

                Material crop = crops[random.Next(crops.Length)];
                const int cells = 4;
                Vector3[,] grid = new Vector3[cells + 1, cells + 1];
                for (int i = 0; i <= cells; i++)
                {
                    for (int j = 0; j <= cells; j++)
                    {
                        Vector2 q = p + new Vector2((i / (float)cells - 0.5f) * size.x, (j / (float)cells - 0.5f) * size.y);
                        grid[i, j] = Ground(q, 0.25f);
                        grid[i, j].y = Mathf.Max(grid[i, j].y, groundY + 0.08f);
                    }
                }
                for (int i = 0; i < cells; i++)
                {
                    for (int j = 0; j < cells; j++)
                    {
                        builder.AddTriangle(grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], crop, Vector3.up);
                        builder.AddTriangle(grid[i, j], grid[i + 1, j + 1], grid[i, j + 1], crop, Vector3.up);
                    }
                }

                // 部分田邊種一排樹
                if (random.NextDouble() < 0.45)
                {
                    bool alongX = random.NextDouble() < 0.5;
                    float sign = random.NextDouble() < 0.5 ? -1f : 1f;
                    float length = alongX ? size.x : size.y;
                    for (float s = -length * 0.5f; s <= length * 0.5f; s += Range(5f, 8f))
                    {
                        Vector2 q = p + (alongX ? new Vector2(s, sign * (size.y * 0.5f + 2f)) : new Vector2(sign * (size.x * 0.5f + 2f), s));
                        if (WallDistance(q) < WallClearance) continue;
                        AddProceduralTree(hedges, Ground(q, -0.2f), Range(5f, 8f), false);
                    }
                }
                occupied.Add((p, radius));
                fieldCount++;
            }
            Finish(builder.Build("Fields", root, false), "Fields");
            Finish(hedges.Build("FieldTrees", root, true), "FieldTrees");
        }

        #endregion

        #region 樹木

        /// <summary>近景：城市外圍與道路旁零星的樹 (程序化低面數樹，投影)</summary>
        private void BuildNearTrees()
        {
            FacetBuilder[] quadrants = Enumerable.Range(0, 4).Select(_ => new FacetBuilder()).ToArray();
            int attempts = profile.NearTrees * 12;
            for (int n = 0; n < attempts && nearTreeCount < profile.NearTrees; n++)
            {
                float d = Mathf.Lerp(profile.SeaDefault ? profile.BuildMin - 10f : 6f, profile.BuildMax + 40f, Mathf.Pow((float)random.NextDouble(), 1.3f));
                Vector2 p = RayPoint(Range(-180f, 180f), d);
                if (WallDistance(p) < WallClearance - 4f || !IsLand(p)) continue;
                if (TouchesRoad(p, 1.5f) || TouchesOccupied(p, 2f, 1f)) continue;
                float h = Height(p);
                if (h - baseY > 25f || h < waterY + 0.6f) continue;

                AddProceduralTree(quadrants[Quadrant(p)], new Vector3(p.x, Mathf.Max(h, groundY) - 0.2f, p.y), Range(6f, 11f), random.NextDouble() < 0.3);
                nearTreeCount++;
            }
            for (int i = 0; i < 4; i++) Finish(quadrants[i].Build($"NearTrees_{i}", root, true), $"NearTrees_{i}");
        }

        /// <summary>丘陵與山腳的森林：程序化低面數樹 (針葉/闊葉)，依噪聲聚成林區</summary>
        private void BuildHillTrees()
        {
            FacetBuilder[] quadrants = Enumerable.Range(0, 4).Select(_ => new FacetBuilder()).ToArray();
            int attempts = profile.HillTrees * 8;
            float min = profile.SeaDefault ? profile.BuildMin : profile.BuildMin + 20f;
            for (int n = 0; n < attempts && hillTreeCount < profile.HillTrees; n++)
            {
                Vector2 p = RayPoint(Range(-180f, 180f), Range(min, dEnd * 0.92f));
                if (Noise(p, 110f) < 0.42f || !IsLand(p) || WallDistance(p) < WallClearance) continue;
                float h = Height(p);
                float relative = h - baseY;
                if (relative > profile.MountainHeight * 0.6f || h < waterY + 1f) continue;
                float slope = Mathf.Abs(Height(p + new Vector2(3f, 0f)) - h) + Mathf.Abs(Height(p + new Vector2(0f, 3f)) - h);
                if (slope > 4f || TouchesRoad(p, 1.5f) || TouchesOccupied(p, 2f, 0.5f)) continue;

                float size = Range(8f, 14f) * Mathf.Lerp(1f, 1.6f, Mathf.InverseLerp(150f, dEnd, WallDistance(p))); // 遠處放大，才看得到
                bool conifer = relative > 30f ? random.NextDouble() < 0.75 : random.NextDouble() < 0.35;
                AddProceduralTree(quadrants[Quadrant(p)], new Vector3(p.x, h - 0.3f, p.y), size, conifer);
                hillTreeCount++;
            }
            for (int i = 0; i < 4; i++) Finish(quadrants[i].Build($"HillTrees_{i}", root, false), $"HillTrees_{i}");
        }

        private void AddProceduralTree(FacetBuilder builder, Vector3 basePoint, float height, bool conifer)
        {
            float yaw = Range(0f, 60f);
            builder.AddPrism(basePoint, height * 0.04f, height * 0.3f, 5, trunk, yaw);
            if (conifer)
            {
                Material pine = random.NextDouble() < 0.7 ? leaves[2] : leaves[0];
                builder.AddCone(basePoint + Vector3.up * height * 0.22f, height * 0.26f, height * 0.5f, 6, pine, yaw);
                builder.AddCone(basePoint + Vector3.up * height * 0.5f, height * 0.19f, height * 0.5f, 6, pine, yaw + 30f);
            }
            else
            {
                Material leaf = leaves[random.NextDouble() < 0.15 ? 3 : random.Next(2)];
                builder.AddBlob(basePoint + Vector3.up * height * 0.58f, height * 0.26f, height * 0.5f, 6, leaf, yaw);
            }
        }

        #endregion

        #region 共用

        private float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

        private int Quadrant(Vector2 p)
        {
            Vector2 v = p - center;
            return Mathf.Abs(v.x) * half.y > Mathf.Abs(v.y) * half.x ? (v.x > 0f ? 0 : 2) : (v.y > 0f ? 1 : 3);
        }

        /// <summary>存網格資產、設為靜態批次、移除碰撞</summary>
        private void Finish(GameObject built, string meshName)
        {
            if (built == null) return;
            MeshFilter filter = built.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                filter.sharedMesh.name = meshName;
                AssetDatabase.CreateAsset(filter.sharedMesh, $"{meshFolder}/{meshName}.asset");
            }
            GameObjectUtility.SetStaticEditorFlags(built, StaticEditorFlags.BatchingStatic);
        }

        #endregion
    }

    #endregion

    #region 網格工具

    /// <summary>平面著色 (每個三角形獨立頂點) 的低多邊形網格，每種材質一個子網格；用於地形、田地、程序化樹</summary>
    private class FacetBuilder
    {
        private readonly Dictionary<Material, (List<Vector3> vertices, List<Vector3> normals)> parts =
            new Dictionary<Material, (List<Vector3>, List<Vector3>)>();
        private readonly List<Material> order = new List<Material>();

        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Material material, Vector3 facing)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-8f) return;
            if (Vector3.Dot(normal, facing) < 0f) { (b, c) = (c, b); normal = -normal; }
            normal.Normalize();

            if (!parts.TryGetValue(material, out var part))
            {
                part = (new List<Vector3>(), new List<Vector3>());
                parts[material] = part;
                order.Add(material);
            }
            part.vertices.Add(a); part.vertices.Add(b); part.vertices.Add(c);
            part.normals.Add(normal); part.normals.Add(normal); part.normals.Add(normal);
        }

        /// <summary>直立多角柱 (樹幹)</summary>
        public void AddPrism(Vector3 basePoint, float radius, float height, int sides, Material material, float yaw)
        {
            Vector3 top = basePoint + Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                Vector3 a = basePoint + Ring(i, sides, radius, yaw), b = basePoint + Ring(i + 1, sides, radius, yaw);
                Vector3 outward = (a + b) * 0.5f - basePoint;
                AddTriangle(a, b, b + Vector3.up * height, material, outward);
                AddTriangle(a, b + Vector3.up * height, a + Vector3.up * height, material, outward);
                AddTriangle(top + (a - basePoint), top + (b - basePoint), top, material, Vector3.up);
            }
        }

        /// <summary>圓錐 (針葉樹冠)，含底面</summary>
        public void AddCone(Vector3 basePoint, float radius, float height, int sides, Material material, float yaw)
        {
            Vector3 tip = basePoint + Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                Vector3 a = basePoint + Ring(i, sides, radius, yaw), b = basePoint + Ring(i + 1, sides, radius, yaw);
                AddTriangle(a, b, tip, material, (a + b) * 0.5f - basePoint + Vector3.up * radius * 0.3f);
                AddTriangle(a, b, basePoint, material, Vector3.down);
            }
        }

        /// <summary>上下兩層錐體組成的低多邊形球冠 (闊葉樹冠)</summary>
        public void AddBlob(Vector3 middle, float radius, float height, int sides, Material material, float yaw)
        {
            Vector3 top = middle + Vector3.up * height * 0.6f, bottom = middle + Vector3.down * height * 0.4f;
            Vector3 upperRing = middle + Vector3.up * height * 0.2f;
            for (int i = 0; i < sides; i++)
            {
                Vector3 a = middle + Ring(i, sides, radius, yaw), b = middle + Ring(i + 1, sides, radius, yaw);
                Vector3 ua = upperRing + Ring(i, sides, radius * 0.7f, yaw + 180f / sides);
                Vector3 ub = upperRing + Ring(i + 1, sides, radius * 0.7f, yaw + 180f / sides);
                Vector3 outward = (a + b) * 0.5f - middle;
                AddTriangle(a, b, bottom, material, outward + Vector3.down * radius);
                AddTriangle(a, b, ua, material, outward);
                AddTriangle(b, ub, ua, material, outward + Vector3.up * radius * 0.3f);
                AddTriangle(ua, ub, top, material, Vector3.up + outward * 0.5f);
            }
        }

        private static Vector3 Ring(int i, int sides, float radius, float yaw)
        {
            float angle = (yaw + 360f * i / sides) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        public GameObject Build(string objectName, Transform parent, bool castShadows)
        {
            if (order.Count == 0) return null;
            Mesh mesh = new Mesh { name = objectName, indexFormat = IndexFormat.UInt32 };
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            foreach (Material material in order)
            {
                vertices.AddRange(parts[material].vertices);
                normals.AddRange(parts[material].normals);
            }
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, vertices.Select(v => new Vector2(v.x, v.z) * 0.1f).ToList());
            mesh.subMeshCount = order.Count;
            int offset = 0;
            for (int i = 0; i < order.Count; i++)
            {
                int count = parts[order[i]].vertices.Count;
                mesh.SetTriangles(Enumerable.Range(offset, count).ToArray(), i, false);
                offset += count;
            }
            mesh.RecalculateBounds();
            return CreateObject(objectName, parent, mesh, order.ToArray(), castShadows);
        }
    }

    private static GameObject CreateObject(string objectName, Transform parent, Mesh mesh, Material[] materials, bool castShadows)
    {
        GameObject target = new GameObject(objectName, typeof(MeshFilter), typeof(MeshRenderer));
        target.transform.SetParent(parent, false);
        target.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer meshRenderer = target.GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterials = materials;
        meshRenderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        return target;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    #endregion
}

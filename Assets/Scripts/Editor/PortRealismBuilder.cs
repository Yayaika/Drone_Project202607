using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 港區寫實化 (試做，只影響 StrikePort)：
/// 1. 程序化 PBR 貼圖：貨櫃/倉庫/屋頂改波浪鋼板、地坪改混凝土板、道路柏油、鋼構髒污、海面波紋法線。
///    港區專屬材質直接更新；與其他場景共用的材質 (OrganicCity 的地磚、外牆、屋頂、柏油) 改用港區專屬的新材質替換，不改原檔。
/// 2. 外圍景觀重建，遠山改用 Unity Terrain (<see cref="OutskirtsSceneryBuilder"/> 的 UnityTerrain 設定)。
/// 3. 全域 Volume (ACES 色調映射、調色、Bloom、暗角) + <see cref="SceneRealism"/> 在執行時開啟攝影機後處理。
/// 4. 天空沿用使用者提供的素材天空盒 (Free Stylized Skybox Sky_Anime_03_Day_a)，平行光對準畫中太陽 (見 EnvironmentProfile.skyboxSuns)。
/// 重新執行會重建外圍景觀並覆蓋上述資產；之後重建港區場景 (選單 11) 會自動套回替換材質。
/// </summary>
public static class PortRealismBuilder
{
    private const string MaterialFolder = "Assets/Materials/StrikePort";
    private const string SharedFolder = "Assets/Materials/OrganicCity";
    private const string ProfilePath = "Assets/Settings/StrikePortRealism.asset";
    private const string RootName = "Realism";
    private const string SkyboxPath = "Assets/Download/Free Stylized Skybox/CubeMap/Materials/Sky_Anime_03_Day_a.mat";

    /// <summary>共用材質 → 港區專屬寫實材質</summary>
    private static readonly (string folder, string shared, string replacement)[] Remap =
    {
        (SharedFolder, "Plaza_Tiles", "YardConcrete"),
        (SharedFolder, "Road_Asphalt", "PortAsphalt"),
        (SharedFolder, "Building_B", "Cladding_A"),
        (SharedFolder, "Building_D", "Cladding_B"),
        (SharedFolder, "Roof", "RoofMetal"),
        (MaterialFolder, "Water", "Sea_Realistic")
    };

    [MenuItem("無人機遊戲/15. 港區寫實化 (試做)", priority = 15)]
    private static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorUtility.DisplayDialog("港區寫實化", Build(), "確定");
    }

    public static string Build()
    {
        UpdateMaterials();
        VolumeProfile profile = BuildProfile();
        Material sky = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
        string outskirts = OutskirtsSceneryBuilder.Build(StrikeSceneBuilder.ScenePath);

        Scene scene = EditorSceneManager.OpenScene(StrikeSceneBuilder.ScenePath, OpenSceneMode.Single);
        GameObject level = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "StrikePort_Level");
        int replaced = level != null ? ApplyToLevel(level.transform) : 0;
        string decor = BuildDecor(scene);
        // 環狀車道的車種沿用最新的篩選 (早期建立的場景混入了飛機模型)
        foreach (StrikeVehicleRoute route in Object.FindObjectsByType<StrikeVehicleRoute>(FindObjectsSortMode.None))
        {
            GameObject[] cars = StrikeSceneBuilder.CarPrefabs();
            SerializedObject routeObject = new SerializedObject(route);
            SerializedProperty prefabs = routeObject.FindProperty("carPrefabs");
            prefabs.arraySize = cars.Length;
            for (int i = 0; i < cars.Length; i++) prefabs.GetArrayElementAtIndex(i).objectReferenceValue = cars[i];
            routeObject.ApplyModifiedPropertiesWithoutUndo();
        }

        GameObject old = scene.GetRootGameObjects().FirstOrDefault(root => root.name == RootName);
        if (old != null) Object.DestroyImmediate(old);
        GameObject realism = new GameObject(RootName);
        Volume volume = realism.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1f;
        volume.sharedProfile = profile;
        realism.AddComponent<SceneRealism>();

        if (sky != null) RenderSettings.skybox = sky;
        // 晝夜系統依 RenderSettings.sun 找平行光，並把它對準天空盒畫中的太陽
        Light sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(light => light.type == LightType.Directional);
        if (sun != null)
        {
            RenderSettings.sun = sun;
            sun.shadows = LightShadows.Soft;
        }
        DynamicGI.UpdateEnvironment();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return $"港區寫實化完成：替換 {replaced} 個材質槽。\n{decor}\n{outskirts}";
    }

    /// <summary>把港區場景物件上的共用材質換成港區寫實材質 (寫實材質尚未建立時不動)，回傳替換數</summary>
    internal static int ApplyToLevel(Transform level)
    {
        Dictionary<Material, Material> map = new Dictionary<Material, Material>();
        foreach ((string folder, string shared, string replacement) in Remap)
        {
            Material from = AssetDatabase.LoadAssetAtPath<Material>($"{folder}/{shared}.mat");
            Material to = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{replacement}.mat");
            if (from != null && to != null) map[from] = to;
        }
        if (map.Count == 0) return 0;

        int count = 0;
        foreach (Renderer renderer in level.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null || !map.TryGetValue(materials[i], out Material replacement)) continue;
                materials[i] = replacement;
                changed = true;
                count++;
            }
            if (changed) renderer.sharedMaterials = materials;
        }
        return count;
    }

    #region 港區豐富化

    private const string DecorRoot = "PortDecor";
    private const string StreetProps = "Assets/Download/LowpolyStreetPack/Prefabs/StreetProps";

    /// <summary>
    /// 港區豐富化 (全部在環狀車道外側或港池內，不影響車流、目標點與重新部署點)：
    /// 停靠在岸邊吊車下的貨櫃船、岸壁繫船柱與防舷材、西側貨櫃堆場、東側停車場與貨櫃拖車、南側大門 (警衛亭、柵欄、圍籬、護欄、三角錐)、
    /// 四角高桿照明 (夜間由 StreetLampLights 點亮)。道具使用 Download 內的 LowpolyStreetPack 與 Awbmecreations 車輛。
    /// </summary>
    private static string BuildDecor(Scene scene)
    {
        GameObject old = scene.GetRootGameObjects().FirstOrDefault(root => root.name == DecorRoot);
        if (old != null) Object.DestroyImmediate(old);
        Transform decor = new GameObject(DecorRoot).transform;
        System.Random random = new System.Random(78);

        ProceduralTextures.Set grime = ProceduralTextures.Grime();
        Material hull = Create("ShipHull");
        ProceduralTextures.Apply(hull, grime, 1f, 6f, new Color(0.13f, 0.2f, 0.3f), 0.35f, 0.2f, 0.4f);
        Material bottom = Create("ShipBottom");
        ProceduralTextures.Apply(bottom, grime, 1f, 6f, new Color(0.5f, 0.13f, 0.1f), 0.3f, 0.1f, 0.4f);
        Material glass = Create("Glass_Dark");
        glass.SetColor("_BaseColor", new Color(0.08f, 0.12f, 0.16f));
        glass.SetFloat("_Smoothness", 0.9f);
        Material white = Load("White");
        Material steel = Load("Steel");
        Material hazard = Load("Hazard");
        Material rubber = Load("PadDark");
        Material asphalt = Load("PortAsphalt");
        Material[] containers = new[] { "Container_Red", "Container_Blue", "Container_Green", "Container_Orange", "Container_Grey" }
            .Select(Load).Where(m => m != null).ToArray();
        Material Paint() => containers[random.Next(containers.Length)];

        CityMeshBuilder builder = new CityMeshBuilder();

        // 貨櫃船：船身 (水線 -2)、船底紅漆、艏部漸窄、艉樓 (窗帶) + 煙囪、甲板 1~3 層貨櫃 (在岸邊吊車的吊臂下方)
        const float shipZ = 89f, beam = 13f;
        builder.AddBox(new Vector3(-10f, -1f, shipZ), new Vector3(120f, 12f, beam), hull);
        builder.AddBox(new Vector3(-10f, -2.3f, shipZ), new Vector3(120.1f, 1f, beam + 0.1f), bottom);
        builder.AddBox(new Vector3(53f, -1f, shipZ), new Vector3(6f, 12f, beam * 0.7f), hull);
        builder.AddBox(new Vector3(58f, -0.5f, shipZ), new Vector3(4f, 11f, beam * 0.38f), hull);
        builder.AddBox(new Vector3(-10f, 5.1f, shipZ), new Vector3(118f, 0.2f, beam - 0.6f), steel);
        builder.AddBox(new Vector3(-62f, 12f, shipZ), new Vector3(10f, 14f, beam - 1f), white);
        for (int floor = 0; floor < 4; floor++)
        {
            builder.AddBox(new Vector3(-62f, 8f + floor * 3.2f, shipZ), new Vector3(10.1f, 0.9f, beam - 0.9f), glass);
        }
        builder.AddCylinder(Matrix4x4.identity, new Vector3(-66f, 19f, shipZ), Vector3.up, 1.8f, 6f, 16, hazard);
        for (float x = -50f; x <= 44f; x += 12.6f)
        {
            for (int row = 0; row < 5; row++)
            {
                int tiers = 1 + random.Next(3);
                for (int tier = 0; tier < tiers; tier++)
                {
                    builder.AddBox(new Vector3(x, 6.5f + tier * 2.6f, shipZ - 5f + row * 2.5f), new Vector3(12f, 2.55f, 2.4f), Paint());
                }
            }
        }

        // 岸壁繫船柱與防舷材
        for (float x = -95f; x <= 95f; x += 10f)
        {
            builder.AddCylinder(Matrix4x4.identity, new Vector3(x, 0f, 76.6f), Vector3.up, 0.3f, 0.7f, 10, rubber);
            builder.AddCylinder(Matrix4x4.identity, new Vector3(x, -2.6f, 78.7f), Vector3.up, 0.45f, 2.2f, 10, rubber);
        }

        // 西側貨櫃堆場：兩個區塊各三排，12m 貨櫃沿 Z，1~4 層錯落
        foreach (float blockX in new[] { -93f, -78f })
        {
            for (int column = 0; column < 3; column++)
            {
                float x = blockX + column * 2.6f;
                for (float z = -52f; z <= 52f; z += 13f)
                {
                    if (random.NextDouble() < 0.12) continue;
                    int tiers = 1 + random.Next(4);
                    for (int tier = 0; tier < tiers; tier++)
                    {
                        builder.AddBox(new Vector3(x, 1.3f + tier * 2.6f, z), new Vector3(2.4f, 2.55f, 12f), Paint());
                    }
                }
            }
        }

        // 東側停車場：柏油鋪面 + 車格線；北段為貨櫃拖車
        builder.AddBox(new Vector3(84f, 0.03f, -10f), new Vector3(28f, 0.06f, 90f), asphalt);
        foreach (float rowX in new[] { 74f, 94f })
        {
            for (float z = -52f; z <= 0f; z += 2.8f)
            {
                builder.AddBox(new Vector3(rowX, 0.07f, z), new Vector3(5f, 0.02f, 0.12f), white);
            }
        }
        for (int i = 0; i < 4; i++)
        {
            float z = 10f + i * 7f;
            builder.AddBox(new Vector3(86f, 1.1f, z), new Vector3(12.4f, 0.35f, 2.4f), rubber);                        // 底盤
            builder.AddBox(new Vector3(86f, 2.6f, z), new Vector3(12f, 2.55f, 2.4f), Paint());                         // 貨櫃
            builder.AddBox(new Vector3(78.5f, 1.8f, z), new Vector3(2.4f, 2.9f, 2.4f), i % 2 == 0 ? white : hazard);   // 車頭
            builder.AddBox(new Vector3(77.28f, 2.4f, z), new Vector3(0.05f, 1f, 2f), glass);
            foreach (float wx in new[] { 78.5f, 82f, 90f, 91.4f })
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    builder.AddCylinder(Matrix4x4.identity, new Vector3(wx, 0.5f, z + side * 1.05f), Vector3.forward * side, 0.5f, 0.35f, 12, rubber);
                }
            }
        }

        // 南側大門：警衛亭、抬起的柵欄桿 (不擋起飛)
        builder.AddBox(new Vector3(9f, 1.4f, -80f), new Vector3(3f, 2.8f, 3f), white);
        builder.AddBox(new Vector3(9f, 2.95f, -80f), new Vector3(3.6f, 0.3f, 3.6f), steel);
        builder.AddBox(new Vector3(9f, 1.8f, -80f), new Vector3(3.05f, 0.9f, 3.05f), glass);
        builder.AddBox(new Vector3(5.5f, 0.6f, -76f), new Vector3(0.4f, 1.2f, 0.4f), hazard);
        builder.AddBox(Matrix4x4.TRS(new Vector3(5.5f, 1.2f, -76f), Quaternion.Euler(0f, 0f, 80f), Vector3.one),
            new Vector3(-3.5f, 0f, 0f), new Vector3(7f, 0.12f, 0.12f), hazard);

        // 南側圍籬 (大門處留開口)
        for (float x = -98f; x <= 98f; x += 4f)
        {
            if (Mathf.Abs(x) < 8f) continue;
            builder.AddBox(new Vector3(x, 1.1f, -98.5f), new Vector3(0.1f, 2.2f, 0.1f), steel);
        }
        foreach (float y in new[] { 0.3f, 1.2f, 2.1f })
        {
            builder.AddBox(new Vector3(-53f, y, -98.5f), new Vector3(90f, 0.06f, 0.06f), steel);
            builder.AddBox(new Vector3(53f, y, -98.5f), new Vector3(90f, 0.06f, 0.06f), steel);
        }

        // 四角高桿照明 (25m)：燈架 + 夜間聚光燈
        Transform masts = new GameObject("FloodLights").transform;
        masts.SetParent(decor, false);
        foreach (Vector2 corner in new[] { new Vector2(-70f, -70f), new Vector2(70f, -70f), new Vector2(-70f, 70f), new Vector2(70f, 70f) })
        {
            Vector3 foot = new Vector3(corner.x, 0f, corner.y);
            builder.AddCylinder(Matrix4x4.identity, foot, Vector3.up, 0.35f, 25f, 10, steel);
            builder.AddBox(foot + Vector3.up * 25.3f, new Vector3(3.2f, 0.6f, 1.2f), steel);
            builder.AddBox(foot + Vector3.up * 24.95f, new Vector3(3f, 0.1f, 1f), white);
            Light flood = new GameObject("NightLamp_Flood").AddComponent<Light>();
            flood.transform.SetParent(masts, false);
            flood.transform.position = foot + Vector3.up * 24.5f;
            flood.transform.rotation = Quaternion.LookRotation(new Vector3(-corner.x, -40f, -corner.y).normalized);
            flood.type = LightType.Spot;
            flood.spotAngle = 95f;
            flood.innerSpotAngle = 60f;
            flood.range = 110f;
            flood.intensity = 600f;   // 25m 高，照到地面約 30~60m
            flood.color = new Color(1f, 0.9f, 0.75f);
            flood.shadows = LightShadows.None;
        }

        GameObject structures = builder.Build("PortStructures", decor);
        MeshCollider meshCollider = structures.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = structures.GetComponent<MeshFilter>().sharedMesh;
        const string meshPath = "Assets/GeneratedLevels/StrikePort/PortDecor.asset";
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(structures.GetComponent<MeshFilter>().sharedMesh, meshPath);

        // 停車場車輛 (Awbmecreations)
        Transform cars = new GameObject("ParkedCars").transform;
        cars.SetParent(decor, false);
        GameObject[] carPrefabs = StrikeSceneBuilder.CarPrefabs();
        int parked = 0;
        foreach ((float rowX, float yaw) in new[] { (74f, 90f), (94f, 270f) })
        {
            for (float z = -50.6f; z <= -1f; z += 2.8f)
            {
                if (carPrefabs.Length == 0 || random.NextDouble() < 0.3) continue;
                GameObject car = CityTraffic.CreateCarModel(carPrefabs[random.Next(carPrefabs.Length)], cars, out _, out Vector3 size);
                car.transform.SetPositionAndRotation(new Vector3(rowX, 0.06f, z), Quaternion.Euler(0f, yaw, 0f));
                BoxCollider box = car.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, size.y * 0.5f, 0f);
                box.size = size;
                car.isStatic = true;
                parked++;
            }
        }

        // Download 街道道具：大門兩側紐澤西護欄、三角錐、配電箱
        Transform props = new GameObject("StreetProps").transform;
        props.SetParent(decor, false);
        Dictionary<Material, Material> remap = CityLevelBaker.StreetPackUrpRemap();
        for (float z = -96f; z <= -70f; z += 2.6f)
        {
            PlaceProp($"{StreetProps}/RoadBlocks/RoadBlock_A.prefab", props, remap, new Vector3(-6.5f, 0f, z), 0f);
            if (z < -84f) PlaceProp($"{StreetProps}/RoadBlocks/RoadBlock_A.prefab", props, remap, new Vector3(6.5f, 0f, z), 0f);
        }
        for (int i = 0; i < 5; i++)
        {
            PlaceProp($"{StreetProps}/RoadBlocks/RoadCone_A.prefab", props, remap, new Vector3(3f + i * 0.9f, 0f, -73f), 0f);
        }
        foreach (Vector3 spot in new[] { new Vector3(-52f, 0f, 9f), new Vector3(29f, 0f, 9f), new Vector3(31f, 0f, -44f), new Vector3(12f, 0f, -77f) })
        {
            PlaceProp($"{StreetProps}/FuseBox/FuseBox.prefab", props, remap, spot, 0f);
        }

        return $"港區豐富化：貨櫃船、貨櫃堆場、停車場 {parked} 輛車、貨櫃拖車 4、大門、高桿照明 4";
    }

    private static void PlaceProp(string path, Transform parent, Dictionary<Material, Material> remap, Vector3 position, float yaw)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) return;
        GameObject prop = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        prop.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f) * prefab.transform.rotation);
        foreach (Renderer renderer in prop.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null && remap.TryGetValue(materials[i], out Material replacement)) materials[i] = replacement;
            }
            renderer.sharedMaterials = materials;
        }
        CityGenerator.FitPropColliders(prop);
    }

    #endregion

    #region 材質

    private static void UpdateMaterials()
    {
        ProceduralTextures.Set corrugated = ProceduralTextures.Corrugated();
        ProceduralTextures.Set concrete = ProceduralTextures.Concrete();
        ProceduralTextures.Set asphalt = ProceduralTextures.Asphalt();
        ProceduralTextures.Set grime = ProceduralTextures.Grime();
        ProceduralTextures.Set water = ProceduralTextures.Water();

        // 港區專屬材質：CityMeshBuilder 預設 UV = 1 公尺
        foreach (string name in new[] { "Container_Red", "Container_Blue", "Container_Green", "Container_Orange", "Container_Grey" })
        {
            Material container = Load(name);
            if (container != null) ProceduralTextures.Apply(container, corrugated, 1f, 2.4f, null, 0.35f, 0.3f, 0.6f);
        }
        // 鋼構用 Download 素材 (WarFX 示範場景的金屬貼圖 + 法線)，找不到時退回程序化髒污
        ProceduralTextures.Set metal = ProceduralTextures.FromAssets("Assets/Download/JMO Assets/WarFX/Demo/Assets/WFX_Demo_T_Metal.tga",
            "Assets/Download/JMO Assets/WarFX/Demo/Assets/WFX_Demo_T_Metal Normal.tga") ?? grime;
        ProceduralTextures.Apply(Load("Steel"), metal, 1f, 2f, null, 0.45f, 0.5f, 0.8f);
        ProceduralTextures.Apply(Load("Tank"), grime, 1f, 6f, null, 0.5f, 0.2f, 0.4f);
        ProceduralTextures.Apply(Load("Hazard"), grime, 1f, 2f, null, 0.3f, 0.1f, 0.5f);
        ProceduralTextures.Apply(Load("PadDark"), asphalt, 1f, 6f, new Color(0.55f, 0.55f, 0.57f), 0.2f, 0f, 0.6f);

        // 取代共用材質的新材質：UV 單位依 StrikeSceneBuilder 的 SetTiling (地坪/外牆/屋頂 4m、柏油 6m)
        ProceduralTextures.Apply(Create("YardConcrete"), concrete, 4f, 8f, Color.white, 0.15f, 0f, 0.8f);
        ProceduralTextures.Apply(Create("PortAsphalt"), asphalt, 6f, 6f, Color.white, 0.2f, 0f, 0.8f);
        ProceduralTextures.Apply(Create("Cladding_A"), corrugated, 4f, 3f, new Color(0.62f, 0.66f, 0.7f), 0.35f, 0.4f, 0.7f);
        ProceduralTextures.Apply(Create("Cladding_B"), corrugated, 4f, 3f, new Color(0.7f, 0.6f, 0.5f), 0.35f, 0.4f, 0.7f);
        ProceduralTextures.Apply(Create("RoofMetal"), corrugated, 4f, 3f, new Color(0.42f, 0.44f, 0.46f), 0.3f, 0.5f, 0.7f);

        Material sea = Create("Sea_Realistic");
        ProceduralTextures.Apply(sea, water, 1f, 10f, new Color(0.05f, 0.16f, 0.21f), 0.93f, 0f, 0.6f);
        sea.SetTexture("_BaseMap", null);
        AssetDatabase.SaveAssets();
    }

    private static Material Load(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{name}.mat");

    private static Material Create(string name)
    {
        Material material = Load(name);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        AssetDatabase.CreateAsset(material, $"{MaterialFolder}/{name}.mat");
        return material;
    }

    #endregion

    #region 後處理

    internal static VolumeProfile BuildProfile()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }
        foreach (VolumeComponent component in profile.components.ToList())
        {
            AssetDatabase.RemoveObjectFromAsset(component);
            Object.DestroyImmediate(component, true);
        }
        profile.components.Clear();

        T Add<T>() where T : VolumeComponent
        {
            T component = profile.Add<T>(false);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
        ColorAdjustments color = Add<ColorAdjustments>();
        color.postExposure.Override(0.3f);   // ACES 會壓暗，補一點曝光
        color.contrast.Override(12f);
        color.saturation.Override(-8f);
        Add<WhiteBalance>().temperature.Override(4f);
        Bloom bloom = Add<Bloom>();
        bloom.threshold.Override(1.1f);
        bloom.intensity.Override(0.3f);
        bloom.scatter.Override(0.65f);
        Vignette vignette = Add<Vignette>();
        vignette.intensity.Override(0.18f);
        vignette.smoothness.Override(0.45f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    #endregion
}

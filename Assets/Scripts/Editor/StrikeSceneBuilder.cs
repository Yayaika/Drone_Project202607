using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 建立打擊模式專屬場景「港區・打擊演習」(Assets/Scenes/StrikePort.unity)：
/// 以訓練場 (SampleScene) 的系統物件為基礎 (GameManager、Spawner、HUD、XR)，搭建港區工業場景：
/// 倉庫 (開放式卸貨口)、卸貨頂棚、貨櫃場 (貨櫃跨越走道形成隧道)、油槽、高架管廊、碼頭龍門吊、辦公樓、環狀車道。
/// 目標點位 (StrikeTargetPoint) 與車道 (StrikeVehicleRoute) 為場景物件，可在 Editor 直接移動調整。
/// 重新執行會重建「StrikePort_Level」，其他物件 (含手動調整過的點位以外的系統物件) 保留。
/// </summary>
public static class StrikeSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/StrikePort.unity";
    private const string TemplatePath = "Assets/Scenes/SampleScene.unity";
    private const string LevelRootName = "StrikePort_Level";
    private const string MeshFolder = "Assets/GeneratedLevels/StrikePort";
    private const string MaterialFolder = "Assets/Materials/StrikePort";
    private const string SurfaceFolder = "Assets/Materials/OrganicCity";
    private const string CarFolder = "Assets/Download/Awbmecreations/Mobile Optimize-Free Low Poly Cars/Prefabs";

    private static readonly Vector2 AreaMin = new Vector2(-100f, -100f);
    private static readonly Vector2 AreaMax = new Vector2(100f, 100f);
    private const float QuayZ = 78f;
    private const float LoopHalf = 62f;
    private const float RoadWidth = 9f;

    private static Material concrete, asphalt, wallA, wallB, roof, steel, water, padDark, white, hazard, tankWhite;
    private static Material[] containerColors;

    [MenuItem("無人機遊戲/11. 建立打擊關卡場景 (港區)", priority = 11)]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorUtility.DisplayDialog("打擊關卡場景", Build(), "確定");
    }

    public static string Build()
    {
        bool created = false;
        if (!File.Exists(ScenePath))
        {
            if (!AssetDatabase.CopyAsset(TemplatePath, ScenePath)) return $"無法從 {TemplatePath} 複製場景。";
            created = true;
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (created) RemoveTemplateCourse(scene);

        GameObject old = scene.GetRootGameObjects().FirstOrDefault(root => root.name == LevelRootName);
        if (old != null) Object.DestroyImmediate(old);

        LoadMaterials();
        EnsureFolder(MeshFolder);
        foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { MeshFolder }))
        {
            AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
        }

        Transform level = new GameObject(LevelRootName).transform;
        BuildGround(level);
        BuildWarehouses(level);
        BuildCanopy(level);
        BuildContainerYard(level);
        BuildTanks(level);
        BuildPipeRack(level);
        BuildCranes(level);
        BuildOffice(level);
        PlaceLamps(level);
        BuildTargetPoints(level);
        BuildVehicleRoute(level);

        StrikeArena arena = new GameObject("StrikeArena").AddComponent<StrikeArena>();
        arena.transform.SetParent(level, false);
        arena.Configure(AreaMin, AreaMax, 0f, 60f);

        PlaceSpawner(scene);
        SaveMeshes(level);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AddToBuildSettings();
        return $"已{(created ? "建立" : "重建")} {ScenePath}，並加入 Build Settings。";
    }

    #region 範本與系統物件

    private static void RemoveTemplateCourse(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "Plane" || root.GetComponent<CourseBuilder>() != null) Object.DestroyImmediate(root);
        }
        SpawnCtrl spawner = Object.FindFirstObjectByType<SpawnCtrl>();
        if (spawner != null)
        {
            SerializedObject so = new SerializedObject(spawner);
            so.FindProperty("courseBuilder").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>起飛點：南側入口的停機坪，面向港區 (+Z)</summary>
    private static void PlaceSpawner(Scene scene)
    {
        SpawnCtrl spawner = Object.FindFirstObjectByType<SpawnCtrl>();
        if (spawner == null) return;
        // SpawnCtrl 會再往上抬 0.8m
        Vector3 spawn = new Vector3(0f, 0.6f, -86f);
        spawner.transform.SetPositionAndRotation(spawn, Quaternion.identity);

        // 範本的「操作說明 / 簡介」看板 (世界空間 Canvas) 移到停機坪前方，保持範本中與出生點的相對距離
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "鍵盤控制Canvas") root.transform.position = new Vector3(spawn.x, 2.19f, spawn.z + 4f);
            else if (root.name == "簡介Canvas") root.transform.position = new Vector3(spawn.x, 2.19f, spawn.z + 16f);
        }
    }

    #endregion

    #region 材質

    private static void LoadMaterials()
    {
        EnsureFolder(MaterialFolder);
        concrete = LoadSurface("Plaza_Tiles", new Color(0.6f, 0.6f, 0.58f));
        asphalt = LoadSurface("Road_Asphalt", new Color(0.22f, 0.22f, 0.24f));
        wallA = LoadSurface("Building_B", new Color(0.55f, 0.58f, 0.62f));
        wallB = LoadSurface("Building_D", new Color(0.62f, 0.55f, 0.48f));
        roof = LoadSurface("Roof", new Color(0.35f, 0.37f, 0.4f));

        steel = Mat("Steel", new Color(0.42f, 0.45f, 0.5f), 0.4f);
        water = Mat("Water", new Color(0.12f, 0.3f, 0.42f), 0.9f);
        padDark = Mat("PadDark", new Color(0.18f, 0.19f, 0.21f), 0.2f);
        white = Mat("White", new Color(0.92f, 0.92f, 0.9f), 0.2f);
        hazard = Mat("Hazard", new Color(0.95f, 0.75f, 0.1f), 0.3f);
        tankWhite = Mat("Tank", new Color(0.82f, 0.84f, 0.86f), 0.5f);
        containerColors = new[]
        {
            Mat("Container_Red", new Color(0.65f, 0.18f, 0.14f), 0.3f),
            Mat("Container_Blue", new Color(0.15f, 0.3f, 0.6f), 0.3f),
            Mat("Container_Green", new Color(0.2f, 0.45f, 0.25f), 0.3f),
            Mat("Container_Orange", new Color(0.85f, 0.45f, 0.12f), 0.3f),
            Mat("Container_Grey", new Color(0.5f, 0.52f, 0.55f), 0.3f)
        };
    }

    private static Material LoadSurface(string name, Color fallback)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>($"{SurfaceFolder}/{name}.mat");
        return material != null ? material : Mat(name, fallback, 0.2f);
    }

    private static Material Mat(string name, Color color, float smoothness)
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

    #region 場景結構

    private static CityMeshBuilder NewBuilder()
    {
        CityMeshBuilder builder = new CityMeshBuilder();
        builder.SetTiling(concrete, 4f);
        builder.SetTiling(asphalt, 6f);
        builder.SetTiling(wallA, 4f);
        builder.SetTiling(wallB, 4f);
        builder.SetTiling(roof, 4f);
        return builder;
    }

    private static GameObject Finish(CityMeshBuilder builder, string name, Transform parent, bool collider = true)
    {
        GameObject built = builder.Build(name, parent);
        if (built != null && collider)
        {
            MeshCollider meshCollider = built.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = built.GetComponent<MeshFilter>().sharedMesh;
        }
        return built;
    }

    /// <summary>地面、環狀車道與入口道路、停機坪、碼頭岸壁與海面</summary>
    private static void BuildGround(Transform level)
    {
        CityMeshBuilder builder = NewBuilder();
        float landDepth = QuayZ - AreaMin.y;
        builder.AddBox(new Vector3(0f, -0.25f, AreaMin.y + landDepth * 0.5f), new Vector3(AreaMax.x - AreaMin.x, 0.5f, landDepth), concrete);

        float span = LoopHalf * 2f + RoadWidth;
        builder.AddBox(new Vector3(0f, 0.02f, -LoopHalf), new Vector3(span, 0.04f, RoadWidth), asphalt);
        builder.AddBox(new Vector3(0f, 0.02f, LoopHalf), new Vector3(span, 0.04f, RoadWidth), asphalt);
        builder.AddBox(new Vector3(-LoopHalf, 0.02f, 0f), new Vector3(RoadWidth, 0.04f, span), asphalt);
        builder.AddBox(new Vector3(LoopHalf, 0.02f, 0f), new Vector3(RoadWidth, 0.04f, span), asphalt);
        // 南側入口道路 (停機坪 → 環狀車道)
        builder.AddBox(new Vector3(0f, 0.02f, (AreaMin.y - LoopHalf) * 0.5f - 2f), new Vector3(RoadWidth, 0.04f, AreaMin.y * -1f - LoopHalf - 4f), asphalt);

        // 停機坪 (H 標誌)
        builder.AddBox(new Vector3(0f, 0.15f, -86f), new Vector3(9f, 0.3f, 9f), padDark);
        builder.AddBox(new Vector3(-1.6f, 0.31f, -86f), new Vector3(0.6f, 0.02f, 4.5f), white);
        builder.AddBox(new Vector3(1.6f, 0.31f, -86f), new Vector3(0.6f, 0.02f, 4.5f), white);
        builder.AddBox(new Vector3(0f, 0.31f, -86f), new Vector3(3.2f, 0.02f, 0.6f), white);

        // 碼頭岸壁 (邊緣警示條) 與海面 (略低於地面)
        builder.AddBox(new Vector3(0f, 0.05f, QuayZ - 0.6f), new Vector3(AreaMax.x - AreaMin.x, 0.1f, 1.2f), hazard);
        builder.AddBox(new Vector3(0f, -1.5f, QuayZ + 0.25f), new Vector3(AreaMax.x - AreaMin.x, 3f, 0.5f), concrete);
        Finish(builder, "Ground", level);

        // 海面延伸到四周 (港區像半島)，地平線不會露出虛空
        CityMeshBuilder sea = new CityMeshBuilder();
        sea.AddBox(new Vector3(0f, -2.25f, 0f), new Vector3(1600f, 0.5f, 1600f), water);
        Finish(sea, "Sea", level);
    }

    /// <summary>
    /// 倉庫：三面牆 + 屋頂，開放側有柱子與門楣 (卸貨口高 5.5m)。
    /// center 為地面中心，openSide 為開口朝向 (世界方向)。
    /// </summary>
    private static void AddWarehouse(CityMeshBuilder builder, Vector3 center, Vector2 size, float height, Vector3 openSide, Material wall, int bays)
    {
        Matrix4x4 frame = Matrix4x4.TRS(center, Quaternion.LookRotation(openSide), Vector3.one);
        float w = size.x;      // 沿開口的寬度
        float d = size.y;      // 深度
        const float t = 0.5f;  // 牆厚
        const float door = 5.5f;

        builder.AddBox(frame, new Vector3(0f, height * 0.5f, -d * 0.5f + t * 0.5f), new Vector3(w, height, t), wall);          // 後牆
        builder.AddBox(frame, new Vector3(-w * 0.5f + t * 0.5f, height * 0.5f, 0f), new Vector3(t, height, d), wall);          // 側牆
        builder.AddBox(frame, new Vector3(w * 0.5f - t * 0.5f, height * 0.5f, 0f), new Vector3(t, height, d), wall);
        builder.AddBox(frame, new Vector3(0f, (door + height) * 0.5f, d * 0.5f - t * 0.5f), new Vector3(w, height - door, t), wall); // 門楣
        for (int i = 1; i < bays; i++)
        {
            float x = -w * 0.5f + w * i / bays;
            builder.AddBox(frame, new Vector3(x, door * 0.5f, d * 0.5f - 0.4f), new Vector3(0.8f, door, 0.8f), steel);
        }
        builder.AddBox(frame, new Vector3(0f, height + 0.2f, 0f), new Vector3(w + 0.6f, 0.4f, d + 0.6f), roof);
        // 卸貨口地面警示線
        builder.AddBox(frame, new Vector3(0f, 0.03f, d * 0.5f + 1.2f), new Vector3(w - 1f, 0.02f, 0.3f), hazard);
    }

    private static void BuildWarehouses(Transform level)
    {
        CityMeshBuilder builder = NewBuilder();
        // 倉庫 A (西側，開口朝南)：中間卸貨口內有撞擊目標
        AddWarehouse(builder, new Vector3(-38f, 0f, 20f), new Vector2(30f, 18f), 9f, Vector3.back, wallA, 3);
        // 倉庫 B (東側，開口朝西)：無目標，作為干擾
        AddWarehouse(builder, new Vector3(38f, 0f, 22f), new Vector2(24f, 22f), 10f, Vector3.left, wallB, 2);
        Finish(builder, "Warehouses", level);
    }

    /// <summary>卸貨頂棚：柱子撐起的平頂 (高 5.5m)，底下停放貨車</summary>
    private static void BuildCanopy(Transform level)
    {
        CityMeshBuilder builder = NewBuilder();
        Vector3 center = new Vector3(28f, 0f, -30f);
        Vector2 size = new Vector2(20f, 11f);
        foreach (float x in new[] { -size.x * 0.5f + 0.5f, 0f, size.x * 0.5f - 0.5f })
        {
            foreach (float z in new[] { -size.y * 0.5f + 0.5f, size.y * 0.5f - 0.5f })
            {
                builder.AddBox(center + new Vector3(x, 2.75f, z), new Vector3(0.6f, 5.5f, 0.6f), steel);
            }
        }
        builder.AddBox(center + new Vector3(0f, 5.7f, 0f), new Vector3(size.x, 0.4f, size.y), roof);
        builder.AddBox(center + new Vector3(0f, 5.45f, -size.y * 0.5f), new Vector3(size.x, 0.3f, 0.2f), hazard);
        GameObject canopy = Finish(builder, "LoadingCanopy", level);

        // 頂棚下停放的貨車
        GameObject[] trucks = CarPrefabs().Where(prefab => prefab.name.ToLowerInvariant().Contains("van") || prefab.name.ToLowerInvariant().Contains("pick")).ToArray();
        for (int i = 0; i < trucks.Length && i < 2; i++)
        {
            GameObject truck = CityTraffic.CreateCarModel(trucks[i], canopy.transform, out _, out Vector3 truckSize);
            truck.transform.SetPositionAndRotation(center + new Vector3(i == 0 ? -5f : 5f, 0f, 0f), Quaternion.identity);
            BoxCollider box = truck.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, truckSize.y * 0.5f, 0f);
            box.size = truckSize;
            truck.isStatic = true;
        }
    }

    /// <summary>
    /// 貨櫃場：四排兩層貨櫃 (沿 X 排列)，中央走道上方架一個跨越走道的貨櫃形成隧道 (撞擊目標)。
    /// </summary>
    private static void BuildContainerYard(Transform level)
    {
        CityMeshBuilder builder = NewBuilder();
        const float length = 12f, width = 2.6f, height = 2.6f;
        float[] rows = { -52f, -46f, -34f, -28f };
        float[] columns = { -48f, -35f, -22f };
        int colorIndex = 0;
        foreach (float z in rows)
        {
            foreach (float x in columns)
            {
                for (int layer = 0; layer < 2; layer++)
                {
                    // 第 1 排西側那疊只放一層，讓頂部錯落
                    if (layer == 1 && z == rows[0] && x == columns[2]) continue;
                    Material color = containerColors[colorIndex++ % containerColors.Length];
                    builder.AddBox(new Vector3(x, height * (layer + 0.5f), z), new Vector3(length - 0.4f, height, width), color);
                }
            }
        }
        // 跨越中央走道 (z -44.7 ~ -35.3) 的貨櫃 = 隧道頂
        builder.AddBox(new Vector3(-35f, height * 2.5f, -40f), new Vector3(width, height, length), containerColors[3]);

        // 場地中央的零散貨櫃堆 (掩蔽與障礙)
        AddStack(builder, new Vector3(12f, 0f, -9f), 3, 0f, ref colorIndex);
        AddStack(builder, new Vector3(12f, 0f, -5.8f), 2, 0f, ref colorIndex);
        AddStack(builder, new Vector3(-12f, 0f, 2f), 2, 90f, ref colorIndex);
        AddStack(builder, new Vector3(5f, 0f, 58f), 1, 0f, ref colorIndex);
        Finish(builder, "ContainerYard", level);
    }

    private static void AddStack(CityMeshBuilder builder, Vector3 ground, int layers, float yaw, ref int colorIndex)
    {
        Matrix4x4 frame = Matrix4x4.TRS(ground, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
        for (int layer = 0; layer < layers; layer++)
        {
            builder.AddBox(frame, new Vector3(0f, 2.6f * (layer + 0.5f), 0f), new Vector3(11.6f, 2.6f, 2.6f), containerColors[colorIndex++ % containerColors.Length]);
        }
    }

    private static void BuildTanks(Transform level)
    {
        CityMeshBuilder builder = NewBuilder();
        foreach (Vector3 center in new[] { new Vector3(22f, 0f, 47f), new Vector3(42f, 0f, 47f) })
        {
            builder.AddCylinder(Matrix4x4.identity, center, Vector3.up, 7f, 10f, 24, tankWhite);
            builder.AddCylinder(Matrix4x4.identity, center + Vector3.up * 10f, Vector3.up, 7.2f, 0.3f, 24, steel);
            // 外側爬梯
            builder.AddBox(center + new Vector3(7.1f, 5f, 0f), new Vector3(0.2f, 10f, 0.8f), hazard);
        }
        // 油槽之間的管線
        builder.AddCylinder(Matrix4x4.identity, new Vector3(29f, 3f, 47f), Vector3.right, 0.5f, 6f, 10, steel);
        Finish(builder, "OilTanks", level);
    }

    /// <summary>高架管廊：6.5m 高的平台架在柱子上，上方鋪設管線，底下可低飛穿越 (撞擊目標)</summary>
    private static void BuildPipeRack(Transform level)
    {
        CityMeshBuilder builder = NewBuilder();
        const float z = 40f, deckY = 6.5f, halfWidth = 2.2f;
        for (float x = -22f; x <= 10f; x += 8f)
        {
            builder.AddBox(new Vector3(x, deckY * 0.5f, z - halfWidth), new Vector3(0.5f, deckY, 0.5f), steel);
            builder.AddBox(new Vector3(x, deckY * 0.5f, z + halfWidth), new Vector3(0.5f, deckY, 0.5f), steel);
        }
        builder.AddBox(new Vector3(-6f, deckY, z), new Vector3(33f, 0.5f, halfWidth * 2f + 0.6f), steel);
        foreach (float offset in new[] { -1.4f, 0f, 1.4f })
        {
            builder.AddCylinder(Matrix4x4.identity, new Vector3(-22.5f, deckY + 0.7f, z + offset), Vector3.right, 0.45f, 33f, 10, offset == 0f ? hazard : tankWhite);
        }
        Finish(builder, "PipeRack", level);
    }

    /// <summary>碼頭龍門吊：跨在岸邊的門型框架，吊臂伸向海面</summary>
    private static void BuildCranes(Transform level)
    {
        CityMeshBuilder builder = NewBuilder();
        foreach (float x in new[] { -35f, 25f })
        {
            foreach (float dx in new[] { -6f, 6f })
            {
                foreach (float z in new[] { 68f, 76f })
                {
                    builder.AddBox(new Vector3(x + dx, 11f, z), new Vector3(1.2f, 22f, 1.2f), hazard);
                }
                builder.AddBox(new Vector3(x + dx, 22.5f, 78f), new Vector3(1.4f, 1.4f, 30f), hazard); // 吊臂
            }
            builder.AddBox(new Vector3(x, 21.5f, 72f), new Vector3(14f, 1.2f, 1.6f), hazard);
            builder.AddBox(new Vector3(x, 24f, 66f), new Vector3(6f, 3f, 4f), steel); // 機房
        }
        Finish(builder, "QuayCranes", level);
    }

    private static void BuildOffice(Transform level)
    {
        CityMeshBuilder builder = NewBuilder();
        Vector3 center = new Vector3(40f, 0f, -50f);
        builder.AddBox(center + new Vector3(0f, 6f, 0f), new Vector3(14f, 12f, 10f), wallB);
        builder.AddBox(center + new Vector3(0f, 12.2f, 0f), new Vector3(14.4f, 0.4f, 10.4f), roof);
        builder.AddBox(center + new Vector3(3f, 13.4f, 1f), new Vector3(3f, 2f, 3f), steel); // 屋頂機房
        Finish(builder, "Office", level);
    }

    private static void PlaceLamps(Transform level)
    {
        GameObject lamp = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Download/LowpolyStreetPack/Prefabs/StreetProps/TraficLights/LampPost_J.prefab");
        if (lamp == null) return;
        Transform lamps = new GameObject("Lamps").transform;
        lamps.SetParent(level, false);
        Dictionary<Material, Material> remap = CityLevelBaker.StreetPackUrpRemap();
        float edge = LoopHalf + RoadWidth * 0.5f + 1f;
        foreach (float along in new[] { -50f, -20f, 45f })
        {
            PlaceLamp(lamp, lamps, remap, new Vector3(along, 0f, -edge), 180f);
            PlaceLamp(lamp, lamps, remap, new Vector3(along, 0f, edge), 0f);
            PlaceLamp(lamp, lamps, remap, new Vector3(-edge, 0f, along), 270f);
            PlaceLamp(lamp, lamps, remap, new Vector3(edge, 0f, along), 90f);
        }
    }

    private static void PlaceLamp(GameObject prefab, Transform parent, Dictionary<Material, Material> remap, Vector3 position, float yaw)
    {
        GameObject lamp = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        // 燈臂沿模型 +X，旋轉讓燈臂伸向車道
        lamp.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw + 90f, 0f) * prefab.transform.rotation);
        foreach (Light light in lamp.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(light);
        foreach (Renderer renderer in lamp.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool cone = materials.Length > 0;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;
                if (remap.TryGetValue(materials[i], out Material replacement)) materials[i] = replacement;
                if (!materials[i].name.Contains("LightShape")) cone = false;
            }
            renderer.sharedMaterials = materials;
            if (cone) renderer.enabled = false;
        }
        CityGenerator.FitPropColliders(lamp);
    }

    #endregion

    #region 目標與車道

    /// <summary>目標點位：投彈 3 (油槽頂、貨櫃頂、碼頭)、撞擊 3 (倉庫卸貨口、貨櫃隧道、管廊下)</summary>
    private static void BuildTargetPoints(Transform level)
    {
        Transform points = new GameObject("StrikeTargetPoints").transform;
        points.SetParent(level, false);

        AddPoint(points, "Bomb_TankTop", StrikeTarget.TargetKind.Bomb, new Vector3(42f, 10.35f, 47f), null);
        AddPoint(points, "Bomb_ContainerTop", StrikeTarget.TargetKind.Bomb, new Vector3(-48f, 5.2f, -52f), null);
        AddPoint(points, "Bomb_Quay", StrikeTarget.TargetKind.Bomb, new Vector3(-5f, 0f, 72f), null);

        AddPoint(points, "Ram_WarehouseBay", StrikeTarget.TargetKind.Ram, new Vector3(-38f, 0f, 16f), new Vector3(-38f, 3f, 4f));
        AddPoint(points, "Ram_ContainerTunnel", StrikeTarget.TargetKind.Ram, new Vector3(-35f, 0f, -40f), new Vector3(-24f, 3f, -40f));
        AddPoint(points, "Ram_PipeRack", StrikeTarget.TargetKind.Ram, new Vector3(-6f, 0f, 40f), new Vector3(-6f, 3f, 30f));
    }

    private static void AddPoint(Transform parent, string name, StrikeTarget.TargetKind kind, Vector3 position, Vector3? approach)
    {
        StrikeTargetPoint point = new GameObject(name).AddComponent<StrikeTargetPoint>();
        point.transform.SetParent(parent, false);
        point.transform.position = position;
        point.kind = kind;
        if (approach.HasValue)
        {
            // 目標面向飛入方向 (先轉向再放子物件，否則子物件會跟著父物件旋轉而偏離)
            Vector3 facing = approach.Value - position;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.01f) point.transform.rotation = Quaternion.LookRotation(facing);

            Transform approachPoint = new GameObject("Approach").transform;
            approachPoint.SetParent(point.transform, false);
            approachPoint.position = approach.Value;
            point.approach = approachPoint;
        }
    }

    /// <summary>環狀車道 (順時針、靠右行駛)</summary>
    private static void BuildVehicleRoute(Transform level)
    {
        StrikeVehicleRoute route = new GameObject("VehicleRoute").AddComponent<StrikeVehicleRoute>();
        route.transform.SetParent(level, false);
        float lane = LoopHalf - 2f; // 內側車道：順時針行駛時位於道路中線右側
        Vector3[] corners =
        {
            new Vector3(-lane, 0.05f, -lane),
            new Vector3(-lane, 0.05f, lane),
            new Vector3(lane, 0.05f, lane),
            new Vector3(lane, 0.05f, -lane)
        };
        for (int i = 0; i < corners.Length; i++)
        {
            Transform waypoint = new GameObject($"Waypoint_{i}").transform;
            waypoint.SetParent(route.transform, false);
            waypoint.position = corners[i];
        }

        GameObject[] cars = CarPrefabs();
        SerializedObject so = new SerializedObject(route);
        SerializedProperty prefabs = so.FindProperty("carPrefabs");
        prefabs.arraySize = cars.Length;
        for (int i = 0; i < cars.Length; i++) prefabs.GetArrayElementAtIndex(i).objectReferenceValue = cars[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    #endregion

    #region 工具

    private static GameObject[] CarPrefabs()
    {
        return AssetDatabase.FindAssets("t:Prefab", new[] { CarFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(path => path)
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .Where(prefab => prefab != null && !prefab.name.ToLowerInvariant().Contains("police"))
            .ToArray();
    }

    private static void SaveMeshes(Transform level)
    {
        int index = 0;
        foreach (MeshFilter filter in level.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || AssetDatabase.Contains(mesh)) continue;
            AssetDatabase.CreateAsset(mesh, $"{MeshFolder}/{mesh.name}_{index++}.asset");
        }
        AssetDatabase.SaveAssets();
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static void AddToBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(scene => scene.path == ScenePath)) return;
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    #endregion
}

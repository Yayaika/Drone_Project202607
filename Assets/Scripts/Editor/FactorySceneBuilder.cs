using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 建立打擊模式室內場景「工廠・突襲行動」(Assets/Scenes/StrikeFactory.unity)：
/// 以訓練場 (SampleScene) 的系統物件為基礎，搭建 100×120m、高 20m 的封閉廠房 (屋頂有天窗)，
/// 無人機要穿梭在管線迷宮、貨架、生產線、樓梯、辦公室房間與作業設備之間，攻擊高價值目標與逃竄的敵方無人機。
/// 分區 (南 → 北)：裝卸區 (起飛點) → 倉儲貨架 / 生產線 → 管線迷宮 / 反應爐 → 辦公樓 (兩層房間) / 天車 / 沖壓機 / 發電機房 / 北側夾層走道。
/// 目標點位 (StrikeTargetPoint) 與敵機航線 (EnemyDronePatrol) 為場景物件，可在 Editor 直接移動調整。
/// 重新執行會重建「StrikeFactory_Level」，其他物件保留。
/// </summary>
public static class FactorySceneBuilder
{
    public const string ScenePath = "Assets/Scenes/StrikeFactory.unity";
    private const string TemplatePath = "Assets/Scenes/SampleScene.unity";
    private const string LevelRootName = "StrikeFactory_Level";
    private const string MeshFolder = "Assets/GeneratedLevels/StrikeFactory";
    private const string MaterialFolder = "Assets/Materials/StrikeFactory";
    private const string SurfaceFolder = "Assets/Materials/OrganicCity";
    // Download 素材的四軸無人機 (與玩家的 Racing Drone / FPV 炸彈機外型不同，容易分辨敵我)
    private const string EnemyModelPath = "Assets/Download/Drone/prefab/drone Black.prefab";

    // 廠房內部範圍 (牆內側) 與屋頂底面高度
    private const float HalfX = 50f;
    private const float HalfZ = 60f;
    private const float RoofY = 20f;
    private static readonly Vector3 SpawnPoint = new Vector3(-25f, 0.6f, -130f);

    private static Material floor, wall, steel, darkSteel, hazard, white, crate, rubber, machineGreen, machineBlue, boiler, lamp, screen, office;
    private static Material[] pipeColors;

    [MenuItem("無人機遊戲/14. 建立打擊關卡場景 (工廠)", priority = 14)]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorUtility.DisplayDialog("工廠打擊關卡場景", Build(), "確定");
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
        if (created) StrikeSceneBuilder.RemoveTemplateCourse(scene);

        GameObject old = scene.GetRootGameObjects().FirstOrDefault(root => root.name == LevelRootName);
        if (old != null) Object.DestroyImmediate(old);
        // 範本 (訓練場) 的空氣牆外圍景觀：廠房封閉看不到，只會浪費效能
        GameObject outskirts = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "OutskirtsScenery");
        if (outskirts != null) Object.DestroyImmediate(outskirts);

        LoadMaterials();
        StrikeSceneBuilder.EnsureFolder(MeshFolder);
        foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { MeshFolder }))
        {
            AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
        }

        Transform level = new GameObject(LevelRootName).transform;
        BuildShell(level);
        BuildLoadingArea(level);
        BuildStorageRacks(level);
        BuildProductionLine(level);
        BuildPipeMaze(level);
        BuildReactor(level);
        BuildOfficeBlock(level);
        BuildNorthHall(level);
        BuildLights(level);
        BuildDetails(level);
        BuildTargetPoints(level);
        BuildEnemyDrones(level);
        BuildSite(level);
        BuildWorkers(level);
        BuildSoldiers(level);
        BuildRealism(level);

        StrikeArena arena = new GameObject("StrikeArena").AddComponent<StrikeArena>();
        arena.transform.SetParent(level, false);
        arena.Configure(SiteMin, SiteMax, 0f, SiteCeiling);

        PlaceSpawner(scene);
        StrikeSceneBuilder.SaveMeshes(level, MeshFolder);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        StrikeSceneBuilder.AddToBuildSettings(ScenePath);
        // 廠區外圍景觀 (Unity Terrain 丘陵、郊區建築)：重新開啟場景、建立並存檔
        string outskirtsSummary = OutskirtsSceneryBuilder.Build(ScenePath);
        return $"已{(created ? "建立" : "重建")} {ScenePath}，並加入 Build Settings。\n{outskirtsSummary}";
    }

    /// <summary>起飛點：南側裝卸區的停機坪，面向廠房內部 (+Z)</summary>
    private static void PlaceSpawner(Scene scene)
    {
        SpawnCtrl spawner = Object.FindFirstObjectByType<SpawnCtrl>();
        if (spawner == null) return;
        spawner.transform.SetPositionAndRotation(SpawnPoint, Quaternion.identity);

        // 場景內不放說明看板：移除範本的「操作說明 / 簡介」世界空間 Canvas (操作說明可從設置面板開啟)
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "鍵盤控制Canvas" || root.name == "簡介Canvas") Object.DestroyImmediate(root);
        }
    }

    #region 材質

    private static void LoadMaterials()
    {
        StrikeSceneBuilder.EnsureFolder(MaterialFolder);
        // 外牆與辦公樓用工廠專屬材質 (不改與其他場景共用的 OrganicCity 材質)
        wall = Mat("Cladding", new Color(0.6f, 0.64f, 0.68f), 0.35f);
        office = Mat("OfficePanel", new Color(0.8f, 0.78f, 0.74f), 0.25f);

        floor = Mat("Floor", new Color(0.36f, 0.4f, 0.38f), 0.35f);
        steel = Mat("Steel", new Color(0.45f, 0.48f, 0.52f), 0.45f);
        darkSteel = Mat("DarkSteel", new Color(0.2f, 0.21f, 0.23f), 0.35f);
        hazard = Mat("Hazard", new Color(0.95f, 0.75f, 0.1f), 0.3f);
        white = Mat("White", new Color(0.9f, 0.9f, 0.88f), 0.2f);
        crate = Mat("Crate", new Color(0.55f, 0.4f, 0.24f), 0.1f);
        rubber = Mat("Belt", new Color(0.1f, 0.1f, 0.11f), 0.2f);
        machineGreen = Mat("MachineGreen", new Color(0.25f, 0.45f, 0.35f), 0.4f);
        machineBlue = Mat("MachineBlue", new Color(0.2f, 0.33f, 0.55f), 0.4f);
        boiler = Mat("Boiler", new Color(0.7f, 0.72f, 0.74f), 0.55f);
        lamp = Mat("LampGlow", new Color(1f, 0.96f, 0.85f), 0.2f, new Color(1f, 0.95f, 0.8f) * 2.5f);
        screen = Mat("Screen", new Color(0.1f, 0.3f, 0.35f), 0.8f, new Color(0.1f, 0.7f, 0.8f) * 1.2f);
        pipeColors = new[]
        {
            Mat("Pipe_Red", new Color(0.62f, 0.16f, 0.12f), 0.45f),
            Mat("Pipe_Silver", new Color(0.75f, 0.77f, 0.8f), 0.6f),
            Mat("Pipe_Green", new Color(0.22f, 0.48f, 0.3f), 0.45f),
            Mat("Pipe_Blue", new Color(0.18f, 0.32f, 0.6f), 0.45f)
        };
        ApplyTextures();
    }

    /// <summary>
    /// 寫實貼圖：外牆/屋頂波浪鋼板、地坪混凝土 (環氧樹脂綠灰)、鋼構用 Download 的 War FX 金屬、木箱用 War FX 木紋，其餘加髒污。
    /// UV 單位：外牆與辦公樓 4m (SetTiling)，其他 1m。
    /// </summary>
    private static void ApplyTextures()
    {
        ProceduralTextures.Set corrugated = ProceduralTextures.Corrugated();
        ProceduralTextures.Set concrete = ProceduralTextures.Concrete();
        ProceduralTextures.Set grime = ProceduralTextures.Grime();
        const string warFx = "Assets/Download/JMO Assets/WarFX/Demo/Assets";
        ProceduralTextures.Set metal = ProceduralTextures.FromAssets($"{warFx}/WFX_Demo_T_Metal.tga", $"{warFx}/WFX_Demo_T_Metal Normal.tga") ?? grime;
        ProceduralTextures.Set wood = ProceduralTextures.FromAssets($"{warFx}/WFX_Demo_T_Wood.tga", $"{warFx}/WFX_Demo_T_Wood Normal.tga") ?? grime;

        ProceduralTextures.Apply(wall, corrugated, 4f, 3f, null, 0.35f, 0.45f, 0.7f);
        ProceduralTextures.Apply(office, grime, 4f, 3f, null, 0.25f, 0f, 0.3f);
        ProceduralTextures.Apply(floor, concrete, 1f, 8f, new Color(0.7f, 0.76f, 0.72f), 0.45f, 0f, 0.6f);
        ProceduralTextures.Apply(steel, metal, 1f, 2f, null, 0.45f, 0.5f, 0.8f);
        ProceduralTextures.Apply(darkSteel, metal, 1f, 2f, null, 0.35f, 0.5f, 0.8f);
        ProceduralTextures.Apply(hazard, grime, 1f, 2f, null, 0.3f, 0.1f, 0.4f);
        ProceduralTextures.Apply(crate, wood, 1f, 1.5f, new Color(0.95f, 0.88f, 0.8f), 0.15f, 0f, 0.8f);
        foreach (Material machine in new[] { machineGreen, machineBlue }) ProceduralTextures.Apply(machine, grime, 1f, 3f, null, 0.4f, 0.2f, 0.4f);
        ProceduralTextures.Apply(boiler, grime, 1f, 6f, null, 0.5f, 0.3f, 0.4f);
        foreach (Material pipe in pipeColors) ProceduralTextures.Apply(pipe, grime, 1f, 3f, null, 0.45f, 0.3f, 0.4f);
    }

    private static Material LoadSurface(string name, Color fallback)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>($"{SurfaceFolder}/{name}.mat");
        return material != null ? material : Mat(name, fallback, 0.2f);
    }

    private static Material Mat(string name, Color color, float smoothness, Color? emission = null)
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
        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    #endregion

    #region 建構工具

    private static CityMeshBuilder NewBuilder()
    {
        CityMeshBuilder builder = new CityMeshBuilder();
        builder.SetTiling(wall, 4f);
        builder.SetTiling(office, 4f);
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

    /// <summary>沿 axis (Vector3.right / Vector3.forward) 從 start 延伸 length 的管線</summary>
    private static void Pipe(CityMeshBuilder builder, Vector3 start, Vector3 axis, float length, float radius, Material material)
    {
        builder.AddCylinder(Matrix4x4.identity, start, axis, radius, length, 12, material);
        // 兩端法蘭
        builder.AddCylinder(Matrix4x4.identity, start, axis, radius * 1.35f, 0.25f, 12, darkSteel);
        builder.AddCylinder(Matrix4x4.identity, start + axis * (length - 0.25f), axis, radius * 1.35f, 0.25f, 12, darkSteel);
    }

    /// <summary>
    /// 牆面 (from → to 的水平線段，高度 y0~y1)，openings 為 (沿牆距離中心, 寬, 底, 頂) 的門窗開口。
    /// </summary>
    private static void WallWithOpenings(CityMeshBuilder builder, Vector3 from, Vector3 to, float y0, float y1, float thickness, Material material,
        params (float along, float width, float bottom, float top)[] openings)
    {
        Vector3 direction = to - from;
        direction.y = 0f;
        float length = direction.magnitude;
        Matrix4x4 frame = Matrix4x4.TRS(new Vector3(from.x, 0f, from.z), Quaternion.LookRotation(direction), Vector3.one);

        void Piece(float a, float b, float bottom, float top)
        {
            if (b - a < 0.01f || top - bottom < 0.01f) return;
            builder.AddBox(frame, new Vector3(0f, (bottom + top) * 0.5f, (a + b) * 0.5f), new Vector3(thickness, top - bottom, b - a), material);
        }

        float cursor = 0f;
        foreach ((float along, float width, float bottom, float top) in openings.OrderBy(opening => opening.along))
        {
            float a = along - width * 0.5f;
            float b = along + width * 0.5f;
            Piece(cursor, a, y0, y1);
            Piece(a, b, y0, bottom);
            Piece(a, b, top, y1);
            cursor = b;
        }
        Piece(cursor, length, y0, y1);
    }

    /// <summary>鋼樓梯：從 start (地面) 沿 direction 水平前進 run、上升 rise，含兩側斜樑與扶手</summary>
    private static void Stairs(CityMeshBuilder builder, Vector3 start, Vector3 direction, float run, float rise, float width)
    {
        direction.y = 0f;
        direction.Normalize();
        Quaternion yaw = Quaternion.LookRotation(direction);
        int steps = Mathf.Max(2, Mathf.RoundToInt(rise / 0.3f));
        float depth = run / steps;
        for (int i = 0; i < steps; i++)
        {
            Vector3 center = start + direction * (depth * (i + 0.5f)) + Vector3.up * (rise * (i + 1) / steps - 0.05f);
            builder.AddBox(Matrix4x4.TRS(center, yaw, Vector3.one), Vector3.zero, new Vector3(width, 0.1f, depth + 0.05f), steel);
        }

        float slope = Mathf.Sqrt(run * run + rise * rise);
        Quaternion incline = Quaternion.LookRotation(direction * run + Vector3.up * rise);
        Vector3 right = yaw * Vector3.right;
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 mid = start + direction * (run * 0.5f) + Vector3.up * (rise * 0.5f) + right * (side * width * 0.5f);
            builder.AddBox(Matrix4x4.TRS(mid, incline, Vector3.one), Vector3.zero, new Vector3(0.12f, 0.3f, slope), darkSteel);
            builder.AddBox(Matrix4x4.TRS(mid + Vector3.up * 1f, incline, Vector3.one), Vector3.zero, new Vector3(0.06f, 0.06f, slope), hazard);
        }
    }

    /// <summary>走道欄杆：沿 from → to，高 1m，每 2.5m 一根立柱</summary>
    private static void Railing(CityMeshBuilder builder, Vector3 from, Vector3 to)
    {
        Vector3 span = to - from;
        Quaternion rotation = Quaternion.LookRotation(span);
        builder.AddBox(Matrix4x4.TRS((from + to) * 0.5f + Vector3.up, rotation, Vector3.one), Vector3.zero, new Vector3(0.06f, 0.06f, span.magnitude), hazard);
        builder.AddBox(Matrix4x4.TRS((from + to) * 0.5f + Vector3.up * 0.5f, rotation, Vector3.one), Vector3.zero, new Vector3(0.04f, 0.04f, span.magnitude), hazard);
        int posts = Mathf.Max(1, Mathf.RoundToInt(span.magnitude / 2.5f));
        for (int i = 0; i <= posts; i++)
        {
            builder.AddBox(Vector3.Lerp(from, to, (float)i / posts) + Vector3.up * 0.5f, new Vector3(0.06f, 1f, 0.06f), hazard);
        }
    }

    private static void CrateStack(CityMeshBuilder builder, Vector3 ground, int count, float yaw)
    {
        Matrix4x4 frame = Matrix4x4.TRS(ground, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
        for (int i = 0; i < count; i++)
        {
            builder.AddBox(frame, new Vector3(0f, 0.65f + i * 1.3f, 0f), new Vector3(1.4f, 1.3f, 1.4f), crate);
        }
    }

    #endregion

    #region 廠房外殼

    /// <summary>地板、四面外牆 (內側壁柱)、屋頂 (每 12m 一道 4m 寬天窗) 與屋架</summary>
    private static void BuildShell(Transform level)
    {
        CityMeshBuilder builder = NewBuilder();
        builder.AddBox(new Vector3(0f, -0.25f, 0f), new Vector3(HalfX * 2f + 4f, 0.5f, HalfZ * 2f + 4f), floor);

        const float t = 0.6f, h = RoofY + 1f;
        // 外牆開口：南牆兩座捲門 (x ±25，8×7m)、西牆側門 (z 0)、東牆側門 (z 25，通往 B 棟)，無人機可從外面飛進廠房
        float wallLength = HalfZ * 2f + t * 2f;
        WallWithOpenings(builder, new Vector3(-HalfX - t * 0.5f, 0f, -HalfZ - t), new Vector3(-HalfX - t * 0.5f, 0f, HalfZ + t), 0f, h, t, wall,
            (wallLength * 0.5f, 7f, 0f, 7f));
        WallWithOpenings(builder, new Vector3(HalfX + t * 0.5f, 0f, -HalfZ - t), new Vector3(HalfX + t * 0.5f, 0f, HalfZ + t), 0f, h, t, wall,
            (wallLength * 0.5f + 25f, 7f, 0f, 7f));
        WallWithOpenings(builder, new Vector3(-HalfX, 0f, -HalfZ - t * 0.5f), new Vector3(HalfX, 0f, -HalfZ - t * 0.5f), 0f, h, t, wall,
            (HalfX - 25f, 8f, 0f, 7f), (HalfX + 25f, 8f, 0f, 7f));
        builder.AddBox(new Vector3(0f, h * 0.5f, HalfZ + t * 0.5f), new Vector3(HalfX * 2f, h, t), wall);

        // 壁柱 (東側與南北兩側；西側被辦公樓佔用的段落跳過)
        for (float z = -HalfZ + 6f; z < HalfZ; z += 12f)
        {
            builder.AddBox(new Vector3(HalfX - 0.3f, RoofY * 0.5f, z), new Vector3(0.6f, RoofY, 0.6f), steel);
            if (z < 24f) builder.AddBox(new Vector3(-HalfX + 0.3f, RoofY * 0.5f, z), new Vector3(0.6f, RoofY, 0.6f), steel);
        }

        // 屋頂：8m 實心 + 4m 天窗，交錯排列；天窗正下方以屋架橫樑分隔
        for (float z = -HalfZ; z < HalfZ - 0.1f; z += 12f)
        {
            builder.AddBox(new Vector3(0f, RoofY + 0.25f, z + 4f), new Vector3(HalfX * 2f + 1.2f, 0.5f, 8f), wall);
            builder.AddBox(new Vector3(0f, RoofY - 1.2f, z + 4f), new Vector3(HalfX * 2f, 0.8f, 0.3f), darkSteel);
            builder.AddBox(new Vector3(0f, RoofY - 0.4f, z + 10f), new Vector3(HalfX * 2f, 0.3f, 0.2f), darkSteel);
        }
        foreach (float x in new[] { -25f, 0f, 25f })
        {
            builder.AddBox(new Vector3(x, RoofY - 0.5f, 0f), new Vector3(0.3f, 0.6f, HalfZ * 2f), darkSteel);
        }

        // 走道標線
        foreach (float x in new[] { -3.5f, 3.5f })
        {
            builder.AddBox(new Vector3(x, 0.01f, -50f), new Vector3(0.25f, 0.02f, 18f), hazard);
        }
        builder.AddBox(new Vector3(0f, 0.01f, -16f), new Vector3(HalfX * 2f - 4f, 0.02f, 0.25f), hazard);
        Finish(builder, "Shell", level);
    }

    #endregion

    #region 分區

    /// <summary>南側裝卸區：停機坪、捲門、棧板貨堆</summary>
    private static void BuildLoadingArea(Transform level)
    {
        CityMeshBuilder builder = NewBuilder();
        Vector3 pad = new Vector3(SpawnPoint.x, 0f, SpawnPoint.z);
        builder.AddBox(pad + new Vector3(0f, 0.15f, 0f), new Vector3(6f, 0.3f, 6f), darkSteel);
        builder.AddBox(pad + new Vector3(-1.2f, 0.31f, 0f), new Vector3(0.5f, 0.02f, 3.5f), white);
        builder.AddBox(pad + new Vector3(1.2f, 0.31f, 0f), new Vector3(0.5f, 0.02f, 3.5f), white);
        builder.AddBox(pad + new Vector3(0f, 0.31f, 0f), new Vector3(2.4f, 0.02f, 0.5f), white);

        // 南牆捲門 (關閉) 與警示框
        foreach (float x in new[] { -25f, 25f })
        {
            builder.AddBox(new Vector3(x, 7.6f, -HalfZ + 0.35f), new Vector3(8.4f, 0.7f, 0.7f), steel);   // 捲起的捲門
            builder.AddBox(new Vector3(x, 7.2f, -HalfZ + 0.2f), new Vector3(8.6f, 0.4f, 0.3f), hazard);
            builder.AddBox(new Vector3(x - 4.15f, 3.5f, -HalfZ + 0.2f), new Vector3(0.3f, 7f, 0.3f), hazard);
            builder.AddBox(new Vector3(x + 4.15f, 3.5f, -HalfZ + 0.2f), new Vector3(0.3f, 7f, 0.3f), hazard);
        }

        CrateStack(builder, new Vector3(-38f, 0f, -52f), 3, 0f);
        CrateStack(builder, new Vector3(-36.4f, 0f, -52f), 2, 0f);
        CrateStack(builder, new Vector3(-30f, 0f, -55f), 2, 15f);
        CrateStack(builder, new Vector3(-14f, 0f, -45f), 1, 0f);
        CrateStack(builder, new Vector3(28f, 0f, -54f), 3, 0f);
        CrateStack(builder, new Vector3(36f, 0f, -50f), 2, 30f);
        CrateStack(builder, new Vector3(44f, 0f, -56f), 3, 0f);
        CrateStack(builder, new Vector3(13f, 0f, -44f), 2, 0f);
        Finish(builder, "LoadingArea", level);
    }

    /// <summary>西側倉儲：三排 10m 高貨架 (沿 Z)，四層棧板，貨架之間 7.6m 走道</summary>
    private static void BuildStorageRacks(Transform level)
    {
        CityMeshBuilder builder = NewBuilder();
        System.Random random = new System.Random(31);
        const float z0 = -38f, z1 = -18f;
        float[] shelves = { 0f, 2.5f, 5f, 7.5f, 10f };
        foreach (float x in new[] { -44f, -34f, -24f })
        {
            for (float z = z0; z <= z1 + 0.01f; z += 4f)
            {
                builder.AddBox(new Vector3(x - 1.1f, 5f, z), new Vector3(0.15f, 10f, 0.15f), machineBlue);
                builder.AddBox(new Vector3(x + 1.1f, 5f, z), new Vector3(0.15f, 10f, 0.15f), machineBlue);
            }
            for (int tier = 1; tier < shelves.Length; tier++)
            {
                builder.AddBox(new Vector3(x, shelves[tier], (z0 + z1) * 0.5f), new Vector3(2.4f, 0.12f, z1 - z0), hazard);
            }
            for (int tier = 0; tier < shelves.Length; tier++)
            {
                for (int bay = 0; bay < 5; bay++)
                {
                    float z = z0 + 2f + bay * 4f;
                    // 中間貨架頂層第 3 格留給目標
                    if (tier == shelves.Length - 1 && ((x == -34f && bay == 2) || random.NextDouble() < 0.5)) continue;
                    if (random.NextDouble() < 0.3) continue;
                    float height = 0.9f + (float)random.NextDouble() * 0.6f;
                    builder.AddBox(new Vector3(x, shelves[tier] + 0.06f + height * 0.5f, z), new Vector3(2f, height, 3.2f), crate);
                }
            }
        }
        Finish(builder, "StorageRacks", level);
    }

    /// <summary>東側生產線：地面輸送帶穿過三台門型加工機、機械手臂、高架輸送帶、原型機組裝台</summary>
    private static void BuildProductionLine(Transform level)
    {
        CityMeshBuilder builder = NewBuilder();
        const float lineZ = -28f;
        builder.AddBox(new Vector3(25f, 0.9f, lineZ), new Vector3(40f, 0.25f, 1.6f), rubber);
        foreach (float side in new[] { -0.85f, 0.85f })
        {
            builder.AddBox(new Vector3(25f, 0.95f, lineZ + side), new Vector3(40f, 0.35f, 0.15f), steel);
        }
        for (float x = 6f; x <= 44f; x += 4f) builder.AddBox(new Vector3(x, 0.42f, lineZ), new Vector3(0.15f, 0.85f, 1.6f), darkSteel);

        foreach (float x in new[] { 11f, 23f, 35f })
        {
            builder.AddBox(new Vector3(x, 2f, lineZ - 2f), new Vector3(3f, 4f, 1.6f), machineGreen);
            builder.AddBox(new Vector3(x, 2f, lineZ + 2f), new Vector3(3f, 4f, 1.6f), machineGreen);
            builder.AddBox(new Vector3(x, 4.75f, lineZ), new Vector3(3f, 1.5f, 5.6f), machineGreen);
            builder.AddBox(new Vector3(x, 5.55f, lineZ), new Vector3(3.05f, 0.12f, 5.65f), hazard);
            builder.AddBox(new Vector3(x + 1.52f, 2.6f, lineZ - 2f), new Vector3(0.05f, 0.8f, 1.1f), screen);
        }
        foreach (float x in new[] { 17f, 29f })
        {
            builder.AddCylinder(Matrix4x4.identity, new Vector3(x, 0f, lineZ + 3f), Vector3.up, 0.6f, 1f, 12, hazard);
            builder.AddBox(new Vector3(x, 2.2f, lineZ + 3f), new Vector3(0.5f, 2.4f, 0.5f), hazard);
            builder.AddBox(new Vector3(x, 3.4f, lineZ + 1.9f), new Vector3(0.4f, 0.4f, 2.6f), hazard);
            builder.AddBox(new Vector3(x, 2.8f, lineZ + 0.8f), new Vector3(0.5f, 0.8f, 0.3f), darkSteel);
        }

        // 高架輸送帶 (4.5m 高，單柱支撐)
        const float upperZ = -20f;
        builder.AddBox(new Vector3(25f, 4.5f, upperZ), new Vector3(40f, 0.3f, 1.4f), rubber);
        foreach (float side in new[] { -0.75f, 0.75f }) builder.AddBox(new Vector3(25f, 4.7f, upperZ + side), new Vector3(40f, 0.3f, 0.1f), steel);
        for (float x = 7f; x <= 43f; x += 9f) builder.AddBox(new Vector3(x, 2.2f, upperZ), new Vector3(0.3f, 4.4f, 0.3f), steel);
        for (float x = 9f; x <= 41f; x += 6.5f) builder.AddBox(new Vector3(x, 5.15f, upperZ), new Vector3(1f, 1f, 1f), crate);

        // 原型機組裝台 (目標放在台面上)
        builder.AddBox(new Vector3(42f, 0.45f, -34f), new Vector3(3.5f, 0.9f, 3.5f), darkSteel);
        builder.AddBox(new Vector3(42f, 0.91f, -34f), new Vector3(3.6f, 0.04f, 3.6f), hazard);
        Finish(builder, "ProductionLine", level);
    }

    /// <summary>
    /// 中段管線迷宮 (x -48~20, z -14~14)：沿 X、沿 Z、垂直的管線與管架，下方有低矮鋼平台 (撞擊目標)。
    /// 留兩條敵機航道：z=-9、y≈2.5 (低) 與 z=3、y≈8.5 (中)，管線不擋這兩條。
    /// </summary>
    private static void BuildPipeMaze(Transform level)
    {
        CityMeshBuilder builder = NewBuilder();
        const float x0 = -48f;

        // 管架立柱與橫樑
        foreach (float x in new[] { -44f, -32f, -8f, 8f })
        {
            foreach (float z in new[] { -13f, 13f }) builder.AddBox(new Vector3(x, 8f, z), new Vector3(0.4f, 16f, 0.4f), steel);
            foreach (float y in new[] { 5f, 10f, 15f }) builder.AddBox(new Vector3(x, y, 0f), new Vector3(0.35f, 0.35f, 26f), steel);
        }

        // 沿 X 的管線 (z, y, 半徑, 顏色, 起點 x, 終點 x)
        (float z, float y, float r, int c, float end)[] alongX =
        {
            (-11f, 3.2f, 0.55f, 0, 20f), (-11f, 8.5f, 0.4f, 1, 20f),
            (-7f, 6f, 0.8f, 2, 28f), (-7f, 12.5f, 0.5f, 3, 20f),
            (-3f, 9.5f, 0.6f, 0, 20f), (-3f, 15.5f, 0.4f, 1, 20f),
            (1f, 5f, 0.45f, 3, 20f), (1f, 11f, 0.7f, 2, 28f),
            (5f, 7.5f, 0.5f, 1, 20f), (5f, 14f, 0.6f, 0, 20f),
            (9f, 3.5f, 0.6f, 3, 20f), (9f, 10f, 0.45f, 0, 20f),
            (12f, 6.5f, 0.4f, 1, 20f)
        };
        foreach ((float z, float y, float r, int c, float end) in alongX)
        {
            Pipe(builder, new Vector3(x0, y, z), Vector3.right, end - x0, r, pipeColors[c]);
        }

        // 沿 Z 的管線 (x, y, 半徑)
        (float x, float y, float r)[] alongZ = { (-38f, 6f, 0.5f), (-26f, 13f, 0.6f), (-14f, 5.5f, 0.45f), (-2f, 11.5f, 0.6f), (4f, 4.2f, 0.5f), (14f, 12.5f, 0.5f) };
        for (int i = 0; i < alongZ.Length; i++)
        {
            Pipe(builder, new Vector3(alongZ[i].x, alongZ[i].y, -14f), Vector3.forward, 28f, alongZ[i].r, pipeColors[(i + 1) % pipeColors.Length]);
        }

        // 垂直管線 (避開航道 z=-9 與 z=3)
        foreach (Vector2 p in new[] { new Vector2(-36f, -5f), new Vector2(-12f, 7f), new Vector2(2f, -1f), new Vector2(12f, -5f), new Vector2(-44f, 7f) })
        {
            builder.AddCylinder(Matrix4x4.identity, new Vector3(p.x, 0f, p.y), Vector3.up, 0.5f, 16f, 12, pipeColors[2]);
            builder.AddCylinder(Matrix4x4.identity, new Vector3(p.x, 0f, p.y), Vector3.up, 0.8f, 0.6f, 12, darkSteel);
        }

        // 低矮鋼平台 (頂 3.65m)：底下是冷卻泵 (撞擊目標)
        Vector3 deck = new Vector3(-20f, 3.5f, 0f);
        builder.AddBox(deck, new Vector3(8f, 0.3f, 6f), steel);
        builder.AddBox(deck + new Vector3(0f, 0.16f, -3f), new Vector3(8f, 0.05f, 0.15f), hazard);
        foreach (Vector2 leg in new[] { new Vector2(-3.7f, -2.7f), new Vector2(3.7f, -2.7f), new Vector2(-3.7f, 2.7f), new Vector2(3.7f, 2.7f) })
        {
            builder.AddBox(new Vector3(deck.x + leg.x, 1.75f, deck.z + leg.y), new Vector3(0.3f, 3.5f, 0.3f), darkSteel);
        }
        builder.AddCylinder(Matrix4x4.identity, new Vector3(-23f, 0f, 1.5f), Vector3.up, 0.7f, 1.6f, 12, machineBlue);
        builder.AddCylinder(Matrix4x4.identity, new Vector3(-17f, 0f, 1.5f), Vector3.up, 0.7f, 1.6f, 12, machineBlue);
        Finish(builder, "PipeMaze", level);
    }

    /// <summary>東側反應爐：13m 高鍋爐、7m 高環形走道 + 樓梯、兩座小儲槽</summary>
    private static void BuildReactor(Transform level)
    {
        CityMeshBuilder builder = NewBuilder();
        Vector3 center = new Vector3(34f, 0f, 0f);
        builder.AddCylinder(Matrix4x4.identity, center, Vector3.up, 6f, 13f, 32, boiler);
        builder.AddCylinder(Matrix4x4.identity, center + Vector3.up * 13f, Vector3.up, 6.3f, 0.3f, 32, steel);
        builder.AddCylinder(Matrix4x4.identity, center + Vector3.up * 4f, Vector3.up, 6.1f, 0.4f, 32, hazard);

        const float walkY = 7f, ring = 7.3f, width = 1.4f;
        builder.AddBox(center + new Vector3(0f, walkY, -ring), new Vector3(ring * 2f + width, 0.2f, width), steel);
        builder.AddBox(center + new Vector3(0f, walkY, ring), new Vector3(ring * 2f + width, 0.2f, width), steel);
        builder.AddBox(center + new Vector3(-ring, walkY, 0f), new Vector3(width, 0.2f, ring * 2f - width), steel);
        builder.AddBox(center + new Vector3(ring, walkY, 0f), new Vector3(width, 0.2f, ring * 2f - width), steel);
        float edge = ring + width * 0.5f;
        foreach (Vector2 corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
        {
            builder.AddBox(center + new Vector3(corner.x * edge, walkY * 0.5f, corner.y * edge), new Vector3(0.3f, walkY, 0.3f), darkSteel);
        }
        Vector3 walk = center + Vector3.up * walkY;
        Railing(builder, walk + new Vector3(edge, 0f, -edge), walk + new Vector3(edge, 0f, edge));
        Railing(builder, walk + new Vector3(-edge, 0f, edge), walk + new Vector3(edge, 0f, edge));
        Railing(builder, walk + new Vector3(-edge, 0f, -edge), walk + new Vector3(-edge, 0f, edge));
        // 南側樓梯：從地面往北爬到走道
        Stairs(builder, new Vector3(30f, 0f, -19f), Vector3.forward, 11f, walkY, 1.6f);

        foreach (float z in new[] { -12f, 12f })
        {
            builder.AddCylinder(Matrix4x4.identity, new Vector3(45f, 0f, z), Vector3.up, 2.5f, 8f, 20, pipeColors[1]);
            builder.AddCylinder(Matrix4x4.identity, new Vector3(45f, 8f, z), Vector3.up, 2.6f, 0.3f, 20, steel);
        }
        // 東牆垂直管線
        for (float z = -40f; z <= 40f; z += 16f)
        {
            if (Mathf.Abs(z - 25f) < 5f) continue;
            builder.AddCylinder(Matrix4x4.identity, new Vector3(HalfX - 1f, 0f, z), Vector3.up, 0.35f, RoofY - 1.5f, 10, pipeColors[3]);
        }
        Finish(builder, "Reactor", level);
    }

    /// <summary>
    /// 西北辦公樓 (x -49.7~-26, z 26~56)：兩層、每層三個房間 (門窗朝東，房間之間有內門)，
    /// 二樓外有陽台走道與樓梯。一樓南側為控制室、二樓中間為伺服器機房 (撞擊目標)。
    /// </summary>
    private static void BuildOfficeBlock(Transform level)
    {
        CityMeshBuilder builder = NewBuilder();
        const float xIn = -HalfX + 0.3f, xFacade = -26f, z0 = 26f, z1 = 56f;
        const float floorH = 5f, slab = 0.3f;
        float midX = (xIn + xFacade) * 0.5f, depth = xFacade - xIn;

        builder.AddBox(new Vector3(midX, floorH, (z0 + z1) * 0.5f), new Vector3(depth, slab, z1 - z0), darkSteel);
        builder.AddBox(new Vector3(midX, floorH * 2f, (z0 + z1) * 0.5f), new Vector3(depth, slab, z1 - z0), darkSteel);
        // 南北端牆
        builder.AddBox(new Vector3(midX, floorH, z0 + 0.15f), new Vector3(depth, floorH * 2f, 0.3f), office);
        builder.AddBox(new Vector3(midX, floorH, z1 - 0.15f), new Vector3(depth, floorH * 2f, 0.3f), office);

        for (int story = 0; story < 2; story++)
        {
            float y0 = story * floorH + (story == 0 ? 0f : slab * 0.5f);
            float y1 = (story + 1) * floorH - slab * 0.5f;
            float doorTop = y0 + 3.4f;
            // 東側立面：每個房間一扇門 + 兩扇窗 (沿牆距離從 z0 起算)
            WallWithOpenings(builder, new Vector3(xFacade, 0f, z0), new Vector3(xFacade, 0f, z1), y0, y1, 0.3f, office,
                (5f, 3.2f, y0, doorTop), (1.8f, 2f, y0 + 1.2f, y0 + 3.2f), (8.2f, 2f, y0 + 1.2f, y0 + 3.2f),
                (15f, 3.2f, y0, doorTop), (11.8f, 2f, y0 + 1.2f, y0 + 3.2f), (18.2f, 2f, y0 + 1.2f, y0 + 3.2f),
                (25f, 3.2f, y0, doorTop), (21.8f, 2f, y0 + 1.2f, y0 + 3.2f), (28.2f, 2f, y0 + 1.2f, y0 + 3.2f));
            // 房間隔牆 (內門靠西側)
            foreach (float z in new[] { 36f, 46f })
            {
                WallWithOpenings(builder, new Vector3(xFacade, 0f, z), new Vector3(xIn, 0f, z), y0, y1, 0.3f, office, (16f, 2.6f, y0, y0 + 3f));
            }
        }

        // 一樓控制室 (南側房間)：兩排操作台避開門口到目標的直線
        foreach (float z in new[] { 28.2f, 33.8f })
        {
            builder.AddBox(new Vector3(-36f, 0.45f, z), new Vector3(8f, 0.9f, 1.2f), darkSteel);
            builder.AddBox(new Vector3(-36f, 1.3f, z), new Vector3(7f, 0.8f, 0.08f), screen);
        }
        // 一樓其他房間：置物櫃與桌子
        for (float z = 38f; z <= 44f; z += 2f) builder.AddBox(new Vector3(-48.8f, 1f, z), new Vector3(1f, 2f, 1.6f), steel);
        builder.AddBox(new Vector3(-38f, 0.4f, 51f), new Vector3(4f, 0.8f, 2f), crate);
        // 二樓伺服器機房 (中間房間)：西側一排機櫃 + 東側兩組機櫃，中間留通道
        for (float z = 37.5f; z <= 44.5f; z += 1.4f) builder.AddBox(new Vector3(-47.5f, floorH + 1.25f, z), new Vector3(1.2f, 2.2f, 1.1f), darkSteel);
        foreach (float z in new[] { 37.8f, 44.2f }) builder.AddBox(new Vector3(-36f, floorH + 1.25f, z), new Vector3(4f, 2.2f, 1.1f), darkSteel);
        // 二樓其他房間
        builder.AddBox(new Vector3(-40f, floorH + 0.55f, 31f), new Vector3(5f, 0.8f, 2.4f), crate);
        builder.AddBox(new Vector3(-42f, floorH + 0.55f, 51f), new Vector3(3f, 0.8f, 6f), crate);

        // 二樓陽台走道 + 欄杆 + 樓梯 (從南側地面往北上)
        builder.AddBox(new Vector3(xFacade + 0.85f + 0.15f, floorH, (z0 + z1) * 0.5f), new Vector3(1.7f, 0.25f, z1 - z0), steel);
        Railing(builder, new Vector3(xFacade + 1.8f, floorH + 0.12f, z0), new Vector3(xFacade + 1.8f, floorH + 0.12f, z1));
        Stairs(builder, new Vector3(xFacade + 1f, 0f, z0 - 11f), Vector3.forward, 11f, floorH, 1.6f);
        Finish(builder, "OfficeBlock", level);
    }

    /// <summary>北側大廳：天車 (橫跨 15m 高)、CNC 機台、沖壓機、發電機房 (屋頂為投彈目標)、北牆夾層走道 + 樓梯</summary>
    private static void BuildNorthHall(Transform level)
    {
        CityMeshBuilder builder = NewBuilder();

        // 天車：大樑、兩端支柱、吊車台車、吊索與吊掛貨物
        builder.AddBox(new Vector3(13f, 15f, 36f), new Vector3(72f, 1.4f, 1.2f), hazard);
        builder.AddBox(new Vector3(-23f, 7.5f, 36f), new Vector3(0.8f, 15f, 0.8f), hazard);
        builder.AddBox(new Vector3(HalfX - 1f, 7.5f, 36f), new Vector3(0.8f, 15f, 0.8f), hazard);
        builder.AddBox(new Vector3(10f, 13.9f, 36f), new Vector3(2.5f, 1.2f, 2.5f), darkSteel);
        builder.AddBox(new Vector3(10f, 9.8f, 36f), new Vector3(0.1f, 7f, 0.1f), darkSteel);
        builder.AddBox(new Vector3(10f, 5.2f, 36f), new Vector3(3f, 2f, 3f), crate);

        // CNC 機台
        foreach (Vector2 p in new[] { new Vector2(4f, 22f), new Vector2(14f, 22f), new Vector2(4f, 30f), new Vector2(14f, 30f) })
        {
            builder.AddBox(new Vector3(p.x, 1.25f, p.y), new Vector3(4f, 2.5f, 3f), machineBlue);
            builder.AddBox(new Vector3(p.x, 2.55f, p.y), new Vector3(4.05f, 0.1f, 3.05f), hazard);
            builder.AddBox(new Vector3(p.x - 2.03f, 1.6f, p.y), new Vector3(0.05f, 0.7f, 1.2f), screen);
        }

        // 沖壓機：底座、四柱、上方機頭 (中間 5m 高的空隙可以穿越)
        Vector3 press = new Vector3(26f, 0f, 48f);
        builder.AddBox(press + new Vector3(0f, 1f, 0f), new Vector3(8f, 2f, 6f), machineGreen);
        foreach (Vector2 c in new[] { new Vector2(-3.5f, -2.5f), new Vector2(3.5f, -2.5f), new Vector2(-3.5f, 2.5f), new Vector2(3.5f, 2.5f) })
        {
            builder.AddBox(press + new Vector3(c.x, 4.5f, c.y), new Vector3(1f, 5f, 1f), steel);
        }
        builder.AddBox(press + new Vector3(0f, 8.25f, 0f), new Vector3(8f, 2.5f, 6f), machineGreen);
        builder.AddBox(press + new Vector3(0f, 9.55f, 0f), new Vector3(8.05f, 0.12f, 6.05f), hazard);

        // 發電機房：門朝西、南側有窗，屋頂 6m
        const float rx0 = 34f, rx1 = HalfX - 0.3f, rz0 = 40f, rz1 = 54f, roomH = 6f;
        WallWithOpenings(builder, new Vector3(rx0, 0f, rz0), new Vector3(rx0, 0f, rz1), 0f, roomH, 0.3f, office, (7f, 3f, 0f, 3.5f));
        WallWithOpenings(builder, new Vector3(rx0, 0f, rz0), new Vector3(rx1, 0f, rz0), 0f, roomH, 0.3f, office, (8f, 4f, 1.5f, 3.5f));
        WallWithOpenings(builder, new Vector3(rx0, 0f, rz1), new Vector3(rx1, 0f, rz1), 0f, roomH, 0.3f, office);
        builder.AddBox(new Vector3((rx0 + rx1) * 0.5f, roomH, (rz0 + rz1) * 0.5f), new Vector3(rx1 - rx0 + 0.3f, 0.3f, rz1 - rz0 + 0.3f), darkSteel);
        foreach (float z in new[] { 44f, 50f })
        {
            builder.AddCylinder(Matrix4x4.identity, new Vector3(37f, 1.3f, z), Vector3.right, 1.2f, 7f, 16, machineGreen);
        }

        // 北牆夾層走道 (9m) + 立柱 + 欄杆 + 樓梯 (從 z 44 往北上)
        const float mezzY = 9f, mz0 = 56.4f;
        builder.AddBox(new Vector3(13f, mezzY, (mz0 + HalfZ) * 0.5f), new Vector3(74f, 0.25f, HalfZ - mz0), steel);
        for (float x = -22f; x <= HalfX - 2f; x += 9f) builder.AddBox(new Vector3(x, mezzY * 0.5f, mz0 + 0.2f), new Vector3(0.3f, mezzY, 0.3f), darkSteel);
        Railing(builder, new Vector3(-24f, mezzY + 0.12f, mz0), new Vector3(-1f, mezzY + 0.12f, mz0));
        Railing(builder, new Vector3(1f, mezzY + 0.12f, mz0), new Vector3(HalfX, mezzY + 0.12f, mz0));
        Stairs(builder, new Vector3(0f, 0f, mz0 - 12.4f), Vector3.forward, 12.4f, mezzY, 1.6f);
        foreach ((float y, float r) in new[] { (12f, 0.35f), (13.5f, 0.45f) })
        {
            Pipe(builder, new Vector3(-24f, y, HalfZ - 0.8f), Vector3.right, HalfX + 23f, r, pipeColors[y > 13f ? 0 : 3]);
        }
        Finish(builder, "NorthHall", level);
    }

    /// <summary>屋架下方的吊燈 (自發光燈箱 + 不投影點光源)</summary>
    private static void BuildLights(Transform level)
    {
        CityMeshBuilder builder = NewBuilder();
        Transform lights = new GameObject("Lights").transform;
        lights.SetParent(level, false);
        foreach (float x in new[] { -36f, -12f, 12f, 36f })
        {
            foreach (float z in new[] { -45f, -21f, 3f, 27f, 51f })
            {
                Vector3 position = new Vector3(x, RoofY - 2.4f, z);
                builder.AddBox(position, new Vector3(1.8f, 0.2f, 0.6f), lamp);
                builder.AddBox(position + Vector3.up * 0.8f, new Vector3(0.05f, 1.6f, 0.05f), darkSteel);

                Light light = new GameObject($"Lamp_{x}_{z}").AddComponent<Light>();
                light.transform.SetParent(lights, false);
                light.transform.position = position - Vector3.up * 0.5f;
                light.type = LightType.Point;
                light.range = 24f;
                light.intensity = 2.2f;
                light.color = new Color(1f, 0.95f, 0.86f);
                light.shadows = LightShadows.None;
                light.lightmapBakeType = LightmapBakeType.Realtime;
            }
        }
        Finish(builder, "LampFixtures", level, collider: false);
    }

    #endregion

    #region 細節與擺設

    private const string StreetProps = "Assets/Download/LowpolyStreetPack/Prefabs/StreetProps";

    /// <summary>
    /// 廠房細節 (都避開敵機航道、撞擊目標的飛入路線與起飛點走道)：
    /// 堆高機、棧板貨物、油桶、牆邊通風風管與吊桿、電纜橋架、滅火器與緊急出口燈、生產線安全護欄、天車吊掛區警示框、
    /// 工作台與工具櫃、鋼捲、Download 素材的配電箱 / 三角錐 / 裝卸區貨車。
    /// </summary>
    private static void BuildDetails(Transform level)
    {
        CityMeshBuilder builder = NewBuilder();
        System.Random random = new System.Random(64);
        Material exitSign = Mat("ExitSign", new Color(0.1f, 0.7f, 0.3f), 0.5f, new Color(0.15f, 1f, 0.4f) * 1.6f);
        Material fireRed = Mat("FireRed", new Color(0.75f, 0.08f, 0.06f), 0.5f);

        // 堆高機
        Forklift(builder, new Vector3(-22f, 0f, -47f), 20f, true);
        Forklift(builder, new Vector3(20f, 0f, -47f), 200f, false);
        Forklift(builder, new Vector3(-39f, 0f, -41f), 90f, true);

        // 棧板貨物
        foreach (Vector3 spot in new[] { new Vector3(-30f, 0f, -45f), new Vector3(-27.5f, 0f, -45f), new Vector3(7f, 0f, -40f), new Vector3(9.5f, 0f, -38f),
                     new Vector3(-46f, 0f, -14.5f), new Vector3(46f, 0f, -45f), new Vector3(46f, 0f, -42.5f) })
        {
            Pallet(builder, spot, random);
        }

        // 油桶群
        foreach (Vector3 center in new[] { new Vector3(46f, 0f, -2f), new Vector3(-44f, 0f, -57f), new Vector3(40f, 0f, 18f) })
        {
            for (int i = 0; i < 6; i++)
            {
                Vector3 offset = new Vector3((i % 3) * 0.68f, 0f, (i / 3) * 0.68f);
                if (random.NextDouble() < 0.15) continue;
                Material color = pipeColors[random.Next(pipeColors.Length)];
                builder.AddCylinder(Matrix4x4.identity, center + offset, Vector3.up, 0.3f, 0.9f, 14, color);
                builder.AddCylinder(Matrix4x4.identity, center + offset + Vector3.up * 0.3f, Vector3.up, 0.31f, 0.04f, 14, darkSteel);
                builder.AddCylinder(Matrix4x4.identity, center + offset + Vector3.up * 0.6f, Vector3.up, 0.31f, 0.04f, 14, darkSteel);
            }
        }

        // 東西牆邊通風風管 (屋架下方 17m) 與吊桿
        foreach (float x in new[] { -HalfX + 2.5f, HalfX - 3.4f })
        {
            builder.AddBox(new Vector3(x, 17f, -1f), new Vector3(1.2f, 1f, 114f), steel);
            for (float z = -56f; z <= 54f; z += 6f)
            {
                builder.AddBox(new Vector3(x, 17.55f, z), new Vector3(1.3f, 0.1f, 0.12f), darkSteel);
                builder.AddBox(new Vector3(x - 0.55f, 18.4f, z), new Vector3(0.04f, 1.6f, 0.04f), darkSteel);
                builder.AddBox(new Vector3(x + 0.55f, 18.4f, z), new Vector3(0.04f, 1.6f, 0.04f), darkSteel);
            }
            for (float z = -48f; z <= 48f; z += 24f) builder.AddBox(new Vector3(x + (x < 0f ? 0.61f : -0.61f), 17f, z), new Vector3(0.04f, 0.6f, 0.9f), darkSteel); // 出風口
        }

        // 南牆電纜橋架 (12m)
        builder.AddBox(new Vector3(0f, 12f, -HalfZ + 0.5f), new Vector3(96f, 0.06f, 0.5f), steel);
        builder.AddBox(new Vector3(0f, 12.08f, -HalfZ + 0.27f), new Vector3(96f, 0.16f, 0.04f), steel);
        builder.AddBox(new Vector3(0f, 12.08f, -HalfZ + 0.73f), new Vector3(96f, 0.16f, 0.04f), steel);
        builder.AddBox(new Vector3(0f, 12.06f, -HalfZ + 0.5f), new Vector3(96f, 0.06f, 0.3f), rubber);

        // 滅火器 (東牆、南牆) 與緊急出口燈 (捲門上方)
        for (float z = -52f; z <= 40f; z += 18f)
        {
            builder.AddCylinder(Matrix4x4.identity, new Vector3(HalfX - 0.25f, 0.6f, z), Vector3.up, 0.09f, 0.5f, 10, fireRed);
            builder.AddBox(new Vector3(HalfX - 0.05f, 1.6f, z), new Vector3(0.05f, 0.5f, 0.35f), fireRed);
        }
        foreach (float x in new[] { -40f, -10f, 10f, 40f })
        {
            builder.AddCylinder(Matrix4x4.identity, new Vector3(x, 0.6f, -HalfZ + 0.25f), Vector3.up, 0.09f, 0.5f, 10, fireRed);
        }
        foreach (float x in new[] { -25f, 25f }) builder.AddBox(new Vector3(x, 7.9f, -HalfZ + 0.3f), new Vector3(1f, 0.35f, 0.08f), exitSign);
        foreach (float z in new[] { 31f, 41f, 51f }) builder.AddBox(new Vector3(-25.82f, 3.75f, z), new Vector3(0.06f, 0.25f, 0.7f), exitSign);

        // 生產線南側安全護欄
        Railing(builder, new Vector3(4f, 0f, -36.5f), new Vector3(46f, 0f, -36.5f));

        // 天車吊掛區：地面黃黑相間警示框
        for (int i = 0; i < 10; i++)
        {
            Material stripe = i % 2 == 0 ? hazard : darkSteel;
            float t = -3f + i * 0.6f + 0.3f;
            builder.AddBox(new Vector3(10f + t, 0.015f, 33f), new Vector3(0.6f, 0.02f, 0.2f), stripe);
            builder.AddBox(new Vector3(10f + t, 0.015f, 39f), new Vector3(0.6f, 0.02f, 0.2f), stripe);
            builder.AddBox(new Vector3(7f, 0.015f, 36f + t), new Vector3(0.2f, 0.02f, 0.6f), stripe);
            builder.AddBox(new Vector3(13f, 0.015f, 36f + t), new Vector3(0.2f, 0.02f, 0.6f), stripe);
        }

        // CNC 旁工作台與工具櫃
        foreach (float z in new[] { 22f, 26f })
        {
            builder.AddBox(new Vector3(22f, 0.88f, z), new Vector3(2.2f, 0.06f, 0.8f), crate);
            foreach (Vector2 leg in new[] { new Vector2(-1f, -0.35f), new Vector2(1f, -0.35f), new Vector2(-1f, 0.35f), new Vector2(1f, 0.35f) })
            {
                builder.AddBox(new Vector3(22f + leg.x, 0.43f, z + leg.y), new Vector3(0.06f, 0.86f, 0.06f), darkSteel);
            }
            builder.AddBox(new Vector3(21.4f, 1.05f, z), new Vector3(0.45f, 0.28f, 0.3f), fireRed);   // 工具箱
            builder.AddBox(new Vector3(22.7f, 0.98f, z - 0.2f), new Vector3(0.2f, 0.14f, 0.15f), darkSteel); // 虎鉗
        }
        builder.AddBox(new Vector3(24.2f, 0.65f, 30f), new Vector3(0.6f, 1.3f, 1.2f), fireRed);

        // 沖壓機旁鋼捲
        foreach (Vector3 coil in new[] { new Vector3(12f, 0.8f, 47f), new Vector3(12f, 0.8f, 49.6f), new Vector3(15f, 0.8f, 47f) })
        {
            builder.AddCylinder(Matrix4x4.identity, coil - Vector3.forward * 0.6f, Vector3.forward, 0.8f, 1.2f, 20, steel);
            builder.AddCylinder(Matrix4x4.identity, coil - Vector3.forward * 0.61f, Vector3.forward, 0.3f, 1.22f, 12, darkSteel);
        }

        Finish(builder, "Details", level);

        // Download 素材：配電箱、三角錐、裝卸區貨車
        Transform props = new GameObject("Props").transform;
        props.SetParent(level, false);
        Dictionary<Material, Material> remap = CityLevelBaker.StreetPackUrpRemap();
        foreach ((Vector3 position, float yaw) in new[]
                 {
                     (new Vector3(-HalfX + 0.4f, 0f, -50f), 90f), (new Vector3(-HalfX + 0.4f, 0f, -20f), 90f),
                     (new Vector3(HalfX - 0.4f, 0f, -36f), 270f), (new Vector3(HalfX - 0.4f, 0f, 33f), 270f)
                 })
        {
            PlaceProp($"{StreetProps}/FuseBox/FuseBox.prefab", props, remap, position, yaw);
        }
        foreach (Vector3 cone in new[] { new Vector3(-19f, 0f, -44f), new Vector3(-18f, 0f, -45.2f), new Vector3(17.5f, 0f, -44.5f), new Vector3(-38f, 0f, -44f) })
        {
            PlaceProp($"{StreetProps}/RoadBlocks/RoadCone_A.prefab", props, remap, cone, 0f);
        }
        GameObject van = StrikeSceneBuilder.CarPrefabs().FirstOrDefault(prefab => prefab.name.Contains("Van"));
        if (van != null)
        {
            GameObject truck = CityTraffic.CreateCarModel(van, props, out _, out Vector3 size);
            truck.transform.SetPositionAndRotation(new Vector3(-14f, 0f, -55.5f), Quaternion.identity);
            BoxCollider box = truck.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, size.y * 0.5f, 0f);
            box.size = size;
            truck.isStatic = true;
        }
    }

    /// <summary>堆高機：車身、配重、護頂架、門架與貨叉、車輪 (frame +Z 為貨叉方向)</summary>
    private static void Forklift(CityMeshBuilder builder, Vector3 position, float yaw, bool loaded)
    {
        Matrix4x4 frame = Matrix4x4.TRS(position, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
        builder.AddBox(frame, new Vector3(0f, 0.75f, 0f), new Vector3(1.2f, 0.9f, 2f), hazard);
        builder.AddBox(frame, new Vector3(0f, 0.65f, -1.05f), new Vector3(1.2f, 0.9f, 0.4f), darkSteel);
        builder.AddBox(frame, new Vector3(0f, 1.35f, -0.3f), new Vector3(0.6f, 0.35f, 0.5f), darkSteel);   // 座椅
        foreach (float x in new[] { -0.55f, 0.55f })
        {
            foreach (float z in new[] { -0.6f, 0.55f }) builder.AddBox(frame, new Vector3(x, 1.85f, z), new Vector3(0.06f, 1.3f, 0.06f), darkSteel);
            builder.AddBox(frame, new Vector3(x * 0.75f, 1.35f, 1.12f), new Vector3(0.1f, 2.7f, 0.1f), darkSteel);   // 門架
            builder.AddBox(frame, new Vector3(x * 0.55f, 0.12f, 1.75f), new Vector3(0.12f, 0.05f, 1.1f), steel);     // 貨叉
            foreach (float z in new[] { -0.7f, 0.65f })
            {
                builder.AddCylinder(frame, new Vector3(x * 1.13f, 0.3f, z), Vector3.right * Mathf.Sign(x), 0.3f, 0.22f, 14, rubber);
            }
        }
        builder.AddBox(frame, new Vector3(0f, 2.52f, -0.02f), new Vector3(1.2f, 0.06f, 1.25f), darkSteel);  // 護頂
        if (loaded)
        {
            builder.AddBox(frame, new Vector3(0f, 0.25f, 1.75f), new Vector3(1.1f, 0.14f, 1.1f), crate);
            builder.AddBox(frame, new Vector3(0f, 0.85f, 1.75f), new Vector3(1f, 1f, 1f), crate);
        }
    }

    private static void Pallet(CityMeshBuilder builder, Vector3 ground, System.Random random)
    {
        builder.AddBox(ground + Vector3.up * 0.07f, new Vector3(1.2f, 0.14f, 1f), crate);
        int layers = 1 + random.Next(2);
        for (int i = 0; i < layers; i++)
        {
            float size = 1f - i * 0.15f;
            builder.AddBox(ground + Vector3.up * (0.14f + 0.45f + i * 0.9f), new Vector3(size, 0.9f, size * 0.85f), crate);
        }
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

    /// <summary>敵方軍人出生點 (起飛點 (0, -52)，皆在 45m 外)：夾層走道精確射手、反應爐走道、辦公樓陽台與機房、生產線機槍兵、發電機房、管線迷宮</summary>
    private static void BuildSoldiers(Transform level)
    {
        Transform root = new GameObject("Soldiers").transform;
        root.SetParent(level, false);
        int count = 0;
        SoldierSetup.AddPoint(root, "Mezzanine_Marksman", SoldierSetup.Prefab("Marksman"), new Vector3(-15f, 9.125f, 58f), 90f, new Vector3(20f, 9.125f, 58f), ref count);
        SoldierSetup.AddPoint(root, "Reactor_Patrol", SoldierSetup.Prefab("Carbine"), new Vector3(26.7f, 7.1f, -5f), 0f, new Vector3(26.7f, 7.1f, 5f), ref count);
        SoldierSetup.AddPoint(root, "Balcony_Patrol", SoldierSetup.Prefab("Rifle"), new Vector3(-25f, 5.125f, 30f), 0f, new Vector3(-25f, 5.125f, 52f), ref count);
        SoldierSetup.AddPoint(root, "ServerRoom_Guard", SoldierSetup.Prefab("Carbine"), new Vector3(-31f, 5.15f, 41f), 90f, null, ref count);
        SoldierSetup.AddPoint(root, "Production_MG", SoldierSetup.Prefab("MachineGunner"), new Vector3(38f, 0f, -22.5f), 200f, null, ref count);
        SoldierSetup.AddPoint(root, "Generator_Guard", SoldierSetup.Prefab("Rifle"), new Vector3(46f, 0f, 52f), 270f, null, ref count);
        SoldierSetup.AddPoint(root, "Maze_Guard", SoldierSetup.Prefab("Rifle"), new Vector3(-26f, 0f, -5f), 90f, null, ref count);

        // 廠區戶外 (起飛點 (-25, -130)，皆在各自射程外)：
        // 出入口 —— A 棟南側捲門之間的機槍陣地、A 棟西側門、B 棟大門機槍兵
        SoldierSetup.AddPoint(root, "HallA_South_MG", SoldierSetup.Prefab("MachineGunner"), new Vector3(0f, 0f, -66f), 180f, null, ref count);
        SoldierSetup.AddPoint(root, "HallA_West_Guard", SoldierSetup.Prefab("Carbine"), new Vector3(-56f, 0f, 6f), 270f, null, ref count);
        SoldierSetup.AddPoint(root, "HallB_Door_MG", SoldierSetup.Prefab("MachineGunner"), new Vector3(76f, 0f, 28f), 270f, null, ref count);
        // 制高點 —— 辦公大樓屋頂、門式起重機頂的精確射手
        SoldierSetup.AddPoint(root, "OfficeC_Marksman", SoldierSetup.Prefab("Marksman"), new Vector3(95f, 16.4f, -50f), 180f, null, ref count);
        SoldierSetup.AddPoint(root, "Gantry_Marksman", SoldierSetup.Prefab("Marksman"), new Vector3(90f, 19.3f, 130f), 180f, null, ref count);
        // 巡邏 —— 貨車場、儲槽區、北側堆置場；變電站站崗
        SoldierSetup.AddPoint(root, "TruckYard_Patrol", SoldierSetup.Prefab("Rifle"), new Vector3(5f, 0f, -84f), 90f, new Vector3(45f, 0f, -84f), ref count);
        SoldierSetup.AddPoint(root, "TankFarm_Patrol", SoldierSetup.Prefab("Carbine"), new Vector3(-112f, 0f, 5f), 0f, new Vector3(-112f, 0f, 80f), ref count);
        SoldierSetup.AddPoint(root, "NorthYard_Patrol", SoldierSetup.Prefab("Rifle"), new Vector3(-30f, 0f, 102f), 90f, new Vector3(30f, 0f, 102f), ref count);
        SoldierSetup.AddPoint(root, "Substation_Guard", SoldierSetup.Prefab("Rifle"), new Vector3(110f, 0f, -108f), 180f, null, ref count);
    }

    /// <summary>後處理 (與港區共用的 ACES 調色 Volume) + 執行時開啟攝影機後處理</summary>
    private static void BuildRealism(Transform level)
    {
        GameObject realism = new GameObject("Realism");
        realism.transform.SetParent(level, false);
        Volume volume = realism.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1f;
        volume.sharedProfile = PortRealismBuilder.BuildProfile();
        realism.AddComponent<SceneRealism>();
    }

    #endregion

    #region 工業區外景

    // 廠區範圍 (A 棟廠房在原點)
    private static readonly Vector2 SiteMin = new Vector2(-160f, -160f);
    private static readonly Vector2 SiteMax = new Vector2(160f, 180f);
    private const float SiteCeiling = 60f;
    private const string PeoplePrefabs = "Assets/Download/DenysAlmaral/CityPeople/Prefabs";

    /// <summary>
    /// 廠區外景：場地鋪面與道路、B 棟裝配廠、C 棟辦公大樓、儲槽區、管線橋、冷卻塔與煙囪、變電站、停車場、貨車場、
    /// 北側鋼材堆置棚與戶外門式起重機、圍籬與大門、路燈。A 棟以外的建築與設施都在環狀道路外側。
    /// </summary>
    private static void BuildSite(Transform level)
    {
        CityMeshBuilder builder = NewBuilder();
        Material yard = Mat("Yard", new Color(0.66f, 0.66f, 0.64f), 0.2f);
        Material asphalt = Mat("Asphalt", new Color(0.9f, 0.9f, 0.9f), 0.25f);
        Material glass = Mat("Glass", new Color(0.12f, 0.18f, 0.22f), 0.92f);
        Material concreteWall = Mat("ConcreteWall", new Color(0.72f, 0.71f, 0.68f), 0.2f);
        ProceduralTextures.Apply(yard, ProceduralTextures.Concrete(), 1f, 8f, null, 0.2f, 0f, 0.6f);
        ProceduralTextures.Apply(asphalt, ProceduralTextures.Asphalt(), 1f, 6f, null, 0.25f, 0f, 0.8f);
        ProceduralTextures.Apply(concreteWall, ProceduralTextures.Grime(), 1f, 4f, null, 0.2f, 0f, 0.4f);
        System.Random random = new System.Random(91);

        // 場地鋪面 (略低於 A 棟地坪，不會重疊閃爍) 與道路
        Vector2 size = SiteMax - SiteMin, center = (SiteMin + SiteMax) * 0.5f;
        builder.AddBox(new Vector3(center.x, -0.27f, center.y), new Vector3(size.x + 40f, 0.5f, size.y + 40f), yard);
        void Road(Vector3 c, Vector3 s) => builder.AddBox(c + Vector3.up * 0.005f, new Vector3(s.x, 0.04f, s.z), asphalt);
        Road(new Vector3(20f, 0f, -75f), new Vector3(190f, 0f, 10f));
        Road(new Vector3(20f, 0f, 87f), new Vector3(190f, 0f, 10f));
        Road(new Vector3(-75f, 0f, 6f), new Vector3(10f, 0f, 172f));
        Road(new Vector3(65f, 0f, 6f), new Vector3(10f, 0f, 172f));
        Road(new Vector3(-25f, 0f, -118f), new Vector3(10f, 0f, 76f));   // 南側大門 → 環狀道路
        for (float x = -70f; x <= 110f; x += 6f) builder.AddBox(new Vector3(x, 0.03f, -75f), new Vector3(3f, 0.02f, 0.18f), white);
        for (float z = -70f; z <= 80f; z += 6f) builder.AddBox(new Vector3(65f, 0.03f, z), new Vector3(0.18f, 0.02f, 3f), white);

        // B 棟裝配廠 (x 80~140, z 10~60, 高 14)：西側大門、南側小門
        const float bx0 = 80f, bx1 = 140f, bz0 = 10f, bz1 = 60f, bh = 14f;
        WallWithOpenings(builder, new Vector3(bx0, 0f, bz0), new Vector3(bx0, 0f, bz1), 0f, bh, 0.5f, wall, (25f, 10f, 0f, 8f));
        WallWithOpenings(builder, new Vector3(bx0, 0f, bz0), new Vector3(bx1, 0f, bz0), 0f, bh, 0.5f, wall, (15f, 5f, 0f, 5f));
        WallWithOpenings(builder, new Vector3(bx0, 0f, bz1), new Vector3(bx1, 0f, bz1), 0f, bh, 0.5f, wall);
        WallWithOpenings(builder, new Vector3(bx1, 0f, bz0), new Vector3(bx1, 0f, bz1), 0f, bh, 0.5f, wall);
        builder.AddBox(new Vector3((bx0 + bx1) * 0.5f, bh + 0.2f, (bz0 + bz1) * 0.5f), new Vector3(bx1 - bx0 + 1f, 0.4f, bz1 - bz0 + 1f), wall);
        builder.AddBox(new Vector3(bx0 - 0.4f, 8.6f, 35f), new Vector3(0.8f, 0.7f, 10.4f), steel);   // 捲門收納箱
        for (float x = bx0 + 10f; x < bx1; x += 10f)
        {
            builder.AddBox(new Vector3(x, bh * 0.5f, 25f), new Vector3(0.5f, bh, 0.5f), steel);
            builder.AddBox(new Vector3(x, bh * 0.5f, 45f), new Vector3(0.5f, bh, 0.5f), steel);
            builder.AddBox(new Vector3(x, bh - 0.6f, 35f), new Vector3(0.4f, 0.8f, bz1 - bz0), darkSteel);   // 屋架
        }
        foreach (float z in new[] { 18f, 52f })
        {
            for (float x = bx0 + 8f; x < bx1 - 6f; x += 6f) Pallet(builder, new Vector3(x, 0f, z), random);
        }
        builder.AddBox(new Vector3(122f, 0.9f, 35f), new Vector3(14f, 1.8f, 4f), machineGreen);         // 裝配平台
        builder.AddBox(new Vector3(122f, 1.85f, 35f), new Vector3(14.1f, 0.1f, 4.1f), hazard);

        // C 棟辦公大樓 (x 80~110, z -70~-45，四層 16m)：玻璃窗帶、入口雨遮、屋頂空調機組
        const float cx0 = 80f, cx1 = 110f, cz0 = -70f, cz1 = -45f;
        builder.AddBox(new Vector3((cx0 + cx1) * 0.5f, 8f, (cz0 + cz1) * 0.5f), new Vector3(cx1 - cx0, 16f, cz1 - cz0), concreteWall);
        for (int floorIndex = 0; floorIndex < 4; floorIndex++)
        {
            builder.AddBox(new Vector3((cx0 + cx1) * 0.5f, 2.2f + floorIndex * 4f, (cz0 + cz1) * 0.5f), new Vector3(cx1 - cx0 + 0.1f, 1.6f, cz1 - cz0 + 0.1f), glass);
        }
        builder.AddBox(new Vector3((cx0 + cx1) * 0.5f, 16.2f, (cz0 + cz1) * 0.5f), new Vector3(cx1 - cx0 + 0.6f, 0.4f, cz1 - cz0 + 0.6f), darkSteel);
        builder.AddBox(new Vector3(cx0 - 1.5f, 3.2f, -57.5f), new Vector3(3f, 0.2f, 8f), steel);       // 入口雨遮
        for (int i = 0; i < 4; i++) builder.AddBox(new Vector3(cx0 + 5f + i * 6f, 17.2f, -60f), new Vector3(3f, 1.6f, 2.2f), steel);

        // 儲槽區 (防溢堤圍住六座儲槽、爬梯、頂部閥組)
        builder.AddBox(new Vector3(-115f, 0.6f, -5f), new Vector3(60f, 1.2f, 0.5f), concreteWall);
        builder.AddBox(new Vector3(-115f, 0.6f, 85f), new Vector3(60f, 1.2f, 0.5f), concreteWall);
        builder.AddBox(new Vector3(-145f, 0.6f, 40f), new Vector3(0.5f, 1.2f, 90f), concreteWall);
        builder.AddBox(new Vector3(-85f, 0.6f, 40f), new Vector3(0.5f, 1.2f, 90f), concreteWall);
        foreach (float tx in new[] { -130f, -100f })
        {
            foreach (float tz in new[] { 10f, 40f, 70f })
            {
                Vector3 baseCenter = new Vector3(tx, 0f, tz);
                builder.AddCylinder(Matrix4x4.identity, baseCenter, Vector3.up, 9f, 12f, 32, boiler);
                builder.AddCylinder(Matrix4x4.identity, baseCenter + Vector3.up * 12f, Vector3.up, 9.2f, 0.3f, 32, steel);
                builder.AddBox(baseCenter + new Vector3(9.1f, 6f, 0f), new Vector3(0.15f, 12f, 0.7f), hazard);
                builder.AddCylinder(Matrix4x4.identity, baseCenter + new Vector3(0f, 2f, 0f), Vector3.right, 0.5f, 12f, 10, pipeColors[0]);
            }
        }

        // 管線橋：儲槽區 → A 棟西牆 (z 40, 8m)、A 棟東牆 → B 棟 (z 50, 9m)
        PipeBridge(builder, new Vector3(-85f, 8f, 40f), new Vector3(-HalfX - 0.6f, 8f, 40f));
        PipeBridge(builder, new Vector3(HalfX + 0.6f, 9f, 50f), new Vector3(bx0, 9f, 50f));

        // 冷卻塔 (上下寬、中間窄) 與煙囪 (紅白相間)
        foreach (Vector3 tower in new[] { new Vector3(-122f, 0f, -102f), new Vector3(-96f, 0f, -125f) })
        {
            float[] radii = { 11f, 10f, 9f, 8.4f, 8.2f, 8.6f, 9.2f };
            for (int i = 0; i < radii.Length; i++)
            {
                builder.AddCylinder(Matrix4x4.identity, tower + Vector3.up * (i * 4f), Vector3.up, radii[i], 4f, 32, concreteWall);
            }
        }
        for (int band = 0; band < 8; band++)
        {
            builder.AddCylinder(Matrix4x4.identity, new Vector3(-70f, band * 6f, -125f), Vector3.up, 2.4f - band * 0.08f, 6f, 20, band % 2 == 0 ? white : pipeColors[0]);
        }

        // 變電站 (圍籬內的變壓器與門型鐵架)
        for (int i = 0; i < 3; i++)
        {
            Vector3 transformer = new Vector3(118f + i * 10f, 0f, -130f);
            builder.AddBox(transformer + Vector3.up * 1.6f, new Vector3(4f, 3.2f, 3f), machineGreen);
            for (int fin = 0; fin < 6; fin++) builder.AddBox(transformer + new Vector3(-1.5f + fin * 0.6f, 1.6f, 1.7f), new Vector3(0.08f, 2.8f, 0.4f), darkSteel);
            for (int bushing = -1; bushing <= 1; bushing++)
            {
                builder.AddCylinder(Matrix4x4.identity, transformer + new Vector3(bushing * 1.1f, 3.2f, 0f), Vector3.up, 0.15f, 1.4f, 8, white);
            }
            builder.AddBox(transformer + new Vector3(0f, 6f, -5f), new Vector3(0.4f, 12f, 0.4f), steel);
        }
        builder.AddBox(new Vector3(128f, 11.8f, -135f), new Vector3(26f, 0.4f, 0.4f), steel);
        Fence(builder, new Vector2(108f, -150f), new Vector2(150f, -112f));

        // 停車場 (x 75~140, z -30~0) 車格線
        builder.AddBox(new Vector3(107f, 0.012f, -15f), new Vector3(65f, 0.02f, 30f), asphalt);
        for (float x = 76f; x <= 138f; x += 2.8f)
        {
            builder.AddBox(new Vector3(x, 0.03f, -26f), new Vector3(0.12f, 0.02f, 5f), white);
            builder.AddBox(new Vector3(x, 0.03f, -4f), new Vector3(0.12f, 0.02f, 5f), white);
        }

        // 貨車場 (A 棟南側、環狀道路外)：貨櫃拖車
        Material[] trailerColors = { pipeColors[0], pipeColors[3], machineGreen, crate };
        for (int i = 0; i < 5; i++)
        {
            float x = 10f + i * 7f;
            builder.AddBox(new Vector3(x, 1.1f, -95f), new Vector3(2.4f, 0.35f, 12.4f), rubber);
            builder.AddBox(new Vector3(x, 2.6f, -95f), new Vector3(2.4f, 2.55f, 12f), trailerColors[i % trailerColors.Length]);
            builder.AddBox(new Vector3(x, 1.8f, -87.5f), new Vector3(2.4f, 2.9f, 2.4f), i % 2 == 0 ? white : hazard);
        }

        // 北側鋼材堆置棚 (x -40~40, z 110~140，柱撐屋頂 12m) 與鋼捲/鋼管堆
        for (float x = -40f; x <= 40f; x += 10f)
        {
            foreach (float z in new[] { 110f, 140f }) builder.AddBox(new Vector3(x, 6f, z), new Vector3(0.6f, 12f, 0.6f), steel);
        }
        builder.AddBox(new Vector3(0f, 12.2f, 125f), new Vector3(82f, 0.4f, 32f), wall);
        for (float x = -34f; x <= 34f; x += 4.5f)
        {
            if (random.NextDouble() < 0.3) continue;
            builder.AddCylinder(Matrix4x4.identity, new Vector3(x, 0.9f, 117f), Vector3.forward, 0.9f, 1.4f, 20, steel);
        }
        for (int layer = 0; layer < 3; layer++)
        {
            for (int i = 0; i < 6 - layer; i++)
            {
                builder.AddCylinder(Matrix4x4.identity, new Vector3(-30f + i * 0.9f + layer * 0.45f, 0.4f + layer * 0.78f, 128f), Vector3.right * -1f + Vector3.right * 2f, 0.4f, 24f, 12, pipeColors[1]);
            }
        }

        // 戶外門式起重機 (x 60~120, z 100~160)：兩條軌道上的黃色門架
        foreach (float x in new[] { 72f, 108f })
        {
            builder.AddBox(new Vector3(x, 0.1f, 130f), new Vector3(0.6f, 0.2f, 60f), darkSteel);
            builder.AddBox(new Vector3(x, 9f, 125f), new Vector3(1f, 18f, 1f), hazard);
            builder.AddBox(new Vector3(x, 9f, 135f), new Vector3(1f, 18f, 1f), hazard);
        }
        builder.AddBox(new Vector3(90f, 18.5f, 130f), new Vector3(38f, 1.6f, 11f), hazard);
        builder.AddBox(new Vector3(95f, 16.9f, 130f), new Vector3(3f, 1.6f, 3f), darkSteel);
        for (int i = 0; i < 4; i++) Pallet(builder, new Vector3(82f + i * 5f, 0f, 150f), random);

        // 廠區圍籬 (南側大門留開口) 與警衛亭
        Fence(builder, SiteMin, SiteMax, -25f);
        builder.AddBox(new Vector3(-16f, 1.4f, -155f), new Vector3(3f, 2.8f, 3f), white);
        builder.AddBox(new Vector3(-16f, 2.95f, -155f), new Vector3(3.6f, 0.3f, 3.6f), steel);
        builder.AddBox(new Vector3(-16f, 1.8f, -155f), new Vector3(3.05f, 0.9f, 3.05f), glass);

        Finish(builder, "Site", level);
        PlaceSiteLamps(level);
        PlaceParkedCars(level, random);
    }

    /// <summary>管線橋：每 8m 一組門型支架，平台上三條管線</summary>
    private static void PipeBridge(CityMeshBuilder builder, Vector3 from, Vector3 to)
    {
        Vector3 span = to - from;
        float length = span.magnitude;
        Quaternion rotation = Quaternion.LookRotation(span);
        Vector3 side = rotation * Vector3.right;
        for (float t = 4f; t < length - 2f; t += 8f)
        {
            Vector3 point = from + span.normalized * t;
            foreach (float s in new[] { -1.4f, 1.4f }) builder.AddBox(new Vector3(point.x, point.y * 0.5f, point.z) + side * s, new Vector3(0.35f, point.y, 0.35f), steel);
        }
        builder.AddBox(Matrix4x4.TRS((from + to) * 0.5f, rotation, Vector3.one), Vector3.zero, new Vector3(3.2f, 0.3f, length), steel);
        foreach ((float offset, int color) in new[] { (-0.9f, 0), (0f, 1), (0.9f, 3) })
        {
            builder.AddCylinder(Matrix4x4.identity, from + side * offset + Vector3.up * 0.6f, span.normalized, 0.35f, length, 10, pipeColors[color]);
        }
    }

    /// <summary>圍籬：立柱 + 三條橫桿；gateX 有值時南側在該 x 留 12m 大門</summary>
    private static void Fence(CityMeshBuilder builder, Vector2 min, Vector2 max, float? gateX = null)
    {
        void Line(Vector3 a, Vector3 b, bool south)
        {
            float length = Vector3.Distance(a, b);
            Vector3 direction = (b - a).normalized;
            for (float t = 0f; t <= length; t += 4f)
            {
                Vector3 p = a + direction * t;
                if (south && gateX.HasValue && Mathf.Abs(p.x - gateX.Value) < 6f) continue;
                builder.AddBox(p + Vector3.up * 1.2f, new Vector3(0.1f, 2.4f, 0.1f), steel);
            }
            foreach (float y in new[] { 0.4f, 1.3f, 2.3f })
            {
                if (south && gateX.HasValue)
                {
                    float left = gateX.Value - 6f - a.x, right = b.x - (gateX.Value + 6f);
                    builder.AddBox(new Vector3(a.x + left * 0.5f, y, a.z), new Vector3(left, 0.06f, 0.06f), steel);
                    builder.AddBox(new Vector3(b.x - right * 0.5f, y, a.z), new Vector3(right, 0.06f, 0.06f), steel);
                }
                else
                {
                    builder.AddBox(Matrix4x4.TRS((a + b) * 0.5f + Vector3.up * y, Quaternion.LookRotation(direction), Vector3.one), Vector3.zero, new Vector3(0.06f, 0.06f, length), steel);
                }
            }
        }
        Vector3 sw = new Vector3(min.x, 0f, min.y), se = new Vector3(max.x, 0f, min.y), ne = new Vector3(max.x, 0f, max.y), nw = new Vector3(min.x, 0f, max.y);
        Line(sw, se, true);
        Line(se, ne, false);
        Line(nw, ne, false);
        Line(sw, nw, false);
    }

    /// <summary>道路旁路燈 (LowpolyStreetPack，光錐保留給夜間照明系統定位燈頭)</summary>
    private static void PlaceSiteLamps(Transform level)
    {
        GameObject lampPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{StreetProps}/TraficLights/LampPost_J.prefab");
        if (lampPrefab == null) return;
        Transform lamps = new GameObject("SiteLamps").transform;
        lamps.SetParent(level, false);
        Dictionary<Material, Material> remap = CityLevelBaker.StreetPackUrpRemap();
        void Lamp(Vector3 position, float yaw)
        {
            GameObject lamp = (GameObject)PrefabUtility.InstantiatePrefab(lampPrefab, lamps);
            lamp.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw + 90f, 0f) * lampPrefab.transform.rotation);
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
        for (float x = -60f; x <= 100f; x += 32f)
        {
            Lamp(new Vector3(x, 0f, -81f), 180f);
            Lamp(new Vector3(x, 0f, 93f), 0f);
        }
        for (float z = -50f; z <= 70f; z += 30f)
        {
            Lamp(new Vector3(-81f, 0f, z), 270f);
            Lamp(new Vector3(71f, 0f, z), 90f);
        }
        for (float z = -150f; z <= -90f; z += 30f) Lamp(new Vector3(-31f, 0f, z), 270f);
    }

    private static void PlaceParkedCars(Transform level, System.Random random)
    {
        Transform cars = new GameObject("ParkedCars").transform;
        cars.SetParent(level, false);
        GameObject[] prefabs = StrikeSceneBuilder.CarPrefabs();
        if (prefabs.Length == 0) return;
        foreach ((float z, float yaw) in new[] { (-26f, 0f), (-4f, 180f) })
        {
            for (float x = 77.4f; x <= 137f; x += 2.8f)
            {
                if (random.NextDouble() < 0.35) continue;
                GameObject car = CityTraffic.CreateCarModel(prefabs[random.Next(prefabs.Length)], cars, out _, out Vector3 size);
                car.transform.SetPositionAndRotation(new Vector3(x, 0.03f, z), Quaternion.Euler(0f, yaw, 0f));
                BoxCollider box = car.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, size.y * 0.5f, 0f);
                box.size = size;
                car.isStatic = true;
            }
        }
    }

    /// <summary>
    /// 平民工人 (CityPeople)：A 棟內工作與走動、貨車場、B 棟、儲槽區巡檢、辦公大樓前、大門警衛。
    /// 爆炸時會驚慌逃跑；被炸到或撞到算誤傷 (扣分)。
    /// </summary>
    private static void BuildWorkers(Transform level)
    {
        Transform root = new GameObject("Workers").transform;
        root.SetParent(level, false);
        GameObject builder = AssetDatabase.LoadAssetAtPath<GameObject>($"{PeoplePrefabs}/worker_Male_constructor_B.prefab");
        GameObject maleG = AssetDatabase.LoadAssetAtPath<GameObject>($"{PeoplePrefabs}/city/casual_Male_G.prefab");
        GameObject femaleG = AssetDatabase.LoadAssetAtPath<GameObject>($"{PeoplePrefabs}/city/casual_Female_G.prefab");
        GameObject maleK = AssetDatabase.LoadAssetAtPath<GameObject>($"{PeoplePrefabs}/downtown/casual_Male_K.prefab");
        GameObject femaleK = AssetDatabase.LoadAssetAtPath<GameObject>($"{PeoplePrefabs}/downtown/casual_Female_K.prefab");
        GameObject guard = AssetDatabase.LoadAssetAtPath<GameObject>($"{PeoplePrefabs}/professions/police_Female_A.prefab");
        GameObject Tool(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PeoplePrefabs}/tools/tool_{name}.prefab");

        void Add(string name, GameObject character, string work, GameObject tool, Vector3 position, float yaw, Vector3? patrol = null)
        {
            if (character == null) return;
            WorkerPoint point = new GameObject(name).AddComponent<WorkerPoint>();
            point.transform.SetParent(root, false);
            point.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            point.character = character;
            point.workState = work;
            point.tool = tool;
            if (patrol.HasValue)
            {
                Transform end = new GameObject("PatrolTo").transform;
                end.SetParent(point.transform, false);
                end.position = patrol.Value;
                point.patrolTo = end;
            }
        }

        // A 棟內
        Add("CNC_Wrench", builder, "Wrench", Tool("wrench"), new Vector3(4f, 0f, 24.8f), 180f);
        Add("Line_Drill", builder, "Drill", Tool("drill"), new Vector3(17f, 0f, -31.2f), 0f);
        Add("Bench_Hammer", builder, "Hammer", Tool("hammer"), new Vector3(22f, 0f, 23.2f), 0f);
        Add("Rack_Walker", maleG, "Walk", null, new Vector3(-39f, 0f, -36f), 0f, new Vector3(-39f, 0f, -20f));
        Add("Office_Phone", femaleG, "Phone", null, new Vector3(-44f, 0f, 50f), 90f);
        Add("Pump_PipeWrench", builder, "PipeWrench", Tool("pipewrench"), new Vector3(-14f, 0f, 3f), 270f);
        // 廠區戶外
        Add("Truck_Hammer", builder, "Hammer", Tool("hammer"), new Vector3(13.5f, 0f, -98f), 270f);
        Add("Truck_Walker", maleK, "Walk", null, new Vector3(5f, 0f, -103f), 90f, new Vector3(45f, 0f, -103f));
        Add("HallB_Wrench", builder, "Wrench", Tool("wrench"), new Vector3(122f, 0f, 38.5f), 180f);
        Add("HallB_Walker", femaleK, "Walk", null, new Vector3(90f, 0f, 30f), 90f, new Vector3(130f, 0f, 30f));
        Add("Tank_Inspector", maleG, "Walk", null, new Vector3(-118f, 0f, 0f), 0f, new Vector3(-118f, 0f, 80f));
        Add("Office_Smoker", femaleG, "Phone", null, new Vector3(76f, 0f, -55f), 270f);
        Add("Parking_Walker", maleK, "Walk", null, new Vector3(80f, 0f, -15f), 90f, new Vector3(135f, 0f, -15f));
        Add("Gate_Guard", guard, "Walk", null, new Vector3(-12f, 0f, -150f), 0f, new Vector3(-12f, 0f, -135f));
        Add("Yard_Hammer", builder, "Hammer", Tool("hammer"), new Vector3(-20f, 0f, 116f), 0f);
    }

    #endregion

    #region 目標與敵機

    /// <summary>高價值目標：投彈 4 (貨架頂、組裝台、鍋爐頂、發電機房屋頂)、撞擊 3 (泵浦平台下、控制室、伺服器機房)</summary>
    private static void BuildTargetPoints(Transform level)
    {
        Transform points = new GameObject("StrikeTargetPoints").transform;
        points.SetParent(level, false);

        StrikeSceneBuilder.AddPoint(points, "Bomb_ChipStock", StrikeTarget.TargetKind.Bomb, new Vector3(-34f, 10.3f, -28f), null, "精密晶片庫存");
        StrikeSceneBuilder.AddPoint(points, "Bomb_Prototype", StrikeTarget.TargetKind.Bomb, new Vector3(42f, 1.2f, -34f), null, "原型機組裝台");
        StrikeSceneBuilder.AddPoint(points, "Bomb_ReactorCore", StrikeTarget.TargetKind.Bomb, new Vector3(34f, 13.6f, 0f), null, "反應爐控制核心");
        StrikeSceneBuilder.AddPoint(points, "Bomb_Transformer", StrikeTarget.TargetKind.Bomb, new Vector3(44f, 6.5f, 47f), null, "主變壓器");

        StrikeSceneBuilder.AddPoint(points, "Ram_CoolingPump", StrikeTarget.TargetKind.Ram, new Vector3(-20f, 0f, 0f), new Vector3(-20f, 3f, -16f), "冷卻泵核心");
        StrikeSceneBuilder.AddPoint(points, "Ram_ControlRoom", StrikeTarget.TargetKind.Ram, new Vector3(-40f, 0f, 31f), new Vector3(-18f, 3f, 31f), "生產線控制台");
        StrikeSceneBuilder.AddPoint(points, "Ram_ServerRoom", StrikeTarget.TargetKind.Ram, new Vector3(-42f, 5.6f, 41f), new Vector3(-20f, 6.8f, 41f), "伺服器機櫃");

        // 廠區戶外與 B 棟
        StrikeSceneBuilder.AddPoint(points, "Bomb_FuelValves", StrikeTarget.TargetKind.Bomb, new Vector3(-130f, 12.6f, 70f), null, "燃料儲槽閥組");
        StrikeSceneBuilder.AddPoint(points, "Bomb_Substation", StrikeTarget.TargetKind.Bomb, new Vector3(128f, 0f, -121f), null, "變電站控制櫃");
        StrikeSceneBuilder.AddPoint(points, "Ram_HallB", StrikeTarget.TargetKind.Ram, new Vector3(134f, 0f, 40f), new Vector3(72f, 3f, 35f), "B 棟裝配控制台");
    }

    /// <summary>三架敵方無人機：南側生產線環線、北側大廳環線、管線迷宮內的往返航道</summary>
    private static void BuildEnemyDrones(Transform level)
    {
        Transform holder = new GameObject("EnemyDrones").transform;
        holder.SetParent(level, false);
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyModelPath);

        AddPatrol(holder, "Patrol_ProductionLoop", model, true, 4f, 8.5f,
            new Vector3(-15f, 7f, -15.5f), new Vector3(40f, 7f, -15.5f), new Vector3(40f, 7f, -37f), new Vector3(-15f, 7f, -37f));
        AddPatrol(holder, "Patrol_NorthHall", model, true, 4.5f, 9f,
            new Vector3(-16f, 8f, 20f), new Vector3(30f, 8f, 20f), new Vector3(31f, 9f, 42f), new Vector3(-16f, 9f, 42f));
        AddPatrol(holder, "Patrol_PipeMaze", model, false, 3.5f, 7.5f,
            new Vector3(-45f, 2.5f, -9f), new Vector3(23f, 2.5f, -9f), new Vector3(23f, 8.5f, -9f), new Vector3(23f, 8.5f, 3f), new Vector3(-45f, 8.5f, 3f));
        // 廠區上空 (24m，高於 A 棟屋頂、低於煙囪頂，避開冷卻塔與煙囪)
        AddPatrol(holder, "Patrol_Perimeter", model, true, 5f, 10f,
            new Vector3(-80f, 24f, -95f), new Vector3(80f, 24f, -95f), new Vector3(80f, 24f, 100f), new Vector3(-80f, 24f, 100f));
    }

    private static void AddPatrol(Transform parent, string name, GameObject model, bool loop, float cruise, float flee, params Vector3[] waypoints)
    {
        EnemyDronePatrol patrol = new GameObject(name).AddComponent<EnemyDronePatrol>();
        patrol.transform.SetParent(parent, false);
        for (int i = 0; i < waypoints.Length; i++)
        {
            Transform waypoint = new GameObject($"Waypoint_{i}").transform;
            waypoint.SetParent(patrol.transform, false);
            waypoint.position = waypoints[i];
        }
        SerializedObject so = new SerializedObject(patrol);
        so.FindProperty("modelPrefab").objectReferenceValue = model;
        so.FindProperty("loop").boolValue = loop;
        so.FindProperty("cruiseSpeed").floatValue = cruise;
        so.FindProperty("fleeSpeed").floatValue = flee;
        so.FindProperty("modelNoseIsBackward").boolValue = false;   // Download 四軸無人機的機頭為 +Z
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    #endregion
}

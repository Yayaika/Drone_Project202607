using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 把程序化城市「烘焙」成固定的關卡場景 (選單：無人機遊戲/7)：
/// 在編輯器中依關卡參數生成一次，網格與材質存成資產，場景內容從此固定、可手動編輯，
/// 執行時不再生成 (非同步載入時 Loading 畫面不會卡住)。車流與路人仍在執行時建立。
/// 同時提供台灣街景素材 (LowpolyStreetPack 設施、有貼圖的地面材質) 的填入。
/// </summary>
public static class CityLevelBaker
{
    private const string TemplateScenePath = "Assets/Scenes/" + LevelCatalog.ProceduralCityScene + ".unity";
    private const string GeneratedRoot = "Assets/GeneratedLevels";
    private const string StreetPackRoot = "Assets/Download/LowpolyStreetPack/Prefabs";
    private const string StreetPackMaterials = "Assets/Download/LowpolyStreetPack/Meshes/Materials";
    private const string UrpMaterialFolder = "Assets/Materials/OrganicCity/LowpolyStreetPack";
    private const string SurfaceMaterialFolder = "Assets/Materials/OrganicCity";

    [MenuItem("無人機遊戲/7. 烘焙固定關卡場景 (住宅區 / 市中心 / 選單背景)", priority = 7)]
    public static void BakeFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string report = BakeAll();
        EditorUtility.DisplayDialog("烘焙完成", report, "確定");
    }

    /// <summary>烘焙所有程序化關卡與選單背景，回傳摘要 (不顯示對話框，可由自動化呼叫)</summary>
    public static string BakeAll()
    {
        List<string> lines = new List<string>();
        List<string> scenes = new List<string>();
        foreach (LevelDefinition level in LevelCatalog.All)
        {
            if (!level.BakedCity) continue;
            string path = "Assets/Scenes/" + level.SceneName + ".unity";
            lines.Add(BakeLevel(level, path));
            scenes.Add(path);
        }

        foreach (string menuScene in new[] { "Assets/Scenes/StartMenu.unity", "Assets/Scenes/" + LevelCatalog.DroneSelectScene + ".unity" })
        {
            lines.Add(BakeExistingScene(menuScene));
        }

        AddScenesToBuildSettings(scenes);
        AssetDatabase.SaveAssets();
        return string.Join("\n", lines);
    }

    private static string BakeLevel(LevelDefinition level, string scenePath)
    {
        if (!File.Exists(TemplateScenePath)) return $"找不到範本 {TemplateScenePath}";

        // 以 CityProcedural (含 GameManager、Spawner、UI) 為範本複製出固定關卡場景
        AssetDatabase.DeleteAsset(scenePath);
        AssetDatabase.CopyAsset(TemplateScenePath, scenePath);
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        CityGenerator generator = Object.FindFirstObjectByType<CityGenerator>(FindObjectsInactive.Include);
        if (generator == null) return $"{scenePath}：找不到 CityGenerator";

        SerializedObject so = new SerializedObject(generator);
        so.FindProperty("seed").intValue = level.CitySeed;
        so.FindProperty("blocks").intValue = level.CityBlocks;
        so.FindProperty("buildingDensity").floatValue = level.BuildingDensity;
        so.FindProperty("maxBuildingHeight").floatValue = level.MaxBuildingHeight;
        so.FindProperty("defaultCheckpointCount").intValue = level.CheckpointCount;
        so.FindProperty("checkpointRadius").floatValue = 3.5f;
        // 台北街景以騎樓街屋為主，素材包大樓多出現在市中心較高的地塊
        so.FindProperty("modelBuildingChance").floatValue = 0.5f;
        so.FindProperty("useLevelCatalog").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();

        AssignStreetAssets(generator);
        return BakeGenerator(generator, scene, level.SceneName);
    }

    /// <summary>已含 CityGenerator 的場景 (選單背景) 就地烘焙</summary>
    private static string BakeExistingScene(string scenePath)
    {
        if (!File.Exists(scenePath)) return $"找不到 {scenePath}";
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        CityGenerator generator = Object.FindFirstObjectByType<CityGenerator>(FindObjectsInactive.Include);
        if (generator == null) return $"{Path.GetFileNameWithoutExtension(scenePath)}：沒有背景城市 (請先執行選單 6)";

        AssignStreetAssets(generator);
        return BakeGenerator(generator, scene, scene.name);
    }

    private static string BakeGenerator(CityGenerator generator, Scene scene, string folderName)
    {
        SerializedObject so = new SerializedObject(generator);
        so.FindProperty("baked").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();

        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        generator.Generate();
        long generateMs = watch.ElapsedMilliseconds;

        Transform root = generator.transform.Find("GeneratedCity");
        if (root == null) return $"{folderName}：生成失敗";
        int saved = SaveGeneratedAssets(root, folderName);

        so.Update();
        so.FindProperty("baked").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(generator);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        int objects = root.GetComponentsInChildren<Transform>(true).Length;
        return $"{folderName}：已固定 (生成 {generateMs}ms，物件 {objects}，新資產 {saved})";
    }

    /// <summary>把生成時建立的網格與材質存成資產，場景才能引用 (相同名稱的材質共用)</summary>
    private static int SaveGeneratedAssets(Transform root, string folderName)
    {
        string meshFolder = EnsureFolder(GeneratedRoot + "/" + folderName);
        string materialFolder = EnsureFolder(GeneratedRoot + "/SharedMaterials");

        // 舊的網格資產先清掉
        foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { meshFolder }))
        {
            AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
        }

        int saved = 0;
        int meshIndex = 0;
        Dictionary<Material, Material> materialMap = new Dictionary<Material, Material>();
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || AssetDatabase.Contains(mesh)) continue;
            AssetDatabase.CreateAsset(mesh, $"{meshFolder}/{Sanitize(mesh.name)}_{meshIndex++}.asset");
            saved++;
        }

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is ParticleSystemRenderer) continue;
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null || AssetDatabase.Contains(material)) continue;
                if (!materialMap.TryGetValue(material, out Material persistent))
                {
                    string path = $"{materialFolder}/{Sanitize(material.name)}.mat";
                    persistent = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (persistent == null)
                    {
                        AssetDatabase.CreateAsset(material, path);
                        persistent = material;
                        saved++;
                    }
                    materialMap[material] = persistent;
                }
                materials[i] = persistent;
                changed = true;
            }
            if (changed) renderer.sharedMaterials = materials;
        }

        AssetDatabase.SaveAssets();
        return saved;
    }

    #region 街景素材

    /// <summary>填入 LowpolyStreetPack 設施、URP 材質替換表與有貼圖的地面材質</summary>
    public static void AssignStreetAssets(CityGenerator generator)
    {
        SerializedObject so = new SerializedObject(generator);
        SetArray(so.FindProperty("streetLampPrefabs"), StreetPrefabs("StreetProps/TraficLights", "LampPost_J"));
        // 植物：Anime Nature (Anime Trees)；Tree_01 面數過高 (9 萬) 不使用
        SetArray(so.FindProperty("treeModelPrefabs"), NaturePrefabs("Tree_02", "Tree_03", "Tree_04", "Tree_05", "Tree_06", "Tree_07", "Tree_08"));
        SetArray(so.FindProperty("bushPrefabs"), NaturePrefabs("Bush_"));
        SetArray(so.FindProperty("grassPrefabs"), NaturePrefabs("Grass.prefab"));
        so.FindProperty("facadeTileTexture").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Download/LowpolyStreetPack/Meshes/Textures/pavement_01.png");
        SetArray(so.FindProperty("powerPolePrefabs"), StreetPrefabs("StreetProps/Powerpoles", "Powerpole_A_A", "Powerpole_B_A"));
        SetArray(so.FindProperty("sidewalkPropPrefabs"), StreetPrefabs("StreetProps", "Hidrant", "Bench_", "MailBox", "FuseBox", "NewsBoard", "Phone"));
        SetArray(so.FindProperty("roadBlockPrefabs"), StreetPrefabs("StreetProps/RoadBlocks", "RoadCone_", "RoadBlock_"));
        SetArray(so.FindProperty("overpassPrefabs"), StreetPrefabs("StreetProps/Overpass", "Overpass_"));

        SerializedProperty remaps = so.FindProperty("propMaterialRemaps");
        remaps.arraySize = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { StreetPackMaterials }))
        {
            Material source = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            Material converted = source != null ? AssetDatabase.LoadAssetAtPath<Material>($"{UrpMaterialFolder}/{source.name}_URP.mat") : null;
            if (converted == null) continue;
            remaps.arraySize++;
            SerializedProperty entry = remaps.GetArrayElementAtIndex(remaps.arraySize - 1);
            entry.FindPropertyRelative("from").objectReferenceValue = source;
            entry.FindPropertyRelative("to").objectReferenceValue = converted;
        }

        // 有貼圖的地面材質 (LowpolyStreetPack 柏油/地磚/草地貼圖)
        AssignIfExists(so, "roadMaterial", SurfaceMaterialFolder + "/Road_Asphalt.mat");
        AssignIfExists(so, "sidewalkMaterial", SurfaceMaterialFolder + "/Sidewalk_Tiles.mat");
        AssignIfExists(so, "groundMaterial", SurfaceMaterialFolder + "/Ground_Grass.mat");
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AssignIfExists(SerializedObject so, string property, string path)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) so.FindProperty(property).objectReferenceValue = material;
    }

    private const string NatureRoot = "Assets/Download/Anime Trees/Prefabs";

    /// <summary>Anime Nature 植物 Prefab (檔名以任一前綴開頭；含 .prefab 的寫法代表完整檔名)</summary>
    public static Object[] NaturePrefabs(params string[] prefixes)
    {
        if (!AssetDatabase.IsValidFolder(NatureRoot)) return new Object[0];
        return AssetDatabase.FindAssets("t:Prefab", new[] { NatureRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => prefixes.Any(prefix => prefix.EndsWith(".prefab")
                ? Path.GetFileName(path) == prefix
                : Path.GetFileNameWithoutExtension(path).StartsWith(prefix)))
            .OrderBy(path => path)
            .Select(path => (Object)AssetDatabase.LoadAssetAtPath<GameObject>(path))
            .Where(prefab => prefab != null)
            .ToArray();
    }

    /// <summary>LowpolyStreetPack 原始材質 → URP 轉換材質 (缺少轉換版時沿用原材質)</summary>
    public static Dictionary<Material, Material> StreetPackUrpRemap()
    {
        Dictionary<Material, Material> remap = new Dictionary<Material, Material>();
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { StreetPackMaterials }))
        {
            Material source = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            Material converted = source != null ? AssetDatabase.LoadAssetAtPath<Material>($"{UrpMaterialFolder}/{source.name}_URP.mat") : null;
            if (converted != null) remap[source] = converted;
        }
        return remap;
    }

    public static Object[] StreetPrefabs(string folder, params string[] prefixes)
    {
        string path = $"{StreetPackRoot}/{folder}";
        if (!AssetDatabase.IsValidFolder(path)) return new Object[0];

        return AssetDatabase.FindAssets("t:Prefab", new[] { path })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(assetPath => !assetPath.Contains("/Trees_Big/")) // 此資料夾的 Prefab 網格遺失
            .Where(assetPath => prefixes.Any(prefix => Path.GetFileNameWithoutExtension(assetPath).StartsWith(prefix)))
            .OrderBy(assetPath => assetPath)
            .Select(assetPath => (Object)AssetDatabase.LoadAssetAtPath<GameObject>(assetPath))
            .Where(prefab => prefab != null)
            .ToArray();
    }

    #endregion

    #region 工具

    private static void SetArray(SerializedProperty property, Object[] values)
    {
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static string EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
        return path;
    }

    private static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrEmpty(name) ? "Generated" : name;
    }

    private static void AddScenesToBuildSettings(List<string> scenePaths)
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
        foreach (string path in scenePaths)
        {
            if (scenes.Any(scene => scene.path == path)) continue;
            scenes.Add(new EditorBuildSettingsScene(path, true));
        }
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    #endregion
}

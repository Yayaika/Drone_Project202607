using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 一鍵建立/設定新功能需要的場景與資料夾 (選單：無人機遊戲/…)。
/// 所有操作只新增檔案或修改 Build Settings，不會改動既有場景內容。
/// </summary>
public static class DroneGameSetupMenu
{
    private const string TemplateScenePath = "Assets/Scenes/SampleScene.unity";
    private const string CityScenePath = "Assets/Scenes/" + LevelCatalog.ProceduralCityScene + ".unity";
    private const string CityPackRoot = "Assets/Download/POLYGON city pack/Meshes,textures";
    private const string CargoPrefabPath = "Assets/Prefabs/Drone_Cargo_RedCube.prefab";
    private const string AudioFolder = "Assets/Resources/Audio";

    [MenuItem("無人機遊戲/1. 建立程序化城市關卡場景 (CityProcedural)", priority = 1)]
    public static void CreateProceduralCityScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (File.Exists(CityScenePath) &&
            !EditorUtility.DisplayDialog("場景已存在", $"{CityScenePath} 已存在，要覆蓋重建嗎？\n(原檔案會被取代)", "覆蓋", "取消"))
        {
            return;
        }

        if (!File.Exists(TemplateScenePath))
        {
            EditorUtility.DisplayDialog("找不到範本場景", $"需要 {TemplateScenePath} 作為範本 (含 GameManager、Spawner、EventSystem 等)。", "確定");
            return;
        }

        // 以可正常遊玩的 SampleScene 為範本，保留 GameManager / Spawner / EventSystem / 介面設定
        AssetDatabase.DeleteAsset(CityScenePath);
        if (!AssetDatabase.CopyAsset(TemplateScenePath, CityScenePath))
        {
            EditorUtility.DisplayDialog("建立失敗", "無法複製範本場景。", "確定");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
        List<string> removed = new List<string>();

        // 移除範本中的訓練場內容：賽道生成器與地面
        foreach (CourseBuilder builder in Object.FindObjectsByType<CourseBuilder>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            removed.Add(builder.gameObject.name);
            Object.DestroyImmediate(builder.gameObject);
        }
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            if (rootObject.name == "Plane" || rootObject.name == "Terrain")
            {
                removed.Add(rootObject.name);
                Object.DestroyImmediate(rootObject);
            }
        }

        GameObject cityObject = new GameObject("CityGenerator");
        CityGenerator generator = cityObject.AddComponent<CityGenerator>();
        AssignCityAssets(generator);

        GameManager gameManager = Object.FindFirstObjectByType<GameManager>();
        if (gameManager != null)
        {
            gameManager.nextSceneName = string.Empty;
            EditorUtility.SetDirty(gameManager);
        }

        ApplySkybox();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AddScenesToBuildSettings(CityScenePath);

        Selection.activeGameObject = cityObject;
        EditorUtility.DisplayDialog("完成",
            $"已建立 {CityScenePath} 並加入 Build Settings。\n\n" +
            $"移除範本物件：{(removed.Count > 0 ? string.Join("、", removed) : "無")}\n" +
            "城市會在進入遊戲時依關卡設定自動生成；\n可在 CityGenerator 右鍵選單「生成城市 (預覽)」先在編輯器中查看。",
            "確定");
    }

    [MenuItem("無人機遊戲/2. 檢查並加入關卡到 Build Settings", priority = 2)]
    public static void EnsureBuildSettings()
    {
        List<string> paths = new List<string>
        {
            "Assets/Scenes/StartMenu.unity",
            "Assets/Scenes/" + LevelCatalog.DroneSelectScene + ".unity"
        };
        foreach (LevelDefinition level in LevelCatalog.All)
        {
            string path = "Assets/Scenes/" + level.SceneName + ".unity";
            if (!paths.Contains(path)) paths.Add(path);
        }

        List<string> missing = paths.Where(path => !File.Exists(path)).ToList();
        AddScenesToBuildSettings(paths.Where(File.Exists).ToArray());

        string message = "Build Settings 已更新。";
        if (missing.Count > 0)
        {
            message += "\n\n以下場景不存在 (程序化城市請先執行選單 1)：\n" + string.Join("\n", missing);
        }
        EditorUtility.DisplayDialog("Build Settings", message, "確定");
    }

    [MenuItem("無人機遊戲/3. 建立音效資料夾 (Resources/Audio)", priority = 3)]
    public static void CreateAudioFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }
        if (!AssetDatabase.IsValidFolder(AudioFolder))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "Audio");
        }

        List<string> missing = new List<string>();
        foreach (GameSfx sfx in System.Enum.GetValues(typeof(GameSfx)))
        {
            string fileName = GameAudio.SfxFileName(sfx);
            if (Resources.Load<AudioClip>("Audio/" + fileName) == null) missing.Add(fileName);
        }
        foreach (GameMusic music in System.Enum.GetValues(typeof(GameMusic)))
        {
            string fileName = GameAudio.MusicFileName(music);
            if (!string.IsNullOrEmpty(fileName) && Resources.Load<AudioClip>("Audio/" + fileName) == null) missing.Add(fileName);
        }

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(AudioFolder);
        EditorUtility.DisplayDialog("音效資料夾",
            $"請把音效檔放到 {AudioFolder}，檔名需與下列名稱一致 (副檔名 wav/mp3/ogg 皆可)：\n\n" +
            (missing.Count > 0 ? string.Join("\n", missing) : "全部音效都已就緒！"),
            "確定");
    }

    private static void AssignCityAssets(CityGenerator generator)
    {
        SerializedObject serialized = new SerializedObject(generator);

        GameObject[] buildings = LoadModels(CityPackRoot + "/Buildings_FBX");
        GameObject[] shops = LoadModels(CityPackRoot + "/Shops_FBX");
        GameObject[] props = LoadModels(CityPackRoot + "/props_FBX")
            .Where(model => !model.name.ToLowerInvariant().Contains("wrench")) // 扳手是手持道具，不適合當街景
            .ToArray();

        SetArray(serialized.FindProperty("buildingPrefabs"), buildings);
        SetArray(serialized.FindProperty("smallBuildingPrefabs"), shops);
        SetArray(serialized.FindProperty("propPrefabs"), props);

        // 素材包 FBX 匯入後材質皆為灰色：填入素材包的牆面/屋頂/窗戶配色與招牌貼圖
        PolygonCityPackAssets.AssignPalette(serialized, buildings.Concat(shops).Concat(props));

        GameObject cargo = AssetDatabase.LoadAssetAtPath<GameObject>(CargoPrefabPath);
        serialized.FindProperty("cargoPrefab").objectReferenceValue = cargo;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log($"[DroneGameSetup] CityGenerator 素材：大樓 {buildings.Length}、商店 {shops.Length}、道具 {props.Length}、貨物 Prefab {(cargo != null ? "OK" : "缺少")}");
    }

    private static GameObject[] LoadModels(string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder)) return new GameObject[0];

        return AssetDatabase.FindAssets("t:Model", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .Where(model => model != null && model.GetComponentInChildren<Renderer>() != null)
            .ToArray();
    }

    private static void SetArray(SerializedProperty property, GameObject[] values)
    {
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }

    private static void ApplySkybox()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Download/Free Stylized Skybox" });
        foreach (string guid in guids)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (material != null && material.shader != null && material.shader.name.ToLowerInvariant().Contains("skybox"))
            {
                RenderSettings.skybox = material;
                return;
            }
        }
    }

    private static void AddScenesToBuildSettings(params string[] scenePaths)
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
        foreach (string path in scenePaths)
        {
            if (scenes.Any(scene => scene.path == path)) continue;
            scenes.Add(new EditorBuildSettingsScene(path, true));
        }
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}

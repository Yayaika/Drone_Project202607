using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 有機城市生成器的 Inspector：修改參數後延遲 0.3 秒自動重建，並提供重新生成 / 隨機種子 / 清除按鈕。
/// 另提供選單「無人機遊戲/4. 建立有機城市測試場景」，建立獨立測試場景 (不加入 Build Settings、不影響其他關卡)。
/// </summary>
[CustomEditor(typeof(OrganicCityGenerator))]
public class OrganicCityGeneratorEditor : Editor
{
    private const float RegenerateDelay = 0.3f;

    private const string ScenePath = "Assets/Scenes/OrganicCityTest.unity";
    private const string MaterialFolder = "Assets/Materials/OrganicCity";
    private const string CityPackRoot = "Assets/Download/POLYGON city pack/Meshes,textures";
    private const string StreetPackRoot = "Assets/Download/LowpolyStreetPack/Prefabs";

    private static OrganicCityGenerator pendingTarget;
    private static double regenerateAt;

    public override void OnInspectorGUI()
    {
        OrganicCityGenerator generator = (OrganicCityGenerator)target;

        serializedObject.Update();
        EditorGUI.BeginChangeCheck();
        DrawPropertiesExcluding(serializedObject, "m_Script");
        bool changed = EditorGUI.EndChangeCheck();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        if (!string.IsNullOrEmpty(generator.LastStats))
        {
            EditorGUILayout.HelpBox(generator.LastStats, MessageType.None);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("重新生成", GUILayout.Height(28f))) Regenerate(generator);
            if (GUILayout.Button("隨機種子", GUILayout.Height(28f)))
            {
                Undo.RecordObject(generator, "隨機城市種子");
                generator.Seed = Random.Range(1, 999999);
                Regenerate(generator);
            }
            if (GUILayout.Button("清除", GUILayout.Height(28f)))
            {
                generator.Clear();
                MarkDirty(generator);
            }
        }

        if (GUILayout.Button("重新指定素材包模型與材質 (保留街道設施規則)"))
        {
            Undo.RecordObject(generator, "指定城市素材");
            AssignAssets(generator);
            Regenerate(generator);
        }

        if (GUILayout.Button("街道設施規則重設為 LowpolyStreetPack 預設值"))
        {
            Undo.RecordObject(generator, "重設街道設施規則");
            ResetPropRules(generator);
            Regenerate(generator);
        }

        if (changed && generator.AutoRegenerate) ScheduleRegenerate(generator);
    }

    private static void ScheduleRegenerate(OrganicCityGenerator generator)
    {
        if (Application.isPlaying)
        {
            generator.RequestRegenerate();
            return;
        }

        // 拖曳滑桿時只在停手後重建一次
        regenerateAt = EditorApplication.timeSinceStartup + RegenerateDelay;
        if (pendingTarget == null) EditorApplication.update += WaitAndRegenerate;
        pendingTarget = generator;
    }

    private static void WaitAndRegenerate()
    {
        if (EditorApplication.timeSinceStartup < regenerateAt) return;

        EditorApplication.update -= WaitAndRegenerate;
        OrganicCityGenerator generator = pendingTarget;
        pendingTarget = null;
        if (generator != null) Regenerate(generator);
    }

    private static void Regenerate(OrganicCityGenerator generator)
    {
        if (Application.isPlaying)
        {
            generator.RequestRegenerate();
            return;
        }

        generator.Generate();
        MarkDirty(generator);
    }

    private static void MarkDirty(OrganicCityGenerator generator)
    {
        if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
    }

    #region 建立測試場景

    [MenuItem("無人機遊戲/4. 建立有機城市測試場景 (OrganicCityTest)", priority = 4)]
    private static void CreateSceneMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog("場景已存在", $"{ScenePath} 已存在，要覆蓋重建嗎？", "覆蓋", "取消"))
        {
            return;
        }

        CreateTestScene();
        EditorUtility.DisplayDialog("完成",
            $"已建立 {ScenePath}。\n\n選取 OrganicCityGenerator 調整參數會自動重建；\n" +
            "按 Play 可用自由相機飛行檢視 (R 換種子、F 回到上空)。\n此場景未加入 Build Settings。",
            "確定");
    }

    /// <summary>建立 (或覆蓋) 測試場景；不跳對話框，可由自動化工具呼叫</summary>
    public static void CreateTestScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        GameObject generatorObject = new GameObject("OrganicCityGenerator");
        OrganicCityGenerator generator = generatorObject.AddComponent<OrganicCityGenerator>();
        AssignAssets(generator);

        Camera camera = Object.FindFirstObjectByType<Camera>();
        if (camera != null)
        {
            camera.farClipPlane = 3000f;
            camera.transform.position = new Vector3(0f, 210f, -290f);
            camera.transform.LookAt(Vector3.zero);
            OrganicCityFlyCamera flyCamera = camera.gameObject.AddComponent<OrganicCityFlyCamera>();
            SerializedObject serializedCamera = new SerializedObject(flyCamera);
            serializedCamera.FindProperty("generator").objectReferenceValue = generator;
            serializedCamera.ApplyModifiedPropertiesWithoutUndo();
        }

        Light sun = Object.FindFirstObjectByType<Light>();
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            sun.shadows = LightShadows.Soft;
        }

        ApplySkybox();
        ApplyFog();
        generator.Generate();

        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = generatorObject;
    }

    /// <summary>指定素材包模型與材質，以及生成器自己的道路/地面材質</summary>
    public static void AssignAssets(OrganicCityGenerator generator)
    {
        SerializedObject serialized = new SerializedObject(generator);

        GameObject[] models = LoadModels(CityPackRoot + "/Buildings_FBX").Concat(LoadModels(CityPackRoot + "/Shops_FBX")).ToArray();
        SetArray(serialized.FindProperty("buildingModels"), models);

        // 有專屬貼圖的模型與牆面/屋頂/窗戶配色 (與 CityGenerator 共用)
        PolygonCityPackAssets.AssignPalette(serialized, models);

        // LowpolyStreetPack：樹木；街道設施規則只在尚未設定時填入預設值，避免覆蓋使用者自訂的規則
        SetArray(serialized.FindProperty("treePrefabs"), StreetPrefabs("Foliage/Trees", "Tree_"));
        FillStreetPackRemaps(serialized.FindProperty("propMaterialRemaps"));
        if (serialized.FindProperty("propRules").arraySize == 0) FillDefaultPropRules(serialized.FindProperty("propRules"));

        // 地面、道路、人行道、草地：LowpolyStreetPack 的可平鋪貼圖 (找不到貼圖時退回純色材質)
        serialized.FindProperty("roadMaterial").objectReferenceValue =
            GetTexturedMaterial("Road_Asphalt", "pavement_03.png", Color.white, 0.2f) ?? GetMaterial("Road", new Color(0.17f, 0.17f, 0.19f), 0.35f);
        serialized.FindProperty("sidewalkMaterial").objectReferenceValue =
            GetTexturedMaterial("Sidewalk_Tiles", "pavement_01.png", Color.white, 0.1f) ?? GetMaterial("Sidewalk", new Color(0.63f, 0.62f, 0.59f));
        serialized.FindProperty("roadLineMaterial").objectReferenceValue = GetMaterial("RoadLine", new Color(0.95f, 0.8f, 0.2f));
        // 城市外的地面略暗略黃，公園草地保持原色，兩者才分得出來
        serialized.FindProperty("groundMaterial").objectReferenceValue =
            GetTexturedMaterial("Ground_Grass", "grass.png", new Color(0.8f, 0.85f, 0.68f), 0.05f) ?? GetMaterial("Ground", new Color(0.4f, 0.48f, 0.33f));
        serialized.FindProperty("parkMaterial").objectReferenceValue =
            GetTexturedMaterial("Park_Grass", "grass.png", new Color(1.1f, 1.15f, 1f), 0.05f) ?? GetMaterial("Park", new Color(0.3f, 0.56f, 0.27f));
        serialized.FindProperty("plazaMaterial").objectReferenceValue =
            GetTexturedMaterial("Plaza_Tiles", "pavement_02.png", Color.white, 0.1f) ?? GetMaterial("Plaza", new Color(0.62f, 0.57f, 0.5f));
        serialized.FindProperty("roofMaterial").objectReferenceValue = GetMaterial("Roof", new Color(0.38f, 0.39f, 0.41f));
        serialized.FindProperty("treeTrunkMaterial").objectReferenceValue = GetMaterial("TreeTrunk", new Color(0.4f, 0.28f, 0.18f));

        SetArray(serialized.FindProperty("treeLeafMaterials"), new Object[]
        {
            GetMaterial("Leaves_A", new Color(0.24f, 0.5f, 0.22f)),
            GetMaterial("Leaves_B", new Color(0.34f, 0.58f, 0.24f)),
            GetMaterial("Leaves_C", new Color(0.2f, 0.42f, 0.25f))
        });

        Color[] palette =
        {
            new Color(0.82f, 0.78f, 0.7f), new Color(0.7f, 0.72f, 0.75f), new Color(0.78f, 0.62f, 0.5f),
            new Color(0.58f, 0.62f, 0.68f), new Color(0.88f, 0.85f, 0.8f), new Color(0.62f, 0.55f, 0.5f),
            new Color(0.5f, 0.6f, 0.66f)
        };
        SetArray(serialized.FindProperty("buildingMaterials"),
            palette.Select((color, i) => (Object)GetMaterial("Building_" + (char)('A' + i), color, 0.25f)).ToArray());

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// LowpolyStreetPack 的材質使用 Built-in 著色器，在 URP 中顯示為洋紅色。
    /// 為每個材質建立 URP Lit 副本 (不修改素材包本身)，並登記到生成器的材質替換表。
    /// </summary>
    private static void FillStreetPackRemaps(SerializedProperty remaps)
    {
        const string sourceFolder = "Assets/Download/LowpolyStreetPack/Meshes/Materials";
        const string targetFolder = MaterialFolder + "/LowpolyStreetPack";
        remaps.arraySize = 0;
        if (!AssetDatabase.IsValidFolder(sourceFolder)) return;

        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) return;
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Materials", "OrganicCity");
        if (!AssetDatabase.IsValidFolder(targetFolder)) AssetDatabase.CreateFolder(MaterialFolder, "LowpolyStreetPack");

        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { sourceFolder }))
        {
            Material source = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (source == null || source.shader == lit) continue;

            string path = $"{targetFolder}/{source.name}_URP.mat";
            Material converted = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (converted == null)
            {
                converted = new Material(lit) { name = source.name + "_URP" };
                Texture texture = source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
                Color color = source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
                converted.SetTexture("_BaseMap", texture);
                converted.SetColor("_BaseColor", color);
                converted.SetFloat("_Smoothness", 0.15f);

                string shaderName = source.shader.name;
                if (shaderName.Contains("Transparent") || shaderName.Contains("Particles"))
                {
                    // 樹葉、地面標線：以 Alpha 裁切取代半透明，並雙面顯示
                    converted.SetFloat("_AlphaClip", 1f);
                    converted.SetFloat("_Cutoff", 0.5f);
                    converted.EnableKeyword("_ALPHATEST_ON");
                    converted.SetFloat("_Cull", 0f);
                    converted.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                }
                AssetDatabase.CreateAsset(converted, path);
            }

            remaps.arraySize++;
            SerializedProperty entry = remaps.GetArrayElementAtIndex(remaps.arraySize - 1);
            entry.FindPropertyRelative("from").objectReferenceValue = source;
            entry.FindPropertyRelative("to").objectReferenceValue = converted;
        }
    }

    public static void ResetPropRules(OrganicCityGenerator generator)
    {
        SerializedObject serialized = new SerializedObject(generator);
        FillDefaultPropRules(serialized.FindProperty("propRules"));
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private const int Main = 1, Secondary = 2, Lane = 4;
    private const int Sidewalk = 0, Intersection = 1, Park = 2;
    private const int FaceRoad = 0, AlongRoad = 1, RandomFacing = 2;

    /// <summary>LowpolyStreetPack 的預設設施規則 (路燈與紅綠燈的燈臂沿模型 +X，因此旋轉 -90 讓燈臂伸向車道)</summary>
    private static void FillDefaultPropRules(SerializedProperty rules)
    {
        rules.arraySize = 0;
        AddRule(rules, "路燈", StreetPrefabs("StreetProps/TraficLights", "LampPost_D", "LampPost_J"),
            Sidewalk, Main | Secondary, 28f, 1f, 0.05f, true, FaceRoad, -90f, 1f, 1f, 4f);
        AddRule(rules, "紅綠燈", StreetPrefabs("StreetProps/TraficLights", "LampPost_B", "LampPost_C", "LampPost_F"),
            Intersection, Main | Secondary, 0f, 0.85f, 0.05f, true, FaceRoad, -90f, 1f, 1f, 2.5f);
        AddRule(rules, "路牌", StreetPrefabs("StreetProps/Signs/StreetSigns", "StreetSign_"),
            Intersection, Main | Secondary | Lane, 0f, 0.5f, 0.6f, true, AlongRoad, 180f, 1f, 1f, 1.5f);
        AddRule(rules, "巷道路燈", StreetPrefabs("StreetProps/TraficLights", "LampPost_A"),
            Sidewalk, Lane, 32f, 0.8f, 0.1f, false, FaceRoad, -90f, 1f, 1f, 4f);
        AddRule(rules, "電線桿", StreetPrefabs("StreetProps/Powerpoles", "Powerpole_A_A", "Powerpole_B_A"),
            Sidewalk, Lane, 36f, 0.9f, 0.9f, false, AlongRoad, 0f, 1f, 1f, 4f);
        AddRule(rules, "行道樹", StreetPrefabs("Foliage/Trees", "Tree_"),
            Sidewalk, Secondary | Lane, 16f, 0.55f, 0.85f, true, RandomFacing, 0f, 1.2f, 1.6f, 3f);
        AddRule(rules, "盆栽", StreetPrefabs("StreetProps/PlantPots/QuickBuild", "PlantPot_A", "PlantPot_B"),
            Sidewalk, Main, 40f, 0.5f, 0.75f, true, AlongRoad, 0f, 1f, 1f, 3f);
        AddRule(rules, "消防栓", StreetPrefabs("StreetProps/Hidrant", "Hidrant"),
            Sidewalk, Main | Secondary | Lane, 70f, 0.7f, 0.12f, true, FaceRoad, 0f, 1f, 1f, 2f);
        AddRule(rules, "長椅", StreetPrefabs("StreetProps/Bench", "Bench_"),
            Sidewalk, Main | Secondary, 55f, 0.5f, 0.8f, true, FaceRoad, 0f, 1f, 1f, 2.5f);
        AddRule(rules, "郵筒", StreetPrefabs("StreetProps/MailBox", "MailBox"),
            Sidewalk, Main | Secondary, 120f, 0.6f, 0.8f, true, FaceRoad, 0f, 1f, 1f, 2f);
        AddRule(rules, "電話亭 / 報攤", StreetPrefabs("StreetProps", "Phone", "NewsBoard"),
            Sidewalk, Main, 140f, 0.6f, 0.85f, true, FaceRoad, 0f, 1f, 1f, 3f);
        AddRule(rules, "電箱", StreetPrefabs("StreetProps/FuseBox", "FuseBox"),
            Sidewalk, Secondary | Lane, 110f, 0.5f, 0.9f, true, AlongRoad, 0f, 1f, 1f, 2f);
        AddRule(rules, "公園長椅", StreetPrefabs("StreetProps/Bench", "Bench_"),
            Park, Main | Secondary | Lane, 18f, 1f, 0f, true, RandomFacing, 0f, 1f, 1f, 3f);
        AddRule(rules, "公園燈", StreetPrefabs("StreetProps/ParkLamp", "ParkLamp"),
            Park, Main | Secondary | Lane, 20f, 1f, 0f, true, RandomFacing, 0f, 1f, 1f, 4f);
        AddRule(rules, "灌木", StreetPrefabs("Foliage/Bushes", "Bush_"),
            Park, Main | Secondary | Lane, 9f, 1f, 0f, true, RandomFacing, 0f, 1.2f, 1.8f, 2f);
    }

    private static void AddRule(SerializedProperty rules, string ruleName, Object[] prefabs, int placement, int roads,
        float spacing, float chance, float sidewalkPosition, bool bothSides, int facing, float yawOffset,
        float scaleMin, float scaleMax, float clearance)
    {
        if (prefabs.Length == 0) return;

        rules.arraySize++;
        SerializedProperty rule = rules.GetArrayElementAtIndex(rules.arraySize - 1);
        rule.FindPropertyRelative("name").stringValue = ruleName;
        rule.FindPropertyRelative("enabled").boolValue = true;
        SetArray(rule.FindPropertyRelative("prefabs"), prefabs);
        rule.FindPropertyRelative("placement").enumValueIndex = placement;
        rule.FindPropertyRelative("roads").intValue = roads;
        rule.FindPropertyRelative("spacing").floatValue = Mathf.Max(2f, spacing);
        rule.FindPropertyRelative("chance").floatValue = chance;
        rule.FindPropertyRelative("sidewalkPosition").floatValue = sidewalkPosition;
        rule.FindPropertyRelative("bothSides").boolValue = bothSides;
        rule.FindPropertyRelative("facing").enumValueIndex = facing;
        rule.FindPropertyRelative("yawOffset").floatValue = yawOffset;
        rule.FindPropertyRelative("scaleRange").vector2Value = new Vector2(scaleMin, scaleMax);
        rule.FindPropertyRelative("clearance").floatValue = clearance;
    }

    /// <summary>LowpolyStreetPack 指定資料夾 (含子資料夾) 中檔名以任一前綴開頭的 Prefab</summary>
    private static Object[] StreetPrefabs(string folder, params string[] prefixes)
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

    /// <summary>以 LowpolyStreetPack 的貼圖建立 URP Lit 材質；貼圖不存在時回傳 null</summary>
    private static Material GetTexturedMaterial(string materialName, string textureFile, Color tint, float smoothness)
    {
        string path = $"{MaterialFolder}/{materialName}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Download/LowpolyStreetPack/Meshes/Textures/{textureFile}");
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (texture == null || shader == null) return null;

        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Materials", "OrganicCity");

        Material material = new Material(shader) { name = materialName };
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", tint);
        material.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Material GetMaterial(string materialName, Color color, float smoothness = 0.15f)
    {
        string path = $"{MaterialFolder}/{materialName}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Materials", "OrganicCity");

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new Material(shader) { name = materialName, color = color };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(material, path);
        return material;
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

    private static void SetArray(SerializedProperty property, Object[] values)
    {
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }

    /// <summary>遠景霧氣，淡化地面邊界</summary>
    private static void ApplyFog()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.62f, 0.74f, 0.82f);
        RenderSettings.fogStartDistance = 300f;
        RenderSettings.fogEndDistance = 900f;
    }

    private static void ApplySkybox()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Download/Free Stylized Skybox" }))
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (material != null && material.shader != null && material.shader.name.ToLowerInvariant().Contains("skybox"))
            {
                RenderSettings.skybox = material;
                return;
            }
        }
    }

    #endregion
}

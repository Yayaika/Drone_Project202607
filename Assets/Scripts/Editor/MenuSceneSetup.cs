using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 一鍵佈置開始選單與選擇無人機場景的動態背景 (選單：無人機遊戲/6)。
/// 只新增「MenuDecor」與「MenuCity」兩個物件 (重複執行會先移除舊的)，並修正選擇按鈕文字；既有 UI 與邏輯不變。
/// </summary>
public static class MenuSceneSetup
{
    private const string StartMenuPath = "Assets/Scenes/StartMenu.unity";
    private const string SelectScenePath = "Assets/Scenes/" + LevelCatalog.DroneSelectScene + ".unity";
    private const string MorningSkyPath = "Assets/Download/Free Stylized Skybox/CubeMap/Materials/Sky_Anime_11_morning_a.mat";
    private const string DecorName = "MenuDecor";
    private const string CityName = "MenuCity";

    [MenuItem("無人機遊戲/6. 佈置選單場景背景 (開始選單 / 選擇無人機)", priority = 6)]
    public static void SetupFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string result = SetupAll();
        EditorUtility.DisplayDialog("選單場景", result, "確定");
    }

    /// <summary>佈置兩個選單場景並存檔，回傳結果摘要 (不顯示對話框，可由自動化呼叫)</summary>
    public static string SetupAll()
    {
        string report = SetupStartMenu() + "\n" + SetupSelectScene();
        AssetDatabase.SaveAssets();
        return report;
    }

    private static string SetupStartMenu()
    {
        Scene scene = EditorSceneManager.OpenScene(StartMenuPath, OpenSceneMode.Single);
        RemoveExisting(scene);

        GameObject drone = FindRoot(scene, "Drone_Parent Variant");
        MenuSceneDecor decor = new GameObject(DecorName).AddComponent<MenuSceneDecor>();
        SerializedObject so = new SerializedObject(decor);
        so.FindProperty("skybox").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(MorningSkyPath);
        so.FindProperty("fogColor").colorValue = new Color(0.86f, 0.84f, 0.8f);
        so.FindProperty("heroDrone").objectReferenceValue = drone != null ? drone.transform : null;
        so.FindProperty("ambientDroneTemplate").objectReferenceValue = drone != null ? drone.transform : null;
        so.FindProperty("flightCenter").vector3Value = new Vector3(0f, 4f, 24f);
        so.FindProperty("flightRadius").vector3Value = new Vector3(17f, 2.5f, 8f);
        so.ApplyModifiedPropertiesWithoutUndo();

        // 從「高樓頂」眺望城市：城市在相機前下方
        CreateCity(new Vector3(0f, -18f, 140f), seed: 2026, maxHeight: 30f);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"開始選單：已加入背景城市與裝飾 (主角無人機 {(drone != null ? "OK" : "找不到")})";
    }

    private static string SetupSelectScene()
    {
        Scene scene = EditorSceneManager.OpenScene(SelectScenePath, OpenSceneMode.Single);
        RemoveExisting(scene);

        UIPlayerSelectCtrl select = Object.FindFirstObjectByType<UIPlayerSelectCtrl>(FindObjectsInactive.Include);
        Transform anchor = select != null ? select.transform.Find("PreviewAnchor") : null;
        GameObject template = FindRoot(scene, "Drone_Parent Variant");
        GameObject terrain = FindRoot(scene, "Terrain");

        MenuSceneDecor decor = new GameObject(DecorName).AddComponent<MenuSceneDecor>();
        SerializedObject so = new SerializedObject(decor);
        so.FindProperty("ambientDroneTemplate").objectReferenceValue = template != null ? template.transform : null;
        so.FindProperty("flightCenter").vector3Value = new Vector3(0f, 3f, 32f);
        so.FindProperty("flightRadius").vector3Value = new Vector3(20f, 3f, 8f);
        so.FindProperty("selectController").objectReferenceValue = select;
        so.FindProperty("previewAnchor").objectReferenceValue = anchor;
        so.FindProperty("buildRooftop").boolValue = true;
        so.FindProperty("hideObject").objectReferenceValue = terrain;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 站在屋頂停機坪上：城市在下方 (樓高限制在屋頂以下)
        CreateCity(new Vector3(0f, -14.5f, 70f), seed: 1314, maxHeight: 14f);

        string buttonResult = FixEnterButton(select);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"選擇無人機：已加入屋頂停機坪、展示台與背景城市 (展示點 {(anchor != null ? "OK" : "找不到")}，{buttonResult})";
    }

    private static void CreateCity(Vector3 position, int seed, float maxHeight)
    {
        GameObject cityObject = new GameObject(CityName);
        cityObject.transform.position = position;
        CityGenerator generator = cityObject.AddComponent<CityGenerator>();

        // 與關卡相同的素材 (建築、配色、車輛、路人)
        MethodInfo assign = typeof(DroneGameSetupMenu).GetMethod("AssignCityAssets", BindingFlags.NonPublic | BindingFlags.Static);
        assign?.Invoke(null, new object[] { generator });

        SerializedObject so = new SerializedObject(generator);
        so.FindProperty("useLevelCatalog").boolValue = false;
        so.FindProperty("seed").intValue = seed;
        so.FindProperty("blocks").intValue = 3;
        so.FindProperty("buildingDensity").floatValue = 0.9f;
        so.FindProperty("maxBuildingHeight").floatValue = maxHeight;
        so.FindProperty("buildRaceCourseInRaceMode").boolValue = false;
        so.FindProperty("addDeliveryManagerInDeliveryMode").boolValue = false;
        so.FindProperty("carsPerLane").floatValue = 0.25f;
        so.FindProperty("pedestriansPerBlock").intValue = 3;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>選擇按鈕原文字為錯字「Selete Game」，改為中文並使用名稱欄位的中文字型</summary>
    private static string FixEnterButton(UIPlayerSelectCtrl select)
    {
        if (select == null) return "找不到選擇面板";
        Transform enter = select.transform.Find("[Panel]Right/[BTN]Enter");
        TextMeshProUGUI label = enter != null ? enter.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        if (label == null) return "找不到按鈕文字";

        Transform nameText = select.transform.Find("[Panel]Right/[TMP]Name");
        TextMeshProUGUI nameLabel = nameText != null ? nameText.GetComponent<TextMeshProUGUI>() : null;
        Undo.RecordObject(label, "Fix enter button");
        if (nameLabel != null && nameLabel.font != null) label.font = nameLabel.font;
        label.text = "開始任務";
        EditorUtility.SetDirty(label);
        return "按鈕文字已改為「開始任務」";
    }

    private static void RemoveExisting(Scene scene)
    {
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            if (rootObject.name == DecorName || rootObject.name == CityName)
            {
                Object.DestroyImmediate(rootObject);
            }
        }
    }

    private static GameObject FindRoot(Scene scene, string objectName)
    {
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            if (rootObject.name == objectName) return rootObject;
        }
        return null;
    }
}

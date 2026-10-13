using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 把非程序化城市的場景植物換成 Anime Nature (選單：無人機遊戲/8)：
/// 訓練場 (CourseBuilder) 樹木/灌木/草叢、有機城市測試場景的行道樹與灌木、City2 的低模樹籬 (改為灌木並加上草叢)。
/// 城市關卡與選單背景的植物由 CityGenerator 處理 (選單 7 重新烘焙)。
/// </summary>
public static class VegetationSetup
{
    private const string VegetationRootName = "AnimeVegetation";

    [MenuItem("無人機遊戲/8. 套用 Anime Nature 植物到訓練場 / City2 / 有機城市", priority = 8)]
    public static void SetupFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorUtility.DisplayDialog("植物", SetupAll(), "確定");
    }

    public static string SetupAll()
    {
        List<string> lines = new List<string>
        {
            SetupTraining("Assets/Scenes/SampleScene.unity"),
            SetupOrganicCity("Assets/Scenes/OrganicCityTest.unity"),
            SetupCity2("Assets/Scenes/City2.unity")
        };
        return string.Join("\n", lines);
    }

    private static Object[] Trees => CityLevelBaker.NaturePrefabs("Tree_02", "Tree_03", "Tree_04", "Tree_05", "Tree_06", "Tree_07", "Tree_08");
    private static Object[] Bushes => CityLevelBaker.NaturePrefabs("Bush_");
    private static Object[] Grass => CityLevelBaker.NaturePrefabs("Grass.prefab");

    private static string SetupTraining(string path)
    {
        if (!File.Exists(path)) return $"找不到 {path}";
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        CourseBuilder builder = Object.FindFirstObjectByType<CourseBuilder>(FindObjectsInactive.Include);
        if (builder == null) return "訓練場：找不到 CourseBuilder";

        SerializedObject so = new SerializedObject(builder);
        SetArray(so.FindProperty("treePrefabs"), Trees);
        SetArray(so.FindProperty("bushPrefabs"), Bushes);
        SetArray(so.FindProperty("grassPrefabs"), Grass);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "訓練場：樹木、灌木、草叢改用 Anime Nature (執行時生成)";
    }

    private static string SetupOrganicCity(string path)
    {
        if (!File.Exists(path)) return $"找不到 {path}";
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        OrganicCityGenerator generator = Object.FindFirstObjectByType<OrganicCityGenerator>(FindObjectsInactive.Include);
        if (generator == null) return "有機城市：找不到 OrganicCityGenerator";

        SerializedObject so = new SerializedObject(generator);
        SetArray(so.FindProperty("treePrefabs"), Trees);
        so.FindProperty("treeScaleRange").vector2Value = new Vector2(0.45f, 0.65f);
        SerializedProperty rules = so.FindProperty("propRules");
        int changed = 0;
        for (int i = 0; i < rules.arraySize; i++)
        {
            SerializedProperty rule = rules.GetArrayElementAtIndex(i);
            string ruleName = rule.FindPropertyRelative("name").stringValue;
            if (ruleName.Contains("行道樹"))
            {
                SetArray(rule.FindPropertyRelative("prefabs"), Trees);
                rule.FindPropertyRelative("scaleRange").vector2Value = new Vector2(0.4f, 0.55f);
                changed++;
            }
            else if (ruleName.Contains("灌木"))
            {
                SetArray(rule.FindPropertyRelative("prefabs"), Bushes);
                rule.FindPropertyRelative("scaleRange").vector2Value = new Vector2(0.2f, 0.32f);
                changed++;
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        generator.Generate();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"有機城市：樹木與 {changed} 條植物規則改用 Anime Nature，已重新生成";
    }

    /// <summary>City2：低模樹籬隱藏外觀 (保留碰撞)，沿樹籬排列灌木；草地步道上散佈草叢</summary>
    private static string SetupCity2(string path)
    {
        if (!File.Exists(path)) return $"找不到 {path}";
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            if (rootObject.name == VegetationRootName) Object.DestroyImmediate(rootObject);
        }

        Object[] bushes = Bushes;
        Object[] grass = Grass;
        if (bushes.Length == 0) return "City2：找不到 Anime Nature 灌木";

        Transform vegetation = new GameObject(VegetationRootName).transform;
        System.Random random = new System.Random(2026);
        int hedges = 0, bushCount = 0, grassCount = 0;

        foreach (MeshFilter filter in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (filter.sharedMesh == null) continue;
            string meshPath = AssetDatabase.GetAssetPath(filter.sharedMesh).ToLowerInvariant();
            MeshRenderer meshRenderer = filter.GetComponent<MeshRenderer>();
            if (meshRenderer == null) continue;

            if (meshPath.Contains("hedge"))
            {
                Bounds bounds = meshRenderer.bounds;
                meshRenderer.enabled = false;
                hedges++;
                bool alongX = bounds.size.x >= bounds.size.z;
                float length = alongX ? bounds.size.x : bounds.size.z;
                float height = Mathf.Clamp(bounds.size.y * 1.3f, 0.8f, 2.4f);
                int count = Mathf.Max(1, Mathf.RoundToInt(length / (height * 0.9f)));
                for (int i = 0; i < count; i++)
                {
                    float t = count == 1 ? 0.5f : i / (float)(count - 1);
                    Vector3 p = new Vector3(
                        alongX ? Mathf.Lerp(bounds.min.x, bounds.max.x, t) : bounds.center.x,
                        bounds.min.y,
                        alongX ? bounds.center.z : Mathf.Lerp(bounds.min.z, bounds.max.z, t));
                    if (Place((GameObject)bushes[random.Next(bushes.Length)], p, height * 1.3f, true, random, vegetation)) bushCount++;
                }
            }
            else if (grass.Length > 0 && meshRenderer.sharedMaterials.Any(m => m != null && m.name.ToLowerInvariant().Contains("grass")))
            {
                Bounds bounds = meshRenderer.bounds;
                int count = Mathf.Clamp(Mathf.RoundToInt(bounds.size.x * bounds.size.z / 40f), 0, 12);
                for (int i = 0; i < count && grassCount < 600; i++)
                {
                    Vector3 p = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, (float)random.NextDouble()), bounds.max.y,
                        Mathf.Lerp(bounds.min.z, bounds.max.z, (float)random.NextDouble()));
                    if (Place((GameObject)grass[random.Next(grass.Length)], p, (float)(1.6 + random.NextDouble()), true, random, vegetation, false)) grassCount++;
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"City2：{hedges} 段樹籬換成 {bushCount} 棵灌木，加入 {grassCount} 叢草";
    }

    private static bool Place(GameObject prefab, Vector3 position, float size, bool byWidth, System.Random random, Transform parent, bool shadows = true)
    {
        if (prefab == null) return false;
        GameObject plant = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        plant.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f) * prefab.transform.rotation);
        Renderer[] renderers = plant.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return true;
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
        float measured = byWidth ? Mathf.Max(bounds.size.x, bounds.size.z) : bounds.size.y;
        if (measured > 0.01f) plant.transform.localScale *= size / measured;
        foreach (Renderer r in renderers)
        {
            if (!shadows) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        if (plant.GetComponent<LODGroup>() == null)
        {
            LODGroup group = plant.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(shadows ? 0.04f : 0.08f, renderers) });
            group.RecalculateBounds();
        }
        foreach (Transform part in plant.GetComponentsInChildren<Transform>()) part.gameObject.isStatic = true;
        return true;
    }

    private static void SetArray(SerializedProperty property, Object[] values)
    {
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
}

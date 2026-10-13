using UnityEditor;
using UnityEngine;

/// <summary>建立晝夜天氣設定資產 Resources/EnvironmentProfile.asset (選單：無人機遊戲/9)</summary>
public static class EnvironmentSetup
{
    private const string ProfilePath = "Assets/Resources/EnvironmentProfile.asset";
    private const string NightSkyPath = "Assets/Download/Free Stylized Skybox/CubeMap/Materials/Sky_LowPoly_02_Night_a.mat";

    [MenuItem("無人機遊戲/9. 建立晝夜天氣設定 (Resources/EnvironmentProfile)", priority = 9)]
    public static void CreateFromMenu()
    {
        EnvironmentProfile profile = EnsureProfile();
        Selection.activeObject = profile;
        EditorGUIUtility.PingObject(profile);
    }

    public static EnvironmentProfile EnsureProfile()
    {
        EnvironmentProfile profile = AssetDatabase.LoadAssetAtPath<EnvironmentProfile>(ProfilePath);
        if (profile == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            profile = ScriptableObject.CreateInstance<EnvironmentProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }
        if (profile.nightSkybox == null)
        {
            profile.nightSkybox = AssetDatabase.LoadAssetAtPath<Material>(NightSkyPath);
            EditorUtility.SetDirty(profile);
        }
        AssetDatabase.SaveAssets();
        return profile;
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把 POLYGON city pack 的配色材質填入城市生成器 (CityGenerator / OrganicCityGenerator 共用欄位名稱)：
/// texturedModels、facadeMaterials、roofMaterials、windowMaterials、trimMaterial、doorMaterial、accentMaterial。
/// 只引用素材包現有的材質，不修改素材包資產。
/// </summary>
public static class PolygonCityPackAssets
{
    private const string PackMaterialRoot = "Assets/Download/POLYGON city pack/Materials";

    /// <summary>模型名稱 → 素材包 Materials/Material_meshs 內的貼圖材質</summary>
    private static readonly Dictionary<string, string> TexturedModelMaterials = new Dictionary<string, string>
    {
        { "Car_repair", "Car repair" },
        { "Fire_department", "Fire_department" },
        { "Building_I_3", "i4" },
        { "Shop_A", "shop a" },
        { "Shop_B", "shop b" },
        { "Shop_C", "Shop_c" },
        { "Supermaket", "supermarket" },
        { "Gas_station_A", "gas pump" },
        { "hedge", "hedge" },
        { "Fence_B_2", "Fence_B_2" }
    };

    /// <param name="models">需要對照專屬貼圖的模型 (建築與道具)</param>
    public static void AssignPalette(SerializedObject serialized, IEnumerable<GameObject> models)
    {
        SerializedProperty textured = serialized.FindProperty("texturedModels");
        textured.arraySize = 0;
        foreach (GameObject model in models.Where(model => model != null).Distinct())
        {
            if (!TexturedModelMaterials.TryGetValue(model.name, out string materialName)) continue;
            Material material = AssetDatabase.LoadAssetAtPath<Material>($"{PackMaterialRoot}/Material_meshs/{materialName}.mat");
            if (material == null) continue;

            textured.arraySize++;
            SerializedProperty entry = textured.GetArrayElementAtIndex(textured.arraySize - 1);
            entry.FindPropertyRelative("Model").objectReferenceValue = model;
            entry.FindPropertyRelative("Material").objectReferenceValue = material;
        }

        // 無貼圖的大樓 → Colors_buildings 調色盤 (排除過於鮮豔的顏色)
        SetArray(serialized.FindProperty("facadeMaterials"), Colors("A", "A1", "A3", "B1", "H1", "J1", "K4", "k5", "K6", "K7", "K8", "K9", "k11"));
        SetArray(serialized.FindProperty("roofMaterials"), Colors("A2", "J2", "J3", "K1"));
        SetArray(serialized.FindProperty("windowMaterials"),
            new Object[] { AssetDatabase.LoadAssetAtPath<Material>($"{PackMaterialRoot}/color_windows/Windows no light.mat") }.Where(m => m != null).ToArray());
        serialized.FindProperty("trimMaterial").objectReferenceValue = Colors("K1").FirstOrDefault();
        serialized.FindProperty("doorMaterial").objectReferenceValue = Colors("J3").FirstOrDefault();
        serialized.FindProperty("accentMaterial").objectReferenceValue = Colors("I3").FirstOrDefault();
    }

    private static Object[] Colors(params string[] names)
    {
        return names
            .Select(name => (Object)AssetDatabase.LoadAssetAtPath<Material>($"{PackMaterialRoot}/Colors_buildings/{name}.mat"))
            .Where(material => material != null)
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
}

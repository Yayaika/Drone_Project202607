using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 執行時建立的材質 (URP)。同色材質會共用，避免每個物件各自產生材質實例。
/// </summary>
public static class RuntimeMaterials
{
    private static readonly Dictionary<Color, Material> opaqueCache = new Dictionary<Color, Material>();
    private static readonly Dictionary<Color, Material> glowCache = new Dictionary<Color, Material>();
    private static readonly Dictionary<Color, Material> unlitCache = new Dictionary<Color, Material>();

    /// <summary>不透明 Lit 材質</summary>
    public static Material Opaque(Color color)
    {
        if (opaqueCache.TryGetValue(color, out Material cached) && cached != null) return cached;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material material = new Material(shader) { name = $"Runtime_Opaque_{ColorUtility.ToHtmlStringRGB(color)}" };
        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        opaqueCache[color] = material;
        return material;
    }

    /// <summary>不透明、不受光的自發光色 (紅綠燈燈號、車燈)</summary>
    public static Material Unlit(Color color)
    {
        if (unlitCache.TryGetValue(color, out Material cached) && cached != null) return cached;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) return Opaque(color);
        Material material = new Material(shader) { name = $"Runtime_Unlit_{ColorUtility.ToHtmlStringRGB(color)}" };
        material.SetColor("_BaseColor", color);
        unlitCache[color] = material;
        return material;
    }

    /// <summary>半透明發光材質 (任務區域光柱、地面光圈)</summary>
    public static Material Glow(Color color)
    {
        if (glowCache.TryGetValue(color, out Material cached) && cached != null) return cached;

        Material material;
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlit != null)
        {
            material = new Material(unlit);
            material.SetFloat("_Surface", 1f); // Transparent
            material.SetFloat("_Blend", 0f);   // Alpha
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetColor("_BaseColor", color);
        }
        else
        {
            // 備援：內建一定包含的 Sprites/Default 也支援透明
            material = new Material(Shader.Find("Sprites/Default"));
        }

        material.name = $"Runtime_Glow_{ColorUtility.ToHtmlStringRGBA(color)}";
        material.color = color;
        glowCache[color] = material;
        return material;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        opaqueCache.Clear();
        glowCache.Clear();
        unlitCache.Clear();
    }
}

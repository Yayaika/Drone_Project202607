using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 程序化產生可無縫平鋪的 PBR 貼圖 (顏色 + 法線)，存成 PNG 放在 <see cref="Folder"/>。
/// 用於寫實化材質與 Unity Terrain 圖層；不需要外部素材，重新執行會覆蓋 (同一名稱結果固定)。
/// 雜訊為週期性 value noise，週期等於貼圖寬度，所以平鋪沒有接縫。
/// </summary>
public static class ProceduralTextures
{
    public const string Folder = "Assets/Textures/Procedural";
    private const int Size = 512;

    /// <summary>一組貼圖：顏色 (sRGB) 與法線</summary>
    public struct Set
    {
        public Texture2D Albedo;
        public Texture2D Normal;
    }

    #region 雜訊

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xffffff) / (float)0xffffff;
        }
    }

    /// <summary>週期為 period 個格點的 value noise (0~1)，u/v 為 0~1 的貼圖座標</summary>
    private static float Value(float u, float v, int period, int seed) => Value(u, v, period, period, seed);

    /// <summary>U、V 方向週期不同的 value noise：拉長的紋理 (垂流痕、岩層) 仍可無縫平鋪</summary>
    private static float Value(float u, float v, int periodU, int periodV, int seed)
    {
        float x = u * periodU, y = v * periodV;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        int WrapU(int i) => ((i % periodU) + periodU) % periodU;
        int WrapV(int i) => ((i % periodV) + periodV) % periodV;
        float a = Hash(WrapU(x0), WrapV(y0), seed), b = Hash(WrapU(x0 + 1), WrapV(y0), seed);
        float c = Hash(WrapU(x0), WrapV(y0 + 1), seed), d = Hash(WrapU(x0 + 1), WrapV(y0 + 1), seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    /// <summary>碎形雜訊 (0~1)：從 basePeriod 開始，每層週期加倍</summary>
    private static float Fbm(float u, float v, int basePeriod, int octaves, int seed) => Fbm(u, v, basePeriod, basePeriod, octaves, seed);

    private static float Fbm(float u, float v, int periodU, int periodV, int octaves, int seed)
    {
        float sum = 0f, amplitude = 0.5f, total = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Value(u, v, periodU << i, periodV << i, seed + i * 31) * amplitude;
            total += amplitude;
            amplitude *= 0.5f;
        }
        return sum / total;
    }

    /// <summary>山脊雜訊 (0~1)：岩石裂紋</summary>
    private static float Ridged(float u, float v, int basePeriod, int octaves, int seed)
    {
        return 1f - Mathf.Abs(Fbm(u, v, basePeriod, octaves, seed) * 2f - 1f);
    }

    #endregion

    #region 產生與存檔

    /// <summary>以 color(u, v) 與 height(u, v) 產生一組貼圖；strength 為法線強度</summary>
    private static Set Make(string name, Func<float, float, Color> color, Func<float, float, float> height, float strength)
    {
        Color[] albedo = new Color[Size * Size];
        float[] heights = new float[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float u = (x + 0.5f) / Size, v = (y + 0.5f) / Size;
                albedo[y * Size + x] = color(u, v);
                heights[y * Size + x] = height(u, v);
            }
        }

        Color[] normals = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float dx = heights[y * Size + (x + 1) % Size] - heights[y * Size + (x + Size - 1) % Size];
                float dy = heights[((y + 1) % Size) * Size + x] - heights[((y + Size - 1) % Size) * Size + x];
                Vector3 n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                normals[y * Size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
        }

        return new Set
        {
            Albedo = Save($"{name}_Albedo", albedo, false),
            Normal = Save($"{name}_Normal", normals, true)
        };
    }

    private static Texture2D Save(string name, Color[] pixels, bool normalMap)
    {
        EnsureFolder(Folder);
        string path = $"{Folder}/{name}.png";
        Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, normalMap);
        texture.SetPixels(pixels);
        texture.Apply();
        byte[] png = texture.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(texture);
        // 結果固定：內容相同時不重寫 (避免重新匯入，也避免檔案被 Editor 佔用時寫入失敗)
        bool unchanged = File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(png);
        if (!unchanged) File.WriteAllBytes(path, png);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !normalMap;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 8;
        importer.maxTextureSize = Size;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static Color Gray(float value) => new Color(value, value, value, 1f);

    #endregion

    #region 套用到材質

    /// <summary>
    /// 套用貼圖：uvMeters = 網格 UV 一單位代表幾公尺，tileMeters = 貼圖一張覆蓋幾公尺。
    /// color 為 null 時保留材質原本的底色 (貼圖為灰階，乘上底色)。
    /// </summary>
    public static void Apply(Material material, Set set, float uvMeters, float tileMeters, Color? color,
        float smoothness, float metallic, float bumpScale)
    {
        if (material == null) return;
        Vector2 scale = Vector2.one * (uvMeters / tileMeters);
        material.SetTexture("_BaseMap", set.Albedo);
        material.SetTextureScale("_BaseMap", scale);
        material.SetTexture("_BumpMap", set.Normal);
        material.SetFloat("_BumpScale", bumpScale);
        material.EnableKeyword("_NORMALMAP");
        if (color.HasValue)
        {
            material.SetColor("_BaseColor", color.Value);
            material.color = color.Value;
        }
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", metallic);
        EditorUtility.SetDirty(material);
    }

    /// <summary>載入 Download 內的貼圖組，並確保法線貼圖的匯入類型正確</summary>
    public static Set? FromAssets(string albedoPath, string normalPath)
    {
        Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
        if (albedo == null) return null;
        if (AssetImporter.GetAtPath(normalPath) is TextureImporter importer && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
        return new Set { Albedo = albedo, Normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) };
    }

    #endregion

    #region 材質用貼圖

    /// <summary>波浪鋼板 (貨櫃、倉庫外牆、屋頂)：沿 U 方向 8 道波紋，灰階 (乘上材質底色)，帶鏽斑與雨水垂流痕</summary>
    public static Set Corrugated()
    {
        const int ribs = 8;
        Color rust = new Color(0.62f, 0.42f, 0.3f);
        return Make("Corrugated",
            (u, v) =>
            {
                float dirt = Mathf.Lerp(0.78f, 1f, Fbm(u, v, 4, 5, 11));
                float streak = Mathf.Lerp(0.85f, 1f, Value(u, v, 64, 4, 12)); // 垂流痕：U 方向高頻、V 方向低頻
                float rustMask = Mathf.SmoothStep(0.62f, 0.78f, Fbm(u, v, 8, 5, 13));
                Color paint = Gray(Mathf.Clamp01(dirt * streak));
                return Color.Lerp(paint, rust * dirt, rustMask * 0.75f);
            },
            (u, v) =>
            {
                // 梯形波紋 (以 tanh 壓平的正弦)
                float wave = (float)Math.Tanh(Mathf.Sin(u * ribs * Mathf.PI * 2f) * 2.2f);
                return wave * 0.5f + Fbm(u, v, 16, 3, 14) * 0.03f;
            }, 7f);
    }

    /// <summary>混凝土地坪：每張 2×2 塊板 (板縫)，污漬與細顆粒，帶顏色 (材質底色用白色)</summary>
    public static Set Concrete()
    {
        return Make("Concrete",
            (u, v) =>
            {
                float stain = Fbm(u, v, 3, 5, 21);
                float grain = Value(u, v, 256, 22);
                float joint = Joint(u, 2) * Joint(v, 2);
                float value = (0.56f + stain * 0.16f + (grain - 0.5f) * 0.06f) * Mathf.Lerp(0.55f, 1f, joint);
                float oil = Mathf.SmoothStep(0.7f, 0.85f, Fbm(u, v, 6, 4, 23));
                return Gray(value * Mathf.Lerp(1f, 0.7f, oil)) * new Color(1f, 0.99f, 0.96f);
            },
            (u, v) => Joint(u, 2) * Joint(v, 2) * 0.6f + Value(u, v, 128, 24) * 0.08f + Fbm(u, v, 8, 3, 25) * 0.05f, 6f);
    }

    /// <summary>柏油：深灰細顆粒與淺色骨材、補丁</summary>
    public static Set Asphalt()
    {
        return Make("Asphalt",
            (u, v) =>
            {
                float grain = Value(u, v, 256, 31);
                float aggregate = Mathf.SmoothStep(0.8f, 0.92f, Value(u, v, 180, 32));
                float patch = Mathf.SmoothStep(0.6f, 0.7f, Fbm(u, v, 3, 4, 33));
                float value = 0.2f + (grain - 0.5f) * 0.05f + aggregate * 0.12f + Fbm(u, v, 4, 4, 34) * 0.05f;
                return Gray(value * Mathf.Lerp(1f, 0.75f, patch));
            },
            (u, v) => Value(u, v, 256, 31) * 0.5f + Value(u, v, 180, 32) * 0.3f, 4f);
    }

    /// <summary>鋼構髒污：斑駁污漬與垂流痕 (灰階，乘上材質底色)</summary>
    public static Set Grime()
    {
        return Make("Grime",
            (u, v) =>
            {
                float mottled = Mathf.Lerp(0.72f, 1f, Fbm(u, v, 4, 5, 41));
                float streak = Mathf.Lerp(0.82f, 1f, Value(u, v, 48, 4, 42));
                return Gray(mottled * streak);
            },
            (u, v) => Fbm(u, v, 16, 4, 43) * 0.2f, 3f);
    }

    /// <summary>海面波紋法線 (顏色貼圖不使用)</summary>
    public static Set Water()
    {
        return Make("Water",
            (u, v) => Color.white,
            (u, v) => Fbm(u, v, 6, 5, 51) * 0.7f + Value(u, v, 32, 52) * 0.3f, 7f);
    }

    /// <summary>板縫：靠近 count 等分線時趨近 0 (暗、凹)</summary>
    private static float Joint(float t, int count)
    {
        float f = t * count;
        float distance = Mathf.Abs(f - Mathf.Round(f)) / count * Size; // 到最近板縫的像素數
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 2.2f, distance));
    }

    #endregion

    #region 地形圖層

    public static Set TerrainGrass()
    {
        return Make("Terrain_Grass",
            (u, v) =>
            {
                float patch = Fbm(u, v, 4, 5, 61);
                float blade = Value(u, v, 200, 62);
                Color green = Color.Lerp(new Color(0.24f, 0.36f, 0.14f), new Color(0.42f, 0.5f, 0.22f), patch);
                return Color.Lerp(green, new Color(0.5f, 0.46f, 0.3f), Mathf.SmoothStep(0.68f, 0.8f, patch) * 0.6f) * Mathf.Lerp(0.85f, 1.1f, blade);
            },
            (u, v) => Value(u, v, 200, 62) * 0.4f + Fbm(u, v, 8, 3, 63) * 0.2f, 5f);
    }

    public static Set TerrainForest()
    {
        return Make("Terrain_Forest",
            (u, v) =>
            {
                float canopy = Fbm(u, v, 8, 5, 71);
                float gaps = Mathf.SmoothStep(0.2f, 0.32f, canopy);
                Color leaves = Color.Lerp(new Color(0.1f, 0.2f, 0.09f), new Color(0.22f, 0.34f, 0.14f), Value(u, v, 64, 72));
                return Color.Lerp(new Color(0.08f, 0.1f, 0.06f), leaves, gaps);
            },
            (u, v) => Fbm(u, v, 8, 5, 71) * 0.9f + Value(u, v, 64, 72) * 0.3f, 6f);
    }

    public static Set TerrainRock()
    {
        return Make("Terrain_Rock",
            (u, v) =>
            {
                float strata = Fbm(u, v, 6, 3, 5, 81);
                float crack = Mathf.SmoothStep(0.88f, 0.98f, Ridged(u, v, 4, 4, 82));
                Color stone = Color.Lerp(new Color(0.36f, 0.34f, 0.31f), new Color(0.56f, 0.53f, 0.48f), strata);
                return Color.Lerp(stone, stone * 0.45f, crack);
            },
            (u, v) => Fbm(u, v, 6, 3, 5, 81) * 0.8f - Mathf.SmoothStep(0.88f, 0.98f, Ridged(u, v, 4, 4, 82)) * 0.4f, 9f);
    }

    public static Set TerrainSnow()
    {
        return Make("Terrain_Snow",
            (u, v) =>
            {
                float drift = Fbm(u, v, 4, 5, 91);
                return Color.Lerp(new Color(0.78f, 0.82f, 0.9f), new Color(0.96f, 0.97f, 0.99f), drift);
            },
            (u, v) => Fbm(u, v, 4, 5, 91) * 0.6f, 3f);
    }

    public static Set TerrainSand()
    {
        return Make("Terrain_Sand",
            (u, v) =>
            {
                float grain = Value(u, v, 256, 101);
                float wet = Fbm(u, v, 3, 4, 102);
                return Color.Lerp(new Color(0.62f, 0.56f, 0.42f), new Color(0.8f, 0.74f, 0.58f), wet) * Mathf.Lerp(0.92f, 1.05f, grain);
            },
            (u, v) => Value(u, v, 256, 101) * 0.3f + Mathf.Sin((u + Fbm(u, v, 4, 3, 103) * 0.2f) * Mathf.PI * 2f * 12f) * 0.08f, 4f);
    }

    #endregion
}

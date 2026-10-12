using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 以程式建立 UI 元件的共用工具 (HUD、模式選擇面板、結算畫面共用)，
/// 讓新功能不需要手動修改場景/Prefab 也能運作。
/// </summary>
public static class RuntimeUI
{
    public static readonly Color PanelColor = new Color(0.04f, 0.08f, 0.12f, 0.88f);
    public static readonly Color AccentColor = new Color(0.2f, 1f, 0.75f, 1f);
    public static readonly Color ButtonColor = new Color(0.12f, 0.22f, 0.3f, 0.95f);
    public static readonly Color WarningColor = new Color(1f, 0.32f, 0.25f, 1f);

    private static Sprite triangleSprite;
    private static Sprite roundedSprite;
    private static TMP_FontAsset cachedFont;

    public static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = parent != null ? parent.gameObject.layer : 5;
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    public static RectTransform CreateCentered(string name, Transform parent, Vector2 position, Vector2 size)
    {
        RectTransform rect = CreateRect(name, parent);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    public static void Stretch(RectTransform rect, float padding = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padding, padding);
        rect.offsetMax = new Vector2(-padding, -padding);
    }

    public static Image CreateImage(string name, Transform parent, Vector2 position, Vector2 size, Color color, Sprite sprite = null)
    {
        RectTransform rect = CreateCentered(name, parent, position, size);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.sprite = sprite;
        image.raycastTarget = false;
        if (sprite == roundedSprite && sprite != null)
        {
            image.type = Image.Type.Sliced;
        }
        return image;
    }

    public static TextMeshProUGUI CreateText(string name, Transform parent, string content, float fontSize,
        Vector2 position, Vector2 size, TextAlignmentOptions alignment = TextAlignmentOptions.Center, TMP_FontAsset font = null)
    {
        RectTransform rect = CreateCentered(name, parent, position, size);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font != null ? font : ResolveFont();
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    public static Button CreateButton(string name, Transform parent, string label, Vector2 position, Vector2 size,
        UnityAction onClick, float fontSize = 14f, TMP_FontAsset font = null)
    {
        Image background = CreateImage(name, parent, position, size, ButtonColor, RoundedSprite);
        background.raycastTarget = true;

        Button button = background.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.75f, 1f, 0.95f);
        colors.pressedColor = new Color(0.55f, 0.8f, 0.75f);
        colors.selectedColor = new Color(0.75f, 1f, 0.95f);
        button.colors = colors;
        button.targetGraphic = background;
        if (onClick != null)
        {
            button.onClick.AddListener(onClick);
        }

        TextMeshProUGUI text = CreateText("Label", background.transform, label, fontSize, Vector2.zero, size, TextAlignmentOptions.Center, font);
        Stretch(text.rectTransform);

        background.gameObject.AddComponent<UIButtonTweenFx>();
        return button;
    }

    /// <summary>
    /// 優先使用專案內已載入的中文字型 (YuPearl)，避免中文顯示成方塊
    /// </summary>
    public static TMP_FontAsset ResolveFont()
    {
        if (cachedFont != null)
        {
            return cachedFont;
        }

        foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (font != null && font.name.IndexOf("YuPearl", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                cachedFont = font;
                return cachedFont;
            }
        }

        TextMeshProUGUI anyText = UnityEngine.Object.FindFirstObjectByType<TextMeshProUGUI>(FindObjectsInactive.Include);
        if (anyText != null && anyText.font != null)
        {
            cachedFont = anyText.font;
            return cachedFont;
        }

        return TMP_Settings.defaultFontAsset;
    }

    public static void SetPreferredFont(TMP_FontAsset font)
    {
        if (font != null)
        {
            cachedFont = font;
        }
    }

    /// <summary>程式產生的三角形 Sprite (箭頭/指標用)，不依賴字型是否有 ▲ 字元</summary>
    public static Sprite TriangleSprite
    {
        get
        {
            if (triangleSprite != null) return triangleSprite;

            const int size = 32;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float halfWidth = (size - 1 - y) * 0.5f;
                for (int x = 0; x < size; x++)
                {
                    bool inside = Mathf.Abs(x - (size - 1) * 0.5f) <= halfWidth;
                    pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            }
            // 貼圖第 0 列在最下方：底部最寬、頂端收尖，尖端朝上
            texture.SetPixels32(pixels);
            texture.Apply();
            triangleSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            triangleSprite.name = "RuntimeTriangle";
            return triangleSprite;
        }
    }

    /// <summary>程式產生的圓角矩形 Sprite (九宮格)，讓面板與按鈕不需要額外貼圖</summary>
    public static Sprite RoundedSprite
    {
        get
        {
            if (roundedSprite != null) return roundedSprite;

            const int size = 32;
            const float radius = 10f;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius)));
                    float dy = Mathf.Max(0f, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
                    float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            roundedSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            roundedSprite.name = "RuntimeRounded";
            return roundedSprite;
        }
    }

    public static string FormatTime(float seconds)
    {
        if (seconds < 0f) seconds = 0f;
        int minutes = Mathf.FloorToInt(seconds / 60f);
        int secs = Mathf.FloorToInt(seconds % 60f);
        int hundredths = Mathf.FloorToInt((seconds * 100f) % 100f);
        return $"{minutes:00}:{secs:00}.{hundredths:00}";
    }
}

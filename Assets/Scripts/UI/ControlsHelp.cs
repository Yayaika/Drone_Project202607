using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 操作說明的唯一資料來源與面板建構器。
/// 按鍵圖示使用 TMP Sprite Asset「all input icons same size」(Download/Input Sprites for TextMesh Pro)，
/// 以 &lt;sprite name="z"&gt; 內嵌在文字中，鍵位變更時只需修改這裡。
/// </summary>
public static class ControlsHelp
{
    public const string SpriteAssetName = "all input icons same size";

    private struct Entry
    {
        public string Icons;
        public string Description;

        public Entry(string icons, string description)
        {
            Icons = icons;
            Description = description;
        }
    }

    private static TMP_SpriteAsset cachedSpriteAsset;

    private static string Key(string spriteName) => $"<sprite name=\"{spriteName}\">";

    private static List<Entry> KeyboardEntries(bool hasPauseMenu)
    {
        List<Entry> entries = new List<Entry>
        {
            new Entry(Key("w") + Key("s"), "上升 / 下降（推滿 1 秒啟動引擎）"),
            new Entry(Key("a") + Key("d"), "向左 / 向右旋轉"),
            new Entry(Key("ku") + Key("kd"), "前進 / 後退"),
            new Entry(Key("kl") + Key("kr"), "向左 / 向右平移"),
            new Entry(Key("c"), "切換第一 / 第三人稱"),
            new Entry(Key("v"), "校準視角（第三人稱，朝向無人機）"),
            new Entry(Key("f"), "切換飛行模式（自穩 / 巡航 / 垂直）"),
            new Entry(Key("t"), "反轉方向鍵"),
            new Entry(Key("z"), "抓取 / 放下貨物"),
            new Entry(Key("r"), "回到最後檢查點"),
            new Entry(Key("h"), "切換 HUD 顯示樣式"),
            new Entry(Key("b"), "投放炸彈（炸彈無人機）"),
            new Entry(Key("esc"), "開啟 / 關閉設置（暫停）"),
        };

        if (hasPauseMenu)
        {
            entries.Add(new Entry(Key("f5"), "重新開始本關"));
        }
        return entries;
    }

    private static List<Entry> GamepadEntries()
    {
        return new List<Entry>
        {
            new Entry(Key("jl"), "升降 / 旋轉"),
            new Entry(Key("jr"), "前後 / 左右移動"),
            new Entry(Key("xa"), "抓取 / 放下貨物"),
            new Entry(Key("xb"), "回到最後檢查點"),
            new Entry(Key("lb") + Key("xy"), "切換視角"),
            new Entry(Key("jrclick"), "校準視角（第三人稱）"),
            new Entry(Key("rb"), "切換飛行模式"),
            new Entry(Key("share"), "切換 HUD 顯示樣式"),
            new Entry(Key("rt"), "投放炸彈（炸彈無人機）"),
            new Entry(Key("xmenu"), "開啟 / 關閉設置（暫停）"),
        };
    }

    /// <summary>目前遊戲模式的過關說明</summary>
    public static string ObjectiveText()
    {
        return LevelCatalog.SelectedMode switch
        {
            GameModeType.Delivery => "抓取貨物（黃色光柱），送到綠色光柱並放下，完成所有訂單！撞擊會損壞貨物。",
            GameModeType.Strike => "摧毀所有目標！紅色目標從空中投彈（B / 右扳機）；紫色目標在騎樓或頂棚下，炸彈打不到，要低飛繞進去撞擊；橘色是行駛中的車輛，投彈要預判或直接撞擊。",
            _ => "依序穿越所有檢查圈（黃色為下一個），最快抵達終點！"
        };
    }

    public static TMP_SpriteAsset ResolveSpriteAsset()
    {
        if (cachedSpriteAsset != null) return cachedSpriteAsset;

        if (TMP_Settings.defaultSpriteAsset != null && TMP_Settings.defaultSpriteAsset.name == SpriteAssetName)
        {
            cachedSpriteAsset = TMP_Settings.defaultSpriteAsset;
            return cachedSpriteAsset;
        }

        foreach (TMP_SpriteAsset asset in Resources.FindObjectsOfTypeAll<TMP_SpriteAsset>())
        {
            if (asset != null && asset.name == SpriteAssetName)
            {
                cachedSpriteAsset = asset;
                return cachedSpriteAsset;
            }
        }

        Debug.LogWarning($"[ControlsHelp] 找不到 TMP Sprite Asset「{SpriteAssetName}」，按鍵圖示將無法顯示。請確認 TMP Settings 的 Default Sprite Asset。");
        return null;
    }

    /// <summary>
    /// 在既有面板中建立操作說明 (標題 + 鍵盤 / 手把兩欄)，並隱藏舊的圖示與說明文字。
    /// 保留面板上的按鈕 (例如「退出」)。
    /// </summary>
    public static RectTransform BuildInto(RectTransform panel, bool hideLegacyContent = true)
    {
        Transform existing = panel.Find("ControlsHelpContent");
        if (existing != null) Object.Destroy(existing.gameObject);

        if (hideLegacyContent)
        {
            foreach (Transform child in panel)
            {
                if (child.GetComponent<Button>() != null) continue;
                child.gameObject.SetActive(false);
            }
        }

        StylePanelBackground(panel.GetComponent<Image>());

        Vector2 size = panel.rect.size;
        if (size.x < 10f || size.y < 10f) size = new Vector2(660f, 380f);

        RectTransform content = RuntimeUI.CreateCentered("ControlsHelpContent", panel, Vector2.zero, size);
        content.SetAsFirstSibling();
        content.SetSiblingIndex(0);

        bool hasPauseMenu = Object.FindFirstObjectByType<OptionsMenu>() != null;
        List<Entry> keyboard = KeyboardEntries(hasPauseMenu);
        List<Entry> gamepad = GamepadEntries();

        float padding = size.y * 0.04f;
        float titleHeight = size.y * 0.1f;
        float footerHeight = size.y * 0.12f;
        int rows = Mathf.Max(keyboard.Count, gamepad.Count + 1);
        float rowHeight = (size.y - padding * 2f - titleHeight - footerHeight) / rows;
        float fontSize = Mathf.Clamp(rowHeight * 0.72f, 12f, 40f);
        float top = size.y * 0.5f - padding;

        TextMeshProUGUI title = RuntimeUI.CreateText("Title", content, "操作說明", fontSize * 1.5f,
            new Vector2(0f, top - titleHeight * 0.5f), new Vector2(size.x - padding * 2f, titleHeight));
        title.color = RuntimeUI.AccentColor;
        title.fontStyle = FontStyles.Bold;

        float columnWidth = (size.x - padding * 3f) * 0.5f;
        float leftX = -columnWidth * 0.5f - padding * 0.5f;
        float rightX = columnWidth * 0.5f + padding * 0.5f;
        float firstRowY = top - titleHeight - rowHeight * 0.5f;

        // 左欄：鍵盤
        for (int i = 0; i < keyboard.Count; i++)
        {
            CreateRow(content, keyboard[i], new Vector2(leftX, firstRowY - i * rowHeight), new Vector2(columnWidth, rowHeight), fontSize);
        }

        // 右欄：手把 (第一列為小標題)
        TextMeshProUGUI padTitle = RuntimeUI.CreateText("GamepadTitle", content, "手把 / 遙控器", fontSize,
            new Vector2(rightX, firstRowY), new Vector2(columnWidth, rowHeight), TextAlignmentOptions.Left);
        padTitle.color = RuntimeUI.AccentColor;
        for (int i = 0; i < gamepad.Count; i++)
        {
            CreateRow(content, gamepad[i], new Vector2(rightX, firstRowY - (i + 1) * rowHeight), new Vector2(columnWidth, rowHeight), fontSize);
        }

        // 分隔線
        RuntimeUI.CreateImage("Divider", content, new Vector2(0f, firstRowY - rowHeight * (rows - 1) * 0.5f),
            new Vector2(2f, rowHeight * rows), new Color(1f, 1f, 1f, 0.15f));

        // 頁尾：目前模式的過關目標
        TextMeshProUGUI objective = RuntimeUI.CreateText("Objective", content, ObjectiveText(), fontSize * 0.9f,
            new Vector2(0f, -size.y * 0.5f + padding + footerHeight * 0.5f + size.y * 0.06f), new Vector2(size.x - padding * 2f, footerHeight));
        objective.color = new Color(1f, 0.85f, 0.3f);
        objective.textWrappingMode = TextWrappingModes.Normal;

        // 既有按鈕統一移到底部中央並套用樣式
        foreach (Button button in panel.GetComponentsInChildren<Button>(true))
        {
            if (button.transform.parent != panel) continue;
            RectTransform rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -size.y * 0.5f + padding * 0.5f + rowHeight * 0.45f);
            rect.sizeDelta = new Vector2(Mathf.Max(rect.sizeDelta.x, fontSize * 5f), rowHeight * 0.9f);
            StyleButton(button, "返回");
            button.transform.SetAsLastSibling();
        }

        return content;
    }

    /// <summary>建立獨立的操作說明彈出面板 (給沒有說明面板的場景，例如主選單)</summary>
    public static UIPopupPanel CreateStandalone(Transform canvas, Vector2 size)
    {
        RectTransform overlay = RuntimeUI.CreateRect("ControlsHelpOverlay", canvas);
        overlay.gameObject.SetActive(false); // 先關閉，避免加入元件時就播放開啟動畫
        RuntimeUI.Stretch(overlay);
        Image dim = overlay.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.6f);
        dim.raycastTarget = true;

        RectTransform panel = RuntimeUI.CreateCentered("ControlsHelpPanel", overlay, Vector2.zero, size);
        Image background = panel.gameObject.AddComponent<Image>();
        background.raycastTarget = true;

        UIPopupPanel popup = overlay.gameObject.AddComponent<UIPopupPanel>();
        popup.SetAnimatedTarget(panel);

        RuntimeUI.CreateButton("CloseButton", panel, "返回", Vector2.zero, new Vector2(160f, 50f), popup.Hide, 24f);
        BuildInto(panel, false);
        return popup;
    }

    private static void CreateRow(RectTransform parent, Entry entry, Vector2 position, Vector2 size, float fontSize)
    {
        TextMeshProUGUI row = RuntimeUI.CreateText("Row", parent, $"{entry.Icons}  {entry.Description}", fontSize,
            position, size, TextAlignmentOptions.MidlineLeft);
        row.spriteAsset = ResolveSpriteAsset();
        row.color = Color.white;
        row.overflowMode = TextOverflowModes.Ellipsis;
    }

    public static void StylePanelBackground(Image image)
    {
        if (image == null) return;
        image.sprite = RuntimeUI.RoundedSprite;
        image.type = Image.Type.Sliced;
        image.color = RuntimeUI.PanelColor;

        Outline outline = image.GetComponent<Outline>();
        if (outline == null) outline = image.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(RuntimeUI.AccentColor.r, RuntimeUI.AccentColor.g, RuntimeUI.AccentColor.b, 0.45f);
        outline.effectDistance = new Vector2(2f, -2f);
    }

    /// <summary>按鈕套用深色圓角樣式；label 不為 null 時同時替換文字</summary>
    public static void StyleButton(Button button, string label = null)
    {
        Image image = button.targetGraphic as Image ?? button.GetComponent<Image>();
        if (image != null)
        {
            image.sprite = RuntimeUI.RoundedSprite;
            image.type = Image.Type.Sliced;
            image.color = RuntimeUI.ButtonColor;
        }

        TextMeshProUGUI tmp = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null)
        {
            tmp.color = Color.white;
            tmp.font = RuntimeUI.ResolveFont();
            tmp.spriteAsset = ResolveSpriteAsset();
            if (label != null) tmp.text = label;
        }

        Text legacy = button.GetComponentInChildren<Text>(true);
        if (legacy != null)
        {
            legacy.color = Color.white;
            if (label != null) legacy.text = label;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cachedSpriteAsset = null;
    }
}

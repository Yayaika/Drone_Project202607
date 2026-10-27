using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 每次載入場景時，依物件名稱找到既有面板並在執行時優化 (不修改 Prefab/場景檔)：
/// - 無人機 HUD「[Panel]設置」：深色卡片、標題、音量百分比、按鈕樣式、返回選單、操作提示、開關動畫
/// - 「操作介紹面板」：重建為含按鍵圖示的鍵盤/手把操作說明
/// - 關卡「介紹面板」：依目前模式 (競速/搬運) 顯示正確的過關目標
/// - 主選單「SettingPanel」：加入音量滑桿與操作說明按鈕
/// - 主選單「ListPanel」(製作名單)：依場景中的名單文字重新排版為 職稱｜姓名｜學號 三欄
/// </summary>
public static class PanelEnhancer
{
    private const string DroneSettingsPanelName = "[Panel]設置";
    private const string ControlsPanelName = "操作介紹面板";
    private const string IntroPanelName = "介紹面板";
    private const string MainMenuSettingsName = "SettingPanel";
    private const string CreditsPanelName = "ListPanel";
    private const string VolumePrefsKey = "MasterVolume";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        EnhanceAll();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnhanceAll();
    }

    public static void EnhanceAll()
    {
        RectTransform[] rects = Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        bool isGameplay = Object.FindFirstObjectByType<GameManager>() != null;

        foreach (RectTransform rect in rects)
        {
            if (rect == null || rect.GetComponent<PanelEnhancedMarker>() != null) continue;

            // 場景中的製作名單物件名稱結尾帶空白 ("ListPanel ")
            switch (rect.name.Trim())
            {
                case DroneSettingsPanelName:
                    EnhanceDroneSettings(rect, isGameplay);
                    break;
                case ControlsPanelName:
                    MarkEnhanced(rect);
                    ControlsHelp.BuildInto(rect);
                    AddPopup(rect);
                    break;
                case IntroPanelName:
                    if (isGameplay) EnhanceIntroPanel(rect);
                    break;
                case MainMenuSettingsName:
                    if (rect.GetComponent<UIPanelCtrl>() != null) EnhanceMainMenuSettings(rect);
                    break;
                case CreditsPanelName:
                    if (rect.GetComponent<UIPanelCtrl>() != null) EnhanceCredits(rect);
                    break;
            }
        }

        UIPopupPanel.RewireCloseButtons(Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None));
    }

    #region 無人機 HUD 設置面板

    private static void EnhanceDroneSettings(RectTransform panel, bool isGameplay)
    {
        MarkEnhanced(panel);

        // 整個畫面半透明暗化，中間放一張深色卡片
        Image dim = panel.GetComponent<Image>();
        if (dim != null)
        {
            dim.sprite = null;
            dim.color = new Color(0f, 0f, 0f, 0.45f);
        }

        Image card = RuntimeUI.CreateImage("SettingsCard", panel, new Vector2(0f, -10f), new Vector2(400f, 290f), RuntimeUI.PanelColor, RuntimeUI.RoundedSprite);
        card.transform.SetSiblingIndex(0);
        Outline outline = card.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(RuntimeUI.AccentColor.r, RuntimeUI.AccentColor.g, RuntimeUI.AccentColor.b, 0.5f);
        outline.effectDistance = new Vector2(2f, -2f);

        TextMeshProUGUI title = RuntimeUI.CreateText("SettingsTitle", panel, "設置", 30f, new Vector2(0f, 105f), new Vector2(300f, 40f));
        title.color = RuntimeUI.AccentColor;
        title.fontStyle = FontStyles.Bold;

        // 音量：標籤顯示百分比，滑桿套用配色
        Slider slider = panel.GetComponentInChildren<Slider>(true);
        TextMeshProUGUI volumeLabel = FindText(panel, "Text 文字");
        if (volumeLabel != null)
        {
            volumeLabel.color = Color.white;
            volumeLabel.font = RuntimeUI.ResolveFont();
        }
        if (slider != null)
        {
            StyleSlider(slider, new Color(0.15f, 0.2f, 0.26f), RuntimeUI.AccentColor, Color.white);
            if (volumeLabel != null)
            {
                VolumePercentLabel binder = slider.gameObject.AddComponent<VolumePercentLabel>();
                binder.Bind(slider, volumeLabel, "無人機音量");
            }
        }

        // 既有按鈕：統一樣式與文字
        Button helpButton = FindButton(panel, "[Button]鍵盤操控");
        if (helpButton != null)
        {
            ControlsHelp.StyleButton(helpButton, "<sprite name=\"help\"> 操作說明");
            SetRect(helpButton, new Vector2(0f, -52f), new Vector2(170f, 34f));
        }

        Button closeButton = FindButton(panel, "[Button]退出");
        if (closeButton != null)
        {
            ControlsHelp.StyleButton(closeButton, "關閉");
            SetRect(closeButton, new Vector2(110f, -100f), new Vector2(150f, 34f));
        }

        if (isGameplay)
        {
            Button menuButton = RuntimeUI.CreateButton("[Button]返回選單", panel, "返回選單", new Vector2(-110f, -100f), new Vector2(150f, 34f),
                () => SceneLoader.Load(LevelCatalog.DroneSelectScene), 22f);
            menuButton.transform.SetSiblingIndex(card.transform.GetSiblingIndex() + 1);
        }

        TextMeshProUGUI hint = RuntimeUI.CreateText("SettingsHint", panel,
            "<sprite name=\"v\"> 校準視角　<sprite name=\"h\"> 切換 HUD　<sprite name=\"r\"> 回到檢查點　<sprite name=\"z\"> 抓取 / 放下", 15f,
            new Vector2(0f, -138f), new Vector2(396f, 22f));
        hint.spriteAsset = ControlsHelp.ResolveSpriteAsset();
        hint.textWrappingMode = TextWrappingModes.NoWrap;
        hint.enableAutoSizing = true;
        hint.fontSizeMin = 10f;
        hint.fontSizeMax = 15f;
        hint.color = new Color(1f, 1f, 1f, 0.75f);

        // 舊的抓取說明已整合進操作說明面板
        Transform pickupInstructions = panel.Find("PickupInstructions");
        if (pickupInstructions != null) pickupInstructions.gameObject.SetActive(false);

        // 操作說明面板要畫在最上層
        Transform controls = panel.Find(ControlsPanelName);
        if (controls != null) controls.SetAsLastSibling();

        AddPopup(panel);
    }

    #endregion

    #region 關卡介紹面板

    private static void EnhanceIntroPanel(RectTransform panel)
    {
        MarkEnhanced(panel);
        foreach (TextMeshProUGUI text in panel.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (!text.name.StartsWith("[Text]")) continue;
            text.text = ControlsHelp.ObjectiveText();
            text.font = RuntimeUI.ResolveFont();
            text.textWrappingMode = TextWrappingModes.Normal;
            break;
        }
    }

    #endregion

    #region 主選單設置面板

    private static void EnhanceMainMenuSettings(RectTransform settingPanel)
    {
        MarkEnhanced(settingPanel);
        RectTransform msgBox = settingPanel.Find("MsgBox") as RectTransform;
        if (msgBox == null) return;

        Color textColor = new Color(0.1f, 0.1f, 0.12f);

        // 說明副標題
        TextMeshProUGUI subtitle = RuntimeUI.CreateText("SettingsSubtitle", msgBox, "調整音量、查看操作說明與製作名單", 26f,
            new Vector2(0f, 30f), new Vector2(800f, 36f));
        subtitle.color = new Color(0.35f, 0.35f, 0.4f);

        // 音量列
        TextMeshProUGUI label = RuntimeUI.CreateText("VolumeLabel", msgBox, "音量", 34f, new Vector2(-360f, -40f), new Vector2(140f, 50f));
        label.color = textColor;
        Slider slider = CreateSlider(msgBox, new Vector2(10f, -40f), new Vector2(520f, 36f));
        StyleSlider(slider, new Color(0.82f, 0.84f, 0.86f), new Color(0.1f, 0.7f, 0.55f), new Color(0.2f, 0.22f, 0.25f));
        TextMeshProUGUI percent = RuntimeUI.CreateText("VolumePercent", msgBox, "", 30f, new Vector2(360f, -40f), new Vector2(120f, 50f));
        percent.color = textColor;

        float saved = PlayerPrefs.GetFloat(VolumePrefsKey, AudioListener.volume);
        AudioListener.volume = saved;
        slider.SetValueWithoutNotify(saved);
        percent.text = $"{saved * 100f:0}%";
        slider.onValueChanged.AddListener(value =>
        {
            AudioListener.volume = value;
            PlayerPrefs.SetFloat(VolumePrefsKey, value);
            PlayerPrefs.Save();
            percent.text = $"{value * 100f:0}%";
        });

        // 操作說明按鈕：複製「製作名單」按鈕以維持一致外觀，放在左右對稱位置
        Transform creditsButton = msgBox.Find("[BTN]製作名單");
        if (creditsButton != null)
        {
            GameObject helpObject = Object.Instantiate(creditsButton.gameObject, msgBox);
            helpObject.name = "[BTN]操作說明";
            RectTransform helpRect = (RectTransform)helpObject.transform;
            RectTransform creditsRect = (RectTransform)creditsButton;
            helpRect.anchoredPosition = new Vector2(-creditsRect.anchoredPosition.x, creditsRect.anchoredPosition.y);

            Button helpButton = helpObject.GetComponent<Button>();
            for (int i = 0; i < helpButton.onClick.GetPersistentEventCount(); i++)
            {
                helpButton.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
            }

            foreach (Text legacy in helpObject.GetComponentsInChildren<Text>(true)) legacy.text = "操作說明";
            foreach (TextMeshProUGUI tmp in helpObject.GetComponentsInChildren<TextMeshProUGUI>(true)) tmp.text = "操作說明";

            Canvas canvas = settingPanel.GetComponentInParent<Canvas>(true);
            UIPopupPanel helpPopup = null;
            helpButton.onClick.AddListener(() =>
            {
                if (helpPopup == null && canvas != null)
                {
                    helpPopup = ControlsHelp.CreateStandalone(canvas.rootCanvas.transform, new Vector2(1150f, 680f));
                }
                helpPopup?.Show();
            });

            // 手把 / VR 搖桿導航：自動導航到不了「製作名單」(滑桿左右用來調音量)，改為明確指定
            //   音量滑桿
            //   操作說明  製作名單
            //   返回
            Selectable credits = creditsButton.GetComponent<Selectable>();
            Transform backTransform = msgBox.Find("[BTN]返回");
            Selectable back = backTransform != null ? backTransform.GetComponent<Selectable>() : null;
            SetNavigation(slider, null, helpButton, null, null);
            SetNavigation(helpButton, slider, back, null, credits);
            SetNavigation(credits, slider, back, helpButton, null);
            if (back != null) SetNavigation(back, helpButton, null, null, credits);
        }
    }

    private static void SetNavigation(Selectable selectable, Selectable up, Selectable down, Selectable left, Selectable right)
    {
        if (selectable == null) return;
        selectable.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnUp = up,
            selectOnDown = down,
            selectOnLeft = left,
            selectOnRight = right
        };
    }

    #endregion

    #region 主選單製作名單

    private struct CreditRow
    {
        public string Label;
        public string Name;
        public string Id;
    }

    /// <summary>
    /// 製作名單：讀取場景中原本的名單文字 (第一行為副標，「職稱：姓名 學號」，續行只寫姓名 學號)，
    /// 重新排成 標題 / 副標 / 分隔線 / 職稱｜姓名｜學號 三欄，之後只需修改場景中的文字即可。
    /// </summary>
    private static void EnhanceCredits(RectTransform panel)
    {
        MarkEnhanced(panel);
        RectTransform msgBox = panel.Find("MsgBox") as RectTransform;
        if (msgBox == null) return;

        TMP_Text source = null;
        foreach (TMP_Text text in msgBox.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.GetComponentInParent<Button>(true) != null) continue;
            source = text;
            break;
        }
        if (source == null) return;

        string subtitle = null;
        List<CreditRow> rows = ParseCredits(source.text, ref subtitle);
        if (rows.Count == 0) return;
        source.gameObject.SetActive(false);

        Color textColor = new Color(0.1f, 0.1f, 0.12f);
        Color accent = new Color(0.1f, 0.6f, 0.48f);
        Color muted = new Color(0.42f, 0.44f, 0.48f);

        TextMeshProUGUI title = RuntimeUI.CreateText("CreditsTitle", msgBox, "製作名單", 44f, new Vector2(0f, 158f), new Vector2(600f, 56f));
        title.color = textColor;
        title.fontStyle = FontStyles.Bold;
        if (!string.IsNullOrEmpty(subtitle))
        {
            TextMeshProUGUI sub = RuntimeUI.CreateText("CreditsSubtitle", msgBox, subtitle, 24f, new Vector2(0f, 118f), new Vector2(600f, 32f));
            sub.color = muted;
        }
        RuntimeUI.CreateImage("CreditsDivider", msgBox, new Vector2(0f, 94f), new Vector2(560f, 3f), accent);

        // 返回按鈕置中靠下，名單區塊置中於分隔線與返回按鈕之間
        float bottomLimit = -msgBox.rect.height * 0.5f + 20f;
        Transform back = msgBox.Find("[BTN]返回");
        if (back != null)
        {
            RectTransform backRect = (RectTransform)back;
            backRect.anchoredPosition = new Vector2(0f, 22f);
            Vector3[] corners = new Vector3[4];
            backRect.GetWorldCorners(corners);
            bottomLimit = msgBox.InverseTransformPoint(corners[1]).y + 12f;
        }
        const float listTop = 82f;

        int sections = 0;
        foreach (CreditRow row in rows) if (row.Label != null) sections++;
        const float sectionGap = 12f;
        float gaps = Mathf.Max(0, sections - 1) * sectionGap;
        float rowHeight = Mathf.Min(44f, (listTop - bottomLimit - gaps) / rows.Count);
        float blockHeight = rows.Count * rowHeight + gaps;
        float y = (listTop + bottomLimit) * 0.5f + blockHeight * 0.5f - rowHeight * 0.5f;
        float fontSize = Mathf.Min(30f, rowHeight * 0.68f);

        for (int i = 0; i < rows.Count; i++)
        {
            CreditRow row = rows[i];
            if (row.Label != null && i > 0) y -= sectionGap;

            if (row.Label != null)
            {
                TextMeshProUGUI label = RuntimeUI.CreateText($"CreditLabel_{i}", msgBox, row.Label, fontSize * 0.9f,
                    new Vector2(-185f, y), new Vector2(170f, rowHeight), TextAlignmentOptions.Right);
                label.color = accent;
                label.fontStyle = FontStyles.Bold;
            }

            TextMeshProUGUI nameText = RuntimeUI.CreateText($"CreditName_{i}", msgBox, row.Name, fontSize,
                new Vector2(10f, y), new Vector2(170f, rowHeight), TextAlignmentOptions.Left);
            nameText.color = textColor;

            if (!string.IsNullOrEmpty(row.Id))
            {
                TextMeshProUGUI idText = RuntimeUI.CreateText($"CreditId_{i}", msgBox, row.Id, fontSize * 0.85f,
                    new Vector2(185f, y), new Vector2(170f, rowHeight), TextAlignmentOptions.Left);
                idText.color = muted;
                idText.characterSpacing = 4f;
            }

            y -= rowHeight;
        }
    }

    private static List<CreditRow> ParseCredits(string content, ref string subtitle)
    {
        List<CreditRow> rows = new List<CreditRow>();
        bool hasLabel = false;
        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0) continue;

            int separator = line.IndexOfAny(new[] { '：', ':' });
            string label = null;
            if (separator >= 0)
            {
                label = line.Substring(0, separator).Trim();
                line = line.Substring(separator + 1).Trim();
                hasLabel = true;
            }
            else if (!hasLabel)
            {
                // 第一個職稱之前的文字當作副標 (例如「專題製作」)
                if (subtitle == null) subtitle = line;
                continue;
            }

            // 結尾是數字則視為學號
            string name = line;
            string id = string.Empty;
            int space = line.LastIndexOf(' ');
            if (space > 0 && long.TryParse(line.Substring(space + 1), out _))
            {
                name = line.Substring(0, space).Trim();
                id = line.Substring(space + 1);
            }
            rows.Add(new CreditRow { Label = label, Name = name, Id = id });
        }
        return rows;
    }

    #endregion

    #region 工具

    private static void AddPopup(RectTransform panel)
    {
        if (panel.GetComponent<UIPopupPanel>() == null)
        {
            panel.gameObject.AddComponent<UIPopupPanel>();
        }
    }

    private static void MarkEnhanced(RectTransform rect)
    {
        rect.gameObject.AddComponent<PanelEnhancedMarker>();
    }

    private static Button FindButton(Transform root, string objectName)
    {
        foreach (Button button in root.GetComponentsInChildren<Button>(true))
        {
            if (button.name == objectName) return button;
        }
        return null;
    }

    private static TextMeshProUGUI FindText(Transform root, string objectName)
    {
        foreach (TextMeshProUGUI text in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text.name == objectName) return text;
        }
        return null;
    }

    private static void SetRect(Component component, Vector2 position, Vector2 size)
    {
        RectTransform rect = (RectTransform)component.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static Slider CreateSlider(Transform parent, Vector2 position, Vector2 size)
    {
        RectTransform root = RuntimeUI.CreateCentered("VolumeSlider", parent, position, size);
        Image background = RuntimeUI.CreateImage("Background", root, Vector2.zero, size, Color.gray, RuntimeUI.RoundedSprite);
        RuntimeUI.Stretch(background.rectTransform);

        RectTransform fillArea = RuntimeUI.CreateRect("Fill Area", root);
        RuntimeUI.Stretch(fillArea);
        Image fill = RuntimeUI.CreateImage("Fill", fillArea, Vector2.zero, Vector2.zero, Color.green, RuntimeUI.RoundedSprite);
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(0f, 1f);
        fill.rectTransform.sizeDelta = Vector2.zero;

        RectTransform handleArea = RuntimeUI.CreateRect("Handle Slide Area", root);
        RuntimeUI.Stretch(handleArea);
        Image handle = RuntimeUI.CreateImage("Handle", handleArea, Vector2.zero, new Vector2(size.y * 1.2f, size.y * 1.2f), Color.white, RuntimeUI.RoundedSprite);
        handle.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        handle.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        handle.raycastTarget = true;

        Slider slider = root.gameObject.AddComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        background.raycastTarget = true;
        // 手把選中時的外框回饋 (執行時建立的滑桿可能晚於 UIButtonTweenFx 的自動安裝)
        slider.gameObject.AddComponent<UIButtonTweenFx>();
        return slider;
    }

    private static void StyleSlider(Slider slider, Color background, Color fill, Color handle)
    {
        foreach (Image image in slider.GetComponentsInChildren<Image>(true))
        {
            image.sprite = RuntimeUI.RoundedSprite;
            image.type = Image.Type.Sliced;
            if (slider.fillRect != null && image.transform == slider.fillRect) image.color = fill;
            else if (slider.handleRect != null && image.transform == slider.handleRect) image.color = handle;
            else if (image.name == "Background") image.color = background;
        }
    }

    #endregion
}

/// <summary>標記已處理過的面板，避免重複優化</summary>
public class PanelEnhancedMarker : MonoBehaviour
{
}

/// <summary>音量滑桿旁的文字即時顯示百分比</summary>
public class VolumePercentLabel : MonoBehaviour
{
    private Slider slider;
    private TextMeshProUGUI label;
    private string prefix;

    public void Bind(Slider target, TextMeshProUGUI text, string labelPrefix)
    {
        slider = target;
        label = text;
        prefix = labelPrefix;
        slider.onValueChanged.AddListener(UpdateLabel);
        UpdateLabel(slider.value);
    }

    private void OnEnable()
    {
        if (slider != null) UpdateLabel(slider.value);
    }

    private void OnDestroy()
    {
        if (slider != null) slider.onValueChanged.RemoveListener(UpdateLabel);
    }

    private void UpdateLabel(float value)
    {
        if (label != null) label.text = $"{prefix}　{value * 100f:0}%";
    }
}

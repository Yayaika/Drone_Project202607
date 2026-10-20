using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 進入關卡前的「遊戲模式 → 關卡」選擇面板 (執行時建立，不需修改場景)。
/// 第一步選擇競速或搬運，第二步選擇支援該模式的關卡並顯示最佳紀錄。
/// </summary>
public class ModeSelectPanel : MonoBehaviour
{
    private const float DesignWidth = 640f;
    private const float DesignHeight = 380f;
    private const float CardWidth = 196f;

    private CanvasGroup group;
    private RectTransform modePage;
    private RectTransform levelPage;
    private TextMeshProUGUI levelTitle;
    private RectTransform levelList;
    private GameModeType pendingMode;
    private InputAction backAction;
    private System.Action onClosed;

    private static ModeSelectPanel current;

    /// <summary>開啟面板。parentCanvas 為 null 時建立螢幕覆蓋 Canvas。</summary>
    public static ModeSelectPanel Open(Canvas parentCanvas, TMP_FontAsset font = null, System.Action closed = null)
    {
        if (current != null) return current;

        RuntimeUI.SetPreferredFont(font);

        Transform parent;
        Vector2 available;
        if (parentCanvas != null)
        {
            Canvas root = parentCanvas.rootCanvas;
            parent = root.transform;
            available = ((RectTransform)root.transform).rect.size;
        }
        else
        {
            GameObject canvasObject = new GameObject("ModeSelectCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            parent = canvasObject.transform;
            available = new Vector2(1280f, 720f);
        }

        // 遮罩背景 (點擊外部不會穿透到下層 UI)
        RectTransform overlay = RuntimeUI.CreateRect("ModeSelectOverlay", parent);
        RuntimeUI.Stretch(overlay);
        overlay.SetAsLastSibling();
        Image dim = overlay.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        ModeSelectPanel panel = overlay.gameObject.AddComponent<ModeSelectPanel>();
        panel.onClosed = closed;
        float scale = Mathf.Min(available.x / (DesignWidth + 40f), available.y / (DesignHeight + 40f));
        panel.Build(overlay, Mathf.Max(0.1f, scale));
        current = panel;
        return panel;
    }

    private void Build(RectTransform overlay, float scale)
    {
        group = overlay.gameObject.AddComponent<CanvasGroup>();

        RectTransform frame = RuntimeUI.CreateCentered("Frame", overlay, Vector2.zero, new Vector2(DesignWidth, DesignHeight));
        frame.localScale = Vector3.one * scale;
        Image frameImage = frame.gameObject.AddComponent<Image>();
        frameImage.sprite = RuntimeUI.RoundedSprite;
        frameImage.type = Image.Type.Sliced;
        frameImage.color = RuntimeUI.PanelColor;
        Outline outline = frame.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(RuntimeUI.AccentColor.r, RuntimeUI.AccentColor.g, RuntimeUI.AccentColor.b, 0.5f);
        outline.effectDistance = new Vector2(2f, -2f);

        BuildModePage(frame);
        BuildLevelPage(frame);
        ShowPage(modePage);

        UITween.ShowPanel(group, 0.35f, 1f);
        frame.localScale = Vector3.one * scale * 0.85f;
        frame.DOScale(scale, 0.35f).SetEase(Ease.OutBack).SetUpdate(true);
        GameAudio.Play(GameSfx.PanelOpen);

        backAction = new InputAction("ModeSelectBack");
        backAction.AddBinding("<Keyboard>/escape");
        backAction.AddBinding("<Keyboard>/backspace");
        backAction.AddBinding("<Gamepad>/buttonEast");
        backAction.Enable();
    }

    private void BuildModePage(RectTransform frame)
    {
        modePage = RuntimeUI.CreateCentered("ModePage", frame, Vector2.zero, new Vector2(DesignWidth, DesignHeight));
        modePage.gameObject.AddComponent<CanvasGroup>();

        TextMeshProUGUI title = RuntimeUI.CreateText("Title", modePage, "選擇遊戲模式", 30f, new Vector2(0f, 150f), new Vector2(500f, 40f));
        title.color = RuntimeUI.AccentColor;
        title.fontStyle = FontStyles.Bold;

        RectTransform cards = RuntimeUI.CreateCentered("Cards", modePage, new Vector2(0f, 0f), new Vector2(DesignWidth, 220f));
        CreateModeCard(cards, GameModeType.Race, new Vector2(-206f, 0f),
            "競速  RACE", "依序穿越檢查圈\n挑戰最快時間與金牌", new Color(1f, 0.75f, 0.2f));
        CreateModeCard(cards, GameModeType.Delivery, new Vector2(0f, 0f),
            "搬運  DELIVERY", "抓取貨物送到指定地點\n碰撞會造成貨損扣分", new Color(0.35f, 0.85f, 1f));
        CreateModeCard(cards, GameModeType.Strike, new Vector2(206f, 0f),
            "打擊  STRIKE", "投彈摧毀目標\n遮蔽下的目標要撞擊", new Color(1f, 0.4f, 0.35f));

        RuntimeUI.CreateButton("CloseButton", modePage, "返回", new Vector2(0f, -155f), new Vector2(140f, 36f), Close, 18f);
    }

    private void CreateModeCard(RectTransform parent, GameModeType mode, Vector2 position, string title, string description, Color accent)
    {
        Button card = RuntimeUI.CreateButton($"Card_{mode}", parent, string.Empty, position, new Vector2(CardWidth, 210f), () => ShowLevels(mode), 18f);
        Outline outline = card.gameObject.AddComponent<Outline>();
        outline.effectColor = accent;
        outline.effectDistance = new Vector2(2f, -2f);

        TextMeshProUGUI titleText = RuntimeUI.CreateText("CardTitle", card.transform, title, 22f, new Vector2(0f, 55f), new Vector2(CardWidth - 16f, 36f));
        titleText.color = accent;
        titleText.fontStyle = FontStyles.Bold;

        TextMeshProUGUI descText = RuntimeUI.CreateText("CardDesc", card.transform, description, 15f, new Vector2(0f, -5f), new Vector2(CardWidth - 16f, 60f));
        descText.textWrappingMode = TextWrappingModes.Normal;
        descText.color = new Color(1f, 1f, 1f, 0.85f);

        int available = 0;
        foreach (LevelDefinition level in LevelCatalog.LevelsFor(mode))
        {
            if (LevelCatalog.IsSceneInBuild(level.SceneName)) available++;
        }
        // 打擊模式只開放給有投彈功能的炸彈無人機
        bool locked = mode == GameModeType.Strike && !LevelCatalog.SelectedDroneCanBomb;
        if (locked) card.interactable = false;
        string countLine = locked ? "需要炸彈無人機" : $"{available} 個關卡可遊玩";
        TextMeshProUGUI countText = RuntimeUI.CreateText("CardCount", card.transform, countLine, 14f, new Vector2(0f, -70f), new Vector2(CardWidth - 16f, 22f));
        countText.color = available > 0 && !locked ? RuntimeUI.AccentColor : RuntimeUI.WarningColor;
    }

    private void BuildLevelPage(RectTransform frame)
    {
        levelPage = RuntimeUI.CreateCentered("LevelPage", frame, Vector2.zero, new Vector2(DesignWidth, DesignHeight));
        levelPage.gameObject.AddComponent<CanvasGroup>();

        levelTitle = RuntimeUI.CreateText("Title", levelPage, "", 28f, new Vector2(0f, 155f), new Vector2(560f, 36f));
        levelTitle.color = RuntimeUI.AccentColor;
        levelTitle.fontStyle = FontStyles.Bold;

        levelList = RuntimeUI.CreateCentered("LevelList", levelPage, new Vector2(0f, 10f), new Vector2(DesignWidth - 40f, 260f));
        RuntimeUI.CreateButton("BackButton", levelPage, "上一步", new Vector2(0f, -160f), new Vector2(140f, 34f), () => ShowPage(modePage), 18f);
    }

    private void ShowLevels(GameModeType mode)
    {
        pendingMode = mode;
        levelTitle.text = $"選擇關卡 — {LevelCatalog.ModeDisplayName(mode)}";

        for (int i = levelList.childCount - 1; i >= 0; i--)
        {
            Destroy(levelList.GetChild(i).gameObject);
        }

        List<LevelDefinition> levels = LevelCatalog.LevelsFor(mode);
        float rowHeight = Mathf.Min(62f, 260f / Mathf.Max(1, levels.Count));
        float startY = (levels.Count - 1) * rowHeight * 0.5f;
        Button firstAvailable = null;

        for (int i = 0; i < levels.Count; i++)
        {
            LevelDefinition level = levels[i];
            bool inBuild = LevelCatalog.IsSceneInBuild(level.SceneName);
            Vector2 position = new Vector2(0f, startY - i * rowHeight);

            Button row = RuntimeUI.CreateButton($"Level_{level.Id}", levelList, string.Empty, position,
                new Vector2(DesignWidth - 60f, rowHeight - 8f), () => LevelCatalog.Load(level, mode), 18f);
            row.interactable = inBuild;
            if (inBuild && firstAvailable == null) firstAvailable = row;

            TextMeshProUGUI nameText = RuntimeUI.CreateText("Name", row.transform, level.DisplayName, 20f,
                new Vector2(-150f, 8f), new Vector2(260f, 24f), TextAlignmentOptions.Left);
            nameText.fontStyle = FontStyles.Bold;

            string description = inBuild ? level.Description : "（場景尚未加入 Build Settings）";
            TextMeshProUGUI descText = RuntimeUI.CreateText("Desc", row.transform, description, 13f,
                new Vector2(-150f, -12f), new Vector2(260f, 18f), TextAlignmentOptions.Left);
            descText.color = inBuild ? new Color(1f, 1f, 1f, 0.7f) : RuntimeUI.WarningColor;

            TextMeshProUGUI bestText = RuntimeUI.CreateText("Best", row.transform, BestRecordText(level, mode), 15f,
                new Vector2(170f, 0f), new Vector2(220f, 24f), TextAlignmentOptions.Right);
            bestText.color = RuntimeUI.AccentColor;
        }

        ShowPage(levelPage);
        UITween.StaggerIn(levelList, 0.06f, -18f);
        if (EventSystem.current != null && firstAvailable != null)
        {
            EventSystem.current.SetSelectedGameObject(firstAvailable.gameObject);
        }
    }

    private static string BestRecordText(LevelDefinition level, GameModeType mode)
    {
        List<LeaderboardEntry> entries = Leaderboard.Get(level.LeaderboardKey(mode));
        if (entries.Count == 0) return "尚無紀錄";
        LeaderboardEntry best = entries[0];
        return mode == GameModeType.Race
            ? $"最佳 {RuntimeUI.FormatTime(best.time)}"
            : $"最高 {best.score} 分";
    }

    private void ShowPage(RectTransform page)
    {
        bool showMode = page == modePage;
        SetPageVisible(modePage, showMode);
        SetPageVisible(levelPage, !showMode);

        if (showMode)
        {
            UITween.StaggerIn(modePage.Find("Cards"), 0.08f, -20f);
            Transform firstCard = modePage.Find("Cards")?.GetChild(0);
            if (EventSystem.current != null && firstCard != null)
            {
                EventSystem.current.SetSelectedGameObject(firstCard.gameObject);
            }
        }
    }

    private static void SetPageVisible(RectTransform page, bool visible)
    {
        CanvasGroup pageGroup = page.GetComponent<CanvasGroup>();
        pageGroup.DOKill();
        page.gameObject.SetActive(visible);
        if (visible)
        {
            pageGroup.alpha = 0f;
            pageGroup.DOFade(1f, 0.2f).SetUpdate(true);
        }
    }

    private void Update()
    {
        if (backAction == null || !backAction.WasPressedThisFrame()) return;

        if (levelPage.gameObject.activeSelf) ShowPage(modePage);
        else Close();
    }

    public void Close()
    {
        backAction?.Disable();
        UITween.HidePanel(group).OnComplete(() =>
        {
            if (this != null) Destroy(gameObject);
        });
        onClosed?.Invoke();
    }

    private void OnDestroy()
    {
        backAction?.Dispose();
        if (current == this) current = null;
    }
}

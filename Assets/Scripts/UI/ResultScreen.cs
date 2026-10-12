using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 關卡結算畫面：成績、獎牌、明細、排行榜前 5 名與「重新挑戰 / 下一關 / 返回選單」。
/// 優先建立在無人機 HUD Canvas (World Space，VR 也看得到)，沒有時建立螢幕覆蓋 Canvas。
/// 快捷鍵：Enter / A = 下一關、Space / X = 重新挑戰、M / B = 返回選單。
/// </summary>
public class ResultScreen : MonoBehaviour
{
    private const float PanelWidth = 300f;
    private const float PanelHeight = 236f;

    private Action onRetry;
    private Action onNext;
    private Action onMenu;
    private bool hasNext;
    private bool actionTaken;
    private InputAction nextAction;
    private InputAction retryAction;
    private InputAction menuAction;

    public static ResultScreen Show(Transform hudCanvas, LevelResult result, LevelDefinition level,
        Action retry, Action next, Action menu)
    {
        Transform parent = hudCanvas;
        // 無人機 HUD Canvas 的既有文字約 20pt，結算面板以小字設計後整體放大
        float scale = 1.6f;
        if (parent == null)
        {
            GameObject canvasObject = new GameObject("ResultCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            parent = canvasObject.transform;
            scale = 3f;
        }

        RectTransform root = RuntimeUI.CreateCentered("ResultScreen", parent, Vector2.zero, new Vector2(PanelWidth, PanelHeight));
        root.localScale = Vector3.one * scale;
        root.SetAsLastSibling();
        ResultScreen screen = root.gameObject.AddComponent<ResultScreen>();
        screen.Build(root, result, level, retry, next, menu);
        return screen;
    }

    private void Build(RectTransform root, LevelResult result, LevelDefinition level, Action retry, Action next, Action menu)
    {
        onRetry = retry;
        onNext = next;
        onMenu = menu;
        hasNext = GameManager.Instance != null && GameManager.Instance.HasNextLevel();

        CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();
        Image background = root.gameObject.AddComponent<Image>();
        background.sprite = RuntimeUI.RoundedSprite;
        background.type = Image.Type.Sliced;
        background.color = RuntimeUI.PanelColor;
        background.raycastTarget = true;

        // 外框 (獎牌顏色)
        Color borderColor = MedalColor(result);
        Outline border = root.gameObject.AddComponent<Outline>();
        border.effectColor = new Color(borderColor.r, borderColor.g, borderColor.b, 0.7f);
        border.effectDistance = new Vector2(1.5f, -1.5f);

        RectTransform content = RuntimeUI.CreateCentered("Content", root, Vector2.zero, new Vector2(PanelWidth, PanelHeight));

        float y = PanelHeight * 0.5f - 18f;
        TextMeshProUGUI title = RuntimeUI.CreateText("Title", content, result.Title, 18f, new Vector2(0f, y), new Vector2(PanelWidth - 20f, 24f));
        title.color = result.Success ? MedalColor(result) : RuntimeUI.WarningColor;
        title.fontStyle = FontStyles.Bold;

        y -= 18f;
        string levelName = level != null ? level.DisplayName : UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        TextMeshProUGUI subtitle = RuntimeUI.CreateText("Subtitle", content, $"{LevelCatalog.ModeDisplayName(result.Mode)}模式　{levelName}", 10f, new Vector2(0f, y), new Vector2(PanelWidth - 20f, 14f));
        subtitle.color = new Color(1f, 1f, 1f, 0.7f);

        // 主要成績 (數字由 0 跳到最終值)
        y -= 26f;
        bool isRace = result.Mode == GameModeType.Race;
        TextMeshProUGUI mainValue = RuntimeUI.CreateText("MainValue", content, "", 26f, new Vector2(0f, y), new Vector2(PanelWidth - 20f, 32f));
        mainValue.color = Color.white;
        mainValue.fontStyle = FontStyles.Bold;
        if (isRace)
        {
            DOVirtual.Float(0f, result.Time, 0.9f, value => mainValue.text = RuntimeUI.FormatTime(value)).SetEase(Ease.OutCubic).SetUpdate(true).SetTarget(this);
        }
        else
        {
            DOVirtual.Int(0, result.Score, 0.9f, value => mainValue.text = $"{value} 分").SetEase(Ease.OutCubic).SetUpdate(true).SetTarget(this);
        }

        y -= 22f;
        string medalLine = MedalLabel(result.Medal);
        string rankLine = !result.Success ? "未完成，不列入排行"
            : result.Rank == 1 ? "新紀錄！排行第 1 名"
            : result.Rank > 0 ? $"排行第 {result.Rank} 名"
            : "未進入前 10 名";
        if (!isRace) medalLine = $"用時 {RuntimeUI.FormatTime(result.Time)}";
        TextMeshProUGUI rank = RuntimeUI.CreateText("Rank", content, string.IsNullOrEmpty(medalLine) ? rankLine : $"{medalLine}　|　{rankLine}", 11f, new Vector2(0f, y), new Vector2(PanelWidth - 20f, 14f));
        rank.color = result.Rank == 1 ? new Color(1f, 0.85f, 0.2f) : RuntimeUI.AccentColor;

        // 明細
        y -= 8f;
        foreach (string detail in result.Details)
        {
            y -= 13f;
            TextMeshProUGUI line = RuntimeUI.CreateText("Detail", content, detail, 9.5f, new Vector2(-60f, y), new Vector2(150f, 12f), TextAlignmentOptions.Left);
            line.color = new Color(1f, 1f, 1f, 0.85f);
        }

        // 排行榜 (右側)
        if (level != null)
        {
            float boardY = PanelHeight * 0.5f - 98f;
            TextMeshProUGUI boardTitle = RuntimeUI.CreateText("BoardTitle", content, "排行榜 TOP 5", 10f, new Vector2(80f, boardY), new Vector2(130f, 13f), TextAlignmentOptions.Left);
            boardTitle.color = RuntimeUI.AccentColor;

            List<LeaderboardEntry> entries = Leaderboard.Get(level.LeaderboardKey(result.Mode));
            for (int i = 0; i < Mathf.Min(5, entries.Count); i++)
            {
                LeaderboardEntry entry = entries[i];
                bool mine = result.SubmittedEntry != null && entry.date == result.SubmittedEntry.date
                            && Mathf.Approximately(entry.time, result.SubmittedEntry.time) && entry.score == result.SubmittedEntry.score;
                string value = isRace ? RuntimeUI.FormatTime(entry.time) : $"{entry.score}";
                TextMeshProUGUI row = RuntimeUI.CreateText($"Row{i}", content, $"{i + 1}. {value}", 9.5f,
                    new Vector2(80f, boardY - 14f * (i + 1)), new Vector2(130f, 12f), TextAlignmentOptions.Left);
                row.color = mine ? new Color(1f, 0.85f, 0.2f) : Color.white;
            }
            if (entries.Count == 0)
            {
                RuntimeUI.CreateText("Empty", content, "尚無紀錄", 9.5f, new Vector2(80f, boardY - 14f), new Vector2(130f, 12f), TextAlignmentOptions.Left);
            }
        }

        // 按鈕列
        float buttonY = -PanelHeight * 0.5f + 22f;
        RectTransform buttonRow = RuntimeUI.CreateCentered("Buttons", root, new Vector2(0f, buttonY), new Vector2(PanelWidth, 28f));
        Button retryButton = RuntimeUI.CreateButton("RetryButton", buttonRow, "重新挑戰 [Space]", new Vector2(-96f, 0f), new Vector2(88f, 24f), () => Choose(onRetry), 9.5f);
        Button nextButton = RuntimeUI.CreateButton("NextButton", buttonRow, "下一關 [Enter]", new Vector2(0f, 0f), new Vector2(88f, 24f), () => Choose(onNext), 9.5f);
        RuntimeUI.CreateButton("MenuButton", buttonRow, "返回選單 [M]", new Vector2(96f, 0f), new Vector2(88f, 24f), () => Choose(onMenu), 9.5f);
        nextButton.interactable = hasNext;
        if (!hasNext)
        {
            nextButton.GetComponentInChildren<TextMeshProUGUI>().text = "已是最後一關";
        }

        UITween.ShowPanel(group, 0.4f, 0.7f);
        UITween.StaggerIn(content, 0.04f, -12f);
        UITween.StaggerIn(buttonRow, 0.08f, -10f);
        GameAudio.Play(GameSfx.PanelOpen);

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject((hasNext ? nextButton : retryButton).gameObject);
        }

        CreateShortcuts();
    }

    private void CreateShortcuts()
    {
        nextAction = new InputAction("ResultNext");
        nextAction.AddBinding("<Keyboard>/enter");
        nextAction.AddBinding("<Keyboard>/numpadEnter");
        nextAction.AddBinding("<Gamepad>/buttonSouth");

        retryAction = new InputAction("ResultRetry");
        retryAction.AddBinding("<Keyboard>/space");
        retryAction.AddBinding("<Gamepad>/buttonWest");

        menuAction = new InputAction("ResultMenu");
        menuAction.AddBinding("<Keyboard>/m");
        menuAction.AddBinding("<Gamepad>/buttonEast");

        // 延遲啟用，避免結算瞬間仍按著的按鍵 (例如 A 放貨) 直接觸發
        DOVirtual.DelayedCall(0.8f, () =>
        {
            if (this == null) return;
            nextAction.Enable();
            retryAction.Enable();
            menuAction.Enable();
        }).SetUpdate(true).SetTarget(this);
    }

    private void Update()
    {
        if (actionTaken || nextAction == null) return;

        if (nextAction.WasPressedThisFrame() && hasNext) Choose(onNext);
        else if (retryAction.WasPressedThisFrame()) Choose(onRetry);
        else if (menuAction.WasPressedThisFrame()) Choose(onMenu);
    }

    private void Choose(Action action)
    {
        if (actionTaken) return;
        actionTaken = true;
        GameAudio.Play(GameSfx.UIClick);
        action?.Invoke();
    }

    private void OnDestroy()
    {
        DOTween.Kill(this);
        nextAction?.Dispose();
        retryAction?.Dispose();
        menuAction?.Dispose();
    }

    private static Color MedalColor(LevelResult result)
    {
        switch (result.Medal)
        {
            case Medal.Gold: return new Color(1f, 0.82f, 0.25f);
            case Medal.Silver: return new Color(0.8f, 0.85f, 0.9f);
            case Medal.Bronze: return new Color(0.85f, 0.55f, 0.3f);
            default: return result.Success ? RuntimeUI.AccentColor : RuntimeUI.WarningColor;
        }
    }

    private static string MedalLabel(Medal medal)
    {
        switch (medal)
        {
            case Medal.Gold: return "金牌 GOLD";
            case Medal.Silver: return "銀牌 SILVER";
            case Medal.Bronze: return "銅牌 BRONZE";
            default: return string.Empty;
        }
    }
}

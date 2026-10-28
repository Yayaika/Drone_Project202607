using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.UI;

/// <summary>
/// 開始選單按下「開始」後播放的開場動畫 (Timeline)：鏡頭從選單升空繞行城市，兩名角色對話說明遊戲目的。
/// - 字幕由 Timeline 的 <see cref="DialogueTrack"/> 驅動，可在 Timeline 視窗調整台詞與時間
/// - 右上角「跳過」按鈕可用滑鼠或 VR 手把射線點擊；鍵盤 Space / Enter / Esc、手把 B / Start、VR 手把 B / Y 也可跳過
/// - 字幕介面為跟隨頭部的世界空間 Canvas (手把射線才點得到)，每句對話彈出時播放音效
/// - 播完或跳過後載入下一個場景 (選擇無人機)
/// - 開始選單：手把 X 鍵 (Xbox X / PS □) 直接開始，並預設選取開始按鈕讓手把可以操作 UI
/// 由選單「無人機遊戲/13. 建立開場動畫」設定，開始按鈕改呼叫 <see cref="Play"/>。
/// </summary>
public class IntroCutscene : MonoBehaviour
{
    private const string SeenKey = "IntroCutsceneSeen";

    [SerializeField] private PlayableDirector director;
    [SerializeField] private string nextScene = "PlayerSelectSettings";
    [Tooltip("播放時隱藏的物件 (選單 Canvas)")]
    [SerializeField] private GameObject[] hideWhilePlaying;
    [Tooltip("只在第一次按開始時播放，之後直接進入下一個場景")]
    [SerializeField] private bool onlyFirstTime;
    [Tooltip("開始後這段時間內不接受跳過，避免按「開始」的同一下就跳過")]
    [SerializeField] private float skipLockTime = 0.6f;
    [Tooltip("字幕打字速度 (字/秒)")]
    [SerializeField] private float charactersPerSecond = 18f;
    [Tooltip("字幕介面在相機前方的距離 (m)")]
    [SerializeField] private float uiDistance = 1.2f;
    [Tooltip("字幕介面佔視野的比例 (VR 頭顯邊緣看不清楚，預設留 25% 邊界)")]
    [Range(0.4f, 1f)]
    [SerializeField] private float uiViewportFill = 0.75f;
    [Tooltip("每句對話彈出時的音效；留空則使用 GameAudio 的 DialoguePop")]
    [SerializeField] private AudioClip dialoguePopSound;
    [SerializeField] private string locationCaption = "新港市　上午 7:42";
    [SerializeField] private Color leftSpeakerColor = new Color(0.2f, 1f, 0.75f, 1f);
    [SerializeField] private Color rightSpeakerColor = new Color(1f, 0.62f, 0.22f, 1f);

    private Canvas canvas;
    private CanvasGroup rootGroup;
    private CanvasGroup dialogueGroup;
    private CanvasGroup captionGroup;
    private Image portrait;
    private TextMeshProUGUI portraitText;
    private RectTransform nameBadge;
    private Image nameBadgeImage;
    private TextMeshProUGUI speakerText;
    private TextMeshProUGUI lineText;
    private InputAction skipAction;
    private InputAction startAction;
    private UIPanelCtrl[] menuPanels;
    private bool playing;
    private bool finished;
    private bool lineVisible;
    private float startTime;
    private RectTransform dialogueBox;
    private float popTime = -1f;

    public bool IsPlaying => playing;

    private void Start()
    {
        // 開始選單沒有預設選取的按鈕時手把無法操作 UI：提供 X 鍵直接開始，並預選開始按鈕
        startAction = new InputAction("StartGame", InputActionType.Button, "<Gamepad>/buttonWest");
        startAction.Enable();
        menuPanels = FindObjectsByType<UIPanelCtrl>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        Button startButton = FindStartButton();
        if (startButton == null) return;

        TextMeshProUGUI icon = RuntimeUI.CreateText("GamepadHint", startButton.transform, "<sprite name=\"xx\">", 44f,
            new Vector2(-135f, 0f), new Vector2(60f, 60f));
        icon.spriteAsset = ControlsHelp.ResolveSpriteAsset();
        icon.raycastTarget = false;

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null && eventSystem.currentSelectedGameObject == null)
        {
            eventSystem.SetSelectedGameObject(startButton.gameObject);
        }
    }

    /// <summary>開始按鈕：OnClick 設定為呼叫本元件 Play 的按鈕</summary>
    private Button FindStartButton()
    {
        foreach (Button button in FindObjectsByType<Button>(FindObjectsSortMode.None))
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentTarget(i) == this && button.onClick.GetPersistentMethodName(i) == nameof(Play)) return button;
            }
        }
        return null;
    }

    /// <summary>設置 / 退出 / 製作名單等面板開著時，X 鍵不開始遊戲</summary>
    private bool IsMenuPanelOpen()
    {
        foreach (UIPanelCtrl panel in menuPanels)
        {
            if (panel != null && panel.IsOpen) return true;
        }
        foreach (UIPopupPanel popup in FindObjectsByType<UIPopupPanel>(FindObjectsSortMode.None))
        {
            if (popup.IsOpen) return true;
        }
        return false;
    }

    /// <summary>開始按鈕呼叫：播放開場動畫 (沒有 Timeline 或設定為只播一次且已看過時直接進入下一個場景)</summary>
    public void Play()
    {
        if (playing || finished) return;
        bool seen = PlayerPrefs.GetInt(SeenKey, 0) == 1;
        if (director == null || director.playableAsset == null || (onlyFirstTime && seen))
        {
            finished = true;
            SceneLoader.Load(nextScene);
            return;
        }

        PlayerPrefs.SetInt(SeenKey, 1);
        foreach (GameObject target in hideWhilePlaying)
        {
            if (target != null) target.SetActive(false);
        }

        BuildUi();
        skipAction = new InputAction("SkipCutscene", InputActionType.Button);
        skipAction.AddBinding("<Keyboard>/space");
        skipAction.AddBinding("<Keyboard>/enter");
        skipAction.AddBinding("<Keyboard>/escape");
        skipAction.AddBinding("<Gamepad>/buttonEast");
        skipAction.AddBinding("<Gamepad>/start");
        skipAction.AddBinding("<XRController>{RightHand}/secondaryButton");
        skipAction.AddBinding("<XRController>{LeftHand}/secondaryButton");
        skipAction.Enable();

        playing = true;
        startTime = Time.unscaledTime;
        director.stopped += OnDirectorStopped;
        director.time = 0;
        director.Play();
    }

    public void Skip()
    {
        if (!playing || Time.unscaledTime - startTime < skipLockTime) return;
        Finish();
    }

    private void OnDirectorStopped(PlayableDirector stopped)
    {
        Finish();
    }

    private void Finish()
    {
        if (finished) return;
        finished = true;
        playing = false;
        director.stopped -= OnDirectorStopped;
        // 暫停而非停止：停止會讓鏡頭跳回選單位置，在 Loading 淡入前被看到
        if (director.state == PlayState.Playing) director.Pause();
        skipAction?.Disable();
        if (dialogueGroup != null) dialogueGroup.alpha = 0f;
        SceneLoader.Load(nextScene);
    }

    private void Update()
    {
        if (!playing)
        {
            if (!finished && startAction != null && startAction.WasPressedThisFrame() && !IsMenuPanelOpen()) Play();
            return;
        }
        AttachToCamera();

        float elapsed = Time.unscaledTime - startTime;
        rootGroup.alpha = Mathf.Clamp01(elapsed / 0.6f);
        captionGroup.alpha = Mathf.Clamp01(elapsed / 0.8f) * Mathf.Clamp01((4.5f - elapsed) / 0.8f);
        float target = lineVisible ? 1f : 0f;
        dialogueGroup.alpha = Mathf.MoveTowards(dialogueGroup.alpha, target, Time.unscaledDeltaTime * 5f);

        // 換句時對話框彈出：0.9 → 1.04 → 1
        float pop = popTime < 0f ? 1f : Mathf.Clamp01((Time.unscaledTime - popTime) / 0.22f);
        float scale = pop < 0.6f ? Mathf.Lerp(0.9f, 1.04f, pop / 0.6f) : Mathf.Lerp(1.04f, 1f, (pop - 0.6f) / 0.4f);
        dialogueBox.localScale = Vector3.one * scale;

        if (skipAction != null && skipAction.WasPressedThisFrame()) Skip();
    }

    private void OnDestroy()
    {
        startAction?.Dispose();
        skipAction?.Dispose();
        if (director != null) director.stopped -= OnDirectorStopped;
    }

    #region 字幕 (由 DialogueTrack 呼叫)

    public void ShowLine(string speaker, string portraitLetter, string line, bool rightSide, float localTime)
    {
        if (!Application.isPlaying || !playing || lineText == null) return;
        lineVisible = true;

        Color color = rightSide ? rightSpeakerColor : leftSpeakerColor;
        float side = rightSide ? 1f : -1f;
        if (speakerText.text != speaker)
        {
            speakerText.text = speaker;
            portraitText.text = portraitLetter ?? string.Empty;
        }
        portrait.color = new Color(color.r * 0.35f, color.g * 0.35f, color.b * 0.35f, 0.95f);
        portrait.rectTransform.anchoredPosition = new Vector2(side * PortraitX, 0f);
        portraitText.color = color;
        nameBadge.anchoredPosition = new Vector2(side * NameBadgeX, 132f);
        nameBadgeImage.color = color;
        lineText.rectTransform.anchoredPosition = new Vector2(-side * LineOffsetX, -8f);

        if (lineText.text != line)
        {
            lineText.text = line;
            popTime = Time.unscaledTime;
            if (dialoguePopSound != null) GameAudio.PlayClip(dialoguePopSound);
            else GameAudio.Play(GameSfx.DialoguePop);
        }
        lineText.maxVisibleCharacters = Mathf.FloorToInt(localTime * charactersPerSecond);
    }

    public void HideLine()
    {
        lineVisible = false;
    }

    #endregion

    #region 介面

    private void BuildUi()
    {
        if (canvas != null) return;

        GameObject host = new GameObject("IntroCutsceneUI", typeof(RectTransform));
        host.transform.SetParent(transform, false);
        canvas = host.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;
        ((RectTransform)host.transform).sizeDelta = new Vector2(1920f, 1080f);
        host.AddComponent<GraphicRaycaster>();
        AddVrRaycaster(host);
        AttachToCamera();

        RectTransform root = RuntimeUI.CreateRect("Root", host.transform);
        RuntimeUI.Stretch(root);
        rootGroup = root.gameObject.AddComponent<CanvasGroup>();
        rootGroup.alpha = 0f;

        // 電影黑邊
        Color bar = new Color(0f, 0f, 0f, 0.85f);
        Image top = RuntimeUI.CreateImage("LetterboxTop", root, Vector2.zero, Vector2.zero, bar);
        Anchor(top.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 96f));
        Image bottom = RuntimeUI.CreateImage("LetterboxBottom", root, Vector2.zero, Vector2.zero, bar);
        Anchor(bottom.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 96f));

        // 地點字卡
        RectTransform caption = RuntimeUI.CreateRect("Caption", root);
        Anchor(caption, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(80f, -130f), new Vector2(700f, 70f));
        captionGroup = caption.gameObject.AddComponent<CanvasGroup>();
        captionGroup.alpha = 0f;
        RuntimeUI.CreateImage("Accent", caption, new Vector2(-344f, 0f), new Vector2(8f, 56f), leftSpeakerColor);
        TextMeshProUGUI captionText = RuntimeUI.CreateText("Text", caption, locationCaption, 40f, new Vector2(16f, 0f),
            new Vector2(660f, 60f), TextAlignmentOptions.Left);
        captionText.fontStyle = FontStyles.Bold;

        BuildSkipButton(root);
        BuildDialogueBox(root);
    }

    private void BuildSkipButton(RectTransform root)
    {
        RectTransform corner = RuntimeUI.CreateRect("Skip", root);
        Anchor(corner, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-48f, -16f), new Vector2(260f, 140f));

        Button button = RuntimeUI.CreateButton("SkipButton", corner, "跳過", new Vector2(0f, 34f), new Vector2(220f, 64f), Skip, 32f);
        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
        label.rectTransform.offsetMax = new Vector2(-50f, 0f);
        for (int i = 0; i < 2; i++)
        {
            Image arrow = RuntimeUI.CreateImage("Arrow", button.transform, new Vector2(50f + i * 18f, 0f), new Vector2(22f, 22f),
                RuntimeUI.AccentColor, RuntimeUI.TriangleSprite);
            arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);
        }

        TextMeshProUGUI hint = RuntimeUI.CreateText("Hint", corner, "射線點擊 / Space / 手把 B 鍵", 20f, new Vector2(0f, -22f), new Vector2(300f, 30f));
        hint.color = new Color(1f, 1f, 1f, 0.7f);
    }

    private void BuildDialogueBox(RectTransform root)
    {
        RectTransform box = RuntimeUI.CreateRect("Dialogue", root);
        dialogueBox = box;
        // 對話框往畫面中央靠攏 (錨點在中央，略低於視線)：VR 頭顯與寬螢幕邊緣都較不需要轉頭/移動視線
        Anchor(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, DialogueCenterY), new Vector2(DialogueWidth, 230f));
        dialogueGroup = box.gameObject.AddComponent<CanvasGroup>();
        dialogueGroup.alpha = 0f;

        Image panel = RuntimeUI.CreateImage("Panel", box, Vector2.zero, Vector2.zero, RuntimeUI.PanelColor, RuntimeUI.RoundedSprite);
        RuntimeUI.Stretch(panel.rectTransform);

        portrait = RuntimeUI.CreateImage("Portrait", box, new Vector2(-PortraitX, 0f), new Vector2(170f, 170f), Color.gray, RuntimeUI.RoundedSprite);
        portraitText = RuntimeUI.CreateText("Initial", portrait.transform, string.Empty, 96f, Vector2.zero, new Vector2(170f, 170f));
        portraitText.fontStyle = FontStyles.Bold;

        nameBadgeImage = RuntimeUI.CreateImage("NameBadge", box, new Vector2(-NameBadgeX, 132f), new Vector2(240f, 54f), leftSpeakerColor, RuntimeUI.RoundedSprite);
        nameBadge = nameBadgeImage.rectTransform;
        speakerText = RuntimeUI.CreateText("Speaker", nameBadge, string.Empty, 30f, Vector2.zero, new Vector2(240f, 54f));
        speakerText.color = new Color(0.03f, 0.06f, 0.09f, 1f);
        speakerText.fontStyle = FontStyles.Bold;

        lineText = RuntimeUI.CreateText("Line", box, string.Empty, 38f, new Vector2(LineOffsetX, -8f), new Vector2(DialogueWidth - 260f, 190f), TextAlignmentOptions.MidlineLeft);
        lineText.textWrappingMode = TextWrappingModes.Normal;
        lineText.lineSpacing = 12f;
    }

    // 對話框版面 (1920×1080 畫布座標)：框中心在畫面中央下方 DialogueCenterY，頭像在框內左右兩側
    private const float DialogueWidth = 1180f;
    private const float DialogueCenterY = -190f;
    private const float PortraitX = 490f;
    private const float NameBadgeX = 350f;
    private const float LineOffsetX = 75f;

    private static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    /// <summary>VR 手把射線點擊需要 TrackedDeviceGraphicRaycaster (XR Interaction Toolkit)</summary>
    private static void AddVrRaycaster(GameObject host)
    {
        System.Type type = System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        if (type != null && host.GetComponent(type) == null) host.AddComponent(type);
    }

    /// <summary>
    /// 字幕介面掛在 Main Camera 前方 uiDistance 處，依視野寬高等比縮放到 uiViewportFill 的大小
    /// (世界空間 Canvas：滑鼠與 VR 手把射線都能點擊)。VR 單眼畫面比 16:9 窄，只依高度縮放會左右超出視角。
    /// 相機尚未出現或更換時重新掛上。
    /// </summary>
    private void AttachToCamera()
    {
        if (canvas == null) return;
        Camera main = Camera.main;
        if (main == null || canvas.worldCamera == main) return;

        Transform host = canvas.transform;
        host.SetParent(main.transform, false);
        host.localPosition = new Vector3(0f, 0f, uiDistance);
        host.localRotation = Quaternion.identity;
        float visibleHeight = 2f * uiDistance * Mathf.Tan(main.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float visibleWidth = visibleHeight * main.aspect;
        float fit = Mathf.Min(visibleHeight / 1080f, visibleWidth / 1920f);
        host.localScale = Vector3.one * (fit * uiViewportFill);
        canvas.worldCamera = main;
    }

    #endregion
}

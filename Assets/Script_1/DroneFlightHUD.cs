using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 擬真 FPV 飛行 OSD：俯仰梯、航向帶、速度/高度/垂直速度、油門與電池、
/// 返航方向、任務導航、載貨狀態與各種警告。全部在執行時建立於 Drone_HUD_Canvas 下。
/// 按 H 切換「OSD / 舊版數據面板」。
/// </summary>
public class DroneFlightHUD : MonoBehaviour
{
    [Header("HUD References")]
    [SerializeField] private DroneController1 droneController;
    [SerializeField] private TMP_FontAsset chineseFont;
    [SerializeField] private Color horizonColor = new Color(0.2f, 1f, 0.75f, 0.9f);
    [SerializeField] private float horizonWidth = 420f;
    [SerializeField] private float pitchPixelsPerDegree = 5f;
    [SerializeField] private float maxPitchOffset = 120f;

    [Header("擬真 OSD 設定")]
    [SerializeField] private bool showOsd = true;
    [Tooltip("顯示 OSD 時隱藏舊版 LeftPanel 數據文字 (避免重複資訊)")]
    [SerializeField] private bool hideLegacyTelemetryPanel = true;
    [SerializeField] private float osdFontSize = 11f;
    [Tooltip("OSD 整體縮放 (此 Canvas 的既有文字約 20pt，OSD 以 11pt 設計後整體放大)")]
    [SerializeField] private float osdScale = 1.45f;
    [SerializeField] private float headingPixelsPerDegree = 2.2f;
    [Tooltip("OSD 元件與畫面中心的水平距離")]
    [SerializeField] private float sideColumnOffset = 150f;
    [Tooltip("OSD 上下列與畫面中心的垂直距離")]
    [SerializeField] private float topBottomOffset = 112f;
    [Tooltip("設置按鈕與畫面中心的垂直距離 (OSD 座標，位於航向帶上方)")]
    [SerializeField] private float settingsButtonHeight = 138f;
    [Tooltip("設置按鈕往右偏移 (OSD 座標)，避開正中央的航向帶與導航提示")]
    [SerializeField] private float settingsButtonOffsetX = 125f;
    [SerializeField] private Color warningColor = new Color(1f, 0.32f, 0.25f, 1f);
    [SerializeField] private Color cautionColor = new Color(1f, 0.85f, 0.2f, 1f);

    private GameObject firstPersonRoot;
    private GameObject flightHudLayer;
    private RectTransform horizonLine;

    // --- OSD 元件 ---
    private GameObject osdRoot;
    private RectTransform pitchLadder;
    private RectTransform velocityMarker;
    private TextMeshProUGUI speedText;
    private TextMeshProUGUI altitudeText;
    private TextMeshProUGUI headingValueText;
    private readonly TextMeshProUGUI[] headingLabels = new TextMeshProUGUI[9];
    private TextMeshProUGUI statusText;
    private TextMeshProUGUI batteryText;
    private TextMeshProUGUI timerText;
    private TextMeshProUGUI warningText;
    private TextMeshProUGUI navText;
    private TextMeshProUGUI homeText;
    private TextMeshProUGUI cargoText;
    private Image throttleFill;
    private Image batteryFill;
    private RectTransform navArrow;
    private RectTransform headingStrip;
    private RectTransform homeArrow;
    private GameObject legacyPanel;

    private DronePickupHandler pickupHandler;
    private DroneBombDropper bombDropper;
    private InputAction toggleOsdAction;

    // --- 效能：文字以固定頻率更新、只在內容改變時重設 (避免每幀重建 Canvas 與產生 GC) ---
    private const float TextUpdateInterval = 1f / 12f;
    private float textTimer;
    private int shownHeading = -1;
    private readonly int[] shownHeadingLabels = { -1, -1, -1, -1, -1, -1, -1, -1, -1 };
    private readonly RaycastHit[] obstacleHits = new RaycastHit[8];
    private float feedCheckTimer;
    private Camera[] feedCameras;
    private RawImage[] feedDisplays;
    private float crashFlashTimer;
    private float airborneTime;
    private string lastWarning = string.Empty;
    private float lastWarningSoundTime = -10f;

    private void Awake()
    {
        if (droneController == null)
        {
            droneController = GetComponentInParent<DroneController1>();
        }

        if (!BindExistingHudElements())
        {
            CreateHudElements();
        }
        // 操作說明 (含抓取/放下) 已由 PanelEnhancer 重建於「操作介紹面板」
        CreateOsd();

        toggleOsdAction = new InputAction("ToggleOSD", binding: "<Keyboard>/h");
        toggleOsdAction.AddBinding("<Gamepad>/select");

        toggleSettingsAction = new InputAction("ToggleSettings", binding: "<Keyboard>/escape");
        toggleSettingsAction.AddBinding("<Gamepad>/start");
        // VR 手把：左手 Menu 鍵
        toggleSettingsAction.AddBinding("<XRController>{LeftHand}/{MenuButton}");

        // 校準視角 (第三人稱)：V / 手把右搖桿按下 / VR 右手搖桿按下
        recenterAction = new InputAction("RecenterView", binding: "<Keyboard>/v");
        recenterAction.AddBinding("<Gamepad>/rightStickPress");
        recenterAction.AddBinding("<XRController>{RightHand}/{Primary2DAxisClick}");
    }

    private InputAction toggleSettingsAction;
    private InputAction recenterAction;
    private GameObject recenterButton;
    private Button settingsRecenterButton;

    private void OnEnable()
    {
        toggleOsdAction?.Enable();
        toggleSettingsAction?.Enable();
        recenterAction?.Enable();
        if (droneController != null) droneController.OnCrash += HandleCrash;
        HandlesEscape = true;
    }

    private void OnDisable()
    {
        toggleOsdAction?.Disable();
        toggleSettingsAction?.Disable();
        recenterAction?.Disable();
        if (droneController != null) droneController.OnCrash -= HandleCrash;
        HandlesEscape = false;
        if (pausedBySettings)
        {
            pausedBySettings = false;
            Time.timeScale = 1f;
        }
    }

    private void Start()
    {
        // OnEnable 時可能還找不到飛控 (例如 HUD 已被移到攝影機底下)，這裡補訂閱
        if (droneController != null)
        {
            droneController.OnCrash -= HandleCrash;
            droneController.OnCrash += HandleCrash;
            pickupHandler = droneController.GetComponent<DronePickupHandler>();
        }
        ApplyOsdVisibility();

        // HUD 是世界空間 Canvas：貼近牆面/建築時不能被遮住
        if (GetComponent<HudAlwaysOnTop>() == null) gameObject.AddComponent<HudAlwaysOnTop>();
    }

    private void Update()
    {
        if (droneController == null)
        {
            droneController = GetComponentInParent<DroneController1>();
            if (droneController == null) return;
            droneController.OnCrash += HandleCrash;
            pickupHandler = droneController.GetComponent<DronePickupHandler>();
        }

        if (toggleOsdAction != null && toggleOsdAction.WasPressedThisFrame())
        {
            showOsd = !showOsd;
            ApplyOsdVisibility();
        }

        if (toggleSettingsAction != null && toggleSettingsAction.WasPressedThisFrame())
        {
            ToggleSettings();
        }
        SyncSettingsPause();

        bool isFirstPerson = droneController.IsFirstPerson;
        firstPersonRoot.SetActive(isFirstPerson);

        // 校準視角只在第三人稱提供
        if (recenterButton != null && recenterButton.activeSelf == isFirstPerson) recenterButton.SetActive(!isFirstPerson);
        if (recenterAction != null && recenterAction.WasPressedThisFrame() && !isFirstPerson) RecenterView();

        // 以第一人稱鏡頭錨點計算姿態，確保與玩家看到的畫面一致 (不受模型朝向影響)
        Transform view = droneController.FirstPersonAnchor;
        float pitchUp = Mathf.Asin(Mathf.Clamp(view.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        float rollRight = -Mathf.Asin(Mathf.Clamp(view.right.y, -1f, 1f)) * Mathf.Rad2Deg;

        float pitchOffset = Mathf.Clamp(-pitchUp * pitchPixelsPerDegree, -maxPitchOffset, maxPitchOffset);
        horizonLine.anchoredPosition = new Vector2(0f, pitchOffset);
        horizonLine.localRotation = Quaternion.Euler(0f, 0f, rollRight);

        if (osdRoot == null || !osdRoot.activeSelf) return;

        bool flying = droneController.IsEngineStarted && !droneController.isGrounded;
        if (flying) airborneTime += Time.deltaTime;
        crashFlashTimer -= Time.deltaTime;

        textTimer -= Time.deltaTime;
        bool textFrame = textTimer <= 0f;

        // 第一人稱：俯仰梯與航向帶每幀更新 (需跟著畫面轉動)。
        // 第三人稱：OSD 只是資訊顯示，整個 OSD 降頻更新——世界空間 HUD 每幀變動會讓整張 Canvas
        // 重新處理並大量配置記憶體，轉彎時造成週期性卡頓。
        if (isFirstPerson || textFrame)
        {
            UpdatePitchLadder(pitchUp, rollRight, isFirstPerson);
            UpdateVelocityMarker(view, isFirstPerson);
            UpdateHeadingTape(view);
            UpdateNavigationArrows(view);
            UpdateBlink();
        }

        if (textFrame)
        {
            textTimer = TextUpdateInterval;
            UpdateTelemetry();
            UpdateNavigation(view);
            UpdateCargo();
            UpdateWarnings();
        }

        feedCheckTimer -= Time.deltaTime;
        if (feedCheckTimer <= 0f)
        {
            feedCheckTimer = 0.5f;
            UpdateFeedCameras();
        }
    }

    /// <summary>只在內容改變時才設定文字 (TMP 每次設定都會重建網格)</summary>
    private static void SetText(TextMeshProUGUI target, string value)
    {
        if (target != null && target.text != value) target.text = value;
    }

    /// <summary>
    /// 機身前視相機只在畫面上有顯示它的 RawImage 時才渲染；
    /// 否則每幀多畫一次整個城市卻沒人看到。
    /// </summary>
    private void UpdateFeedCameras()
    {
        // 只在第一次搜尋 (全場景搜尋很慢，週期性呼叫會造成卡頓)
        if (feedCameras == null)
        {
            feedCameras = droneController.GetComponentsInChildren<Camera>(true);
            feedDisplays = GetComponentsInChildren<RawImage>(true);
        }

        foreach (Camera feedCamera in feedCameras)
        {
            if (feedCamera == null || feedCamera.targetTexture == null) continue;
            bool displayed = false;
            foreach (RawImage image in feedDisplays)
            {
                if (image != null && image.texture == feedCamera.targetTexture && image.isActiveAndEnabled)
                {
                    displayed = true;
                    break;
                }
            }
            if (feedCamera.enabled != displayed) feedCamera.enabled = displayed;
        }
    }

    #region 既有元素 (人工地平線 / 操作說明)

    private void CreateHudElements()
    {
        flightHudLayer = CreateRectObject("FlightHudLayer", transform);
        flightHudLayer.transform.SetAsFirstSibling();

        firstPersonRoot = CreateRectObject("FPV_HorizonHUD", flightHudLayer.transform);
        firstPersonRoot.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0.5f);
        firstPersonRoot.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);
        firstPersonRoot.GetComponent<RectTransform>().sizeDelta = new Vector2(600f, 300f);

        GameObject horizon = new GameObject("ArtificialHorizon", typeof(RectTransform), typeof(Image));
        horizon.transform.SetParent(firstPersonRoot.transform, false);
        horizonLine = horizon.GetComponent<RectTransform>();
        horizonLine.anchorMin = new Vector2(0.5f, 0.5f);
        horizonLine.anchorMax = new Vector2(0.5f, 0.5f);
        horizonLine.sizeDelta = new Vector2(horizonWidth, 3f);
        horizon.GetComponent<Image>().color = horizonColor;

        TextMeshProUGUI centerMarker = CreateText("CenterMarker", firstPersonRoot.transform, "+", 34f);
        centerMarker.alignment = TextAlignmentOptions.Center;
        centerMarker.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        centerMarker.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        centerMarker.rectTransform.sizeDelta = new Vector2(80f, 60f);
        centerMarker.color = horizonColor;

    }

    private bool BindExistingHudElements()
    {
        Transform layer = transform.Find("FlightHudLayer");
        Transform firstPerson = layer != null ? layer.Find("FPV_HorizonHUD") : null;
        Transform horizon = firstPerson != null ? firstPerson.Find("ArtificialHorizon") : null;

        if (layer == null || firstPerson == null || horizon == null)
        {
            return false;
        }

        flightHudLayer = layer.gameObject;
        firstPersonRoot = firstPerson.gameObject;
        horizonLine = horizon.GetComponent<RectTransform>();
        return horizonLine != null;
    }

    #endregion

    #region OSD 建立

    private void CreateOsd()
    {
        RuntimeUI.SetPreferredFont(ResolveFont());

        Transform existing = flightHudLayer.transform.Find("FlightOSD");
        if (existing != null)
        {
            Destroy(existing.gameObject);
        }

        RectTransform root = RuntimeUI.CreateCentered("FlightOSD", flightHudLayer.transform, Vector2.zero, new Vector2(400f, 250f));
        root.localScale = Vector3.one * osdScale;
        osdRoot = root.gameObject;
        Color dim = new Color(horizonColor.r, horizonColor.g, horizonColor.b, 0.55f);

        // --- 俯仰梯 (只在第一人稱顯示，放在 FPV_HorizonHUD 底下，並以遮罩限制範圍) ---
        RectTransform ladderWindow = RuntimeUI.CreateCentered("PitchLadderWindow", firstPersonRoot.transform, Vector2.zero, new Vector2(260f, 190f));
        ladderWindow.gameObject.AddComponent<RectMask2D>();
        pitchLadder = RuntimeUI.CreateCentered("PitchLadder", ladderWindow, Vector2.zero, new Vector2(260f, 600f));
        for (int degrees = -40; degrees <= 40; degrees += 10)
        {
            if (degrees == 0) continue;
            float y = degrees * pitchPixelsPerDegree;
            Color lineColor = degrees > 0 ? horizonColor : dim;
            float segment = degrees > 0 ? 34f : 14f;
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 46f;
                RuntimeUI.CreateImage($"Rung{degrees}_{side}", pitchLadder, new Vector2(x, y), new Vector2(segment, 1.5f), lineColor);
                if (degrees < 0)
                {
                    // 負俯角以虛線表示
                    RuntimeUI.CreateImage($"Rung{degrees}_{side}_b", pitchLadder, new Vector2(x + side * 20f, y), new Vector2(segment, 1.5f), lineColor);
                }
                TextMeshProUGUI label = RuntimeUI.CreateText($"RungLabel{degrees}_{side}", pitchLadder, Mathf.Abs(degrees).ToString(),
                    osdFontSize * osdScale * 0.8f, new Vector2(side * 88f, y), new Vector2(30f, 16f));
                label.color = lineColor;
            }
        }

        // 飛行路徑標記 (實際移動方向)
        velocityMarker = RuntimeUI.CreateCentered("VelocityMarker", firstPersonRoot.transform, Vector2.zero, new Vector2(10f, 10f));
        velocityMarker.localScale = Vector3.one * osdScale;
        Image ring = velocityMarker.gameObject.AddComponent<Image>();
        ring.sprite = RuntimeUI.RoundedSprite;
        ring.type = Image.Type.Sliced;
        ring.color = new Color(horizonColor.r, horizonColor.g, horizonColor.b, 0.85f);
        ring.raycastTarget = false;
        RuntimeUI.CreateImage("WingL", velocityMarker, new Vector2(-10f, 0f), new Vector2(8f, 1.5f), horizonColor);
        RuntimeUI.CreateImage("WingR", velocityMarker, new Vector2(10f, 0f), new Vector2(8f, 1.5f), horizonColor);
        RuntimeUI.CreateImage("Fin", velocityMarker, new Vector2(0f, 8f), new Vector2(1.5f, 6f), horizonColor);

        // --- 航向帶 ---
        float topY = topBottomOffset;
        RectTransform headingWindow = RuntimeUI.CreateCentered("HeadingTape", root, new Vector2(0f, topY), new Vector2(200f, 18f));
        headingWindow.gameObject.AddComponent<RectMask2D>();
        RuntimeUI.CreateImage("HeadingTapeBG", headingWindow, Vector2.zero, new Vector2(200f, 18f), new Color(0f, 0f, 0f, 0.25f));
        // 標籤固定排在一條帶子上，每幀只移動帶子 (移動個別 TMP 會讓它們每幀重建網格並產生 GC)
        headingStrip = RuntimeUI.CreateCentered("HeadingStrip", headingWindow, Vector2.zero, new Vector2(200f, 18f));
        int halfCount = headingLabels.Length / 2;
        for (int i = 0; i < headingLabels.Length; i++)
        {
            headingLabels[i] = RuntimeUI.CreateText($"HeadingLabel{i}", headingStrip, "", osdFontSize * 0.85f,
                new Vector2((i - halfCount) * 15f * headingPixelsPerDegree, 0f), new Vector2(30f, 16f));
            headingLabels[i].color = horizonColor;
        }
        Image caret = RuntimeUI.CreateImage("HeadingCaret", root, new Vector2(0f, topY - 14f), new Vector2(8f, 6f), horizonColor, RuntimeUI.TriangleSprite);
        caret.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);
        headingValueText = RuntimeUI.CreateText("HeadingValue", root, "000°", osdFontSize, new Vector2(0f, topY + 16f), new Vector2(60f, 14f));
        headingValueText.color = horizonColor;

        // --- 速度 (左) / 高度 (右) 方框 ---
        speedText = CreateDataBox("SpeedBox", root, new Vector2(-sideColumnOffset, 0f), TextAlignmentOptions.Right);
        altitudeText = CreateDataBox("AltitudeBox", root, new Vector2(sideColumnOffset, 0f), TextAlignmentOptions.Left);

        // 油門條 (速度框左側)
        RuntimeUI.CreateImage("ThrottleBG", root, new Vector2(-sideColumnOffset - 44f, 0f), new Vector2(5f, 60f), new Color(0f, 0f, 0f, 0.35f));
        throttleFill = RuntimeUI.CreateImage("ThrottleFill", root, new Vector2(-sideColumnOffset - 44f, -30f), new Vector2(5f, 60f), horizonColor);
        throttleFill.rectTransform.pivot = new Vector2(0.5f, 0f);
        TextMeshProUGUI thrLabel = RuntimeUI.CreateText("ThrottleLabel", root, "THR", osdFontSize * 0.7f, new Vector2(-sideColumnOffset - 44f, -38f), new Vector2(30f, 10f));
        thrLabel.color = dim;

        // --- 下方狀態列 ---
        float bottomY = -topBottomOffset;
        statusText = RuntimeUI.CreateText("StatusLine", root, "", osdFontSize, new Vector2(-40f, bottomY), new Vector2(200f, 14f), TextAlignmentOptions.Left);
        statusText.color = horizonColor;
        timerText = RuntimeUI.CreateText("FlightTimer", root, "", osdFontSize, new Vector2(-sideColumnOffset, bottomY), new Vector2(80f, 14f), TextAlignmentOptions.Left);
        timerText.color = horizonColor;

        batteryText = RuntimeUI.CreateText("Battery", root, "", osdFontSize, new Vector2(sideColumnOffset - 10f, bottomY), new Vector2(110f, 14f), TextAlignmentOptions.Right);
        batteryText.color = horizonColor;
        RuntimeUI.CreateImage("BatteryBG", root, new Vector2(sideColumnOffset + 10f, bottomY - 11f), new Vector2(70f, 3f), new Color(0f, 0f, 0f, 0.35f));
        batteryFill = RuntimeUI.CreateImage("BatteryFill", root, new Vector2(sideColumnOffset - 25f, bottomY - 11f), new Vector2(70f, 3f), horizonColor);
        batteryFill.rectTransform.pivot = new Vector2(0f, 0.5f);

        // 返航指示
        homeArrow = RuntimeUI.CreateImage("HomeArrow", root, new Vector2(sideColumnOffset - 4f, bottomY - 24f), new Vector2(8f, 10f), horizonColor, RuntimeUI.TriangleSprite).rectTransform;
        homeText = RuntimeUI.CreateText("HomeText", root, "", osdFontSize * 0.85f, new Vector2(sideColumnOffset - 50f, bottomY - 24f), new Vector2(80f, 12f), TextAlignmentOptions.Right);
        homeText.color = horizonColor;

        // --- 導航目標 (航向帶下方) ---
        navArrow = RuntimeUI.CreateImage("NavArrow", root, new Vector2(0f, topY - 30f), new Vector2(12f, 14f), Color.cyan, RuntimeUI.TriangleSprite).rectTransform;
        navText = RuntimeUI.CreateText("NavText", root, "", osdFontSize, new Vector2(0f, topY - 46f), new Vector2(220f, 14f));

        // --- 警告與載貨提示 (放在準星下方，避開既有 RightPanel 的 ProgressText) ---
        warningText = RuntimeUI.CreateText("Warning", root, "", osdFontSize * 1.4f, new Vector2(0f, -38f), new Vector2(240f, 20f));
        warningText.color = warningColor;
        warningText.fontStyle = FontStyles.Bold;
        cargoText = RuntimeUI.CreateText("CargoText", root, "", osdFontSize, new Vector2(0f, -54f), new Vector2(260f, 14f));
        cargoText.color = cautionColor;

        legacyPanel = FindDeepChild(transform, "LeftPanel")?.gameObject;

        // 動態元素放在子 Canvas：文字/位置變動時只重建子 Canvas，不影響整個 HUD
        AddNestedCanvas(osdRoot);
        AddNestedCanvas(firstPersonRoot);
        ArrangeLegacyElements();
    }

    private static void AddNestedCanvas(GameObject target)
    {
        if (target != null && target.GetComponent<Canvas>() == null) target.AddComponent<Canvas>();
    }

    /// <summary>可互動的 Canvas：啟用滑鼠用的 GraphicRaycaster，並比照 HUD 根 Canvas 加上 VR 射線用的 Raycaster</summary>
    private void EnsureRaycasters(GameObject target)
    {
        if (target == null) return;
        GraphicRaycaster raycaster = target.GetComponent<GraphicRaycaster>();
        if (raycaster == null) raycaster = target.AddComponent<GraphicRaycaster>();
        raycaster.enabled = true;

        if (target == gameObject) return;
        foreach (UnityEngine.EventSystems.BaseRaycaster rootRaycaster in GetComponents<UnityEngine.EventSystems.BaseRaycaster>())
        {
            System.Type type = rootRaycaster.GetType();
            if (type == typeof(GraphicRaycaster) || target.GetComponent(type) != null) continue;
            target.AddComponent(type);
        }
    }

    /// <summary>
    /// 既有 HUD 元素重新排版，避免與 OSD 重疊：
    /// 競速計時放左上、檢查點進度放右上 (原本被失效的版面配置疊在航向帶與下方狀態列上)，
    /// 設置按鈕移到左下角 (原本壓在電量文字上)。
    /// </summary>
    private void ArrangeLegacyElements()
    {
        Transform rightPanel = FindDeepChild(transform, "RightPanel");
        if (rightPanel != null)
        {
            LayoutGroup layout = rightPanel.GetComponent<LayoutGroup>();
            if (layout != null) layout.enabled = false;
            AddNestedCanvas(rightPanel.gameObject);
            PlaceLegacyText(FindDeepChild(rightPanel, "TimerText"), new Vector2(-osdScale * 150f, osdScale * 100f), TextAlignmentOptions.Left);
            PlaceLegacyText(FindDeepChild(rightPanel, "ProgressText"), new Vector2(osdScale * 150f, osdScale * 100f), TextAlignmentOptions.Right);
        }

        Transform settingsButton = null;
        foreach (Transform child in transform)
        {
            if (child.name.Contains("設置") && child.GetComponent<Button>() != null) settingsButton = child;
        }
        if (settingsButton != null)
        {
            // 放在畫面右上方 (檢查點進度上面)，避開中央的航向帶與導航提示；標示 ESC / 手把 Menu 鍵
            RectTransform rect = (RectTransform)settingsButton;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(osdScale * settingsButtonOffsetX, osdScale * settingsButtonHeight);
            rect.sizeDelta = HudButtonSize;
            Button button = settingsButton.GetComponent<Button>();
            ControlsHelp.StyleButton(button, "<sprite name=\"esc\"><sprite name=\"xmenu\"> 設置");
            Image image = button.targetGraphic as Image;
            if (image != null) image.color = new Color(0.05f, 0.35f, 0.32f, 0.92f);
            TextMeshProUGUI label = settingsButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.fontSize = HudButtonFontSize;
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.NoWrap;
            }
            AddNestedCanvas(settingsButton.gameObject);
            // 巢狀 Canvas 的 UI 只會被「自己」的 Raycaster 判定，按鈕要能點必須補上
            EnsureRaycasters(settingsButton.gameObject);
        }
        CreateRecenterButton();
        // 滑鼠點擊設置面板/按鈕需要 GraphicRaycaster (Prefab 上預設停用，只留 VR 射線用的 Raycaster)
        EnsureRaycasters(gameObject);

        settingsPanel = FindDeepChild(transform, "[Panel]設置")?.gameObject;
    }

    #region 校準視角 (第三人稱)

    // HUD 上方「校準視角」與「設置」按鈕的大小
    private static readonly Vector2 HudButtonSize = new Vector2(120f, 28f);
    private const float HudButtonFontSize = 14f;

    /// <summary>醒目的常駐按鈕，放在設置按鈕的左右對稱位置；第一人稱時隱藏</summary>
    private void CreateRecenterButton()
    {
        if (recenterButton != null) return;

        Button button = RuntimeUI.CreateButton("[Button]校準視角", transform, string.Empty,
            new Vector2(-osdScale * settingsButtonOffsetX, osdScale * settingsButtonHeight), HudButtonSize, RecenterFromButton, HudButtonFontSize);
        // 鍵盤 V / 手把右搖桿按下 (R3)
        ControlsHelp.StyleButton(button, "<sprite name=\"v\"><sprite name=\"jrclick\"> 校準視角");
        Image image = button.targetGraphic as Image;
        if (image != null) image.color = new Color(1f, 0.55f, 0.1f, 0.95f);
        Outline outline = button.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 0.95f, 0.6f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);

        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.fontSize = HudButtonFontSize;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
        }

        recenterButton = button.gameObject;
        AddNestedCanvas(recenterButton);
        // 巢狀 Canvas 的 UI 只會被「自己」的 Raycaster 判定，按鈕要能點必須補上
        EnsureRaycasters(recenterButton);
        recenterButton.SetActive(droneController == null || !droneController.IsFirstPerson);
    }

    /// <summary>讓玩家目前的頭部朝向對準無人機 (V 鍵、手把 / VR 右搖桿按下)</summary>
    public void RecenterView()
    {
        if (droneController == null || !droneController.RecenterView()) return;
        GameAudio.Play(GameSfx.UIClick);
    }

    /// <summary>
    /// 設置面板內也放一顆「校準視角」，與「操作說明」並排；第一人稱時停用。
    /// 在面板開啟時才建立 (PanelEnhancer 排版完成之後)，避免位置被覆蓋。
    /// </summary>
    private void SyncSettingsRecenterButton()
    {
        if (settingsPanel == null) return;

        if (settingsRecenterButton == null)
        {
            settingsRecenterButton = RuntimeUI.CreateButton("[Button]校準視角", settingsPanel.transform, string.Empty,
                new Vector2(92f, -52f), new Vector2(170f, 34f), RecenterFromButton, 18f);
            ControlsHelp.StyleButton(settingsRecenterButton, "<sprite name=\"v\"><sprite name=\"jrclick\"> 校準視角");

            foreach (Button button in settingsPanel.GetComponentsInChildren<Button>(true))
            {
                if (button.name != "[Button]鍵盤操控") continue;
                ((RectTransform)button.transform).anchoredPosition = new Vector2(-92f, -52f);
                break;
            }
        }

        settingsRecenterButton.interactable = droneController != null && droneController.CanRecenterView;
    }

    /// <summary>按鈕點擊音效已由 UIButtonTweenFx 播放</summary>
    private void RecenterFromButton()
    {
        if (droneController != null) droneController.RecenterView();
    }

    #endregion

    #region 設置面板 (ESC)

    /// <summary>有無人機 HUD 設置面板時，ESC 由 HUD 處理 (OptionsMenu 不再顯示自己的暫停選單)</summary>
    public static bool HandlesEscape { get; private set; }

    private GameObject settingsPanel;
    private bool pausedBySettings;

    /// <summary>ESC / 手把 Start：開啟或關閉設置面板</summary>
    public void ToggleSettings()
    {
        if (settingsPanel == null) return;
        UIPopupPanel popup = settingsPanel.GetComponent<UIPopupPanel>();
        bool open = IsSettingsOpen();
        if (popup != null)
        {
            if (open) popup.Hide();
            else popup.Show();
        }
        else
        {
            settingsPanel.SetActive(!open);
        }
    }

    private bool IsSettingsOpen()
    {
        if (settingsPanel == null || !settingsPanel.activeInHierarchy) return false;
        UIPopupPanel popup = settingsPanel.GetComponent<UIPopupPanel>();
        return popup == null || popup.IsOpen;
    }

    /// <summary>面板開著時暫停遊戲 (按鈕、ESC 開關都適用)，關閉時恢復</summary>
    private void SyncSettingsPause()
    {
        bool open = IsSettingsOpen();
        if (open == pausedBySettings) return;
        pausedBySettings = open;
        Time.timeScale = open ? 0f : 1f;
        if (open) SyncSettingsRecenterButton();
        SyncSettingsSelection(open);
    }

    /// <summary>
    /// 手把操作 UI 需要「目前選取的按鈕」才能用搖桿/方向鍵移動、A 鍵確認：
    /// 開啟面板時預選第一個可互動元件，關閉時清掉面板內的選取 (避免 A 鍵誤觸看不見的按鈕)。
    /// </summary>
    private void SyncSettingsSelection(bool open)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null || settingsPanel == null) return;

        if (open)
        {
            foreach (Selectable selectable in settingsPanel.GetComponentsInChildren<Selectable>())
            {
                if (!selectable.IsInteractable()) continue;
                eventSystem.SetSelectedGameObject(selectable.gameObject);
                return;
            }
        }
        else if (eventSystem.currentSelectedGameObject != null
                 && eventSystem.currentSelectedGameObject.transform.IsChildOf(settingsPanel.transform))
        {
            eventSystem.SetSelectedGameObject(null);
        }
    }

    #endregion

    private void PlaceLegacyText(Transform target, Vector2 canvasPosition, TextAlignmentOptions alignment)
    {
        if (target == null) return;
        RectTransform rect = (RectTransform)target;
        RectTransform canvasRect = (RectTransform)transform;
        // 改以 Canvas 中心為基準定位 (先換算父物件的偏移)
        Vector3 worldCenter = canvasRect.TransformPoint(canvasRect.rect.center);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(alignment == TextAlignmentOptions.Left ? 0f : 1f, 0.5f);
        rect.sizeDelta = new Vector2(140f, 20f);
        rect.position = worldCenter + canvasRect.TransformVector(new Vector3(
            canvasPosition.x + (alignment == TextAlignmentOptions.Left ? -70f : 70f), canvasPosition.y, 0f));
        TextMeshProUGUI text = target.GetComponent<TextMeshProUGUI>();
        if (text != null)
        {
            text.alignment = alignment;
            text.fontSize = osdFontSize * osdScale;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = horizonColor;
        }
    }

    private TextMeshProUGUI CreateDataBox(string objectName, Transform parent, Vector2 position, TextAlignmentOptions alignment)
    {
        Image frame = RuntimeUI.CreateImage(objectName, parent, position, new Vector2(78f, 54f), new Color(0f, 0f, 0f, 0.28f), RuntimeUI.RoundedSprite);
        TextMeshProUGUI text = RuntimeUI.CreateText(objectName + "Text", frame.transform, "", osdFontSize, Vector2.zero, new Vector2(70f, 50f), alignment);
        text.color = horizonColor;
        text.lineSpacing = -10f;
        return text;
    }

    private void ApplyOsdVisibility()
    {
        if (osdRoot != null) osdRoot.SetActive(showOsd);
        if (pitchLadder != null) pitchLadder.parent.gameObject.SetActive(showOsd);
        if (velocityMarker != null) velocityMarker.gameObject.SetActive(showOsd);
        if (legacyPanel != null && hideLegacyTelemetryPanel) legacyPanel.SetActive(!showOsd);
    }

    #endregion

    #region OSD 更新

    private void UpdatePitchLadder(float pitchUp, float rollRight, bool isFirstPerson)
    {
        if (!isFirstPerson || pitchLadder == null) return;
        pitchLadder.anchoredPosition = new Vector2(0f, -pitchUp * pitchPixelsPerDegree);
        pitchLadder.parent.localRotation = Quaternion.Euler(0f, 0f, rollRight);
    }

    private void UpdateVelocityMarker(Transform view, bool isFirstPerson)
    {
        if (!isFirstPerson || velocityMarker == null) return;

        Vector3 localVelocity = view.InverseTransformDirection(droneController.Velocity);
        bool visible = localVelocity.z > 0.5f && droneController.Velocity.magnitude > 1f;
        if (velocityMarker.gameObject.activeSelf != visible) velocityMarker.gameObject.SetActive(visible);
        if (!visible) return;

        float x = Mathf.Atan2(localVelocity.x, localVelocity.z) * Mathf.Rad2Deg * pitchPixelsPerDegree;
        float y = Mathf.Atan2(localVelocity.y, localVelocity.z) * Mathf.Rad2Deg * pitchPixelsPerDegree;
        velocityMarker.anchoredPosition = new Vector2(Mathf.Clamp(x, -120f, 120f), Mathf.Clamp(y, -90f, 90f));
    }

    private void UpdateHeadingTape(Transform view)
    {
        float heading = HeadingOf(view.forward);
        int roundedHeading = Mathf.RoundToInt(heading) % 360;
        if (roundedHeading != shownHeading)
        {
            shownHeading = roundedHeading;
            headingValueText.text = $"{roundedHeading:000}°";
        }

        float baseValue = Mathf.Round(heading / 15f) * 15f;
        int half = headingLabels.Length / 2;
        headingStrip.anchoredPosition = new Vector2(Mathf.DeltaAngle(heading, baseValue) * headingPixelsPerDegree, 0f);
        for (int i = 0; i < headingLabels.Length; i++)
        {
            float value = baseValue + (i - half) * 15f;
            int labelValue = Mathf.RoundToInt(Mathf.Repeat(value, 360f)) % 360;
            if (shownHeadingLabels[i] != labelValue)
            {
                shownHeadingLabels[i] = labelValue;
                headingLabels[i].text = HeadingLabel(labelValue);
            }
        }
    }

    private void UpdateTelemetry()
    {
        float groundSpeed = droneController.GroundSpeed;
        SetText(speedText, $"<size=70%>SPD</size>\n{groundSpeed:0.0}<size=60%> m/s</size>\n<size=70%>{groundSpeed * 3.6f:0} km/h</size>");

        float altitude = droneController.RelativeAltitude;
        float agl = droneController.AltitudeAboveGround;
        float vs = droneController.VerticalSpeed;
        string vsSign = vs >= 0f ? "+" : "-";
        string aglText = agl >= 0f ? $"{agl:0.0}" : "---";
        SetText(altitudeText, $"<size=70%>ALT</size>\n{altitude:0.0}<size=60%> m</size>\n<size=70%>AGL {aglText}  VS {vsSign}{Mathf.Abs(vs):0.0}</size>");

        float throttle = droneController.Throttle01;
        throttleFill.rectTransform.sizeDelta = new Vector2(5f, 60f * throttle);
        throttleFill.color = throttle > 0.9f ? cautionColor : horizonColor;

        SetText(timerText, $"T {RuntimeUI.FormatTime(airborneTime).Substring(0, 5)}");

        string armState = droneController.IsEngineStarted ? "<color=#33FF99>ARMED</color>" : "<color=#FF5544>DISARMED</color>";
        string invert = droneController.isInverted ? "  INV" : string.Empty;
        string view = droneController.IsFirstPerson ? "FPV" : "TPV";
        SetText(statusText, $"{armState}  {droneController.FlightModeLabel}  THR {throttle * 100f:0}%  {view}{invert}");

        float battery = droneController.BatteryPercent;
        // 模擬 4S 鋰電：滿電 16.8V，空電 14.0V，大油門時電壓下降
        float voltage = Mathf.Lerp(14.0f, 16.8f, battery / 100f) - throttle * 0.6f;
        Color batteryColor = battery < 15f ? warningColor : battery < 30f ? cautionColor : horizonColor;
        // 碰撞扣電時短暫顯示扣除量
        float crashDrain = crashFlashTimer > 0f ? droneController.LastCrashBatteryDrain : 0f;
        if (crashDrain > 0f) batteryColor = warningColor;
        SetText(batteryText, crashDrain > 0f
            ? $"BAT {battery:0}%  -{crashDrain:0.#}%"
            : $"BAT {battery:0}%  {voltage:0.0}V");
        batteryText.color = batteryColor;
        batteryFill.rectTransform.sizeDelta = new Vector2(70f * battery / 100f, 3f);
        batteryFill.color = batteryColor;
    }

    /// <summary>返航/導航箭頭每幀更新 (平順轉動)</summary>
    private void UpdateNavigationArrows(Transform view)
    {
        Vector3 dronePosition = droneController.transform.position;
        float viewHeading = HeadingOf(view.forward);
        Vector3 toHome = droneController.HomePosition - dronePosition;
        homeArrow.localRotation = Quaternion.Euler(0f, 0f, -Mathf.DeltaAngle(viewHeading, HeadingOf(toHome)));

        bool hasTarget = NavigationTarget.HasTarget;
        if (navArrow.gameObject.activeSelf != hasTarget) navArrow.gameObject.SetActive(hasTarget);
        if (navText.gameObject.activeSelf != hasTarget) navText.gameObject.SetActive(hasTarget);
        if (!hasTarget) return;

        Vector3 toTarget = NavigationTarget.Position - dronePosition;
        navArrow.localRotation = Quaternion.Euler(0f, 0f, -Mathf.DeltaAngle(viewHeading, HeadingOf(toTarget)));
    }

    /// <summary>返航距離與導航文字 (降頻更新)</summary>
    private void UpdateNavigation(Transform view)
    {
        Vector3 dronePosition = droneController.transform.position;
        Vector3 toHome = droneController.HomePosition - dronePosition;
        float homeDistance = new Vector2(toHome.x, toHome.z).magnitude;
        SetText(homeText, $"HOME {homeDistance:0}m");

        if (!NavigationTarget.HasTarget) return;
        Vector3 toTarget = NavigationTarget.Position - dronePosition;
        Image arrowImage = navArrow.GetComponent<Image>();
        if (arrowImage.color != NavigationTarget.Color) arrowImage.color = NavigationTarget.Color;
        if (navText.color != NavigationTarget.Color) navText.color = NavigationTarget.Color;
        string height = toTarget.y >= 0f ? $"+{toTarget.y:0}" : $"{toTarget.y:0}";
        SetText(navText, $"{NavigationTarget.Label}  {toTarget.magnitude:0}m  ({height}m)");
    }

    private void UpdateCargo()
    {
        if (bombDropper == null && droneController != null) bombDropper = droneController.GetComponent<DroneBombDropper>();

        if (pickupHandler == null)
        {
            SetText(cargoText, string.Empty);
            return;
        }

        if (bombDropper != null && !pickupHandler.IsCarrying && pickupHandler.CargoInRange == null)
        {
            // 炸彈無人機：顯示投彈狀態
            cargoText.color = bombDropper.IsLoaded ? warningColor : cautionColor;
            SetText(cargoText, bombDropper.IsLoaded ? "BOMB READY   [B / RT] 投彈" : $"RELOADING {bombDropper.ReloadProgress * 100f:0}%");
        }
        else if (pickupHandler.IsCarrying)
        {
            cargoText.color = cautionColor;
            SetText(cargoText, $"PAYLOAD {pickupHandler.CarriedMass:0.0}kg   [Z / A] 放下");
        }
        else if (pickupHandler.CargoInRange != null)
        {
            cargoText.color = horizonColor;
            SetText(cargoText, "[Z / A] 抓取貨物");
        }
        else
        {
            SetText(cargoText, string.Empty);
        }
    }

    private void UpdateBlink()
    {
        if (string.IsNullOrEmpty(lastWarning)) return;
        // 以 CanvasRenderer 調整透明度：不會讓 TMP 重建網格
        warningText.canvasRenderer.SetAlpha(0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 6f)));
    }

    private void UpdateWarnings()
    {
        string warning = string.Empty;
        float agl = droneController.AltitudeAboveGround;
        float battery = droneController.BatteryPercent;
        float speed = droneController.Velocity.magnitude;

        if (crashFlashTimer > 0f)
        {
            warning = "COLLISION";
        }
        else if (agl >= 0f && agl < 2.5f && droneController.VerticalSpeed < -3.5f)
        {
            warning = "PULL UP";
        }
        else if (speed > 3f && IsObstacleAhead(speed))
        {
            warning = "OBSTACLE";
        }
        else if (battery < 10f)
        {
            warning = "BATTERY CRITICAL";
        }
        else if (battery < 20f)
        {
            warning = "LOW BATTERY";
        }

        if (warning.Length > 0 && warning != lastWarning && Time.time - lastWarningSoundTime > 1.5f)
        {
            lastWarningSoundTime = Time.time;
            GameAudio.Play(GameSfx.Warning, 0.6f);
        }
        lastWarning = warning;

        SetText(warningText, warning);
    }

    private bool IsObstacleAhead(float speed)
    {
        Vector3 direction = droneController.Velocity.normalized;
        float range = Mathf.Clamp(speed * 1.2f, 3f, 25f);
        int count = Physics.SphereCastNonAlloc(droneController.transform.position, 0.3f, direction, obstacleHits, range, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (!obstacleHits[i].collider.transform.IsChildOf(droneController.transform))
            {
                return true;
            }
        }
        return false;
    }

    private void HandleCrash(float impactSpeed)
    {
        crashFlashTimer = 0.8f;
    }

    private static float HeadingOf(Vector3 direction)
    {
        Vector3 flat = new Vector3(direction.x, 0f, direction.z);
        if (flat.sqrMagnitude < 0.0001f) return 0f;
        return Mathf.Repeat(Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, 360f);
    }

    private static string HeadingLabel(int degrees)
    {
        switch (degrees)
        {
            case 0: return "N";
            case 45: return "NE";
            case 90: return "E";
            case 135: return "SE";
            case 180: return "S";
            case 225: return "SW";
            case 270: return "W";
            case 315: return "NW";
            default: return (degrees / 10).ToString("00");
        }
    }

    #endregion

    private static GameObject CreateRectObject(string objectName, Transform parent)
    {
        GameObject created = new GameObject(objectName, typeof(RectTransform));
        created.transform.SetParent(parent, false);
        return created;
    }

    private TextMeshProUGUI CreateText(string objectName, Transform parent, string content, float fontSize)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = fontSize;
        text.font = ResolveFont();
        text.raycastTarget = false;
        return text;
    }

    private TMP_FontAsset ResolveFont()
    {
        if (chineseFont != null)
        {
            return chineseFont;
        }

        // 不再沿用畫布上第一個文字的字型 (可能是沒有中文的 LiberationSans)
        return RuntimeUI.ResolveFont();
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        foreach (Transform child in root)
        {
            if (child.name == childName)
            {
                return child;
            }

            Transform result = FindDeepChild(child, childName);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}

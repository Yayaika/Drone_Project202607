using TMPro;
using UnityEngine;
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
    private RectTransform homeArrow;
    private GameObject legacyPanel;

    private DronePickupHandler pickupHandler;
    private InputAction toggleOsdAction;
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
    }

    private void OnEnable()
    {
        toggleOsdAction?.Enable();
        if (droneController != null) droneController.OnCrash += HandleCrash;
    }

    private void OnDisable()
    {
        toggleOsdAction?.Disable();
        if (droneController != null) droneController.OnCrash -= HandleCrash;
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

        bool isFirstPerson = droneController.IsFirstPerson;
        firstPersonRoot.SetActive(isFirstPerson);

        // 以第一人稱鏡頭錨點計算姿態，確保與玩家看到的畫面一致 (不受模型朝向影響)
        Transform view = droneController.FirstPersonAnchor;
        float pitchUp = Mathf.Asin(Mathf.Clamp(view.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        float rollRight = -Mathf.Asin(Mathf.Clamp(view.right.y, -1f, 1f)) * Mathf.Rad2Deg;

        float pitchOffset = Mathf.Clamp(-pitchUp * pitchPixelsPerDegree, -maxPitchOffset, maxPitchOffset);
        horizonLine.anchoredPosition = new Vector2(0f, pitchOffset);
        horizonLine.localRotation = Quaternion.Euler(0f, 0f, rollRight);

        if (osdRoot == null || !osdRoot.activeSelf) return;

        UpdatePitchLadder(pitchUp, rollRight, isFirstPerson);
        UpdateVelocityMarker(view, isFirstPerson);
        UpdateHeadingTape(view);
        UpdateTelemetry();
        UpdateNavigation(view);
        UpdateCargo();
        UpdateWarnings();
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
        for (int i = 0; i < headingLabels.Length; i++)
        {
            headingLabels[i] = RuntimeUI.CreateText($"HeadingLabel{i}", headingWindow, "", osdFontSize * 0.85f, Vector2.zero, new Vector2(30f, 16f));
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
        velocityMarker.gameObject.SetActive(visible);
        if (!visible) return;

        float x = Mathf.Atan2(localVelocity.x, localVelocity.z) * Mathf.Rad2Deg * pitchPixelsPerDegree;
        float y = Mathf.Atan2(localVelocity.y, localVelocity.z) * Mathf.Rad2Deg * pitchPixelsPerDegree;
        velocityMarker.anchoredPosition = new Vector2(Mathf.Clamp(x, -120f, 120f), Mathf.Clamp(y, -90f, 90f));
    }

    private void UpdateHeadingTape(Transform view)
    {
        float heading = HeadingOf(view.forward);
        headingValueText.text = $"{Mathf.RoundToInt(heading) % 360:000}°";

        float baseValue = Mathf.Round(heading / 15f) * 15f;
        int half = headingLabels.Length / 2;
        for (int i = 0; i < headingLabels.Length; i++)
        {
            float value = baseValue + (i - half) * 15f;
            float x = Mathf.DeltaAngle(heading, value) * headingPixelsPerDegree;
            headingLabels[i].rectTransform.anchoredPosition = new Vector2(x, 0f);
            headingLabels[i].text = HeadingLabel(Mathf.RoundToInt(Mathf.Repeat(value, 360f)) % 360);
        }
    }

    private void UpdateTelemetry()
    {
        float groundSpeed = droneController.GroundSpeed;
        speedText.text = $"<size=70%>SPD</size>\n{groundSpeed:0.0}<size=60%> m/s</size>\n<size=70%>{groundSpeed * 3.6f:0} km/h</size>";

        float altitude = droneController.RelativeAltitude;
        float agl = droneController.AltitudeAboveGround;
        float vs = droneController.VerticalSpeed;
        string vsSign = vs >= 0f ? "+" : "-";
        string aglText = agl >= 0f ? $"{agl:0.0}" : "---";
        altitudeText.text = $"<size=70%>ALT</size>\n{altitude:0.0}<size=60%> m</size>\n<size=70%>AGL {aglText}  VS {vsSign}{Mathf.Abs(vs):0.0}</size>";

        float throttle = droneController.Throttle01;
        throttleFill.rectTransform.sizeDelta = new Vector2(5f, 60f * throttle);
        throttleFill.color = throttle > 0.9f ? cautionColor : horizonColor;

        bool flying = droneController.IsEngineStarted && !droneController.isGrounded;
        if (flying) airborneTime += Time.deltaTime;
        timerText.text = $"T {RuntimeUI.FormatTime(airborneTime).Substring(0, 5)}";

        string armState = droneController.IsEngineStarted ? "<color=#33FF99>ARMED</color>" : "<color=#FF5544>DISARMED</color>";
        string invert = droneController.isInverted ? "  INV" : string.Empty;
        string view = droneController.IsFirstPerson ? "FPV" : "TPV";
        statusText.text = $"{armState}  {droneController.FlightModeLabel}  THR {throttle * 100f:0}%  {view}{invert}";

        float battery = droneController.BatteryPercent;
        // 模擬 4S 鋰電：滿電 16.8V，空電 14.0V，大油門時電壓下降
        float voltage = Mathf.Lerp(14.0f, 16.8f, battery / 100f) - throttle * 0.6f;
        Color batteryColor = battery < 15f ? warningColor : battery < 30f ? cautionColor : horizonColor;
        batteryText.text = $"BAT {battery:0}%  {voltage:0.0}V";
        batteryText.color = batteryColor;
        batteryFill.rectTransform.sizeDelta = new Vector2(70f * battery / 100f, 3f);
        batteryFill.color = batteryColor;
    }

    private void UpdateNavigation(Transform view)
    {
        Vector3 dronePosition = droneController.transform.position;
        float viewHeading = HeadingOf(view.forward);

        // 返航方向與距離
        Vector3 toHome = droneController.HomePosition - dronePosition;
        float homeDistance = new Vector2(toHome.x, toHome.z).magnitude;
        homeText.text = $"HOME {homeDistance:0}m";
        homeArrow.localRotation = Quaternion.Euler(0f, 0f, -Mathf.DeltaAngle(viewHeading, HeadingOf(toHome)));

        bool hasTarget = NavigationTarget.HasTarget;
        navArrow.gameObject.SetActive(hasTarget);
        navText.gameObject.SetActive(hasTarget);
        if (!hasTarget) return;

        Vector3 toTarget = NavigationTarget.Position - dronePosition;
        float distance = toTarget.magnitude;
        float relative = Mathf.DeltaAngle(viewHeading, HeadingOf(toTarget));
        navArrow.localRotation = Quaternion.Euler(0f, 0f, -relative);
        Image arrowImage = navArrow.GetComponent<Image>();
        arrowImage.color = NavigationTarget.Color;

        string height = toTarget.y >= 0f ? $"+{toTarget.y:0}" : $"{toTarget.y:0}";
        navText.color = NavigationTarget.Color;
        navText.text = $"{NavigationTarget.Label}  {distance:0}m  ({height}m)";
    }

    private void UpdateCargo()
    {
        if (pickupHandler == null)
        {
            cargoText.text = string.Empty;
            return;
        }

        if (pickupHandler.IsCarrying)
        {
            cargoText.color = cautionColor;
            cargoText.text = $"PAYLOAD {pickupHandler.CarriedMass:0.0}kg   [Z / A] 放下";
        }
        else if (pickupHandler.CargoInRange != null)
        {
            cargoText.color = horizonColor;
            cargoText.text = "[Z / A] 抓取貨物";
        }
        else
        {
            cargoText.text = string.Empty;
        }
    }

    private void UpdateWarnings()
    {
        crashFlashTimer -= Time.deltaTime;

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

        warningText.text = warning;
        float blink = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 6f));
        warningText.alpha = blink;
    }

    private bool IsObstacleAhead(float speed)
    {
        Vector3 direction = droneController.Velocity.normalized;
        float range = Mathf.Clamp(speed * 1.2f, 3f, 25f);
        RaycastHit[] hits = Physics.SphereCastAll(droneController.transform.position, 0.3f, direction, range, ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            if (!hit.collider.transform.IsChildOf(droneController.transform))
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

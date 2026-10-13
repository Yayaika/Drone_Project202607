using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using TMPro; // 【新增】用於支援 World Space Canvas 的 TextMeshPro 文字顯示
using UnityEngine.SceneManagement;

public class DroneController1 : MonoBehaviour
{
    // 飛行模式列舉
    public enum FlightMode
    {
        Stabilized, // 0. 自平懸停模式
        Cruise,     // 1. 定角巡航模式
        Vertical    // 2. 垂直無攻角模式
    }

    [Header("【飛行狀態 (Flight State)】")]
    [SerializeField] private FlightMode currentFlightMode = FlightMode.Stabilized;
    public bool isArmed = false;     // 引擎是否已解鎖
    public bool isGrounded = true;   // 是否在地面上

    [Header("【控制反轉設定 (Controls Inversion)】")]
    [Tooltip("按鍵 T 可在運行中即時切換。勾選時右搖桿/方向鍵輸入反轉")]
    public bool isInverted = true; // 🌟【已修改】預設反轉開關改為 true

    [Header("【核心動力與定高 (Core Physics)】")]
    [SerializeField] private float maxThrust = 40f;
    [SerializeField] private float altitudeHoldStrength = 5f;
    [SerializeField] private float horizontalSpeedBoost = 10f;
    [Min(0.1f)]
    [SerializeField] private float baseMass = 1f;
    [Min(0f)]
    [SerializeField] private float payloadMass;

    [Header("【姿態控制 (Attitude Control)】")]
    [SerializeField] private float cruisePitchAngle = -12f;
    [SerializeField] private float maxTiltAngle = 20f;
    [SerializeField] private float maxYawRate = 200f;
    [SerializeField] private float stabilizerStrength = 15f;
    [Tooltip("姿態角速度阻尼 (D 項)，越大越不會過衝")]
    [SerializeField] private float attitudeDamping = 0.8f;
    [Tooltip("姿態控制整體增益 (原版程式重複施加兩次扭力，等同 2 倍)")]
    [SerializeField] private float attitudeGain = 2f;

    [Header("【操控手感 (Handling Feel)】")]
    [Tooltip("搖桿 Expo 曲線 (0 = 線性，越大中心越細膩)")]
    [Range(0f, 1f)]
    [SerializeField] private float stickExpo = 0.3f;
    [Tooltip("搖桿輸入平滑速度 (每秒)，讓鍵盤數位輸入也有類比感")]
    [SerializeField] private float stickResponse = 8f;
    [Tooltip("偏航角加速度 (度/秒²)，避免瞬間轉向")]
    [SerializeField] private float yawAcceleration = 900f;
    [Tooltip("載重時姿態反應降低的比例 (0~1)")]
    [Range(0f, 1f)]
    [SerializeField] private float payloadAgilityLoss = 0.35f;
    [Tooltip("空中氣流擾動強度 (m/s²)，0 = 關閉")]
    [SerializeField] private float turbulenceStrength = 0.25f;

    [Header("【碰撞防吸附 (Anti-Stick)】")]
    [Tooltip("接觸牆面時往外推開的加速度 (m/s²)")]
    [SerializeField] private float wallPushOut = 6f;
    [Tooltip("撞牆時保留的反彈比例")]
    [Range(0f, 1f)]
    [SerializeField] private float wallBounce = 0.2f;
    [Tooltip("法線 Y 值大於此值的接觸視為地面/屋頂，不做防吸附處理")]
    [Range(0f, 1f)]
    [SerializeField] private float groundNormalThreshold = 0.6f;

    [Header("【模擬電池 (Simulated Battery)】")]
    [Tooltip("懸停狀態下電量從 100% 用到 0% 所需秒數")]
    [SerializeField] private float batteryHoverSeconds = 600f;

    [Header("【螺旋槳視覺效果 (Propeller Visuals)】")]
    [SerializeField] private GameObject[] propellers;
    [SerializeField] private float propIdleRPM = 800f;
    [SerializeField] private float propHoverRPM = 2800f;
    [SerializeField] private float propMaxRPM = 3500f;
    private float currentVisualRpm = 0f;
    // 新增：當玩家按下重生鍵時，對外廣播的事件
    public event System.Action OnRespawnPressed;

    public bool IsEngineStarted => isArmed;
    public float CurrentRPM => currentVisualRpm;

    [Header("【攝影機系統 (Single XR Origin)】")]
    [Tooltip("唯一的 XR Origin Transform，避免 VR 多 Origin 導致的輸入與渲染衝突")]
    [SerializeField] private Transform xrOriginTransform;

    [Tooltip("兩個視角的掛載空物件錨點 (例如 Element 0: FPV_CamPos, Element 1: TPV_CamPos)")]
    [SerializeField] private Transform[] cameraPositions;
    private int currentCameraIndex = 0;

    [Header("【防暈眩鏡頭 (Comfort Camera)】")]
    [Tooltip("第三人稱鏡頭只跟隨無人機航向，不跟著俯仰/橫滾傾斜")]
    [SerializeField] private bool stabilizeThirdPersonCamera = true;
    [Tooltip("第三人稱鏡頭航向跟隨速度，越小越柔和")]
    [SerializeField] private float tpvYawSharpness = 6f;
    [Tooltip("第三人稱鏡頭位置跟隨速度，越大越貼緊")]
    [SerializeField] private float tpvPositionSharpness = 25f;
    [Tooltip("第一人稱視角水平穩定程度 (0 = 真實 FPV 跟著傾斜，1 = 地平線完全鎖定)")]
    [Range(0f, 1f)]
    [SerializeField] private float fpvHorizonStabilization = 0f;

    private Transform tpvRig;
    private Vector3 tpvLocalOffset;
    private Quaternion tpvLocalRotation = Quaternion.identity;
    private float tpvSmoothedYaw;
    private bool tpvOffsetCaptured;

    [Header("【VR World Space UI 介面】")]
    [Tooltip("若使用跟隨無人機的 World Space Canvas，請拖入對應 TextMeshProUGUI 組件")]
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI altitudeText;
    [SerializeField] private TextMeshProUGUI headingText;
    [SerializeField] private TextMeshProUGUI positionText;
    [SerializeField] private TextMeshProUGUI speedText;

    private Vector3 lastPos;
    private float currentSpeed;

    [Header("【音效系統 (Audio Setup)】")]
    [SerializeField] private AudioSource engineAudioSource;
    [SerializeField] private AudioClip takeoffSound;
    [SerializeField] private AudioClip flyingLoopSound;
    [SerializeField] private AudioClip landingSound;
    [SerializeField] private float engineVolume = 0.8f;

    [Header("【重生與翻正 (Respawn & Flip)】")]
    //[SerializeField] private Vector3 respawnPosition = new Vector3(0f, 1.5f, 0f);
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private bool isFlipping = false;

    // 變數區新增：
    [Header("【HUD 引用 (選填)】")]
    [SerializeField] private VRHUDFollower hudFollower;

    [Header("【HUD UI 綁定】")]
    [SerializeField] private Transform hudCanvasTransform; // 將 Drone_HUD_Canvas 拖入這個欄位

    // --- 防吸附/防貼牆機制變數 ---
    private bool isTouchingWall = false;
    private Vector3 accumulatedWallNormal;
    private int wallContactCount;

    // --- 遙測數據 (供 HUD 與遊戲規則使用) ---
    private float lastThrustAcceleration;
    private float altitudeAboveGround = -1f;
    private float batteryPercent = 100f;
    private float currentYawRate;
    private float turbulenceSeed;

    /// <summary>撞擊事件，參數為相對撞擊速度 (m/s)</summary>
    public event System.Action<float> OnCrash;

    // --- 輸入系統 (Input Actions) ---
    private InputAction throttleAction;
    private InputAction yawAction;
    private InputAction pitchAction;
    private InputAction rollAction;
    private InputAction toggleModeAction;
    private InputAction switchCamAction;
    private InputAction respawnAction;
    private InputAction toggleInvertAction;

    private float stickThrottle, stickYaw, stickPitch, stickRoll;

    private Rigidbody rb;
    private float targetYawAngle;

    private float armTimer = 0f;
    private float disarmTimer = 0f;
    private bool wasGroundedLastFrame = true;
    public bool canControl = true;
    public bool IsFirstPerson => currentCameraIndex == 0;
    public float PayloadMass => payloadMass;

    // --- 遙測 (Telemetry) ---
    public FlightMode CurrentFlightMode => currentFlightMode;
    public string FlightModeLabel => currentFlightMode switch
    {
        FlightMode.Cruise => "CRUISE",
        FlightMode.Vertical => "VERT",
        _ => "ANGLE"
    };
    public Vector3 Velocity => rb != null ? rb.linearVelocity : Vector3.zero;
    public float GroundSpeed => new Vector2(Velocity.x, Velocity.z).magnitude;
    public float VerticalSpeed => Velocity.y;
    /// <summary>相對起飛點的高度 (m)</summary>
    public float RelativeAltitude => transform.position.y - initialPosition.y;
    /// <summary>離正下方地面/屋頂的距離 (m)，-1 表示超出量測範圍</summary>
    public float AltitudeAboveGround => altitudeAboveGround;
    /// <summary>目前推力佔最大推力的比例 (0~1)</summary>
    public float Throttle01 => maxThrust > 0f ? Mathf.Clamp01(lastThrustAcceleration / maxThrust) : 0f;
    public float BatteryPercent => batteryPercent;
    public Vector3 HomePosition => initialPosition;
    public bool IsTouchingObstacle => isTouchingWall;
    public float PitchAngle => NormalizeAngle(transform.eulerAngles.x);
    public float RollAngle => NormalizeAngle(transform.eulerAngles.z);
    public float Heading => YawOf(transform);
    /// <summary>第一人稱鏡頭錨點 (HUD 以此計算玩家實際看到的姿態)</summary>
    public Transform FirstPersonAnchor => cameraPositions != null && cameraPositions.Length > 0 && cameraPositions[0] != null
        ? cameraPositions[0]
        : transform;

    private void Awake()
    {
        // 油門 (左搖桿 Y 軸 / 鍵盤 W S)
        throttleAction = new InputAction("Throttle", binding: "<Gamepad>/leftStick/y");
        throttleAction.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/w").With("Negative", "<Keyboard>/s");

        // 航向 (左搖桿 X 軸 / 鍵盤 D A)
        yawAction = new InputAction("Yaw", binding: "<Gamepad>/leftStick/x");
        yawAction.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/d").With("Negative", "<Keyboard>/a");

        // 俯仰 (右搖桿 Y 軸 / 鍵盤 上 下)
        pitchAction = new InputAction("Pitch", binding: "<Gamepad>/rightStick/y");
        pitchAction.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/upArrow").With("Negative", "<Keyboard>/downArrow");

        // 橫滾 (右搖桿 X 軸 / 鍵盤 右 左)
        rollAction = new InputAction("Roll", binding: "<Gamepad>/rightStick/x");
        rollAction.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/rightArrow").With("Negative", "<Keyboard>/leftArrow");

        // 功能鍵綁定
        toggleModeAction = new InputAction("ToggleMode", binding: "<Keyboard>/f");
        toggleModeAction.AddBinding("<Gamepad>/rightShoulder");

        switchCamAction = new InputAction("SwitchCam", binding: "<Keyboard>/c");
        switchCamAction.AddBinding("<Gamepad>/leftShoulder");
        switchCamAction.AddBinding("<Gamepad>/buttonNorth");

        respawnAction = new InputAction("Respawn", binding: "<Keyboard>/r");
        respawnAction.AddBinding("<Gamepad>/buttonEast");

        toggleInvertAction = new InputAction("ToggleInvert", binding: "<Keyboard>/t");
    }

    private void OnEnable()
    {
        throttleAction.Enable(); yawAction.Enable();
        pitchAction.Enable(); rollAction.Enable();
        toggleModeAction.Enable(); switchCamAction.Enable();
        respawnAction.Enable(); toggleInvertAction.Enable();

        // 使用具名方法訂閱，避免 OnEnable 多次觸發時重複綁定
        toggleModeAction.performed += OnToggleModePerformed;
        switchCamAction.performed += OnSwitchCamPerformed;
        respawnAction.performed += OnRespawnPerformed;
        toggleInvertAction.performed += OnToggleInvertPerformed;
    }

    private void OnToggleModePerformed(InputAction.CallbackContext _) { if (canControl) ToggleFlightMode(); }
    private void OnSwitchCamPerformed(InputAction.CallbackContext _) => SwitchCamera();
    private void OnRespawnPerformed(InputAction.CallbackContext _) { if (canControl) Respawn(); }
    private void OnToggleInvertPerformed(InputAction.CallbackContext _) => ToggleInvertControls();

    private void OnDisable()
    {
        toggleModeAction.performed -= OnToggleModePerformed;
        switchCamAction.performed -= OnSwitchCamPerformed;
        respawnAction.performed -= OnRespawnPerformed;
        toggleInvertAction.performed -= OnToggleInvertPerformed;

        throttleAction.Disable(); yawAction.Disable();
        pitchAction.Disable(); rollAction.Disable();
        toggleModeAction.Disable(); switchCamAction.Disable();
        respawnAction.Disable(); toggleInvertAction.Disable();
    }

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();

        // 強制將重心鎖定在正中心，避免大推力轉化為自轉力矩
        rb.centerOfMass = Vector3.zero;

        rb.mass = baseMass + payloadMass;
        rb.linearDamping = 1.0f;
        rb.angularDamping = 6.0f;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // 自動生成零摩擦力 + 微彈性物理材質
        ApplyFrictionlessMaterial();

        if (engineAudioSource != null)
        {
            engineAudioSource.spatialBlend = 1f;
            engineAudioSource.loop = true;
            engineAudioSource.volume = engineVolume;
            engineAudioSource.pitch = 1.0f;
        }

        FindPropellers();
        EnsureCameraPositions();
        CaptureThirdPersonOffset();
        InitializeCameras();

        // 在 Start() 最下面加入這兩行，記錄剛出生時的真實座標
        initialPosition = transform.position;
        initialRotation = transform.rotation;

        targetYawAngle = transform.eulerAngles.y;
        lastPos = transform.position;
        turbulenceSeed = Random.Range(0f, 1000f);

        if (hudFollower == null)
        {
            hudFollower = GetComponentInChildren<VRHUDFollower>(true);
        }
    }

    /// <summary>
    /// 為無人機所有碰撞器賦予零摩擦材質，防止與牆面產生靜摩擦力吸附
    /// </summary>
    private void ApplyFrictionlessMaterial()
    {
        PhysicsMaterial zeroFrictionMat = new PhysicsMaterial("DroneFrictionless");
        zeroFrictionMat.dynamicFriction = 0f;
        zeroFrictionMat.staticFriction = 0f;
        zeroFrictionMat.frictionCombine = PhysicsMaterialCombine.Minimum;
        zeroFrictionMat.bounciness = 0.25f; // 微彈性，碰撞時自動自然彈開
        zeroFrictionMat.bounceCombine = PhysicsMaterialCombine.Maximum;

        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
        {
            col.material = zeroFrictionMat;
        }
    }

    private void Update()
    {
        if (isFlipping) return;

        UpdateGroundedState();
        UpdateAltitudeAboveGround();

        if (!canControl)
        {
            stickThrottle = 0f;
            stickYaw = 0f;
            stickPitch = 0f;
            stickRoll = 0f;
        }
        else
        {
            // 數位/類比輸入統一經過 Expo 曲線與平滑，讓鍵盤操作也有細膩手感
            float dt = Time.deltaTime;
            stickThrottle = SmoothStick(stickThrottle, throttleAction.ReadValue<float>(), 0f, dt);
            stickYaw = SmoothStick(stickYaw, yawAction.ReadValue<float>(), stickExpo, dt);
            stickPitch = SmoothStick(stickPitch, pitchAction.ReadValue<float>(), stickExpo, dt);
            stickRoll = SmoothStick(stickRoll, rollAction.ReadValue<float>(), stickExpo, dt);

            HandleArmingState();
        }

        UpdatePropellersVisual();
        UpdateAudio();
        UpdateBattery();
        UpdateHUDData(); // 【新增】即時更新 World Space Canvas 的飛行數據

        wasGroundedLastFrame = isGrounded;
    }

    private float SmoothStick(float current, float raw, float expo, float dt)
    {
        raw = Mathf.Clamp(raw, -1f, 1f);
        float shaped = raw * (1f - expo) + raw * raw * raw * expo;
        return Mathf.MoveTowards(current, shaped, stickResponse * dt);
    }

    private void FixedUpdate()
    {
        if (isFlipping) return;
        CheckAutoFlip();

        isTouchingWall = wallContactCount > 0;

        if (isArmed)
        {
            ApplyFlightPhysics();
        }
        else
        {
            lastThrustAcceleration = 0f;
        }

        if (isTouchingWall)
        {
            ResolveWallContact();
        }

        // 接觸資料由下一次物理步驟的 OnCollisionStay 重新收集
        accumulatedWallNormal = Vector3.zero;
        wallContactCount = 0;
    }

    private void LateUpdate()
    {
        if (xrOriginTransform == null)
        {
            return;
        }

        if (!IsFirstPerson)
        {
            UpdateStabilizedThirdPersonRig(Time.deltaTime);
            return;
        }

        xrOriginTransform.localRotation = GetFirstPersonStabilization();

        Camera trackedCamera = xrOriginTransform.GetComponentInChildren<Camera>(true);
        if (trackedCamera == null)
        {
            return;
        }

        if (trackedCamera.transform.parent != null)
        {
            trackedCamera.transform.parent.localRotation = Quaternion.identity;
        }

        trackedCamera.transform.localRotation = Quaternion.identity;
    }

    private void OnDestroy()
    {
        if (tpvRig != null)
        {
            Destroy(tpvRig.gameObject);
        }
    }

    /// <summary>
    /// 第一人稱的地平線穩定：依比例抵銷機身的俯仰與橫滾 (0 = 不抵銷)
    /// </summary>
    private Quaternion GetFirstPersonStabilization()
    {
        if (fpvHorizonStabilization <= 0f || cameraPositions == null || cameraPositions.Length == 0 || cameraPositions[0] == null)
        {
            return Quaternion.identity;
        }

        Transform anchor = cameraPositions[0];
        Quaternion level = Quaternion.Euler(0f, YawOf(anchor), 0f);
        Quaternion toLevel = Quaternion.Inverse(anchor.rotation) * level;
        return Quaternion.Slerp(Quaternion.identity, toLevel, fpvHorizonStabilization);
    }

    /// <summary>
    /// 記錄第三人稱錨點相對於無人機「航向」的偏移，之後鏡頭只跟隨航向，不跟隨俯仰/橫滾
    /// </summary>
    private void CaptureThirdPersonOffset()
    {
        if (cameraPositions == null || cameraPositions.Length < 2 || cameraPositions[1] == null)
        {
            return;
        }

        Transform anchor = cameraPositions[1];
        float droneYaw = YawOf(transform);
        Quaternion inverseYaw = Quaternion.Inverse(Quaternion.Euler(0f, droneYaw, 0f));

        tpvLocalOffset = inverseYaw * (anchor.position - transform.position);

        // 只保留錨點本身的俯角與相對航向，去除生成當下機身可能的橫滾
        Vector3 localForward = inverseYaw * anchor.forward;
        tpvLocalRotation = Quaternion.LookRotation(localForward, Vector3.up);
        tpvSmoothedYaw = droneYaw;
        tpvOffsetCaptured = true;
    }

    private Transform GetOrCreateThirdPersonRig()
    {
        if (tpvRig == null)
        {
            tpvRig = new GameObject($"{name}_TPV_StabilizedRig").transform;
            tpvSmoothedYaw = YawOf(transform);
            UpdateStabilizedThirdPersonRig(0f);
        }

        return tpvRig;
    }

    private void UpdateStabilizedThirdPersonRig(float dt)
    {
        if (tpvRig == null || !tpvOffsetCaptured)
        {
            return;
        }

        float yawBlend = dt > 0f ? 1f - Mathf.Exp(-tpvYawSharpness * dt) : 1f;
        tpvSmoothedYaw = Mathf.LerpAngle(tpvSmoothedYaw, YawOf(transform), yawBlend);

        Quaternion yawRotation = Quaternion.Euler(0f, tpvSmoothedYaw, 0f);
        Vector3 targetPosition = transform.position + yawRotation * tpvLocalOffset;

        float positionBlend = dt > 0f ? 1f - Mathf.Exp(-tpvPositionSharpness * dt) : 1f;
        tpvRig.position = Vector3.Lerp(tpvRig.position, targetPosition, positionBlend);
        tpvRig.rotation = yawRotation * tpvLocalRotation;
    }

    // 碰撞防吸附判斷：收集非地面接觸的法線，於 FixedUpdate 中統一處理
    private void OnCollisionStay(Collision collision)
    {
        if (IsOwnCollider(collision.collider)) return;

        int count = collision.contactCount;
        for (int i = 0; i < count; i++)
        {
            Vector3 normal = collision.GetContact(i).normal;
            if (normal.y > groundNormalThreshold) continue; // 地面/屋頂，正常降落

            accumulatedWallNormal += normal;
            wallContactCount++;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (IsOwnCollider(collision.collider)) return;

        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed > 1.5f)
        {
            OnCrash?.Invoke(impactSpeed);
            Vector3 impactPoint = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            GameAudio.PlayAt(GameSfx.Crash, impactPoint, Mathf.Clamp01(impactSpeed / 10f));
        }

        OnCollisionStay(collision);
    }

    private bool IsOwnCollider(Collider other)
    {
        return other == null || other.transform.IsChildOf(transform) || other.attachedRigidbody == rb;
    }

    /// <summary>
    /// 防吸附核心：移除朝牆內的速度分量、施加外推力並抑制碰撞造成的亂轉
    /// </summary>
    private void ResolveWallContact()
    {
        if (accumulatedWallNormal.sqrMagnitude < 0.0001f) return;

        Vector3 wallNormal = accumulatedWallNormal.normalized;
        Vector3 velocity = rb.linearVelocity;
        float intoWall = Vector3.Dot(velocity, wallNormal);
        if (intoWall < 0f)
        {
            velocity -= wallNormal * intoWall * (1f + wallBounce);
            rb.linearVelocity = velocity;
        }

        rb.AddForce(wallNormal * wallPushOut, ForceMode.Acceleration);
        rb.angularVelocity *= 0.85f;
    }

    private void UpdateGroundedState()
    {
        bool hitGround = false;
        Ray ray = new Ray(transform.position + Vector3.up * 0.1f, Vector3.down);
        RaycastHit[] hits = Physics.RaycastAll(ray, 0.35f, ~0, QueryTriggerInteraction.Ignore);

        foreach (var h in hits)
        {
            if (!h.collider.transform.IsChildOf(transform) && h.collider.gameObject != gameObject)
            {
                hitGround = true;
                break;
            }
        }
        isGrounded = hitGround;
    }

    private void UpdateAltitudeAboveGround()
    {
        altitudeAboveGround = -1f;
        RaycastHit[] hits = Physics.RaycastAll(transform.position, Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore);
        float closest = float.PositiveInfinity;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform) || hit.distance >= closest) continue;
            closest = hit.distance;
        }

        if (!float.IsPositiveInfinity(closest))
        {
            altitudeAboveGround = closest;
        }
    }

    private void UpdateBattery()
    {
        if (!isArmed || batteryHoverSeconds <= 0f) return;

        // 懸停約耗用 40% 推力，耗電與推力成正比
        float hoverRatio = maxThrust > 0f ? Mathf.Abs(Physics.gravity.y) / maxThrust : 0.25f;
        float load = isGrounded ? 0.1f : Mathf.Max(Throttle01, 0.05f) / Mathf.Max(hoverRatio, 0.05f);
        batteryPercent = Mathf.Max(0f, batteryPercent - load * 100f / batteryHoverSeconds * Time.deltaTime);
    }

    public void RechargeBattery()
    {
        batteryPercent = 100f;
    }

    private static float YawOf(Transform target)
    {
        Vector3 flatForward = Vector3.ProjectOnPlane(target.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.0001f)
        {
            // 機頭垂直朝上/下時，改用機身上方向推算航向
            flatForward = Vector3.ProjectOnPlane(-target.up * Mathf.Sign(target.forward.y), Vector3.up);
        }
        return Quaternion.LookRotation(flatForward.normalized, Vector3.up).eulerAngles.y;
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        return angle > 180f ? angle - 360f : angle;
    }

    private void HandleArmingState()
    {
        if (!isArmed)
        {
            if (isGrounded && stickThrottle > 0.8f)
            {
                armTimer += Time.deltaTime;
                if (armTimer > 0.8f)
                {
                    isArmed = true;
                    armTimer = 0f;
                    targetYawAngle = transform.eulerAngles.y;
                    Debug.Log("無人機引擎已啟動！");
                }
            }
            else
            {
                armTimer = 0f;
            }
        }
        else
        {
            if (isGrounded && stickThrottle < -0.8f)
            {
                disarmTimer += Time.deltaTime;
                if (disarmTimer > 0.8f)
                {
                    isArmed = false;
                    disarmTimer = 0f;
                    currentFlightMode = FlightMode.Stabilized;
                    Debug.Log("無人機引擎已熄火！");
                }
            }
            else
            {
                disarmTimer = 0f;
            }
        }
    }

    private void ApplyFlightPhysics()
    {
        float fixedDt = Time.fixedDeltaTime;
        currentYawRate = Mathf.MoveTowards(currentYawRate, stickYaw * maxYawRate, yawAcceleration * fixedDt);
        targetYawAngle += currentYawRate * fixedDt;
        float payloadSpeedMultiplier = 1f / (1f + payloadMass / Mathf.Max(baseMass, 0.1f));
        float agility = Mathf.Lerp(1f, payloadSpeedMultiplier, payloadAgilityLoss);

        float invertMultiplier = isInverted ? -1f : 1f;

        float targetPitchAngle = (stickPitch * maxTiltAngle) * invertMultiplier;
        float targetRollAngle = (-stickRoll * maxTiltAngle) * invertMultiplier;

        if (currentFlightMode == FlightMode.Cruise)
        {
            targetPitchAngle = cruisePitchAngle;
            targetRollAngle = 0f;
        }
        else if (currentFlightMode == FlightMode.Vertical)
        {
            targetPitchAngle = 0f;
            targetRollAngle = 0f;
        }

        Quaternion targetRotation = Quaternion.Euler(targetPitchAngle, targetYawAngle, targetRollAngle);
        Quaternion errorRotation = targetRotation * Quaternion.Inverse(transform.rotation);
        errorRotation.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;

        // PD 姿態控制 (原本重複呼叫兩次 AddTorque，改由 attitudeGain = 2 表達相同強度)
        if (float.IsNaN(axis.x) || axis.sqrMagnitude < 0.0001f)
        {
            axis = Vector3.zero;
        }
        Vector3 correctionTorque = (axis.normalized * (angle * stabilizerStrength * agility * Mathf.Deg2Rad)) - (rb.angularVelocity * attitudeDamping);
        rb.AddTorque(correctionTorque * attitudeGain, ForceMode.Acceleration);

        float gravityForce = Mathf.Abs(Physics.gravity.y);
        float tiltAngle = Vector3.Angle(Vector3.up, transform.up);
        float tiltCompensation = 1f / Mathf.Max(Mathf.Cos(tiltAngle * Mathf.Deg2Rad), 0.3f);
        float hoverThrust = gravityForce * tiltCompensation;

        float finalThrust = 0f;

        if (Mathf.Abs(stickThrottle) > 0.05f)
        {
            if (stickThrottle > 0)
                finalThrust = Mathf.Lerp(hoverThrust, maxThrust, stickThrottle) * payloadSpeedMultiplier;
            else
                finalThrust = Mathf.Lerp(hoverThrust, 0f, -stickThrottle);
        }
        else
        {
            if (!isGrounded)
            {
                float verticalVelocity = rb.linearVelocity.y;
                finalThrust = hoverThrust - (verticalVelocity * altitudeHoldStrength);
            }
        }

        lastThrustAcceleration = finalThrust;

        Vector3 horizontalDirection = new Vector3(transform.up.x, 0, transform.up.z);
        Vector3 propulsion = transform.up * finalThrust
                             + horizontalDirection * horizontalSpeedBoost * payloadSpeedMultiplier;

        // 撞牆時只移除「朝牆內」的推力分量，仍可沿牆滑開或後退脫離，不會被推力壓在牆上
        if (isTouchingWall && accumulatedWallNormal.sqrMagnitude > 0.0001f)
        {
            Vector3 wallNormal = accumulatedWallNormal.normalized;
            float intoWall = Vector3.Dot(propulsion, wallNormal);
            if (intoWall < 0f)
            {
                propulsion -= wallNormal * intoWall;
            }
        }

        rb.AddForce(propulsion, ForceMode.Acceleration);

        // 天氣風力 (晝夜天氣系統；晴天為零)
        if (!isGrounded)
        {
            rb.AddForce(EnvironmentController.Wind * payloadSpeedMultiplier, ForceMode.Acceleration);
        }

        if (!isGrounded && turbulenceStrength > 0f)
        {
            float t = Time.time * 0.6f;
            Vector3 gust = new Vector3(
                Mathf.PerlinNoise(turbulenceSeed, t) - 0.5f,
                (Mathf.PerlinNoise(turbulenceSeed + 31f, t) - 0.5f) * 0.4f,
                Mathf.PerlinNoise(turbulenceSeed + 67f, t) - 0.5f);
            rb.AddForce(gust * (2f * turbulenceStrength), ForceMode.Acceleration);
        }
    }

    private void ToggleInvertControls()
    {
        isInverted = !isInverted;
        Debug.Log($"【DroneController】右搖桿/方向鍵輸入已切換 | 當前反轉狀態 (isInverted): {isInverted}");
    }

    private void UpdateAudio()
    {
        if (engineAudioSource == null) return;

        // 引擎音高隨螺旋槳轉速變化，增加臨場感
        float rpm01 = propMaxRPM > 0f ? Mathf.Clamp01(currentVisualRpm / propMaxRPM) : 0f;
        engineAudioSource.pitch = Mathf.Lerp(0.8f, 1.3f, rpm01);

        if (!isArmed)
        {
            if (engineAudioSource.isPlaying) engineAudioSource.Stop();
            return;
        }

        if (!engineAudioSource.isPlaying && flyingLoopSound != null)
        {
            engineAudioSource.clip = flyingLoopSound;
            engineAudioSource.Play();
        }

        // 只在「離地的那一幀」播放起飛音效 (原條件在爬升期間每幀都成立)
        if (wasGroundedLastFrame && !isGrounded && rb.linearVelocity.y > 0.1f)
        {
            if (takeoffSound) engineAudioSource.PlayOneShot(takeoffSound, engineVolume);
        }

        if (!wasGroundedLastFrame && isGrounded && rb.linearVelocity.y < -0.5f)
        {
            if (landingSound) engineAudioSource.PlayOneShot(landingSound, engineVolume);
        }
    }

    private void UpdatePropellersVisual()
    {
        float targetRpm = 0f;
        if (isArmed)
        {
            if (isGrounded)
            {
                targetRpm = propIdleRPM;
            }
            else
            {
                if (stickThrottle > 0)
                    targetRpm = Mathf.Lerp(propHoverRPM, propMaxRPM, stickThrottle);
                else if (stickThrottle < 0)
                    targetRpm = Mathf.Lerp(propHoverRPM, propIdleRPM, -stickThrottle);
                else
                    targetRpm = propHoverRPM;
            }
        }

        currentVisualRpm = Mathf.Lerp(currentVisualRpm, targetRpm, Time.deltaTime * 8f);
        float[] directions = { 1f, -1f, -1f, 1f };

        for (int i = 0; i < propellers.Length; i++)
        {
            if (propellers[i] != null)
            {
                Vector3 rotationAxis = Vector3.forward;
                propellers[i].transform.Rotate(rotationAxis * currentVisualRpm * directions[i] * Time.deltaTime);
            }
        }
    }

    /// <summary>
    /// 【修改】將單一 XR Origin 的 Transform 綁定/移動至指定的鏡頭空物件錨點
    /// </summary>
    private void UpdateCameraPosition()
    {
        if (xrOriginTransform == null || cameraPositions == null || cameraPositions.Length <= currentCameraIndex) return;

        Transform targetAnchor = cameraPositions[currentCameraIndex];
        if (targetAnchor == null) return;

        // 若 XR Origin 尚未設置為錨點的子物件，自動進行父子繫結
        if (xrOriginTransform.parent != targetAnchor)
        {
            xrOriginTransform.SetParent(targetAnchor);
            xrOriginTransform.localPosition = Vector3.zero;
            xrOriginTransform.localRotation = Quaternion.identity;
        }

    }

    public void SetPayloadMass(float mass)
    {
        payloadMass = Mathf.Max(0f, mass);
        if (rb != null)
        {
            rb.mass = baseMass + payloadMass;
        }
    }

    public void ClearPayloadMass()
    {
        SetPayloadMass(0f);
    }

    private void ToggleFlightMode()
    {
        currentFlightMode = (FlightMode)(((int)currentFlightMode + 1) % 3);
        targetYawAngle = transform.eulerAngles.y;
    }

    // 3. 修改 SwitchCamera() 方法，讓切換鏡頭時自動刷新 HUD 的目標：
    private void SwitchCamera()
    {
        if (cameraPositions == null || cameraPositions.Length <= 1 || xrOriginTransform == null) return;

        currentCameraIndex = (currentCameraIndex + 1) % cameraPositions.Length;

        Transform targetPos = cameraPositions[currentCameraIndex];
        if (targetPos != null)
        {
            // 第三人稱改掛在只跟隨航向的穩定支架上，避免鏡頭隨機身傾斜造成暈眩
            if (currentCameraIndex != 0 && stabilizeThirdPersonCamera && tpvOffsetCaptured)
            {
                targetPos = GetOrCreateThirdPersonRig();
            }

            // 1. 移動 XR Origin 到指定鏡頭位置
            xrOriginTransform.SetParent(targetPos);
            xrOriginTransform.localPosition = Vector3.zero;
            xrOriginTransform.localRotation = Quaternion.identity;
            SetCameraTrackingEnabled(currentCameraIndex != 0);

            // 2. 切換鏡頭時，將 HUD Canvas 移到新 Main Camera 底下，第二個參數傳 true (worldPositionStays = true)
            // 這樣能保證它掛過去後，依然維持與攝影機當前的相對擺設數值
            if (hudCanvasTransform != null && Camera.main != null)
            {
                hudCanvasTransform.SetParent(Camera.main.transform, true);
            }
        }

        Debug.Log($"[DroneController] 已成功切換至視角 {currentCameraIndex + 1}");
    }

    private void Respawn()
    {
        // 🌟 方式 A：最乾淨、最徹底的重置方法 —— 直接重新載入當前場景
        // 這會讓 CourseBuilder 重新執行 Start()，重新隨機生成整條賽道與樹木，並將無人機擺回最乾淨的初始狀態！
        //SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

        if (OnRespawnPressed != null)
        {
            OnRespawnPressed.Invoke();
        }
        else
        {
            // 如果沒有 RaceManager 監聽（例如你在沒賽道的測試場景），才退回備用位置
            RespawnAtCheckpoint(initialPosition, initialRotation);
        }

    }

    private void CheckAutoFlip()
    {
        if (isArmed || isFlipping) return;

        bool upsideDown = Vector3.Dot(transform.up, Vector3.up) < 0f;
        if (upsideDown && isGrounded)
        {
            transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
            transform.position += Vector3.up * 0.2f;
        }
    }

    private void FindPropellers()
    {
        propellers = new GameObject[4];
        string[] rotorNames = { "rotor_left_back", "rotor_left_front", "rotor_right_back", "rotor_right_front" };
        Transform[] modelParts = GetComponentsInChildren<Transform>(true);

        // Imported FPV models expose the actual propeller meshes as named rotor objects.
        int rotorIndex = 0;
        foreach (string rotorName in rotorNames)
        {
            foreach (Transform modelPart in modelParts)
            {
                if (modelPart.name != rotorName)
                {
                    continue;
                }

                propellers[rotorIndex++] = modelPart.gameObject;
                break;
            }
        }

        if (rotorIndex == propellers.Length)
        {
            return;
        }

        string[] names = { "FL_Motor_Parent", "FR_Motor_Parent", "RL_Motor_Parent", "RR_Motor_Parent" };
        for (int i = 0; i < names.Length; i++)
        {
            Transform m = transform.Find("Racing Drone Merged/" + names[i]);
            if (m != null) propellers[i] = m.Find("Prop")?.gameObject;
        }

        if (propellers[0] == null && propellers[1] == null && propellers[2] == null && propellers[3] == null)
        {
            int propellerIndex = 0;
            foreach (Transform modelPart in modelParts)
            {
                if (modelPart == transform || !modelPart.name.ToLowerInvariant().Contains("prop"))
                {
                    continue;
                }

                propellers[propellerIndex++] = modelPart.gameObject;
                if (propellerIndex == propellers.Length)
                {
                    break;
                }
            }
        }
    }

    private void EnsureCameraPositions()
    {
        if (cameraPositions == null || cameraPositions.Length < 2)
        {
            cameraPositions = new Transform[2];
        }

        cameraPositions[0] = FindDeepChild(transform, "FPV_CamPos");
        cameraPositions[1] = FindDeepChild(transform, "TPV_CamPos");

        Transform bombModel = FindDeepChild(transform, "FPV_Bomb03");
        if (bombModel == null)
        {
            bombModel = FindDeepChild(transform, "FPV_Bomb02");
        }
        if (bombModel == null)
        {
            return;
        }

        if (cameraPositions[0] == null)
        {
            cameraPositions[0] = CreateCameraAnchor(
                bombModel,
                "FPV_CamPos",
                new Vector3(0f, 0.34f, -0.24f),
                Quaternion.identity);
        }

        if (cameraPositions[1] == null)
        {
            cameraPositions[1] = CreateCameraAnchor(
                bombModel,
                "TPV_CamPos",
                new Vector3(0f, 0.62f, 0.95f),
                Quaternion.identity);
        }
    }

    private static Transform CreateCameraAnchor(
        Transform parent,
        string anchorName,
        Vector3 localPosition,
        Quaternion localRotation)
    {
        GameObject anchorObject = new GameObject(anchorName);
        Transform anchor = anchorObject.transform;
        anchor.SetParent(parent, false);
        anchor.localPosition = localPosition;
        anchor.localRotation = localRotation;
        return anchor;
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

    /// <summary>
    /// 【修改】初始化攝影機系統：將唯一的 XR Origin 置於第 1 個預設視角位置 (Element 0)
    /// </summary>
    private void InitializeCameras()
    {
        if (xrOriginTransform != null && cameraPositions != null && cameraPositions.Length > 0 && cameraPositions[0] != null)
        {
            currentCameraIndex = 0;
            xrOriginTransform.SetParent(cameraPositions[0]);
            xrOriginTransform.localPosition = Vector3.zero;
            xrOriginTransform.localRotation = Quaternion.identity;
            SetCameraTrackingEnabled(false);
        }
    }

    private void SetCameraTrackingEnabled(bool enabled)
    {
        if (xrOriginTransform == null)
        {
            return;
        }

        Camera trackedCamera = xrOriginTransform.GetComponentInChildren<Camera>(true);
        if (trackedCamera == null)
        {
            return;
        }

        TrackedPoseDriver poseDriver = trackedCamera.GetComponent<TrackedPoseDriver>();
        if (poseDriver != null)
        {
            poseDriver.enabled = enabled;
        }

        if (!enabled)
        {
            trackedCamera.transform.localRotation = Quaternion.identity;
        }
    }

    /// <summary>
    /// 【新增】刷新 World Space UI 上的動態數據 (替代舊有 Screen-Space OnGUI)
    /// </summary>
    private void UpdateHUDData()
    {
        // 計算當前速度 (m/s)
        currentSpeed = (transform.position - lastPos).magnitude / Time.deltaTime;
        lastPos = transform.position;

        float altitude = Mathf.Max(0f, transform.position.y - 1.5f);
        bool isFlying = isArmed && !isGrounded;
        Vector3 pos = transform.position;
        float yaw = transform.eulerAngles.y;

        if (statusText != null)
        {
            statusText.text = $"Status:    {(isFlying ? "FLYING" : "GROUNDED")}";
            statusText.color = isFlying ? Color.green : Color.red;
        }
        if (altitudeText != null) altitudeText.text = $"Altitude:  {altitude:F1} m";
        if (headingText != null) headingText.text = $"Heading:   {yaw:F0}°";
        if (positionText != null) positionText.text = $"Position:  X:{pos.x:F1}  Z:{pos.z:F1}";
        if (speedText != null) speedText.text = $"Speed:     {currentSpeed:F1} m/s";
    }
    // 新增給 RaceManager 呼叫的專用重生函式
    public void RespawnAtCheckpoint(Vector3 newPos, Quaternion newRot)
    {
        isArmed = true; 
        currentFlightMode = FlightMode.Stabilized;
        
        // ！！關鍵修復：只保留 Y 軸方向 (Yaw)，強制讓機身水平 (X=0, Z=0)
        float flatYaw = newRot.eulerAngles.y+180f;
        Quaternion flatRotation = Quaternion.Euler(0, flatYaw, 0);
        
        // 對 Rigidbody 傳送，套用過濾後的水平旋轉
        rb.position = newPos;
        rb.rotation = flatRotation;
        transform.position = newPos;
        transform.rotation = flatRotation;
        Physics.SyncTransforms();
        
        // 清除殘留的物理速度
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // 重置內部輸入數值
        stickPitch = 0f;
        stickRoll = 0f;
        stickYaw = 0f;
        currentYawRate = 0f;
        accumulatedWallNormal = Vector3.zero;
        wallContactCount = 0;
        isTouchingWall = false;

        // 對齊物理目標方向
        targetYawAngle = flatYaw;
        tpvSmoothedYaw = flatYaw;
        if (tpvRig != null) UpdateStabilizedThirdPersonRig(0f);
    }
}
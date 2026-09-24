using UnityEngine;
using UnityEngine.InputSystem;
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

    [Header("【姿態控制 (Attitude Control)】")]
    [SerializeField] private float cruisePitchAngle = -12f;
    [SerializeField] private float maxTiltAngle = 40f;
    [SerializeField] private float maxYawRate = 200f;
    [SerializeField] private float stabilizerStrength = 15f;

    [Header("【螺旋槳視覺效果 (Propeller Visuals)】")]
    [SerializeField] private GameObject[] propellers;
    [SerializeField] private float propIdleRPM = 800f;
    [SerializeField] private float propHoverRPM = 2800f;
    [SerializeField] private float propMaxRPM = 3500f;
    private float currentVisualRpm = 0f;

    public bool IsEngineStarted => isArmed;
    public float CurrentRPM => currentVisualRpm;

    [Header("【攝影機系統 (Single XR Origin)】")]
    [Tooltip("唯一的 XR Origin Transform，避免 VR 多 Origin 導致的輸入與渲染衝突")]
    [SerializeField] private Transform xrOriginTransform;

    [Tooltip("兩個視角的掛載空物件錨點 (例如 Element 0: FPV_CamPos, Element 1: TPV_CamPos)")]
    [SerializeField] private Transform[] cameraPositions;
    private int currentCameraIndex = 0;

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
    [SerializeField] private Vector3 respawnPosition = new Vector3(0f, 1.5f, 0f);
    private bool isFlipping = false;

    // 變數區新增：
    [Header("【HUD 引用 (選填)】")]
    [SerializeField] private VRHUDFollower hudFollower;

    [Header("【HUD UI 綁定】")]
    [SerializeField] private Transform hudCanvasTransform; // 將 Drone_HUD_Canvas 拖入這個欄位

    // --- 防吸附/防貼牆機制變數 ---
    private bool isTouchingWall = false;

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

        toggleModeAction.performed += _ => ToggleFlightMode();
        switchCamAction.performed += _ => SwitchCamera();
        respawnAction.performed += _ => Respawn();
        toggleInvertAction.performed += _ => ToggleInvertControls();
    }

    private void OnDisable()
    {
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

        rb.mass = 1.0f;
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
        InitializeCameras();

        targetYawAngle = transform.eulerAngles.y;
        lastPos = transform.position;

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

        stickThrottle = throttleAction.ReadValue<float>();
        stickYaw = yawAction.ReadValue<float>();
        stickPitch = pitchAction.ReadValue<float>();
        stickRoll = rollAction.ReadValue<float>();

        UpdateGroundedState();
        HandleArmingState();
        UpdatePropellersVisual();
        UpdateAudio();
        UpdateHUDData(); // 【新增】即時更新 World Space Canvas 的飛行數據

        wasGroundedLastFrame = isGrounded;
    }

    private void FixedUpdate()
    {
        if (isFlipping) return;
        CheckAutoFlip();

        if (isArmed)
        {
            ApplyFlightPhysics();
        }

        // 定期平滑同步/維護攝影機相對位置 (若有平滑追蹤需求)
        UpdateCameraPosition();
    }

    // 碰撞防吸附判斷
    private void OnCollisionStay(Collision collision)
    {
        if (!isGrounded)
        {
            isTouchingWall = true;
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        isTouchingWall = false;
    }

    private void UpdateGroundedState()
    {
        bool hitGround = false;
        Ray ray = new Ray(transform.position + Vector3.up * 0.1f, Vector3.down);
        RaycastHit[] hits = Physics.RaycastAll(ray, 0.35f);

        foreach (var h in hits)
        {
            if (!h.collider.transform.IsChildOf(transform) && h.collider.gameObject != gameObject)
            {
                hitGround = true;
                break;
            }
        }
        isGrounded = hitGround;
        if (isGrounded) isTouchingWall = false;
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
        targetYawAngle += stickYaw * maxYawRate * Time.fixedDeltaTime;

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

        Vector3 correctionTorque = (axis.normalized * (angle * stabilizerStrength * Mathf.Deg2Rad)) - (rb.angularVelocity * 0.8f);
        rb.AddTorque(correctionTorque, ForceMode.Acceleration);
        rb.AddTorque(correctionTorque, ForceMode.Acceleration);

        float gravityForce = Mathf.Abs(Physics.gravity.y);
        float tiltAngle = Vector3.Angle(Vector3.up, transform.up);
        float tiltCompensation = 1f / Mathf.Max(Mathf.Cos(tiltAngle * Mathf.Deg2Rad), 0.3f);
        float hoverThrust = gravityForce * tiltCompensation;

        float finalThrust = 0f;

        if (Mathf.Abs(stickThrottle) > 0.05f)
        {
            if (stickThrottle > 0)
                finalThrust = Mathf.Lerp(hoverThrust, maxThrust, stickThrottle);
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

        rb.AddForce(transform.up * finalThrust, ForceMode.Acceleration);

        // 撞牆時停用水平推進力，防止強行擠壓貼牆
        if (!isTouchingWall)
        {
            Vector3 horizontalDirection = new Vector3(transform.up.x, 0, transform.up.z);
            rb.AddForce(horizontalDirection * horizontalSpeedBoost, ForceMode.Acceleration);
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

        engineAudioSource.pitch = 1.0f;

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

        if (!wasGroundedLastFrame && !isGrounded && rb.linearVelocity.y > 0.5f)
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
                propellers[i].transform.Rotate(Vector3.forward * currentVisualRpm * directions[i] * Time.deltaTime);
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
            // 1. 移動 XR Origin 到指定鏡頭位置
            xrOriginTransform.SetParent(targetPos);
            xrOriginTransform.localPosition = Vector3.zero;
            xrOriginTransform.localRotation = Quaternion.identity;

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
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

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
        string[] names = { "FL_Motor_Parent", "FR_Motor_Parent", "RL_Motor_Parent", "RR_Motor_Parent" };
        for (int i = 0; i < names.Length; i++)
        {
            Transform m = transform.Find("Racing Drone Merged/" + names[i]);
            if (m != null) propellers[i] = m.Find("Prop")?.gameObject;
        }
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
}
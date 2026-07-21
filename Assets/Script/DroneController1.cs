using UnityEngine;
using UnityEngine.InputSystem;

public class DroneController1 : MonoBehaviour
{
    private enum FlightMode
    {
        Manual,     // 0. 手動自平模式
        Cruise,     // 1. 定角前進模式 (按第一次 F)
        Vertical    // 2. 垂直無攻角模式 (按第二次 F)
    }

    [Header("Flight Characteristics")]
    [SerializeField] private float throttleStrength = 35f;
    [SerializeField] private float minThrottleForce = 8f;
    [SerializeField] private float throttleExpo = 0.5f;

    [Header("Rates (deg/s at full stick)")]
    [SerializeField] private float rcRate = 1.0f;
    [SerializeField] private float superRate = 0.7f;
    [SerializeField] private float expo = 0.3f;

    [SerializeField] private float maxPitchRate = 600f;
    [SerializeField] private float maxRollRate = 600f;
    [SerializeField] private float maxYawRate = 300f;

    [Header("Airmode")]
    [SerializeField] private bool airmodeEnabled = true;
    [SerializeField] private float airmodeStrength = 0.7f;

    [Header("Propellers")]
    [SerializeField] private GameObject[] propellers;
    [SerializeField] private float propMaxRPM = 3000f;
    [SerializeField] private float propIdleRPM = 600f; // 預設怠速 RPM
    [SerializeField] private float propRpmRampTime = 2.0f; // 從 0 加速到滿速所需的總時間 (秒)
    [SerializeField] private float propAccelSpeed = 3f;  // 槳葉加速平滑度

    [Header("Camera Setup")]
    [SerializeField] private Transform cameraTarget;
    [SerializeField] private float cameraSmoothTime = 0.04f;
    // 【可同時放入 一般 Camera 與 XR Origin】
    [SerializeField] private GameObject[] droneCameras;

    [Header("Respawn")]
    [SerializeField] private Vector3 respawnPosition = new Vector3(0f, 0.101f, 0f);

    [Header("Auto-Flip Recovery")]
    [SerializeField] private bool autoFlipEnabled = true;
    [SerializeField] private float flipDuration = 0.8f;
    [SerializeField] private float flipHeightThreshold = 0.101f;

    [Header("Easy Cruise Mode (定角巡航與自平)")]
    [SerializeField] private FlightMode currentFlightMode = FlightMode.Manual;
    [SerializeField] private float cruisePitchAngle = 12f;
    [SerializeField] private float maxTiltAngle = 40f; // 最大傾斜角度
    [SerializeField] private float stabilizerStrength = 15f;

    [Header("Audio Setup (引擎音效系統)")]
    [SerializeField] private AudioSource engineAudioSource;
    [SerializeField] private AudioClip takeoffSound;
    [SerializeField] private AudioClip flyingLoopSound;
    [SerializeField] private AudioClip landingSound;
    [Space(5)]
    [SerializeField] private float minEnginePitch = 0.65f;
    [SerializeField] private float maxEnginePitch = 1.6f;
    [SerializeField] private float minEngineVolume = 0.2f;
    [SerializeField] private float maxEngineVolume = 0.85f;

    // Input Actions
    private InputAction throttleAction;
    private InputAction pitchAction;
    private InputAction rollAction;
    private InputAction yawAction;
    private InputAction respawnAction;
    private InputAction switchCameraAction;
    private InputAction cruiseToggleAction;

    private float throttleInput;
    private float pitchInput;
    private float rollInput;
    private float yawInput;

    private Rigidbody rb;
    private Vector3 cameraVelocity;

    // States
    private bool isFlipping = false;
    private Quaternion flipStartRotation;
    private Quaternion flipTargetRotation;
    private float flipProgress = 0f;

    // 【修復】移除重複宣告的 currentCameraIndex
    private int currentCameraIndex = 0;
    private float targetYaw = 0f;

    // Audio status & Engine Ignition Lock
    private bool wasGrounded = true;
    private bool isGrounded = true;
    private bool isPlayingTakeoff = false;
    private bool enginesStarted = false; // 引擎啟動狀態
    private float disarmTimer = 0f;      // 熄火計時器

    // 轉速控制與公開屬性
    private float currentRpm = 0f;
    public float CurrentRPM => currentRpm;
    public bool IsEngineStarted => enginesStarted;

    /*
    private void Awake()
    {
        throttleAction = new InputAction("Throttle", binding: "<Gamepad>/leftStick/y");
        throttleAction.AddCompositeBinding("1DAxis")
            .With("Positive", "<Keyboard>/e")
            .With("Negative", "<Keyboard>/q");

        pitchAction = new InputAction("Pitch", binding: "<Gamepad>/rightStick/y");
        pitchAction.AddCompositeBinding("1DAxis")
            .With("Positive", "<Keyboard>/upArrow")
            .With("Negative", "<Keyboard>/downArrow");

        rollAction = new InputAction("Roll", binding: "<Gamepad>/rightStick/x");
        rollAction.AddCompositeBinding("1DAxis")
            .With("Positive", "<Keyboard>/rightArrow")
            .With("Negative", "<Keyboard>/leftArrow");

        yawAction = new InputAction("Yaw", binding: "<Gamepad>/leftStick/x");
        yawAction.AddCompositeBinding("1DAxis")
            .With("Positive", "<Keyboard>/d")
            .With("Negative", "<Keyboard>/a");

        respawnAction = new InputAction("Respawn", binding: "<Gamepad>/buttonEast");
        respawnAction.AddBinding("<Keyboard>/r");

        switchCameraAction = new InputAction("SwitchCamera", binding: "<Gamepad>/buttonNorth");
        switchCameraAction.AddBinding("<Keyboard>/c");

        cruiseToggleAction = new InputAction("ToggleCruise", binding: "<Gamepad>/leftShoulder");
        cruiseToggleAction.AddBinding("<Keyboard>/f");
    }
    */
    private void Awake()
    {
        // ==========================================
        // 1. 油門 (Throttle)：上升 (E) / 下降 (Q)
        // ==========================================
        throttleAction = new InputAction("Throttle");
        throttleAction.AddBinding("<Gamepad>/leftStick/y");
        throttleAction.AddBinding("<XRController>{LeftHand}/primary2DAxis/y");
        throttleAction.AddCompositeBinding("1DAxis")
            .With("Positive", "<Keyboard>/e")
            .With("Negative", "<Keyboard>/q");

        // ==========================================
        // 2. 航向/偏航 (Yaw)：右轉 (D) / 左轉 (A)
        // ==========================================
        yawAction = new InputAction("Yaw");
        yawAction.AddBinding("<Gamepad>/leftStick/x");
        yawAction.AddBinding("<XRController>{LeftHand}/primary2DAxis/x");
        yawAction.AddCompositeBinding("1DAxis")
            .With("Positive", "<Keyboard>/d")
            .With("Negative", "<Keyboard>/a");

        // ==========================================
        // 3. 俯仰/攻角 (Pitch)：前推/後拉
        // ==========================================
        pitchAction = new InputAction("Pitch");
        pitchAction.AddBinding("<Gamepad>/rightStick/y");
        pitchAction.AddBinding("<XRController>{RightHand}/primary2DAxis/y");
        pitchAction.AddCompositeBinding("1DAxis")
            .With("Positive", "<Keyboard>/upArrow")
            .With("Negative", "<Keyboard>/downArrow");

        // ==========================================
        // 4. 橫滾/攻角 (Roll)：右傾/左傾
        // ==========================================
        rollAction = new InputAction("Roll");
        rollAction.AddBinding("<Gamepad>/rightStick/x");
        rollAction.AddBinding("<XRController>{RightHand}/primary2DAxis/x");
        rollAction.AddCompositeBinding("1DAxis")
            .With("Positive", "<Keyboard>/rightArrow")
            .With("Negative", "<Keyboard>/leftArrow");

        // ==========================================
        // 5. 切換飛行模式 (Toggle Cruise)：F 鍵 / LB / VR primaryButton
        // ==========================================
        cruiseToggleAction = new InputAction("ToggleCruise");
        cruiseToggleAction.AddBinding("<Keyboard>/f");
        cruiseToggleAction.AddBinding("<Gamepad>/leftShoulder");
        cruiseToggleAction.AddBinding("<XRController>{RightHand}/primaryButton");

        // ==========================================
        // 6. 切換鏡頭 (Switch Camera)：C 鍵 / Y 鈕 / VR secondaryButton
        // ==========================================
        switchCameraAction = new InputAction("SwitchCamera");
        switchCameraAction.AddBinding("<Keyboard>/c");
        switchCameraAction.AddBinding("<Gamepad>/buttonNorth");
        switchCameraAction.AddBinding("<XRController>{RightHand}/secondaryButton");

        // ==========================================
        // 7. 重置重生 (Respawn)：R 鍵 / B 鈕 / VR primaryButton
        // ==========================================
        respawnAction = new InputAction("Respawn");
        respawnAction.AddBinding("<Keyboard>/r");
        respawnAction.AddBinding("<Gamepad>/buttonEast");
        respawnAction.AddBinding("<XRController>{LeftHand}/primaryButton");
    }

    private void OnEnable()
    {
        throttleAction.Enable();
        pitchAction.Enable();
        rollAction.Enable();
        yawAction.Enable();
        respawnAction.Enable();
        switchCameraAction.Enable();
        cruiseToggleAction.Enable();

        respawnAction.performed += OnRespawn;
        switchCameraAction.performed += OnSwitchCamera;
        cruiseToggleAction.performed += OnToggleCruise;
    }

    private void OnDisable()
    {
        throttleAction.Disable();
        pitchAction.Disable();
        rollAction.Disable();
        yawAction.Disable();
        respawnAction.Disable();
        switchCameraAction.Disable();
        cruiseToggleAction.Disable();

        respawnAction.performed -= OnRespawn;
        switchCameraAction.performed -= OnSwitchCamera;
        cruiseToggleAction.performed -= OnToggleCruise;
    }

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();

        rb.mass = 1.2f;
        rb.linearDamping = 0.5f;
        rb.angularDamping = 2.0f;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        AutoFindPropellers();
        if (cameraTarget == null)
        {
            Transform cam = transform.Find("Racing Drone Merged/Cam_Parent");
            if (cam != null) cameraTarget = cam;
        }

        InitializeCameras();
        targetYaw = transform.rotation.eulerAngles.y;

        if (engineAudioSource != null)
        {
            engineAudioSource.spatialBlend = 1.0f;
        }
    }

    private void Update()
    {
        if (!isFlipping)
        {
            throttleInput = throttleAction.ReadValue<float>();
            pitchInput = -pitchAction.ReadValue<float>();
            rollInput = -rollAction.ReadValue<float>();
            yawInput = yawAction.ReadValue<float>();
        }

        isGrounded = CheckIsGrounded();

        // 嚴格熄火判定：必須「引擎已啟動」+「確實著地」+「垂直速度極低」+「長按 Q 鍵 (-0.95以下)」
        bool isStrictlyOnGround = isGrounded && Mathf.Abs(rb.linearVelocity.y) < 0.2f;

        if (enginesStarted && isStrictlyOnGround && throttleInput < -0.95f)
        {
            disarmTimer += Time.deltaTime;
            if (disarmTimer >= 1.0f)
            {
                enginesStarted = false; // 熄火
                disarmTimer = 0f;
                currentFlightMode = FlightMode.Manual;
                if (engineAudioSource != null) engineAudioSource.Stop();
                Debug.Log("無人機已手動熄火關機，槳葉已停轉。");
            }
        }
        else
        {
            disarmTimer = 0f;
        }

        SpinPropellers();
        UpdateEngineAudio();
    }

    private void FixedUpdate()
    {
        if (autoFlipEnabled && !isFlipping && ShouldAutoFlip())
        {
            StartAutoFlip();
        }

        if (isFlipping)
        {
            UpdateAutoFlip();
        }
        else
        {
            ApplyThrottle();
            ApplyRotation();
            ApplyCustomDrag();
        }

        UpdateCameraFollow();
    }

    private void ApplyThrottle()
    {
        if (!enginesStarted)
        {
            if (throttleInput > 0.1f)
            {
                enginesStarted = true; // 按 E 點火
            }
            return;
        }

        float takeoffRpmThreshold = propMaxRPM * 0.40f;
        if (currentRpm < takeoffRpmThreshold)
        {
            return;
        }

        float rawStick = throttleInput;
        float deadzone = 0.05f;
        float stick = Mathf.Abs(rawStick) < deadzone ? 0f : rawStick;

        float force;
        if (stick > 0)
        {
            force = Mathf.Lerp(minThrottleForce, throttleStrength, stick);
        }
        else if (stick < 0)
        {
            force = Mathf.Lerp(minThrottleForce, 1.0f, -stick);
        }
        else
        {
            if (isGrounded)
            {
                force = 0f;
            }
            else
            {
                force = minThrottleForce;
            }
        }

        rb.AddForce(transform.up * force, ForceMode.Acceleration);

        if (rb.linearVelocity.y < -12f)
        {
            Vector3 vel = rb.linearVelocity;
            vel.y = -12f;
            rb.linearVelocity = vel;
        }
    }

    private void ApplyRotation()
    {
        if (!enginesStarted || currentRpm < propIdleRPM * 0.9f) return;

        targetYaw += yawInput * maxYawRate * Time.fixedDeltaTime;

        float targetPitch;
        float targetRoll;

        switch (currentFlightMode)
        {
            case FlightMode.Cruise:
                targetPitch = cruisePitchAngle;
                targetRoll = 0f;
                break;

            case FlightMode.Vertical:
                targetPitch = 0f;
                targetRoll = 0f;
                break;

            case FlightMode.Manual:
            default:
                targetPitch = pitchInput * maxTiltAngle;
                targetRoll = rollInput * maxTiltAngle;
                break;
        }

        Quaternion targetRotation = Quaternion.Euler(-targetPitch, targetYaw, -targetRoll);

        Quaternion deltaRotation = targetRotation * Quaternion.Inverse(transform.rotation);
        deltaRotation.ToAngleAxis(out float angle, out Vector3 axis);

        if (angle > 180f) angle -= 360f;

        Vector3 targetAngularVel = axis.normalized * (angle * Mathf.Deg2Rad * stabilizerStrength);

        Vector3 currentAngularVel = rb.angularVelocity;
        Vector3 torque = (targetAngularVel - currentAngularVel) * rb.mass;

        float throttleScale = airmodeEnabled ? 1f : Mathf.Clamp01((throttleInput + 1f) * 0.5f);
        throttleScale = Mathf.Lerp(airmodeStrength, 1f, throttleScale);
        torque *= throttleScale;

        rb.AddTorque(torque, ForceMode.Acceleration);
    }

    private void ApplyCustomDrag()
    {
        rb.linearVelocity *= 0.993f;
    }

    private void SpinPropellers()
    {
        float targetRpm = 0f;

        if (enginesStarted)
        {
            if (throttleInput > 0.05f)
            {
                targetRpm = propMaxRPM;
            }
            else if (throttleInput < -0.05f)
            {
                if (isGrounded)
                {
                    targetRpm = propIdleRPM;
                }
                else
                {
                    targetRpm = propMaxRPM * 0.50f;
                }
            }
            else
            {
                if (isGrounded)
                {
                    targetRpm = propIdleRPM;
                }
                else
                {
                    targetRpm = propMaxRPM * 0.60f;
                }
            }
        }
        else
        {
            targetRpm = 0f;
        }

        float maxRpmChangePerFrame = (propMaxRPM / propRpmRampTime) * Time.deltaTime;
        currentRpm = Mathf.MoveTowards(currentRpm, targetRpm, maxRpmChangePerFrame);

        float[] directions = { 1f, -1f, -1f, 1f };

        for (int i = 0; i < propellers.Length; i++)
        {
            if (propellers[i] != null)
            {
                propellers[i].transform.Rotate(Vector3.forward * currentRpm * directions[i] * Time.deltaTime);
            }
        }
    }

    private void UpdateCameraFollow()
    {
        // 如果當前啟用的鏡頭包含 XR Origin（例如名字含有 "XR" 或底下有 HMD），
        // 代表是 VR 模式，視角會自動跟隨 Parent 物件，不需要手動控制 Camera.main
        if (droneCameras != null && droneCameras.Length > currentCameraIndex && droneCameras[currentCameraIndex] != null)
        {
            if (droneCameras[currentCameraIndex].name.Contains("XR")) return;
        }

        if (cameraTarget != null && Camera.main != null)
        {
            Camera.main.transform.position = Vector3.SmoothDamp(
                Camera.main.transform.position,
                cameraTarget.position,
                ref cameraVelocity,
                cameraSmoothTime
            );
            Camera.main.transform.rotation = cameraTarget.rotation;
        }
    }

    private void AutoFindPropellers()
    {
        propellers = new GameObject[4];
        string[] motorNames = { "FL_Motor_Parent", "FR_Motor_Parent", "RL_Motor_Parent", "RR_Motor_Parent" };
        for (int i = 0; i < motorNames.Length; i++)
        {
            Transform motor = transform.Find("Racing Drone Merged/" + motorNames[i]);
            if (motor != null) propellers[i] = motor.Find("Prop")?.gameObject;
        }
    }

    private void InitializeCameras()
    {
        if (droneCameras == null || droneCameras.Length == 0) return;

        for (int i = 0; i < droneCameras.Length; i++)
        {
            if (droneCameras[i] != null)
                droneCameras[i].SetActive(i == 0);
        }
        currentCameraIndex = 0;
    }

    private void OnSwitchCamera(InputAction.CallbackContext context)
    {
        if (droneCameras == null || droneCameras.Length <= 1) return;

        // 關閉目前鏡頭
        if (droneCameras[currentCameraIndex] != null)
            droneCameras[currentCameraIndex].SetActive(false);

        // 切換下一個索引
        currentCameraIndex = (currentCameraIndex + 1) % droneCameras.Length;

        // 開啟新鏡頭
        if (droneCameras[currentCameraIndex] != null)
            droneCameras[currentCameraIndex].SetActive(true);

        Debug.Log("已切換至鏡頭: " + droneCameras[currentCameraIndex].name);
    }

    private void OnToggleCruise(InputAction.CallbackContext context)
    {
        currentFlightMode = (FlightMode)(((int)currentFlightMode + 1) % 3);

        if (currentFlightMode == FlightMode.Cruise || currentFlightMode == FlightMode.Vertical)
        {
            targetYaw = transform.rotation.eulerAngles.y;
        }

        Debug.Log("當前飛行模式切換為：" + currentFlightMode.ToString());
    }

    private bool CheckIsGrounded()
    {
        Vector3 rayStart = transform.position + (Vector3.up * 0.05f);
        bool hit = Physics.Raycast(rayStart, Vector3.down, out RaycastHit hitInfo, 0.2f);
        Debug.DrawRay(rayStart, Vector3.down * 0.2f, hit ? Color.green : Color.red);
        return hit;
    }

    private void UpdateEngineAudio()
    {
        if (engineAudioSource == null) return;

        if (!enginesStarted)
        {
            if (engineAudioSource.isPlaying) engineAudioSource.Stop();
            return;
        }

        if (isGrounded)
        {
            if (!wasGrounded)
            {
                PlayLandingAudio();
            }
            else if (!engineAudioSource.isPlaying && !isPlayingTakeoff)
            {
                PlayIdleAudio();
            }
        }
        else
        {
            if (wasGrounded)
            {
                PlayTakeoffAudio();
            }
            else if (engineAudioSource.clip == flyingLoopSound && !isPlayingTakeoff)
            {
                float throttleFactor = (throttleInput + 1f) * 0.5f;

                float targetPitch = Mathf.Lerp(minEnginePitch, maxEnginePitch, throttleFactor);
                float targetVolume = Mathf.Lerp(minEngineVolume, maxEngineVolume, throttleFactor);

                engineAudioSource.pitch = Mathf.Lerp(engineAudioSource.pitch, targetPitch, Time.deltaTime * 6f);
                engineAudioSource.volume = Mathf.Lerp(engineAudioSource.volume, targetVolume, Time.deltaTime * 6f);
            }
        }

        wasGrounded = isGrounded;
    }

    private void PlayIdleAudio()
    {
        if (engineAudioSource == null) return;

        CancelInvoke("StartFlyingLoop");
        isPlayingTakeoff = false;

        engineAudioSource.clip = flyingLoopSound;
        engineAudioSource.loop = true;
        engineAudioSource.pitch = minEnginePitch;
        engineAudioSource.volume = minEngineVolume;

        if (!engineAudioSource.isPlaying) engineAudioSource.Play();
    }

    private void PlayTakeoffAudio()
    {
        if (engineAudioSource == null) return;

        if (takeoffSound == null)
        {
            StartFlyingLoop();
            return;
        }

        CancelInvoke("StartFlyingLoop");
        isPlayingTakeoff = true;

        engineAudioSource.clip = takeoffSound;
        engineAudioSource.loop = false;
        engineAudioSource.pitch = 1.0f;
        engineAudioSource.volume = maxEngineVolume;
        engineAudioSource.Play();

        Invoke("StartFlyingLoop", takeoffSound.length);
    }

    private void StartFlyingLoop()
    {
        if (engineAudioSource == null) return;

        isPlayingTakeoff = false;
        if (isGrounded) return;

        engineAudioSource.clip = flyingLoopSound;
        engineAudioSource.loop = true;
        engineAudioSource.Play();
    }

    private void PlayLandingAudio()
    {
        if (engineAudioSource == null) return;

        CancelInvoke("StartFlyingLoop");
        isPlayingTakeoff = false;

        if (landingSound == null)
        {
            PlayIdleAudio();
            return;
        }

        engineAudioSource.clip = landingSound;
        engineAudioSource.loop = false;
        engineAudioSource.pitch = 1.0f;
        engineAudioSource.volume = maxEngineVolume;
        engineAudioSource.Play();
    }

    private void OnRespawn(InputAction.CallbackContext context)
    {
        Respawn();
    }

    private void Respawn()
    {
        isFlipping = false;
        currentFlightMode = FlightMode.Manual;
        enginesStarted = false;
        currentRpm = 0f;
        transform.position = respawnPosition;
        transform.rotation = Quaternion.identity;

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (engineAudioSource != null) engineAudioSource.Stop();
        Debug.Log("無人機已重生於 " + respawnPosition);
    }

    private bool ShouldAutoFlip()
    {
        bool upsideDown = Vector3.Dot(transform.up, Vector3.up) < 0f;
        bool lowEnough = transform.position.y <= flipHeightThreshold;
        return upsideDown && lowEnough;
    }

    private void StartAutoFlip()
    {
        isFlipping = true;
        flipProgress = 0f;
        flipStartRotation = transform.rotation;

        Vector3 currentEuler = transform.rotation.eulerAngles;
        flipTargetRotation = Quaternion.Euler(0f, currentEuler.y, 0f);

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private void UpdateAutoFlip()
    {
        flipProgress += Time.fixedDeltaTime / flipDuration;

        if (flipProgress >= 1f)
        {
            transform.rotation = flipTargetRotation;
            isFlipping = false;

            Vector3 pos = transform.position;
            if (pos.y <= flipHeightThreshold)
            {
                pos.y = flipHeightThreshold + 0.05f;
                transform.position = pos;
            }
        }
        else
        {
            transform.rotation = Quaternion.Slerp(flipStartRotation, flipTargetRotation, flipProgress);
        }
    }
}
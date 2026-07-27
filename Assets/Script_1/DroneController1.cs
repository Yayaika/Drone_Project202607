using UnityEngine;
using UnityEngine.InputSystem;

public class DroneController1 : MonoBehaviour
{
    // 飛行模式列舉
    public enum FlightMode
    {
        Stabilized, // 0. 自平懸停模式 (放開搖桿會自動回正並懸停)
        Cruise,     // 1. 定角巡航模式 (機頭固定朝下，適合一直往前飛)
        Vertical    // 2. 垂直無攻角模式 (強制保持水平)
    }

    [Header("【飛行狀態 (Flight State)】")]
    [SerializeField] private FlightMode currentFlightMode = FlightMode.Stabilized;
    public bool isArmed = false;     // 引擎是否已解鎖(轉動)
    public bool isGrounded = true;   // 是否在地面上

    [Header("【核心動力與定高 (Core Physics)】")]
    [SerializeField] private float maxThrust = 40f;              // 全油門時的最大向上推力
    [SerializeField] private float altitudeHoldStrength = 5f;    // 懸停時抵抗掉落的力量 (越大抓高度越緊)
    [SerializeField] private float horizontalSpeedBoost = 15f;   // 【新增】水平加速補償：數字越大，傾斜時往前飛的速度越快！

    [Header("【姿態控制 (Attitude Control)】")]
    [SerializeField] private float cruisePitchAngle = 12f;       // 巡航模式下，機身自動往前傾斜的角度
    [SerializeField] private float maxTiltAngle = 40f;           // 玩家手動打方向時，無人機最大的傾斜角度
    [SerializeField] private float maxYawRate = 200f;            // 左右旋轉(偏航)的最快速度
    [SerializeField] private float stabilizerStrength = 15f;     // 自平回正的力度 (越大回正越迅速)

    [Header("【螺旋槳視覺效果 (Propeller Visuals)】")]
    [SerializeField] private GameObject[] propellers;            // 綁定四個螺旋槳的模型
    [SerializeField] private float propIdleRPM = 800f;           // 剛啟動停在地面時的慢速轉速
    [SerializeField] private float propHoverRPM = 2800f;         // 懸停在空中時的轉速 (接近滿速)
    [SerializeField] private float propMaxRPM = 3500f;           // 全油門往上衝時的最大轉速
    private float currentVisualRpm = 0f;                         // 當前動畫轉速

    // 提供給外部腳本 (如 DroneAnimator) 讀取的公開屬性
    public bool IsEngineStarted => isArmed;
    public float CurrentRPM => currentVisualRpm;

    [Header("【攝影機系統 (Camera System)】")]
    [SerializeField] private Transform cameraTarget;             // 攝影機要跟隨的目標點
    [SerializeField] private float cameraSmoothTime = 0.05f;     // 攝影機跟隨的平滑延遲
    [SerializeField] private GameObject[] droneCameras;          // 多鏡頭陣列 (包含 VR 鏡頭)
    private int currentCameraIndex = 0;
    private Vector3 cameraVelocity;

    [Header("【音效系統 (Audio Setup)】")]
    [SerializeField] private AudioSource engineAudioSource;      // 播放引擎聲的喇叭
    [SerializeField] private AudioClip takeoffSound;             // 起飛瞬間音效
    [SerializeField] private AudioClip flyingLoopSound;          // 飛行循環音效
    [SerializeField] private AudioClip landingSound;             // 降落瞬間音效
    [SerializeField] private float engineVolume = 0.8f;          // 基礎音量大小

    [Header("【重生與翻正 (Respawn & Flip)】")]
    [SerializeField] private Vector3 respawnPosition = new Vector3(0f, 0.5f, 0f); // 按 R 鍵重生的位置
    private bool isFlipping = false;

    // --- 輸入系統 (Input Actions) ---
    private InputAction throttleAction; // 油門 (上升/下降)
    private InputAction yawAction;      // 航向 (左右自轉)
    private InputAction pitchAction;    // 俯仰 (前進/後退)
    private InputAction rollAction;     // 橫滾 (左右側飛)
    private InputAction toggleModeAction; // 切換飛行模式
    private InputAction switchCamAction;  // 切換鏡頭
    private InputAction respawnAction;    // 重生

    // 儲存玩家當前的按鍵輸入值 (-1 到 1)
    private float stickThrottle, stickYaw, stickPitch, stickRoll;

    private Rigidbody rb;
    private float targetYawAngle; // 目標自轉角度 (用於鎖定機頭方向)

    // 計時器 (用於長按解鎖/上鎖)
    private float armTimer = 0f;
    private float disarmTimer = 0f;
    private bool wasGroundedLastFrame = true;

    // ==========================================
    // 初始化與按鍵綁定 (Awake & OnEnable)
    // ==========================================
    private void Awake()
    {
        // 綁定油門 (左搖桿 Y 軸 / 鍵盤 W S)
        throttleAction = new InputAction("Throttle", binding: "<Gamepad>/leftStick/y");
        throttleAction.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/w").With("Negative", "<Keyboard>/s");

        // 綁定航向 (左搖桿 X 軸 / 鍵盤 D A)
        yawAction = new InputAction("Yaw", binding: "<Gamepad>/leftStick/x");
        yawAction.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/d").With("Negative", "<Keyboard>/a");

        // 綁定俯仰 (右搖桿 Y 軸 / 鍵盤 上 下)
        pitchAction = new InputAction("Pitch", binding: "<Gamepad>/rightStick/y");
        pitchAction.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/upArrow").With("Negative", "<Keyboard>/downArrow");

        // 綁定橫滾 (右搖桿 X 軸 / 鍵盤 右 左)
        rollAction = new InputAction("Roll", binding: "<Gamepad>/rightStick/x");
        rollAction.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/rightArrow").With("Negative", "<Keyboard>/leftArrow");

        // 綁定功能鍵
        toggleModeAction = new InputAction("ToggleMode", binding: "<Keyboard>/f");
        toggleModeAction.AddBinding("<Gamepad>/leftShoulder");

        switchCamAction = new InputAction("SwitchCam", binding: "<Keyboard>/c");
        switchCamAction.AddBinding("<Gamepad>/buttonNorth");

        respawnAction = new InputAction("Respawn", binding: "<Keyboard>/r");
        respawnAction.AddBinding("<Gamepad>/buttonEast");
    }

    private void OnEnable()
    {
        // 啟用所有按鍵輸入
        throttleAction.Enable(); yawAction.Enable();
        pitchAction.Enable(); rollAction.Enable();
        toggleModeAction.Enable(); switchCamAction.Enable(); respawnAction.Enable();

        // 綁定按鍵按下時觸發的事件
        toggleModeAction.performed += _ => ToggleFlightMode();
        switchCamAction.performed += _ => SwitchCamera();
        respawnAction.performed += _ => Respawn();
    }

    private void OnDisable()
    {
        // 禁用按鍵輸入 (防止報錯)
        throttleAction.Disable(); yawAction.Disable();
        pitchAction.Disable(); rollAction.Disable();
        toggleModeAction.Disable(); switchCamAction.Disable(); respawnAction.Disable();
    }

    private void Start()
    {
        // 初始化剛體物理屬性
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();

        rb.mass = 1.0f;
        rb.linearDamping = 1.0f;  // 空氣阻力
        rb.angularDamping = 3.0f; // 旋轉阻力 (讓機身更穩)
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // 初始化音效喇叭
        if (engineAudioSource != null)
        {
            engineAudioSource.spatialBlend = 1f; // 3D 音效
            engineAudioSource.loop = true;
            engineAudioSource.volume = engineVolume;
            engineAudioSource.pitch = 1.0f; // 鎖定音調為原音
        }

        FindPropellers();
        InitializeCameras();

        targetYawAngle = transform.eulerAngles.y; // 記住初始面向
    }

    private void Update()
    {
        if (isFlipping) return;

        // 1. 讀取玩家按鍵輸入
        stickThrottle = throttleAction.ReadValue<float>();
        stickYaw = yawAction.ReadValue<float>();
        stickPitch = pitchAction.ReadValue<float>();
        stickRoll = rollAction.ReadValue<float>();

        // 2. 更新系統狀態
        UpdateGroundedState();     // 檢查是否落地
        HandleArmingState();       // 檢查是否長按解鎖/熄火
        UpdatePropellersVisual();  // 更新螺旋槳動畫
        UpdateAudio();             // 更新音效播放

        wasGroundedLastFrame = isGrounded;
    }

    private void FixedUpdate()
    {
        if (isFlipping) return;
        CheckAutoFlip(); // 檢查翻車自動回正

        // 如果引擎已解鎖，則套用飛行物理
        if (isArmed)
        {
            ApplyFlightPhysics();
        }

        // 攝影機跟隨
        UpdateCameraPosition();
    }

    // ==========================================
    // 狀態偵測與控制邏輯
    // ==========================================

    // 偵測是否接觸地面 (排除自身碰撞體)
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
    }

    // 引擎解鎖 (Arm) 與 熄火 (Disarm) 邏輯
    private void HandleArmingState()
    {
        if (!isArmed)
        {
            // 熄火狀態下：在地面長按 W (大於 0.8) 即可解鎖啟動
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
            // 啟動狀態下：在地面長按 S (小於 -0.8) 即可熄火關閉
            if (isGrounded && stickThrottle < -0.8f)
            {
                disarmTimer += Time.deltaTime;
                if (disarmTimer > 0.8f)
                {
                    isArmed = false;
                    disarmTimer = 0f;
                    currentFlightMode = FlightMode.Stabilized; // 熄火自動切回手動模式
                    Debug.Log("無人機引擎已熄火！");
                }
            }
            else
            {
                disarmTimer = 0f;
            }
        }
    }

    // ==========================================
    // 核心飛行物理 (推力與角度)
    // ==========================================
    private void ApplyFlightPhysics()
    {
        // 1. 計算目標旋轉角度 (Yaw, Pitch, Roll)
        targetYawAngle += stickYaw * maxYawRate * Time.fixedDeltaTime;

        float targetPitchAngle = -stickPitch * maxTiltAngle;
        float targetRollAngle = stickRoll * maxTiltAngle;

        // 依據飛行模式覆寫傾斜角
        if (currentFlightMode == FlightMode.Cruise)
        {
            targetPitchAngle = -cruisePitchAngle; // 巡航模式固定前傾
            targetRollAngle = 0f;
        }
        else if (currentFlightMode == FlightMode.Vertical)
        {
            targetPitchAngle = 0f;
            targetRollAngle = 0f;
        }

        // 2. 套用姿態控制力矩 (讓機身旋轉到目標角度)
        Quaternion targetRotation = Quaternion.Euler(targetPitchAngle, targetYawAngle, targetRollAngle);
        Quaternion errorRotation = targetRotation * Quaternion.Inverse(transform.rotation);
        errorRotation.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;

        Vector3 correctionTorque = axis.normalized * (angle * stabilizerStrength * Mathf.Deg2Rad);
        rb.AddTorque(correctionTorque, ForceMode.Acceleration);

        // 3. 計算懸停所需的基礎升力 (抵銷重力，並加上傾角補償避免掉高度)
        float gravityForce = Mathf.Abs(Physics.gravity.y);
        float tiltAngle = Vector3.Angle(Vector3.up, transform.up);
        float tiltCompensation = 1f / Mathf.Max(Mathf.Cos(tiltAngle * Mathf.Deg2Rad), 0.3f);
        float hoverThrust = gravityForce * tiltCompensation;

        float finalThrust = 0f;

        // 4. 油門邏輯 (上升/下降/懸停)
        if (Mathf.Abs(stickThrottle) > 0.05f)
        {
            if (stickThrottle > 0)
                finalThrust = Mathf.Lerp(hoverThrust, maxThrust, stickThrottle); // 向上加速
            else
                finalThrust = Mathf.Lerp(hoverThrust, 0f, -stickThrottle); // 向下減速
        }
        else
        {
            if (!isGrounded)
            {
                // 空中放開油門時：啟動自動定高 PID 補償
                float verticalVelocity = rb.linearVelocity.y;
                finalThrust = hoverThrust - (verticalVelocity * altitudeHoldStrength);
            }
        }

        // 輸出垂直升力
        rb.AddForce(transform.up * finalThrust, ForceMode.Acceleration);

        // ==========================================
        // 【新增】5. 傾斜水平加速補償 (解決前進太慢的問題)
        // 提取無人機朝向水平面(X,Z)的分量，依照該分量的大小給予額外的推進力
        // 傾角越大 -> 水平分量越大 -> 飛得越快
        // ==========================================
        Vector3 horizontalDirection = new Vector3(transform.up.x, 0, transform.up.z);
        rb.AddForce(horizontalDirection * horizontalSpeedBoost, ForceMode.Acceleration);
    }

    // ==========================================
    // 音效與視覺
    // ==========================================
    private void UpdateAudio()
    {
        if (engineAudioSource == null) return;

        // 【修改】鎖定音調為 1.0 (原音)，移除怪異的高低音變化
        engineAudioSource.pitch = 1.0f;

        if (!isArmed)
        {
            if (engineAudioSource.isPlaying) engineAudioSource.Stop();
            return;
        }

        // 確保循環音效持續播放
        if (!engineAudioSource.isPlaying && flyingLoopSound != null)
        {
            engineAudioSource.clip = flyingLoopSound;
            engineAudioSource.Play();
        }

        // 播報起飛瞬間音效
        if (!wasGroundedLastFrame && !isGrounded && rb.linearVelocity.y > 0.5f)
        {
            if (takeoffSound) engineAudioSource.PlayOneShot(takeoffSound, engineVolume);
        }

        // 播報降落瞬間音效
        if (!wasGroundedLastFrame && isGrounded && rb.linearVelocity.y < -0.5f)
        {
            if (landingSound) engineAudioSource.PlayOneShot(landingSound, engineVolume);
        }
    }

    // 控制槳葉視覺轉速
    private void UpdatePropellersVisual()
    {
        float targetRpm = 0f;
        if (isArmed)
        {
            if (isGrounded)
            {
                targetRpm = propIdleRPM; // 地面怠速
            }
            else
            {
                // 空中轉速變化
                if (stickThrottle > 0)
                    targetRpm = Mathf.Lerp(propHoverRPM, propMaxRPM, stickThrottle);
                else if (stickThrottle < 0)
                    targetRpm = Mathf.Lerp(propHoverRPM, propIdleRPM, -stickThrottle);
                else
                    targetRpm = propHoverRPM;
            }
        }

        // 平滑過渡當前轉速
        currentVisualRpm = Mathf.Lerp(currentVisualRpm, targetRpm, Time.deltaTime * 8f);
        float[] directions = { 1f, -1f, -1f, 1f };

        // 旋轉實體槳葉模型
        for (int i = 0; i < propellers.Length; i++)
        {
            if (propellers[i] != null)
            {
                propellers[i].transform.Rotate(Vector3.forward * currentVisualRpm * directions[i] * Time.deltaTime);
            }
        }
    }

    // ==========================================
    // 輔助功能 (鏡頭、重生、翻正)
    // ==========================================
    private void UpdateCameraPosition()
    {
        // 若為 VR 鏡頭則跳過手動跟隨 (由 XR Origin 自行處理)
        if (droneCameras != null && droneCameras.Length > currentCameraIndex && droneCameras[currentCameraIndex] != null)
        {
            if (droneCameras[currentCameraIndex].name.Contains("XR")) return;
        }

        // 平滑跟隨攝影機
        if (cameraTarget != null && Camera.main != null)
        {
            Camera.main.transform.position = Vector3.SmoothDamp(
                Camera.main.transform.position, cameraTarget.position, ref cameraVelocity, cameraSmoothTime);
            Camera.main.transform.rotation = Quaternion.Slerp(Camera.main.transform.rotation, cameraTarget.rotation, Time.deltaTime * 20f);
        }
    }

    private void ToggleFlightMode()
    {
        currentFlightMode = (FlightMode)(((int)currentFlightMode + 1) % 3);
        targetYawAngle = transform.eulerAngles.y;
    }

    private void SwitchCamera()
    {
        if (droneCameras == null || droneCameras.Length <= 1) return;

        if (droneCameras[currentCameraIndex] != null) droneCameras[currentCameraIndex].SetActive(false);
        currentCameraIndex = (currentCameraIndex + 1) % droneCameras.Length;
        if (droneCameras[currentCameraIndex] != null) droneCameras[currentCameraIndex].SetActive(true);
    }

    private void Respawn()
    {
        isArmed = false;
        currentFlightMode = FlightMode.Stabilized;
        transform.position = respawnPosition;
        transform.rotation = Quaternion.identity;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        targetYawAngle = 0f;
    }

    private void CheckAutoFlip()
    {
        // 熄火且四腳朝天時，自動翻正機身
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

    private void InitializeCameras()
    {
        if (droneCameras == null) return;
        for (int i = 0; i < droneCameras.Length; i++)
        {
            if (droneCameras[i] != null) droneCameras[i].SetActive(i == 0);
        }
        currentCameraIndex = 0;
    }
}
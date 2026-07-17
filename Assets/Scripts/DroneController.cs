using UnityEngine;

public class DroneController : MonoBehaviour
{
    [Header("WASD Movement Settings")]
    public float moveSpeed = 15f;
    public float verticalSpeed = 8f;
    public float rotateSpeed = 60f;
    public float landSpeed = 0.8f;

    [Header("Hand Movement Settings")]
    public float handMoveSpeed = 20f;
    public float handVerticalSpeed = 15f;

    [Header("Tilt Settings")]
    public float maxTiltAngle = 20f;
    public float tiltSpeed = 5f;

    [Header("Propellers")]
    public Transform[] propellers;
    public float propellerFlySpeed = 800f;
    private float currentPropSpeed = 0f;

    [Header("State")]
    public bool isFlying = false;
    public float groundY = 0f;

    [Header("Hand Sensitivity")]
    public float handZSensitivity = 8f;
    public float handRotateMultiplier = 2f;

    private float currentTiltX = 0f;
    private float currentTiltZ = 0f;
    private float inputX = 0f;
    private float inputZ = 0f;
    private bool isLanding = false;
    private CharacterController cc;

    void Start()
    {
        cc = GetComponent<CharacterController>();
        // 【新增】如果沒有指定鏡頭，自動抓取場景中的 Main Camera
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
        else if (cameraTransform == null)
        {
            Debug.LogError("場景中找不到 Main Camera，請確認相機有 Tag: MainCamera");
        }
    }

    void Update()
    {
        UpdatePropellers();
        HandleKeyboardInput();
        ApplyTilt();
    }

    void HandleKeyboardInput()
    {
        inputX = 0f;
        inputZ = 0f;
        float y = 0f;
        float rot = 0f;

        if (!isFlying && Input.GetKey(KeyCode.Q))
        {
            isFlying = true;
            return;
        }

        if (!isFlying) return;

        if (Input.GetKey(KeyCode.W)) inputZ = 1f;
        if (Input.GetKey(KeyCode.S)) inputZ = -1f;
        if (Input.GetKey(KeyCode.A)) inputX = -1f;
        if (Input.GetKey(KeyCode.D)) inputX = 1f;

        if (Input.GetKey(KeyCode.Q)) y = 1f;
        if (Input.GetKey(KeyCode.E)) y = -1f;

        if (Input.GetKey(KeyCode.LeftArrow)) rot = -1f;
        if (Input.GetKey(KeyCode.RightArrow)) rot = 1f;

        // --- 【核心修改區：基於鏡頭的方向計算】 ---
        // 確保 cameraTransform 已經在 Inspector 中指定
        if (cameraTransform != null)
        {
            Vector3 camForward = cameraTransform.forward;
            Vector3 camRight = cameraTransform.right;

            // 忽略鏡頭的傾斜，只取水平分量
            camForward.y = 0;
            camRight.y = 0;
            camForward.Normalize();
            camRight.Normalize();

            // 計算移動方向
            Vector3 moveDirection = (camForward * inputZ + camRight * inputX).normalized;

            // 執行移動
            Vector3 moveVelocity = moveDirection * moveSpeed;
            Vector3 verticalVelocity = Vector3.up * y * verticalSpeed;
            cc.Move((moveVelocity + verticalVelocity) * Time.deltaTime);

            // 讓無人機轉向移動方向 (這會取代原本純粹由鍵盤控制的 rot)
            if (moveDirection.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotateSpeed * Time.deltaTime * 0.1f);
            }
        }
        else
        {
            Debug.LogError("請在 DroneController 的 Inspector 中拖入 Main Camera！");
        }
    }

    void ApplyTilt()
    {
        float targetTiltX = isFlying ? inputZ * maxTiltAngle : 0f;
        float targetTiltZ = isFlying ? -inputX * maxTiltAngle : 0f;

        currentTiltX = Mathf.Lerp(currentTiltX, targetTiltX, Time.deltaTime * tiltSpeed);
        currentTiltZ = Mathf.Lerp(currentTiltZ, targetTiltZ, Time.deltaTime * tiltSpeed);

        float currentYaw = transform.eulerAngles.y;
        transform.rotation = Quaternion.Euler(currentTiltX, currentYaw, currentTiltZ);
    }

    void UpdatePropellers()
    {
        float targetSpeed = isFlying ? propellerFlySpeed : 0f;
        currentPropSpeed = Mathf.Lerp(currentPropSpeed, targetSpeed, Time.deltaTime * 3f);

        if (propellers != null)
            foreach (Transform prop in propellers)
                if (prop != null)
                    prop.Rotate(Vector3.up, currentPropSpeed * Time.deltaTime);
    }

    public void TakeOff()
    {
        isFlying = true;
    }

    public void Land()
    {
        StartCoroutine(LandCoroutine());
    }

    private System.Collections.IEnumerator LandCoroutine()
    {
        isLanding = true;
        while (true)
        {
            RaycastHit hit;
            float minY;

            if (Physics.Raycast(transform.position, Vector3.down, out hit, 20f))
                minY = hit.point.y + 0.5f;
            else
                minY = groundY + 0.5f;

            if (transform.position.y <= minY + 0.05f)
            {
                isFlying = false;
                isLanding = false;
                currentPropSpeed = 0f;
                break;
            }

            cc.Move(-Vector3.up * landSpeed * Time.deltaTime);
            yield return null;
        }
    }

    public void MoveFromHand(float y, float z, float rotate)
    {
        if (!isFlying) return;
        inputZ = z;

        transform.Rotate(Vector3.up, rotate * rotateSpeed * handRotateMultiplier * Time.deltaTime);

        Vector3 forward = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
        cc.Move(forward * z * handMoveSpeed * Time.deltaTime);
        cc.Move(Vector3.up * y * handVerticalSpeed * Time.deltaTime);
    }

    public Transform cameraTransform; // 在 Inspector 中把你的 Main Camera 拖進來
}
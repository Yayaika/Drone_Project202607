using UnityEngine;
using UnityEngine.InputSystem; // 必須引入新版 Input System 命名空間

public class Move : MonoBehaviour
{
    [Header("移動設定")]
    public float moveSpeed = 8f;      // 移動推力
    // public float jumpForce = 100f;    // 跳躍爆發力
    public float ascendSpeed = 100f;
    public float turnSpeed = 10f;

    private Rigidbody rb;
    // private bool isGrounded = true;   // 檢查是否在地面
    private Transform mainCameraTransform;

    [Header("賽道起點設定")]
    public UnityEngine.Splines.SplineContainer raceSpline;

    void Start()
    {
        if (raceSpline != null)
        {
        // 1. 直接取得已經換算好的「真實世界座標」與「方向」！
            Vector3 startPos = (Vector3)raceSpline.EvaluatePosition(0f);
            Vector3 startTangent = (Vector3)raceSpline.EvaluateTangent(0f);
            Vector3 startUp = (Vector3)raceSpline.EvaluateUpVector(0f);

        // 2. 直接套用給無人機，搞定！
            transform.position = startPos;
            transform.rotation = Quaternion.LookRotation(startTangent, startUp);
        }

        rb = GetComponent<Rigidbody>();
        if (Camera.main != null)
        {
            mainCameraTransform = Camera.main.transform;
        }
    }

    void Update()
    {
        // 處理跳躍：只偵測鍵盤的空白鍵 (Space)
        // if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame && isGrounded)
        // {
        //     // ForceMode.Impulse = 瞬間衝擊力
        //     // rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        //     isGrounded = false; // 跳起來後，設為不在地面
        // }
    }

    void FixedUpdate()
    {
        // 取得玩家鍵盤輸入
        float moveH = 0f;
        float moveV = 0f;
        float moveUp = 0f;

        if (Keyboard.current != null)
        {
            // 支援 WASD 與 上下左右方向鍵
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) moveV += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) moveV -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) moveH += 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) moveH -= 1f;
            if (Keyboard.current.spaceKey.isPressed) moveUp += 1f;
            if (Keyboard.current.shiftKey.isPressed) moveUp -= 1f;
        }

        // 限制範圍在 -1 到 1 之間
        moveH = Mathf.Clamp(moveH, -1f, 1f);
        moveV = Mathf.Clamp(moveV, -1f, 1f);
       // moveUp = Mathf.Clamp(moveUp, -1f, 1f);

        // --- 攝影機方向與移動計算 ---

        Vector3 camForward = mainCameraTransform.forward;
        Vector3 camRight = mainCameraTransform.right;

        camForward.y = 0;
        camRight.y = 0;

        camForward.Normalize();
        camRight.Normalize();

        Vector3 moveDirection = (camForward * moveV + camRight * moveH).normalized;

        if (moveDirection != Vector3.zero)
        {
            // 計算出目標面向的角度
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            
            // 使用 Quaternion.Slerp 來達到「平滑轉身」的效果
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.fixedDeltaTime * turnSpeed);
        }
        
        // 計算目標速度 (方向 * 速度)
        Vector3 targetVelocity = moveDirection * moveSpeed;

        // float targetVelocityY = rb.linearVelocity.y; // 預設保留原本的 Y 軸重力掉落速度
        float targetVelocityY = 0f;
        if (moveUp != 0)
        {
            // 如果玩家有按下上升或下降鍵，就強制覆蓋 Y 軸的速度
            targetVelocityY = moveUp * ascendSpeed;
        }
        rb.linearVelocity = new Vector3(targetVelocity.x, targetVelocityY, targetVelocity.z);
        // 直接套用速度 (保留原本的 Y 軸重力)
        // rb.linearVelocity = new Vector3(targetVelocity.x, rb.linearVelocity.y, targetVelocity.z);
    }

    // --- 碰撞與觸發區 ---

    /*private void OnCollisionEnter(Collision collision)
    {
        // 碰到標籤為 "Ground" 的物體，恢復跳躍能力
        if (collision.gameObject.CompareTag("Ground"))
        {
            // isGrounded = true; 
        }
    }*/
}
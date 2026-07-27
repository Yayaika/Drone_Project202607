using UnityEngine;

public class DroneRespawnHandler : MonoBehaviour
{
    [Header("【重生點與旋轉】")]
    public Vector3 respawnPoint;
    public Quaternion respawnRotation;

    private CharacterController cc;
    private Rigidbody rb;
    private DroneController oldCtrl;
    private DroneController1 newCtrl;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        rb = GetComponent<Rigidbody>();
        oldCtrl = GetComponent<DroneController>();
        newCtrl = GetComponent<DroneController1>();
    }

    void Update()
    {
        // 按下 R 鍵執行重生
        if (Input.GetKeyDown(KeyCode.R))
        {
            Respawn();
        }
    }

    /// <summary>
    /// 設定最新重生點 (SpawnCtrl 或 GameManager 穿過圈圈時會呼叫)
    /// </summary>
    public void SetRespawnPoint(Vector3 newPoint)
    {
        respawnPoint = newPoint;
        respawnRotation = transform.rotation;
        Debug.Log($"[DroneRespawnHandler] 已更新重生座標至：{respawnPoint}");
    }

    /// <summary>
    /// 執行重生：精確移動至 respawnPoint
    /// </summary>
    public void Respawn()
    {
        // 1. 關閉 CharacterController 避免位移被物理引擎鎖住
        if (cc != null) cc.enabled = false;

        // 2. 直接設為目標重生點 (不重複加高度，確保與 SpawnCtrl 初始位置完全一致)
        transform.position = respawnPoint;
        transform.rotation = Quaternion.Euler(0f, respawnRotation.eulerAngles.y, 0f);

        // 3. 清空物理殘留速度
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // 4. 重置飛控狀態
        if (oldCtrl != null) oldCtrl.isFlying = false;
        if (newCtrl != null) newCtrl.isArmed = true;

        // 5. 重新啟用 CharacterController
        if (cc != null) cc.enabled = true;

        Debug.Log($"[Respawn] 無人機已精確重生於：{respawnPoint}");
    }
}
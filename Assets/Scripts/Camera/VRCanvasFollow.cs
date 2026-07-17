using UnityEngine;

public class VRCanvasFollow : MonoBehaviour
{
    [Header("跟隨目標 (通常為 XR Origin 的 Main Camera)")]
    public Transform targetCamera;

    [Header("跟隨設定")]
    public float distance = 3.0f;      // UI 與玩家的距離
    public float followSpeed = 5.0f;   // 跟隨平滑速度
    public float heightOffset = -0.2f; // UI 的高度修正量

    void Start()
    {
        if (targetCamera == null && Camera.main != null)
        {
            targetCamera = Camera.main.transform;
        }
    }

    void LateUpdate()
    {
        if (targetCamera == null) return;

        // 計算目標位置 (相機前方距離 distance 處)
        Vector3 targetPosition = targetCamera.position + (targetCamera.forward * distance);
        targetPosition.y += heightOffset; // 稍微調整高度

        // 平滑移動位置
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * followSpeed);

        // 讓 UI 轉向玩家相機 (只旋轉 Y 軸，防止 UI 傾斜)
        Vector3 lookDirection = transform.position - targetCamera.position;
        lookDirection.y = 0; // 鎖定 Y 軸

        if (lookDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * followSpeed);
        }
    }
}
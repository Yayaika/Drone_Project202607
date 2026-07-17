using UnityEngine;

public class VRCameraFollow : MonoBehaviour
{
    [Header("跟隨目標 (動態生成的無人機)")]
    public Transform target;

    [Header("跟隨設定")]
    public float distance = 5.0f;     // 在無人機後方的水平距離
    public float height = 2.0f;       // 在無人機上方的垂直距離
    public float smoothSpeed = 4.0f;  // 跟隨平滑速度 (數值越小越柔和，不易眩暈)

    void LateUpdate()
    {
        if (target == null) return;

        // 計算目標跟隨位置 (無人機後方)
        Vector3 targetPos = target.position - (target.forward * distance) + (Vector3.up * height);

        // 平滑移動 XR Origin
        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * smoothSpeed);

        // 讓 XR Origin 始終水平面朝無人機前方方向 (鎖定 Y 軸旋轉，不干涉 VR 頭戴裝置的自由上下旋轉，防止玩家眩暈)
        Vector3 lookDir = target.position - transform.position;
        lookDir.y = 0;

        if (lookDir.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(lookDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * smoothSpeed);
        }
    }
}
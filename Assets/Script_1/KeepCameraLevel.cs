using UnityEngine;

public class KeepCameraLevel : MonoBehaviour
{
    [Header("目標無人機")]
    [SerializeField] private Transform droneTarget; // 指向無人機 Transform

    [Header("鏡頭相對偏移量 (X:左右, Y:上下, Z:前後)")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 1.5f, -3f); // 預設放在無人機後上方

    void LateUpdate()
    {
        if (droneTarget == null) return;

        // 1. 只取得無人機的 Y 軸旋轉（Yaw），忽略 X (Pitch) 與 Z (Roll) 傾斜
        Quaternion targetRotation = Quaternion.Euler(0f, droneTarget.eulerAngles.y, 0f);

        // 2. 將 Offset 依照無人機的水平朝向做旋轉轉化，計算出鏡頭在世界座標中的位置
        Vector3 targetPosition = droneTarget.position + (targetRotation * offset);

        // 3. 套用位置與旋轉
        transform.position = targetPosition;
        transform.rotation = targetRotation;
    }
}
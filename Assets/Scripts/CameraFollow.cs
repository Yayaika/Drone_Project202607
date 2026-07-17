using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target Settings")]
    public Transform target;

    [Header("Distance & Sensitivity")]
    public float distance = 7f;           // 鏡頭與目標的距離
    public float mouseSensitivity = 2f;    // 滑鼠旋轉靈敏度

    [Header("Rotation Limits")]
    public float yMinLimit = -20f;
    public float yMaxLimit = 80f;

    private float x = 0.0f; // 水平旋轉角度
    private float y = 0.0f; // 垂直旋轉角度

    void Start()
    {
        // 初始化旋轉角度以匹配當前鏡頭位置
        Vector3 angles = transform.eulerAngles;
        x = angles.y;
        y = angles.x;
    }

    // 在 LateUpdate 中將滑鼠輸入改為隨時讀取
    void LateUpdate()
    {
        if (target == null) return;

        // 隨時讀取滑鼠移動，不再受限於左鍵
        x += Input.GetAxis("Mouse X") * mouseSensitivity;
        y -= Input.GetAxis("Mouse Y") * mouseSensitivity;

        y = Mathf.Clamp(y, yMinLimit, yMaxLimit);

        Quaternion rotation = Quaternion.Euler(y, x, 0);
        transform.rotation = rotation; // 更新鏡頭旋轉

        // 計算位置
        Vector3 position = rotation * new Vector3(0.0f, 0.0f, -distance) + target.position;
        transform.position = position;
    }
}
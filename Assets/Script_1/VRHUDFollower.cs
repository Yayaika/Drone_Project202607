using UnityEngine;

public class VRHUDFollower : MonoBehaviour
{
    [Header("【HUD 相對鏡頭的距離與偏移】")]
    [Tooltip("HUD 在鏡頭正前方的距離（米）")]
    public float distance = 0.5f;

    [Tooltip("HUD 相對位移 (X: 左右, Y: 上下, Z: 前後)")]
    public Vector3 offset = new Vector3(0f, -0.08f, 0f);

    [Tooltip("滑鼠滾輪調整上下位置的靈敏度")]
    public float scrollSensitivity = 0.05f;

    [Header("【姿態鎖定】")]
    [Tooltip("勾選時，HUD 永遠保持垂直地面，不會隨鏡頭俯仰/橫滾而傾斜")]
    public bool lockVerticalToGround = true;

    private Transform targetCameraTransform;

    private void Start()
    {
        // 初始自動獲取主攝影機 Transform
        FindActiveCamera();
    }

    private void LateUpdate()
    {
        if (targetCameraTransform == null || !targetCameraTransform.gameObject.activeInHierarchy)
        {
            FindActiveCamera();
            if (targetCameraTransform == null) return;
        }

        // 處理滑鼠滾輪調整上下 offset
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scrollInput) > 0.01f)
        {
            offset.y += scrollInput * scrollSensitivity;
        }

        // 1. 計算目標位置：以鏡頭自身的座標軸進行偏移 (Forward, Right, Up)
        Vector3 targetPosition = targetCameraTransform.position
                               + (targetCameraTransform.forward * distance)
                               + (targetCameraTransform.right * offset.x)
                               + (targetCameraTransform.up * offset.y)
                               + (targetCameraTransform.forward * offset.z);

        transform.position = targetPosition;

        // 2. 旋轉完全同步鏡頭：直接繼承鏡頭的 Rotation，確保抬頭、俯視、轉頭都完全正對畫面
        transform.rotation = targetCameraTransform.rotation;
    }

    /// <summary>
    /// 自動尋找目前場景中啟用的攝影機 (支援 XR Origin / Main Camera)
    /// </summary>
    public void FindActiveCamera()
    {
        Camera cam = Camera.main;
        if (cam != null && cam.gameObject.activeInHierarchy)
        {
            targetCameraTransform = cam.transform;
            return;
        }

        // 若 Camera.main 找不到，搜尋場景中第一個啟用的 Camera
        Camera[] allCams = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (allCams.Length > 0)
        {
            targetCameraTransform = allCams[0].transform;
        }
    }

    /// <summary>
    /// 手動指定切換後的鏡頭 Transform
    /// </summary>
    public void SetTargetCamera(Transform newCamTransform)
    {
        targetCameraTransform = newCamTransform;
    }
}
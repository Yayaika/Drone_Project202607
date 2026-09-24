using UnityEngine;

public class VRHUDFollower : MonoBehaviour
{
    [Header("【HUD 視距與位置調整】")]
    [Tooltip("HUD 距離攝影機的物理距離（滑桿可直接拉動微調）")]
    [Range(5.0f, 20.0f)] // 在 Inspector 中生成滑桿，範圍為 5 到 20 米
    public float distance = 15.0f;

    [Tooltip("HUD 相對偏移 (X: 左右, Y: 上下, Z: 前後微調)")]
    public Vector3 offset = new Vector3(0f, -0.1f, 0f);

    [Tooltip("滑鼠滾輪調整上下位置的靈敏度")]
    public float scrollSensitivity = 0.05f;

    private Transform targetCameraTransform;

    private void Start()
    {
        FindActiveCamera();
    }

    private void LateUpdate()
    {
        if (targetCameraTransform == null || !targetCameraTransform.gameObject.activeInHierarchy)
        {
            FindActiveCamera();
            if (targetCameraTransform == null) return;
        }

        // 滑鼠滾輪微調上下位置
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scrollInput) > 0.01f)
        {
            offset.y += scrollInput * scrollSensitivity;
        }

        // 1. 確保 Canvas 綁定為當前攝影機的子物件
        if (transform.parent != targetCameraTransform)
        {
            transform.SetParent(targetCameraTransform, true);
        }

        // 2. 直跟（Hard Lock）：即時套用 Inspector 設定的 distance 數值
        transform.localPosition = new Vector3(offset.x, offset.y, distance + offset.z);
        transform.localRotation = Quaternion.identity;
    }

    public void FindActiveCamera()
    {
        Camera cam = Camera.main;
        if (cam != null && cam.gameObject.activeInHierarchy)
        {
            SetTargetCamera(cam.transform);
            return;
        }

        Camera[] allCams = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (allCams.Length > 0)
        {
            SetTargetCamera(allCams[0].transform);
        }
    }

    public void SetTargetCamera(Transform newCamTransform)
    {
        targetCameraTransform = newCamTransform;
        if (targetCameraTransform != null)
        {
            transform.SetParent(targetCameraTransform, true);
            transform.localPosition = new Vector3(offset.x, offset.y, distance + offset.z);
            transform.localRotation = Quaternion.identity;
        }
    }
}
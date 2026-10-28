using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 寫實化場景 (目前試做於港區)：無人機 Prefab 的攝影機預設不跑後處理，
/// 這個元件在本場景執行時為所有 Base 攝影機開啟後處理與 SMAA，讓場景中的全域 Volume (色調映射、調色、Bloom) 生效。
/// 只改執行時的攝影機設定，不修改 Prefab，其他場景不受影響。
/// </summary>
public class SceneRealism : MonoBehaviour
{
    [SerializeField] private AntialiasingMode antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

    private float nextCheck;

    private void Start()
    {
        ApplyToCameras();
    }

    // 無人機重生或切換視角時可能出現新的攝影機，每秒檢查一次
    private void Update()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 1f;
        ApplyToCameras();
    }

    private void ApplyToCameras()
    {
        foreach (Camera camera in Camera.allCameras)
        {
            if (camera.targetTexture != null) continue;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (data == null || data.renderType != CameraRenderType.Base || data.renderPostProcessing) continue;
            data.renderPostProcessing = true;
            data.antialiasing = antialiasing;
            data.antialiasingQuality = AntialiasingQuality.Medium;
        }
    }
}

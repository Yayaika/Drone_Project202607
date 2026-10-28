using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 室內關卡 (工廠廠房) 的環境光與霧：場景中有這個元件時 <see cref="EnvironmentController"/> 不會建立，
/// 改用固定的室內照明 (Flat 環境光 + 淡淡的粉塵霧)，天窗透進的日光仍由場景的平行光提供。
/// </summary>
public class IndoorLighting : MonoBehaviour
{
    [SerializeField] private Color ambient = new Color(0.42f, 0.44f, 0.48f);
    [SerializeField] private Color fogColor = new Color(0.55f, 0.57f, 0.6f);
    [SerializeField] private float fogStart = 30f;
    [SerializeField] private float fogEnd = 220f;
    [Tooltip("天窗日光強度 (平行光)")]
    [SerializeField] private float sunIntensity = 1.1f;

    private void Awake()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambient;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance = fogEnd;

        Light sun = RenderSettings.sun;
        if (sun == null)
        {
            foreach (Light candidate in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (candidate.type == LightType.Directional) { sun = candidate; break; }
            }
        }
        if (sun != null) sun.intensity = sunIntensity;
    }
}

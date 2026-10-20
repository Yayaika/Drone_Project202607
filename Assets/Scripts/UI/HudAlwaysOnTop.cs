using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// 世界空間 HUD 永遠顯示在最上層：HUD 底下所有 UI (Image、TMP 文字、按鍵圖示) 改用「深度測試永遠通過」的材質，
/// 無人機貼近牆面、建築或其他物件時 HUD 不會被遮住。
/// 執行時新增的 UI (結算畫面、彈出面板) 會定期補上。
/// </summary>
public class HudAlwaysOnTop : MonoBehaviour
{
    [Tooltip("檢查新增 UI 的間隔 (秒，不受暫停影響)")]
    [SerializeField] private float rescanInterval = 0.5f;

    private float nextScan;

    // 不調整 Canvas 排序：排序會影響點擊判定的先後，巢狀 Canvas 上的按鈕可能被蓋住
    private void OnEnable()
    {
        Scan();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextScan) return;
        Scan();
    }

    private void Scan()
    {
        nextScan = Time.unscaledTime + rescanInterval;
        foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
        {
            if (graphic.GetComponent<UIZTestAlways>() != null) continue;
            graphic.gameObject.AddComponent<UIZTestAlways>();
        }
    }
}

/// <summary>把 UI 材質的深度測試改為 Always (UI/Default 與 TextMeshPro 著色器都使用 unity_GUIZTestMode)</summary>
[DisallowMultipleComponent]
public class UIZTestAlways : MonoBehaviour, IMaterialModifier
{
    private static readonly int ZTestMode = Shader.PropertyToID("unity_GUIZTestMode");
    private static readonly Dictionary<Material, Material> cache = new Dictionary<Material, Material>();

    private Graphic graphic;

    private void OnEnable()
    {
        graphic = GetComponent<Graphic>();
        if (graphic != null) graphic.SetMaterialDirty();
    }

    private void OnDisable()
    {
        if (graphic != null) graphic.SetMaterialDirty();
    }

    public Material GetModifiedMaterial(Material baseMaterial)
    {
        if (!isActiveAndEnabled || baseMaterial == null) return baseMaterial;
        if (!cache.TryGetValue(baseMaterial, out Material overlay) || overlay == null)
        {
            overlay = new Material(baseMaterial) { name = baseMaterial.name + " (HUD On Top)" };
            overlay.SetInt(ZTestMode, (int)CompareFunction.Always);
            cache[baseMaterial] = overlay;
        }
        return overlay;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cache.Clear();
    }
}

using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Slider))]
public class VolumeController : MonoBehaviour
{
    private Slider volumeSlider;

    void Awake()
    {
        volumeSlider = GetComponent<Slider>();
    }

    void Start()
    {
        if (volumeSlider != null)
        {
            // 初始化 Slider 數值為當前系統全域音量 (0.0 ~ 1.0)
            AudioListener.volume = 0.1f;
            volumeSlider.value = 0.1f;

            // 綁定數值改變時的監聽事件
            volumeSlider.onValueChanged.AddListener(SetVolume);
        }
    }

    /// <summary>
    /// 調整全域音量
    /// </summary>
    public void SetVolume(float value)
    {
        AudioListener.volume = value;
    }

    private void OnDestroy()
    {
        if (volumeSlider != null)
        {
            volumeSlider.onValueChanged.RemoveListener(SetVolume);
        }
    }
}
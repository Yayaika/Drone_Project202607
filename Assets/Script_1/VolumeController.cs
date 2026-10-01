using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Slider))]
public class VolumeController : MonoBehaviour
{
    private Slider volumeSlider;
    private const string VOLUME_KEY = "MasterVolume";
    private const float DEFAULT_VOLUME = 0.1f;

    private bool isInitializing = false; // 防止初始化過程觸發 OnValueChanged

    void Awake()
    {
        volumeSlider = GetComponent<Slider>();
    }

    void Start()
    {
        if (volumeSlider != null)
        {
            isInitializing = true; // 開啟防護

            // 1. 讀取儲存的音量
            float savedVolume = PlayerPrefs.GetFloat(VOLUME_KEY, DEFAULT_VOLUME);

            // 2. 同步設定音量與 Slider 數值
            AudioListener.volume = savedVolume;
            volumeSlider.value = savedVolume;

            // 3. 綁定事件監聽
            volumeSlider.onValueChanged.RemoveListener(SetVolume);
            volumeSlider.onValueChanged.AddListener(SetVolume);

            isInitializing = false; // 初始化完成，解除防護
        }
    }

    public void SetVolume(float value)
    {
        // 如果正在初始化，直接略過，不寫入音量與 PlayerPrefs
        if (isInitializing) return;

        AudioListener.volume = value;
        PlayerPrefs.SetFloat(VOLUME_KEY, value);
        PlayerPrefs.Save(); // 即時寫入檔案
    }
}
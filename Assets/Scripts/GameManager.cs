using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    // 🌟 透過 Property 監聽，當 SpawnCtrl 填入 droneTransform 時立刻觸發 UI 綁定
    private Transform _droneTransform;
    public Transform droneTransform
    {
        get => _droneTransform;
        set
        {
            _droneTransform = value;
            FindAndBindDroneUI(); // 立刻綁定動態生成的無人機 UI
        }
    }

    [Header("【 關卡與場景設定 】")]
    public string nextSceneName = "City2";   // 要切換的下一個關卡場景名稱
    public int totalCheckpoints = 6;         // 總檢查點數量
    public int passedCheckpoints = 0;

    [Header("【 UI 組件 】")]
    public TextMeshProUGUI timerText;          // TimerText
    public TextMeshProUGUI progressText;       // ProgressText
    public TextMeshProUGUI centerDisplayText;  // [Text]Finish
    public Button nextStageButton;             // [Button]NextStage
    public Slider progressSlider;              // [Slider]進度

    private float elapsedTime = 0f;
    private bool isGameFinished = false;
    private bool isTimerRunning = true;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        FindAndBindDroneUI();
    }

    void Update()
    {
        // 🌟 計時器每幀更新
        if (isTimerRunning && !isGameFinished)
        {
            elapsedTime += Time.deltaTime;
            UpdateTimerUI();
        }
    }

    // 每次通過檢查點時呼叫
    public void CheckpointPassed(int index)
    {
        if (isGameFinished) return;

        passedCheckpoints++;
        UpdateProgressUI();

        if (passedCheckpoints >= totalCheckpoints)
        {
            TriggerWin();
        }
    }

    // 🌟 更新 TimerText 格式 (修正為 {0:00})
    private void UpdateTimerUI()
    {
        if (timerText != null)
        {
            int minutes = Mathf.FloorToInt(elapsedTime / 60f);
            int seconds = Mathf.FloorToInt(elapsedTime % 60f);
            int fraction = Mathf.FloorToInt((elapsedTime * 100f) % 100f);

            // 修正為 {0:00} 格式，時間會正常顯示為 TIME: 01:15.23
            timerText.text = string.Format("TIME: {0:00}:{1:00}.{2:00}", minutes, seconds, fraction);
        }
        else
        {
            FindAndBindDroneUI();
        }
    }

    // 更新 ProgressText 與 Slider UI
    private void UpdateProgressUI()
    {
        if (progressText != null)
        {
            progressText.text = $"CHECKPOINT: {passedCheckpoints} / {totalCheckpoints}";
        }

        if (progressSlider != null)
        {
            progressSlider.maxValue = totalCheckpoints;
            progressSlider.value = passedCheckpoints;
        }
    }

    // 自動尋找無人機底下的所有 UI 組件
    public void FindAndBindDroneUI()
    {
        GameObject droneObj = null;
        if (droneTransform != null)
        {
            droneObj = droneTransform.gameObject;
        }
        else
        {
            droneObj = GameObject.FindWithTag("Player");
            if (droneObj == null)
            {
                droneObj = GameObject.Find("Drone_Parent Variant(Clone)");
            }
        }

        if (droneObj != null)
        {
            // 搜尋所有 TextMeshProUGUI
            TextMeshProUGUI[] uiTexts = droneObj.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var t in uiTexts)
            {
                if (t.name.Contains("TimerText") || t.name.Contains("Timer"))
                {
                    timerText = t;
                }
                else if (t.name.Contains("ProgressText") || t.name.Contains("Progress"))
                {
                    progressText = t;
                }
                else if (t.name.Contains("Finish") || t.name.Contains("Center") || t.name.Contains("Display"))
                {
                    centerDisplayText = t;
                }
            }

            // 搜尋按鈕
            Button[] buttons = droneObj.GetComponentsInChildren<Button>(true);
            foreach (var btn in buttons)
            {
                if (btn.name.Contains("NextStage") || btn.name.Contains("NextGame"))
                {
                    nextStageButton = btn;
                }
            }

            // 搜尋 Slider
            Slider[] sliders = droneObj.GetComponentsInChildren<Slider>(true);
            if (sliders.Length > 0)
            {
                progressSlider = sliders[0];
            }

            // 確保 Canvas 有 GraphicRaycaster（供按鈕點擊）
            Canvas canvas = droneObj.GetComponentInChildren<Canvas>(true);
            if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }

            UpdateProgressUI();
        }
    }

    // 通關結算
    public void TriggerWin()
    {
        if (isGameFinished) return;
        isGameFinished = true;
        isTimerRunning = false; // 停止計時

        Debug.Log("【GameManager】通關！開啟 Finish! 文字與 NextStage 按鈕");

        FindAndBindDroneUI();

        if (centerDisplayText != null)
        {
            centerDisplayText.text = "Finish!";
            centerDisplayText.gameObject.SetActive(true);
        }

        if (nextStageButton != null)
        {
            nextStageButton.onClick.RemoveAllListeners();
            nextStageButton.onClick.AddListener(OnNextStageButtonClicked);
            nextStageButton.gameObject.SetActive(true);
        }
    }

    // 按鈕點擊：切換場景
    public void OnNextStageButtonClicked()
    {
        Debug.Log($"【GameManager】切換場景至：{nextSceneName}");
        SceneManager.LoadScene(nextSceneName);
    }
}
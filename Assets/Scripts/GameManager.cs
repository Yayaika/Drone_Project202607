using UnityEngine;
using System.Linq;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.UI;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("【Drone Reference】")]
    [Tooltip("無人機根物件 Transform")]
    public Transform droneTransform;

    [Header("【Checkpoints System】")]
    [Tooltip("場景中所有的檢查點環節")]
    public CheckpointRing[] rings;
    [Tooltip("目前已通過的檢查點數量")]
    public int passedCheckpoints = 0;

    private int _totalCheckpoints = 0;
    public int totalCheckpoints
    {
        get
        {
            if (_totalCheckpoints <= 0)
            {
                RefreshCheckpointsData();
            }
            return _totalCheckpoints;
        }
        set { _totalCheckpoints = value; }
    }

    [Header("【HUD UI (TextMeshPro)】")]
    [Tooltip("顯示比賽時間的 TMP 文字組件")]
    public TextMeshProUGUI timerText;
    [Tooltip("顯示檢查點進度的 TMP 文字組件")]
    public TextMeshProUGUI progressText;

    [Header("【VR Clear Stage Button】")]
    [Tooltip("通關時顯示的下關按鈕")]
    public Button nextStageButton;

    // 參照變數
    private Canvas hudCanvas;
    private Camera currentActiveCam;

    private float elapsedTime = 0f;
    private bool isGameFinished = false;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (nextStageButton != null)
        {
            nextStageButton.gameObject.SetActive(false);
        }

        Invoke(nameof(InitializeGame), 0.5f);
    }

    void Update()
    {
        if (droneTransform == null || timerText == null || progressText == null || nextStageButton == null)
        {
            FindDynamicDrone();
        }

        if (!isGameFinished)
        {
            elapsedTime += Time.deltaTime;
            UpdateHUDTimer();
        }
    }

    private void InitializeGame()
    {
        FindDynamicDrone();
        RefreshCheckpointsData();
        FixTreeColliders();
        UpdateHUDProgress();
        UpdateHUDCanvasCamera();
    }

    /// <summary>
    /// 更新 HUD Canvas 的渲染攝影機，但不干涉其 Transform / Parent 關係
    /// </summary>
    public void UpdateHUDCanvasCamera()
    {
        if (droneTransform == null) return;

        currentActiveCam = Camera.main;
        if (currentActiveCam == null || !currentActiveCam.gameObject.activeInHierarchy)
        {
            Camera[] allCams = droneTransform.GetComponentsInChildren<Camera>(false);
            if (allCams.Length > 0) currentActiveCam = allCams[0];
            else currentActiveCam = Camera.current;
        }

        if (hudCanvas == null && timerText != null)
        {
            hudCanvas = timerText.GetComponentInParent<Canvas>();
        }

        if (hudCanvas != null && currentActiveCam != null)
        {
            hudCanvas.renderMode = RenderMode.WorldSpace;
            hudCanvas.worldCamera = currentActiveCam; // 只指派事件/渲染相機
        }
    }

    public void RefreshCheckpointsData()
    {
        CheckpointRing[] foundRings = FindObjectsByType<CheckpointRing>(FindObjectsSortMode.None);

        if (foundRings != null && foundRings.Length > 0)
        {
            rings = foundRings.OrderBy(r => r.checkpointIndex).ToArray();
            _totalCheckpoints = rings.Length;
        }
        else
        {
            CourseBuilder builder = FindFirstObjectByType<CourseBuilder>();
            if (builder != null)
            {
                _totalCheckpoints = builder.checkpointCount;
            }
        }
    }

    private void FindDynamicDrone()
    {
        GameObject droneObj = GameObject.FindWithTag("Player");

        if (droneObj == null)
        {
            DroneController1 controller = FindFirstObjectByType<DroneController1>();
            if (controller != null) droneObj = controller.gameObject;
        }

        if (droneObj != null)
        {
            droneTransform = droneObj.transform;

            if (timerText == null || progressText == null || nextStageButton == null)
            {
                TextMeshProUGUI[] uiTexts = droneObj.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var t in uiTexts)
                {
                    if (t.name.Contains("Timer")) timerText = t;
                    if (t.name.Contains("Progress")) progressText = t;
                }

                Button btn = droneObj.GetComponentInChildren<Button>(true);
                if (btn != null && btn.name.Contains("NextStage"))
                {
                    nextStageButton = btn;
                    nextStageButton.onClick.RemoveAllListeners();
                    nextStageButton.onClick.AddListener(OnNextStageButtonClicked);
                    nextStageButton.gameObject.SetActive(false);
                }
            }

            if (hudCanvas == null && timerText != null)
            {
                hudCanvas = timerText.GetComponentInParent<Canvas>();
            }

            UpdateHUDCanvasCamera();
        }
    }

    public void CheckpointPassed(int index)
    {
        if (index == passedCheckpoints && !isGameFinished)
        {
            passedCheckpoints++;

            if (_totalCheckpoints <= 0) RefreshCheckpointsData();

            UpdateHUDProgress();

            if (droneTransform == null) FindDynamicDrone();

            Vector3 newRespawnPos = Vector3.zero;
            if (rings != null && index >= 0 && index < rings.Length && rings[index] != null)
            {
                newRespawnPos = rings[index].transform.position + Vector3.up * 0.5f;
            }

            if (newRespawnPos != Vector3.zero && droneTransform != null)
            {
                droneTransform.SendMessage("SetRespawnPoint", newRespawnPos, SendMessageOptions.DontRequireReceiver);
            }

            if (passedCheckpoints < totalCheckpoints)
            {
                if (rings != null && passedCheckpoints < rings.Length)
                {
                    rings[passedCheckpoints].MarkActive();
                }
            }
            else
            {
                isGameFinished = true;
                if (progressText != null)
                {
                    int minutes = (int)(elapsedTime / 60f);
                    int seconds = (int)(elapsedTime % 60f);
                    progressText.text = $"<color=yellow>STAGE CLEAR!</color>\nTIME: {minutes:00}:{seconds:00}";
                }

                if (nextStageButton != null)
                {
                    nextStageButton.gameObject.SetActive(true);
                }
            }
        }
    }

    public void OnNextStageButtonClicked()
    {
        int nextSceneIndex = SceneManager.GetActiveScene().buildIndex + 1;

        if (nextSceneIndex < SceneManager.sceneCountInBuildSettings)
        {
            SceneManager.LoadScene(nextSceneIndex);
        }
        else
        {
            Debug.Log("[GameManager] Last scene reached. Reloading scene 0.");
            SceneManager.LoadScene(0);
        }
    }

    private void UpdateHUDTimer()
    {
        if (timerText != null)
        {
            int minutes = (int)(elapsedTime / 60f);
            int seconds = (int)(elapsedTime % 60f);
            int milliseconds = (int)((elapsedTime * 100f) % 100f);
            timerText.text = $"TIME: {minutes:00}:{seconds:00}.{milliseconds:00}";
        }
    }

    private void UpdateHUDProgress()
    {
        if (progressText != null)
        {
            progressText.text = $"CHECKPOINT: {passedCheckpoints} / {totalCheckpoints}";
        }
    }

    public void FixTreeColliders()
    {
        SphereCollider[] allSphereCols = FindObjectsByType<SphereCollider>(FindObjectsSortMode.None);
        foreach (SphereCollider sphereCol in allSphereCols)
        {
            if (sphereCol.gameObject.name.Equals("Sphere") &&
                sphereCol.transform.parent != null &&
                sphereCol.transform.parent.name.Contains("Tree"))
            {
                Vector3 scale = sphereCol.transform.localScale;
                float maxScaleAxis = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                float minHorizontalAxis = Mathf.Min(scale.x, scale.z);

                if (maxScaleAxis > 0)
                {
                    sphereCol.radius = (minHorizontalAxis / (maxScaleAxis * 2f)) * 0.95f;
                    sphereCol.center = Vector3.zero;
                }
            }
        }
    }
}
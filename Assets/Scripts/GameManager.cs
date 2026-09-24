using UnityEngine;
using System.Linq;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.UI;
using System.Collections;

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

    [Header("【VR HUD 3D 空間跟隨與 Inspector 調校】")]
    [Tooltip("HUD 懸浮在鏡頭正前方的 3D 距離（米）")]
    [Range(0.2f, 3.0f)]
    public float hudDistance = 0.5f;

    [Tooltip("HUD 3D 空間相對位移 (X: 左右, Y: 上下, Z: 前後微調)")]
    public Vector3 hudOffset = new Vector3(0f, -0.08f, 0f);

    [Tooltip("HUD Canvas 的整體 3D 縮放大小")]
    public Vector3 hudScale = new Vector3(0.0008f, 0.0008f, 0.0008f);

    [Tooltip("HUD 旋轉與移動的跟隨速度（已換成直接跟隨，此變數暫不影響）")]
    public float followSpeed = 25f;

    [Header("【滾輪動態微調設定】")]
    [Tooltip("使用滑鼠滾輪調整 HUD 上下位置的靈敏度")]
    public float scrollSensitivity = 0.05f;

    [Header("【比賽流程控制】")]
    public TextMeshProUGUI centerDisplayText; // 拖入畫面正中央的大字體 UI
    public bool hasRaceStarted = false;
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

        // 按下 C 鍵切換無人機視角時，重新獲取當前啟用的攝影機
        if (Input.GetKeyDown(KeyCode.C))
        {
            Invoke(nameof(UpdateHUDCanvasCamera), 0.05f);
        }

        // 監聽滑鼠滾輪：動態調整 HUD 上下位置 (Y 軸位移)
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scrollInput) > 0.01f)
        {
            hudOffset.y += scrollInput * scrollSensitivity;
        }

        // 即時套用 Scale
        if (hudCanvas != null)
        {
            hudCanvas.transform.localScale = hudScale;
        }

        if (hasRaceStarted && !isGameFinished)
        {
            elapsedTime += Time.deltaTime;
            UpdateHUDTimer();
        }
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
        StartCoroutine(RaceCountdownRoutine());
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

    /*public void CheckpointPassed(int index)
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
    }*/

    public void CheckpointPassed(int index)
    {
        if (index == passedCheckpoints && !isGameFinished)
        {
            passedCheckpoints++;

            //if (_totalCheckpoints <= 0) RefreshCheckpointsData();

            UpdateHUDProgress();

            if (droneTransform == null) FindDynamicDrone();

            // ！！已經刪除舊版強制設定重生點 (SetRespawnPoint) 的程式碼！！
            // 讓重生座標的控制權 100% 交給 RaceManager 處理

            /*if (passedCheckpoints < totalCheckpoints)
            {
                if (rings != null && passedCheckpoints < rings.Length)
                {
                    rings[passedCheckpoints].MarkActive();
                }
            }*/
            bool isFinalRing = (rings != null && index == rings.Length - 1);
            if (!isFinalRing)
            {
                // 如果不是最後一個，就繼續把下一個圓圈亮起來
                if (rings != null && passedCheckpoints < rings.Length)
                {
                    rings[passedCheckpoints].MarkActive();
                }
            }
            /*else
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
            }*/
            else
            {
                isGameFinished = true;
                if (progressText != null) progressText.text = $"<color=yellow>STAGE CLEAR!</color>";

                // 👇 新增結算畫面與鎖定無人機
                if (centerDisplayText != null)
                {
                    centerDisplayText.gameObject.SetActive(true);
                    int minutes = (int)(elapsedTime / 60f);
                    int seconds = (int)(elapsedTime % 60f);
                    int milliseconds = (int)((elapsedTime * 100f) % 100f);
                    centerDisplayText.text = $"<color=yellow>FINISH!</color>\n<size=50>TIME: {minutes:00}:{seconds:00}.{milliseconds:00}</size>";
                }

                if (droneTransform != null)
                {
                    DroneController1 drone = droneTransform.GetComponent<DroneController1>();
                    if (drone != null) drone.canControl = false; // 抵達終點鎖定控制
                }
                
                if (nextStageButton != null) nextStageButton.gameObject.SetActive(true);
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
    private IEnumerator RaceCountdownRoutine()
    {
        yield return new WaitUntil(() => droneTransform != null);
        DroneController1 drone = droneTransform.GetComponent<DroneController1>();
        
        if (drone != null) drone.canControl = false; // 鎖定操作

        if (centerDisplayText != null)
        {
            centerDisplayText.gameObject.SetActive(true);
            centerDisplayText.text = "<color=red>3</color>";
            yield return new WaitForSeconds(1f);
            centerDisplayText.text = "<color=orange>2</color>";
            yield return new WaitForSeconds(1f);
            centerDisplayText.text = "<color=yellow>1</color>";
            yield return new WaitForSeconds(1f);
            centerDisplayText.text = "<color=green>GO!</color>";
        }

        hasRaceStarted = true; // 正式開始計時
        if (drone != null)
        {
            drone.canControl = true; // 解鎖操作
            drone.isArmed = true;    // 發動引擎
        }

        yield return new WaitForSeconds(1f);
        if (centerDisplayText != null) centerDisplayText.text = "";
    }
}
using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 關卡流程管理：開場倒數 → 計時 → 進度 (競速檢查點 / 搬運訂單) → 結算畫面與排行榜。
/// 競速模式由檢查點呼叫 <see cref="CheckpointPassed(int, Transform)"/>；
/// 搬運模式由 <see cref="DeliveryMissionManager"/> 呼叫 <see cref="CompleteLevel"/>。
/// </summary>
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
    public string nextSceneName = "City2";   // 要切換的下一個關卡場景名稱 (無關卡目錄資料時使用)
    public int totalCheckpoints = 6;         // 總檢查點數量
    public int passedCheckpoints = 0;

    [Header("【 開場倒數 】")]
    [Tooltip("開場倒數秒數，0 = 不倒數直接開始")]
    public int countdownSeconds = 3;

    [Header("【 起飛位置 (競速) 】")]
    [Tooltip("開場把無人機放在第一個檢查圈前方這個距離 (m)，並面向檢查圈")]
    public float startDistanceBeforeFirstGate = 14f;
    [Tooltip("起飛位置離地最低高度 (m)")]
    public float startMinGroundClearance = 2.5f;

    [Header("【 UI 組件 】")]
    public TextMeshProUGUI timerText;          // TimerText
    public TextMeshProUGUI progressText;       // ProgressText
    public TextMeshProUGUI centerDisplayText;  // [Text]Finish
    public Button nextStageButton;             // [Button]NextStage
    public Slider progressSlider;              // [Slider]進度

    private float elapsedTime = 0f;
    private bool isGameFinished = false;
    private bool isTimerRunning = false;
    private string progressOverride;

    // 檢查點重生
    private bool hasCheckpointRespawn;
    private Vector3 lastCheckpointPosition;
    private Vector3 lastCheckpointFacing = Vector3.forward;
    private DroneController1 subscribedDrone;

    public float ElapsedTime => elapsedTime;
    public bool IsRunning => isTimerRunning && !isGameFinished;
    public bool IsFinished => isGameFinished;
    public GameModeType Mode => LevelCatalog.SelectedMode;
    public LevelDefinition Level => LevelCatalog.CurrentLevel;

    /// <summary>倒數結束、比賽正式開始時觸發</summary>
    public event System.Action OnRaceStarted;

    void Awake()
    {
        Instance = this;
        EnsureEventSystem();
    }

    void Start()
    {
        FindAndBindDroneUI();
        UpdateProgressUI();
        GameAudio.PlayMusic(Mode == GameModeType.Delivery ? GameMusic.Delivery : GameMusic.Race);

        // 搬運模式：場景沒有放任務管理器時自動建立 (程序化城市會先自行建立並帶入貨物 Prefab)
        if (Mode == GameModeType.Delivery && FindFirstObjectByType<DeliveryMissionManager>() == null)
        {
            new GameObject("DeliveryMissionManager").AddComponent<DeliveryMissionManager>();
        }
        if (Mode == GameModeType.Strike && FindFirstObjectByType<StrikeMissionManager>() == null)
        {
            new GameObject("StrikeMissionManager").AddComponent<StrikeMissionManager>();
        }
        StartCoroutine(StartSequence());
    }

    private void OnDestroy()
    {
        if (subscribedDrone != null)
        {
            subscribedDrone.OnRespawnPressed -= RespawnAtLastCheckpoint;
        }
        if (Instance == this) Instance = null;
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

    #region 開場倒數

    private IEnumerator StartSequence()
    {
        // 等一幀，讓 SpawnCtrl / RaceManager / 任務管理器完成初始化與無人機定位
        yield return null;

        // 賽道 (RaceManager / 城市生成器) 可能在本元件 Start 之後才設定總檢查點數，這裡重新整理顯示
        UpdateProgressUI();
        DroneController1 drone = GetDrone();
        // 競速：不論場景出生點在哪，都改到第一個檢查圈前方並面向它
        if (Mode == GameModeType.Race && drone != null) PlaceDroneAtCourseStart(drone);
        BindRespawn(drone);
        if (drone != null) drone.canControl = false;

        if (centerDisplayText != null && countdownSeconds > 0)
        {
            centerDisplayText.gameObject.SetActive(true);
            centerDisplayText.alpha = 1f;
            for (int i = countdownSeconds; i > 0; i--)
            {
                ShowCenterText(i.ToString(), Color.white);
                GameAudio.Play(GameSfx.CountdownBeep);
                yield return new WaitForSeconds(1f);
            }
        }
        else if (countdownSeconds > 0)
        {
            yield return new WaitForSeconds(countdownSeconds);
        }

        if (drone != null) drone.canControl = true;
        isTimerRunning = true;
        GameAudio.Play(GameSfx.CountdownGo);
        ShowCenterMessage("GO!", new Color(0.3f, 1f, 0.5f), 0.9f);
        OnRaceStarted?.Invoke();
    }

    #endregion

    #region 競速進度

    // 每次通過檢查點時呼叫 (舊版相容)
    public void CheckpointPassed(int index)
    {
        CheckpointPassed(index, null);
    }

    /// <summary>通過檢查點，傳入檢查點 Transform 可作為之後按 R 的重生點</summary>
    public void CheckpointPassed(int index, Transform checkpoint)
    {
        if (isGameFinished) return;

        passedCheckpoints++;
        UpdateProgressUI();
        GameAudio.Play(GameSfx.Checkpoint);

        if (checkpoint != null)
        {
            RecordRespawnPoint(checkpoint.position, checkpoint.forward);
        }

        if (passedCheckpoints >= totalCheckpoints)
        {
            TriggerWin();
        }
        else if (progressText != null)
        {
            UITween.Pop(progressText.transform, 0.25f);
        }
    }

    /// <summary>設定 R 鍵重生位置，朝向 = 下一段要飛的方向</summary>
    public void RecordRespawnPoint(Vector3 position, Vector3 facing)
    {
        hasCheckpointRespawn = true;
        lastCheckpointPosition = position;
        if (TryFlatDirection(facing, out Vector3 flat)) lastCheckpointFacing = flat;
    }

    /// <summary>
    /// 傳給 DroneController1.RespawnAtCheckpoint 的旋轉，讓「玩家看到的前方」(第一人稱鏡頭/機頭) 朝向 facing。
    /// 無人機模型的機頭是 transform 的 -Z，RespawnAtCheckpoint 內部會再加 180 度；
    /// 這裡以鏡頭錨點實際量出機頭與 transform 的夾角，不依賴模型的擺放方向。
    /// </summary>
    private static Quaternion RespawnRotationFacing(DroneController1 drone, Vector3 facing)
    {
        float noseOffset = 0f;
        if (TryFlatDirection(drone.transform.forward, out Vector3 body) && TryFlatDirection(drone.FirstPersonAnchor.forward, out Vector3 nose))
        {
            noseOffset = Vector3.SignedAngle(body, nose, Vector3.up);
        }
        float targetYaw = Quaternion.LookRotation(facing).eulerAngles.y;
        // 機身 yaw = 目標 - 機頭偏角；再扣掉 RespawnAtCheckpoint 會加上的 180 度
        return Quaternion.Euler(0f, targetYaw - noseOffset - 180f, 0f);
    }

    private void BindRespawn(DroneController1 drone)
    {
        if (drone == null || subscribedDrone == drone) return;

        // 所有場景 (含 City2) 都由 GameManager 負責按 R 回到最後檢查點
        subscribedDrone = drone;
        drone.OnRespawnPressed += RespawnAtLastCheckpoint;
        // RaceManager 已先記錄起飛點時不要覆蓋
        if (!hasCheckpointRespawn) RecordRespawnPoint(drone.transform.position, drone.FirstPersonAnchor.forward);
    }

    private void RespawnAtLastCheckpoint()
    {
        if (subscribedDrone == null || !hasCheckpointRespawn) return;

        DronePickupHandler pickup = subscribedDrone.GetComponent<DronePickupHandler>();
        if (pickup != null && pickup.IsCarrying) pickup.ForceDrop();

        // 重生後面向下一個要穿越的檢查圈 (沒有檢查圈時沿用記錄的朝向)
        Vector3 facing = lastCheckpointFacing;
        RingTrigger next = FindCheckpoint(passedCheckpoints);
        if (next != null && TryFlatDirection(next.transform.position - lastCheckpointPosition, out Vector3 toNext))
        {
            facing = toNext;
        }
        subscribedDrone.RespawnAtCheckpoint(lastCheckpointPosition, RespawnRotationFacing(subscribedDrone, facing));
    }

    /// <summary>把無人機傳送到指定位置並面向 facing，同時設為之後按 R 的重生點 (打擊模式撞擊後重新部署)</summary>
    public void RespawnAt(Vector3 position, Vector3 facing)
    {
        RecordRespawnPoint(position, facing);
        DroneController1 drone = subscribedDrone != null ? subscribedDrone : GetDrone();
        if (drone == null) return;
        drone.RespawnAtCheckpoint(position, RespawnRotationFacing(drone, lastCheckpointFacing));
    }

    private static RingTrigger FindCheckpoint(int index)
    {
        foreach (RingTrigger ring in RingTrigger.All)
        {
            if (ring != null && ring.checkpointIndex == index) return ring;
        }
        return null;
    }

    private static bool TryFlatDirection(Vector3 direction, out Vector3 flat)
    {
        flat = new Vector3(direction.x, 0f, direction.z);
        if (flat.sqrMagnitude < 0.01f) return false;
        flat.Normalize();
        return true;
    }

    /// <summary>
    /// 把無人機放到第一個檢查圈前方 (沿「第二圈 → 第一圈」的方向往後退)，高度與第一圈相同，面向第一圈。
    /// 依序嘗試不同距離與偏角，選第一個不在建築內、離地足夠且看得到第一圈的位置；都不行時只轉向。
    /// </summary>
    private void PlaceDroneAtCourseStart(DroneController1 drone)
    {
        RingTrigger first = FindCheckpoint(0);
        if (first == null) return;

        Vector3 gate = first.transform.position;
        RingTrigger second = FindCheckpoint(1);
        if (!TryFlatDirection(second != null ? gate - second.transform.position : drone.transform.position - gate, out Vector3 back))
        {
            back = Vector3.back;
        }

        Vector3 start = drone.transform.position;
        bool found = false;
        float[] distanceScales = { 1f, 0.75f, 1.3f, 0.5f, 1.7f };
        float[] yawOffsets = { 0f, 25f, -25f, 50f, -50f };
        foreach (float yaw in yawOffsets)
        {
            foreach (float scale in distanceScales)
            {
                Vector3 candidate = gate + Quaternion.Euler(0f, yaw, 0f) * back * (startDistanceBeforeFirstGate * scale);
                if (TryStartPoint(candidate, gate, drone.transform, out start))
                {
                    found = true;
                    break;
                }
            }
            if (found) break;
        }
        if (!found)
        {
            start = drone.transform.position;
            Debug.LogWarning("[GameManager] 第一個檢查圈前方找不到安全的起飛位置，保留場景出生點，只轉向第一圈。");
        }

        if (!TryFlatDirection(gate - start, out Vector3 facing)) facing = -back;
        Quaternion rotation = RespawnRotationFacing(drone, facing);
        drone.RespawnAtCheckpoint(start, rotation);
        // 起飛點的旋轉與 RespawnAtCheckpoint 參數同一套定義 (沒有 GameManager 時的備用重生也朝向第一圈)
        drone.SetHomePoint(start, rotation);
        RecordRespawnPoint(start, facing);
    }

    private bool TryStartPoint(Vector3 candidate, Vector3 gate, Transform drone, out Vector3 result)
    {
        result = candidate;
        // 下方要有地面 (不在地圖外)，並保持離地高度
        if (!Physics.Raycast(candidate + Vector3.up, Vector3.down, out RaycastHit ground, 120f, ~0, QueryTriggerInteraction.Ignore)) return false;
        if (ground.transform.IsChildOf(drone)) return false;
        result.y = Mathf.Max(candidate.y, ground.point.y + startMinGroundClearance);

        // 周圍淨空
        foreach (Collider hit in Physics.OverlapSphere(result, 1.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!hit.transform.IsChildOf(drone)) return false;
        }

        // 從檢查圈往起飛點看得到 (由外往內打射線，起飛點在建築內時會打到外牆)
        Vector3 toStart = result - gate;
        foreach (RaycastHit hit in Physics.RaycastAll(gate, toStart.normalized, toStart.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!hit.transform.IsChildOf(drone)) return false;
        }
        return true;
    }

    #endregion

    #region UI

    // 🌟 更新 TimerText 格式 (修正為 {0:00})
    private void UpdateTimerUI()
    {
        if (timerText != null)
        {
            timerText.text = "TIME: " + RuntimeUI.FormatTime(elapsedTime);
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
            progressText.text = progressOverride ?? $"CHECKPOINT: {passedCheckpoints} / {totalCheckpoints}";
        }

        if (progressSlider != null && progressOverride == null)
        {
            progressSlider.maxValue = Mathf.Max(1, totalCheckpoints);
            progressSlider.value = passedCheckpoints;
        }
    }

    /// <summary>搬運模式等自訂進度文字 (傳 null 恢復檢查點顯示)</summary>
    public void SetProgress(string text, float value01)
    {
        progressOverride = text;
        if (progressText != null) progressText.text = text ?? string.Empty;
        if (progressSlider != null)
        {
            progressSlider.maxValue = 1f;
            progressSlider.value = Mathf.Clamp01(value01);
        }
        if (text == null) UpdateProgressUI();
    }

    /// <summary>畫面中央短暫顯示訊息 (DOTween 彈出後淡出)</summary>
    public void ShowCenterMessage(string message, Color color, float holdSeconds = 1.5f)
    {
        if (centerDisplayText == null) FindAndBindDroneUI();
        if (centerDisplayText == null) return;

        ShowCenterText(message, color);
        centerDisplayText.DOFade(0f, 0.4f).SetDelay(holdSeconds).SetUpdate(true)
            .OnComplete(() =>
            {
                if (centerDisplayText != null && !isGameFinished) centerDisplayText.gameObject.SetActive(false);
            });
    }

    private void ShowCenterText(string message, Color color)
    {
        centerDisplayText.DOKill();
        centerDisplayText.gameObject.SetActive(true);
        centerDisplayText.text = message;
        centerDisplayText.color = color;
        centerDisplayText.alpha = 1f;
        UITween.Pop(centerDisplayText.transform, 0.5f, 0.5f);
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
            if (droneObj == null)
            {
                DroneController1 controller = FindFirstObjectByType<DroneController1>();
                if (controller != null) droneObj = controller.gameObject;
            }
        }

        if (droneObj != null)
        {
            // HUD Canvas 可能已被 VRHUDFollower 移到攝影機底下，但攝影機仍在無人機階層內
            Transform searchRoot = droneObj.transform;
            DroneFlightHUD hud = FindHudFor(droneObj);
            Transform hudRoot = hud != null ? hud.transform : searchRoot;

            // 搜尋所有 TextMeshProUGUI
            TextMeshProUGUI[] uiTexts = hudRoot.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var t in uiTexts)
            {
                if (t.GetComponentInParent<ResultScreen>(true) != null) continue; // 結算畫面自己的文字

                if (t.name.Contains("TimerText") || t.name.Contains("Timer"))
                {
                    if (t.name == "FlightTimer") continue; // OSD 的飛行時間，不是比賽計時
                    timerText = t;
                }
                else if (t.name.Contains("ProgressText") || t.name.Contains("Progress"))
                {
                    progressText = t;
                }
                else if (t.name.Contains("Finish") || t.name.Contains("Center") || t.name.Contains("Display"))
                {
                    if (t.name == "CenterMarker") continue; // 人工地平線中心十字
                    centerDisplayText = t;
                }
            }

            // 搜尋按鈕
            Button[] buttons = hudRoot.GetComponentsInChildren<Button>(true);
            foreach (var btn in buttons)
            {
                if (btn.name.Contains("NextStage") || btn.name.Contains("NextGame"))
                {
                    nextStageButton = btn;
                }
            }

            // 修正後的 Slider 搜尋邏輯：過濾名字，避開 Volume / 音量 Slider
            Slider[] sliders = hudRoot.GetComponentsInChildren<Slider>(true);
            foreach (var s in sliders)
            {
                // 只有名字包含 Progress 或 Checkpoint 的才認定為進度條
                if (s.name.Contains("Progress") || s.name.Contains("Checkpoint") || s.name.Contains("進度"))
                {
                    progressSlider = s;
                    break;
                }
            }

            // 確保 Canvas 有 GraphicRaycaster（供按鈕點擊）
            Canvas canvas = hudRoot.GetComponentInChildren<Canvas>(true);
            if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }

            UpdateProgressUI();
        }
    }

    private static DroneFlightHUD FindHudFor(GameObject droneObj)
    {
        DroneFlightHUD hud = droneObj.GetComponentInChildren<DroneFlightHUD>(true);
        if (hud != null) return hud;

        // HUD 被移出無人機階層時，退而求其次找場景中唯一的 HUD
        return FindFirstObjectByType<DroneFlightHUD>(FindObjectsInactive.Include);
    }

    #endregion

    #region 結算

    // 競速通關結算
    public void TriggerWin()
    {
        if (isGameFinished) return;

        LevelResult result = new LevelResult
        {
            Mode = GameModeType.Race,
            Success = true,
            Time = elapsedTime,
            Title = "完成賽道！"
        };
        result.Details.Add($"通過檢查點　{passedCheckpoints} / {totalCheckpoints}");
        CompleteLevel(result);
    }

    /// <summary>結束關卡並顯示結算畫面 (競速與搬運共用)</summary>
    public void CompleteLevel(LevelResult result)
    {
        if (isGameFinished) return;
        isGameFinished = true;
        isTimerRunning = false; // 停止計時
        result.Time = result.Time > 0f ? result.Time : elapsedTime;

        Debug.Log($"【GameManager】關卡結束：{result.Title}  時間 {RuntimeUI.FormatTime(result.Time)}  分數 {result.Score}");

        DroneController1 drone = GetDrone();
        if (drone != null) drone.canControl = false;
        NavigationTarget.Clear();

        FindAndBindDroneUI();
        if (centerDisplayText != null)
        {
            centerDisplayText.DOKill();
            centerDisplayText.gameObject.SetActive(false);
        }
        if (nextStageButton != null)
        {
            nextStageButton.gameObject.SetActive(false);
        }

        GameAudio.Play(result.Success ? GameSfx.Finish : GameSfx.Warning);
        GameAudio.PlayMusic(GameMusic.Result);

        LevelDefinition level = Level;
        if (result.Success && level != null)
        {
            string pilot = GetPilotName();
            LeaderboardEntry entry = new LeaderboardEntry { pilot = pilot, time = result.Time, score = result.Score };
            result.Rank = Leaderboard.Submit(level.LeaderboardKey(result.Mode), result.Mode, entry);
            result.SubmittedEntry = entry;
            if (result.Rank == 1) GameAudio.Play(GameSfx.NewRecord);
        }

        if (result.Mode == GameModeType.Race && level != null && result.Success)
        {
            result.Medal = result.Time <= level.GoldTime ? Medal.Gold
                : result.Time <= level.SilverTime ? Medal.Silver
                : Medal.Bronze;
        }

        StartCoroutine(ShowResultAfterDelay(result, 1.2f));
    }

    private IEnumerator ShowResultAfterDelay(LevelResult result, float delay)
    {
        ShowCenterMessage(result.Success ? "FINISH!" : "任務失敗", result.Success ? new Color(1f, 0.85f, 0.2f) : RuntimeUI.WarningColor, delay * 0.6f);
        yield return new WaitForSeconds(delay);

        Transform parent = null;
        DroneFlightHUD hud = droneTransform != null ? FindHudFor(droneTransform.gameObject) : FindFirstObjectByType<DroneFlightHUD>();
        if (hud != null) parent = hud.transform;

        ResultScreen.Show(parent, result, Level, OnRetry, OnNextLevel, OnBackToMenu);
    }

    private void OnRetry()
    {
        SceneLoader.ReloadCurrent();
    }

    private void OnNextLevel()
    {
        LevelDefinition level = Level;
        LevelDefinition next = level != null ? LevelCatalog.NextLevel(level, Mode) : null;
        if (next != null)
        {
            LevelCatalog.Load(next, Mode);
        }
        else
        {
            OnNextStageButtonClicked();
        }
    }

    private void OnBackToMenu()
    {
        SceneLoader.Load(LevelCatalog.DroneSelectScene);
    }

    public bool HasNextLevel()
    {
        LevelDefinition level = Level;
        if (level != null) return LevelCatalog.NextLevel(level, Mode) != null;
        return !string.IsNullOrEmpty(nextSceneName) && LevelCatalog.IsSceneInBuild(nextSceneName);
    }

    // 按鈕點擊：切換場景
    public void OnNextStageButtonClicked()
    {
        Debug.Log($"【GameManager】切換場景至：{nextSceneName}");
        SceneLoader.Load(nextSceneName);
    }

    private string GetPilotName()
    {
        DroneController1 drone = GetDrone();
        string droneName = drone != null ? drone.name.Replace("(Clone)", string.Empty).Trim() : "Drone";
        return $"Pilot・{droneName}";
    }

    #endregion

    public DroneController1 GetDrone()
    {
        if (droneTransform != null)
        {
            DroneController1 controller = droneTransform.GetComponent<DroneController1>();
            if (controller != null) return controller;
        }
        return FindFirstObjectByType<DroneController1>();
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null) return;

        GameObject eventSystem = new GameObject("EventSystem (Auto)");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<InputSystemUIInputModule>();
    }
}

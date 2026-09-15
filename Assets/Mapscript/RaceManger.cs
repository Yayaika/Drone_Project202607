using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using TMPro;

public class RaceManager : MonoBehaviour
{
    [Header("多賽道隨機系統")]
    public SplineContainer[] availableSplines;   // 這裡放你所有的路線 (Spline)
    public Transform[] availableStartPoints;     // 這裡放每一條路線對應的起點
    
    [Header("設定")]
    public GameObject gatePrefab;        // 這裡放你的圓圈 Prefab (藍圖)
    private GameObject gateInstance;     // 系統真正在場上生出來的實體

    [Header("比賽設定")]
    public int totalWaypoints = 15;      // 預計整條路徑要過幾個點
    public int currentIndex = 0;         // 目前進度
    public int startKnotIndex = 2;

    [Header("計時器 UI")]
    public TextMeshProUGUI timerText;    // 綁定顯示時間的文字
    
    // 計時器內部變數
    private float raceTimer = 0f;
    private bool isRacing = false;
    
    [Header("玩家設定")]
    public Transform droneTransform;
    
    // 系統自動指派的變數
    private SplineContainer raceSpline;   
    private Transform trackStartPoint;

    // === 重生點記錄變數 ===
    private Vector3 lastCheckpointPosition;
    private Quaternion lastCheckpointRotation;
    private DroneController1 droneCtrl;
    private float lastTriggerTime = 0f; // 用來防止同一個 Checkpoint 被連續觸發多次

    void Awake()
    {
        if (availableSplines != null && availableSplines.Length > 0 && availableSplines.Length == availableStartPoints.Length)
        {
            int randomIndex = UnityEngine.Random.Range(0, availableSplines.Length);

            SplineContainer spawnedSpline = Instantiate(availableSplines[randomIndex]);
            Transform spawnedStart = Instantiate(availableStartPoints[randomIndex]);

            raceSpline = spawnedSpline;
            trackStartPoint = spawnedStart;

            // 🌟 新增：把檢查點圓圈也動態生出來！
            if (gatePrefab != null)
            {
                gateInstance = Instantiate(gatePrefab);
            }

            Debug.Log($"🎲 系統隨機抽中並『動態生成』了第 {randomIndex + 1} 條賽道與圓圈！");
        }
    }

    void Start()
    {
        if (droneTransform != null)
        {
            droneCtrl = droneTransform.GetComponent<DroneController1>();
            
            // 訂閱：當聽到無人機大喊「按了重生」時，執行 RespawnDrone 函式
            if (droneCtrl != null)
            {
                droneCtrl.OnRespawnPressed += RespawnDrone;
            }

            // 🌟 2. 初始重生點對齊：把記錄點設為「抽中的專屬起跑線」
            if (trackStartPoint != null)
            {
                lastCheckpointPosition = trackStartPoint.position;
                lastCheckpointRotation = trackStartPoint.rotation;
                
                // 遊戲一開始，強制呼叫一次控制器，把無人機精準吸到起跑線上
                if (droneCtrl != null)
                {
                    droneCtrl.RespawnAtCheckpoint(lastCheckpointPosition, lastCheckpointRotation);
                }
            }
        }

        // 遊戲開始時，先將圓圈放到第一個點 (t=0)
        UpdateGatePosition();
        isRacing = true;
    }

    // 安全機制，關閉時取消監聽
    private void OnDestroy()
    {
        if (droneCtrl != null)
        {
            droneCtrl.OnRespawnPressed -= RespawnDrone;
        }
    }

    void Update()
    {
        if (isRacing)
        {
            raceTimer += Time.deltaTime;
            UpdateTimerUI(); 
        }
    }

    public void PointReached()
    {
        // 防連點機制：0.5 秒內不允許連續觸發兩次
        if (Time.time - lastTriggerTime < 0.5f) return;
        lastTriggerTime = Time.time;

        // 🌟 3. 關鍵修復：必須記錄「無人機」的安全殘影，絕對不要記錄圓圈，以免受城市群組座標干擾！
        if (droneTransform != null && raceSpline != null)
        {
            lastCheckpointPosition = droneTransform.position; // 位置依然用無人機的安全位置
            
            // 算出目前進度 t
            int knotCount = raceSpline.Spline.Count;
            int safeStartIndex = Mathf.Clamp(startKnotIndex, 0, knotCount - 1);
            float startT = (float)safeStartIndex / (knotCount - 1);
            float rawT = (float)currentIndex / (totalWaypoints - 1);
            float t = Mathf.Lerp(startT, 1f, rawT);

            // 取得這一段賽道的世界座標方向 (Tangent)
            float3 localTangent = raceSpline.EvaluateTangent(t);
            float3 localUp = raceSpline.EvaluateUpVector(t);
            // 轉換為世界座標的方向
            Vector3 worldTangent = raceSpline.transform.TransformDirection(localTangent);
            Vector3 worldUp = raceSpline.transform.TransformDirection(localUp);
            
            lastCheckpointRotation = Quaternion.LookRotation(worldTangent, worldUp);
        }
        currentIndex++;

        if (currentIndex < totalWaypoints)
        {
            UpdateGatePosition();
            if (raceTimer > 0f) raceTimer -= 1f;
        }
        else
        {
            gateInstance.SetActive(false); 
            isRacing = false;            
            Debug.Log($"比賽結束！最終時間: {timerText.text}");
        }
    }

    void UpdateGatePosition()
    {
        if (raceSpline == null || gateInstance == null) return;

        int knotCount = raceSpline.Spline.Count;
        int safeStartIndex = Mathf.Clamp(startKnotIndex, 0, knotCount - 1);
        float startT = (float)safeStartIndex / (knotCount - 1);
        float rawT = (float)currentIndex / (totalWaypoints - 1);
        float t = Mathf.Lerp(startT, 1f, rawT);

        float3 localPos = raceSpline.EvaluatePosition(t);
        float3 localTangent = raceSpline.EvaluateTangent(t);
        float3 localUp = raceSpline.EvaluateUpVector(t);

        // 🌟 4. 關鍵修復：必須使用 localPosition 才能讓圓圈精準對齊賽道，不會飛到地圖外！
        gateInstance.transform.localPosition = localPos;
        gateInstance.transform.localRotation = Quaternion.LookRotation(localTangent, localUp);
    }

    void UpdateTimerUI()
    {
        if (timerText != null)
        {
            int minutes = Mathf.FloorToInt(raceTimer / 60f);
            int seconds = Mathf.FloorToInt(raceTimer % 60f);
            int milliseconds = Mathf.FloorToInt((raceTimer * 1000f) % 1000f);
            timerText.text = string.Format("{0:00}:{1:00}.{2:000}", minutes, seconds, milliseconds);
        }
    }

    // === 重生邏輯 ===
    private void RespawnDrone()
    {
        // 直接把 Checkpoint 的位置與方向派發給無人機，讓無人機自己處理物理與重置
        if (droneCtrl != null)
        {
            droneCtrl.RespawnAtCheckpoint(lastCheckpointPosition, lastCheckpointRotation);
            Debug.Log("RaceManager 已呼叫控制器執行重生！");
        }
    }
}
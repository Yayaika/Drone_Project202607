using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using TMPro;

/// <summary>
/// City2 競速：從場景中的 Spline 路線隨機抽一條 (可反向飛)，沿路線一次生成整串連續的檢查圈 (ring target 20)，
/// 通過的檢查圈會消失。進度、計時、重生 (R) 與結算統一由 GameManager 處理。
/// </summary>
public class RaceManager : MonoBehaviour
{
    [Header("多賽道隨機系統")]
    public SplineContainer[] availableSplines;   // 這裡放你所有的路線 (Spline)
    public Transform[] availableStartPoints;     // 舊版欄位：起點改由路線本身決定，保留以免場景引用遺失

    [Header("設定")]
    [Tooltip("舊版單一移動閘門 Prefab (已改用 GameplayAssets 的 ring target 20 檢查圈)")]
    public GameObject gatePrefab;

    [Header("檢查圈配置")]
    [Tooltip("直線路段上相鄰檢查圈的距離 (m)，轉彎處會自動加密")]
    public float checkpointSpacing = 18f;
    [Tooltip("相鄰檢查圈之間最大方向變化 (度)")]
    public float maxTurnBetweenGates = 30f;
    [Tooltip("檢查圈半徑 (m，實際大小再乘上 CourseBuilder.RingSizeScale)")]
    public float gateRadius = 2.6f;
    [Tooltip("起飛點到第一個檢查圈的距離 (m)")]
    public float firstGateDistance = 22f;
    [Tooltip("短於此長度的 Spline 視為未完成的草稿路線，不會被抽中")]
    public float minRouteLength = 100f;
    [Tooltip("路線可反向飛行 (同一條 Spline 產生兩種賽道)")]
    public bool allowReverseRoutes = true;
    [Tooltip("檢查圈中心離地面最少高度 (m)")]
    public float minGateHeight = 4f;

    [Header("比賽設定 (執行時自動計算)")]
    public int totalWaypoints = 15;      // 本次生成的檢查圈數量
    public int currentIndex = 0;         // 舊版欄位
    public int startKnotIndex = 0;       // 舊版欄位

    [Header("計時器 UI")]
    public TextMeshProUGUI timerText;    // 舊版欄位：計時改由 GameManager 顯示

    [Header("玩家設定")]
    public Transform droneTransform;

    private readonly List<Vector3> route = new List<Vector3>();
    private readonly List<float> routeDistance = new List<float>();
    private Vector3 startPosition;
    private Vector3 startFacing = Vector3.forward;
    private bool hasInitializedDrone;

    private const int SamplesPerMeter = 1;

    void Awake()
    {
        HideLegacySplineLines();

        if (!PickRoute())
        {
            Debug.LogWarning("[RaceManager] 找不到可用的賽道路線 (Spline 長度不足)，City2 不會生成檢查圈。");
            return;
        }

        BuildCheckpoints();

        // 起飛點放在路線起點並面向第一個檢查圈，SpawnCtrl 會在這裡生成無人機
        GameObject spawnerObj = GameObject.Find("Spawner");
        if (spawnerObj != null)
        {
            spawnerObj.transform.SetPositionAndRotation(startPosition, Quaternion.LookRotation(startFacing));
        }
    }

    void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.totalCheckpoints = totalWaypoints;
            GameManager.Instance.passedCheckpoints = 0;
        }
        RingTrigger.SetNavigationTo(0);
    }

    void Update()
    {
        if (!hasInitializedDrone) FindDynamicDroneAndInitialize();
    }

    #region 路線

    /// <summary>把舊的 Spline 預覽線關掉 (位置是未對齊的舊資料)，路線改由檢查圈連線呈現</summary>
    private void HideLegacySplineLines()
    {
        if (availableSplines == null) return;
        foreach (SplineContainer spline in availableSplines)
        {
            if (spline == null) continue;
            LineRenderer legacyLine = spline.GetComponent<LineRenderer>();
            if (legacyLine != null) legacyLine.enabled = false;
        }
    }

    private bool PickRoute()
    {
        List<(SplineContainer spline, bool reverse)> candidates = new List<(SplineContainer, bool)>();
        if (availableSplines != null)
        {
            foreach (SplineContainer spline in availableSplines)
            {
                if (spline == null || spline.Spline == null || spline.Spline.Count < 2) continue;
                if (spline.CalculateLength() < minRouteLength) continue;
                candidates.Add((spline, false));
                if (allowReverseRoutes) candidates.Add((spline, true));
            }
        }
        if (candidates.Count == 0) return false;

        var chosen = candidates[Random.Range(0, candidates.Count)];
        SampleRoute(chosen.spline, chosen.reverse);
        Debug.Log($"🎲 賽道：{chosen.spline.name}{(chosen.reverse ? " (反向)" : string.Empty)}，全長 {routeDistance[routeDistance.Count - 1]:0} m");

        startPosition = route[0];
        Vector3 ahead = PointAtDistance(Mathf.Min(8f, TotalLength * 0.5f)) - startPosition;
        ahead.y = 0f;
        startFacing = ahead.sqrMagnitude > 0.01f ? ahead.normalized : Vector3.forward;
        return true;
    }

    private void SampleRoute(SplineContainer spline, bool reverse)
    {
        route.Clear();
        routeDistance.Clear();

        int samples = Mathf.Max(32, Mathf.CeilToInt(spline.CalculateLength() * SamplesPerMeter));
        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / samples;
            if (reverse) t = 1f - t;
            // EvaluatePosition 已是世界座標
            Vector3 point = (Vector3)spline.EvaluatePosition(t);
            if (route.Count > 0 && (point - route[route.Count - 1]).sqrMagnitude < 0.01f) continue;
            routeDistance.Add(route.Count == 0 ? 0f : routeDistance[routeDistance.Count - 1] + Vector3.Distance(point, route[route.Count - 1]));
            route.Add(point);
        }
    }

    private float TotalLength => routeDistance.Count > 0 ? routeDistance[routeDistance.Count - 1] : 0f;

    private Vector3 PointAtDistance(float distance)
    {
        distance = Mathf.Clamp(distance, 0f, TotalLength);
        int index = routeDistance.BinarySearch(distance);
        if (index >= 0) return route[index];
        index = ~index;
        if (index <= 0) return route[0];
        if (index >= route.Count) return route[route.Count - 1];
        float span = routeDistance[index] - routeDistance[index - 1];
        float k = span > 0.0001f ? (distance - routeDistance[index - 1]) / span : 0f;
        return Vector3.Lerp(route[index - 1], route[index], k);
    }

    #endregion

    #region 檢查圈

    private void BuildCheckpoints()
    {
        // 手繪 Spline 有接近直角的尖角：先還原轉折點、抬到安全高度 (不卡在建築/招牌裡)，
        // 再把尖角圓角，最後交給共用配置：連續、轉彎加密、錯落、部分貼近障礙
        List<Vector3> corners = RaceCourseLayout.Simplify(route, 2f);
        for (int i = 0; i < corners.Count; i++) corners[i] = PlaceGate(corners[i]);
        // 圓角只避開建築 (小檢查半徑)；弧線上的檢查圈若碰到街角路燈，PlaceGates 會再微調位置
        List<Vector3> safeRoute = RaceCourseLayout.RoundCorners(corners, 14f, 4f, 0.3f);

        List<RaceCourseLayout.Gate> gates = RaceCourseLayout.PlaceGates(safeRoute, firstGateDistance, checkpointSpacing, maxTurnBetweenGates,
            gateRadius * CourseBuilder.RingSizeScale, Random.Range(0, 100000));

        for (int i = 0; i < gates.Count; i++)
        {
            if (i == 0)
            {
                Vector3 toFirst = gates[0].Position - startPosition;
                toFirst.y = 0f;
                if (toFirst.sqrMagnitude > 0.01f) startFacing = toFirst.normalized;
            }

            GameObject gate = CourseBuilder.CreateCheckpointRing(gates[i].Position, i, gates[i].Rotation, gateRadius);
            gate.transform.SetParent(transform, true);
            if (i == gates.Count - 1) gate.name = "Checkpoint_Finish";
        }

        totalWaypoints = gates.Count;
    }

    /// <summary>確保檢查圈離地夠高且沒有卡在建築/招牌裡</summary>
    private Vector3 PlaceGate(Vector3 position)
    {
        if (Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore))
        {
            position.y = Mathf.Max(position.y, hit.point.y + minGateHeight);
        }

        for (int attempt = 0; attempt < 8; attempt++)
        {
            if (!Physics.CheckSphere(position, gateRadius * CourseBuilder.RingSizeScale, ~0, QueryTriggerInteraction.Ignore)) break;
            position.y += 1f;
        }
        return position;
    }

    #endregion

    #region 無人機初始化

    private void FindDynamicDroneAndInitialize()
    {
        if (route.Count == 0) return;

        GameObject droneObj = GameObject.FindWithTag("Player");
        if (droneObj == null)
        {
            DroneController1 controller = FindFirstObjectByType<DroneController1>();
            if (controller != null) droneObj = controller.gameObject;
        }
        if (droneObj == null) return;

        droneTransform = droneObj.transform;
        DroneController1 droneCtrl = droneTransform.GetComponent<DroneController1>();
        if (droneCtrl != null)
        {
            // 無人機機頭是 transform 的 -Z，RespawnAtCheckpoint 會自動加 180 度讓機頭朝向 startFacing
            // (競速模式的最終起飛位置與朝向由 GameManager 依第一個檢查圈重新設定)
            droneCtrl.RespawnAtCheckpoint(startPosition + Vector3.up * 0.8f, Quaternion.LookRotation(startFacing));
        }
        if (GameManager.Instance != null)
        {
            GameManager.Instance.RecordRespawnPoint(startPosition + Vector3.up * 0.8f, startFacing);
        }
        hasInitializedDrone = true;
    }

    #endregion
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 打擊任務規則：
/// 0. 打擊專屬場景有手動點位 (<see cref="StrikeTargetPoint"/>) 與車道 (<see cref="StrikeVehicleRoute"/>) 時只使用它們；
///    沒有時才在城市中自動取樣。
/// 1. 開場在城市中放置投彈目標 (上方無遮蔽：屋頂、廣場、空地) 與撞擊目標 (騎樓/頂棚/天橋下，側面可飛入)。
/// 2. 投彈 (B / 右扳機) 命中即摧毀；撞擊目標上方被遮蔽，炸彈打不到，必須低飛撞上去；
///    車輛目標是車流中被標記的汽車，會沿街道移動，可投彈 (需預判) 或撞擊。
/// 3. 撞擊摧毀目標後無人機重新部署在該目標旁的開闊空中，面向最近的剩餘目標。
/// 4. 全部摧毀或時限到進入結算；計分 = 目標分數 + 全數摧毀時的剩餘時間獎勵。
/// </summary>
public class StrikeMissionManager : MonoBehaviour
{
    private enum Phase
    {
        WaitingForStart,
        Running,
        Finished
    }

    [Header("任務設定 (有關卡目錄時會被覆寫)")]
    [SerializeField] private int bombTargetCount = 3;
    [SerializeField] private int ramTargetCount = 2;
    [SerializeField] private int vehicleTargetCount = 1;
    [SerializeField] private float timeLimit = 210f;

    [Header("計分")]
    [SerializeField] private int bombTargetScore = 500;
    [SerializeField] private int ramTargetScore = 800;
    [SerializeField] private int vehicleTargetScore = 700;
    [SerializeField] private int droneTargetScore = 1000;
    [Tooltip("擊斃一名敵方軍人的加分 (軍人不計入必須摧毀的目標)")]
    [SerializeField] private int soldierScore = 150;
    [Tooltip("誤傷一名平民工人的扣分")]
    [SerializeField] private int civilianPenalty = 300;
    [SerializeField] private int timeBonusPerSecond = 5;

    [Header("配置")]
    [Tooltip("目標之間最小距離 (m)")]
    [SerializeField] private float minTargetSpacing = 25f;
    [Tooltip("目標離起飛點最小距離 (m)")]
    [SerializeField] private float minDistanceFromStart = 30f;
    [Tooltip("撞擊目標取樣間距 (m)")]
    [SerializeField] private float samplingStep = 3f;
    [Tooltip("撞擊後重新部署前的等待秒數")]
    [SerializeField] private float redeploySeconds = 1.5f;

    private readonly List<StrikeTarget> targets = new List<StrikeTarget>();
    private Phase phase = Phase.WaitingForStart;
    private int destroyedBomb;
    private int destroyedRam;
    private int destroyedVehicle;
    private int destroyedDrone;
    private int totalScore;
    private int soldiersKilled;
    private int civilianCasualties;
    private float nextUpdate;
    private bool warned60;
    private bool warned30;
    private Coroutine redeploy;

    public void Configure(int bombTargets, int ramTargets, int vehicleTargets, float limitSeconds)
    {
        bombTargetCount = Mathf.Max(0, bombTargets);
        ramTargetCount = Mathf.Max(0, ramTargets);
        vehicleTargetCount = Mathf.Max(0, vehicleTargets);
        timeLimit = Mathf.Max(30f, limitSeconds);
    }

    private void Start()
    {
        LevelDefinition level = LevelCatalog.CurrentLevel;
        if (level != null && level.Supports(GameModeType.Strike))
        {
            Configure(level.StrikeBombTargets, level.StrikeRamTargets, level.StrikeVehicleTargets, level.StrikeTimeLimit);
        }

        DroneController1 drone = GameManager.Instance != null ? GameManager.Instance.GetDrone() : FindFirstObjectByType<DroneController1>();
        Vector3 origin = drone != null ? drone.transform.position : transform.position;
        if (drone != null && drone.GetComponent<DroneBombDropper>() == null)
        {
            Debug.LogWarning("[StrikeMissionManager] 目前的無人機沒有投彈功能，只能以撞擊摧毀目標。");
        }

        PlaceTargets(origin);
        if (targets.Count == 0)
        {
            Debug.LogError("[StrikeMissionManager] 找不到可放置目標的位置。");
            return;
        }

        GameManager gameManager = GameManager.Instance;
        if (gameManager != null)
        {
            gameManager.OnRaceStarted += BeginMission;
            gameManager.SetProgress("打擊任務準備中…", 0f);
        }
        else
        {
            BeginMission();
        }
    }

    private void OnEnable()
    {
        SoldierUnit.OnKilled += HandleSoldierKilled;
        FactoryWorker.OnCasualty += HandleCivilianCasualty;
    }

    private void OnDisable()
    {
        SoldierUnit.OnKilled -= HandleSoldierKilled;
        FactoryWorker.OnCasualty -= HandleCivilianCasualty;
    }

    private void HandleCivilianCasualty(FactoryWorker worker)
    {
        if (phase != Phase.Running) return;
        civilianCasualties++;
        totalScore -= civilianPenalty;
        GameManager.Instance?.ShowCenterMessage($"誤傷平民 -{civilianPenalty}", RuntimeUI.WarningColor, 1.2f);
        UpdateProgress();
    }

    private void HandleSoldierKilled(SoldierUnit soldier, bool rammed)
    {
        if (phase != Phase.Running) return;
        soldiersKilled++;
        totalScore += soldierScore;
        GameManager.Instance?.ShowCenterMessage($"擊斃敵兵 +{soldierScore}", new Color(1f, 0.75f, 0.35f), 0.9f);
        UpdateProgress();
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null) GameManager.Instance.OnRaceStarted -= BeginMission;
        foreach (StrikeTarget target in targets)
        {
            if (target != null) target.OnDestroyed -= HandleTargetDestroyed;
        }
    }

    private void BeginMission()
    {
        phase = Phase.Running;
        string legend = "紅：投彈　紫：撞擊";
        if (CountOf(StrikeTarget.TargetKind.Vehicle) > 0) legend += "　橘：移動車輛";
        if (CountOf(StrikeTarget.TargetKind.Drone) > 0) legend += "　青：敵方無人機";
        GameManager.Instance?.ShowCenterMessage($"摧毀 {targets.Count} 個目標！\n{legend}", new Color(1f, 0.6f, 0.3f), 2.2f);
        UpdateNavigation();
        UpdateProgress();
    }

    #region 目標配置

    private void PlaceTargets(Vector3 origin)
    {
        // 同一關卡使用固定種子，讓每次的目標相同，排行榜才公平
        System.Random random = new System.Random((LevelCatalog.CurrentLevel?.CitySeed ?? 12345) * 7 + 11);

        PlaceEnemyDrones();

        StrikeTargetPoint[] manualPoints = FindObjectsByType<StrikeTargetPoint>(FindObjectsSortMode.InstanceID);
        if (manualPoints.Length > 0)
        {
            PlaceManualTargets(manualPoints);
            int placedVehicles = PlaceVehicleTargets(origin, random);
            if (placedVehicles < vehicleTargetCount)
            {
                Debug.LogWarning($"[StrikeMissionManager] 車輛目標不足：{placedVehicles}/{vehicleTargetCount}");
            }
            return;
        }

        Bounds area = SearchArea(origin);

        List<(Vector3 ground, Vector3 approach)> ramSpots = CollectRamSpots(area);
        List<Vector3> bombSpots = CollectBombSpots(area);
        Shuffle(ramSpots, random);
        Shuffle(bombSpots, random);

        List<Vector3> used = new List<Vector3>();
        int ramPlaced = 0;
        foreach ((Vector3 ground, Vector3 approach) in ramSpots)
        {
            if (ramPlaced >= ramTargetCount) break;
            if (!FarEnough(ground, origin, used)) continue;
            AddTarget(StrikeTarget.Create(ground, StrikeTarget.TargetKind.Ram, approach, YawToward(ground, approach)));
            used.Add(ground);
            ramPlaced++;
        }

        int bombPlaced = 0;
        foreach (Vector3 ground in bombSpots)
        {
            if (bombPlaced >= bombTargetCount) break;
            if (!FarEnough(ground, origin, used)) continue;
            AddTarget(StrikeTarget.Create(ground, StrikeTarget.TargetKind.Bomb, ground + Vector3.up * 8f, (float)random.NextDouble() * 360f));
            used.Add(ground);
            bombPlaced++;
        }

        int vehiclePlaced = PlaceVehicleTargets(origin, random);

        if (ramPlaced < ramTargetCount || bombPlaced < bombTargetCount || vehiclePlaced < vehicleTargetCount)
        {
            Debug.LogWarning($"[StrikeMissionManager] 位置不足：撞擊目標 {ramPlaced}/{ramTargetCount}、投彈目標 {bombPlaced}/{bombTargetCount}、車輛目標 {vehiclePlaced}/{vehicleTargetCount}");
        }
    }

    /// <summary>打擊專屬場景：依設計者擺放的點位建立目標 (點位貼齊下方地面)</summary>
    private void PlaceManualTargets(StrikeTargetPoint[] points)
    {
        foreach (StrikeTargetPoint point in points)
        {
            if (point.kind == StrikeTarget.TargetKind.Vehicle) continue;
            Vector3 ground = point.transform.position;
            if (Physics.Raycast(ground + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                ground = hit.point;
            }
            AddTarget(StrikeTarget.Create(ground, point.kind, point.ApproachPoint, point.transform.eulerAngles.y, point.label));
        }
    }

    /// <summary>場景中的每條敵方無人機航線 (<see cref="EnemyDronePatrol"/>) 都是一個移動目標</summary>
    private void PlaceEnemyDrones()
    {
        foreach (EnemyDronePatrol patrol in FindObjectsByType<EnemyDronePatrol>(FindObjectsSortMode.InstanceID))
        {
            StrikeTarget target = StrikeTarget.AttachToDrone(patrol);
            if (target == null) continue;
            // 無人機沿航線移動，不改父物件
            target.OnDestroyed += HandleTargetDestroyed;
            targets.Add(target);
        }
    }

    /// <summary>從車流 (城市車流或打擊場景的環狀車道) 中挑選離起飛點夠遠的車輛標記為移動目標</summary>
    private int PlaceVehicleTargets(Vector3 origin, System.Random random)
    {
        if (vehicleTargetCount <= 0) return 0;

        List<Transform> vehicles = new List<Transform>();
        if (CityTraffic.Instance != null) vehicles.AddRange(CityTraffic.Instance.ActiveVehicleRoots());
        foreach (StrikeVehicleRoute route in FindObjectsByType<StrikeVehicleRoute>(FindObjectsSortMode.InstanceID))
        {
            vehicles.AddRange(route.ActiveVehicles());
        }
        Shuffle(vehicles, random);
        int placed = 0;
        foreach (Transform vehicle in vehicles)
        {
            if (placed >= vehicleTargetCount) break;
            if (FlatDistance(vehicle.position, origin) < minDistanceFromStart * 1.5f) continue;
            StrikeTarget target = StrikeTarget.AttachToVehicle(vehicle);
            if (target == null) continue;
            // 車輛跟著車流移動，不改父物件
            target.OnDestroyed += HandleTargetDestroyed;
            targets.Add(target);
            placed++;
        }
        return placed;
    }

    private void AddTarget(StrikeTarget target)
    {
        target.transform.SetParent(transform, true);
        target.OnDestroyed += HandleTargetDestroyed;
        targets.Add(target);
    }

    private bool FarEnough(Vector3 point, Vector3 origin, List<Vector3> used)
    {
        if (FlatDistance(point, origin) < minDistanceFromStart) return false;
        foreach (Vector3 other in used)
        {
            if (FlatDistance(point, other) < minTargetSpacing) return false;
        }
        return true;
    }

    /// <summary>城市範圍：程序化城市提供的屋頂/街道點外擴一些；沒有時以起飛點為中心</summary>
    private static Bounds SearchArea(Vector3 origin)
    {
        Bounds area = new Bounds(origin, new Vector3(200f, 0f, 200f));
        foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (!(behaviour is IDeliverySpotProvider provider)) continue;
            foreach (Vector3 spot in provider.RooftopSpots) area.Encapsulate(spot);
            foreach (Vector3 spot in provider.StreetSpots) area.Encapsulate(spot);
        }
        area.Expand(new Vector3(20f, 0f, 20f));
        return area;
    }

    /// <summary>投彈目標：屋頂、廣場與空地，上方必須完全無遮蔽</summary>
    private List<Vector3> CollectBombSpots(Bounds area)
    {
        List<Vector3> spots = new List<Vector3>();
        foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (!(behaviour is IDeliverySpotProvider provider)) continue;
            foreach (Vector3 spot in provider.RooftopSpots) if (IsOpenGround(spot, 2.2f)) spots.Add(spot);
            foreach (Vector3 spot in provider.StreetSpots) if (IsOpenGround(spot, 2.2f)) spots.Add(spot);
        }

        // 不足時在範圍內取樣平坦且上方無遮蔽的地面 (避開道路，車流會穿過目標)
        for (int attempt = 0; attempt < 600 && spots.Count < bombTargetCount * 3; attempt++)
        {
            Vector3 top = new Vector3(Random.Range(area.min.x, area.max.x), 200f, Random.Range(area.min.z, area.max.z));
            if (!RaycastIgnoringBoundary(top, Vector3.down, 400f, out RaycastHit hit)) continue;
            if (hit.normal.y < 0.92f || IsOnRoad(hit.point)) continue;
            if (IsOpenGround(hit.point, 2.2f)) spots.Add(hit.point);
        }
        return spots;
    }

    private static bool IsOpenGround(Vector3 ground, float clearRadius)
    {
        if (Physics.CheckSphere(ground + Vector3.up * (clearRadius + 0.3f), clearRadius, ~0, QueryTriggerInteraction.Ignore)) return false;
        foreach (RaycastHit hit in Physics.SphereCastAll(ground + Vector3.up * 1.5f, 1f, Vector3.up, 80f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!IsBoundary(hit.collider)) return false;
        }
        return true;
    }

    /// <summary>地圖外圍空氣牆/天花板 (<see cref="MapBoundary"/>)：判斷「上方是否開闊」時不算遮蔽</summary>
    private static bool IsBoundary(Collider collider)
    {
        Transform parent = collider.transform.parent;
        return parent != null && parent.name == MapBoundary.RootName;
    }

    private static bool RaycastIgnoringBoundary(Vector3 origin, Vector3 direction, float distance, out RaycastHit closest)
    {
        closest = default;
        bool found = false;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, direction, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (IsBoundary(hit.collider) || (found && hit.distance >= closest.distance)) continue;
            closest = hit;
            found = true;
        }
        return found;
    }

    /// <summary>
    /// 撞擊目標：地面點上方 2.8~12m 有實心遮蔽 (騎樓、頂棚、天橋)，且至少一個水平方向可讓無人機從開闊天空飛進來。
    /// 回傳目標地面點與飛入方向外側的開闊點。
    /// </summary>
    private List<(Vector3, Vector3)> CollectRamSpots(Bounds area)
    {
        List<(Vector3, Vector3)> spots = new List<(Vector3, Vector3)>();
        for (float x = area.min.x; x <= area.max.x; x += samplingStep)
        {
            for (float z = area.min.z; z <= area.max.z; z += samplingStep)
            {
                // 從略高於街道處往下打 (騎樓屋頂至少 2.8m 高，起點在屋頂下方)，只取街道層的地面
                if (!Physics.Raycast(new Vector3(x, area.min.y + 2.2f, z), Vector3.down, out RaycastHit ground, 4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (ground.normal.y < 0.92f || IsOnRoad(ground.point)) continue;
                if (TryRamSpot(ground.point, out Vector3 approach)) spots.Add((ground.point, approach));
            }
        }
        return spots;
    }

    private static bool TryRamSpot(Vector3 ground, out Vector3 approach)
    {
        approach = Vector3.zero;
        Vector3 center = ground + Vector3.up * 1.2f;
        if (Physics.CheckSphere(center, 0.9f, ~0, QueryTriggerInteraction.Ignore)) return false;

        // 上方要有實心遮蔽：中心與四周都打到同一層，排除路燈、電線等細物件
        if (!Physics.Raycast(center, Vector3.up, out RaycastHit roof, 11f, ~0, QueryTriggerInteraction.Ignore)) return false;
        if (roof.distance < 1.6f || IsThinObject(roof.collider)) return false;
        foreach (Vector3 offset in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
        {
            if (!Physics.Raycast(center + offset * 1.2f, Vector3.up, roof.distance + 1.5f, ~0, QueryTriggerInteraction.Ignore)) return false;
        }

        // 至少一個方向可從開闊天空飛入 (8 個方向，9m 內無阻擋且終點上方無遮蔽)
        for (int i = 0; i < 8; i++)
        {
            Vector3 direction = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
            if (Physics.SphereCast(center, 0.6f, direction, out _, 9f, ~0, QueryTriggerInteraction.Ignore)) continue;
            Vector3 outside = center + direction * 9f;
            if (RaycastIgnoringBoundary(outside, Vector3.up, 60f, out _)) continue;
            approach = outside;
            return true;
        }
        return false;
    }

    private static bool IsThinObject(Collider collider)
    {
        string name = collider.name;
        return name.Contains("Lamp") || name.Contains("Wire") || name.Contains("Pole") || name.Contains("Sign") || name.Contains("Tree");
    }

    /// <summary>車道上 (車流會穿過目標) 不放目標；沒有車流系統時不檢查</summary>
    private static bool IsOnRoad(Vector3 point)
    {
        if (CityTraffic.Instance == null) return false;
        CityGrid grid = CityTraffic.Instance.Grid;
        float half = grid.RoadWidth * 0.5f;
        float fx = Mathf.Repeat(point.x - grid.Origin.x + half, grid.Pitch);
        float fz = Mathf.Repeat(point.z - grid.Origin.z + half, grid.Pitch);
        return fx < grid.RoadWidth || fz < grid.RoadWidth;
    }

    private static float YawToward(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.01f ? Quaternion.LookRotation(direction).eulerAngles.y : 0f;
    }

    private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    private static void Shuffle<T>(List<T> list, System.Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    #endregion

    #region 任務流程

    private void HandleTargetDestroyed(StrikeTarget target, bool rammed)
    {
        if (phase == Phase.Finished) return;

        int score;
        switch (target.Kind)
        {
            case StrikeTarget.TargetKind.Ram:
                score = ramTargetScore;
                destroyedRam++;
                break;
            case StrikeTarget.TargetKind.Vehicle:
                score = vehicleTargetScore;
                destroyedVehicle++;
                break;
            case StrikeTarget.TargetKind.Drone:
                score = droneTargetScore;
                destroyedDrone++;
                break;
            default:
                score = bombTargetScore;
                destroyedBomb++;
                break;
        }
        totalScore += score;

        GameAudio.Play(GameSfx.Checkpoint);
        string verb = target.Kind == StrikeTarget.TargetKind.Drone ? "擊落" : "摧毀";
        GameManager.Instance?.ShowCenterMessage($"{(rammed ? "撞擊" : "投彈")}{verb}！ +{score}", StrikeTarget.ColorOf(target.Kind), 1.2f);

        if (RemainingCount == 0)
        {
            FinishMission(false);
            return;
        }

        UpdateNavigation();
        UpdateProgress();
        if (rammed)
        {
            if (redeploy != null) StopCoroutine(redeploy);
            redeploy = StartCoroutine(Redeploy(target));
        }
    }

    /// <summary>撞擊後：短暫停止操控，再把無人機放到目標旁的開闊空中，面向最近的剩餘目標</summary>
    private IEnumerator Redeploy(StrikeTarget rammedTarget)
    {
        GameManager gameManager = GameManager.Instance;
        DroneController1 drone = gameManager != null ? gameManager.GetDrone() : null;
        if (drone == null) yield break;

        drone.canControl = false;
        yield return new WaitForSeconds(redeploySeconds);
        if (phase == Phase.Finished) yield break;

        Vector3 position = rammedTarget.ApproachPoint;
        if (Physics.Raycast(position + Vector3.up, Vector3.down, out RaycastHit ground, 30f, ~0, QueryTriggerInteraction.Ignore))
        {
            position.y = Mathf.Max(position.y, ground.point.y + 6f);
        }
        StrikeTarget next = NearestRemaining(position);
        Vector3 facing = next != null ? next.transform.position - position : rammedTarget.transform.position - position;
        gameManager.RespawnAt(position, facing);
        gameManager.ShowCenterMessage("無人機重新部署", new Color(0.6f, 0.9f, 1f), 1f);
        drone.canControl = true;
        redeploy = null;
    }

    private void Update()
    {
        if (phase != Phase.Running) return;

        if (CurrentTime >= timeLimit)
        {
            FinishMission(true);
            return;
        }

        float remaining = timeLimit - CurrentTime;
        if (!warned60 && remaining <= 60f)
        {
            warned60 = true;
            GameManager.Instance?.ShowCenterMessage("剩餘 60 秒", new Color(1f, 0.85f, 0.2f), 1f);
        }
        if (!warned30 && remaining <= 30f)
        {
            warned30 = true;
            GameManager.Instance?.ShowCenterMessage("剩餘 30 秒！", RuntimeUI.WarningColor, 1f);
            GameAudio.Play(GameSfx.Warning);
        }

        if (Time.time >= nextUpdate)
        {
            nextUpdate = Time.time + 0.4f;
            UpdateNavigation();
            UpdateProgress();
        }
    }

    private void FinishMission(bool timeUp)
    {
        if (phase == Phase.Finished) return;
        phase = Phase.Finished;
        NavigationTarget.Clear();

        int destroyed = DestroyedCount;
        bool allClear = RemainingCount == 0;
        int remainingSeconds = Mathf.Max(0, Mathf.RoundToInt(timeLimit - CurrentTime));
        int timeBonus = allClear ? remainingSeconds * timeBonusPerSecond : 0;
        totalScore += timeBonus;

        LevelResult result = new LevelResult
        {
            Mode = GameModeType.Strike,
            Success = destroyed > 0,
            Score = totalScore,
            Time = CurrentTime,
            Title = allClear ? "打擊任務完成！" : destroyed > 0 ? "時間到！" : "時間到，任務失敗"
        };
        result.Details.Add($"摧毀 {destroyed} / {targets.Count}");
        result.Details.Add($"投彈目標 {destroyedBomb} / {CountOf(StrikeTarget.TargetKind.Bomb)}　+{destroyedBomb * bombTargetScore}");
        result.Details.Add($"撞擊目標 {destroyedRam} / {CountOf(StrikeTarget.TargetKind.Ram)}　+{destroyedRam * ramTargetScore}");
        if (CountOf(StrikeTarget.TargetKind.Vehicle) > 0)
        {
            result.Details.Add($"車輛目標 {destroyedVehicle} / {CountOf(StrikeTarget.TargetKind.Vehicle)}　+{destroyedVehicle * vehicleTargetScore}");
        }
        if (CountOf(StrikeTarget.TargetKind.Drone) > 0)
        {
            result.Details.Add($"敵方無人機 {destroyedDrone} / {CountOf(StrikeTarget.TargetKind.Drone)}　+{destroyedDrone * droneTargetScore}");
        }
        if (soldiersKilled > 0) result.Details.Add($"擊斃敵兵 {soldiersKilled}　+{soldiersKilled * soldierScore}");
        if (civilianCasualties > 0) result.Details.Add($"誤傷平民 {civilianCasualties}　-{civilianCasualties * civilianPenalty}");
        if (timeBonus > 0) result.Details.Add($"全數摧毀　剩餘時間獎勵 +{timeBonus}");
        result.Medal = !result.Success ? Medal.None
            : allClear ? Medal.Gold
            : destroyed * 2 >= targets.Count ? Medal.Silver
            : Medal.Bronze;

        UpdateProgress();
        GameManager.Instance?.CompleteLevel(result);
    }

    #endregion

    private int DestroyedCount => destroyedBomb + destroyedRam + destroyedVehicle + destroyedDrone;

    private float CurrentTime => GameManager.Instance != null ? GameManager.Instance.ElapsedTime : Time.timeSinceLevelLoad;

    private int RemainingCount
    {
        get
        {
            int count = 0;
            foreach (StrikeTarget target in targets) if (target != null && !target.IsDestroyed) count++;
            return count;
        }
    }

    private int CountOf(StrikeTarget.TargetKind kind)
    {
        int count = 0;
        foreach (StrikeTarget target in targets) if (target != null && target.Kind == kind) count++;
        return count;
    }

    private StrikeTarget NearestRemaining(Vector3 from)
    {
        StrikeTarget nearest = null;
        float best = float.MaxValue;
        foreach (StrikeTarget target in targets)
        {
            if (target == null || target.IsDestroyed) continue;
            float distance = (target.transform.position - from).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                nearest = target;
            }
        }
        return nearest;
    }

    private void UpdateNavigation()
    {
        DroneController1 drone = GameManager.Instance != null ? GameManager.Instance.GetDrone() : null;
        StrikeTarget nearest = NearestRemaining(drone != null ? drone.transform.position : transform.position);
        if (nearest == null)
        {
            NavigationTarget.Clear();
            return;
        }
        string label = nearest.Kind switch
        {
            StrikeTarget.TargetKind.Ram => "撞擊目標（低飛繞入）",
            StrikeTarget.TargetKind.Vehicle => "移動車輛（投彈或撞擊）",
            StrikeTarget.TargetKind.Drone => "敵方無人機（追擊撞落）",
            _ => "投彈目標"
        };
        if (!string.IsNullOrEmpty(nearest.Label))
        {
            label = nearest.Kind == StrikeTarget.TargetKind.Ram ? $"{nearest.Label}（撞擊）" : $"{nearest.Label}（投彈）";
        }
        NavigationTarget.Set(nearest.transform.position + Vector3.up * 0.6f, label, StrikeTarget.ColorOf(nearest.Kind));
    }

    private void UpdateProgress()
    {
        if (GameManager.Instance == null || phase == Phase.WaitingForStart) return;
        float remaining = Mathf.Max(0f, timeLimit - CurrentTime);
        int destroyed = DestroyedCount;
        string vehicles = CountOf(StrikeTarget.TargetKind.Vehicle) > 0 ? $"  車輛 {destroyedVehicle}/{CountOf(StrikeTarget.TargetKind.Vehicle)}" : string.Empty;
        if (CountOf(StrikeTarget.TargetKind.Drone) > 0) vehicles += $"  敵機 {destroyedDrone}/{CountOf(StrikeTarget.TargetKind.Drone)}";
        GameManager.Instance.SetProgress(
            $"TARGET {destroyed}/{targets.Count}  投彈 {destroyedBomb}/{CountOf(StrikeTarget.TargetKind.Bomb)}  撞擊 {destroyedRam}/{CountOf(StrikeTarget.TargetKind.Ram)}{vehicles}\n剩餘 {RuntimeUI.FormatTime(remaining).Substring(0, 5)}  分數 {totalScore}",
            (float)destroyed / Mathf.Max(1, targets.Count));
    }
}

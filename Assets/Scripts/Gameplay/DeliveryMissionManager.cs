using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 搬運任務規則：
/// 1. 依序派發訂單；每張訂單在取貨點生成貨物 (黃色光柱)。
/// 2. 抓起貨物 (Z / A) 後，送到送貨點 (綠色光柱) 並「放下」，貨物在區域內靜止即算送達。
/// 3. 計分 = 基本分 × 貨物完整度 + 準時獎勵；撞擊會損傷貨物，耐久歸零則該訂單失敗。
/// 4. 全部訂單處理完或總時限到，進入結算；至少送達一件才算過關。
/// </summary>
public class DeliveryMissionManager : MonoBehaviour
{
    private class Order
    {
        public Vector3 PickupPoint;
        public Vector3 DropPoint;
        public string CargoName;
        public float CargoMass;
        public float ParTime;
    }

    private enum Phase
    {
        WaitingForStart,
        GoToPickup,
        Carrying,
        CargoLoose,
        Finished
    }

    [Header("任務設定 (有關卡目錄時會被覆寫)")]
    [SerializeField] private int orderCount = 3;
    [SerializeField] private float timeLimit = 300f;

    [Header("計分")]
    [SerializeField] private int baseScorePerOrder = 1000;
    [SerializeField] private int timeBonusPerSecond = 10;
    [SerializeField] private float settleSpeed = 0.35f;
    [SerializeField] private float settleSeconds = 0.6f;

    [Header("地點 (留空則自動搜尋：程序化城市 → 場景射線取樣)")]
    [SerializeField] private Transform[] pickupPoints;
    [SerializeField] private Transform[] dropPoints;
    [SerializeField] private float searchRadius = 140f;
    [SerializeField] private float minOrderDistance = 35f;

    [Header("貨物")]
    [Tooltip("可留空，會自動建立木箱造型的貨物")]
    [SerializeField] private GameObject cargoPrefab;

    private static readonly string[] CargoNames = { "急件包裹", "醫療物資", "外送餐點", "精密零件", "生日蛋糕", "文件袋" };

    private readonly List<Order> orders = new List<Order>();
    private int currentOrderIndex = -1;
    private Phase phase = Phase.WaitingForStart;
    private DroneCargo currentCargo;
    private DeliveryZone pickupZone;
    private DeliveryZone dropZone;
    private DronePickupHandler pickupHandler;
    private float orderStartTime;
    private float settleTimer;
    private float nextProgressUpdate;
    private bool warned60;
    private bool warned30;

    private int deliveredCount;
    private int failedCount;
    private int totalScore;
    private readonly List<string> details = new List<string>();

    public void Configure(int orders, float limitSeconds, GameObject prefab = null)
    {
        orderCount = Mathf.Max(1, orders);
        timeLimit = Mathf.Max(30f, limitSeconds);
        if (prefab != null) cargoPrefab = prefab;
    }

    private void Start()
    {
        LevelDefinition level = LevelCatalog.CurrentLevel;
        if (level != null && level.Supports(GameModeType.Delivery))
        {
            orderCount = level.DeliveryOrders;
            timeLimit = level.DeliveryTimeLimit;
        }

        GameManager gameManager = GameManager.Instance;
        if (gameManager != null)
        {
            gameManager.OnRaceStarted += BeginMissions;
            gameManager.SetProgress("搬運任務準備中…", 0f);
        }
        else
        {
            BeginMissions();
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null) GameManager.Instance.OnRaceStarted -= BeginMissions;
        UnbindCargo();
    }

    private void BeginMissions()
    {
        DroneController1 drone = GameManager.Instance != null ? GameManager.Instance.GetDrone() : FindFirstObjectByType<DroneController1>();
        if (drone == null)
        {
            Debug.LogError("[DeliveryMissionManager] 找不到無人機 (DroneController1)，無法開始搬運任務。");
            return;
        }

        pickupHandler = drone.GetComponent<DronePickupHandler>();
        if (pickupHandler == null)
        {
            Debug.LogError("[DeliveryMissionManager] 無人機上沒有 DronePickupHandler，無法搬運。");
            return;
        }

        GenerateOrders(drone.transform.position);
        if (orders.Count == 0)
        {
            Debug.LogError("[DeliveryMissionManager] 找不到足夠的取貨/送貨地點。");
            return;
        }

        StartNextOrder();
    }

    #region 訂單產生

    private void GenerateOrders(Vector3 origin)
    {
        List<Vector3> pickups = new List<Vector3>();
        List<Vector3> drops = new List<Vector3>();
        CollectSpots(origin, pickups, drops);

        // 同一關卡使用固定種子，讓每次的訂單相同，排行榜才公平
        System.Random random = new System.Random(LevelCatalog.CurrentLevel?.CitySeed ?? 12345);
        Shuffle(pickups, random);
        Shuffle(drops, random);

        for (int i = 0; i < orderCount && pickups.Count > 0 && drops.Count > 0; i++)
        {
            Vector3 pickup = pickups[i % pickups.Count];
            Vector3 drop = Vector3.zero;
            bool found = false;
            foreach (Vector3 candidate in drops)
            {
                if (Vector3.Distance(candidate, pickup) >= minOrderDistance)
                {
                    drop = candidate;
                    found = true;
                    break;
                }
            }
            if (!found) drop = drops[random.Next(drops.Count)];
            drops.Remove(drop);
            if (drops.Count == 0) drops.Add(pickup); // 地點不足時允許重複使用

            float distance = Vector3.Distance(pickup, drop);
            orders.Add(new Order
            {
                PickupPoint = pickup,
                DropPoint = drop,
                CargoName = CargoNames[random.Next(CargoNames.Length)],
                CargoMass = Mathf.Round((0.6f + (float)random.NextDouble() * 1.6f) * 10f) / 10f,
                ParTime = distance / 6f + 25f
            });
        }
    }

    private void CollectSpots(Vector3 origin, List<Vector3> pickups, List<Vector3> drops)
    {
        // 1. 手動指定的地點
        if (pickupPoints != null) foreach (Transform point in pickupPoints) if (point != null) pickups.Add(point.position);
        if (dropPoints != null) foreach (Transform point in dropPoints) if (point != null) drops.Add(point.position);
        if (pickups.Count > 0 && drops.Count > 0) return;

        // 2. 程序化城市提供的屋頂/街道
        foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (behaviour is IDeliverySpotProvider provider)
            {
                // 取貨多在街道，送貨多在屋頂 (更有挑戰性)
                pickups.AddRange(provider.StreetSpots);
                drops.AddRange(provider.RooftopSpots);
                if (drops.Count < orderCount) drops.AddRange(provider.StreetSpots);
                if (pickups.Count > 0 && drops.Count > 0) return;
            }
        }

        // 3. 任意場景：射線取樣可降落的平面
        for (int attempt = 0; attempt < 400 && (pickups.Count < orderCount * 2 || drops.Count < orderCount * 2); attempt++)
        {
            Vector2 circle = Random.insideUnitCircle * searchRadius;
            Vector3 top = new Vector3(origin.x + circle.x, origin.y + 250f, origin.z + circle.y);
            if (!Physics.Raycast(top, Vector3.down, out RaycastHit hit, 500f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (hit.normal.y < 0.92f || hit.collider.GetComponentInParent<DroneController1>() != null) continue;
            if (Physics.CheckSphere(hit.point + Vector3.up * 1.6f, 1.2f, ~0, QueryTriggerInteraction.Ignore)) continue;

            if (hit.point.y > origin.y + 4f) drops.Add(hit.point);
            else if (pickups.Count <= drops.Count) pickups.Add(hit.point);
            else drops.Add(hit.point);
        }
    }

    private static void Shuffle(List<Vector3> list, System.Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    #endregion

    #region 訂單流程

    private void StartNextOrder()
    {
        ClearZones();
        UnbindCargo();
        currentOrderIndex++;

        if (currentOrderIndex >= orders.Count)
        {
            FinishMission(false);
            return;
        }

        Order order = orders[currentOrderIndex];
        orderStartTime = CurrentTime;

        pickupZone = DeliveryZone.Create(order.PickupPoint, DeliveryZone.ZoneKind.Pickup, 2.5f);
        currentCargo = SpawnCargo(order);
        BindCargo(currentCargo);

        phase = Phase.GoToPickup;
        NavigationTarget.Set(order.PickupPoint, $"取貨：{order.CargoName}", DeliveryZone.PickupColor);
        GameManager.Instance?.ShowCenterMessage($"訂單 {currentOrderIndex + 1}/{orders.Count}\n{order.CargoName}  {order.CargoMass:0.0}kg", DeliveryZone.PickupColor, 1.8f);
        UpdateProgress();
    }

    private DroneCargo SpawnCargo(Order order)
    {
        Vector3 position = order.PickupPoint + Vector3.up * 0.6f;
        Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        GameObject cargoObject;

        if (cargoPrefab != null)
        {
            cargoObject = Instantiate(cargoPrefab, position, rotation);
        }
        else
        {
            cargoObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cargoObject.transform.SetPositionAndRotation(position, rotation);
            cargoObject.transform.localScale = Vector3.one * 0.45f;
            cargoObject.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.Opaque(new Color(0.72f, 0.5f, 0.26f));
            Rigidbody body = cargoObject.AddComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        cargoObject.name = $"Cargo_{currentOrderIndex + 1}";
        DroneCargo cargo = cargoObject.GetComponent<DroneCargo>();
        if (cargo == null) cargo = cargoObject.AddComponent<DroneCargo>();
        cargo.Configure(order.CargoName, order.CargoMass);
        cargo.SetSpawnPose(position, rotation);

        Rigidbody cargoBody = cargoObject.GetComponent<Rigidbody>();
        if (cargoBody != null) cargoBody.mass = order.CargoMass;
        return cargo;
    }

    private void BindCargo(DroneCargo cargo)
    {
        if (cargo == null) return;
        cargo.OnPickedUp += HandlePickedUp;
        cargo.OnDropped += HandleDropped;
        cargo.OnBroken += HandleBroken;
        cargo.OnDamaged += HandleDamaged;
    }

    private void UnbindCargo()
    {
        if (currentCargo == null) return;
        currentCargo.OnPickedUp -= HandlePickedUp;
        currentCargo.OnDropped -= HandleDropped;
        currentCargo.OnBroken -= HandleBroken;
        currentCargo.OnDamaged -= HandleDamaged;
    }

    private void HandlePickedUp(DroneCargo cargo)
    {
        if (phase == Phase.Finished) return;
        Order order = orders[currentOrderIndex];

        if (pickupZone != null)
        {
            pickupZone.Hide();
            pickupZone = null;
        }
        if (dropZone == null)
        {
            dropZone = DeliveryZone.Create(order.DropPoint, DeliveryZone.ZoneKind.DropOff, 3f);
        }

        phase = Phase.Carrying;
        NavigationTarget.Set(order.DropPoint, $"送貨：{order.CargoName}", DeliveryZone.DropColor);
        UpdateProgress();
    }

    private void HandleDropped(DroneCargo cargo)
    {
        if (phase == Phase.Finished) return;
        phase = Phase.CargoLoose;
        settleTimer = 0f;
        UpdateProgress();
    }

    private void HandleDamaged(DroneCargo cargo, float damage)
    {
        if (phase == Phase.Finished || cargo.IsBroken) return;
        GameManager.Instance?.ShowCenterMessage($"貨物受損！ {cargo.Health:0}%", RuntimeUI.WarningColor, 0.8f);
    }

    private void HandleBroken(DroneCargo cargo)
    {
        if (phase == Phase.Finished) return;

        failedCount++;
        details.Add($"訂單 {currentOrderIndex + 1}　{cargo.CargoName}　損毀　+0");
        GameManager.Instance?.ShowCenterMessage("貨物損毀！訂單失敗", RuntimeUI.WarningColor, 1.5f);

        UnbindCargo();
        Destroy(cargo.gameObject, 1.5f);
        currentCargo = null;
        StartNextOrder();
    }

    private void Update()
    {
        if (phase == Phase.WaitingForStart || phase == Phase.Finished) return;

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

        if (currentCargo != null && currentCargo.transform.position.y < -50f)
        {
            // 掉出地圖：放回取貨點
            currentCargo.ResetCargo();
            phase = Phase.GoToPickup;
            HandleReturnToPickup();
        }

        if (phase == Phase.CargoLoose && currentCargo != null)
        {
            CheckLooseCargo();
        }

        if (Time.time >= nextProgressUpdate)
        {
            nextProgressUpdate = Time.time + 0.25f;
            UpdateProgress();
        }
    }

    private void HandleReturnToPickup()
    {
        Order order = orders[currentOrderIndex];
        if (pickupZone == null) pickupZone = DeliveryZone.Create(order.PickupPoint, DeliveryZone.ZoneKind.Pickup, 2.5f);
        NavigationTarget.Set(order.PickupPoint, $"取貨：{order.CargoName}", DeliveryZone.PickupColor);
    }

    private void CheckLooseCargo()
    {
        Rigidbody body = currentCargo.GetComponent<Rigidbody>();
        float speed = body != null ? body.linearVelocity.magnitude : 0f;
        bool inDropZone = dropZone != null && dropZone.Contains(currentCargo.transform.position);

        if (inDropZone && speed < settleSpeed)
        {
            settleTimer += Time.deltaTime;
            if (settleTimer >= settleSeconds)
            {
                CompleteOrder();
            }
            return;
        }

        settleTimer = 0f;
        if (!inDropZone && speed < settleSpeed)
        {
            // 貨物落在送貨區外：導航改指向貨物，提醒玩家撿回
            NavigationTarget.Set(currentCargo.transform.position, $"撿回：{currentCargo.CargoName}", DeliveryZone.PickupColor);
        }
    }

    private void CompleteOrder()
    {
        Order order = orders[currentOrderIndex];
        float orderTime = CurrentTime - orderStartTime;
        float integrity = currentCargo.Health / 100f;
        int baseScore = Mathf.RoundToInt(baseScorePerOrder * integrity);
        int timeBonus = Mathf.Max(0, Mathf.RoundToInt((order.ParTime - orderTime) * timeBonusPerSecond));
        int orderScore = baseScore + timeBonus;

        deliveredCount++;
        totalScore += orderScore;
        details.Add($"訂單 {currentOrderIndex + 1}　{order.CargoName}　完整 {integrity * 100f:0}%　+{orderScore}");

        GameAudio.Play(GameSfx.Delivered);
        GameManager.Instance?.ShowCenterMessage($"送達！ +{orderScore}", DeliveryZone.DropColor, 1.2f);

        DroneCargo delivered = currentCargo;
        UnbindCargo();
        currentCargo = null;
        if (delivered != null)
        {
            Destroy(delivered.gameObject, 2f);
        }

        StartNextOrder();
    }

    private void FinishMission(bool timeUp)
    {
        if (phase == Phase.Finished) return;
        phase = Phase.Finished;
        ClearZones();
        NavigationTarget.Clear();

        if (timeUp && currentOrderIndex < orders.Count)
        {
            for (int i = Mathf.Max(0, currentOrderIndex); i < orders.Count; i++)
            {
                details.Add($"訂單 {i + 1}　{orders[i].CargoName}　逾時未送達");
            }
        }

        bool success = deliveredCount > 0;
        int remainingSeconds = Mathf.Max(0, Mathf.RoundToInt(timeLimit - CurrentTime));
        int allClearBonus = success && failedCount == 0 && deliveredCount == orders.Count ? remainingSeconds * 5 : 0;
        if (allClearBonus > 0)
        {
            totalScore += allClearBonus;
            details.Add($"全數完美送達　剩餘時間獎勵 +{allClearBonus}");
        }

        LevelResult result = new LevelResult
        {
            Mode = GameModeType.Delivery,
            Success = success,
            Score = totalScore,
            Time = CurrentTime,
            Title = timeUp ? (success ? "時間到！" : "時間到，任務失敗") : (success ? "搬運任務完成！" : "任務失敗")
        };
        result.Details.Add($"送達 {deliveredCount} / {orders.Count}　損毀 {failedCount}");
        // 結算畫面空間有限，最多列出 5 行訂單明細
        for (int i = 0; i < Mathf.Min(5, details.Count); i++) result.Details.Add(details[i]);
        result.Medal = !success ? Medal.None
            : deliveredCount == orders.Count && failedCount == 0 ? Medal.Gold
            : deliveredCount * 2 >= orders.Count ? Medal.Silver
            : Medal.Bronze;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.CompleteLevel(result);
        }
    }

    private void ClearZones()
    {
        if (pickupZone != null) { pickupZone.Hide(); pickupZone = null; }
        if (dropZone != null) { dropZone.Hide(); dropZone = null; }
    }

    #endregion

    private float CurrentTime => GameManager.Instance != null ? GameManager.Instance.ElapsedTime : Time.timeSinceLevelLoad;

    private void UpdateProgress()
    {
        if (GameManager.Instance == null || currentOrderIndex < 0 || currentOrderIndex >= orders.Count) return;

        string state = phase switch
        {
            Phase.GoToPickup => "前往取貨",
            Phase.Carrying => "運送中",
            Phase.CargoLoose => "放置貨物",
            _ => string.Empty
        };
        float remaining = Mathf.Max(0f, timeLimit - CurrentTime);
        string health = currentCargo != null ? $"  貨況 {currentCargo.Health:0}%" : string.Empty;
        GameManager.Instance.SetProgress(
            $"ORDER {currentOrderIndex + 1}/{orders.Count}  {state}{health}\n剩餘 {RuntimeUI.FormatTime(remaining).Substring(0, 5)}  分數 {totalScore}",
            (float)(deliveredCount + failedCount) / Mathf.Max(1, orders.Count));
    }
}

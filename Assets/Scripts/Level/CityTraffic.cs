using System.Collections.Generic;
using UnityEngine;

/// <summary>程序化城市的棋盤路網資訊 (路口 = 節點，兩節點之間 = 一段道路)</summary>
public struct CityGrid
{
    /// <summary>節點 (0,0) 的世界座標 (道路中心交會點)</summary>
    public Vector3 Origin;
    public float Pitch;
    public float RoadWidth;
    public float BlockSize;
    public float SidewalkHeight;
    public int Blocks;

    public Vector3 Node(Vector2Int node) => Origin + new Vector3(node.x * Pitch, 0f, node.y * Pitch);

    public bool Contains(Vector2Int node) => node.x >= 0 && node.y >= 0 && node.x <= Blocks && node.y <= Blocks;

    /// <summary>相鄰的路口數 (角落 2、邊緣 3、內部 4)</summary>
    public int Degree(Vector2Int node)
    {
        int degree = 0;
        foreach (Vector2Int step in Steps)
        {
            if (Contains(node + step)) degree++;
        }
        return degree;
    }

    /// <summary>街區 (bx, bz) 的邊界：x/z 最小值 (人行道外緣)</summary>
    public Vector2 BlockMin(int bx, int bz) => new Vector2(Origin.x + bx * Pitch + RoadWidth * 0.5f, Origin.z + bz * Pitch + RoadWidth * 0.5f);

    /// <summary>路口 node 往 diagonal 方向 (x、z 分量取正負號) 的街角是否有街區</summary>
    public bool BlockExists(Vector2Int node, Vector3 diagonal)
    {
        int bx = node.x + (diagonal.x > 0f ? 0 : -1);
        int bz = node.y + (diagonal.z > 0f ? 0 : -1);
        return bx >= 0 && bz >= 0 && bx < Blocks && bz < Blocks;
    }

    public static readonly Vector2Int[] Steps = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
}

/// <summary>
/// 城市車流 (台灣街景)：靠右行駛，每個方向兩車道，車輛只使用專案提供的汽車模型。
/// 內側車道直行/左轉、外側車道直行/右轉，轉彎路徑不互相交叉。
/// 路口有兩時相紅綠燈 (含黃燈與全紅)；車輛跟車保持距離、紅燈停在停止線、
/// 左轉讓對向直行、轉彎禮讓斑馬線上的行人。所有車輛由此元件統一更新 (不使用 NavMesh/物理)。
/// </summary>
public class CityTraffic : MonoBehaviour
{
    public enum Signal { Green, Yellow, Red }

    private const float GreenTime = 14f;
    private const float YellowTime = 3f;
    private const float AllRedTime = 1.5f;
    private const float HalfCycle = GreenTime + YellowTime + AllRedTime;
    private const float Accel = 2.6f;
    private const float Brake = 7f;
    private const float MinGap = 1.6f;
    private const float TurnSpeed = 5.5f;

    public static CityTraffic Instance { get; private set; }

    private class Lane
    {
        public Vector2Int From;
        public Vector2Int To;
        public bool Outer;
        public bool AlongX;
        public Vector3 Dir;
        public Vector3 Start;
        /// <summary>起點 (路口邊緣) 到停止線的距離</summary>
        public float Length;
        /// <summary>依車頭位置 S 由大到小排序 (index 0 = 最前面)</summary>
        public readonly List<Vehicle> Vehicles = new List<Vehicle>();
    }

    private enum Turn { Straight, Left, Right }

    private class Vehicle
    {
        public Transform Root;
        public Transform Visual;
        public Lane Lane;
        public Lane CameFrom;
        public Lane Next;
        public Turn NextTurn;
        public float S;          // 車頭沿車道的位置；負值 = 仍在路口內轉彎
        public float Speed;
        public float DesiredSpeed;
        public bool Wrecked;     // 被炸毀：原地停下燃燒，一段時間後清除
        public float Length;
        public bool Committed;
        // 轉彎 (二次貝茲曲線)
        public Vector3 P0, P1, P2;
        public float TurnLength;
    }

    private class SignalHead
    {
        public Vector2Int Node;
        public bool AlongX;
        /// <summary>一組燈號合併為一個 Renderer：子網格依序為 紅、黃、綠、行人燈</summary>
        public Renderer Lamps;
        public readonly Material[] Materials = new Material[4];
        public int Shown = -1;
    }

    private readonly Dictionary<int, Lane> lanes = new Dictionary<int, Lane>();
    private readonly List<Lane> laneList = new List<Lane>();
    private readonly List<Vehicle> vehicles = new List<Vehicle>();
    private readonly List<SignalHead> heads = new List<SignalHead>();
    /// <summary>超過 160m 的車輛不繪製 (仍會行駛)</summary>
    private readonly DistanceCuller culler = new DistanceCuller(160f, 15f);
    private float[] signalOffsets;
    private CityGrid grid;
    private System.Random random;

    private Material redOn, redOff, yellowOn, yellowOff, greenOn, greenOff, walkGreen, walkRed;

    public CityGrid Grid => grid;

    public void Initialize(CityGrid cityGrid, GameObject[] carPrefabs, float carsPerLane, int seed)
    {
        Instance = this;
        grid = cityGrid;
        random = new System.Random(seed);

        int nodes = grid.Blocks + 1;
        signalOffsets = new float[nodes * nodes];
        for (int i = 0; i < signalOffsets.Length; i++)
        {
            // 相鄰路口時相錯開，形成不同步的車流
            signalOffsets[i] = (float)random.NextDouble() * HalfCycle * 2f;
        }

        BuildLanes();
        BuildSignals();
        SpawnVehicles(carPrefabs, carsPerLane);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    #region 號誌

    private bool IsSignalized(Vector2Int node) => grid.Degree(node) >= 3;

    /// <summary>取得路口某方向 (沿 X 或沿 Z 行駛) 的燈號與剩餘秒數</summary>
    public Signal GetSignal(Vector2Int node, bool alongX, out float remaining)
    {
        if (!grid.Contains(node) || !IsSignalized(node))
        {
            remaining = 99f;
            return Signal.Green;
        }

        float t = Mathf.Repeat(Time.time + signalOffsets[node.x * (grid.Blocks + 1) + node.y], HalfCycle * 2f);
        bool xPhase = t < HalfCycle;
        float phaseTime = xPhase ? t : t - HalfCycle;

        if (xPhase != alongX)
        {
            remaining = HalfCycle - phaseTime;
            return Signal.Red;
        }
        if (phaseTime < GreenTime)
        {
            remaining = GreenTime - phaseTime;
            return Signal.Green;
        }
        if (phaseTime < GreenTime + YellowTime)
        {
            remaining = GreenTime + YellowTime - phaseTime;
            return Signal.Yellow;
        }
        remaining = HalfCycle - phaseTime;
        return Signal.Red;
    }

    /// <summary>行人能否穿越「沿 roadAlongX 方向」的道路：該道路車流為紅燈、另一向綠燈且剩餘時間足夠</summary>
    public bool PedestrianMayCross(Vector2Int node, bool roadAlongX, float secondsNeeded)
    {
        if (!grid.Contains(node) || !IsSignalized(node)) return true;
        return GetSignal(node, !roadAlongX, out float remaining) == Signal.Green && remaining >= secondsNeeded;
    }

    private void BuildSignals()
    {
        redOn = RuntimeMaterials.Unlit(new Color(1f, 0.12f, 0.08f));
        yellowOn = RuntimeMaterials.Unlit(new Color(1f, 0.72f, 0.08f));
        greenOn = RuntimeMaterials.Unlit(new Color(0.15f, 1f, 0.55f));
        walkGreen = greenOn;
        walkRed = redOn;
        redOff = RuntimeMaterials.Opaque(new Color(0.22f, 0.05f, 0.05f));
        yellowOff = RuntimeMaterials.Opaque(new Color(0.22f, 0.17f, 0.04f));
        greenOff = RuntimeMaterials.Opaque(new Color(0.04f, 0.18f, 0.1f));

        Material poleMaterial = RuntimeMaterials.Opaque(new Color(0.5f, 0.52f, 0.55f));
        Material housing = RuntimeMaterials.Opaque(new Color(0.08f, 0.08f, 0.09f));
        CityMeshBuilder builder = new CityMeshBuilder();
        Transform signalRoot = new GameObject("TrafficSignals").transform;
        signalRoot.SetParent(transform, false);
        Transform colliders = new GameObject("TrafficSignalColliders").transform;
        colliders.SetParent(signalRoot, false);

        float corner = grid.RoadWidth * 0.5f + 0.7f;
        for (int x = 0; x <= grid.Blocks; x++)
        {
            for (int z = 0; z <= grid.Blocks; z++)
            {
                Vector2Int node = new Vector2Int(x, z);
                if (!IsSignalized(node)) continue;

                foreach (Vector2Int step in CityGrid.Steps)
                {
                    Vector2Int from = node - step;
                    if (!grid.Contains(from)) continue;

                    Vector3 d = new Vector3(step.x, 0f, step.y);
                    Vector3 right = Vector3.Cross(Vector3.up, d);
                    // 燈桿放在路口右前方街角；T 字路口右前方沒有街區時改放左前方
                    float side;
                    if (grid.BlockExists(node, d + right)) side = 1f;
                    else if (grid.BlockExists(node, d - right)) side = -1f;
                    else continue;
                    Vector3 basePoint = grid.Node(node) + d * corner + right * corner * side + Vector3.up * grid.SidewalkHeight;

                    // frame：+Z 朝向來車駕駛，+X 為駕駛的左手邊；懸臂往路中央延伸
                    Matrix4x4 frame = Matrix4x4.TRS(basePoint, Quaternion.LookRotation(-d), Vector3.one);
                    float armSign = side;
                    builder.AddCylinder(frame, Vector3.zero, Vector3.up, 0.12f, 6.2f, 8, poleMaterial);
                    builder.AddBox(frame, new Vector3(armSign * 2.1f, 5.85f, 0f), new Vector3(4.2f, 0.12f, 0.12f), poleMaterial);
                    Vector3 headCenter = new Vector3(armSign * 3.6f, 5.5f, 0f);
                    builder.AddBox(frame, headCenter, new Vector3(1.3f, 0.46f, 0.3f), housing);
                    builder.AddBox(frame, new Vector3(0f, 2.75f, 0f), new Vector3(0.36f, 0.5f, 0.26f), housing);

                    BoxCollider pole = colliders.gameObject.AddComponent<BoxCollider>();
                    pole.center = colliders.InverseTransformPoint(basePoint + Vector3.up * 3.1f);
                    pole.size = new Vector3(0.3f, 6.2f, 0.3f);

                    SignalHead head = new SignalHead { Node = node, AlongX = step.x != 0 };
                    // 台灣橫式號誌：由駕駛視角左到右為 紅、黃、綠
                    CityMeshBuilder lamps = new CityMeshBuilder();
                    lamps.AddBox(frame, headCenter + new Vector3(0.4f, 0f, 0.16f), new Vector3(0.3f, 0.3f, 0.04f), redOff);
                    lamps.AddBox(frame, headCenter + new Vector3(0f, 0f, 0.16f), new Vector3(0.3f, 0.3f, 0.04f), yellowOff);
                    lamps.AddBox(frame, headCenter + new Vector3(-0.4f, 0f, 0.16f), new Vector3(0.3f, 0.3f, 0.04f), greenOff);
                    lamps.AddBox(frame, new Vector3(0f, 2.75f, 0.14f), new Vector3(0.24f, 0.24f, 0.04f), walkRed);
                    GameObject lampObject = lamps.Build("SignalLamps", signalRoot, false);
                    lampObject.isStatic = false;
                    head.Lamps = lampObject.GetComponent<Renderer>();
                    heads.Add(head);
                }
            }
        }

        builder.Build("TrafficSignalPoles", signalRoot);
        UpdateSignalVisuals(true);
    }

    private void UpdateSignalVisuals(bool force)
    {
        bool blinkOn = Mathf.Repeat(Time.time, 0.8f) < 0.4f;
        foreach (SignalHead head in heads)
        {
            Signal signal = GetSignal(head.Node, head.AlongX, out float remaining);
            // 行人燈：燈桿在路口對角，看燈的行人沿車流方向穿越「橫向道路」，
            // 橫向車流此時為紅燈 = 本方向綠燈 (與 PedestrianMayCross 一致)；剩 5 秒時閃爍
            int walk = signal == Signal.Green ? (remaining < 5f && !blinkOn ? 2 : 1) : 0;
            int state = (int)signal * 3 + walk;
            if (!force && state == head.Shown) continue;
            head.Shown = state;

            head.Materials[0] = signal == Signal.Red ? redOn : redOff;
            head.Materials[1] = signal == Signal.Yellow ? yellowOn : yellowOff;
            head.Materials[2] = signal == Signal.Green ? greenOn : greenOff;
            head.Materials[3] = walk == 1 ? walkGreen : walk == 2 ? greenOff : walkRed;
            head.Lamps.sharedMaterials = head.Materials;
        }
    }

    #endregion

    #region 車道

    private static int LaneKey(Vector2Int from, Vector2Int to, bool outer) =>
        ((from.x * 32 + from.y) * 1024 + to.x * 32 + to.y) * 2 + (outer ? 1 : 0);

    private Lane GetLane(Vector2Int from, Vector2Int to, bool outer)
    {
        lanes.TryGetValue(LaneKey(from, to, outer), out Lane lane);
        return lane;
    }

    private void BuildLanes()
    {
        for (int x = 0; x <= grid.Blocks; x++)
        {
            for (int z = 0; z <= grid.Blocks; z++)
            {
                Vector2Int from = new Vector2Int(x, z);
                foreach (Vector2Int step in CityGrid.Steps)
                {
                    Vector2Int to = from + step;
                    if (!grid.Contains(to)) continue;

                    for (int type = 0; type < 2; type++)
                    {
                        bool outer = type == 1;
                        Vector3 dir = new Vector3(step.x, 0f, step.y);
                        Vector3 right = Vector3.Cross(Vector3.up, dir);
                        float offset = outer ? CityGenerator.OuterLaneOffset : CityGenerator.InnerLaneOffset;

                        Lane lane = new Lane
                        {
                            From = from,
                            To = to,
                            Outer = outer,
                            AlongX = step.x != 0,
                            Dir = dir,
                            Start = grid.Node(from) + dir * (grid.RoadWidth * 0.5f) + right * offset + Vector3.up * 0.02f,
                            Length = grid.BlockSize - CityGenerator.CarStopDistance
                        };
                        lanes[LaneKey(from, to, outer)] = lane;
                        laneList.Add(lane);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 為剛進入車道的車輛決定下一個路口的去向 (直行為主，不迴轉)。
    /// 內側車道可直行/左轉、外側車道可直行/右轉；角落路口只能轉彎時維持原車道轉入。
    /// </summary>
    private void ChooseNext(Vehicle vehicle)
    {
        Lane lane = vehicle.Lane;
        Vector2Int step = lane.To - lane.From;
        Vector2Int rightStep = new Vector2Int(step.y, -step.x);
        Vector2Int leftStep = -rightStep;

        Lane straight = GetLane(lane.To, lane.To + step, lane.Outer);
        bool turnOnly = straight == null;
        Lane right = GetLane(lane.To, lane.To + rightStep, turnOnly ? lane.Outer : true);
        Lane left = GetLane(lane.To, lane.To + leftStep, turnOnly ? lane.Outer : false);

        float wStraight = straight != null ? 0.6f : 0f;
        float wRight = right != null && (lane.Outer || turnOnly) ? 0.4f : 0f;
        float wLeft = left != null && (!lane.Outer || turnOnly) ? 0.35f : 0f;
        float roll = (float)random.NextDouble() * (wStraight + wRight + wLeft);

        if (roll < wStraight) { vehicle.Next = straight; vehicle.NextTurn = Turn.Straight; }
        else if (roll < wStraight + wRight) { vehicle.Next = right; vehicle.NextTurn = Turn.Right; }
        else if (wLeft > 0f) { vehicle.Next = left; vehicle.NextTurn = Turn.Left; }
        else vehicle.Next = null;
        vehicle.Committed = false;
    }

    private void PrepareTurnGeometry(Vehicle vehicle)
    {
        Lane from = vehicle.Lane;
        Lane to = vehicle.Next;
        vehicle.P0 = from.Start + from.Dir * from.Length;
        vehicle.P2 = to.Start;

        if (vehicle.NextTurn == Turn.Straight)
        {
            vehicle.P1 = (vehicle.P0 + vehicle.P2) * 0.5f;
        }
        else
        {
            // 進入線與離開線的交點 (兩線互相垂直)
            Vector3 toP2 = vehicle.P2 - vehicle.P0;
            vehicle.P1 = vehicle.P0 + from.Dir * Vector3.Dot(toP2, from.Dir);
        }

        // 以折線近似弧長
        float length = 0f;
        Vector3 previous = vehicle.P0;
        for (int i = 1; i <= 8; i++)
        {
            Vector3 point = Bezier(vehicle.P0, vehicle.P1, vehicle.P2, i / 8f);
            length += Vector3.Distance(previous, point);
            previous = point;
        }
        vehicle.TurnLength = Mathf.Max(length, 1f);
    }

    private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
    }

    private static Vector3 BezierTangent(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        return 2f * (1f - t) * (b - a) + 2f * t * (c - b);
    }

    #endregion

    #region 車輛生成

    private void SpawnVehicles(GameObject[] carPrefabs, float carsPerLane)
    {
        Transform vehicleRoot = new GameObject("Vehicles").transform;
        vehicleRoot.SetParent(transform, false);

        List<GameObject> validCars = new List<GameObject>();
        if (carPrefabs != null)
        {
            foreach (GameObject prefab in carPrefabs)
            {
                if (prefab != null) validCars.Add(prefab);
            }
        }
        if (validCars.Count == 0)
        {
            Debug.LogWarning("[CityTraffic] CityGenerator 沒有指定車輛模型 (vehiclePrefabs)，街上不會有車。請執行選單「無人機遊戲/5. 套用台灣街景車輛與路人」。");
            return;
        }

        int carCount = Mathf.RoundToInt(laneList.Count * carsPerLane);
        for (int i = 0; i < carCount; i++)
        {
            Lane lane = FindFreeSpot(5f, out float s);
            if (lane == null) continue;
            Vehicle vehicle = CreateCar(validCars[random.Next(validCars.Count)], vehicleRoot);
            AddToLane(vehicle, lane, s);
        }

        foreach (Lane lane in laneList)
        {
            lane.Vehicles.Sort((a, b) => b.S.CompareTo(a.S));
        }
        foreach (Vehicle vehicle in vehicles)
        {
            PlaceVehicle(vehicle);
        }
    }

    private Lane FindFreeSpot(float length, out float s)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            Lane lane = laneList[random.Next(laneList.Count)];

            float candidate = length + (float)random.NextDouble() * (lane.Length - length);
            bool free = true;
            foreach (Vehicle other in lane.Vehicles)
            {
                if (Mathf.Abs(other.S - candidate) < length + other.Length + MinGap * 2f)
                {
                    free = false;
                    break;
                }
            }
            if (!free) continue;
            s = candidate;
            return lane;
        }
        s = 0f;
        return null;
    }

    private void AddToLane(Vehicle vehicle, Lane lane, float s)
    {
        vehicle.Lane = lane;
        vehicle.S = s;
        vehicle.Speed = vehicle.DesiredSpeed * 0.6f;
        lane.Vehicles.Add(vehicle);
        ChooseNext(vehicle);
        vehicles.Add(vehicle);
        culler.Add(vehicle.Root);
    }

    private Vehicle CreateCar(GameObject prefab, Transform parent)
    {
        GameObject rootObject = CreateCarModel(prefab, parent, out Transform visual, out Vector3 size);
        AddPhysics(rootObject, size);

        return new Vehicle
        {
            Root = rootObject.transform,
            Visual = visual,
            Length = size.z,
            DesiredSpeed = 9f + (float)random.NextDouble() * 3.5f
        };
    }

    /// <summary>
    /// 建立車輛外觀 (車頭朝 +Z、底部貼地、縮放為真實尺寸)，不含碰撞體。路邊停車場也使用。
    /// size 為車寬/高/長 (m)。
    /// </summary>
    public static GameObject CreateCarModel(GameObject prefab, Transform parent, out Transform visual, out Vector3 size)
    {
        GameObject rootObject = new GameObject("Car_" + prefab.name);
        rootObject.transform.SetParent(parent, false);

        GameObject model = Instantiate(prefab);
        model.name = prefab.name;
        foreach (Collider modelCollider in model.GetComponentsInChildren<Collider>())
        {
            if (Application.isPlaying) Destroy(modelCollider);
            else DestroyImmediate(modelCollider);
        }

        // 依模型長軸判斷車頭方向並縮放到真實尺寸 (約 4.4m，箱型車/貨卡 5m)
        model.transform.SetPositionAndRotation(Vector3.zero, prefab.transform.rotation);
        Bounds bounds = RendererBounds(model);
        bool alongX = bounds.size.x > bounds.size.z;
        string lowerName = prefab.name.ToLowerInvariant();
        float targetLength = lowerName.Contains("van") || lowerName.Contains("pick") ? 5f : 4.4f;
        float scale = targetLength / Mathf.Max(0.01f, Mathf.Max(bounds.size.x, bounds.size.z));

        visual = new GameObject("Visual").transform;
        visual.SetParent(rootObject.transform, false);
        model.transform.SetParent(visual, false);
        model.transform.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        model.transform.localRotation = prefab.transform.rotation;
        visual.localRotation = Quaternion.Euler(0f, alongX ? 90f : 0f, 0f);
        visual.localScale = Vector3.one * scale;

        size = new Vector3(Mathf.Min(bounds.size.x, bounds.size.z), bounds.size.y, Mathf.Max(bounds.size.x, bounds.size.z)) * scale;
        return rootObject;
    }

    /// <summary>無人機可以撞到車輛：運動學剛體 + 方塊碰撞體</summary>
    private static void AddPhysics(GameObject target, Vector3 size)
    {
        BoxCollider box = target.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, size.y * 0.5f, 0f);
        box.size = size;
        Rigidbody body = target.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.None;
    }

    private static Bounds RendererBounds(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(target.transform.position, Vector3.one);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    #endregion

    #region 每幀更新

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        UpdateSignalVisuals(false);
        culler.Update();

        // 先決定速度 (以本幀開始時的位置計算)，再移動
        foreach (Lane lane in laneList)
        {
            for (int i = 0; i < lane.Vehicles.Count; i++)
            {
                UpdateSpeed(lane, i, dt);
            }
        }

        // 由前往後移動，最前面的車可能進入下一條車道 (會改動清單，所以先複製一份)
        transfers.Clear();
        foreach (Lane lane in laneList)
        {
            for (int i = 0; i < lane.Vehicles.Count; i++)
            {
                Vehicle vehicle = lane.Vehicles[i];
                vehicle.S += vehicle.Speed * dt;
                if (i == 0 && vehicle.S >= lane.Length)
                {
                    if (vehicle.Committed && vehicle.Next != null) transfers.Add(vehicle);
                    else vehicle.S = lane.Length;
                }
            }
        }

        foreach (Vehicle vehicle in transfers)
        {
            EnterNextLane(vehicle);
        }

        foreach (Vehicle vehicle in vehicles)
        {
            PlaceVehicle(vehicle);
        }
    }

    private readonly List<Vehicle> transfers = new List<Vehicle>();

    private void UpdateSpeed(Lane lane, int index, float dt)
    {
        Vehicle vehicle = lane.Vehicles[index];
        if (vehicle.Wrecked)
        {
            vehicle.Speed = 0f;
            return;
        }
        float target = vehicle.DesiredSpeed;
        float gap;

        if (vehicle.S < 0f && vehicle.CameFrom != null && vehicle.CameFrom.Dir != lane.Dir)
        {
            target = Mathf.Min(target, TurnSpeed); // 路口內轉彎減速
        }

        if (index > 0)
        {
            Vehicle leader = lane.Vehicles[index - 1];
            gap = leader.S - leader.Length - vehicle.S;
        }
        else
        {
            float toStop = lane.Length - vehicle.S;
            if (!vehicle.Committed && vehicle.Next != null && CanProceed(vehicle, toStop))
            {
                vehicle.Committed = true;
            }

            if (vehicle.Committed && vehicle.Next != null)
            {
                gap = toStop + EstimateTurnLength(vehicle) + NextLaneRear(vehicle);
                if (vehicle.NextTurn != Turn.Straight)
                {
                    // 接近路口準備轉彎：提早減速
                    target = Mathf.Min(target, TurnSpeed + toStop * 0.45f);
                }
            }
            else
            {
                gap = toStop + MinGap - 0.3f;
            }
        }

        float safe = Mathf.Sqrt(Mathf.Max(0f, 2f * Brake * 0.6f * (gap - MinGap)));
        target = Mathf.Min(target, safe);
        vehicle.Speed = target > vehicle.Speed
            ? Mathf.MoveTowards(vehicle.Speed, target, Accel * dt)
            : Mathf.MoveTowards(vehicle.Speed, target, Brake * 1.5f * dt);
    }

    /// <summary>下一條車道最後一輛車的車尾位置 (空車道回傳一個大值)</summary>
    private float NextLaneRear(Vehicle vehicle)
    {
        List<Vehicle> nextVehicles = vehicle.Next.Vehicles;
        if (nextVehicles.Count == 0) return 60f;
        Vehicle last = nextVehicles[nextVehicles.Count - 1];
        return last.S - last.Length;
    }

    /// <summary>停止線前的車輛能否通過路口：號誌、下一段車道空間、左轉讓直行、禮讓行人</summary>
    private bool CanProceed(Vehicle vehicle, float toStop)
    {
        Lane lane = vehicle.Lane;
        float stoppingDistance = vehicle.Speed * vehicle.Speed / (2f * Brake * 0.6f);
        // 太遠時先不做決定 (保持跟車/停等)
        if (toStop > Mathf.Max(3f, stoppingDistance + 2f)) return false;

        Signal signal = GetSignal(lane.To, lane.AlongX, out _);
        bool waitingAtLine = vehicle.Speed < 0.5f && toStop < 1.5f;
        bool signalOk = signal == Signal.Green
                        || (signal == Signal.Yellow && (stoppingDistance > toStop - 0.5f || (vehicle.NextTurn == Turn.Left && waitingAtLine)));
        if (!signalOk) return false;

        // 下一段車道要有空間 (跟著同車道前車通過時，交給跟車距離處理)
        List<Vehicle> nextVehicles = vehicle.Next.Vehicles;
        if (nextVehicles.Count > 0)
        {
            Vehicle last = nextVehicles[nextVehicles.Count - 1];
            if (last.CameFrom != lane && last.S - last.Length < vehicle.Length + 2f) return false;
        }

        if (vehicle.NextTurn == Turn.Left && !OpposingClear(lane)) return false;

        CityPedestrians pedestrians = CityPedestrians.Instance;
        if (pedestrians != null && vehicle.NextTurn != Turn.Straight &&
            (pedestrians.IsCrosswalkOccupied(lane.To, vehicle.Next.To) || pedestrians.IsCrosswalkOccupied(lane.To, lane.From)))
        {
            return false;
        }

        return true;
    }

    /// <summary>左轉前確認對向沒有即將進入路口或正在路口內的直行車</summary>
    private bool OpposingClear(Lane lane)
    {
        Vector2Int node = lane.To;
        Vector2Int opposingFrom = node + (node - lane.From);
        for (int type = 0; type < 2; type++)
        {
            bool outer = type == 1;
            Lane opposing = GetLane(opposingFrom, node, outer);
            if (opposing != null)
            {
                foreach (Vehicle other in opposing.Vehicles)
                {
                    float toStop = opposing.Length - other.S;
                    if (toStop < 22f && (other.Speed > 1f || other.Committed)) return false;
                }
            }

            Lane opposingExit = GetLane(node, lane.From, outer);
            if (opposingExit != null)
            {
                foreach (Vehicle other in opposingExit.Vehicles)
                {
                    if (other.S < 0f) return false;
                }
            }
        }
        return true;
    }

    private void EnterNextLane(Vehicle vehicle)
    {
        Lane from = vehicle.Lane;
        Lane next = vehicle.Next;
        float overshoot = vehicle.S - from.Length;

        PrepareTurnGeometry(vehicle);
        from.Vehicles.Remove(vehicle);

        vehicle.CameFrom = from;
        vehicle.Lane = next;
        vehicle.S = overshoot - vehicle.TurnLength;

        // 依 S 由大到小插入
        int insertAt = next.Vehicles.Count;
        while (insertAt > 0 && next.Vehicles[insertAt - 1].S < vehicle.S) insertAt--;
        next.Vehicles.Insert(insertAt, vehicle);

        ChooseNext(vehicle);
    }

    private void PlaceVehicle(Vehicle vehicle)
    {
        Lane lane = vehicle.Lane;
        Vector3 front;
        Vector3 forward;

        if (vehicle.S < 0f && vehicle.CameFrom != null)
        {
            float t = Mathf.Clamp01((vehicle.TurnLength + vehicle.S) / vehicle.TurnLength);
            front = Bezier(vehicle.P0, vehicle.P1, vehicle.P2, t);
            forward = BezierTangent(vehicle.P0, vehicle.P1, vehicle.P2, t);
            if (forward.sqrMagnitude < 0.0001f) forward = lane.Dir;
            forward.Normalize();
        }
        else
        {
            front = lane.Start + lane.Dir * vehicle.S;
            forward = lane.Dir;
        }

        Vector3 position = front - forward * (vehicle.Length * 0.5f);
        vehicle.Root.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
    }

    /// <summary>尚未進入路口時估計轉彎路徑長度 (直行約為路口寬 + 停止線距離)</summary>
    private float EstimateTurnLength(Vehicle vehicle)
    {
        Lane from = vehicle.Lane;
        Lane to = vehicle.Next;
        Vector3 p0 = from.Start + from.Dir * from.Length;
        return Vector3.Distance(p0, to.Start) * (vehicle.NextTurn == Turn.Straight ? 1f : 1.12f);
    }

    #endregion

    #region 炸彈損毀

    [Header("炸彈損毀")]
    [Tooltip("被炸毀的車輛停在原地燃燒多久後清除 (秒)，清除後後方車流恢復")]
    [SerializeField] private float wreckClearSeconds = 40f;

    /// <summary>
    /// 炸彈命中：hit 屬於某輛行駛中的車時，讓它立刻停下 (後方車輛會排隊等候) 並回傳 true。
    /// 外觀的火焰/煙霧由 <see cref="BurningDamage"/> 處理。
    /// </summary>
    public bool TryWreck(Transform hit)
    {
        if (hit == null) return false;
        foreach (Vehicle vehicle in vehicles)
        {
            if (vehicle.Root == null || (hit != vehicle.Root && !hit.IsChildOf(vehicle.Root))) continue;
            if (vehicle.Wrecked) return true;

            vehicle.Wrecked = true;
            vehicle.Speed = 0f;
            vehicle.DesiredSpeed = 0f;
            StartCoroutine(ClearWreckLater(vehicle));
            return true;
        }
        return false;
    }

    private System.Collections.IEnumerator ClearWreckLater(Vehicle vehicle)
    {
        yield return new WaitForSeconds(wreckClearSeconds);
        if (vehicle.Lane != null) vehicle.Lane.Vehicles.Remove(vehicle);
        vehicles.Remove(vehicle);
        if (vehicle.Root != null) Destroy(vehicle.Root.gameObject);
    }

    #endregion

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }
}

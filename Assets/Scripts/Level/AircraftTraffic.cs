using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 空中障礙：遊戲關卡 (有 GameManager 的場景) 自動加入幾架小飛機 (GameplayAssets.airplanePrefab)，
/// 在檢查圈附近低空盤旋 (城市生成器關卡沿街道上空繞街區、其他場景為圓形航線)，無人機撞到會墜機。
/// 航線在生成時以 SphereCast 逐段檢查，選擇「不會碰到任何建築/路燈/檢查圈」的最低高度；
/// 航線範圍重疊的飛機高度錯開，彼此也不會相撞或穿模。
/// </summary>
public class AircraftTraffic : MonoBehaviour
{
    [SerializeField] private int aircraftCount = 3;
    [Tooltip("飛機模型縮放 (Air Plane_1 原尺寸翼展約 9m)")]
    [SerializeField] private float planeScale = 0.5f;
    [Tooltip("最低飛行高度 (離地 m)，貼近無人機的飛行高度")]
    [SerializeField] private float minHeight = 6f;
    [Tooltip("最高飛行高度 (離地 m)，找不到安全航線時這架飛機不生成")]
    [SerializeField] private float maxHeight = 90f;
    [Tooltip("航線水平範圍重疊的兩架飛機之間的最小高度差 (m)")]
    [SerializeField] private float layerSeparation = 4.5f;
    [Tooltip("航線與建築物之間的安全距離 (m，另加上機身半徑)")]
    [SerializeField] private float clearance = 1.5f;
    [Tooltip("航線與檢查圈之間的水平/垂直安全距離 (m)")]
    [SerializeField] private float checkpointClearance = 2.5f;
    [SerializeField] private Vector2 speedRange = new Vector2(10f, 15f);
    [SerializeField] private int pathSegments = 56;

    private Bounds area;
    private float groundHeight;
    private readonly List<float> usedHeights = new List<float>();
    private readonly List<(Vector3 center, float radius, float height)> usedLoops = new List<(Vector3, float, float)>();
    private readonly List<Vector3> flightZone = new List<Vector3>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryCreate();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) TryCreate();
    }

    private static void TryCreate()
    {
        if (FindFirstObjectByType<GameManager>() == null || FindFirstObjectByType<AircraftTraffic>() != null) return;
        GameplayAssets assets = GameplayAssets.Instance;
        if (assets == null || assets.airplanePrefab == null) return;
        new GameObject("AircraftTraffic").AddComponent<AircraftTraffic>();
    }

    private IEnumerator Start()
    {
        // 等城市生成器、檢查圈與無人機都就位後再規劃航線
        yield return null;
        yield return new WaitForSeconds(0.5f);

        if (!ComputeArea())
        {
            Debug.Log("[AircraftTraffic] 找不到關卡範圍，不生成飛機。");
            yield break;
        }

        GameObject prefab = GameplayAssets.Instance.airplanePrefab;
        Vector3 planeSize = MeasurePrefab(prefab) * planeScale;
        float bodyRadius = Mathf.Max(planeSize.x, planeSize.y, planeSize.z) * 0.5f;
        System.Random random = new System.Random(SceneManager.GetActiveScene().name.GetHashCode());

        // 收集所有可行航線 (每條取「最低的淨空高度」)，越低、越靠近檢查圈越優先
        List<Candidate> candidates = new List<Candidate>();
        CollectStreetCandidates(bodyRadius, random, candidates);
        yield return CollectCircleCandidates(bodyRadius, random, candidates);
        candidates.Sort((a, b) => a.Score.CompareTo(b.Score));

        // 先挑彼此分散的航線，不夠再放寬；重疊範圍的航線一定高度錯開
        List<Candidate> picked = new List<Candidate>();
        for (int pass = 0; pass < 2 && picked.Count < aircraftCount; pass++)
        {
            foreach (Candidate candidate in candidates)
            {
                if (picked.Count >= aircraftCount) break;
                if (picked.Contains(candidate) || !HeightLayerFree(candidate.Center, candidate.Radius, candidate.Height, 1f)) continue;
                if (pass == 0 && picked.Exists(p => Vector2.Distance(Flat2(p.Center), Flat2(candidate.Center)) < 30f)) continue;
                picked.Add(candidate);
                usedLoops.Add((candidate.Center, candidate.Radius, candidate.Height));
            }
        }

        for (int i = 0; i < picked.Count; i++)
        {
            List<Vector3> path = picked[i].Path;
            usedHeights.Add(picked[i].Height);
            GameObject plane = Instantiate(prefab, path[0], Quaternion.identity, transform);
            plane.name = $"Aircraft_{i + 1}";
            plane.transform.localScale = prefab.transform.localScale * planeScale;
            Aircraft aircraft = plane.AddComponent<Aircraft>();
            aircraft.Initialize(path, Mathf.Lerp(speedRange.x, speedRange.y, (float)random.NextDouble()), (float)random.NextDouble());
        }
        Debug.Log($"[AircraftTraffic] 候選航線 {candidates.Count} 條，生成 {picked.Count} 架飛機，離地高度：{string.Join(", ", usedHeights.ConvertAll(h => $"{h - groundHeight:0}m"))}");
    }

    private class Candidate
    {
        public List<Vector3> Path;
        public Vector3 Center;
        public float Radius;
        public float Height;
        public float Score;
    }

    private static Vector2 Flat2(Vector3 v) => new Vector2(v.x, v.z);

    private float DistanceToFlightZone(Vector3 point)
    {
        float nearest = float.MaxValue;
        foreach (Vector3 zone in flightZone) nearest = Mathf.Min(nearest, Vector2.Distance(Flat2(zone), Flat2(point)));
        return nearest;
    }

    /// <summary>由低往高找第一個完全淨空的高度，回傳該高度的航線 (找不到回傳 null)</summary>
    private List<Vector3> LowestClearPath(System.Func<float, List<Vector3>> build, float bodyRadius, out float height)
    {
        for (float h = minHeight; h <= maxHeight; h += 2f)
        {
            height = groundHeight + h;
            List<Vector3> points = build(height);
            if (PathClear(points, bodyRadius + clearance)) return points;
        }
        height = 0f;
        return null;
    }

    private void AddCandidate(List<Candidate> candidates, List<Vector3> path, Vector3 center, float radius, float height)
    {
        if (path == null) return;
        float score = (height - groundHeight) + DistanceToFlightZone(center) * 0.08f - radius * 0.05f;
        candidates.Add(new Candidate { Path = path, Center = center, Radius = radius, Height = height, Score = score });
    }

    /// <summary>城市生成器關卡：沿街道上空繞街區 (道路上方沒有建築，可壓低到檢查圈的高度)</summary>
    private void CollectStreetCandidates(float bodyRadius, System.Random random, List<Candidate> candidates)
    {
        CityGenerator city = FindFirstObjectByType<CityGenerator>();
        if (city == null) return;
        foreach (Vector3[] loop in city.GetStreetLoops())
        {
            Vector3 center = (loop[0] + loop[2]) * 0.5f;
            if (DistanceToFlightZone(center) > 80f) continue;
            bool reverse = random.NextDouble() < 0.5;
            List<Vector3> path = LowestClearPath(h => BuildRoundedLoop(loop, h, reverse), bodyRadius, out float height);
            AddCandidate(candidates, path, center, Vector3.Distance(loop[0], loop[2]) * 0.5f, height);
        }
    }

    /// <summary>
    /// 一般場景 (例如 City2)：在檢查圈附近以網格系統性地嘗試不同大小的圓形航線，
    /// 找出能壓低飛過低矮建築區的航線 (分散在多幀計算，避免開場卡頓)
    /// </summary>
    private IEnumerator CollectCircleCandidates(float bodyRadius, System.Random random, List<Candidate> candidates)
    {
        float[] radii = { 12f, 18f, 26f, 36f };
        const float step = 16f;
        for (float x = area.min.x; x <= area.max.x; x += step)
        {
            for (float z = area.min.z; z <= area.max.z; z += step)
            {
                Vector3 center = new Vector3(x, 0f, z);
                if (DistanceToFlightZone(center) > 45f) continue;
                foreach (float radius in radii)
                {
                    bool clockwise = random.NextDouble() < 0.5;
                    List<Vector3> path = LowestClearPath(h => BuildEllipse(center, radius, radius, 0f, h, 1f, clockwise), bodyRadius, out float height);
                    AddCandidate(candidates, path, center, radius, height);
                }
                yield return null;
            }
        }
    }

    /// <summary>關卡範圍：檢查圈 + 起飛點 + 送貨點；沒有任何目標時以無人機附近 150m 為範圍</summary>
    private bool ComputeArea()
    {
        bool has = false;
        Bounds bounds = default;
        void Add(Vector3 point)
        {
            if (!has) { bounds = new Bounds(point, Vector3.zero); has = true; }
            else bounds.Encapsulate(point);
        }

        flightZone.Clear();
        foreach (RingTrigger ring in RingTrigger.All)
        {
            if (ring == null) continue;
            Add(ring.transform.position);
            flightZone.Add(ring.transform.position);
        }
        foreach (DeliveryZone zone in FindObjectsByType<DeliveryZone>(FindObjectsSortMode.None))
        {
            Add(zone.transform.position);
            flightZone.Add(zone.transform.position);
        }
        DroneController1 drone = FindFirstObjectByType<DroneController1>();
        if (drone != null)
        {
            Add(drone.transform.position);
            flightZone.Add(drone.transform.position);
        }
        if (!has) return false;

        // 圓形航線候選的搜尋範圍 (至少 120m 見方)
        Vector3 size = bounds.size;
        size.x = Mathf.Max(size.x + 40f, 120f);
        size.z = Mathf.Max(size.z + 40f, 120f);
        bounds.size = new Vector3(size.x, 0f, size.z);
        area = bounds;

        groundHeight = drone != null ? drone.transform.position.y : bounds.center.y;
        if (Physics.Raycast(new Vector3(bounds.center.x, bounds.center.y + 500f, bounds.center.z), Vector3.down, out RaycastHit hit, 1000f, ~0, QueryTriggerInteraction.Ignore))
        {
            groundHeight = Mathf.Min(groundHeight, hit.point.y);
        }
        return true;
    }

    /// <summary>四個路口組成的迴圈：直線段每 5m 一點，轉角以 9m 半徑圓弧繞過路口，高度輕微起伏</summary>
    private static List<Vector3> BuildRoundedLoop(Vector3[] corners, float height, bool reverse)
    {
        Vector3[] c = (Vector3[])corners.Clone();
        if (reverse) System.Array.Reverse(c);
        for (int i = 0; i < c.Length; i++) c[i].y = height;

        const float cornerRadius = 9f;
        List<Vector3> points = new List<Vector3>();
        for (int i = 0; i < c.Length; i++)
        {
            Vector3 previous = c[(i - 1 + c.Length) % c.Length];
            Vector3 corner = c[i];
            Vector3 next = c[(i + 1) % c.Length];
            Vector3 entry = corner + (previous - corner).normalized * cornerRadius;
            Vector3 exit = corner + (next - corner).normalized * cornerRadius;

            for (int s = 0; s <= 4; s++)
            {
                float t = s / 4f;
                points.Add((1f - t) * (1f - t) * entry + 2f * (1f - t) * t * corner + t * t * exit);
            }

            // 轉角出口到下一個轉角入口的直線段
            Vector3 nextEntry = next + (corner - next).normalized * cornerRadius;
            float length = Vector3.Distance(exit, nextEntry);
            int steps = Mathf.Max(1, Mathf.FloorToInt(length / 5f));
            for (int s = 1; s < steps; s++)
            {
                Vector3 point = Vector3.Lerp(exit, nextEntry, s / (float)steps);
                point.y += Mathf.Sin(s * 0.6f) * 0.8f;
                points.Add(point);
            }
        }
        return points;
    }

    /// <summary>水平範圍和既有航線重疊時，高度必須錯開，避免兩架飛機相撞</summary>
    private bool HeightLayerFree(Vector3 center, float radius, float height, float wave)
    {
        foreach (var loop in usedLoops)
        {
            bool overlap = Vector2.Distance(new Vector2(center.x, center.z), new Vector2(loop.center.x, loop.center.z)) < radius + loop.radius + 10f;
            if (overlap && Mathf.Abs(loop.height - height) < layerSeparation + wave * 2f) return false;
        }
        return true;
    }

    private List<Vector3> BuildEllipse(Vector3 center, float radiusX, float radiusZ, float rotationDegrees, float height, float wave, bool clockwise)
    {
        List<Vector3> points = new List<Vector3>(pathSegments);
        Quaternion rotation = Quaternion.Euler(0f, rotationDegrees, 0f);
        for (int i = 0; i < pathSegments; i++)
        {
            float angle = (clockwise ? -1f : 1f) * i * Mathf.PI * 2f / pathSegments;
            Vector3 local = new Vector3(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle * 2f) * wave, Mathf.Sin(angle) * radiusZ);
            points.Add(center + rotation * local + Vector3.up * height);
        }
        return points;
    }

    /// <summary>逐段 SphereCast：碰到任何非觸發碰撞體 (建築、路燈、電線…) 或靠近檢查圈都視為不可用</summary>
    private bool PathClear(List<Vector3> points, float radius)
    {
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 a = points[i];
            Vector3 b = points[(i + 1) % points.Count];
            Vector3 delta = b - a;
            if (Physics.CheckSphere(a, radius, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (Physics.SphereCast(a, radius, delta.normalized, out _, delta.magnitude, ~0, QueryTriggerInteraction.Ignore)) return false;

            foreach (RingTrigger ring in RingTrigger.All)
            {
                if (ring == null) continue;
                Vector3 closest = ClosestPointOnSegment(ring.transform.position, a, b);
                if (Vector3.Distance(closest, ring.transform.position) < checkpointClearance + radius) return false;
            }
        }
        return true;
    }

    private static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
        return a + ab * t;
    }

    private static Vector3 MeasurePrefab(GameObject prefab)
    {
        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Vector3(10f, 3f, 9f);
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer target in renderers) bounds.Encapsulate(target.bounds);
        return bounds.size;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
}

/// <summary>單架飛機：沿封閉航線 (Catmull-Rom 平滑) 等速飛行，轉彎時壓坡度，螺旋槳轉動；運動學剛體讓無人機撞得到</summary>
public class Aircraft : MonoBehaviour
{
    private List<Vector3> path;
    private float speed;
    private float progress;          // 以「航點段」為單位的位置 (0 ~ path.Count)
    private float[] segmentLengths;
    private Rigidbody body;
    private Transform propeller;
    private float bank;

    public void Initialize(List<Vector3> route, float flightSpeed, float startFraction)
    {
        path = route;
        speed = flightSpeed;
        segmentLengths = new float[path.Count];
        for (int i = 0; i < path.Count; i++)
        {
            segmentLengths[i] = Vector3.Distance(path[i], path[(i + 1) % path.Count]);
        }
        progress = startFraction * path.Count;

        body = GetComponent<Rigidbody>();
        if (body == null) body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        if (GetComponentInChildren<Collider>() == null) gameObject.AddComponent<BoxCollider>();

        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child.name.Contains("Fan") || child.name.ToLowerInvariant().Contains("prop")) propeller = child;
        }

        transform.SetPositionAndRotation(Evaluate(progress), Quaternion.LookRotation(Tangent(progress)));
    }

    private void FixedUpdate()
    {
        if (path == null) return;

        int segment = Mathf.FloorToInt(progress) % path.Count;
        progress += speed * Time.fixedDeltaTime / Mathf.Max(segmentLengths[segment], 0.01f);
        if (progress >= path.Count) progress -= path.Count;

        Vector3 position = Evaluate(progress);
        Vector3 forward = Tangent(progress);
        Vector3 ahead = Tangent(progress + 0.5f);

        // 依轉彎方向壓坡度 (右轉右翼下沉)
        float turn = Vector3.SignedAngle(new Vector3(forward.x, 0f, forward.z), new Vector3(ahead.x, 0f, ahead.z), Vector3.up);
        bank = Mathf.Lerp(bank, Mathf.Clamp(-turn * 4f, -35f, 35f), 2f * Time.fixedDeltaTime);
        Quaternion rotation = Quaternion.LookRotation(forward) * Quaternion.Euler(0f, 0f, bank);

        body.MovePosition(position);
        body.MoveRotation(rotation);
    }

    private void Update()
    {
        if (propeller != null) propeller.Rotate(0f, 0f, 1500f * Time.deltaTime, Space.Self);
    }

    private Vector3 Evaluate(float t)
    {
        int count = path.Count;
        t = Mathf.Repeat(t, count);
        int i = Mathf.FloorToInt(t);
        float f = t - i;
        Vector3 p0 = path[(i - 1 + count) % count];
        Vector3 p1 = path[i % count];
        Vector3 p2 = path[(i + 1) % count];
        Vector3 p3 = path[(i + 2) % count];
        return 0.5f * (2f * p1 + (-p0 + p2) * f + (2f * p0 - 5f * p1 + 4f * p2 - p3) * f * f + (-p0 + 3f * p1 - 3f * p2 + p3) * f * f * f);
    }

    private Vector3 Tangent(float t)
    {
        Vector3 tangent = Evaluate(t + 0.05f) - Evaluate(t - 0.05f);
        return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : transform.forward;
    }
}

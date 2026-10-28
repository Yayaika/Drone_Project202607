using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 打擊模式的敵方無人機：子物件依序為航點 (航線)，執行時生成一架無人機沿航線巡航。
/// 玩家靠近 (alertRadius) 時會在航點處選擇遠離玩家的方向折返，進入 fleeRadius 內則加速逃離，形成追逐；
/// 航線為開放路線 (loop 關閉) 時到端點折返。
/// <see cref="StrikeMissionManager"/> 會把每架敵機標記為 <see cref="StrikeTarget.TargetKind.Drone"/> 目標：
/// 撞上去或炸彈在看得到它的地方爆炸即可擊落，擊落後墜地燃燒。
/// 航點之間以直線飛行，擺放航點時要確認航段沒有穿過管線或牆面 (Gizmos 會畫出航線)。
/// </summary>
public class EnemyDronePatrol : MonoBehaviour
{
    [Tooltip("外觀模型 (留空時以方塊組成)")]
    [SerializeField] private GameObject modelPrefab;
    [Tooltip("模型機頭朝 -Z 時勾選 (本專案的 Racing Drone 模型機頭為 -Z)")]
    [SerializeField] private bool modelNoseIsBackward = true;
    [SerializeField] private float modelWidth = 0.9f;
    [Tooltip("首尾相連的環狀航線；關閉時到端點折返")]
    [SerializeField] private bool loop = true;
    [SerializeField] private float cruiseSpeed = 4f;
    [Tooltip("逃離速度 (玩家無人機 30° 前傾極速約 11 m/s，略慢一點才追得上)")]
    [SerializeField] private float fleeSpeed = 8.5f;
    [SerializeField] private float acceleration = 6f;
    [Tooltip("玩家在此距離內：到航點時改往遠離玩家的方向飛")]
    [SerializeField] private float alertRadius = 26f;
    [Tooltip("玩家在此距離內：加速逃離")]
    [SerializeField] private float fleeRadius = 14f;

    private readonly List<Vector3> points = new List<Vector3>();
    private Transform body;
    private Rigidbody bodyRigidbody;
    private int target;
    private int direction = 1;
    private float speed;
    private bool wrecked;

    /// <summary>無人機本體 (StrikeTarget 掛在這裡)；航點不足時為 null</summary>
    public Transform Body => body;

    private void Awake()
    {
        foreach (Transform child in transform) points.Add(child.position);
        if (points.Count < 2) return;

        body = BuildBody().transform;
        body.position = points[0];
        target = 1;
        speed = cruiseSpeed;
        FaceToward(points[1], 1f);
    }

    /// <summary>撞擊擊落後的重新部署點：離無人機最近的航點 (航點都在開闊空中)</summary>
    public Vector3 NearestWaypoint(Vector3 from)
    {
        Vector3 best = from + Vector3.up * 6f;
        float bestDistance = float.MaxValue;
        foreach (Vector3 point in points)
        {
            float distance = (point - from).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = point;
            }
        }
        return best;
    }

    private void FixedUpdate()
    {
        if (body == null || wrecked) return;

        Transform player = PlayerDrone();
        float playerDistance = player != null ? Vector3.Distance(player.position, body.position) : float.MaxValue;
        float desired = playerDistance <= fleeRadius ? fleeSpeed : Mathf.Lerp(fleeSpeed, cruiseSpeed, Mathf.InverseLerp(fleeRadius, alertRadius, playerDistance));
        speed = Mathf.MoveTowards(speed, desired, acceleration * Time.fixedDeltaTime);

        Vector3 position = body.position;
        Vector3 goal = points[target];
        float step = speed * Time.fixedDeltaTime;
        if ((goal - position).magnitude <= step)
        {
            position = goal;
            ChooseNext(player, playerDistance);
        }
        else
        {
            position += (goal - position).normalized * step;
        }

        // 輕微上下浮動，看起來像在懸停修正
        Vector3 bob = Vector3.up * (Mathf.Sin(Time.time * 2.3f + GetInstanceID()) * 0.004f);
        bodyRigidbody.MovePosition(position + bob);
        FaceToward(points[target], Time.fixedDeltaTime * 6f);
    }

    /// <summary>到達航點：依序前往下一個；玩家在警戒範圍內時改走離玩家較遠的那一側</summary>
    private void ChooseNext(Transform player, float playerDistance)
    {
        int current = target;
        int forward = Step(current, direction);
        int backward = Step(current, -direction);
        if (forward < 0) direction = -direction;
        else if (player != null && playerDistance <= alertRadius && backward >= 0
            && (points[backward] - player.position).sqrMagnitude > (points[forward] - player.position).sqrMagnitude)
        {
            direction = -direction;
        }
        target = Step(current, direction);
        if (target < 0) target = current;
    }

    /// <summary>沿 dir 走一步的航點索引；開放航線走出端點時回傳 -1</summary>
    private int Step(int index, int dir)
    {
        int next = index + dir;
        if (loop) return (next + points.Count) % points.Count;
        return next >= 0 && next < points.Count ? next : -1;
    }

    private void FaceToward(Vector3 goal, float t)
    {
        Vector3 flat = goal - body.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.01f) return;
        // 速度越快前傾越多
        float pitch = Mathf.Lerp(4f, 18f, Mathf.InverseLerp(cruiseSpeed, fleeSpeed, speed));
        Quaternion goalRotation = Quaternion.LookRotation(flat) * Quaternion.Euler(pitch, 0f, 0f);
        body.rotation = Quaternion.Slerp(body.rotation, goalRotation, Mathf.Clamp01(t));
    }

    private static Transform PlayerDrone()
    {
        DroneController1 drone = GameManager.Instance != null ? GameManager.Instance.GetDrone() : null;
        return drone != null ? drone.transform : null;
    }

    /// <summary>擊落：停止巡航、翻滾墜落到下方地面後起火</summary>
    public void Wreck()
    {
        if (wrecked || body == null) return;
        wrecked = true;
        StartCoroutine(Fall());
    }

    private IEnumerator Fall()
    {
        float floorY = body.position.y - 30f;
        foreach (RaycastHit hit in Physics.RaycastAll(body.position, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(body)) continue;
            floorY = Mathf.Max(floorY, hit.point.y);
        }

        float velocity = 0f;
        Vector3 spin = new Vector3(Random.Range(-1f, 1f), 1f, Random.Range(-1f, 1f)).normalized * 420f;
        while (body.position.y > floorY + 0.15f)
        {
            velocity += 9.81f * Time.deltaTime;
            Vector3 position = body.position;
            position.y = Mathf.Max(floorY + 0.15f, position.y - velocity * Time.deltaTime);
            body.position = position;
            body.Rotate(spin * Time.deltaTime, Space.World);
            yield return null;
        }
        GameAudio.PlayAt(GameSfx.Crash, body.position, 0.8f);
        BurningDamage.Ignite(body, false);
    }

    private GameObject BuildBody()
    {
        GameObject root = new GameObject("EnemyDrone");
        root.transform.SetParent(transform, false);

        Transform visual;
        if (modelPrefab != null)
        {
            visual = Instantiate(modelPrefab, root.transform).transform;
            foreach (Collider modelCollider in visual.GetComponentsInChildren<Collider>(true)) Destroy(modelCollider);
            foreach (MonoBehaviour behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(behaviour);
            foreach (Rigidbody modelBody in visual.GetComponentsInChildren<Rigidbody>(true)) Destroy(modelBody);
            visual.localRotation = modelNoseIsBackward ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            Bounds bounds = RendererBounds(visual);
            float width = Mathf.Max(bounds.size.x, bounds.size.z);
            if (width > 0.01f) visual.localScale *= modelWidth / width;
            bounds = RendererBounds(visual);
            visual.position -= bounds.center - root.transform.position;
            PropellerSpinner.Attach(visual.gameObject);
        }
        else
        {
            visual = BuildPrimitiveDrone(root.transform);
        }

        // 紅色警示燈，與玩家的無人機區分
        Light beacon = new GameObject("Beacon").AddComponent<Light>();
        beacon.transform.SetParent(root.transform, false);
        beacon.transform.localPosition = Vector3.up * 0.25f;
        beacon.type = LightType.Point;
        beacon.color = new Color(1f, 0.15f, 0.1f);
        beacon.range = 5f;
        beacon.intensity = 2f;
        beacon.shadows = LightShadows.None;

        Bounds visualBounds = RendererBounds(visual);
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = root.transform.InverseTransformPoint(visualBounds.center);
        box.size = new Vector3(Mathf.Max(0.6f, visualBounds.size.x), Mathf.Max(0.3f, visualBounds.size.y), Mathf.Max(0.6f, visualBounds.size.z));

        bodyRigidbody = root.AddComponent<Rigidbody>();
        bodyRigidbody.isKinematic = true;
        bodyRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        return root;
    }

    private Transform BuildPrimitiveDrone(Transform parent)
    {
        Transform visual = new GameObject("Visual").transform;
        visual.SetParent(parent, false);
        Material dark = RuntimeMaterials.Opaque(new Color(0.12f, 0.12f, 0.14f));
        Material red = RuntimeMaterials.Opaque(new Color(0.75f, 0.12f, 0.1f));
        AddPrimitive(PrimitiveType.Cube, visual, Vector3.zero, Quaternion.identity, new Vector3(0.3f, 0.12f, 0.4f), dark);
        float arm = modelWidth * 0.5f;
        foreach (float yaw in new[] { 45f, 135f })
        {
            AddPrimitive(PrimitiveType.Cube, visual, Vector3.zero, Quaternion.Euler(0f, yaw, 0f), new Vector3(0.06f, 0.04f, modelWidth * 1.35f), dark);
        }
        foreach (Vector2 corner in new[] { new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f) })
        {
            Vector3 hub = new Vector3(corner.x, 0f, corner.y) * (arm * 0.7f) + Vector3.up * 0.06f;
            AddPrimitive(PrimitiveType.Cylinder, visual, hub, Quaternion.identity, new Vector3(0.36f, 0.01f, 0.36f), red);
        }
        return visual;
    }

    private static void AddPrimitive(PrimitiveType type, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localRotation = rotation;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static Bounds RendererBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(root.position, Vector3.one * 0.5f);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = StrikeTarget.DroneColor;
        int count = transform.childCount;
        for (int i = 0; i < count; i++)
        {
            Vector3 point = transform.GetChild(i).position;
            Gizmos.DrawWireSphere(point, 0.6f);
            if (i + 1 < count) Gizmos.DrawLine(point, transform.GetChild(i + 1).position);
            else if (loop && count > 2) Gizmos.DrawLine(point, transform.GetChild(0).position);
        }
    }
}

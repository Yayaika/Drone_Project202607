using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 城市路人：沿街區人行道散步，在路口等行人綠燈後走斑馬線過馬路 (登記佔用讓轉彎車禮讓)，
/// 偶爾停下講電話/滑手機；少數人站在店門口。使用 CityPeople 模型與其行走/待機動畫。
/// </summary>
public class CityPedestrians : MonoBehaviour
{
    public static CityPedestrians Instance { get; private set; }

    private enum State { Walking, ToCurb, Waiting, Crossing, Idle, Standing, Injured, Fleeing }

    private class Walker
    {
        public Transform Transform;
        public Animator Animator;
        public string WalkState;
        public string[] IdleStates;
        public State State;
        public float Speed;
        public float Inset;
        public int Bx, Bz;
        public int Corner;      // 0:(-,-) 1:(+,-) 2:(+,+) 3:(-,+)
        public int Direction;   // +1 / -1 繞行方向
        public Vector3 Target;
        public float Timer;
        // 過馬路
        public Vector2Int CrossNode;
        public Vector2Int CrossToward;
        public bool CrossRoadAlongX;
        public Vector3 CrossEnd;
        public int NextBx, NextBz, NextCorner;
        public string CurrentAnimation;
        // 驚慌逃跑
        public Vector3 FleeDirection;
    }

    private static readonly Vector2Int[] CornerSigns =
    {
        new Vector2Int(-1, -1), new Vector2Int(1, -1), new Vector2Int(1, 1), new Vector2Int(-1, 1)
    };

    private readonly List<Walker> walkers = new List<Walker>();
    private readonly Dictionary<long, int> occupiedCrosswalks = new Dictionary<long, int>();
    /// <summary>超過 90m 的路人不繪製、不播動畫 (仍會走動)</summary>
    private readonly DistanceCuller culler = new DistanceCuller(90f);
    private CityGrid grid;
    private System.Random random;
    private float groundY;

    public void Initialize(CityGrid cityGrid, GameObject[] prefabs, int perBlock, int seed)
    {
        Instance = this;
        grid = cityGrid;
        random = new System.Random(seed);
        groundY = grid.Origin.y + grid.SidewalkHeight;

        List<GameObject> valid = new List<GameObject>();
        foreach (GameObject prefab in prefabs)
        {
            if (prefab != null) valid.Add(prefab);
        }
        if (valid.Count == 0) return;

        Transform pedestrianRoot = new GameObject("Pedestrians").transform;
        pedestrianRoot.SetParent(transform, false);

        int total = grid.Blocks * grid.Blocks * Mathf.Max(0, perBlock);
        for (int i = 0; i < total; i++)
        {
            Walker walker = CreateWalker(valid[random.Next(valid.Count)], pedestrianRoot);
            if (walker == null) continue;
            walkers.Add(walker);
            culler.Add(walker.Transform, walker.Animator);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>斑馬線 (路口 node 往 toward 那段道路上) 是否有行人正在穿越</summary>
    public bool IsCrosswalkOccupied(Vector2Int node, Vector2Int toward)
    {
        return occupiedCrosswalks.TryGetValue(CrosswalkKey(node, toward), out int count) && count > 0;
    }

    private static long CrosswalkKey(Vector2Int node, Vector2Int toward) =>
        ((long)(node.x * 32 + node.y) << 16) | (long)(toward.x * 32 + toward.y + 4096);

    #region 生成

    private Walker CreateWalker(GameObject prefab, Transform parent)
    {
        GameObject instance = Instantiate(prefab, parent);
        instance.name = prefab.name;
        // 停用素材包的隨機動畫腳本 (會播放跳舞等動作)，改由這裡控制
        foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>())
        {
            behaviour.enabled = false;
        }
        foreach (Collider existing in instance.GetComponentsInChildren<Collider>())
        {
            Destroy(existing);
        }

        Animator animator = instance.GetComponentInChildren<Animator>();
        string walk = null;
        List<string> idles = new List<string>();
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            {
                string lower = clip.name.ToLowerInvariant();
                if (lower.Contains("basicwalk")) walk = clip.name;
                else if (walk == null && lower.Contains("walk")) walk = clip.name;
                else if (lower.StartsWith("idle")) idles.Add(clip.name);
            }
        }

        CapsuleCollider capsule = instance.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.85f, 0f);
        capsule.radius = 0.28f;
        capsule.height = 1.7f;
        Rigidbody body = instance.AddComponent<Rigidbody>();
        body.isKinematic = true;

        Walker walker = new Walker
        {
            Transform = instance.transform,
            Animator = animator,
            WalkState = walk,
            IdleStates = idles.ToArray(),
            Speed = 1.1f + (float)random.NextDouble() * 0.45f,
            Inset = 1.0f + (float)random.NextDouble() * 0.9f,
            Bx = random.Next(grid.Blocks),
            Bz = random.Next(grid.Blocks),
            Corner = random.Next(4),
            Direction = random.NextDouble() < 0.5 ? 1 : -1
        };

        // 從目前角落到下一個角落之間的隨機位置出發
        Vector3 from = CornerPoint(walker.Bx, walker.Bz, walker.Corner, walker.Inset);
        int next = NextCorner(walker);
        Vector3 to = CornerPoint(walker.Bx, walker.Bz, next, walker.Inset);
        Vector3 start = Vector3.Lerp(from, to, (float)random.NextDouble());
        walker.Corner = next;
        walker.Target = to;
        walker.Transform.position = start;
        Face(walker, to - start, 1f);

        if (random.NextDouble() < 0.15)
        {
            // 站在店門口/騎樓邊的人
            walker.State = State.Standing;
            Vector3 roadward = RoadwardDirection(walker, start);
            walker.Transform.position = start - roadward * 0.6f;
            Face(walker, Quaternion.Euler(0f, (float)random.NextDouble() * 120f - 60f, 0f) * roadward, 1f);
            PlayIdle(walker);
        }
        else
        {
            walker.State = State.Walking;
            Play(walker, walker.WalkState, true);
        }
        return walker;
    }

    #endregion

    #region 路徑

    private Vector3 CornerPoint(int bx, int bz, int corner, float inset)
    {
        Vector2 min = grid.BlockMin(bx, bz);
        Vector2Int sign = CornerSigns[corner];
        float x = sign.x < 0 ? min.x + inset : min.x + grid.BlockSize - inset;
        float z = sign.y < 0 ? min.y + inset : min.y + grid.BlockSize - inset;
        return new Vector3(x, groundY, z);
    }

    private static int NextCorner(Walker walker) => (walker.Corner + walker.Direction + 4) % 4;

    /// <summary>路人所在人行道朝向車道的方向 (用於站立者面向街道)</summary>
    private Vector3 RoadwardDirection(Walker walker, Vector3 position)
    {
        Vector2 min = grid.BlockMin(walker.Bx, walker.Bz);
        float dxMin = position.x - min.x;
        float dxMax = min.x + grid.BlockSize - position.x;
        float dzMin = position.z - min.y;
        float dzMax = min.y + grid.BlockSize - position.z;
        float best = Mathf.Min(Mathf.Min(dxMin, dxMax), Mathf.Min(dzMin, dzMax));
        if (best == dxMin) return Vector3.left;
        if (best == dxMax) return Vector3.right;
        if (best == dzMin) return Vector3.back;
        return Vector3.forward;
    }

    /// <summary>抵達角落：繼續繞行、停下滑手機或過馬路</summary>
    private void OnCornerReached(Walker walker)
    {
        double roll = random.NextDouble();
        if (roll < 0.1 && walker.IdleStates.Length > 0)
        {
            walker.State = State.Idle;
            walker.Timer = 2f + (float)random.NextDouble() * 5f;
            PlayIdle(walker);
            return;
        }

        if (roll < 0.45 && TryPlanCrossing(walker))
        {
            return;
        }

        walker.Corner = NextCorner(walker);
        walker.Target = CornerPoint(walker.Bx, walker.Bz, walker.Corner, walker.Inset);
        walker.State = State.Walking;
    }

    private bool TryPlanCrossing(Walker walker)
    {
        Vector2Int sign = CornerSigns[walker.Corner];
        Vector2 min = grid.BlockMin(walker.Bx, walker.Bz);
        float edgeX = sign.x > 0 ? min.x + grid.BlockSize : min.x;
        float edgeZ = sign.y > 0 ? min.y + grid.BlockSize : min.y;
        Vector2Int node = new Vector2Int(sign.x > 0 ? walker.Bx + 1 : walker.Bx, sign.y > 0 ? walker.Bz + 1 : walker.Bz);

        bool canCrossX = walker.Bx + sign.x >= 0 && walker.Bx + sign.x < grid.Blocks;
        bool canCrossZ = walker.Bz + sign.y >= 0 && walker.Bz + sign.y < grid.Blocks;
        if (!canCrossX && !canCrossZ) return false;

        bool crossX = canCrossX && (!canCrossZ || random.NextDouble() < 0.5);
        float crossingWidth = grid.RoadWidth + walker.Inset * 2f;
        float jitter = (float)random.NextDouble() * 1.6f - 0.8f;

        if (crossX)
        {
            // 沿 X 方向走過一條南北向 (沿 Z) 的道路
            float lineZ = edgeZ - sign.y * (2f + jitter);
            walker.Target = new Vector3(edgeX - sign.x * walker.Inset, groundY, lineZ);
            walker.CrossEnd = new Vector3(edgeX + sign.x * (crossingWidth - walker.Inset), groundY, lineZ);
            walker.CrossNode = node;
            walker.CrossToward = new Vector2Int(node.x, node.y - sign.y);
            walker.CrossRoadAlongX = false;
            walker.NextBx = walker.Bx + sign.x;
            walker.NextBz = walker.Bz;
            walker.NextCorner = CornerIndex(new Vector2Int(-sign.x, sign.y));
        }
        else
        {
            float lineX = edgeX - sign.x * (2f + jitter);
            walker.Target = new Vector3(lineX, groundY, edgeZ - sign.y * walker.Inset);
            walker.CrossEnd = new Vector3(lineX, groundY, edgeZ + sign.y * (crossingWidth - walker.Inset));
            walker.CrossNode = node;
            walker.CrossToward = new Vector2Int(node.x - sign.x, node.y);
            walker.CrossRoadAlongX = true;
            walker.NextBx = walker.Bx;
            walker.NextBz = walker.Bz + sign.y;
            walker.NextCorner = CornerIndex(new Vector2Int(sign.x, -sign.y));
        }

        walker.State = State.ToCurb;
        return true;
    }

    private static int CornerIndex(Vector2Int sign)
    {
        for (int i = 0; i < CornerSigns.Length; i++)
        {
            if (CornerSigns[i] == sign) return i;
        }
        return 0;
    }

    #endregion

    #region 更新

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        culler.Update();
        CityTraffic traffic = CityTraffic.Instance;
        foreach (Walker walker in walkers)
        {
            switch (walker.State)
            {
                case State.Walking:
                    if (MoveTowards(walker, walker.Target, dt)) OnCornerReached(walker);
                    break;

                case State.ToCurb:
                    if (MoveTowards(walker, walker.Target, dt))
                    {
                        walker.State = State.Waiting;
                        Face(walker, walker.CrossEnd - walker.Transform.position, 1f);
                        PlayIdle(walker);
                    }
                    break;

                case State.Waiting:
                    float crossSeconds = Vector3.Distance(walker.Transform.position, walker.CrossEnd) / walker.Speed + 1.5f;
                    if (traffic == null || traffic.PedestrianMayCross(walker.CrossNode, walker.CrossRoadAlongX, crossSeconds))
                    {
                        walker.State = State.Crossing;
                        SetCrosswalk(walker, 1);
                        Play(walker, walker.WalkState, false);
                    }
                    break;

                case State.Crossing:
                    if (MoveTowards(walker, walker.CrossEnd, dt))
                    {
                        SetCrosswalk(walker, -1);
                        walker.Bx = walker.NextBx;
                        walker.Bz = walker.NextBz;
                        walker.Corner = walker.NextCorner;
                        walker.Corner = NextCorner(walker);
                        walker.Target = CornerPoint(walker.Bx, walker.Bz, walker.Corner, walker.Inset);
                        walker.State = State.Walking;
                    }
                    break;

                case State.Fleeing:
                    walker.Timer -= dt;
                    Flee(walker, dt);
                    if (walker.Timer <= 0f)
                    {
                        // 跑完回到原本路線 (走向目前街角)
                        walker.Target = CornerPoint(walker.Bx, walker.Bz, walker.Corner, walker.Inset);
                        walker.State = State.Walking;
                    }
                    break;

                case State.Idle:
                    walker.Timer -= dt;
                    if (walker.Timer <= 0f)
                    {
                        Play(walker, walker.WalkState, false);
                        walker.Corner = NextCorner(walker);
                        walker.Target = CornerPoint(walker.Bx, walker.Bz, walker.Corner, walker.Inset);
                        walker.State = State.Walking;
                    }
                    break;
            }
        }
    }

    #region 驚慌逃跑

    private const float FleeSpeed = 4.5f;

    private void OnEnable() => PanicAlarm.OnAlarm += Panic;
    private void OnDisable() => PanicAlarm.OnAlarm -= Panic;

    /// <summary>附近爆炸：範圍內的路人 (正在過馬路的除外，先走完斑馬線) 往遠離爆炸的方向慢跑數秒</summary>
    private void Panic(Vector3 point, float radius)
    {
        foreach (Walker walker in walkers)
        {
            if (walker.Transform == null || walker.State == State.Injured || walker.State == State.Crossing) continue;
            Vector3 away = walker.Transform.position - point;
            away.y = 0f;
            if (away.magnitude > radius) continue;
            if (away.sqrMagnitude < 0.01f) away = -walker.Transform.forward;
            walker.FleeDirection = Quaternion.Euler(0f, (float)random.NextDouble() * 50f - 25f, 0f) * away.normalized;
            walker.Timer = 4f + (float)random.NextDouble() * 3f;
            walker.State = State.Fleeing;
        }
    }

    private void Flee(Walker walker, float dt)
    {
        string jog = walker.WalkState != null ? walker.WalkState.Replace("basicWalk", "jogging") : null;
        if (jog != null && walker.CurrentAnimation != jog) Play(walker, jog, false);
        walker.Transform.position += walker.FleeDirection * (FleeSpeed * dt);
        Face(walker, walker.FleeDirection, 1f - Mathf.Exp(-10f * dt));
    }

    #endregion

    #region 炸彈受傷

    [Header("炸彈受傷")]
    [Tooltip("受傷倒地的路人多久後移除 (秒)")]
    [SerializeField] private float injuredRemoveSeconds = 45f;

    /// <summary>
    /// 炸彈命中：hit 屬於某位路人時讓他倒地 (停止行走與動畫) 並回傳 true。
    /// 火焰/煙霧由 <see cref="BurningDamage"/> 處理。
    /// </summary>
    public bool TryInjure(Transform hit)
    {
        if (hit == null) return false;
        foreach (Walker walker in walkers)
        {
            if (walker.Transform == null || (hit != walker.Transform && !hit.IsChildOf(walker.Transform))) continue;
            if (walker.State == State.Injured) return true;

            // 正在穿越馬路時釋放斑馬線，車輛才不會一直等待
            if (walker.State == State.Crossing) SetCrosswalk(walker, -1);
            walker.State = State.Injured;
            if (walker.Animator != null) walker.Animator.speed = 0f;

            // 向後倒地
            Transform body = walker.Transform;
            Vector3 lyingEuler = body.eulerAngles + new Vector3(-85f, 0f, 0f);
            body.DORotate(lyingEuler, 0.45f).SetEase(Ease.OutBounce);
            body.DOMoveY(body.position.y + 0.15f, 0.45f);
            StartCoroutine(RemoveInjuredLater(walker));
            return true;
        }
        return false;
    }

    private System.Collections.IEnumerator RemoveInjuredLater(Walker walker)
    {
        yield return new WaitForSeconds(injuredRemoveSeconds);
        walkers.Remove(walker);
        if (walker.Transform != null)
        {
            walker.Transform.DOKill();
            Destroy(walker.Transform.gameObject);
        }
    }

    #endregion

    private void SetCrosswalk(Walker walker, int delta)
    {
        long key = CrosswalkKey(walker.CrossNode, walker.CrossToward);
        occupiedCrosswalks.TryGetValue(key, out int count);
        occupiedCrosswalks[key] = Mathf.Max(0, count + delta);
    }

    /// <summary>朝目標移動，抵達時回傳 true</summary>
    private bool MoveTowards(Walker walker, Vector3 target, float dt)
    {
        if (walker.CurrentAnimation != walker.WalkState) Play(walker, walker.WalkState, false);

        Vector3 position = walker.Transform.position;
        Vector3 toTarget = target - position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;
        float step = walker.Speed * dt;
        if (distance <= step)
        {
            walker.Transform.position = new Vector3(target.x, groundY, target.z);
            return true;
        }

        walker.Transform.position = position + toTarget / distance * step;
        Face(walker, toTarget, 1f - Mathf.Exp(-8f * dt));
        return false;
    }

    private static void Face(Walker walker, Vector3 direction, float blend)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(direction.normalized);
        walker.Transform.rotation = Quaternion.Slerp(walker.Transform.rotation, target, blend);
    }

    private void PlayIdle(Walker walker)
    {
        if (walker.IdleStates.Length == 0) return;
        Play(walker, walker.IdleStates[random.Next(walker.IdleStates.Length)], true);
    }

    private void Play(Walker walker, string state, bool randomOffset)
    {
        if (walker.Animator == null || string.IsNullOrEmpty(state) || walker.CurrentAnimation == state) return;
        int hash = Animator.StringToHash(state);
        if (!walker.Animator.HasState(0, hash)) return;

        walker.Animator.speed = state == walker.WalkState ? walker.Speed / 1.3f : 1f;
        walker.Animator.CrossFadeInFixedTime(hash, 0.25f, 0, randomOffset ? (float)random.NextDouble() : 0f);
        walker.CurrentAnimation = state;
    }

    #endregion

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }
}

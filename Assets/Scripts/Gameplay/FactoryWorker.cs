using System.Collections;
using UnityEngine;

/// <summary>
/// 工業區平民工人 (CityPeople 模型，Humanoid)：在定點用工具工作、講電話，或在兩點間走動。
/// 收到 <see cref="PanicAlarm"/> (附近爆炸) 時丟下工具，往遠離爆炸的方向奔跑 (前方有障礙就轉向)，跑完後停下張望，一段時間後回去工作。
/// 被炸彈波及 (致命範圍內且有視線) 或被無人機高速撞上即倒地，觸發 <see cref="OnCasualty"/> (誤傷平民)。
/// 由場景中的 <see cref="WorkerPoint"/> 生成。
/// </summary>
public class FactoryWorker : MonoBehaviour
{
    private enum State { Working, Walking, Fleeing, Shaken, Down }

    /// <summary>平民被誤傷</summary>
    public static event System.Action<FactoryWorker> OnCasualty;

    [SerializeField] private float walkSpeed = 1.4f;
    [SerializeField] private float runSpeed = 5f;
    [Tooltip("致命範圍 = 爆炸半徑 × 此比例")]
    [SerializeField] private float lethalFraction = 0.8f;

    private Animator animator;
    private string workState = "Idle";
    private Transform tool;
    private Vector3 home;
    private Vector3? patrolTo;
    private bool goingOut = true;
    private State state;
    private Vector3 fleeDirection;
    private float timer;
    private string currentAnimation;

    public bool IsDown => state == State.Down;

    /// <summary>生成工人：workState 為動畫狀態 (Hammer / Drill / Wrench / PipeWrench / Phone / Idle)；patrolTo 有值時改為來回走動</summary>
    public static FactoryWorker Spawn(GameObject character, string workState, GameObject toolPrefab, Vector3 position, float yaw, Vector3? patrolTo, Transform parent)
    {
        GameplayAssets assets = GameplayAssets.Instance;
        if (character == null || assets == null || assets.workerAnimator == null) return null;

        GameObject root = Instantiate(character, position, Quaternion.Euler(0f, yaw, 0f), parent);
        Animator animator = root.GetComponentInChildren<Animator>();
        animator.runtimeAnimatorController = assets.workerAnimator;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.height = 1.8f;
        capsule.radius = 0.3f;
        root.AddComponent<Rigidbody>().isKinematic = true;

        FactoryWorker worker = root.AddComponent<FactoryWorker>();
        worker.animator = animator;
        worker.workState = string.IsNullOrEmpty(workState) ? "Idle" : workState;
        worker.home = position;
        worker.patrolTo = patrolTo;
        worker.state = patrolTo.HasValue ? State.Walking : State.Working;
        if (toolPrefab != null) worker.AttachTool(toolPrefab);
        return worker;
    }

    /// <summary>工具掛在 CityPeople 骨架的工具骨骼 (bip Xtra_tool)，沒有時掛在右手</summary>
    private void AttachTool(GameObject toolPrefab)
    {
        Transform socket = null;
        foreach (Transform bone in GetComponentsInChildren<Transform>())
        {
            if (bone.name.Contains("Xtra_tool")) socket = bone;
        }
        if (socket == null) socket = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (socket == null) return;
        tool = Instantiate(toolPrefab, socket).transform;
        tool.localPosition = Vector3.zero;
        tool.localRotation = Quaternion.identity;
    }

    private void OnEnable() => PanicAlarm.OnAlarm += HandleAlarm;
    private void OnDisable() => PanicAlarm.OnAlarm -= HandleAlarm;

    private void Start()
    {
        Play(state == State.Walking ? "Walk" : workState, true);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        switch (state)
        {
            case State.Working:
                Play(workState, false);
                break;

            case State.Walking:
                Vector3 goal = goingOut ? patrolTo.Value : home;
                if (Move(goal - transform.position, walkSpeed, dt) < 0.3f)
                {
                    goingOut = !goingOut;
                }
                Play("Walk", false);
                break;

            case State.Fleeing:
                timer -= dt;
                Move(AvoidObstacles(fleeDirection), runSpeed, dt);
                Play("Run", false);
                if (timer <= 0f)
                {
                    state = State.Shaken;
                    timer = Random.Range(15f, 25f);
                }
                break;

            case State.Shaken:
                Play("LookAround", false);
                timer -= dt;
                if (timer <= 0f)
                {
                    // 回到原本的位置繼續工作
                    if (Move(home - transform.position, walkSpeed, dt) < 0.3f)
                    {
                        state = patrolTo.HasValue ? State.Walking : State.Working;
                        if (tool != null) tool.gameObject.SetActive(true);
                    }
                    else
                    {
                        Play("Walk", false);
                    }
                }
                break;
        }
    }

    /// <summary>往 direction 移動並轉向，回傳剩餘距離</summary>
    private float Move(Vector3 direction, float speed, float dt)
    {
        direction.y = 0f;
        float distance = direction.magnitude;
        if (distance < 0.01f) return 0f;
        Vector3 step = direction / distance * Mathf.Min(distance, speed * dt);
        transform.position += step;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), 1f - Mathf.Exp(-10f * dt));
        return distance;
    }

    /// <summary>前方 1.5m 內有障礙時改往左右較空的一側跑</summary>
    private Vector3 AvoidObstacles(Vector3 direction)
    {
        Vector3 origin = transform.position + Vector3.up * 0.6f;
        if (!Blocked(origin, direction)) return direction;
        for (int angle = 40; angle <= 160; angle += 40)
        {
            foreach (int sign in new[] { 1, -1 })
            {
                Vector3 candidate = Quaternion.Euler(0f, angle * sign, 0f) * direction;
                if (Blocked(origin, candidate)) continue;
                fleeDirection = candidate;
                return candidate;
            }
        }
        return -direction;
    }

    private bool Blocked(Vector3 origin, Vector3 direction)
    {
        foreach (RaycastHit hit in Physics.RaycastAll(origin, direction.normalized, 1.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!hit.transform.IsChildOf(transform)) return true;
        }
        return false;
    }

    private void HandleAlarm(Vector3 point, float radius)
    {
        if (state == State.Down) return;
        Vector3 away = transform.position - point;
        away.y = 0f;
        if (away.magnitude > radius) return;
        if (away.sqrMagnitude < 0.01f) away = -transform.forward;
        fleeDirection = Quaternion.Euler(0f, Random.Range(-25f, 25f), 0f) * away.normalized;
        timer = Random.Range(6f, 9f);
        state = State.Fleeing;
        if (tool != null) tool.gameObject.SetActive(false);
    }

    /// <summary>爆炸：致命範圍內且有視線即倒地，回傳是否倒地</summary>
    public bool TryBlast(Vector3 center, float radius)
    {
        if (state == State.Down) return false;
        Vector3 chest = transform.position + Vector3.up * 1.1f;
        Vector3 to = chest - center;
        if (to.magnitude > radius * lethalFraction) return false;
        foreach (RaycastHit hit in Physics.RaycastAll(center + Vector3.up * 0.3f, to.normalized, to.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform)) continue;
            if (hit.rigidbody != null && (hit.rigidbody.GetComponent<DroppedBomb>() != null || hit.rigidbody.GetComponent<DroneController1>() != null)) continue;
            return false;
        }
        KnockDown(center);
        BurningDamage.Ignite(transform, true);
        return true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (state == State.Down || collision.rigidbody == null || collision.rigidbody.GetComponent<DroneController1>() == null) return;
        if (collision.relativeVelocity.magnitude < 3f) return;
        KnockDown(collision.GetContact(0).point);
    }

    private void KnockDown(Vector3 from)
    {
        state = State.Down;
        if (animator != null) animator.speed = 0f;
        foreach (Collider own in GetComponentsInChildren<Collider>()) own.enabled = false;
        StartCoroutine(Fall(from));
        OnCasualty?.Invoke(this);
    }

    private IEnumerator Fall(Vector3 from)
    {
        Vector3 away = transform.position - from;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = -transform.forward;
        Quaternion start = transform.rotation;
        Quaternion end = Quaternion.AngleAxis(85f, Vector3.Cross(Vector3.up, away.normalized)) * start;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.45f)
        {
            transform.rotation = Quaternion.Slerp(start, end, t * t);
            yield return null;
        }
        transform.rotation = end;
    }

    private void Play(string animation, bool randomOffset)
    {
        if (animator == null || currentAnimation == animation) return;
        int hash = Animator.StringToHash(animation);
        if (!animator.HasState(0, hash)) return;
        animator.CrossFadeInFixedTime(hash, 0.25f, 0, randomOffset ? Random.value : 0f);
        currentAnimation = animation;
    }
}

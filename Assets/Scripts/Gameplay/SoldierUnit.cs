using System.Collections;
using UnityEngine;

/// <summary>
/// 敵方軍人 (LowPolySoldiers_demo) 的行為樹 (由上到下優先)：
/// <code>
/// 陣亡 (Dead)         ← 炸彈致命範圍 / 被無人機撞上
/// 受衝擊 (Stagger)     ← 爆炸衝擊波 (致命範圍外)，踉蹌後回到戰鬥或搜索
/// 戰鬥
///   ├ 發現 (Alert)     ← 看到無人機：舉槍、轉身面向目標
///   ├ 交戰 (Combat)    ← 瞄準 (Aim) → 射擊 (Shoot，單發 / 點放 / 連射)，上半身抬到目標仰角
///   └ 換彈 (Reload)    ← 彈匣打完
/// 搜索 (Search)        ← 失去目標：望向最後看到的方向張望，逾時返回
/// 待命
///   ├ 巡邏 (Patrol)    ← 有巡邏點：在兩點間持槍行走
///   └ 站崗 (Guard)     ← 原地警戒待機
/// </code>
/// 動畫以狀態名稱切換 (Idle / Walk / Run / Aim / Shoot / Reload / Alert / Hit / Death)，由選單「無人機遊戲/16」建立控制器；
/// 素材缺少的動作暫以現有動作代替，補上動畫檔後重新執行選單即可替換 (見 Documentation/ASSET_REQUESTS.md)。
/// 兵種 (槍械外觀與射擊參數) 由 Prefab 設定；模型縮放 0.73 倍為身高 1.8m。
/// </summary>
public class SoldierUnit : MonoBehaviour
{
    public const float ModelScale = 0.73f;
    private static Vector3? cachedMuzzle;

    /// <summary>軍人陣亡 (軍人, 是否為無人機撞擊)</summary>
    public static event System.Action<SoldierUnit, bool> OnKilled;

    private enum State { Guard, Patrol, Alert, Combat, Reload, Search, Stagger, Dead }

    [Header("槍械")]
    [SerializeField] private string weaponName = "步槍";
    [Tooltip("槍口 (留空 = 由內建步槍網格最前端計算)")]
    [SerializeField] private Transform muzzle;
    [SerializeField] private GameObject muzzleFlash;
    [Tooltip("曳光彈拖尾材質 (Bullet FX Pack 的 BulletTrail 著色器)")]
    [SerializeField] private Material tracerMaterial;
    [SerializeField] private Color tracerColor = new Color(1f, 0.82f, 0.4f);
    [SerializeField] private float detectRange = 45f;
    [Tooltip("每次開火連發數 (1 = 單發)")]
    [SerializeField] private int burstCount = 1;
    [SerializeField] private float burstInterval = 0.1f;
    [Tooltip("兩次開火 (連發) 之間的間隔秒數")]
    [SerializeField] private Vector2 fireInterval = new Vector2(0.7f, 1.2f);
    [Tooltip("射擊散布 (度)：越大越難命中")]
    [SerializeField] private float spreadDegrees = 2.5f;
    [SerializeField] private float bulletSpeed = 90f;
    [SerializeField] private int damagePerHit = 8;
    [SerializeField] private int magazineSize = 20;
    [SerializeField] private float reloadSeconds = 2.2f;

    [Header("行為")]
    [SerializeField] private float walkSpeed = 1.6f;
    [SerializeField] private float turnSpeed = 300f;
    [SerializeField] private float maxAimPitch = 65f;
    [Tooltip("發現目標後舉槍轉身的時間 (秒)")]
    [SerializeField] private float alertSeconds = 0.6f;
    [Tooltip("失去目標後搜索的時間 (秒)")]
    [SerializeField] private float searchSeconds = 5f;
    [Tooltip("被無人機撞上的最低相對速度 (m/s)")]
    [SerializeField] private float ramSpeed = 2.5f;

    private Animator animator;
    private Transform spine;
    private Transform rightHand;
    private Vector3 muzzleLocal;
    private Collider[] ownColliders;
    private Vector3 home;
    private Quaternion homeRotation;
    private Vector3 patrolTarget;
    private bool hasPatrol;
    private bool goingOut = true;

    private State state;
    private float stateTimer;
    private float pauseUntil;
    private Transform target;
    private bool canSeeTarget;
    private Vector3 lastSeen;
    private float nextSightCheck;
    private float nextShot;
    private float nextBurstShot;
    private int shotsLeft;
    private int roundsLeft;
    private string currentAnimation;

    // 上半身瞄準：動畫沒有寫入脊椎時 (例如控制器遺失) 不能讓旋轉每幀疊加
    private float aimPitch;
    private Quaternion spineBase;
    private Quaternion spineWritten;
    private bool spineHasWritten;

    public bool IsDead => state == State.Dead;
    public string WeaponName => weaponName;

    /// <summary>由軍人 Prefab 生成 (prefab 為空時用 GameplayAssets 的預設步槍兵)；patrolTo 有值時在出生點與該點之間巡邏</summary>
    public static SoldierUnit Spawn(GameObject prefab, Vector3 position, float yaw, Vector3? patrolTo, Transform parent)
    {
        if (prefab == null && GameplayAssets.Instance != null) prefab = GameplayAssets.Instance.soldierPrefab;
        if (prefab == null) return null;

        GameObject root = Instantiate(prefab, position, Quaternion.Euler(0f, yaw, 0f), parent);
        SoldierUnit unit = root.GetComponent<SoldierUnit>();
        if (unit == null) return null;
        unit.home = position;
        unit.homeRotation = root.transform.rotation;
        if (patrolTo.HasValue)
        {
            unit.hasPatrol = true;
            unit.patrolTarget = patrolTo.Value;
        }
        return unit;
    }

    private void Start()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            // Prefab 的控制器引用遺失時 (例如控制器資產被重建) 改用素材清單中的控制器
            if (animator.runtimeAnimatorController == null && GameplayAssets.Instance != null) animator.runtimeAnimatorController = GameplayAssets.Instance.soldierAnimator;
            animator.applyRootMotion = false;
        }
        foreach (Transform bone in GetComponentsInChildren<Transform>())
        {
            if (bone.name == "Bip001 Spine1") spine = bone;
            else if (bone.name == "Bip001 R Hand") rightHand = bone;
        }
        if (muzzle == null) muzzleLocal = MuzzleOffset();
        ownColliders = GetComponentsInChildren<Collider>();
        roundsLeft = Mathf.Max(1, magazineSize);
        if (home == Vector3.zero)
        {
            home = transform.position;
            homeRotation = transform.rotation;
        }
        state = hasPatrol ? State.Patrol : State.Guard;
        Play("Idle", true);
    }

    #region 行為樹

    private void Update()
    {
        if (state == State.Dead) return;
        float dt = Time.deltaTime;
        stateTimer -= dt;
        Sense();

        switch (state)
        {
            case State.Stagger:
                Play("Hit");
                if (stateTimer <= 0f) Enter(target != null && canSeeTarget ? State.Combat : State.Search);
                break;

            case State.Alert:
                Play("Alert");
                FaceToward(lastSeen, dt);
                if (stateTimer <= 0f) Enter(State.Combat);
                break;

            case State.Combat:
                if (!canSeeTarget)
                {
                    Enter(State.Search);
                    break;
                }
                FaceToward(target.position, dt);
                UpdateFiring();
                break;

            case State.Reload:
                Play("Reload");
                if (target != null && canSeeTarget) FaceToward(target.position, dt);
                if (stateTimer <= 0f)
                {
                    roundsLeft = Mathf.Max(1, magazineSize);
                    Enter(canSeeTarget ? State.Combat : State.Search);
                }
                break;

            case State.Search:
                Play("Alert");
                FaceToward(lastSeen, dt);
                if (canSeeTarget) Enter(State.Combat);
                else if (stateTimer <= 0f) Enter(hasPatrol ? State.Patrol : State.Guard);
                break;

            case State.Patrol:
                if (canSeeTarget) Enter(State.Alert);
                else Patrol(dt);
                break;

            case State.Guard:
                if (canSeeTarget)
                {
                    Enter(State.Alert);
                    break;
                }
                // 回到崗位並面向原本的方向
                if (MoveToward(home, walkSpeed, dt) > 0.3f) Play("Walk");
                else
                {
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, homeRotation, turnSpeed * 0.5f * dt);
                    Play("Idle");
                }
                break;
        }
    }

    private void Enter(State next)
    {
        state = next;
        switch (next)
        {
            case State.Alert: stateTimer = alertSeconds; break;
            case State.Reload: stateTimer = reloadSeconds; shotsLeft = 0; break;
            case State.Search: stateTimer = searchSeconds; shotsLeft = 0; break;
            case State.Stagger: stateTimer = 0.6f; shotsLeft = 0; break;
            case State.Combat: nextShot = Mathf.Max(nextShot, Time.time + 0.2f); break;
        }
    }

    /// <summary>每 0.25 秒檢查一次目標與視線 (倒數期間與結算後沒有目標)</summary>
    private void Sense()
    {
        if (Time.time < nextSightCheck) return;
        nextSightCheck = Time.time + 0.25f;
        target = PlayerDrone();
        canSeeTarget = target != null && CanSee(target);
        if (canSeeTarget) lastSeen = target.position;
    }

    private void UpdateFiring()
    {
        Vector3 flat = target.position - transform.position;
        flat.y = 0f;
        if (shotsLeft > 0)
        {
            if (Time.time >= nextBurstShot) FireShot(target);
            return;
        }
        if (Time.time >= nextShot && Vector3.Angle(transform.forward, flat) < 15f)
        {
            // 開始一次射擊 (單發 / 點放 / 連射)：只在第一發重播射擊動作，連發時動作不會一直重播抖動
            shotsLeft = Mathf.Min(Mathf.Max(1, burstCount), roundsLeft);
            Play("Shoot", false, true);
            FireShot(target);
        }
        else if (currentAnimation != "Shoot" || ShootFinished())
        {
            Play("Aim");
        }
    }

    private bool ShootFinished()
    {
        if (animator == null) return true;
        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        return info.shortNameHash != Animator.StringToHash("Shoot") || info.normalizedTime >= 0.95f;
    }

    private void Patrol(float dt)
    {
        if (Time.time < pauseUntil)
        {
            Play("Idle");
            return;
        }
        Vector3 goal = goingOut ? patrolTarget : home;
        if (MoveToward(goal, walkSpeed, dt) < 0.4f)
        {
            goingOut = !goingOut;
            pauseUntil = Time.time + Random.Range(1.5f, 3.5f);
            return;
        }
        Play("Walk");
    }

    /// <summary>往 goal 移動並轉向，回傳移動前的距離</summary>
    private float MoveToward(Vector3 goal, float speed, float dt)
    {
        Vector3 flat = goal - transform.position;
        flat.y = 0f;
        float distance = flat.magnitude;
        if (distance < 0.05f) return distance;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), turnSpeed * dt);
        transform.position += flat / distance * Mathf.Min(distance, speed * dt);
        return distance;
    }

    private void FaceToward(Vector3 point, float dt)
    {
        Vector3 flat = point - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), turnSpeed * dt);
    }

    #endregion

    #region 動畫與瞄準

    /// <summary>以狀態名稱切換動畫 (控制器沒有該狀態時忽略)；restart 用於重播射擊</summary>
    private void Play(string animation, bool randomOffset = false, bool restart = false)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        if (!restart && currentAnimation == animation) return;
        int hash = Animator.StringToHash(animation);
        if (!animator.HasState(0, hash)) return;
        if (restart) animator.Play(hash, 0, 0f);
        else animator.CrossFadeInFixedTime(hash, 0.2f, 0, randomOffset ? Random.value : 0f);
        currentAnimation = animation;
    }

    private void LateUpdate()
    {
        if (spine == null || state == State.Dead) return;

        // 動畫有寫入脊椎時以動畫姿勢為基準；沒有寫入 (與上一幀寫入的值相同) 時退回上一幀的基準，避免旋轉疊加
        Quaternion current = spine.localRotation;
        spineBase = spineHasWritten && current == spineWritten ? spineBase : current;
        spine.localRotation = spineBase;

        float desired = 0f;
        bool aiming = state == State.Combat || state == State.Alert || state == State.Reload;
        if (aiming && target != null)
        {
            Vector3 to = target.position - spine.position;
            float horizontal = new Vector2(to.x, to.z).magnitude;
            desired = Mathf.Clamp(Mathf.Atan2(to.y, horizontal) * Mathf.Rad2Deg, -10f, maxAimPitch);
        }
        aimPitch = Mathf.Lerp(aimPitch, desired, Time.deltaTime * 8f);
        // 繞軍人右方軸往上抬 (角度為負 = 槍口朝上)
        spine.rotation = Quaternion.AngleAxis(-aimPitch, transform.right) * spine.rotation;
        spineWritten = spine.localRotation;
        spineHasWritten = true;
    }

    /// <summary>
    /// 槍口在右手座標系的位置：槍械網格 (第 2 個子網格) 中離右手最遠的頂點 (槍管前端)。
    /// 槍械完全綁在右手，與姿勢無關，所以同一個模型只需算一次。BakeMesh 不套用縮放，由 TransformPoint 套用。
    /// </summary>
    private Vector3 MuzzleOffset()
    {
        if (cachedMuzzle.HasValue || rightHand == null) return cachedMuzzle ?? Vector3.forward;
        SkinnedMeshRenderer skin = GetComponentInChildren<SkinnedMeshRenderer>();
        if (skin == null || skin.sharedMesh == null || skin.sharedMesh.subMeshCount < 2) return Vector3.forward;

        Mesh baked = new Mesh();
        skin.BakeMesh(baked, false);
        Vector3[] vertices = baked.vertices;
        Vector3 best = rightHand.position;
        float bestDistance = -1f;
        foreach (int index in baked.GetTriangles(1))
        {
            Vector3 world = skin.transform.TransformPoint(vertices[index]);
            float distance = (world - rightHand.position).sqrMagnitude;
            if (distance <= bestDistance) continue;
            bestDistance = distance;
            best = world;
        }
        Destroy(baked);
        cachedMuzzle = rightHand.InverseTransformPoint(best);
        return cachedMuzzle.Value;
    }

    private Vector3 MuzzlePosition => muzzle != null ? muzzle.position
        : rightHand != null ? rightHand.TransformPoint(muzzleLocal)
        : transform.position + Vector3.up * 1.4f + transform.forward * 0.8f;

    #endregion

    #region 射擊

    private static Transform PlayerDrone()
    {
        // 倒數期間與結算後不開火
        if (GameManager.Instance == null || !GameManager.Instance.IsRunning) return null;
        DroneController1 drone = GameManager.Instance.GetDrone();
        if (drone == null || DroneDestruction.IsDestroyed(drone)) return null;
        return drone.transform;
    }

    /// <summary>距離內、且從眼睛高度到無人機沒有被牆面或設備擋住</summary>
    private bool CanSee(Transform drone)
    {
        Vector3 eye = transform.position + Vector3.up * 1.6f;
        Vector3 to = drone.position - eye;
        if (to.magnitude > detectRange) return false;
        foreach (RaycastHit hit in Physics.RaycastAll(eye, to.normalized, to.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(drone)) continue;
            if (hit.collider.GetComponentInParent<SoldierRound>() != null) continue;
            if (SoldierRound.IsBoundary(hit.collider)) continue;
            return false;
        }
        return true;
    }

    private void FireShot(Transform drone)
    {
        shotsLeft--;
        roundsLeft--;
        nextBurstShot = Time.time + burstInterval;
        if (shotsLeft <= 0) nextShot = Time.time + Random.Range(fireInterval.x, fireInterval.y);

        Vector3 origin = MuzzlePosition;
        Rigidbody droneBody = drone.GetComponent<Rigidbody>();
        Vector3 aim = droneBody != null ? droneBody.worldCenterOfMass : drone.position;
        // 依子彈飛行時間預判無人機位置
        if (droneBody != null) aim += droneBody.linearVelocity * (Vector3.Distance(origin, aim) / bulletSpeed);
        Vector3 direction = (aim - origin).normalized;
        Vector3 side = Vector3.Cross(direction, Vector3.up);
        if (side.sqrMagnitude < 1e-4f) side = transform.right;
        direction = Quaternion.AngleAxis(Random.Range(-spreadDegrees, spreadDegrees), Vector3.up)
                    * Quaternion.AngleAxis(Random.Range(-spreadDegrees, spreadDegrees), side.normalized) * direction;

        SoldierRound.Fire(origin, direction, bulletSpeed, damagePerHit, ownColliders, tracerMaterial, tracerColor);
        GameObject flashPrefab = muzzleFlash != null ? muzzleFlash : GameplayAssets.Instance != null ? GameplayAssets.Instance.muzzleFlashEffect : null;
        if (flashPrefab != null)
        {
            GameObject flash = Instantiate(flashPrefab, origin, Quaternion.LookRotation(direction));
            flash.transform.localScale = Vector3.one * ModelScale;
            Destroy(flash, 1f);
        }
        GameAudio.PlayAt(GameSfx.Gunshot, origin, 0.7f);

        if (roundsLeft <= 0) Enter(State.Reload);
    }

    #endregion

    #region 受傷與陣亡

    /// <summary>炸彈爆炸：半徑內且有視線即陣亡；稍遠 (1.6 倍半徑內) 只受衝擊踉蹌。回傳是否陣亡</summary>
    public bool TryBlast(Vector3 center, float radius)
    {
        if (state == State.Dead) return false;
        Vector3 chest = transform.position + Vector3.up * 1.1f;
        Vector3 to = chest - center;
        if (to.magnitude > radius)
        {
            if (to.magnitude <= radius * 1.6f) Enter(State.Stagger);
            return false;
        }
        foreach (RaycastHit hit in Physics.RaycastAll(center + Vector3.up * 0.3f, to.normalized, to.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform)) continue;
            if (hit.rigidbody != null && (hit.rigidbody.GetComponent<DroppedBomb>() != null || hit.rigidbody.GetComponent<DroneController1>() != null)) continue;
            Enter(State.Stagger);
            return false;
        }
        Kill(false, center);
        return true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (state == State.Dead || collision.rigidbody == null || collision.rigidbody.GetComponent<DroneController1>() == null) return;
        if (collision.relativeVelocity.magnitude < ramSpeed) return;
        Kill(true, collision.GetContact(0).point);
    }

    public void Kill(bool rammed, Vector3 from)
    {
        if (state == State.Dead) return;
        state = State.Dead;
        foreach (Collider own in ownColliders ?? GetComponentsInChildren<Collider>()) own.enabled = false;
        GameAudio.PlayAt(GameSfx.Crash, transform.position, 0.5f);
        // 有陣亡動畫時播放；沒有時停止動畫，整個人往遠離衝擊的方向倒下
        if (animator != null && animator.runtimeAnimatorController != null && animator.HasState(0, Animator.StringToHash("Death")))
        {
            Play("Death");
        }
        else
        {
            if (animator != null) animator.enabled = false;
            StartCoroutine(FallOver(from));
        }
        OnKilled?.Invoke(this, rammed);
    }

    private IEnumerator FallOver(Vector3 from)
    {
        Vector3 away = transform.position - from;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = -transform.forward;
        Vector3 axis = Vector3.Cross(Vector3.up, away.normalized);
        Quaternion start = transform.rotation;
        Quaternion end = Quaternion.AngleAxis(85f, axis) * start;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.45f)
        {
            transform.rotation = Quaternion.Slerp(start, end, t * t);
            yield return null;
        }
        transform.rotation = end;
    }

    #endregion
}

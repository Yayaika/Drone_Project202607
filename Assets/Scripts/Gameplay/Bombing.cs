using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 炸彈無人機投下的炸彈 (Assets/Prefabs/Bomb.prefab)：自由落體，撞到地面/物體 (或引信時間到) 時爆炸。
/// 爆炸特效使用 War FX，範圍內的路人受傷倒地、車輛停下燃燒，其他剛體被炸飛。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DroppedBomb : MonoBehaviour
{
    [SerializeField] private float blastRadius = 8f;
    [Tooltip("落地前最長存活時間 (秒)，時間到直接引爆")]
    [SerializeField] private float fuseSeconds = 12f;
    [Tooltip("剛投下時忽略碰撞的時間，避免撞到自己的無人機就爆")]
    [SerializeField] private float armDelay = 0.25f;
    [SerializeField] private GameObject explosionEffect;

    private Rigidbody body;
    private Transform owner;
    private float spawnTime;
    private bool exploded;

    public float BlastRadius => blastRadius;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        spawnTime = Time.time;
    }

    /// <summary>由 <see cref="DroneBombDropper"/> 呼叫：設定初速並忽略與投彈無人機的碰撞</summary>
    public void Launch(Transform dropper, Vector3 velocity)
    {
        owner = dropper;
        spawnTime = Time.time;
        body.linearVelocity = velocity;
        if (dropper == null) return;

        Collider[] own = dropper.GetComponentsInChildren<Collider>();
        foreach (Collider mine in GetComponentsInChildren<Collider>())
        {
            foreach (Collider theirs in own) Physics.IgnoreCollision(mine, theirs);
        }
    }

    private void Update()
    {
        if (!exploded && Time.time - spawnTime > fuseSeconds) Explode(transform.position);
    }

    private void FixedUpdate()
    {
        // 彈體長軸 (網格 Y 軸) 逐漸順著速度方向，看起來像真的炸彈往下栽；保持原本較接近下方的那一端當彈頭
        Vector3 velocity = body.linearVelocity;
        if (velocity.sqrMagnitude > 4f)
        {
            Vector3 axis = transform.up;
            if (Vector3.Dot(axis, velocity) < 0f) axis = -axis;
            Quaternion target = Quaternion.FromToRotation(axis, velocity.normalized) * body.rotation;
            body.MoveRotation(Quaternion.Slerp(body.rotation, target, 4f * Time.fixedDeltaTime));
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (exploded) return;
        if (owner != null && collision.transform.IsChildOf(owner) && Time.time - spawnTime < armDelay) return;

        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        Explode(point);
    }

    private void Explode(Vector3 point)
    {
        exploded = true;
        BombExplosion.Detonate(point, blastRadius, owner, explosionEffect);
        Destroy(gameObject);
    }
}

/// <summary>爆炸流程 (投下的炸彈落地、或掛著炸彈的無人機高速撞擊時共用)：特效、音效、範圍傷害、殘留火焰</summary>
public static class BombExplosion
{
    public const float DefaultRadius = 8f;

    public static void Detonate(Vector3 point, float radius, Transform owner, GameObject effectOverride = null)
    {
        GameplayAssets assets = GameplayAssets.Instance;
        GameObject effectPrefab = effectOverride != null ? effectOverride : (assets != null ? assets.explosionEffect : null);
        if (effectPrefab != null)
        {
            GameObject effect = Object.Instantiate(effectPrefab, point + Vector3.up * 0.2f, Quaternion.identity);
            Object.Destroy(effect, 8f);
        }
        GameAudio.PlayAt(GameSfx.Crash, point, 1f);
        GroundFire.Spawn(point);

        int hits = BombDamage.Apply(point, radius, owner);
        if (hits > 0 && GameManager.Instance != null)
        {
            GameManager.Instance.ShowCenterMessage($"命中 ×{hits}", new Color(1f, 0.55f, 0.15f), 0.8f);
        }
    }
}

/// <summary>爆炸點地面殘留的火焰 (War FX)，持續一段時間後熄滅並移除</summary>
public class GroundFire : MonoBehaviour
{
    private const float BurnSeconds = 18f;
    private readonly List<ParticleSystem> systems = new List<ParticleSystem>();

    public static void Spawn(Vector3 explosionPoint)
    {
        GameplayAssets assets = GameplayAssets.Instance;
        if (assets == null || assets.fireEffect == null) return;

        // 火焰落在爆炸點正下方的地面/屋頂上 (空中引爆時找不到地面就不留火)
        if (!Physics.Raycast(explosionPoint + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 30f, ~0, QueryTriggerInteraction.Ignore)) return;

        GameObject root = new GameObject("GroundFire");
        root.transform.position = ground.point;
        GroundFire fire = root.AddComponent<GroundFire>();
        fire.Begin(assets.fireEffect);
    }

    private void Begin(GameObject prefab)
    {
        GameObject effect = Instantiate(prefab, transform.position, prefab.transform.rotation, transform);
        foreach (CFX_AutoDestructShuriken autoDestruct in effect.GetComponentsInChildren<CFX_AutoDestructShuriken>(true)) Destroy(autoDestruct);
        foreach (ParticleSystem system in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.stopAction = ParticleSystemStopAction.None;
            system.Play(true);
            systems.Add(system);
        }
        StartCoroutine(BurnOut());
    }

    private IEnumerator BurnOut()
    {
        yield return new WaitForSeconds(BurnSeconds);
        foreach (ParticleSystem system in systems)
        {
            if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        yield return new WaitForSeconds(6f);
        Destroy(gameObject);
    }
}

/// <summary>爆炸範圍傷害：路人受傷、車輛 (行駛中/路邊停放) 毀損燃燒、其他剛體與無人機被衝擊波推開</summary>
public static class BombDamage
{
    private static readonly Collider[] overlap = new Collider[128];
    private static readonly HashSet<Transform> handled = new HashSet<Transform>();

    /// <summary>無人機被衝擊波推開的最大速度變化 (m/s)；原本以衝量計算，靠太近會被瞬間彈到高空</summary>
    private const float DroneMaxKnockback = 4.5f;
    /// <summary>其他剛體 (貨物等) 的最大速度變化 (m/s)</summary>
    private const float PropMaxKnockback = 9f;

    /// <summary>回傳受傷/毀損的路人與車輛數量</summary>
    public static int Apply(Vector3 center, float radius, Transform source)
    {
        int count = Physics.OverlapSphereNonAlloc(center, radius, overlap, ~0, QueryTriggerInteraction.Ignore);
        handled.Clear();
        int damaged = 0;

        CityPedestrians pedestrians = CityPedestrians.Instance;
        CityTraffic traffic = CityTraffic.Instance;

        for (int i = 0; i < count; i++)
        {
            Collider hit = overlap[i];
            if (hit == null) continue;
            Rigidbody attached = hit.attachedRigidbody;
            Transform root = attached != null ? attached.transform : hit.transform;
            if (!handled.Add(root)) continue;

            if (pedestrians != null && pedestrians.TryInjure(root))
            {
                BurningDamage.Ignite(root, true);
                damaged++;
                continue;
            }
            if (traffic != null && traffic.TryWreck(root))
            {
                BurningDamage.Ignite(root, false);
                damaged++;
                continue;
            }
            if (IsParkedCar(root))
            {
                BurningDamage.Ignite(root, false);
                damaged++;
                continue;
            }

            if (attached != null && !attached.isKinematic)
            {
                Knockback(attached, center, radius);
            }
        }

        return damaged;
    }

    /// <summary>
    /// 衝擊波：依距離衰減的「速度變化」(與質量無關)，並限制上限；
    /// 無人機以水平推開為主、只稍微往上，再加一點隨機翻滾讓它晃一下，不會被彈到高空。
    /// </summary>
    private static void Knockback(Rigidbody body, Vector3 center, float radius)
    {
        Vector3 offset = body.worldCenterOfMass - center;
        float distance = offset.magnitude;
        float falloff = Mathf.Clamp01(1f - distance / radius);
        if (falloff <= 0f) return;

        Vector3 direction = distance > 0.01f ? offset / distance : Vector3.up;
        bool isDrone = body.GetComponent<DroneController1>() != null;
        if (isDrone)
        {
            Vector3 horizontal = new Vector3(direction.x, 0f, direction.z);
            direction = (horizontal.sqrMagnitude > 0.01f ? horizontal.normalized : Vector3.forward) + Vector3.up * 0.25f;
            body.AddForce(direction.normalized * DroneMaxKnockback * falloff, ForceMode.VelocityChange);
            body.AddTorque(Random.insideUnitSphere * 6f * falloff, ForceMode.VelocityChange);
        }
        else
        {
            body.AddForce((direction + Vector3.up * 0.4f).normalized * PropMaxKnockback * falloff, ForceMode.VelocityChange);
        }
    }

    /// <summary>城市生成器擺放的路邊停車 (CityTraffic.CreateCarModel 建立，名稱以 Car_ 開頭)</summary>
    private static bool IsParkedCar(Transform root)
    {
        return root.name.StartsWith("Car_") && root.GetComponentInChildren<Renderer>() != null;
    }
}

/// <summary>被炸中的目標：外觀燒焦變黑，身上冒火 (War FX) 與黑煙，一段時間後火勢熄滅只剩煙</summary>
public class BurningDamage : MonoBehaviour
{
    private const float FireSeconds = 25f;
    private const float SmokeSeconds = 60f;

    private readonly List<ParticleSystem> fires = new List<ParticleSystem>();
    private readonly List<ParticleSystem> smokes = new List<ParticleSystem>();

    public static void Ignite(Transform target, bool isPerson)
    {
        if (target == null || target.GetComponent<BurningDamage>() != null) return;
        BurningDamage burning = target.gameObject.AddComponent<BurningDamage>();
        burning.Begin(isPerson);
    }

    private void Begin(bool isPerson)
    {
        Bounds bounds = RendererBounds();
        Char(isPerson ? 0.45f : 0.25f);

        GameplayAssets assets = GameplayAssets.Instance;
        if (assets != null)
        {
            GameObject firePrefab = isPerson && assets.smallFireEffect != null ? assets.smallFireEffect : assets.fireEffect;
            Vector3 firePoint = isPerson ? bounds.center : new Vector3(bounds.center.x, bounds.max.y - bounds.size.y * 0.3f, bounds.center.z);
            Spawn(firePrefab, firePoint, fires);
            Spawn(assets.smokeEffect, new Vector3(bounds.center.x, bounds.max.y, bounds.center.z), smokes);
        }

        StartCoroutine(BurnOut());
    }

    private void Spawn(GameObject prefab, Vector3 position, List<ParticleSystem> into)
    {
        if (prefab == null) return;
        GameObject effect = Instantiate(prefab, position, Quaternion.identity, transform);
        // War FX 的火焰/煙霧向上竄，不跟著倒地的人或車體旋轉
        effect.transform.rotation = prefab.transform.rotation;
        foreach (ParticleSystem system in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.stopAction = ParticleSystemStopAction.None;
            system.Play(true);
        }
        // 素材包的自動銷毀腳本會在一次播放後刪除特效，這裡改由 BurnOut 控制
        foreach (CFX_AutoDestructShuriken autoDestruct in effect.GetComponentsInChildren<CFX_AutoDestructShuriken>(true))
        {
            Destroy(autoDestruct);
        }
        into.AddRange(effect.GetComponentsInChildren<ParticleSystem>(true));
    }

    private IEnumerator BurnOut()
    {
        yield return new WaitForSeconds(FireSeconds);
        foreach (ParticleSystem system in fires)
        {
            if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        yield return new WaitForSeconds(SmokeSeconds - FireSeconds);
        foreach (ParticleSystem system in smokes)
        {
            if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    /// <summary>燒焦：材質顏色壓暗 (使用材質實例，不影響其他共用同材質的物件)</summary>
    private void Char(float brightness)
    {
        foreach (Renderer target in GetComponentsInChildren<Renderer>())
        {
            if (target is ParticleSystemRenderer) continue;
            foreach (Material material in target.materials)
            {
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", material.GetColor("_BaseColor") * brightness);
                else if (material.HasProperty("_Color")) material.color *= brightness;
            }
        }
    }

    private Bounds RendererBounds()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(transform.position + Vector3.up, Vector3.one);
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer target in renderers) bounds.Encapsulate(target.bounds);
        return bounds;
    }
}

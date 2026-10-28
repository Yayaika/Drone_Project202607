using System.Reflection;
using UnityEngine;

/// <summary>
/// 軍人射出的子彈：外觀用 Bullet FX Pack 的 Bullet.prefab (Trail 模式；該素材的 Fire / Energy 模式用 Built-in 的 Surface Shader，URP 下不顯示)，
/// 子彈本體換成 URP 發光材質並縮小為約 15cm 的曳光彈。
/// 飛行與命中由這裡以射線逐幀檢查 (不使用素材的物理碰撞，避免子彈把無人機撞開)：
/// 打中玩家無人機時扣耐久 (<see cref="DroneArmor"/>)，打中其他物體時產生 War FX 彈著點。
/// </summary>
public class SoldierRound : MonoBehaviour
{
    private const float Lifetime = 2.5f;
    private const float VisualScale = 0.15f;   // Bullet.prefab 原本 0.5 = 子彈長 0.5m

    private static readonly FieldInfo EffectTypeField = typeof(bullet.fx.pack.Bullet).GetField("BulletEffectType", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo TrailField = typeof(bullet.fx.pack.Bullet).GetField("BulletTrailMaterial", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo FlyingField = typeof(bullet.fx.pack.Bullet).GetField("IsFlying", BindingFlags.NonPublic | BindingFlags.Instance);

    private Vector3 velocity;
    private int damage;
    private Collider[] ignore;
    private float age;
    private bool finished;

    /// <summary>射出一發子彈：trail 為曳光拖尾材質 (null = 素材預設的橘色)，tracer 為彈頭顏色</summary>
    public static void Fire(Vector3 origin, Vector3 direction, float speed, int damage, Collider[] ignore, Material trail, Color tracer)
    {
        GameplayAssets assets = GameplayAssets.Instance;
        GameObject round;
        if (assets != null && assets.bulletPrefab != null)
        {
            // 素材的 Gun.Shoot 也是這個朝向：子彈網格沿 +Y
            round = Instantiate(assets.bulletPrefab, origin, Quaternion.LookRotation(direction) * Quaternion.Euler(90f, 0f, 0f));
            round.transform.localScale = Vector3.one * VisualScale;
            bullet.fx.pack.Bullet visual = round.GetComponent<bullet.fx.pack.Bullet>();
            if (visual != null && EffectTypeField != null) EffectTypeField.SetValue(visual, System.Enum.ToObject(EffectTypeField.FieldType, 0));
            if (visual != null && trail != null && TrailField != null) TrailField.SetValue(visual, trail);
            Renderer body = round.GetComponent<Renderer>();
            if (body != null) body.sharedMaterial = RuntimeMaterials.Unlit(tracer);
            Rigidbody rigidbody = round.GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.isKinematic = true;
                rigidbody.detectCollisions = false;
            }
        }
        else
        {
            round = new GameObject("Round");
            round.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
        }

        SoldierRound flight = round.AddComponent<SoldierRound>();
        flight.velocity = direction * speed;
        flight.damage = damage;
        flight.ignore = ignore;
    }

    private void Update()
    {
        if (finished) return;
        float step = velocity.magnitude * Time.deltaTime;
        if (TryHit(transform.position, velocity.normalized, step, out RaycastHit hit))
        {
            transform.position = hit.point;
            OnHit(hit);
            return;
        }
        transform.position += velocity * Time.deltaTime;
        age += Time.deltaTime;
        if (age >= Lifetime) Finish();
    }

    private bool TryHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit closest)
    {
        closest = default;
        bool found = false;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, direction, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (found && hit.distance >= closest.distance) continue;
            if (hit.transform.IsChildOf(transform) || IsIgnored(hit.collider) || IsBoundary(hit.collider)) continue;
            if (hit.collider.GetComponentInParent<SoldierRound>() != null) continue;
            closest = hit;
            found = true;
        }
        return found;
    }

    private bool IsIgnored(Collider collider)
    {
        if (ignore == null) return false;
        foreach (Collider own in ignore)
        {
            if (own == collider) return true;
        }
        return false;
    }

    /// <summary>地圖空氣牆 (無外觀) 不算命中</summary>
    public static bool IsBoundary(Collider collider)
    {
        Transform parent = collider.transform.parent;
        return parent != null && parent.name == MapBoundary.RootName;
    }

    private void OnHit(RaycastHit hit)
    {
        DroneController1 drone = hit.collider.GetComponentInParent<DroneController1>();
        if (drone != null)
        {
            DroneArmor.Hit(drone, damage, hit.point);
        }
        else
        {
            GameplayAssets assets = GameplayAssets.Instance;
            if (assets != null && assets.bulletImpactEffect != null)
            {
                GameObject impact = Instantiate(assets.bulletImpactEffect, hit.point, Quaternion.LookRotation(hit.normal));
                impact.transform.localScale = Vector3.one * 0.6f;
                Destroy(impact, 1.5f);
            }
        }
        Finish();
    }

    /// <summary>停止素材產生拖尾 (正在淡出的拖尾段由素材的協程清除)，稍後移除子彈</summary>
    private void Finish()
    {
        finished = true;
        bullet.fx.pack.Bullet visual = GetComponent<bullet.fx.pack.Bullet>();
        if (visual != null && FlyingField != null) FlyingField.SetValue(visual, false);
        Renderer body = GetComponent<Renderer>();
        if (body != null) body.enabled = false;
        Destroy(gameObject, 0.15f);
    }
}

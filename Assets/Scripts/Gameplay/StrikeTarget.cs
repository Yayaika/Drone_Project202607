using DG.Tweening;
using UnityEngine;

/// <summary>
/// 打擊模式的目標物：
/// - 投彈目標 (紅)：放在上方無遮蔽處，炸彈爆炸即可摧毀。
/// - 撞擊目標 (紫)：放在騎樓/頂棚/天橋下，上方遮蔽擋住炸彈，且有裝甲不受一般爆炸影響，只能低飛撞擊。
/// - 車輛目標 (橘)：車流中被標記的汽車，會沿街道移動，可投彈或撞擊。
/// 爆炸需「看得到」目標才有效 (屋頂、牆面會擋住爆炸)。
/// 兩種目標被無人機撞到都會摧毀 (撞擊的無人機隨後由 <see cref="StrikeMissionManager"/> 重新部署)。
/// </summary>
public class StrikeTarget : MonoBehaviour
{
    public enum TargetKind
    {
        Bomb,
        Ram,
        Vehicle,
        /// <summary>敵方無人機 (<see cref="EnemyDronePatrol"/>)：沿航線巡航，玩家靠近時逃離</summary>
        Drone
    }

    [Tooltip("撞擊速度低於此值 (m/s) 只算擦碰，不會摧毀")]
    [SerializeField] private float minRamSpeed = 1.5f;
    [Tooltip("爆炸時無人機離目標這麼近 (m) 視為撞擊 (掛載炸彈在機腹引爆)")]
    [SerializeField] private float ramBlastDistance = 2.5f;

    public TargetKind Kind { get; private set; }
    /// <summary>場景設計者指定的目標名稱 (例如「伺服器機櫃」)；沒有時為空字串</summary>
    public string Label { get; private set; } = string.Empty;
    public bool IsDestroyed { get; private set; }
    /// <summary>撞擊目標旁可飛入的開闊點 (重新部署用)；投彈目標與車輛為目標上方</summary>
    public Vector3 ApproachPoint => Kind switch
    {
        TargetKind.Vehicle => transform.position + Vector3.up * 8f,
        TargetKind.Drone => patrol != null ? patrol.NearestWaypoint(transform.position) : transform.position,
        _ => approachPoint
    };

    /// <summary>目標被摧毀 (目標, 是否為無人機撞擊)</summary>
    public event System.Action<StrikeTarget, bool> OnDestroyed;

    public static Color BombColor => new Color(1f, 0.25f, 0.2f);
    public static Color RamColor => new Color(0.75f, 0.35f, 1f);
    public static Color VehicleColor => new Color(1f, 0.6f, 0.1f);
    public static Color DroneColor => new Color(0.2f, 0.9f, 1f);

    public static Color ColorOf(TargetKind kind) => kind switch
    {
        TargetKind.Bomb => BombColor,
        TargetKind.Ram => RamColor,
        TargetKind.Drone => DroneColor,
        _ => VehicleColor
    };

    private Vector3 approachPoint;
    private Transform lamp;
    private Light beacon;
    private BoxCollider body;
    private EnemyDronePatrol patrol;

    public static StrikeTarget Create(Vector3 groundPosition, TargetKind kind, Vector3 approachPoint, float yaw, string label = null)
    {
        GameObject root = new GameObject(kind == TargetKind.Bomb ? "StrikeTarget_Bomb" : "StrikeTarget_Ram");
        root.transform.SetPositionAndRotation(groundPosition, Quaternion.Euler(0f, yaw, 0f));
        StrikeTarget target = root.AddComponent<StrikeTarget>();
        target.Kind = kind;
        target.approachPoint = approachPoint;
        target.Label = label ?? string.Empty;
        target.Build();
        return target;
    }

    /// <summary>把車流中的一輛車標記為移動目標 (沿用車輛本身的碰撞體與運動學剛體)</summary>
    public static StrikeTarget AttachToVehicle(Transform vehicleRoot)
    {
        BoxCollider vehicleBody = vehicleRoot.GetComponent<BoxCollider>();
        if (vehicleBody == null) return null;

        StrikeTarget target = vehicleRoot.gameObject.AddComponent<StrikeTarget>();
        target.Kind = TargetKind.Vehicle;
        target.body = vehicleBody;
        target.BuildVehicleMarkers();
        return target;
    }

    /// <summary>把敵方無人機標記為目標 (沿用本體的碰撞體與運動學剛體；敵機本身已有紅色警示燈，不另加標記)</summary>
    public static StrikeTarget AttachToDrone(EnemyDronePatrol dronePatrol)
    {
        Transform droneBody = dronePatrol != null ? dronePatrol.Body : null;
        BoxCollider droneCollider = droneBody != null ? droneBody.GetComponent<BoxCollider>() : null;
        if (droneCollider == null) return null;

        StrikeTarget target = droneBody.gameObject.AddComponent<StrikeTarget>();
        target.Kind = TargetKind.Drone;
        target.body = droneCollider;
        target.patrol = dronePatrol;
        return target;
    }

    /// <summary>車頂的閃爍警示燈 (跟著車移動)</summary>
    private void BuildVehicleMarkers()
    {
        float roof = body.center.y + body.size.y * 0.5f;
        AddBeacon(new Vector3(0f, roof, 0f), VehicleColor);
    }

    /// <summary>
    /// 寫實外觀的目標設備 (不使用光柱與地面光圈，找目標靠 HUD 導航)：
    /// 投彈目標 = 底座上的設備機組 (百葉散熱口、排氣管、控制盒)；撞擊目標 = 較小的設備機櫃 (面板、螢幕)。
    /// 頂部有依種類顏色閃爍的警示燈，機身中段一圈同色的識別漆。
    /// </summary>
    private void Build()
    {
        Color color = ColorOf(Kind);
        // 撞擊目標要塞進騎樓下，比投彈目標小
        Vector3 size = Kind == TargetKind.Bomb ? new Vector3(1.8f, 1.3f, 1.8f) : new Vector3(1.1f, 1.1f, 1.1f);

        body = gameObject.AddComponent<BoxCollider>();
        body.center = new Vector3(0f, size.y * 0.5f, 0f);
        body.size = size;

        Material skid = RuntimeMaterials.Opaque(new Color(0.16f, 0.16f, 0.17f));
        Material slat = RuntimeMaterials.Opaque(new Color(0.08f, 0.08f, 0.09f));
        Material paint = RuntimeMaterials.Opaque(new Color(color.r * 0.8f, color.g * 0.8f, color.b * 0.8f));
        Material screen = RuntimeMaterials.Unlit(new Color(0.15f, 0.55f, 0.6f));

        if (Kind == TargetKind.Bomb)
        {
            Material housing = RuntimeMaterials.Opaque(new Color(0.34f, 0.37f, 0.31f));
            AddPart(PrimitiveType.Cube, new Vector3(0f, 0.06f, 0f), new Vector3(1.8f, 0.12f, 1.8f), skid);
            AddPart(PrimitiveType.Cube, new Vector3(0f, 0.6f, 0f), new Vector3(1.6f, 0.96f, 1.4f), housing);
            AddPart(PrimitiveType.Cube, new Vector3(0f, 1.1f, 0f), new Vector3(1.66f, 0.06f, 1.46f), skid);
            AddPart(PrimitiveType.Cube, new Vector3(0f, 0.86f, 0f), new Vector3(1.62f, 0.07f, 1.42f), paint);
            for (int i = 0; i < 4; i++)
            {
                float y = 0.3f + i * 0.11f;
                AddPart(PrimitiveType.Cube, new Vector3(0.81f, y, 0f), new Vector3(0.03f, 0.05f, 1f), slat);
                AddPart(PrimitiveType.Cube, new Vector3(-0.81f, y, 0f), new Vector3(0.03f, 0.05f, 1f), slat);
            }
            AddPart(PrimitiveType.Cylinder, new Vector3(-0.5f, 1.35f, -0.4f), new Vector3(0.2f, 0.25f, 0.2f), skid);
            AddPart(PrimitiveType.Cube, new Vector3(0.3f, 0.6f, 0.72f), new Vector3(0.45f, 0.4f, 0.08f), skid);
            AddPart(PrimitiveType.Cube, new Vector3(0.3f, 0.65f, 0.765f), new Vector3(0.3f, 0.18f, 0.01f), screen);
            AddBeacon(new Vector3(0.55f, 1.13f, 0.45f), color);
        }
        else
        {
            Material housing = RuntimeMaterials.Opaque(new Color(0.5f, 0.52f, 0.55f));
            AddPart(PrimitiveType.Cube, new Vector3(0f, 0.04f, 0f), new Vector3(1f, 0.08f, 0.9f), skid);
            AddPart(PrimitiveType.Cube, new Vector3(0f, 0.55f, 0f), new Vector3(0.95f, 0.94f, 0.85f), housing);
            AddPart(PrimitiveType.Cube, new Vector3(0f, 0.75f, 0f), new Vector3(0.97f, 0.06f, 0.87f), paint);
            AddPart(PrimitiveType.Cube, new Vector3(0f, 0.5f, 0.43f), new Vector3(0.02f, 0.8f, 0.01f), slat);   // 雙開門縫
            AddPart(PrimitiveType.Cube, new Vector3(-0.22f, 0.58f, 0.435f), new Vector3(0.3f, 0.16f, 0.01f), screen);
            for (int i = 0; i < 3; i++)
            {
                AddPart(PrimitiveType.Cube, new Vector3(0.22f, 0.3f + i * 0.08f, 0.433f), new Vector3(0.3f, 0.03f, 0.01f), slat);
            }
            AddBeacon(new Vector3(0.3f, 1.02f, 0.25f), color);
        }
    }

    /// <summary>警示燈：燈座 + 發光燈罩 + 閃爍的點光源</summary>
    private void AddBeacon(Vector3 localBase, Color color)
    {
        AddPart(PrimitiveType.Cylinder, localBase + Vector3.up * 0.03f, new Vector3(0.16f, 0.03f, 0.16f), RuntimeMaterials.Opaque(new Color(0.12f, 0.12f, 0.13f)));
        lamp = AddPart(PrimitiveType.Sphere, localBase + Vector3.up * 0.1f, new Vector3(0.13f, 0.13f, 0.13f), RuntimeMaterials.Unlit(color));
        beacon = new GameObject("Beacon").AddComponent<Light>();
        beacon.transform.SetParent(transform, false);
        beacon.transform.localPosition = localBase + Vector3.up * 0.25f;
        beacon.type = LightType.Point;
        beacon.color = color;
        beacon.range = 4f;
        beacon.shadows = LightShadows.None;
        beacon.intensity = 0f;
        beacon.DOIntensity(3f, 0.45f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutQuad);
    }

    private Transform AddPart(PrimitiveType type, Vector3 localPosition, Vector3 scale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(transform, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = scale;
        Renderer partRenderer = part.GetComponent<Renderer>();
        partRenderer.sharedMaterial = material;
        partRenderer.shadowCastingMode = material.name.Contains("Glow") ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
        return part.transform;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (IsDestroyed || collision.rigidbody == null) return;
        if (collision.rigidbody.GetComponent<DroneController1>() == null) return;
        if (collision.relativeVelocity.magnitude < minRamSpeed) return;
        DestroyTarget(true);
    }

    /// <summary>
    /// 爆炸波及：從爆炸點 (略往上抬，避免起點就在屋頂表面) 到目標中心或頂部有一條沒被擋住的視線才算命中。
    /// 炸彈落在騎樓屋頂上時，屋頂本身會擋住視線，底下的撞擊目標不受影響。
    /// </summary>
    public bool TryBlastHit(Vector3 explosionPoint)
    {
        if (IsDestroyed) return false;

        Vector3 origin = explosionPoint + Vector3.up * 0.3f;
        Bounds bounds = body.bounds;
        if (!HasLineOfSight(origin, bounds.center) && !HasLineOfSight(origin, new Vector3(bounds.center.x, bounds.max.y - 0.05f, bounds.center.z)))
        {
            return false;
        }

        // 掛載炸彈貼著目標引爆 = 無人機撞上去
        DroneController1 drone = GameManager.Instance != null ? GameManager.Instance.GetDrone() : null;
        bool rammed = drone != null && Vector3.Distance(body.ClosestPoint(drone.transform.position), drone.transform.position) <= ramBlastDistance;
        // 撞擊目標有裝甲：炸彈落在騎樓前的街上 (側面看得到) 也炸不壞，只能撞擊
        if (Kind == TargetKind.Ram && !rammed) return false;
        DestroyTarget(rammed);
        return true;
    }

    private bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        foreach (RaycastHit hit in Physics.RaycastAll(from, direction.normalized, direction.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == body) continue;
            if (hit.rigidbody != null && (hit.rigidbody.GetComponent<DroneController1>() != null || hit.rigidbody.GetComponent<DroppedBomb>() != null)) continue;
            return false;
        }
        return true;
    }

    private void DestroyTarget(bool rammed)
    {
        if (IsDestroyed) return;
        IsDestroyed = true;
        body.enabled = false;

        GameplayAssets assets = GameplayAssets.Instance;
        if (assets != null && assets.explosionEffect != null)
        {
            GameObject effect = Instantiate(assets.explosionEffect, body.bounds.center, Quaternion.identity);
            Destroy(effect, 8f);
        }
        GameAudio.PlayAt(GameSfx.Crash, transform.position, 1f);

        // 先關掉警示燈，燃燒效果才會以目標本體的大小定位火焰
        if (beacon != null)
        {
            beacon.DOKill();
            beacon.enabled = false;
        }
        if (lamp != null) lamp.gameObject.SetActive(false);
        // 敵方無人機先墜落到地面才起火
        if (Kind == TargetKind.Drone && patrol != null) patrol.Wreck();
        else BurningDamage.Ignite(transform, false);
        // 車輛目標：讓車流系統把車停下 (後方車輛排隊)，之後由車流系統清除
        if (Kind == TargetKind.Vehicle)
        {
            if (CityTraffic.Instance != null) CityTraffic.Instance.TryWreck(transform);
            StrikeVehicleRoute route = GetComponentInParent<StrikeVehicleRoute>();
            if (route != null) route.Wreck(transform);
        }

        OnDestroyed?.Invoke(this, rammed);
    }

    private void OnDestroy()
    {
        if (beacon != null) beacon.DOKill();
    }
}

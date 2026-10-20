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
        Vehicle
    }

    [Tooltip("撞擊速度低於此值 (m/s) 只算擦碰，不會摧毀")]
    [SerializeField] private float minRamSpeed = 1.5f;
    [Tooltip("爆炸時無人機離目標這麼近 (m) 視為撞擊 (掛載炸彈在機腹引爆)")]
    [SerializeField] private float ramBlastDistance = 2.5f;

    public TargetKind Kind { get; private set; }
    public bool IsDestroyed { get; private set; }
    /// <summary>撞擊目標旁可飛入的開闊點 (重新部署用)；投彈目標與車輛為目標上方</summary>
    public Vector3 ApproachPoint => Kind == TargetKind.Vehicle ? transform.position + Vector3.up * 8f : approachPoint;

    /// <summary>目標被摧毀 (目標, 是否為無人機撞擊)</summary>
    public event System.Action<StrikeTarget, bool> OnDestroyed;

    public static Color BombColor => new Color(1f, 0.25f, 0.2f);
    public static Color RamColor => new Color(0.75f, 0.35f, 1f);
    public static Color VehicleColor => new Color(1f, 0.6f, 0.1f);

    public static Color ColorOf(TargetKind kind) => kind switch
    {
        TargetKind.Bomb => BombColor,
        TargetKind.Ram => RamColor,
        _ => VehicleColor
    };

    private Vector3 approachPoint;
    private Transform beam;
    private Transform marker;
    private BoxCollider body;

    public static StrikeTarget Create(Vector3 groundPosition, TargetKind kind, Vector3 approachPoint, float yaw)
    {
        GameObject root = new GameObject(kind == TargetKind.Bomb ? "StrikeTarget_Bomb" : "StrikeTarget_Ram");
        root.transform.SetPositionAndRotation(groundPosition, Quaternion.Euler(0f, yaw, 0f));
        StrikeTarget target = root.AddComponent<StrikeTarget>();
        target.Kind = kind;
        target.approachPoint = approachPoint;
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

    /// <summary>車頂上方旋轉的警示標記 + 光柱 (跟著車移動)</summary>
    private void BuildVehicleMarkers()
    {
        Color color = VehicleColor;
        float roof = body.center.y + body.size.y * 0.5f;
        marker = AddPart(PrimitiveType.Cube, new Vector3(0f, roof + 1.2f, 0f), new Vector3(0.7f, 0.7f, 0.7f), RuntimeMaterials.Glow(new Color(color.r, color.g, color.b, 0.85f)));
        marker.localRotation = Quaternion.Euler(45f, 0f, 45f);
        beam = AddPart(PrimitiveType.Cylinder, new Vector3(0f, roof + 30f, 0f), new Vector3(0.5f, 30f, 0.5f),
            RuntimeMaterials.Glow(new Color(color.r, color.g, color.b, 0.22f)));

        marker.DORotate(new Vector3(0f, 360f, 0f), 2f, RotateMode.WorldAxisAdd).SetLoops(-1, LoopType.Incremental).SetEase(Ease.Linear);
        beam.DOScaleX(0.8f, 0.8f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine);
    }

    private void Build()
    {
        Color color = ColorOf(Kind);
        // 撞擊目標要塞進騎樓下，比投彈目標小
        Vector3 size = Kind == TargetKind.Bomb ? new Vector3(1.8f, 1.3f, 1.8f) : new Vector3(1.1f, 1.1f, 1.1f);

        body = gameObject.AddComponent<BoxCollider>();
        body.center = new Vector3(0f, size.y * 0.5f, 0f);
        body.size = size;

        Material dark = RuntimeMaterials.Opaque(new Color(0.12f, 0.12f, 0.14f));
        Material paint = RuntimeMaterials.Opaque(color);
        AddPart(PrimitiveType.Cube, new Vector3(0f, size.y * 0.35f, 0f), new Vector3(size.x, size.y * 0.7f, size.z), dark);
        AddPart(PrimitiveType.Cube, new Vector3(0f, size.y * 0.85f, 0f), new Vector3(size.x * 0.75f, size.y * 0.3f, size.z * 0.75f), paint);
        // 警示條紋
        AddPart(PrimitiveType.Cube, new Vector3(0f, size.y * 0.45f, 0f), new Vector3(size.x * 1.02f, size.y * 0.12f, size.z * 1.02f), paint);

        // 地面標記 (投彈：準星圈；撞擊：小光圈) 與高空光柱，從遠處也找得到
        float markerSize = Kind == TargetKind.Bomb ? 5f : 2.4f;
        marker = AddPart(PrimitiveType.Cylinder, new Vector3(0f, 0.04f, 0f), new Vector3(markerSize, 0.02f, markerSize),
            RuntimeMaterials.Glow(new Color(color.r, color.g, color.b, 0.4f)));
        beam = AddPart(PrimitiveType.Cylinder, new Vector3(0f, 30f, 0f), new Vector3(0.5f, 30f, 0.5f),
            RuntimeMaterials.Glow(new Color(color.r, color.g, color.b, 0.22f)));

        marker.DOScale(new Vector3(markerSize * 1.15f, 0.02f, markerSize * 1.15f), 0.8f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine);
        beam.DOScaleX(0.8f, 0.8f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine);
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

        // 先關掉光柱與地面標記，燃燒效果才會以目標本體的大小定位火焰
        foreach (Transform visual in new[] { beam, marker })
        {
            if (visual == null) continue;
            visual.DOKill();
            visual.gameObject.SetActive(false);
        }
        BurningDamage.Ignite(transform, false);
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
        if (beam != null) beam.DOKill();
        if (marker != null) marker.DOKill();
    }
}

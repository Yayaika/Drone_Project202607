using UnityEngine;

public class DroneCargo : MonoBehaviour
{
    [SerializeField] private bool canBePickedUp = true;
    [Min(0f)]
    [SerializeField] private float mass = 1f;

    [Header("搬運任務 (Delivery)")]
    [SerializeField] private string cargoName = "貨物";
    [Tooltip("撞擊速度超過此值 (m/s) 才會造成損傷")]
    [SerializeField] private float safeImpactSpeed = 4f;
    [Tooltip("每 1 m/s 超出安全速度造成的損傷")]
    [SerializeField] private float damagePerImpactSpeed = 12f;
    [SerializeField] private bool breakable = true;

    public bool CanBePickedUp => canBePickedUp && !IsBroken;
    public float Mass => mass;
    public string CargoName => cargoName;
    public float Health { get; private set; } = 100f;
    public bool IsBroken => breakable && Health <= 0f;
    public bool IsCarried { get; private set; }

    /// <summary>受損事件 (損傷量, 剩餘耐久)</summary>
    public event System.Action<DroneCargo, float> OnDamaged;
    public event System.Action<DroneCargo> OnBroken;
    public event System.Action<DroneCargo> OnPickedUp;
    public event System.Action<DroneCargo> OnDropped;

    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private float lastImpactTime;

    private void Awake()
    {
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
    }

    public void Configure(string displayName, float cargoMass, bool canBreak = true)
    {
        cargoName = displayName;
        mass = Mathf.Max(0f, cargoMass);
        breakable = canBreak;
    }

    public void SetSpawnPose(Vector3 position, Quaternion rotation)
    {
        spawnPosition = position;
        spawnRotation = rotation;
    }

    /// <summary>重置貨物狀態並放回生成點 (任務失敗重試用)</summary>
    public void ResetCargo()
    {
        Health = 100f;
        IsCarried = false;
        transform.SetParent(null, true);
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);

        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        foreach (Collider cargoCollider in GetComponentsInChildren<Collider>(true))
        {
            cargoCollider.enabled = true;
        }
    }

    public void ApplyImpact(float impactSpeed)
    {
        if (!breakable || IsBroken || impactSpeed <= safeImpactSpeed) return;
        if (Time.time - lastImpactTime < 0.2f) return; // 同一次撞擊的多個接觸點只計算一次
        lastImpactTime = Time.time;

        float damage = (impactSpeed - safeImpactSpeed) * damagePerImpactSpeed;
        Health = Mathf.Max(0f, Health - damage);
        OnDamaged?.Invoke(this, damage);

        if (Health <= 0f)
        {
            GameAudio.PlayAt(GameSfx.CargoBroken, transform.position);
            OnBroken?.Invoke(this);
        }
    }

    internal void NotifyPickedUp()
    {
        IsCarried = true;
        OnPickedUp?.Invoke(this);
    }

    internal void NotifyDropped()
    {
        IsCarried = false;
        OnDropped?.Invoke(this);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (IsCarried) return;

        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed > 1.5f)
        {
            GameAudio.PlayAt(GameSfx.CargoDrop, transform.position, Mathf.Clamp01(impactSpeed / 8f));
        }
        ApplyImpact(impactSpeed);
    }

    private void OnValidate()
    {
        mass = Mathf.Max(0f, mass);
    }
}

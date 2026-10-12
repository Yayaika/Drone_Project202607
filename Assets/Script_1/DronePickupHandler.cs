using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;

public class DronePickupHandler : MonoBehaviour
{
    [Header("Pickup Settings")]
    [SerializeField] private Transform pickupPoint;
    [SerializeField] private float pickupRadius = 1.5f;
    [SerializeField] private LayerMask cargoLayers = ~0;

    [Header("搬運手感")]
    [Tooltip("貨物被吸到掛點的時間 (秒)")]
    [SerializeField] private float attachDuration = 0.25f;
    [Tooltip("放下後與無人機保持不碰撞的時間，避免貨物把無人機彈飛")]
    [SerializeField] private float dropIgnoreCollisionTime = 0.5f;
    [Tooltip("載貨時無人機撞擊會傳遞給貨物的比例")]
    [Range(0f, 1f)]
    [SerializeField] private float crashDamageTransfer = 0.8f;

    private DroneCargo carriedCargo;
    private Rigidbody carriedBody;
    private Collider[] carriedColliders;
    private Collider[] droneColliders;
    private Rigidbody droneBody;
    private DroneController1 droneController;
    private InputAction cargoAction;
    private BoxCollider payloadProxy;
    private float nextScanTime;

    public bool IsCarrying => carriedCargo != null;
    public DroneCargo CarriedCargo => carriedCargo;
    public float CarriedMass => carriedCargo != null ? carriedCargo.Mass : 0f;
    /// <summary>目前在抓取範圍內最近的貨物 (HUD 提示用)，沒有則為 null</summary>
    public DroneCargo CargoInRange { get; private set; }

    public event System.Action<DroneCargo> OnCargoPickedUp;
    public event System.Action<DroneCargo> OnCargoDropped;

    private void Update()
    {
        if (cargoAction != null && cargoAction.WasPressedThisFrame() && (droneController == null || droneController.canControl))
        {
            if (carriedCargo == null)
            {
                TryPickUp();
            }
            else
            {
                Drop();
            }
        }

        if (carriedCargo == null && Time.time >= nextScanTime)
        {
            nextScanTime = Time.time + 0.1f;
            CargoInRange = FindClosestCargo();
        }
        else if (carriedCargo != null)
        {
            CargoInRange = null;
        }
    }

    private void Awake()
    {
        droneController = GetComponent<DroneController1>();
        droneBody = GetComponent<Rigidbody>();
        droneColliders = GetComponentsInChildren<Collider>();

        cargoAction = new InputAction("ToggleCargo");
        cargoAction.AddBinding("<Keyboard>/z");
        cargoAction.AddBinding("<Gamepad>/buttonSouth");
        cargoAction.AddBinding("<XRController>{LeftHand}/grip");
    }

    private void OnEnable()
    {
        cargoAction?.Enable();
        if (droneController != null) droneController.OnCrash += HandleDroneCrash;
    }

    private void OnDisable()
    {
        cargoAction?.Disable();
        if (droneController != null) droneController.OnCrash -= HandleDroneCrash;
    }

    private DroneCargo FindClosestCargo()
    {
        Transform targetPoint = pickupPoint != null ? pickupPoint : transform;
        Collider[] nearbyColliders = Physics.OverlapSphere(targetPoint.position, pickupRadius, cargoLayers, QueryTriggerInteraction.Ignore);

        DroneCargo closestCargo = null;
        float closestDistance = float.PositiveInfinity;

        foreach (Collider nearbyCollider in nearbyColliders)
        {
            DroneCargo candidate = nearbyCollider.GetComponentInParent<DroneCargo>();
            if (candidate == null || !candidate.CanBePickedUp || candidate.IsCarried || candidate.transform.IsChildOf(transform))
            {
                continue;
            }

            float distance = (candidate.transform.position - targetPoint.position).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestCargo = candidate;
                closestDistance = distance;
            }
        }

        return closestCargo;
    }

    private void TryPickUp()
    {
        if (carriedCargo != null)
        {
            return;
        }

        Transform targetPoint = pickupPoint != null ? pickupPoint : transform;
        DroneCargo closestCargo = FindClosestCargo();

        if (closestCargo == null)
        {
            return;
        }

        carriedCargo = closestCargo;
        carriedBody = carriedCargo.GetComponent<Rigidbody>();
        carriedColliders = carriedCargo.GetComponentsInChildren<Collider>();
        droneController?.SetPayloadMass(carriedCargo.Mass);

        if (carriedBody != null)
        {
            carriedBody.mass = carriedCargo.Mass;
            carriedBody.linearVelocity = Vector3.zero;
            carriedBody.angularVelocity = Vector3.zero;
            carriedBody.isKinematic = true;
        }

        // 平滑吸附到掛點 (保留世界座標後再補間到掛點)
        Transform cargoTransform = carriedCargo.transform;
        cargoTransform.DOKill();
        cargoTransform.SetParent(targetPoint, true);
        cargoTransform.DOLocalMove(Vector3.zero, attachDuration).SetEase(Ease.OutBack);
        cargoTransform.DOLocalRotateQuaternion(Quaternion.identity, attachDuration).SetEase(Ease.OutQuad);

        // 貨物外形改由無人機剛體上的代理碰撞體負責，載貨時撞牆會正確擋住而不是穿牆
        CreatePayloadProxy(targetPoint);
        foreach (Collider cargoCollider in carriedColliders)
        {
            cargoCollider.enabled = false;
        }

        carriedCargo.NotifyPickedUp();
        OnCargoPickedUp?.Invoke(carriedCargo);
        GameAudio.Play(GameSfx.CargoPickup);
    }

    private void Drop()
    {
        if (carriedCargo == null)
        {
            return;
        }

        Transform cargoTransform = carriedCargo.transform;
        cargoTransform.DOKill();
        cargoTransform.SetParent(null, true);
        DestroyPayloadProxy();

        foreach (Collider cargoCollider in carriedColliders)
        {
            cargoCollider.enabled = true;
        }
        MoveCargoAboveGround(cargoTransform);
        StartCoroutine(IgnoreDroneCollisionTemporarily(carriedColliders));

        if (carriedBody != null)
        {
            carriedBody.isKinematic = false;
            carriedBody.linearVelocity = droneBody != null
                ? droneBody.linearVelocity
                : Vector3.zero;
        }

        droneController?.ClearPayloadMass();

        DroneCargo dropped = carriedCargo;
        carriedCargo = null;
        carriedBody = null;
        carriedColliders = null;

        dropped.NotifyDropped();
        OnCargoDropped?.Invoke(dropped);
        GameAudio.Play(GameSfx.CargoDrop, 0.7f);
    }

    /// <summary>強制放下 (重生、任務重置時呼叫)</summary>
    public void ForceDrop()
    {
        Drop();
    }

    private IEnumerator IgnoreDroneCollisionTemporarily(Collider[] cargoColliders)
    {
        List<Collider> colliders = new List<Collider>(cargoColliders);
        SetIgnoreCollision(colliders, true);
        yield return new WaitForSeconds(dropIgnoreCollisionTime);
        SetIgnoreCollision(colliders, false);
    }

    private void SetIgnoreCollision(List<Collider> cargoColliders, bool ignore)
    {
        foreach (Collider cargoCollider in cargoColliders)
        {
            if (cargoCollider == null) continue;
            foreach (Collider droneCollider in droneColliders)
            {
                if (droneCollider == null) continue;
                Physics.IgnoreCollision(cargoCollider, droneCollider, ignore);
            }
        }
    }

    private void CreatePayloadProxy(Transform targetPoint)
    {
        DestroyPayloadProxy();
        if (carriedColliders == null || carriedColliders.Length == 0) return;

        // 以貨物最終掛載姿態 (掛點本地座標原點、無旋轉) 計算其本地包圍盒
        Bounds localBounds = new Bounds();
        bool hasBounds = false;
        Transform cargoTransform = carriedCargo.transform;
        Vector3 cargoScale = cargoTransform.localScale;

        foreach (Collider cargoCollider in carriedColliders)
        {
            Bounds colliderBounds;
            if (cargoCollider is BoxCollider box)
            {
                colliderBounds = new Bounds(box.center, box.size);
            }
            else if (cargoCollider is SphereCollider sphere)
            {
                colliderBounds = new Bounds(sphere.center, Vector3.one * sphere.radius * 2f);
            }
            else if (cargoCollider is CapsuleCollider capsule)
            {
                colliderBounds = new Bounds(capsule.center, new Vector3(capsule.radius * 2f, capsule.height, capsule.radius * 2f));
            }
            else
            {
                Bounds world = cargoCollider.bounds;
                colliderBounds = new Bounds(cargoCollider.transform.InverseTransformPoint(world.center), world.size);
            }

            // 轉到貨物根節點空間 (僅考慮位置與縮放)
            Transform colliderTransform = cargoCollider.transform;
            Vector3 relativeScale = colliderTransform == cargoTransform
                ? Vector3.one
                : Vector3.Scale(colliderTransform.lossyScale, Inverse(cargoTransform.lossyScale));
            Vector3 centerInCargo = colliderTransform == cargoTransform
                ? colliderBounds.center
                : cargoTransform.InverseTransformPoint(colliderTransform.TransformPoint(colliderBounds.center));
            Bounds inCargo = new Bounds(centerInCargo, Vector3.Scale(colliderBounds.size, relativeScale));

            if (!hasBounds)
            {
                localBounds = inCargo;
                hasBounds = true;
            }
            else
            {
                localBounds.Encapsulate(inCargo);
            }
        }

        if (!hasBounds) return;

        GameObject proxyObject = new GameObject("PayloadProxyCollider");
        proxyObject.layer = gameObject.layer;
        proxyObject.transform.SetParent(targetPoint, false);
        payloadProxy = proxyObject.AddComponent<BoxCollider>();
        payloadProxy.center = Vector3.Scale(localBounds.center, cargoScale);
        payloadProxy.size = Vector3.Scale(localBounds.size, cargoScale) * 0.95f;

        Collider droneCollider = droneColliders.Length > 0 ? droneColliders[0] : null;
        if (droneCollider != null)
        {
            payloadProxy.sharedMaterial = droneCollider.sharedMaterial;
        }
    }

    private void DestroyPayloadProxy()
    {
        if (payloadProxy != null)
        {
            Destroy(payloadProxy.gameObject);
            payloadProxy = null;
        }
    }

    private void HandleDroneCrash(float impactSpeed)
    {
        if (carriedCargo != null)
        {
            carriedCargo.ApplyImpact(impactSpeed * crashDamageTransfer);
            if (carriedCargo.IsBroken)
            {
                Drop();
            }
        }
    }

    private static Vector3 Inverse(Vector3 scale)
    {
        return new Vector3(
            Mathf.Approximately(scale.x, 0f) ? 0f : 1f / scale.x,
            Mathf.Approximately(scale.y, 0f) ? 0f : 1f / scale.y,
            Mathf.Approximately(scale.z, 0f) ? 0f : 1f / scale.z);
    }

    private void MoveCargoAboveGround(Transform cargoTransform)
    {
        if (carriedColliders == null || carriedColliders.Length == 0)
        {
            return;
        }

        RaycastHit[] hits = Physics.RaycastAll(cargoTransform.position + Vector3.up * 5f, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform) || hit.collider.transform.IsChildOf(cargoTransform))
            {
                continue;
            }

            Bounds bounds = carriedColliders[0].bounds;
            for (int i = 1; i < carriedColliders.Length; i++)
            {
                bounds.Encapsulate(carriedColliders[i].bounds);
            }

            if (bounds.min.y < hit.point.y)
            {
                cargoTransform.position += Vector3.up * (hit.point.y - bounds.min.y + 0.02f);
            }
            return;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Transform targetPoint = pickupPoint != null ? pickupPoint : transform;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(targetPoint.position, pickupRadius);
    }
}

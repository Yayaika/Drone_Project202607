using UnityEngine;
using UnityEngine.InputSystem;

public class DronePickupHandler : MonoBehaviour
{
    [Header("Pickup Settings")]
    [SerializeField] private Transform pickupPoint;
    [SerializeField] private float pickupRadius = 1.5f;
    [SerializeField] private LayerMask cargoLayers = ~0;

    private DroneCargo carriedCargo;
    private Rigidbody carriedBody;
    private Collider[] carriedColliders;
    private Collider[] droneColliders;
    private Rigidbody droneBody;
    private DroneController1 droneController;
    private InputAction cargoAction;

    public bool IsCarrying => carriedCargo != null;

    private void Update()
    {
        if (cargoAction != null && cargoAction.WasPressedThisFrame())
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
    }

    private void OnDisable()
    {
        cargoAction?.Disable();
    }

    private void TryPickUp()
    {
        if (carriedCargo != null)
        {
            return;
        }

        Transform targetPoint = pickupPoint != null ? pickupPoint : transform;
        Collider[] nearbyColliders = Physics.OverlapSphere(targetPoint.position, pickupRadius, cargoLayers, QueryTriggerInteraction.Ignore);

        DroneCargo closestCargo = null;
        float closestDistance = float.PositiveInfinity;

        foreach (Collider nearbyCollider in nearbyColliders)
        {
            DroneCargo candidate = nearbyCollider.GetComponentInParent<DroneCargo>();
            if (candidate == null || !candidate.CanBePickedUp || candidate.transform.IsChildOf(transform))
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
            carriedBody.detectCollisions = true;
            carriedBody.linearVelocity = Vector3.zero;
            carriedBody.angularVelocity = Vector3.zero;
            carriedBody.isKinematic = true;
        }

        foreach (Collider cargoCollider in carriedColliders)
        {
            cargoCollider.enabled = true;
            foreach (Collider droneCollider in droneColliders)
            {
                Physics.IgnoreCollision(cargoCollider, droneCollider, true);
            }
        }

        carriedCargo.transform.SetParent(targetPoint, false);
        carriedCargo.transform.localPosition = Vector3.zero;
        carriedCargo.transform.localRotation = Quaternion.identity;
    }

    private void Drop()
    {
        if (carriedCargo == null)
        {
            return;
        }

        Transform cargoTransform = carriedCargo.transform;
        cargoTransform.SetParent(null, true);
        MoveCargoAboveGround(cargoTransform);

        foreach (Collider cargoCollider in carriedColliders)
        {
            foreach (Collider droneCollider in droneColliders)
            {
                Physics.IgnoreCollision(cargoCollider, droneCollider, false);
            }
        }

        if (carriedBody != null)
        {
            carriedBody.isKinematic = false;
            carriedBody.linearVelocity = droneBody != null
                ? droneBody.linearVelocity
                : Vector3.zero;
        }

        droneController?.ClearPayloadMass();

        carriedCargo = null;
        carriedBody = null;
        carriedColliders = null;
    }

    private void MoveCargoAboveGround(Transform cargoTransform)
    {
        if (carriedColliders == null || carriedColliders.Length == 0)
        {
            return;
        }

        RaycastHit[] hits = Physics.RaycastAll(cargoTransform.position + Vector3.up * 5f, Vector3.down, 20f);
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
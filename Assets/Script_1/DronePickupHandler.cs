using UnityEngine;
using UnityEngine.InputSystem;

public class DronePickupHandler : MonoBehaviour
{
    [Header("Pickup Settings")]
    [SerializeField] private Transform pickupPoint;
    [SerializeField] private float pickupRadius = 1.5f;
    [SerializeField] private LayerMask cargoLayers = ~0;
    [SerializeField] private Key pickupKey = Key.Z;
    [SerializeField] private Key dropKey = Key.X;

    private DroneCargo carriedCargo;
    private Rigidbody carriedBody;
    private Collider[] carriedColliders;
    private Collider[] droneColliders;
    private Rigidbody droneBody;
    private DroneController1 droneController;
    private InputAction pickupAction;
    private InputAction dropAction;

    public bool IsCarrying => carriedCargo != null;

    private void Update()
    {
        bool pickupPressed = pickupAction != null && pickupAction.WasPressedThisFrame();
        bool dropPressed = dropAction != null && dropAction.WasPressedThisFrame();

        if (Keyboard.current != null && Keyboard.current[pickupKey].wasPressedThisFrame)
        {
            pickupPressed = true;
        }

        if (Keyboard.current != null && Keyboard.current[dropKey].wasPressedThisFrame)
        {
            dropPressed = true;
        }

        if (pickupPressed)
        {
            TryPickUp();
        }

        if (dropPressed)
        {
            Drop();
        }
    }

    private void Awake()
    {
        droneController = GetComponent<DroneController1>();
        droneBody = GetComponent<Rigidbody>();
        droneColliders = GetComponentsInChildren<Collider>();

        pickupAction = new InputAction("PickupCargo");
        pickupAction.AddBinding("<Keyboard>/z");
        pickupAction.AddBinding("<XRController>{LeftHand}/grip");

        dropAction = new InputAction("DropCargo");
        dropAction.AddBinding("<Keyboard>/x");
        dropAction.AddBinding("<XRController>{RightHand}/grip");
    }

    private void OnEnable()
    {
        pickupAction?.Enable();
        dropAction?.Enable();
    }

    private void OnDisable()
    {
        pickupAction?.Disable();
        dropAction?.Disable();
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
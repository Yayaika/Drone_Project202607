using UnityEngine;

public class DroneCargo : MonoBehaviour
{
    [SerializeField] private bool canBePickedUp = true;
    [Min(0f)]
    [SerializeField] private float mass = 1f;

    public bool CanBePickedUp => canBePickedUp;
    public float Mass => mass;

    private void OnValidate()
    {
        mass = Mathf.Max(0f, mass);
    }
}
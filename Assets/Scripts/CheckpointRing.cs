using UnityEngine;

public class CheckpointRing : MonoBehaviour
{
    public int checkpointIndex;
    public bool isPassed = false;

    private Renderer[] ringRenderers;

    void Start()
    {
        ringRenderers = GetComponentsInChildren<Renderer>();
    }

    void OnTriggerEnter(Collider other)
    {
        Debug.Log("Trigger hit by: " + other.name + " tag: " + other.tag);
        if (isPassed) return;
        if (!other.CompareTag("Player")) return;

        GameManager gm = FindObjectOfType<GameManager>();
        if (gm != null)
            gm.CheckpointPassed(checkpointIndex);
    }

    public void MarkPassed()
    {
        isPassed = true;
        foreach (var r in ringRenderers)
            r.material.color = Color.green;
    }

    public void MarkActive()
    {
        foreach (var r in ringRenderers)
            r.material.color = Color.yellow;
    }
}
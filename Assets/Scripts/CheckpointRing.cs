using UnityEngine;

public class CheckpointRing : MonoBehaviour
{
    public int checkpointIndex;
    public bool isPassed = false;
    public bool isActive = false;

    private Renderer[] ringRenderers;

    void Awake()
    {
        ringRenderers = GetComponentsInChildren<Renderer>();
    }

    /// <summary>
    /// 【新增】標記此圈為當前目標 (例如顯示為黃色/藍色高亮)
    /// </summary>
    public void MarkActive()
    {
        if (isPassed) return;
        isActive = true;

        SetColor(Color.yellow); // 將下一個目標圈改成亮黃色 (也可改成 Color.cyan)
    }

    /// <summary>
    /// 標記此圈已通過 (顯示為綠色)
    /// </summary>
    public void MarkPassed()
    {
        isPassed = true;
        isActive = false;

        SetColor(Color.green);
    }

    /// <summary>
    /// 設為未激活的普通狀態 (橘色)
    /// </summary>
    public void SetInactive()
    {
        if (isPassed) return;
        isActive = false;

        SetColor(new Color(1f, 0.6f, 0f));
    }

    private void SetColor(Color col)
    {
        if (ringRenderers == null) ringRenderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer r in ringRenderers)
        {
            if (r != null)
            {
                r.material.color = col;
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (isPassed) return;

        bool isDrone = other.CompareTag("Player") ||
                       (other.transform.root != null && other.transform.root.CompareTag("Player")) ||
                       other.GetComponentInParent<DroneController1>() != null ||
                       other.GetComponentInParent<DroneController>() != null;

        if (isDrone)
        {
            MarkPassed();

            GameManager gm = FindFirstObjectByType<GameManager>();
            if (gm != null)
            {
                gm.CheckpointPassed(checkpointIndex);
            }
        }
    }
}
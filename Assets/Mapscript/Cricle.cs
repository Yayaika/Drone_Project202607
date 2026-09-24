using UnityEngine;

public class GateTrigger : MonoBehaviour
{
    private RaceManager manager;

    void Start()
    {
        // 自動找到場景中的管理器
        manager = Object.FindFirstObjectByType<RaceManager>();
    }

    private void OnTriggerEnter(Collider other)
    {
        // 檢查進入的是不是玩家 (無人機)
        if (other.CompareTag("Player") || 
            (other.transform.root != null && other.transform.root.CompareTag("Player")) ||
            other.GetComponentInParent<DroneController1>() != null ||
            other.GetComponentInParent<DroneController>() != null)
        {
            // 執行管理器裡的過關邏輯
            if (manager != null)
            {
                manager.PointReached();
            }
        }
    }
}
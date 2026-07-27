using UnityEngine;
using System.Collections.Generic;
using System.Linq; // 用於 OrderBy 排序

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("【無人機參照】")]
    public Transform droneTransform;

    [Header("【檢查點系統 (Checkpoints)】")]
    public CheckpointRing[] rings;
    public int passedCheckpoints = 0;
    public int totalCheckpoints = 7;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // 延遲 0.2 秒執行，確保 CourseBuilder 已生成好樹木與檢查圈
        Invoke(nameof(InitializeGame), 0.2f);
    }

    private void InitializeGame()
    {
        InitializeCheckpoints();
        FixTreeColliders();
    }

    /// <summary>
    /// 自動抓取並初始化場景中所有的 CheckpointRing
    /// </summary>
    public void InitializeCheckpoints()
    {
        rings = FindObjectsByType<CheckpointRing>(FindObjectsSortMode.None)
                .OrderBy(r => r.checkpointIndex)
                .ToArray();

        totalCheckpoints = rings.Length;
        passedCheckpoints = 0;

        if (rings != null && rings.Length > 0)
        {
            rings[0].MarkActive();
            Debug.Log($"[GameManager] 檢查點系統已初始化！共 {totalCheckpoints} 個。");
        }
    }

    /// <summary>
    /// 【核心修復】：自動校正所有樹冠碰撞體，使其嚴格限制在綠色樹木內部
    /// </summary>
    public void FixTreeColliders()
    {
        SphereCollider[] allSphereCols = FindObjectsByType<SphereCollider>(FindObjectsSortMode.None);
        int fixedCount = 0;

        foreach (SphereCollider sphereCol in allSphereCols)
        {
            // 搜尋名稱為 Sphere 且父物件包含 Tree 的樹冠
            if (sphereCol.gameObject.name.Equals("Sphere") &&
                sphereCol.transform.parent != null &&
                sphereCol.transform.parent.name.Contains("Tree"))
            {
                Vector3 scale = sphereCol.transform.localScale;

                float maxScaleAxis = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)); // 15.081
                float minHorizontalAxis = Mathf.Min(scale.x, scale.z);               // 11.5

                if (maxScaleAxis > 0)
                {
                    // 抵銷 Unity 以最大 Y 軸計算碰撞球半徑的特性，強制縮回水平綠色邊界內 (乘 0.95 留 5% 緩衝)
                    sphereCol.radius = (minHorizontalAxis / (maxScaleAxis * 2f)) * 0.95f;
                    sphereCol.center = Vector3.zero;
                    fixedCount++;
                }
            }
        }

        Debug.Log($"<color=cyan>[GameManager] 已自動修復 {fixedCount} 棵樹的碰撞體體積！</color>");
    }

    /// <summary>
    /// 當無人機穿過 CheckpointRing 時呼叫
    /// </summary>
    public void CheckpointPassed(int index)
    {
        if (index == passedCheckpoints)
        {
            passedCheckpoints++;
            Debug.Log($"<color=green>[GameManager] 通過第 {index} 個檢查點！進度: {passedCheckpoints}/{totalCheckpoints}</color>");

            Vector3 newRespawnPos = rings[index].transform.position + Vector3.up * 0.5f;

            // 動態傳送最新重生點給無人機
            if (droneTransform != null)
            {
                droneTransform.SendMessage("SetRespawnPoint", newRespawnPos, SendMessageOptions.DontRequireReceiver);
            }
            else
            {
                GameObject droneObj = GameObject.FindWithTag("Player");
                if (droneObj != null)
                {
                    droneObj.SendMessage("SetRespawnPoint", newRespawnPos, SendMessageOptions.DontRequireReceiver);
                }
            }

            if (passedCheckpoints < rings.Length)
            {
                rings[passedCheckpoints].MarkActive();
            }
            else
            {
                Debug.Log("<color=yellow>【通關】恭喜！已穿過所有檢查點！</color>");
            }
        }
    }
}
using UnityEngine;

/// <summary>
/// 打擊模式的手動目標點位 (放在打擊專屬場景中，可在 Editor 移動調整)。
/// 場景中有任何點位時，<see cref="StrikeMissionManager"/> 只使用這些點位，不再自動取樣。
/// 撞擊目標需指定 approach (遮蔽物外、上方開闊的飛入點)，撞擊後無人機會重新部署在那裡。
/// </summary>
public class StrikeTargetPoint : MonoBehaviour
{
    [Tooltip("投彈 (上方無遮蔽) 或撞擊 (遮蔽物下方)；車輛目標由 StrikeVehicleRoute 提供")]
    public StrikeTarget.TargetKind kind = StrikeTarget.TargetKind.Bomb;
    [Tooltip("撞擊目標的飛入點 (留空 = 目標上方 8m)")]
    public Transform approach;

    public Vector3 ApproachPoint => approach != null ? approach.position : transform.position + Vector3.up * 8f;

    private void OnDrawGizmos()
    {
        Gizmos.color = StrikeTarget.ColorOf(kind);
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.6f, new Vector3(1.4f, 1.2f, 1.4f));
        if (approach != null)
        {
            Gizmos.DrawLine(transform.position + Vector3.up * 0.6f, approach.position);
            Gizmos.DrawWireSphere(approach.position, 0.6f);
        }
    }
}

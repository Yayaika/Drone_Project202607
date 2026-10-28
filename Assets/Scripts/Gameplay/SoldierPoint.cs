using UnityEngine;

/// <summary>
/// 敵方軍人的出生點 (放在打擊場景中，可在 Editor 移動調整)：打擊模式開始時在此生成 <see cref="SoldierUnit"/>，
/// 面向本物件的 +Z；指定 patrolTo 時在兩點之間巡邏。其他模式不生成。
/// </summary>
public class SoldierPoint : MonoBehaviour
{
    [Tooltip("軍人 Prefab (兵種與槍械)；留空 = 預設步槍兵")]
    public GameObject soldierPrefab;
    [Tooltip("巡邏的另一端 (留空 = 原地站崗)")]
    public Transform patrolTo;

    private void Start()
    {
        if (LevelCatalog.SelectedMode != GameModeType.Strike) return;
        SoldierUnit.Spawn(soldierPrefab, transform.position, transform.eulerAngles.y, patrolTo != null ? patrolTo.position : (Vector3?)null, transform);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.9f, 0.3f, 0.2f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.9f, new Vector3(0.6f, 1.8f, 0.6f));
        Gizmos.DrawLine(transform.position + Vector3.up * 1.5f, transform.position + Vector3.up * 1.5f + transform.forward);
        if (patrolTo != null) Gizmos.DrawLine(transform.position, patrolTo.position);
    }
}

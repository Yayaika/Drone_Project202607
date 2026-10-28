using UnityEngine;

/// <summary>
/// 工人出生點 (放在場景中，可在 Editor 移動調整)：開始時在此生成 <see cref="FactoryWorker"/>，面向本物件的 +Z。
/// workState 為工作動作 (Hammer / Drill / Wrench / PipeWrench / Phone / Idle)；指定 patrolTo 時改為在兩點間走動。
/// </summary>
public class WorkerPoint : MonoBehaviour
{
    [Tooltip("CityPeople 角色 Prefab")]
    public GameObject character;
    public string workState = "Idle";
    [Tooltip("手上的工具 (CityPeople/Prefabs/tools)，可留空")]
    public GameObject tool;
    [Tooltip("走動的另一端 (留空 = 定點工作)")]
    public Transform patrolTo;

    private void Start()
    {
        FactoryWorker.Spawn(character, workState, tool, transform.position, transform.eulerAngles.y,
            patrolTo != null ? patrolTo.position : (Vector3?)null, transform);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 0.4f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.9f, new Vector3(0.5f, 1.8f, 0.5f));
        if (patrolTo != null) Gizmos.DrawLine(transform.position, patrolTo.position);
    }
}

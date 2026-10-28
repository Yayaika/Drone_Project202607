using System.Collections;
using UnityEngine;

/// <summary>
/// 玩家無人機被爆炸波及摧毀：爆炸特效 + 殘骸 (機體網格的複製品) 翻滾墜落起火，原機隱藏並停止操控，
/// 數秒後由 <see cref="GameManager.RespawnAfterDestroyed"/> 在最後檢查點 (打擊模式為重新部署點) 生成新的無人機。
/// 由 <see cref="BombDamage"/> 在致命範圍內 (爆炸半徑的 lethalFraction 內、且有視線) 呼叫，或機體耐久歸零 (<see cref="DroneArmor"/>) 時呼叫。
/// </summary>
public class DroneDestruction : MonoBehaviour
{
    private const float LethalFraction = 0.75f;
    private const float RespawnDelay = 2f;
    private const float WreckLifetime = 10f;

    private DroneController1 drone;
    private bool destroyed;

    public static bool IsDestroyed(DroneController1 drone)
    {
        DroneDestruction destruction = drone != null ? drone.GetComponent<DroneDestruction>() : null;
        return destruction != null && destruction.destroyed;
    }

    /// <summary>爆炸在致命範圍內且無遮蔽時摧毀無人機，回傳是否摧毀 (未摧毀時由呼叫端施加衝擊波)</summary>
    public static bool TryDestroy(DroneController1 drone, Vector3 center, float radius)
    {
        if (drone == null || IsDestroyed(drone)) return drone != null;
        Rigidbody body = drone.GetComponent<Rigidbody>();
        Vector3 position = body != null ? body.worldCenterOfMass : drone.transform.position;
        Vector3 offset = position - center;
        if (offset.magnitude > radius * LethalFraction) return false;

        // 牆面或屋頂擋在中間時只受衝擊波
        foreach (RaycastHit hit in Physics.RaycastAll(center + Vector3.up * 0.2f, offset.normalized, offset.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(drone.transform)) continue;
            if (hit.rigidbody != null && hit.rigidbody.GetComponent<DroppedBomb>() != null) continue;
            return false;
        }

        ForceDestroy(drone, center);
        return true;
    }

    /// <summary>直接摧毀 (耐久歸零等)，source 為衝擊來源點 (殘骸往反方向飛)</summary>
    public static void ForceDestroy(DroneController1 drone, Vector3 source)
    {
        if (drone == null || IsDestroyed(drone)) return;
        DroneDestruction destruction = drone.GetComponent<DroneDestruction>();
        if (destruction == null) destruction = drone.gameObject.AddComponent<DroneDestruction>();
        destruction.drone = drone;
        destruction.StartCoroutine(destruction.Sequence(source));
    }

    private IEnumerator Sequence(Vector3 center)
    {
        destroyed = true;
        drone.canControl = false;
        Rigidbody body = drone.GetComponent<Rigidbody>();

        GameplayAssets assets = GameplayAssets.Instance;
        if (assets != null && assets.explosionEffect != null)
        {
            GameObject effect = Instantiate(assets.explosionEffect, drone.transform.position, Quaternion.identity);
            Destroy(effect, 8f);
        }
        GameAudio.PlayAt(GameSfx.Crash, drone.transform.position, 1f);

        Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
        SpawnWreck(velocity + (drone.transform.position - center).normalized * 4f + Vector3.up * 2f);

        // 隱藏原機並凍結在原地，等待重新生成
        Renderer[] renderers = drone.GetComponentsInChildren<Renderer>(false);
        bool[] wasEnabled = new bool[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            wasEnabled[i] = renderers[i].enabled;
            renderers[i].enabled = false;
        }
        bool wasKinematic = body != null && body.isKinematic;
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }
        GameManager.Instance?.ShowCenterMessage("無人機被炸毀！", RuntimeUI.WarningColor, 1.4f);

        yield return new WaitForSeconds(RespawnDelay);

        if (body != null) body.isKinematic = wasKinematic;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null) renderers[i].enabled = wasEnabled[i];
        }
        GameManager.Instance?.RespawnAfterDestroyed();
        drone.GetComponent<DroneArmor>()?.Repair();
        GameManager.Instance?.ShowCenterMessage("新的無人機已部署", new Color(0.6f, 0.9f, 1f), 1.2f);
        // 結算中 (任務已結束) 不恢復操控
        if (GameManager.Instance == null || !GameManager.Instance.IsFinished) drone.canControl = true;
        destroyed = false;
    }

    /// <summary>殘骸：複製機體上的網格，帶初速與翻滾墜落，落地後起火，數秒後清除</summary>
    private void SpawnWreck(Vector3 velocity)
    {
        GameObject wreck = new GameObject("DroneWreck");
        wreck.transform.SetPositionAndRotation(drone.transform.position, drone.transform.rotation);
        foreach (MeshFilter filter in drone.GetComponentsInChildren<MeshFilter>(false))
        {
            MeshRenderer source = filter.GetComponent<MeshRenderer>();
            if (filter.sharedMesh == null || source == null || !source.enabled) continue;
            GameObject part = new GameObject(filter.name, typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
            part.transform.localScale = filter.transform.lossyScale;
            part.transform.SetParent(wreck.transform, true);
            part.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            part.GetComponent<MeshRenderer>().sharedMaterials = source.sharedMaterials;
        }

        BoxCollider box = wreck.AddComponent<BoxCollider>();
        box.size = new Vector3(0.5f, 0.2f, 0.5f);
        Rigidbody rigidbody = wreck.AddComponent<Rigidbody>();
        rigidbody.mass = 1.5f;
        rigidbody.linearVelocity = velocity;
        rigidbody.angularVelocity = Random.insideUnitSphere * 8f;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        BurningDamage.Ignite(wreck.transform, false);
        Destroy(wreck, WreckLifetime);
    }
}

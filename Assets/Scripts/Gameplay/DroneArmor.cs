using UnityEngine;

/// <summary>
/// 玩家無人機的機體耐久 (被軍人子彈擊中扣除)：歸零時由 <see cref="DroneDestruction"/> 摧毀並重新生成，重生後回滿。
/// 第一次被擊中時才加到無人機上；中彈有火花與音效，畫面中央提示目前耐久 (每秒最多一次)。
/// </summary>
public class DroneArmor : MonoBehaviour
{
    public const int MaxHealth = 100;

    private int health = MaxHealth;
    private float lastMessage = -10f;

    public int Health => health;

    public static void Hit(DroneController1 drone, int damage, Vector3 point)
    {
        if (drone == null || DroneDestruction.IsDestroyed(drone)) return;
        DroneArmor armor = drone.GetComponent<DroneArmor>();
        if (armor == null) armor = drone.gameObject.AddComponent<DroneArmor>();

        GameplayAssets assets = GameplayAssets.Instance;
        if (assets != null && assets.bulletImpactEffect != null)
        {
            GameObject spark = Instantiate(assets.bulletImpactEffect, point, Quaternion.LookRotation(point - drone.transform.position));
            spark.transform.localScale = Vector3.one * 0.5f;
            Destroy(spark, 1.5f);
        }
        GameAudio.PlayAt(GameSfx.BulletHit, point, 0.9f);

        armor.health = Mathf.Max(0, armor.health - damage);
        if (armor.health == 0)
        {
            DroneDestruction.ForceDestroy(drone, point);
            return;
        }
        if (Time.time - armor.lastMessage > 1f)
        {
            armor.lastMessage = Time.time;
            GameManager.Instance?.ShowCenterMessage($"遭到槍擊！機體耐久 {armor.health}%", RuntimeUI.WarningColor, 0.7f);
        }
    }

    public void Repair()
    {
        health = MaxHealth;
    }
}

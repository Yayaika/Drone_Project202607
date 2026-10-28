using UnityEngine;

/// <summary>
/// 執行時程式需要用到的素材 Prefab (放在 Resources/GameplayAssets.asset)。
/// 檢查點、炸彈、爆炸/火焰特效、空中飛機都由這裡取得，沒有這個資產時各系統退回簡易外觀。
/// </summary>
[CreateAssetMenu(menuName = "無人機遊戲/玩法素材清單", fileName = "GameplayAssets")]
public class GameplayAssets : ScriptableObject
{
    [Header("競速檢查點")]
    [Tooltip("檢查圈模型 (ring target 20，直徑 1m，圈面法線為 Prefab 本身的 +Z)")]
    public GameObject checkpointRing;

    [Header("炸彈與特效 (War FX)")]
    public GameObject bombPrefab;
    public GameObject explosionEffect;
    [Tooltip("被炸中的車輛身上的火焰 (帶黑煙)")]
    public GameObject fireEffect;
    [Tooltip("被炸中的路人身上的小火焰")]
    public GameObject smallFireEffect;
    [Tooltip("被炸中後持續冒出的煙霧")]
    public GameObject smokeEffect;

    [Header("空中障礙")]
    public GameObject airplanePrefab;
    [Tooltip("空中交通改用的無人機模型 (指定後取代小飛機)")]
    public GameObject trafficDronePrefab;

    [Header("敵方軍人 (LowPolySoldiers_demo / Bullet FX Pack / War FX)")]
    [Tooltip("軍人模型 (槍械為同一個蒙皮網格，跟著動作)")]
    public GameObject soldierPrefab;
    [Tooltip("軍人動畫控制器 (Idle / Run / Shoot，參數 Speed、Shoot)，由選單「無人機遊戲/16」建立")]
    public RuntimeAnimatorController soldierAnimator;
    [Tooltip("子彈 (Bullet FX Pack 的 Bullet.prefab，使用 Trail 模式)")]
    public GameObject bulletPrefab;
    public GameObject muzzleFlashEffect;
    public GameObject bulletImpactEffect;

    [Header("平民工人 (CityPeople)")]
    [Tooltip("工人動畫控制器 (Humanoid：Idle / Walk / Run / LookAround / Phone / Hammer / Drill / Wrench / PipeWrench)，由選單「無人機遊戲/16」建立")]
    public RuntimeAnimatorController workerAnimator;

    private static GameplayAssets cached;
    private static bool loaded;

    public static GameplayAssets Instance
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                cached = Resources.Load<GameplayAssets>("GameplayAssets");
            }
            return cached;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cached = null;
        loaded = false;
    }
}

using UnityEngine;

/// <summary>
/// 晝夜與天氣設定 (放在 Resources/EnvironmentProfile.asset，由選單「無人機遊戲/9」建立)。
/// 沒有這個資產時 <see cref="EnvironmentController"/> 仍可運作，只是夜晚不會切換夜空天空盒。
/// </summary>
[CreateAssetMenu(menuName = "無人機遊戲/晝夜天氣設定", fileName = "EnvironmentProfile")]
public class EnvironmentProfile : ScriptableObject
{
    [Header("晝夜")]
    [Tooltip("夜晚使用的天空盒 (白天沿用場景原本的天空盒)")]
    public Material nightSkybox;
    [Tooltip("遊戲中一整天 (24 小時) 的長度 (分鐘)")]
    [Min(0.5f)] public float dayLengthMinutes = 12f;
    [Tooltip("進入關卡時的時間 (0~24)；小於 0 代表隨機")]
    public float startHour = 9.5f;

    [Header("天氣")]
    [Tooltip("天氣多久變化一次 (秒)")]
    public Vector2 weatherChangeInterval = new Vector2(180f, 360f);
    [Tooltip("天氣轉換所需秒數")]
    public float weatherBlendSeconds = 20f;
    [Range(0f, 1f)] public float clearWeight = 0.4f;
    [Range(0f, 1f)] public float cloudyWeight = 0.25f;
    [Range(0f, 1f)] public float rainWeight = 0.15f;
    [Range(0f, 1f)] public float stormWeight = 0.08f;
    [Range(0f, 1f)] public float fogWeight = 0.12f;
    [Tooltip("天氣風力對無人機的影響倍率 (0 = 不影響飛行)")]
    [Range(0f, 2f)] public float windEffect = 1f;
}

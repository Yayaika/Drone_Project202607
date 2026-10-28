using UnityEngine;

/// <summary>
/// 爆炸警報：炸彈爆炸時由 <see cref="BombExplosion"/> 發出，附近的平民 (城市路人 <see cref="CityPedestrians"/>、工業區工人 <see cref="FactoryWorker"/>)
/// 收到後驚慌逃離爆炸點。軍人不受影響 (會繼續戰鬥)。
/// </summary>
public static class PanicAlarm
{
    /// <summary>爆炸半徑乘上此倍數 = 會驚慌的範圍</summary>
    public const float RadiusMultiplier = 5f;

    /// <summary>(爆炸點, 驚慌範圍)</summary>
    public static event System.Action<Vector3, float> OnAlarm;

    public static void Raise(Vector3 point, float radius)
    {
        OnAlarm?.Invoke(point, radius);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnAlarm = null;
    }
}

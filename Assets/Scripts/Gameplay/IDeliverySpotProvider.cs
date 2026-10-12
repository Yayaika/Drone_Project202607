using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 提供搬運任務可用的取貨/送貨地點 (例如程序化城市的屋頂與街道)。
/// 回傳的座標必須是可站立的表面高度。
/// </summary>
public interface IDeliverySpotProvider
{
    IReadOnlyList<Vector3> RooftopSpots { get; }
    IReadOnlyList<Vector3> StreetSpots { get; }
}

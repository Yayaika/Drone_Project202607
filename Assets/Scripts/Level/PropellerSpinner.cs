using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 讓無人機模型的每片螺旋槳繞自身軸旋轉 (交通無人機、敵方無人機的外觀)。
/// 以名稱尋找葉片：fan.xxx / prop / rotor / blade (不含底下還有子物件的群組節點，避免整組繞中心公轉)。
/// </summary>
public class PropellerSpinner : MonoBehaviour
{
    [SerializeField] private float degreesPerSecond = 2400f;
    [SerializeField] private Vector3 localAxis = Vector3.forward;

    private readonly List<Transform> blades = new List<Transform>();

    public static PropellerSpinner Attach(GameObject root)
    {
        PropellerSpinner spinner = root.AddComponent<PropellerSpinner>();
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.childCount > 0 || child == root.transform) continue;
            string name = child.name.ToLowerInvariant();
            if (name.StartsWith("fan.") || name.Contains("prop") || name.Contains("rotor") || name.Contains("blade")) spinner.blades.Add(child);
        }
        return spinner;
    }

    public int BladeCount => blades.Count;

    private void Update()
    {
        float step = degreesPerSecond * Time.deltaTime;
        for (int i = 0; i < blades.Count; i++)
        {
            // 相鄰葉片反向旋轉 (真實四軸的對角同向)
            if (blades[i] != null) blades[i].Rotate(localAxis, i % 2 == 0 ? step : -step, Space.Self);
        }
    }
}

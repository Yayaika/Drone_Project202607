using UnityEngine;
using TMPro; // 使用 TextMeshPro

public class DroneHUD1 : MonoBehaviour
{
    [Header("Ref Target")]
    [SerializeField] private Rigidbody droneRigidbody; // 無人機 Rigidbody
    [SerializeField] private TextMeshProUGUI hudText;  // UI 文字組件

    [Header("Display Options")]
    [SerializeField] private string speedUnit = "m/s";
    [SerializeField] private bool useKmH = false; // 是否切換為 km/h

    private void Update()
    {
        if (droneRigidbody == null || hudText == null) return;

        // 1. 計算速度 (m/s)
        float speed = droneRigidbody.linearVelocity.magnitude;
        if (useKmH)
        {
            speed *= 3.6f; // 轉為 km/h
            speedUnit = "km/h";
        }

        // 2. 計算高度 (以世界座標 Y 軸為準)
        float altitude = droneRigidbody.transform.position.y;

        // 3. 更新文字顯示 (格式化為小數點後 1 位)
        hudText.text = $"SPD: {speed:F1} {speedUnit}\nALT: {altitude:F1} m";
    }
}
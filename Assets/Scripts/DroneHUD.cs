using UnityEngine;

public class DroneHUD : MonoBehaviour
{
    public DroneController drone;

    private GUIStyle styleWhite;
    private GUIStyle styleGreen;
    private GUIStyle styleRed;
    private GUIStyle styleYellow;
    private GUIStyle styleCyan;

    private Vector3 lastPos;
    private float currentSpeed;

    void Start()
    {
        styleWhite = CreateStyle(Color.white, 22);
        styleGreen = CreateStyle(Color.green, 22);
        styleRed = CreateStyle(Color.red, 22);
        styleYellow = CreateStyle(Color.yellow, 22);
        styleCyan = CreateStyle(Color.cyan, 22);
        lastPos = drone.transform.position;

        // 【修改】確保 drone 不是 null 才讀取位置
        if (drone != null)
            lastPos = drone.transform.position;
    }

    GUIStyle CreateStyle(Color color, int fontSize)
    {
        GUIStyle style = new GUIStyle();
        style.normal.textColor = color;
        style.fontSize = fontSize;
        style.fontStyle = FontStyle.Bold;
        return style;
    }

    void Update()
    {
        // 【新增】如果無人機還沒生成，就不執行計算
        if (drone == null) return;

        currentSpeed = (drone.transform.position - lastPos).magnitude / Time.deltaTime;
        lastPos = drone.transform.position;
    }

    void OnGUI()
    {
        if (drone == null) return;

        float altitude = Mathf.Max(0f, drone.transform.position.y - 1.5f);
        string status = drone.isFlying ? "FLYING" : "GROUNDED";
        GUIStyle statusStyle = drone.isFlying ? styleGreen : styleRed;

        Vector3 pos = drone.transform.position;
        float yaw = drone.transform.eulerAngles.y;

        GUI.Box(new Rect(10, 10, 280, 195), "");
        GUI.Label(new Rect(20, 20, 260, 30), $"Status:    {status}", statusStyle);
        GUI.Label(new Rect(20, 50, 260, 30), $"Altitude:  {altitude:F1} m", styleWhite);
        GUI.Label(new Rect(20, 80, 260, 30), $"Heading:   {yaw:F0}°", styleWhite);
        GUI.Label(new Rect(20, 110, 260, 30), $"Position:  X:{pos.x:F1}  Z:{pos.z:F1}", styleYellow);
        GUI.Label(new Rect(20, 140, 260, 30), $"Speed:     {currentSpeed:F1} m/s", styleCyan);
        GUI.Label(new Rect(20, 170, 260, 30), $"Control:   Hand / WASD", styleWhite);
    }
}
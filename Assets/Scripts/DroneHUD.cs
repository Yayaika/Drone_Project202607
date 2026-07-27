using UnityEngine;

public class DroneHUD : MonoBehaviour
{
    // 保留原有必要組件
    public DroneController drone;
    private DroneController1 _newDrone; // 【新增】支援新飛控的私有變數

    private GUIStyle styleWhite;
    private GUIStyle styleGreen;
    private GUIStyle styleRed;
    private GUIStyle styleYellow;
    private GUIStyle styleCyan;

    private Vector3 lastPos;
    private float currentSpeed;

    // 🌟【新增】提供給 SpawnCtrl 呼叫的設定方法（消除紅線報錯）
    public void SetDrone(DroneController oldCtrl)
    {
        drone = oldCtrl;
        _newDrone = null;
        if (drone != null) lastPos = drone.transform.position;
    }

    public void SetDrone(DroneController1 newCtrl)
    {
        _newDrone = newCtrl;
        drone = null;
        if (_newDrone != null) lastPos = _newDrone.transform.position;
    }

    void Start()
    {
        styleWhite = CreateStyle(Color.white, 22);
        styleGreen = CreateStyle(Color.green, 22);
        styleRed = CreateStyle(Color.red, 22);
        styleYellow = CreateStyle(Color.yellow, 22);
        styleCyan = CreateStyle(Color.cyan, 22);

        // 【新增】若沒有透過 SetDrone 設定，自動在場景中尋找新飛控
        if (drone == null && _newDrone == null)
        {
            _newDrone = FindFirstObjectByType<DroneController1>();
        }

        // 相容新舊飛控的位置初始化
        Transform activeTransform = GetActiveTransform();
        if (activeTransform != null)
        {
            lastPos = activeTransform.position;
        }
    }

    GUIStyle CreateStyle(Color color, int fontSize)
    {
        GUIStyle style = new GUIStyle();
        style.normal.textColor = color;
        style.fontSize = fontSize;
        style.fontStyle = FontStyle.Bold;
        return style;
    }

    // 🌟【新增輔助】取得當前作用中的無人機 Transform (不論新舊)
    private Transform GetActiveTransform()
    {
        if (drone != null) return drone.transform;
        if (_newDrone != null) return _newDrone.transform;
        return null;
    }

    // 取得當前無人機的飛行狀態
    private bool IsDroneFlying()
    {
        if (drone != null) return drone.isFlying;
        // 【修正】新飛控以「引擎已解鎖 且 不在地面上」判定為飛行中，若在地面但已解鎖則視情況可調
        if (_newDrone != null) return _newDrone.isArmed && !_newDrone.isGrounded;
        return false;
    }

    void Update()
    {
        // 【修改】使用通用方法取得 Transform
        Transform activeTransform = GetActiveTransform();
        if (activeTransform == null) return;

        currentSpeed = (activeTransform.position - lastPos).magnitude / Time.deltaTime;
        lastPos = activeTransform.position;
    }

    void OnGUI()
    {
        // 【修改】使用通用方法取得 Transform
        Transform activeTransform = GetActiveTransform();
        if (activeTransform == null) return;

        // 原有計算邏輯與變數（完全保留）
        float altitude = Mathf.Max(0f, activeTransform.position.y - 1.5f);

        bool isFlying = IsDroneFlying();
        string status = isFlying ? "FLYING" : "GROUNDED";
        GUIStyle statusStyle = isFlying ? styleGreen : styleRed;

        Vector3 pos = activeTransform.position;
        float yaw = activeTransform.eulerAngles.y;

        // 🌟 原有 GUI 佈局與樣式設定（100% 完整保留，無任何刪改）
        GUI.Box(new Rect(10, 10, 280, 195), "");
        GUI.Label(new Rect(20, 20, 260, 30), $"Status:    {status}", statusStyle);
        GUI.Label(new Rect(20, 50, 260, 30), $"Altitude:  {altitude:F1} m", styleWhite);
        GUI.Label(new Rect(20, 80, 260, 30), $"Heading:   {yaw:F0}°", styleWhite);
        GUI.Label(new Rect(20, 110, 260, 30), $"Position:  X:{pos.x:F1}  Z:{pos.z:F1}", styleYellow);
        GUI.Label(new Rect(20, 140, 260, 30), $"Speed:     {currentSpeed:F1} m/s", styleCyan);
        GUI.Label(new Rect(20, 170, 260, 30), $"Control:   Hand / WASD", styleWhite);
    }
}
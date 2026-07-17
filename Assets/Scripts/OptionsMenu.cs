using UnityEngine;
using UnityEngine.SceneManagement;

public class OptionsMenu : MonoBehaviour
{
    public DroneController drone;
    private bool isOpen = false;

    private float wasdMoveSpeed;
    private float wasdVerticalSpeed;
    private float wasdRotateSpeed;
    private float handMoveSpeed;
    private float handVerticalSpeed;
    private float handRotateMultiplier;
    private float handZSensitivity;

    private GUIStyle styleTitle;
    private GUIStyle styleLabel;
    private GUIStyle styleValue;

    void Start()
    {
        wasdMoveSpeed = drone.moveSpeed;
        wasdVerticalSpeed = drone.verticalSpeed;
        wasdRotateSpeed = drone.rotateSpeed;
        handMoveSpeed = 45f;
        handVerticalSpeed = 45f;
        handRotateMultiplier = 9f;
        handZSensitivity = 5f;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            isOpen = !isOpen;

        if (Input.GetKeyDown(KeyCode.R))
            RestartGame();
    }

    void RestartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void OnGUI()
    {
        // ESC ikon mindig látható
        GUI.Box(new Rect(Screen.width / 2 - 80, 10, 160, 30), "");
        GUI.Label(new Rect(Screen.width / 2 - 75, 13, 150, 25), "ESC = Options", new GUIStyle()
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
            alignment = TextAnchor.MiddleCenter
        });

        // Restart gomb – mindig látható
        if (GUI.Button(new Rect(Screen.width - 130, Screen.height - 55, 120, 40), "RESTART (R)"))
            RestartGame();

        if (!isOpen) return;
        if (styleTitle == null) InitStyles();

        int w = 440;
        int h = 570;
        int x = Screen.width / 2 - w / 2;
        int y = Screen.height / 2 - h / 2;

        GUI.Box(new Rect(x, y, w, h), "");
        GUI.Label(new Rect(x + w / 2 - 60, y + 10, 200, 35), "OPTIONS", styleTitle);

        int row = y + 55;
        int col1 = x + 15;
        int col2 = x + 210;
        int col3 = x + 370;
        int rowH = 42;

        // WASD
        GUI.Label(new Rect(col1, row, 300, 25), "── WASD ──", styleLabel); row += 28;
        wasdMoveSpeed = SliderRow(col1, col2, col3, row, "Move Speed", wasdMoveSpeed, 1f, 40f); row += rowH;
        wasdVerticalSpeed = SliderRow(col1, col2, col3, row, "Vertical Speed", wasdVerticalSpeed, 1f, 20f); row += rowH;
        wasdRotateSpeed = SliderRow(col1, col2, col3, row, "Rotate Speed", wasdRotateSpeed, 10f, 150f); row += rowH;

        // Nyilak
        GUI.Label(new Rect(col1, row, 300, 25), "── NYILAK ──", styleLabel); row += 28;
        GUI.Label(new Rect(col1, row, 380, 25), "Nyilak = WASD sebességet használják", new GUIStyle()
        {
            fontSize = 14,
            normal = { textColor = Color.gray }
        }); row += rowH;

        // Hand Control
        GUI.Label(new Rect(col1, row, 300, 25), "── HAND CONTROL ──", styleLabel); row += 28;
        handMoveSpeed = SliderRow(col1, col2, col3, row, "Move Speed", handMoveSpeed, 1f, 200f); row += rowH;
        handVerticalSpeed = SliderRow(col1, col2, col3, row, "Vertical Speed", handVerticalSpeed, 1f, 200f); row += rowH;
        handRotateMultiplier = SliderRow(col1, col2, col3, row, "Rotate Speed", handRotateMultiplier, 0.5f, 50f); row += rowH;
        handZSensitivity = SliderRow(col1, col2, col3, row, "Z Sensitivity", handZSensitivity, 1f, 30f); row += rowH;

        // Azonnal alkalmaz
        drone.moveSpeed = wasdMoveSpeed;
        drone.verticalSpeed = wasdVerticalSpeed;
        drone.rotateSpeed = wasdRotateSpeed;
        drone.handMoveSpeed = handMoveSpeed;
        drone.handVerticalSpeed = handVerticalSpeed;
        drone.handRotateMultiplier = handRotateMultiplier;
        drone.handZSensitivity = handZSensitivity;

        // Reset + Bezárás gombok
        if (GUI.Button(new Rect(x + w / 2 - 160, row + 5, 140, 38), "RESET"))
        {
            wasdMoveSpeed = 15f;
            wasdVerticalSpeed = 8f;
            wasdRotateSpeed = 60f;
            handMoveSpeed = 45f;
            handVerticalSpeed = 45f;
            handRotateMultiplier = 9f;
            handZSensitivity = 5f;
        }

        if (GUI.Button(new Rect(x + w / 2 + 20, row + 5, 140, 38), "BEZÁRÁS"))
            isOpen = false;
    }

    float SliderRow(int col1, int col2, int col3, int row, string label, float val, float min, float max)
    {
        GUI.Label(new Rect(col1, row, 200, 25), label, styleLabel);
        float newVal = GUI.HorizontalSlider(new Rect(col2, row + 7, 140, 20), val, min, max);
        GUI.Label(new Rect(col3, row, 70, 25), newVal.ToString("F1"), styleValue);
        return newVal;
    }

    void InitStyles()
    {
        styleTitle = new GUIStyle();
        styleTitle.fontSize = 22;
        styleTitle.fontStyle = FontStyle.Bold;
        styleTitle.normal.textColor = Color.white;
        styleTitle.alignment = TextAnchor.MiddleCenter;

        styleLabel = new GUIStyle();
        styleLabel.fontSize = 16;
        styleLabel.normal.textColor = Color.cyan;

        styleValue = new GUIStyle();
        styleValue.fontSize = 16;
        styleValue.fontStyle = FontStyle.Bold;
        styleValue.normal.textColor = Color.yellow;
    }
}
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    [Header("Database & Spawning")]
    public PlayerDB playerDB; // 【新增】拖入你的 PlayerDB
    //public Vector3 spawnPosition = new Vector3(0, 2f, 0); // 【新增】無人機起始位置

    public int totalCheckpoints = 7;
    public Transform droneTransform;
    public string pythonPath = "python";
    public string scriptPath = "C:\\Users\\csapo\\Desktop\\dronegame\\hand_tracker.py";

    private int passedCheckpoints = 0;
    private float elapsedTime = 0f;
    private bool raceStarted = false;
    private bool raceFinished = false;
    private Vector3 lastDronePos;

    private CheckpointRing[] rings;

    private GUIStyle styleCounter;
    private GUIStyle styleTimer;
    private GUIStyle styleFinish;
    private GUIStyle styleButton;

    void Awake()
    {
        Invoke("InitRings", 0.1f);
    }

    void InitRings()
    {
        rings = FindObjectsOfType<CheckpointRing>();
        System.Array.Sort(rings, (a, b) => a.checkpointIndex.CompareTo(b.checkpointIndex));
        totalCheckpoints = rings.Length;

        Debug.Log("Rings found: " + rings.Length);

        if (rings.Length > 0)
            rings[0].MarkActive();

        if (droneTransform != null)
            lastDronePos = droneTransform.position;

        styleCounter = CreateStyle(Color.white, 26, TextAnchor.UpperRight);
        styleTimer = CreateStyle(Color.yellow, 24, TextAnchor.UpperRight);
        styleFinish = CreateStyle(Color.green, 40, TextAnchor.MiddleCenter);
    }

    GUIStyle CreateStyle(Color color, int size, TextAnchor anchor)
    {
        GUIStyle s = new GUIStyle();
        s.normal.textColor = color;
        s.fontSize = size;
        s.fontStyle = FontStyle.Bold;
        s.alignment = anchor;
        return s;
    }

    void Update()
    {
        if (!raceFinished)
        {
            if (!raceStarted && droneTransform != null)
            {
                float moved = Vector3.Distance(droneTransform.position, lastDronePos);
                if (moved > 0.001f)
                    raceStarted = true;
                lastDronePos = droneTransform.position;
            }

            if (raceStarted)
                elapsedTime += Time.deltaTime;
        }
    }

    public void CheckpointPassed(int index)
    {
        if (raceFinished) return;
        if (index != passedCheckpoints) return;

        rings[index].MarkPassed();
        passedCheckpoints++;

        if (passedCheckpoints < rings.Length)
            rings[passedCheckpoints].MarkActive();

        if (passedCheckpoints >= rings.Length)
            raceFinished = true;
    }

    public void StartHandTracking()
    {
        System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
        psi.FileName = "cmd.exe";
        psi.Arguments = "/c py -3.11 \"" + scriptPath + "\"";
        psi.UseShellExecute = true;
        psi.WorkingDirectory = "C:\\Users\\csapo\\Desktop\\dronegame";
        System.Diagnostics.Process.Start(psi);
    }

    void RestartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void OnGUI()
    {
        if (styleCounter == null || styleTimer == null || styleFinish == null) return;

        int screenW = Screen.width;

        // Checkpoint counter – jobb felső sarok
        GUI.Box(new Rect(screenW - 230, 10, 220, 50), "");
        GUI.Label(new Rect(screenW - 225, 15, 210, 40),
            $"Checkpoints:  {passedCheckpoints} / {totalCheckpoints}",
            styleCounter);

        // Timer – checkpoint counter alatt
        GUI.Box(new Rect(screenW - 230, 65, 220, 45), "");
        string timeStr = raceFinished ? $"FINISH: {elapsedTime:F2}s" :
                         raceStarted ? $"Time: {elapsedTime:F2}s" : "Time: --";
        GUI.Label(new Rect(screenW - 225, 70, 210, 40), timeStr, styleTimer);

        // Hand Control gomb – bal oldal
        if (GUI.Button(new Rect(10, 210, 200, 40), "Hand Control"))
        {
            StartHandTracking();
        }

        // Finish felirat + Play Again gomb
        if (raceFinished)
        {
            GUI.Label(new Rect(screenW / 2 - 200, Screen.height / 2 - 60, 400, 80),
                $"FINISHED!  {elapsedTime:F2}s", styleFinish);

            if (GUI.Button(new Rect(screenW / 2 - 100, Screen.height / 2 + 40, 200, 50), "PLAY AGAIN"))
            {
                RestartGame();
            }
        }
    }
}
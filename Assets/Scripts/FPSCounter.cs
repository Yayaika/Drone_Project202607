using UnityEngine;

public class FPSCounter : MonoBehaviour
{
    private float deltaTime = 0f;
    private GUIStyle style;

    void Update()
    {
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
    }

    void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle();
            style.fontSize = 20;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = Color.white;
        }

        float fps = 1.0f / deltaTime;
        Color c = fps >= 50 ? Color.green : fps >= 30 ? Color.yellow : Color.red;
        style.normal.textColor = c;

        GUI.Box(new Rect(10, Screen.height - 45, 120, 35), "");
        GUI.Label(new Rect(15, Screen.height - 40, 110, 30), $"FPS: {fps:F0}", style);
    }
}
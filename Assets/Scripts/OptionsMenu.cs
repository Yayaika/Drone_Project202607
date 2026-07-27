using UnityEngine;
using UnityEngine.SceneManagement;

public class OptionsMenu : MonoBehaviour
{
    private bool isOpen = false;
    private GUIStyle styleTitle;

    void Update()
    {
        // 按下 ESC 開啟/關閉選單，並同步暫停/恢復遊戲時間
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            isOpen = !isOpen;
            Time.timeScale = isOpen ? 0f : 1f;
        }

        // 按下 R 鍵快捷重新開始
        if (Input.GetKeyDown(KeyCode.R))
        {
            RestartGame();
        }
    }

    void RestartGame()
    {
        Time.timeScale = 1f; // 恢復時間才能正常載入
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void OnGUI()
    {
        // 上方 ESC 狀態提示列 (保留)
        GUI.Box(new Rect(Screen.width / 2 - 80, 10, 160, 30), "");
        GUI.Label(new Rect(Screen.width / 2 - 75, 13, 150, 25), "ESC = Menu", new GUIStyle()
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
            alignment = TextAnchor.MiddleCenter
        });

        // 右下角 RESTART (R) 快捷按鈕 (保留)
        if (GUI.Button(new Rect(Screen.width - 130, Screen.height - 55, 120, 40), "RESTART (R)"))
        {
            RestartGame();
        }

        if (!isOpen) return;
        if (styleTitle == null) InitStyles();

        // 縮小後的精簡面板尺寸 (260 x 200)
        int w = 260;
        int h = 200;
        int x = Screen.width / 2 - w / 2;
        int y = Screen.height / 2 - h / 2;

        GUI.Box(new Rect(x, y, w, h), "");
        GUI.Label(new Rect(x, y + 15, w, 30), "PAUSE", styleTitle);

        int btnW = 180;
        int btnH = 35;
        int btnX = x + (w - btnW) / 2;
        int startY = y + 55;

        // 1. 繼續遊戲
        if (GUI.Button(new Rect(btnX, startY, btnW, btnH), "RESUME"))
        {
            isOpen = false;
            Time.timeScale = 1f;
        }

        // 2. 重新開始
        if (GUI.Button(new Rect(btnX, startY + 42, btnW, btnH), "RESTART"))
        {
            RestartGame();
        }

        // 3. 離開遊戲
        if (GUI.Button(new Rect(btnX, startY + 84, btnW, btnH), "QUIT"))
        {
            Time.timeScale = 1f;
            Debug.Log("[OptionsMenu] 退出遊戲");
            Application.Quit();
        }
    }

    void InitStyles()
    {
        styleTitle = new GUIStyle();
        styleTitle.fontSize = 22;
        styleTitle.fontStyle = FontStyle.Bold;
        styleTitle.normal.textColor = Color.white;
        styleTitle.alignment = TextAnchor.MiddleCenter;
    }
}
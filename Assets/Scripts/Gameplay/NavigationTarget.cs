using UnityEngine;

/// <summary>
/// 目前的導航目標 (下一個檢查點、取貨點或送貨點)，供 HUD 顯示方向與距離。
/// 由遊戲規則腳本設定，HUD 只負責讀取。
/// </summary>
public static class NavigationTarget
{
    public static bool HasTarget { get; private set; }
    public static Vector3 Position { get; private set; }
    public static string Label { get; private set; } = string.Empty;
    public static Color Color { get; private set; } = Color.cyan;

    public static void Set(Vector3 position, string label, Color color)
    {
        HasTarget = true;
        Position = position;
        Label = label ?? string.Empty;
        Color = color;
    }

    public static void Set(Vector3 position, string label)
    {
        Set(position, label, Color.cyan);
    }

    public static void Clear()
    {
        HasTarget = false;
        Label = string.Empty;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Clear();
        // 換場景時清除舊目標 (新場景的規則腳本會在 Start 之後重新設定)
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (mode == UnityEngine.SceneManagement.LoadSceneMode.Single)
        {
            Clear();
        }
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 靜態唯一資料管理脚本
/// </summary>
public static class GameManager1
{
    /// <summary>
    /// 當前正在操作角色的索引號碼
    /// </summary>
    public static int playerIndex;

    public static void LoadScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }
}

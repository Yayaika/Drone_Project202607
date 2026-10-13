using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneChanger : MonoBehaviour
{
    // 按下PLAY時會執行一次（初始化）
    void Start()
    {

    }

    // 畫面每一幀執行一次（持續更新）
    void Update()
    {
        
    }

    public void LoadScene(string sceneName)
    {
        // 改走非同步載入 + Loading 畫面
        SceneLoader.Load(sceneName);
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}

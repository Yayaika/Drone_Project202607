using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class ScreenFader : MonoBehaviour
{
    // 靜態實例，方便其他腳本直接調用
    public static ScreenFader Instance { get; private set; }

    public Image fadeImage;
    public float fadeDuration = 1.0f;

    private void Awake()
    {
        // 確保遊戲中只有一個 ScreenFader 實例
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // 關鍵：跨場景時不銷毀此物件
        }
        else
        {
            Destroy(gameObject); // 如果有重複的，則銷毀
            return;
        }
    }

    private void Start()
    {
        // 遊戲啟動時，自動執行一次淡出（變亮）
        StartCoroutine(FadeIn());
    }

    public IEnumerator FadeIn()
    {
        fadeImage.raycastTarget = true;
        float elapsedTime = 0f;
        Color color = fadeImage.color;

        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            color.a = Mathf.Clamp01(1f - (elapsedTime / fadeDuration));
            fadeImage.color = color;
            yield return null;
        }

        fadeImage.raycastTarget = false;
    }

    public IEnumerator FadeOut()
    {
        fadeImage.raycastTarget = true;
        float elapsedTime = 0f;
        Color color = fadeImage.color;

        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            color.a = Mathf.Clamp01(elapsedTime / fadeDuration);
            fadeImage.color = color;
            yield return null;
        }
    }

    // 外部調用的場景切換方法
    public void TransitionToScene(string sceneName)
    {
        StartCoroutine(DoTransition(sceneName));
    }

    private IEnumerator DoTransition(string sceneName)
    {
        yield return StartCoroutine(FadeOut()); // 1. 螢幕變黑

        // 2. 異步載入新場景（此時畫面上是黑屏，玩家看不到載入過程）
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        yield return StartCoroutine(FadeIn()); // 3. 新場景載入完成後，螢幕變亮
    }
}
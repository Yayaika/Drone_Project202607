using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 帶 DOTween 淡入淡出的場景切換 (跨場景常駐)。
/// 註：Screen Space Overlay 在 VR 頭顯中不會顯示，VR 下仍會正常切換，只是沒有淡出效果。
/// </summary>
public class SceneLoader : MonoBehaviour
{
    private static SceneLoader instance;

    private CanvasGroup fadeGroup;
    private bool isLoading;

    public static bool IsLoading => instance != null && instance.isLoading;

    public static void Load(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[SceneLoader] 場景「{sceneName}」不在 Build Settings 中，請先加入 (File > Build Profiles / Build Settings)。");
            return;
        }

        EnsureInstance();
        if (instance.isLoading) return;
        instance.StartCoroutine(instance.LoadRoutine(sceneName));
    }

    public static void ReloadCurrent()
    {
        Load(SceneManager.GetActiveScene().name);
    }

    private static void EnsureInstance()
    {
        if (instance != null) return;

        GameObject host = new GameObject("[SceneLoader]");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<SceneLoader>();

        Canvas canvas = host.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        host.AddComponent<CanvasScaler>();

        RectTransform fade = RuntimeUI.CreateRect("Fade", host.transform);
        RuntimeUI.Stretch(fade);
        Image image = fade.gameObject.AddComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = true;
        instance.fadeGroup = fade.gameObject.AddComponent<CanvasGroup>();
        instance.fadeGroup.alpha = 0f;
        instance.fadeGroup.blocksRaycasts = false;
    }

    private IEnumerator LoadRoutine(string sceneName)
    {
        isLoading = true;
        fadeGroup.blocksRaycasts = true;
        DOTween.KillAll(); // 避免舊場景的補間在物件銷毀後繼續執行
        yield return fadeGroup.DOFade(1f, 0.35f).SetUpdate(true).WaitForCompletion();

        Time.timeScale = 1f;
        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
        while (operation != null && !operation.isDone)
        {
            yield return null;
        }

        yield return fadeGroup.DOFade(0f, 0.45f).SetUpdate(true).WaitForCompletion();
        fadeGroup.blocksRaycasts = false;
        isLoading = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }
}

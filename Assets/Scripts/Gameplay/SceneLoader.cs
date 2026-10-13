using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 跨場景常駐的非同步場景切換 + 簡潔 Loading 畫面 (旋轉螺旋槳的無人機圖示、進度條、操作提示)。
/// Canvas 使用 Screen Space - Camera 綁定目前的 Main Camera，PC 與 VR 頭顯都看得到；
/// 找不到相機時 (切換場景的瞬間) 暫時改用 Overlay。
/// </summary>
public class SceneLoader : MonoBehaviour
{
    private const float FadeInTime = 0.3f;
    private const float FadeOutTime = 0.45f;
    /// <summary>Loading 畫面最短顯示時間，避免一閃而過</summary>
    private const float MinimumShowTime = 0.9f;

    private static readonly string[] Tips =
    {
        "提示：油門推滿約 0.8 秒即可解鎖引擎起飛",
        "提示：按 C 切換第一人稱 / 第三人稱視角",
        "提示：按 F 切換飛行模式 (自穩 / 巡航 / 垂直)",
        "提示：按 R 回到最後通過的檢查點",
        "提示：靠近貨物後按 Z 抓取，到達送貨點再按 Z 放下",
        "提示：注意路口的車流與行人，撞擊會損傷貨物",
        "提示：按 H 切換擬真 OSD 與簡易數據面板"
    };

    private static SceneLoader instance;

    private Canvas canvas;
    private CanvasGroup group;
    private RectTransform droneIcon;
    private readonly RectTransform[] propellers = new RectTransform[4];
    private Image progressFill;
    private TextMeshProUGUI percentText;
    private TextMeshProUGUI tipText;
    private float shownProgress;
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
        instance.BuildUi();
        instance.canvas.enabled = false;
    }

    #region 介面

    private void BuildUi()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        RectTransform root = RuntimeUI.CreateRect("LoadingRoot", transform);
        RuntimeUI.Stretch(root);
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;

        Image background = root.gameObject.AddComponent<Image>();
        background.color = new Color(0.03f, 0.06f, 0.09f, 1f);
        background.raycastTarget = true;

        // 無人機圖示：X 形機架 + 四個旋轉的螺旋槳
        droneIcon = RuntimeUI.CreateCentered("DroneIcon", root, new Vector2(0f, 70f), new Vector2(220f, 220f));
        Color frame = new Color(0.82f, 0.88f, 0.92f, 1f);
        for (int i = 0; i < 2; i++)
        {
            Image arm = RuntimeUI.CreateImage("Arm", droneIcon, Vector2.zero, new Vector2(200f, 14f), frame, RuntimeUI.RoundedSprite);
            arm.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
        }
        RuntimeUI.CreateImage("Body", droneIcon, Vector2.zero, new Vector2(58f, 58f), frame, RuntimeUI.RoundedSprite);
        RuntimeUI.CreateImage("Eye", droneIcon, new Vector2(0f, 8f), new Vector2(18f, 18f), RuntimeUI.AccentColor, RuntimeUI.RoundedSprite);

        Vector2[] motors = { new Vector2(-71f, 71f), new Vector2(71f, 71f), new Vector2(-71f, -71f), new Vector2(71f, -71f) };
        for (int i = 0; i < motors.Length; i++)
        {
            RuntimeUI.CreateImage("Motor", droneIcon, motors[i], new Vector2(22f, 22f), frame, RuntimeUI.RoundedSprite);
            RectTransform prop = RuntimeUI.CreateCentered("Prop", droneIcon, motors[i], new Vector2(96f, 96f));
            Color blade = new Color(RuntimeUI.AccentColor.r, RuntimeUI.AccentColor.g, RuntimeUI.AccentColor.b, 0.85f);
            RuntimeUI.CreateImage("BladeA", prop, Vector2.zero, new Vector2(96f, 10f), blade, RuntimeUI.RoundedSprite);
            RuntimeUI.CreateImage("BladeB", prop, Vector2.zero, new Vector2(10f, 96f), new Color(blade.r, blade.g, blade.b, 0.25f), RuntimeUI.RoundedSprite);
            propellers[i] = prop;
        }

        RuntimeUI.CreateText("Title", root, "載入中", 40f, new Vector2(0f, -110f), new Vector2(600f, 60f));

        Image track = RuntimeUI.CreateImage("ProgressTrack", root, new Vector2(0f, -170f), new Vector2(560f, 10f),
            new Color(1f, 1f, 1f, 0.15f), RuntimeUI.RoundedSprite);
        progressFill = RuntimeUI.CreateImage("ProgressFill", track.transform, Vector2.zero, new Vector2(560f, 10f),
            RuntimeUI.AccentColor, RuntimeUI.RoundedSprite);
        RectTransform fillRect = progressFill.rectTransform;
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.anchoredPosition = Vector2.zero;
        fillRect.sizeDelta = new Vector2(0f, 0f);

        percentText = RuntimeUI.CreateText("Percent", root, "0%", 24f, new Vector2(0f, -205f), new Vector2(200f, 40f));
        percentText.color = RuntimeUI.AccentColor;
        tipText = RuntimeUI.CreateText("Tip", root, string.Empty, 24f, new Vector2(0f, -300f), new Vector2(1400f, 40f));
        tipText.color = new Color(1f, 1f, 1f, 0.65f);
    }

    private void SetProgress(float value)
    {
        shownProgress = Mathf.Clamp01(value);
        progressFill.rectTransform.sizeDelta = new Vector2(560f * shownProgress, 0f);
        percentText.text = Mathf.RoundToInt(shownProgress * 100f) + "%";
    }

    private void Update()
    {
        if (!canvas.enabled) return;

        BindCamera();

        // 螺旋槳轉動 + 機身輕微懸浮 (不受 timeScale 影響)
        float t = Time.unscaledTime;
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < propellers.Length; i++)
        {
            float direction = i == 0 || i == 3 ? 1f : -1f;
            propellers[i].Rotate(0f, 0f, direction * 1080f * dt);
        }
        droneIcon.anchoredPosition = new Vector2(0f, 70f + Mathf.Sin(t * 2.2f) * 10f);
        droneIcon.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.4f) * 4f);
    }

    /// <summary>綁定目前的 Main Camera (VR 頭顯中可見)；沒有相機時改用 Overlay</summary>
    private void BindCamera()
    {
        Camera main = Camera.main;
        if (main != null)
        {
            if (canvas.renderMode != RenderMode.ScreenSpaceCamera || canvas.worldCamera != main)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = main;
                canvas.planeDistance = Mathf.Max(main.nearClipPlane + 0.05f, 0.4f);
            }
        }
        else if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
    }

    #endregion

    private IEnumerator LoadRoutine(string sceneName)
    {
        isLoading = true;
        DOTween.KillAll(); // 避免舊場景的補間在物件銷毀後繼續執行

        canvas.enabled = true;
        BindCamera();
        group.blocksRaycasts = true;
        tipText.text = Tips[Random.Range(0, Tips.Length)];
        SetProgress(0f);

        for (float t = 0f; t < FadeInTime; t += Time.unscaledDeltaTime)
        {
            group.alpha = t / FadeInTime;
            yield return null;
        }
        group.alpha = 1f;

        Time.timeScale = 1f;
        float shownSince = Time.unscaledTime;
        float loadStart = Time.realtimeSinceStartup;
        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
        if (operation == null)
        {
            Debug.LogError($"[SceneLoader] 無法載入場景「{sceneName}」。");
            yield return FadeOut();
            yield break;
        }
        operation.allowSceneActivation = false;

        // 載入進度到 0.9 代表資料讀取完成，等待啟用
        while (operation.progress < 0.9f || Time.unscaledTime - shownSince < MinimumShowTime || shownProgress < 0.995f)
        {
            float target = Mathf.Clamp01(operation.progress / 0.9f);
            // 依最短顯示時間限制顯示進度，讓進度條平順前進
            target = Mathf.Min(target, (Time.unscaledTime - shownSince) / MinimumShowTime);
            SetProgress(Mathf.MoveTowards(shownProgress, target, Time.unscaledDeltaTime * 2.5f));
            yield return null;
        }

        SetProgress(1f);
        float readSeconds = Time.realtimeSinceStartup - loadStart;
        float activateStart = Time.realtimeSinceStartup;
        operation.allowSceneActivation = true;
        while (!operation.isDone)
        {
            yield return null;
        }
        Debug.Log($"[SceneLoader] {sceneName}：讀取 {readSeconds:F2}s、啟用 (Awake/Start 與程序化生成) {Time.realtimeSinceStartup - activateStart:F2}s");

        // 等新場景的 Start/程序化生成完成一幀，再淡出
        yield return null;
        BindCamera();
        yield return FadeOut();
    }

    private IEnumerator FadeOut()
    {
        for (float t = 0f; t < FadeOutTime; t += Time.unscaledDeltaTime)
        {
            group.alpha = 1f - t / FadeOutTime;
            yield return null;
        }
        group.alpha = 0f;
        group.blocksRaycasts = false;
        canvas.enabled = false;
        isLoading = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }
}

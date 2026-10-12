using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 按鈕 DOTween 動態回饋：滑入放大、按下壓縮、點擊彈跳，並播放 UI 音效。
/// 由 <see cref="InstallOnAllButtons"/> 在每次載入場景時自動掛到所有 Button 上。
/// </summary>
[DisallowMultipleComponent]
public class UIButtonTweenFx : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
    ISelectHandler, IDeselectHandler
{
    [SerializeField] private float hoverScale = 1.08f;
    [SerializeField] private float pressScale = 0.92f;
    [SerializeField] private float duration = 0.15f;

    private Vector3 baseScale = Vector3.one;
    private Button button;
    private bool hovered;

    private void Awake()
    {
        baseScale = transform.localScale;
        button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.AddListener(OnClicked);
        }
    }

    private void OnDisable()
    {
        transform.DOKill();
        transform.localScale = baseScale;
        hovered = false;
    }

    private bool Interactable => button == null || button.IsInteractable();

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!Interactable) return;
        hovered = true;
        TweenTo(hoverScale, Ease.OutBack);
        GameAudio.Play(GameSfx.UIHover, 0.5f);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        TweenTo(1f, Ease.OutQuad);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!Interactable) return;
        TweenTo(pressScale, Ease.OutQuad);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        TweenTo(hovered ? hoverScale : 1f, Ease.OutBack);
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (!Interactable) return;
        TweenTo(hoverScale, Ease.OutBack);
    }

    public void OnDeselect(BaseEventData eventData)
    {
        TweenTo(1f, Ease.OutQuad);
    }

    private void OnClicked()
    {
        GameAudio.Play(GameSfx.UIClick);
        transform.DOKill();
        transform.localScale = baseScale * (hovered ? hoverScale : 1f);
        transform.DOPunchScale(baseScale * 0.12f, 0.25f, 8, 0.8f).SetUpdate(true);
    }

    private void TweenTo(float multiplier, Ease ease)
    {
        transform.DOKill();
        transform.DOScale(baseScale * multiplier, duration).SetEase(ease).SetUpdate(true);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        DOTween.Init(recycleAllByDefault: false, useSafeMode: true, logBehaviour: LogBehaviour.ErrorsOnly);
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        InstallOnAllButtons();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        InstallOnAllButtons();
    }

    /// <summary>為場景中所有按鈕 (含未啟用的) 加上動態效果</summary>
    public static void InstallOnAllButtons()
    {
        Button[] buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Button target in buttons)
        {
            if (target != null && target.GetComponent<UIButtonTweenFx>() == null)
            {
                target.gameObject.AddComponent<UIButtonTweenFx>();
            }
        }
    }
}

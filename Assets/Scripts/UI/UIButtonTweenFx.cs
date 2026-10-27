using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 按鈕 DOTween 動態回饋：滑入放大、按下壓縮、點擊彈跳，並播放 UI 音效。
/// 手把 / 鍵盤選中時 (Selected) 加上脈動的藍色外框並維持放大，清楚標示目前選取的按鈕。
/// 由 <see cref="InstallOnAllButtons"/> 在每次載入場景時自動掛到所有 Button / Toggle / Slider 上。
/// </summary>
[DisallowMultipleComponent]
public class UIButtonTweenFx : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
    ISelectHandler, IDeselectHandler
{
    [SerializeField] private float hoverScale = 1.08f;
    [SerializeField] private float pressScale = 0.92f;
    [SerializeField] private float duration = 0.15f;
    [SerializeField] private Color selectedGlowColor = new Color(0.2f, 0.6f, 1f, 1f);
    [Tooltip("滑入 / 選中時按鈕底色 (Color Tint)")]
    [SerializeField] private Color highlightTint = new Color(0.6f, 0.8f, 1f, 1f);
    [SerializeField] private Vector2 selectedGlowSize = new Vector2(10f, -10f);

    private Vector3 baseScale = Vector3.one;
    private Selectable selectable;
    private Button button;
    private Outline selectionGlow;
    private bool hovered;
    private bool selected;
    // 只控制自己的縮放動畫；不可用 transform.DOKill()，會連同 UITween.StaggerIn 的位移動畫一起中斷，使按鈕停在錯位的位置
    private Tween scaleTween;

    private void Awake()
    {
        baseScale = transform.localScale;
        selectable = GetComponent<Selectable>();
        button = selectable as Button;
        if (selectable != null && selectable.transition == Selectable.Transition.ColorTint)
        {
            // 互動高亮統一為藍色
            ColorBlock colors = selectable.colors;
            colors.highlightedColor = highlightTint;
            colors.selectedColor = highlightTint;
            selectable.colors = colors;
        }
        if (button != null)
        {
            button.onClick.AddListener(OnClicked);
        }
    }

    private void OnDisable()
    {
        scaleTween?.Kill();
        transform.localScale = baseScale;
        hovered = false;
        selected = false;
        SetGlow(false);
    }

    private bool Interactable => selectable == null || selectable.IsInteractable();

    /// <summary>目前是否顯示選中回饋 (選取發生在本元件安裝前時，由 GamepadUINavigator 補上)</summary>
    public bool IsShowingSelection => selected;

    /// <summary>調整滑入 / 選中與按下的縮放倍率 (並排的大卡片設為 1 可保持同樣大小)</summary>
    public void SetScaleFeedback(float hover, float press)
    {
        hoverScale = hover;
        pressScale = press;
    }

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
        TweenTo(selected ? hoverScale : 1f, Ease.OutQuad);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!Interactable) return;
        TweenTo(pressScale, Ease.OutQuad);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        TweenTo(hovered || selected ? hoverScale : 1f, Ease.OutBack);
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (!Interactable) return;
        selected = true;
        TweenTo(hoverScale, Ease.OutBack);
        SetGlow(true);
        // 滑鼠 / 射線已在滑入時播過音效
        if (!hovered) GameAudio.Play(GameSfx.UIHover, 0.5f);
    }

    public void OnDeselect(BaseEventData eventData)
    {
        selected = false;
        TweenTo(hovered ? hoverScale : 1f, Ease.OutQuad);
        SetGlow(false);
    }

    /// <summary>選中外框：加在按鈕的底圖上，透明度來回脈動</summary>
    private void SetGlow(bool on)
    {
        if (selectionGlow == null)
        {
            if (!on) return;
            Graphic graphic = selectable != null && selectable.targetGraphic != null ? selectable.targetGraphic : GetComponent<Graphic>();
            if (graphic == null) return;
            selectionGlow = graphic.gameObject.AddComponent<Outline>();
            selectionGlow.effectDistance = selectedGlowSize;
        }

        DOTween.Kill(selectionGlow);
        selectionGlow.effectColor = selectedGlowColor;
        selectionGlow.enabled = on;
        if (!on) return;

        DOTween.To(() => selectionGlow.effectColor.a, alpha =>
            {
                Color color = selectionGlow.effectColor;
                color.a = alpha;
                selectionGlow.effectColor = color;
            }, 0.6f, 0.55f)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetUpdate(true)
            .SetTarget(selectionGlow);
    }

    private void OnClicked()
    {
        GameAudio.Play(GameSfx.UIClick);
        scaleTween?.Kill();
        transform.localScale = baseScale * (hovered || selected ? hoverScale : 1f);
        scaleTween = transform.DOPunchScale(baseScale * 0.12f, 0.25f, 8, 0.8f).SetUpdate(true);
    }

    private void TweenTo(float multiplier, Ease ease)
    {
        scaleTween?.Kill();
        scaleTween = transform.DOScale(baseScale * multiplier, duration).SetEase(ease).SetUpdate(true);
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

    /// <summary>為場景中所有按鈕 / 開關 / 滑桿 (含未啟用的) 加上動態效果與選中外框</summary>
    public static void InstallOnAllButtons()
    {
        Selectable[] selectables = Object.FindObjectsByType<Selectable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Selectable target in selectables)
        {
            if (!(target is Button || target is Toggle || target is Slider)) continue;
            if (target != null && target.GetComponent<UIButtonTweenFx>() == null)
            {
                target.gameObject.AddComponent<UIButtonTweenFx>();
            }
        }
    }
}

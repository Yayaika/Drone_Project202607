using DG.Tweening;
using UnityEngine;

// 預設必須的原件
[RequireComponent(typeof(CanvasGroup))]
public class UIPanelCtrl : MonoBehaviour
{
    /// <summary>
    /// CanvasGroup元件本體(盡量不直接使用)
    /// </summary>
    private CanvasGroup _canvasGroup;

    private CanvasGroup canvasGroup
    {
        get
        {
            // 如果還沒抓過元件，就去抓
            if (_canvasGroup == null)
            {
                _canvasGroup = GetComponent<CanvasGroup>();
            }
            // 回傳已抓取的元件
            return _canvasGroup;
        }
    }

    [Tooltip("UI面板預設是否開啓")]
    public bool openOnAwake;

    [Header("DOTween 動畫")]
    [Tooltip("開關面板時是否播放縮放/淡入淡出動畫")]
    public bool animate = true;
    [Tooltip("開啟時從多小的縮放比例彈出")]
    [Range(0.5f, 1f)]
    public float popFromScale = 0.85f;
    [Tooltip("開啟後子物件是否依序淡入")]
    public bool staggerChildren = true;

    private bool initialized;

    private UIModalNavigation _modal;
    // 手把 B 鍵關閉面板
    private UIModalNavigation modal => _modal ??= new UIModalNavigation(transform, () => Switch(false));

    public bool IsOpen { get; private set; }

    void Start()
    {
        // 初始狀態直接套用，不播放動畫
        SetInstant(openOnAwake);
        initialized = true;
    }

    /// <summary>
    /// UI面板開關，開啓時alpha為1，關閉時alpha為0，並且根據參數決定是否可交互
    /// </summary>
    /// <param name="B">true 開 / false 関</param>
    public void Switch(bool B)
    {
        if (!animate || !initialized || !Application.isPlaying)
        {
            SetInstant(B);
            return;
        }

        IsOpen = B;
        if (B)
        {
            UITween.ShowPanel(canvasGroup, UITween.DefaultDuration, popFromScale);
            // 有 LayoutGroup 時子物件位置由版面控制，不做位移動畫
            if (staggerChildren && GetComponent<UnityEngine.UI.LayoutGroup>() == null)
            {
                UITween.StaggerIn(transform, 0.05f, -15f);
            }
            GameAudio.Play(GameSfx.PanelOpen, 0.7f);
            // 手把 / VR 搖桿只能在面板內移動選取
            modal.Begin();
        }
        else
        {
            UITween.HidePanel(canvasGroup);
            modal.End();
        }
    }

    /// <summary>切換目前開關狀態 (可直接綁在按鈕 OnClick)</summary>
    public void Toggle()
    {
        Switch(!IsOpen);
    }

    private void SetInstant(bool open)
    {
        IsOpen = open;
        canvasGroup.DOKill();
        transform.DOKill();
        transform.localScale = Vector3.one;
        canvasGroup.alpha = open ? 1 : 0;
        canvasGroup.blocksRaycasts = open;
        canvasGroup.interactable = open;
        if (!initialized) return;
        if (open) modal.Begin();
        else modal.End();
    }

    private void OnDisable()
    {
        _modal?.End();
    }
}

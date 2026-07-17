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
    
    void Start()
    {
        Switch(openOnAwake);
    }

    /// <summary>
    /// UI面板開關，開啓時alpha為1，關閉時alpha為0，並且根據參數決定是否可交互
    /// </summary>
    /// <param name="B">true 開 / false 関</param>
    public void Switch(bool B)
    {
        canvasGroup.alpha = B ? 1 : 0;
        canvasGroup.blocksRaycasts = B;
    }
}

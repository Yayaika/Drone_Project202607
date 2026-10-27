using System.Collections;
using System.Reflection;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 以 GameObject.SetActive 開關的面板加上 DOTween 動畫：
/// 啟用時自動彈出 (淡入 + 放大回彈)，呼叫 <see cref="Hide"/> 時先收合再關閉。
/// <see cref="RewireCloseButtons"/> 會把按鈕上「SetActive(false) 關閉此面板」的 Inspector 事件
/// 改為呼叫 Hide，因此不需要修改既有 Prefab 的按鈕設定。
/// </summary>
[DisallowMultipleComponent]
public class UIPopupPanel : MonoBehaviour
{
    [SerializeField] private float showDuration = 0.32f;
    [SerializeField] private float hideDuration = 0.2f;
    [Range(0.5f, 1f)]
    [SerializeField] private float fromScale = 0.82f;
    [SerializeField] private bool playSound = true;

    private CanvasGroup group;
    private RectTransform animatedTarget;
    private Vector3 baseScale = Vector3.one;
    private bool hiding;
    private UIModalNavigation modal;

    public bool IsOpen => gameObject.activeSelf && !hiding;

    /// <summary>指定只縮放的子物件 (例如卡片)，背景遮罩只做淡入</summary>
    public void SetAnimatedTarget(RectTransform target)
    {
        animatedTarget = target;
        baseScale = target != null ? target.localScale : Vector3.one;
    }

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        if (animatedTarget == null)
        {
            animatedTarget = (RectTransform)transform;
            baseScale = animatedTarget.localScale;
        }
    }

    private void OnEnable()
    {
        hiding = false;
        if (group == null) Awake();

        group.DOKill();
        animatedTarget.DOKill();
        group.alpha = 0f;
        group.blocksRaycasts = true;
        group.interactable = true;
        animatedTarget.localScale = baseScale * fromScale;

        group.DOFade(1f, showDuration * 0.7f).SetUpdate(true);
        animatedTarget.DOScale(baseScale, showDuration).SetEase(Ease.OutBack).SetUpdate(true);
        if (playSound) GameAudio.Play(GameSfx.PanelOpen, 0.7f);

        // 手把 / VR 搖桿只能在面板內移動選取
        // 選單中手把 B 鍵關閉面板；飛行中 B 鍵是「回到檢查點」，不改接
        if (modal == null) modal = new UIModalNavigation(transform, FindFirstObjectByType<DroneController1>() == null ? Hide : null);
        modal.Begin();
    }

    private void OnDisable()
    {
        group?.DOKill();
        if (animatedTarget != null)
        {
            animatedTarget.DOKill();
            animatedTarget.localScale = baseScale;
        }
        if (group != null) group.alpha = 1f;
        hiding = false;
        modal?.End();
    }

    public void Show()
    {
        if (gameObject.activeSelf && !hiding) return;
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (!gameObject.activeSelf || hiding) return;
        hiding = true;
        group.blocksRaycasts = false;
        group.interactable = false;
        modal?.End();

        group.DOKill();
        animatedTarget.DOKill();
        DOTween.Sequence()
            .Join(group.DOFade(0f, hideDuration))
            .Join(animatedTarget.DOScale(baseScale * 0.9f, hideDuration).SetEase(Ease.InQuad))
            .SetUpdate(true)
            .SetTarget(group)
            .OnComplete(() =>
            {
                if (this != null) gameObject.SetActive(false);
            });
    }

    public void Toggle()
    {
        if (IsOpen) Hide();
        else Show();
    }

    #region 按鈕事件改接

    private static FieldInfo persistentCallsField;
    private static FieldInfo callsField;
    private static FieldInfo argumentsField;
    private static FieldInfo boolArgumentField;
    private static bool reflectionResolved;

    /// <summary>
    /// 掃描按鈕：若 Inspector 設定為「目標.SetActive(false)」且目標有 UIPopupPanel，
    /// 停用該持久事件並改呼叫 Hide()，讓關閉也有動畫。
    /// </summary>
    public static int RewireCloseButtons(Button[] buttons)
    {
        int rewired = 0;
        foreach (Button button in buttons)
        {
            if (button == null) continue;
            UnityEvent onClick = button.onClick;
            for (int i = 0; i < onClick.GetPersistentEventCount(); i++)
            {
                if (onClick.GetPersistentMethodName(i) != "SetActive") continue;
                if (!(onClick.GetPersistentTarget(i) is GameObject target)) continue;

                UIPopupPanel popup = target.GetComponent<UIPopupPanel>();
                if (popup == null) continue;

                bool? argument = GetPersistentBoolArgument(onClick, i);
                if (argument != false) continue; // 開啟由 OnEnable 自動播放動畫，不需改接

                onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
                onClick.AddListener(popup.Hide);
                rewired++;
            }
        }
        return rewired;
    }

    private static bool? GetPersistentBoolArgument(UnityEventBase unityEvent, int index)
    {
        try
        {
            if (!reflectionResolved)
            {
                reflectionResolved = true;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                persistentCallsField = typeof(UnityEventBase).GetField("m_PersistentCalls", flags);
                callsField = persistentCallsField?.FieldType.GetField("m_Calls", flags);
                System.Type callType = callsField?.FieldType.GetGenericArguments()[0];
                argumentsField = callType?.GetField("m_Arguments", flags);
                boolArgumentField = argumentsField?.FieldType.GetField("m_BoolArgument", flags);
            }

            if (boolArgumentField == null) return null;
            object group = persistentCallsField.GetValue(unityEvent);
            IList calls = callsField.GetValue(group) as IList;
            if (calls == null || index >= calls.Count) return null;
            object arguments = argumentsField.GetValue(calls[index]);
            return (bool)boolArgumentField.GetValue(arguments);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[UIPopupPanel] 無法讀取按鈕事件參數，關閉時將不播放動畫：{exception.Message}");
            return null;
        }
    }

    #endregion
}

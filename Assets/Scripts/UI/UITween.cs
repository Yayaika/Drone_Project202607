using DG.Tweening;
using UnityEngine;

/// <summary>
/// 面板彈出/收合的 DOTween 共用動畫。全部使用 SetUpdate(true)，暫停 (timeScale = 0) 時也能播放。
/// </summary>
public static class UITween
{
    public const float DefaultDuration = 0.3f;

    public static Sequence ShowPanel(CanvasGroup group, float duration = DefaultDuration, float fromScale = 0.85f)
    {
        if (group == null) return null;

        Transform target = group.transform;
        group.DOKill();
        target.DOKill();

        group.gameObject.SetActive(true);
        group.alpha = 0f;
        group.blocksRaycasts = true;
        group.interactable = true;
        target.localScale = Vector3.one * fromScale;

        return DOTween.Sequence()
            .Join(group.DOFade(1f, duration * 0.8f))
            .Join(target.DOScale(1f, duration).SetEase(Ease.OutBack))
            .SetUpdate(true)
            .SetTarget(group);
    }

    public static Sequence HidePanel(CanvasGroup group, float duration = DefaultDuration * 0.7f, bool deactivate = false)
    {
        if (group == null) return null;

        Transform target = group.transform;
        group.DOKill();
        target.DOKill();
        group.blocksRaycasts = false;
        group.interactable = false;

        Sequence sequence = DOTween.Sequence()
            .Join(group.DOFade(0f, duration))
            .Join(target.DOScale(0.9f, duration).SetEase(Ease.InQuad))
            .SetUpdate(true)
            .SetTarget(group);

        sequence.OnComplete(() =>
        {
            target.localScale = Vector3.one;
            if (deactivate && group != null)
            {
                group.gameObject.SetActive(false);
            }
        });
        return sequence;
    }

    /// <summary>子物件依序從下方淡入 (卡片/按鈕列表)</summary>
    public static void StaggerIn(Transform container, float delayStep = 0.06f, float offsetY = -20f)
    {
        if (container == null) return;

        for (int i = 0; i < container.childCount; i++)
        {
            RectTransform child = container.GetChild(i) as RectTransform;
            if (child == null || !child.gameObject.activeSelf) continue;

            CanvasGroup group = child.GetComponent<CanvasGroup>();
            if (group == null) group = child.gameObject.AddComponent<CanvasGroup>();

            // 先完成上一次動畫，確保讀到的是最終位置
            child.DOKill(true);
            group.DOKill(true);
            Vector2 endPosition = child.anchoredPosition;
            child.anchoredPosition = endPosition + new Vector2(0f, offsetY);
            group.alpha = 0f;

            float delay = i * delayStep;
            child.DOAnchorPos(endPosition, 0.3f).SetDelay(delay).SetEase(Ease.OutCubic).SetUpdate(true);
            group.DOFade(1f, 0.25f).SetDelay(delay).SetUpdate(true);
        }
    }

    /// <summary>文字/圖示彈跳強調 (倒數、提示訊息)</summary>
    public static void Pop(Transform target, float strength = 0.35f, float duration = 0.4f)
    {
        if (target == null) return;
        target.DOKill(true);
        target.localScale = Vector3.one;
        target.DOPunchScale(Vector3.one * strength, duration, 6, 0.6f).SetUpdate(true);
    }
}

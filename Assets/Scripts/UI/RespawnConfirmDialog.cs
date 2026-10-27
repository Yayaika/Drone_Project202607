using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 按 R / 手把 B「回到最後檢查點」前的確認視窗 (執行時建立)，避免誤觸。
/// 開啟時暫停遊戲並停用無人機操控 (A 鍵確認不會同時抓取貨物)；確認才重生，退出則恢復飛行。
/// 手把 B 鍵 = 退出 (見 <see cref="GamepadUINavigator"/>)。
/// </summary>
public class RespawnConfirmDialog : MonoBehaviour
{
    private const float DesignWidth = 420f;
    private const float DesignHeight = 210f;

    private static RespawnConfirmDialog current;

    public static bool IsOpen => current != null;

    private CanvasGroup group;
    private UIModalNavigation modal;
    private DroneController1 drone;
    private bool droneCouldControl;
    private float previousTimeScale;
    private System.Action onConfirm;
    private bool closing;
    private int openedFrame;

    /// <summary>開啟確認視窗。parentCanvas 為 null 時建立螢幕覆蓋 Canvas。</summary>
    public static void Open(Canvas parentCanvas, DroneController1 drone, System.Action confirmed)
    {
        if (current != null) return;

        Transform parent;
        Vector2 available;
        if (parentCanvas != null)
        {
            Canvas root = parentCanvas.rootCanvas;
            parent = root.transform;
            available = ((RectTransform)root.transform).rect.size;
        }
        else
        {
            GameObject canvasObject = new GameObject("RespawnConfirmCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 450;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            parent = canvasObject.transform;
            available = new Vector2(1280f, 720f);
        }

        RectTransform overlay = RuntimeUI.CreateRect("RespawnConfirmOverlay", parent);
        RuntimeUI.Stretch(overlay);
        overlay.SetAsLastSibling();
        Image dim = overlay.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.5f);
        dim.raycastTarget = true;

        RespawnConfirmDialog dialog = overlay.gameObject.AddComponent<RespawnConfirmDialog>();
        dialog.drone = drone;
        dialog.onConfirm = confirmed;
        current = dialog;

        float scale = Mathf.Min(available.x / (DesignWidth + 40f), available.y / (DesignHeight + 40f), 1.6f);
        dialog.Build(overlay, Mathf.Max(0.1f, scale));
    }

    private void Build(RectTransform overlay, float scale)
    {
        openedFrame = Time.frameCount;
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        if (drone != null)
        {
            droneCouldControl = drone.canControl;
            drone.canControl = false;
        }

        group = overlay.gameObject.AddComponent<CanvasGroup>();

        RectTransform frame = RuntimeUI.CreateCentered("Frame", overlay, Vector2.zero, new Vector2(DesignWidth, DesignHeight));
        Image frameImage = frame.gameObject.AddComponent<Image>();
        frameImage.sprite = RuntimeUI.RoundedSprite;
        frameImage.type = Image.Type.Sliced;
        frameImage.color = RuntimeUI.PanelColor;
        Outline outline = frame.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(RuntimeUI.AccentColor.r, RuntimeUI.AccentColor.g, RuntimeUI.AccentColor.b, 0.5f);
        outline.effectDistance = new Vector2(2f, -2f);

        TextMeshProUGUI title = RuntimeUI.CreateText("Title", frame, "確定要重來嗎？", 28f, new Vector2(0f, 62f), new Vector2(380f, 40f));
        title.color = RuntimeUI.AccentColor;
        title.fontStyle = FontStyles.Bold;

        TextMeshProUGUI message = RuntimeUI.CreateText("Message", frame, "無人機將回到最後通過的檢查點", 18f, new Vector2(0f, 18f), new Vector2(380f, 28f));
        message.color = new Color(1f, 1f, 1f, 0.85f);

        Button confirm = RuntimeUI.CreateButton("ConfirmButton", frame, "確認重來", new Vector2(-95f, -55f), new Vector2(160f, 44f), Confirm, 20f);
        Button cancel = RuntimeUI.CreateButton("CancelButton", frame, "退出 (B)", new Vector2(95f, -55f), new Vector2(160f, 44f), Cancel, 20f);
        SetNavigation(confirm, cancel);
        SetNavigation(cancel, confirm);

        // 鎖住面板外的手把導航；預選「退出」，連按兩次 B / A 也不會誤觸重來
        modal = new UIModalNavigation(overlay, Cancel);
        modal.Begin(false);
        if (UnityEngine.EventSystems.EventSystem.current != null)
        {
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(cancel.gameObject);
        }

        UITween.ShowPanel(group, 0.25f, 1f);
        frame.localScale = Vector3.one * scale * 0.85f;
        frame.DOScale(scale, 0.25f).SetEase(Ease.OutBack).SetUpdate(true);
        GameAudio.Play(GameSfx.PanelOpen);
    }

    private static void SetNavigation(Selectable selectable, Selectable other)
    {
        selectable.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnLeft = other,
            selectOnRight = other
        };
    }

    private void Confirm() => Close(true);

    private void Cancel()
    {
        // 按 B 開啟的同一幀，GamepadUINavigator 也會把這次 B 當成「關閉面板」，忽略之
        if (Time.frameCount == openedFrame) return;
        Close(false);
    }

    private void Close(bool confirmed)
    {
        if (closing) return;
        closing = true;
        modal?.End();
        RestoreGameplay();
        UITween.HidePanel(group).OnComplete(() =>
        {
            if (this != null) Destroy(gameObject);
        });
        if (confirmed) onConfirm?.Invoke();
    }

    private void RestoreGameplay()
    {
        if (Time.timeScale == 0f) Time.timeScale = previousTimeScale > 0f ? previousTimeScale : 1f;
        if (drone != null) drone.canControl = droneCouldControl;
        drone = null;
    }

    private void OnDestroy()
    {
        modal?.End();
        if (!closing) RestoreGameplay();
        if (current == this) current = null;
    }
}

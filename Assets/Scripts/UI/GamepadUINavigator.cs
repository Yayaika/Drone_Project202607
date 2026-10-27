using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// 讓整個遊戲可以純手把 (Xbox) 操作 UI：
/// - XR UI Input Module 改用 Input System：左搖桿 / 十字鍵移動選取、A 確認、B 取消
///   (原本的 Input Manager 模式只在偵測到舊版搖桿時才讀取，且十字鍵沒有對應)
/// - 沒有選取任何按鈕時，推搖桿 / 十字鍵或按 A 會自動選取一個按鈕 (彈出面板優先)
/// - 選單面板開著時按 B 關閉最上層面板
/// 飛行中 (場景有無人機且沒有開啟面板) 不自動選取，避免 A 鍵 (抓取貨物) 誤觸 HUD 按鈕。
/// 選中按鈕的外框與音效回饋見 <see cref="UIButtonTweenFx"/>。
/// </summary>
public class GamepadUINavigator : MonoBehaviour
{
    private const float StickThreshold = 0.5f;

    private static GamepadUINavigator instance;
    private GameObject lastSelection;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (instance != null) return;
        GameObject host = new GameObject(nameof(GamepadUINavigator));
        DontDestroyOnLoad(host);
        instance = host.AddComponent<GamepadUINavigator>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        ConfigureInputModules();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        lastSelection = null;
        ConfigureInputModules();
    }

    private static void ConfigureInputModules()
    {
        foreach (XRUIInputModule module in FindObjectsByType<XRUIInputModule>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (module.activeInputMode == XRUIInputModule.ActiveInputMode.InputManagerBindings)
            {
                module.activeInputMode = XRUIInputModule.ActiveInputMode.InputSystemActions;
            }
        }
    }

    private void Update()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return;

        GameObject selected = eventSystem.currentSelectedGameObject;
        // 自動導航可能跑到 XR 裝置模擬器的除錯 UI，拉回上一個遊戲按鈕
        if (selected != null && IsDebugUi(selected.transform) && IsUsable(lastSelection))
        {
            eventSystem.SetSelectedGameObject(lastSelection);
            selected = lastSelection;
        }

        if (IsUsable(selected))
        {
            lastSelection = selected;
            // 選取若早於 UIButtonTweenFx 安裝 (例如場景載入時預選)，補上選中外框
            UIButtonTweenFx feedback = selected.GetComponent<UIButtonTweenFx>();
            if (feedback != null && !feedback.IsShowingSelection) feedback.OnSelect(null);
        }

        Gamepad gamepad = Gamepad.current;
        if (gamepad == null) return;

        if (gamepad.buttonEast.wasPressedThisFrame && UIModalNavigation.CancelTop()) return;

        bool navigating = gamepad.leftStick.ReadValue().magnitude > StickThreshold
                          || gamepad.dpad.ReadValue().sqrMagnitude > 0.25f
                          || gamepad.buttonSouth.wasPressedThisFrame;
        if (!navigating || IsUsable(selected)) return;

        // 飛行中沒有開啟面板時不搶選取
        if (UIModalNavigation.Top == null && FindFirstObjectByType<DroneController1>() != null) return;

        if (UIModalNavigation.Top != null)
        {
            UIModalNavigation.Top.SelectFirst();
            return;
        }

        GameObject fallback = IsUsable(lastSelection) ? lastSelection : FindTopLeftSelectable();
        if (fallback != null) eventSystem.SetSelectedGameObject(fallback);
    }

    private static bool IsDebugUi(Transform target)
    {
        return target.root.name.Contains("Simulator");
    }

    private static bool IsUsable(GameObject target)
    {
        if (target == null || !target.activeInHierarchy || IsDebugUi(target.transform)) return false;
        Selectable selectable = target.GetComponent<Selectable>();
        return selectable == null || (selectable.IsInteractable() && selectable.navigation.mode != Navigation.Mode.None);
    }

    /// <summary>畫面上最上方、再最左邊的可互動元件 (略過 XR 裝置模擬器的除錯 UI)</summary>
    private static GameObject FindTopLeftSelectable()
    {
        Camera camera = Camera.main;
        Selectable best = null;
        Vector2 bestPosition = Vector2.zero;
        foreach (Selectable selectable in Selectable.allSelectablesArray)
        {
            if (!selectable.IsInteractable() || selectable.navigation.mode == Navigation.Mode.None) continue;
            if (IsDebugUi(selectable.transform)) continue;

            Vector3 world = selectable.transform.position;
            Canvas canvas = selectable.GetComponentInParent<Canvas>();
            Vector2 position = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay || camera == null
                ? (Vector2)world
                : (Vector2)camera.WorldToScreenPoint(world);

            if (best == null || position.y > bestPosition.y + 1f || (Mathf.Abs(position.y - bestPosition.y) <= 1f && position.x < bestPosition.x))
            {
                best = selectable;
                bestPosition = position;
            }
        }
        return best != null ? best.gameObject : null;
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 彈出面板的手把 / VR 搖桿導航限制：
/// - 開啟時暫時把面板外所有可選元件的 Navigation 設為 None，選取不會跑到被面板蓋住的按鈕
/// - 開啟時選取面板內第一個可互動元件 (沒有選取時手把無法操作)
/// - 關閉時還原 Navigation，並把選取還給開啟前的按鈕 (避免選取物件被銷毀後手把失效)
/// 面板可巢狀開啟 (例如 設置 → 製作名單)，依開啟的相反順序關閉即可正確還原。
/// 有提供 cancel 時，手把 B 鍵會關閉最上層面板 (見 <see cref="GamepadUINavigator"/>)。
/// </summary>
public sealed class UIModalNavigation
{
    private static readonly List<UIModalNavigation> activeModals = new List<UIModalNavigation>();

    private readonly Transform owner;
    private readonly System.Action cancel;
    private readonly Dictionary<Selectable, Navigation> blocked = new Dictionary<Selectable, Navigation>();
    private GameObject previousSelection;

    public bool Active { get; private set; }

    /// <summary>最上層 (最後開啟) 的面板</summary>
    public static UIModalNavigation Top
    {
        get
        {
            activeModals.RemoveAll(modal => modal.owner == null);
            return activeModals.Count > 0 ? activeModals[activeModals.Count - 1] : null;
        }
    }

    public UIModalNavigation(Transform owner, System.Action cancel = null)
    {
        this.owner = owner;
        this.cancel = cancel;
    }

    /// <summary>關閉最上層面板 (手把 B 鍵)；該面板沒有提供 cancel 時回傳 false</summary>
    public static bool CancelTop()
    {
        UIModalNavigation top = Top;
        if (top == null || top.cancel == null) return false;
        top.cancel();
        return true;
    }

    public void Begin(bool selectFirst = true)
    {
        if (Active) return;
        Active = true;
        activeModals.RemoveAll(modal => modal.owner == null);

        EventSystem eventSystem = EventSystem.current;
        GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        previousSelection = selected != null && !selected.transform.IsChildOf(owner) ? selected : null;

        foreach (Selectable selectable in Selectable.allSelectablesArray)
        {
            if (selectable == null) continue;
            if (selectable.transform.IsChildOf(owner))
            {
                // 巢狀開啟：外層面板可能已鎖住本面板的按鈕，先暫時恢復
                foreach (UIModalNavigation other in activeModals)
                {
                    if (other.blocked.TryGetValue(selectable, out Navigation original)) selectable.navigation = original;
                }
                continue;
            }

            Navigation navigation = selectable.navigation;
            if (navigation.mode == Navigation.Mode.None) continue;

            blocked[selectable] = navigation;
            selectable.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        activeModals.Add(this);
        if (selectFirst) SelectFirst();
    }

    public void End()
    {
        if (!Active) return;
        Active = false;

        foreach (KeyValuePair<Selectable, Navigation> pair in blocked)
        {
            if (pair.Key != null) pair.Key.navigation = pair.Value;
        }
        blocked.Clear();
        activeModals.Remove(this);

        // 外層面板仍開著：重新鎖住它面板外的按鈕 (包含剛才暫時恢復的)
        foreach (UIModalNavigation other in activeModals)
        {
            foreach (Selectable selectable in other.blocked.Keys)
            {
                if (selectable != null) selectable.navigation = new Navigation { mode = Navigation.Mode.None };
            }
        }

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return;

        GameObject selected = eventSystem.currentSelectedGameObject;
        bool selectionLost = selected == null || !selected.activeInHierarchy || selected.transform.IsChildOf(owner);
        if (selectionLost && IsSelectable(previousSelection))
        {
            eventSystem.SetSelectedGameObject(previousSelection);
        }
        previousSelection = null;
    }

    /// <summary>選取面板內第一個可互動的元件</summary>
    public void SelectFirst()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null || owner == null) return;

        foreach (Selectable selectable in owner.GetComponentsInChildren<Selectable>())
        {
            if (!selectable.IsInteractable() || selectable.navigation.mode == Navigation.Mode.None) continue;
            eventSystem.SetSelectedGameObject(selectable.gameObject);
            return;
        }
    }

    private static bool IsSelectable(GameObject target)
    {
        if (target == null || !target.activeInHierarchy) return false;
        Selectable selectable = target.GetComponent<Selectable>();
        return selectable == null || selectable.IsInteractable();
    }
}

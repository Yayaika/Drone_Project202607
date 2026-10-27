using System;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class UIPlayerSelectCtrl : MonoBehaviour
{
    private int _index;
    private int index
    {
        get => _index;
        set => _index = value < 0 ? charOptions.Length - 1 : value % charOptions.Length;
    }

    [SerializeField] private PlayerDB _playerDB;
    [SerializeField] private TextMeshProUGUI _textName;
    [SerializeField] private TextMeshProUGUI _textDesc;

    [Header("浮空顯示器中央展示點")]
    [SerializeField] private Transform _previewAnchor;

    [Header("預覽無人機角度旋轉修正")]
    [Tooltip("可以調整 X 軸使無人機向前傾斜 (例如 15~25)，Y 軸設定為 180 讓無人機轉過來面對玩家")]
    [SerializeField] private Vector3 _previewRotationOffset = new Vector3(15f, 180f, 0f);

    [Serializable]
    public struct CharOption
    {
        public string name;
        public CinemachineCamera vCam;
        public Toggle toggle;
        [TextArea] public string desc;

        [Header("場景中的無人機實體")]
        public GameObject droneModel;
    }

    public CharOption[] charOptions;

    private InputAction previousDroneAction;
    private InputAction nextDroneAction;

    private void Start()
    {
        // 初始時先關閉所有無人機腳本
        DisableAllDroneScripts();
        UpdateInfo();
        GameAudio.PlayMusic(GameMusic.Menu);
        SetupGamepadNavigation();
    }

    private void Update()
    {
        // 手把 LB / RB 直接切換無人機 (模式選擇面板開著時不切換)
        if (ModeSelectPanel.IsOpen) return;
        if (previousDroneAction != null && previousDroneAction.WasPressedThisFrame()) PreDrone();
        if (nextDroneAction != null && nextDroneAction.WasPressedThisFrame()) NextDrone();
    }

    private void OnDestroy()
    {
        previousDroneAction?.Dispose();
        nextDroneAction?.Dispose();
    }

    /// <summary>
    /// 手把操作：左 / 右切換鈕與「進入」按鈕明確互相導航 (自動導航會跑到 XR 模擬器的除錯 UI)，
    /// 預選「進入」讓 A 鍵可直接確認；LB / RB 切換無人機。
    /// </summary>
    private void SetupGamepadNavigation()
    {
        previousDroneAction = new InputAction("PreviousDrone", InputActionType.Button, "<Gamepad>/leftShoulder");
        nextDroneAction = new InputAction("NextDrone", InputActionType.Button, "<Gamepad>/rightShoulder");
        previousDroneAction.Enable();
        nextDroneAction.Enable();

        Button left = FindButtonCalling(nameof(PreDrone));
        Button right = FindButtonCalling(nameof(NextDrone));
        Button enter = FindButtonCalling(nameof(EnterStage));

        SetNavigation(left, enter, null, null, right);
        SetNavigation(right, enter, null, left, enter);
        SetNavigation(enter, null, right, right, null);

        if (enter != null && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(enter.gameObject);
        }
    }

    /// <summary>OnClick 設定為呼叫本元件指定方法的按鈕</summary>
    private Button FindButtonCalling(string methodName)
    {
        foreach (Button button in FindObjectsByType<Button>(FindObjectsSortMode.None))
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentTarget(i) == this && button.onClick.GetPersistentMethodName(i) == methodName) return button;
            }
        }
        return null;
    }

    private static void SetNavigation(Selectable selectable, Selectable up, Selectable down, Selectable left, Selectable right)
    {
        if (selectable == null) return;
        selectable.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnUp = up,
            selectOnDown = down,
            selectOnLeft = left,
            selectOnRight = right
        };
    }

    /// <summary>
    /// 強制停用場景中所有預覽無人機的物理與控制腳本，確保鍵盤輸入完全不影響它們
    /// </summary>
    private void DisableAllDroneScripts()
    {
        foreach (var option in charOptions)
        {
            if (option.droneModel != null)
            {
                // 【新增】凍結 Rigidbody 物理，防止重力讓無人機掉落
                var rb = option.droneModel.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                // 關閉 CharacterController 避免重力影響與物理碰撞
                var cc = option.droneModel.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;

                // 關閉控制腳本，杜絕鍵盤輸入偵測
                var controller = option.droneModel.GetComponent<DroneController1>();
                if (controller != null) controller.enabled = false;

                // 關閉 UDP 接收
                var udp = option.droneModel.GetComponent<UdpReceiver>();
                if (udp != null) udp.enabled = false;
            }
        }
    }

    private void UpdateInfo()
    {
        // 1. 先行確保所有無人機腳本與物理被關閉
        DisableAllDroneScripts();

        for (int i = 0; i < charOptions.Length; i++)
        {
            bool isSelected = (i == index);

            // 更新 UI Toggle 狀態
            if (charOptions[i].toggle != null)
            {
                charOptions[i].toggle.isOn = isSelected;
            }

            // 切換虛擬相機優先級
            if (charOptions[i].vCam != null)
            {
                charOptions[i].vCam.Priority.Enabled = isSelected;
            }

            // 控制無人機顯示/隱藏與位置
            if (charOptions[i].droneModel != null)
            {
                charOptions[i].droneModel.SetActive(isSelected);

                // 如果是被選中的無人機，將其定位在展示點並加上指定旋轉角
                if (isSelected && _previewAnchor != null)
                {
                    charOptions[i].droneModel.transform.position = _previewAnchor.position;
                    Quaternion targetRotation = _previewAnchor.rotation * Quaternion.Euler(_previewRotationOffset);
                    charOptions[i].droneModel.transform.rotation = targetRotation;
                }
            }
        }

        // 【修正】移除 .Datas.Count，直接透過 GetPlayerData 取得資料
        if (_playerDB != null)
        {
            var data = _playerDB.GetPlayerData(index);
            if (_textName != null) _textName.text = data.name;
            if (_textDesc != null) _textDesc.text = data.desc;
       
        }
    }

    public void EnterStage()
    {
        _playerDB.selectedIndex = index;
        GameManager1.playerIndex = index;
        GameObject selectedDrone = _playerDB.GetPlayerData(index).playerObj;
        LevelCatalog.SelectedDroneCanBomb = selectedDrone != null && selectedDrone.GetComponentInChildren<DroneBombDropper>(true) != null;

        // 進入關卡前先選擇遊戲模式 (競速 / 搬運 / 打擊) 與關卡
        Canvas canvas = _textName != null ? _textName.canvas : FindFirstObjectByType<Canvas>();
        ModeSelectPanel.Open(canvas, _textName != null ? _textName.font : null);
    }

    public void NextDrone()
    {
        index++;
        UpdateInfo();
    }

    public void PreDrone()
    {
        index--;
        UpdateInfo();
    }
}
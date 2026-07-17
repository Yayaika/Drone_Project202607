using System;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
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

    private void Start()
    {
        // 初始時先關閉所有無人機腳本
        DisableAllDroneScripts();
        UpdateInfo();
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
                // 關閉 CharacterController 避免重力影響與物理碰撞
                var cc = option.droneModel.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;

                // 關閉控制腳本，杜絕鍵盤輸入偵測
                var controller = option.droneModel.GetComponent<DroneController>();
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

            // 切換虛擬相機優先級 (VR 模式中如不需要可忽略)
            if (charOptions[i].vCam != null)
            {
                charOptions[i].vCam.Priority.Enabled = isSelected;
            }

            // 控制無人機顯示/隱藏與位置
            if (charOptions[i].droneModel != null)
            {
                charOptions[i].droneModel.SetActive(isSelected);

                // 如果是被選中的無人機，將其定位在展示點並加上我們指定的傾斜旋轉角
                if (isSelected && _previewAnchor != null)
                {
                    charOptions[i].droneModel.transform.position = _previewAnchor.position;

                    // 計算結合展示點旋轉與自訂偏移量
                    Quaternion targetRotation = _previewAnchor.rotation * Quaternion.Euler(_previewRotationOffset);
                    charOptions[i].droneModel.transform.rotation = targetRotation;
                }
            }
        }

        if (_playerDB != null)
        {
            _textName.text = _playerDB.GetPlayerData(index).name;
            _textDesc.text = _playerDB.GetPlayerData(index).desc;
        }
    }

    public void EnterStage()
    {
        _playerDB.selectedIndex = index;
        GameManager1.playerIndex = index;
        GameManager1.LoadScene("SampleScene");
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
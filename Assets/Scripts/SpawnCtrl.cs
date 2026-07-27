using UnityEngine;

public class SpawnCtrl : MonoBehaviour
{
    #region 基礎元件
    [SerializeField] private PlayerDB _playerDB;

    [Header("場景現有腳本")]
    public CourseBuilder courseBuilder;
    public DroneHUD droneHUD;             // 稍後會在下面提供 DroneHUD 的相容修改方式
    public VRCameraFollow vrCameraFollow;
    public GameManager gameManager;
    #endregion 基礎元件

    private int PlayerIndex => _playerDB.selectedIndex;

    void Awake()
    {
        PrepareDrone();
    }

    private void PrepareDrone()
    {
        if (_playerDB == null)
        {
            Debug.LogError("SpawnCtrl 沒有設定 PlayerDB！");
            return;
        }

        for (int i = 0; i < _playerDB.datas.Length; i++)
        {
            GameObject droneGo = _playerDB.datas[i].playerObj;
            if (droneGo == null) continue;

            if (i == PlayerIndex)
            {
                // 1. 啟用並傳送選中的無人機
                droneGo.SetActive(true);
                droneGo.transform.position = transform.position;
                droneGo.transform.rotation = transform.rotation;

                // 2. 對接基礎場景腳本
                if (courseBuilder != null) courseBuilder.drone = droneGo;
                if (vrCameraFollow != null) vrCameraFollow.target = droneGo.transform;
                if (gameManager != null) gameManager.droneTransform = droneGo.transform;

                // 3. 偵測並初始化對應的飛控
                DroneController oldCtrl = droneGo.GetComponent<DroneController>();
                DroneController1 newCtrl = droneGo.GetComponent<DroneController1>();

                if (oldCtrl != null)
                {
                    Debug.Log($"【{_playerDB.datas[i].name}】已啟用，使用【舊版飛控】。");
                    if (Camera.main != null) oldCtrl.cameraTransform = Camera.main.transform;

                    // 指派舊飛控給 HUD
                    if (droneHUD != null) droneHUD.SetDrone(oldCtrl);
                }
                else if (newCtrl != null)
                {
                    Debug.Log($"【{_playerDB.datas[i].name}】已啟用，使用【新版飛控】。");
                    newCtrl.isArmed = true; // 啟動新版飛控引擎

                    // 指派新飛控給 HUD
                    if (droneHUD != null) droneHUD.SetDrone(newCtrl);
                }
            }
            else
            {
                // 停用未選中的無人機
                droneGo.SetActive(false);
            }
        }
    }
}
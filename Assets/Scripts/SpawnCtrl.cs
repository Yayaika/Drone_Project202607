using TMPro;
using UnityEngine;

public class SpawnCtrl : MonoBehaviour
{
    #region 基础元件
    [SerializeField] private PlayerDB _playerDB;

    [Header("場景現有腳本（在 Inspector 拖入，不新增額外邏輯變數）")]
    public CourseBuilder courseBuilder;
    public DroneHUD droneHUD;
    //public CameraFollow cameraFollow;
    public VRCameraFollow vrCameraFollow; // 修正：改用 VRCameraFollow 
    public GameManager gameManager;
    #endregion 基础元件

    #region 公用参数

    // 🛠️ 修正：直接讀取 PlayerDB 裡面的選取編號
    private int PlayerIndex => _playerDB.selectedIndex;
    // 🛠️ 確保向 PlayerDB 請求正確的無人機
    private DroneController CurrentPlayer => _playerDB.GetPlayerData(PlayerIndex).playerCtrl;
    #endregion 公用参数

    void Awake()
    {
        Spawn();
    }

    private void Spawn()
    {
        // 防呆機制：確保資料庫存在
        if (_playerDB == null)
        {
            Debug.LogError("SpawnCtrl 沒有設定 PlayerDB！");
            return;
        }
        // 1. 執行你原本的動態生成
        DroneController spawnedDrone = Instantiate(CurrentPlayer, transform.position, transform.rotation);

        // 2. 自動指派給無人機需要的相機（解決 cameraTransform 的 Null 錯誤）
        if (Camera.main != null)
        {
            spawnedDrone.cameraTransform = Camera.main.transform;
        }

        // 3. 將生成出來的實體，直接對接填入現有的場景腳本欄位中
        if (courseBuilder != null) courseBuilder.drone = spawnedDrone.gameObject;
        if (droneHUD != null) droneHUD.drone = spawnedDrone;
        //if (cameraFollow != null) cameraFollow.target = spawnedDrone.transform;
        if (vrCameraFollow != null) vrCameraFollow.target = spawnedDrone.transform; // 🛠️ 修正：改用 vrCameraFollow
        if (gameManager != null) gameManager.droneTransform = spawnedDrone.transform;
    }
}
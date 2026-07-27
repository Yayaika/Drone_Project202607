using UnityEngine;

public class SpawnCtrl : MonoBehaviour
{
    #region 基礎元件
    [SerializeField] private PlayerDB _playerDB;

    [Header("場景現有腳本")]
    public CourseBuilder courseBuilder;
    public DroneHUD droneHUD;
    public VRCameraFollow vrCameraFollow;
    public GameManager gameManager;
    #endregion 基礎元件

    private int PlayerIndex => _playerDB.selectedIndex;

    void Awake()
    {
        if (_playerDB == null)
        {
            Debug.LogError("SpawnCtrl 沒有設定 PlayerDB！");
            return;
        }

        // 1. 同步從選單傳遞過來的玩家選擇 (GameManager1.playerIndex)
        _playerDB.selectedIndex = GameManager1.playerIndex;

        PrepareDrone();
    }

    private void PrepareDrone()
    {
        int activeIndex = PlayerIndex;

        // 🌟 防呆機制：若索引超出範圍，強制退回 0
        if (activeIndex < 0 || activeIndex >= _playerDB.datas.Length)
        {
            Debug.LogWarning($"選中的無人機索引 {activeIndex} 超出範圍，已自動退回使用預設【無人機 1】(Index 0)！");
            activeIndex = 0;
            _playerDB.selectedIndex = 0;
            GameManager1.playerIndex = 0;
        }

        GameObject selectedTarget = _playerDB.datas[activeIndex].playerObj;

        if (selectedTarget == null)
        {
            Debug.LogError($"索引 {activeIndex} 的無人機物件在資料庫中為空 (Null)，無法啟用/生成！");
            return;
        }

        GameObject activeDroneInstance = null;

        // 🌟 2. 核心判斷：選中的物件是「場景中的現有實體」還是「專案預置物 Prefab」
        bool isPrefab = (selectedTarget.scene.name == null);

        // 算出一點點安全的高度的起點位置 (向上抬高 0.5 ~ 1.0m，避免卡在地板裡面)
        Vector3 safeSpawnPos = transform.position + Vector3.up * 0.8f;

        if (isPrefab)
        {
            // A 方案：如果是 Prefab 檔案，我們直接在 Spawner 起點動態生成它
            activeDroneInstance = Instantiate(selectedTarget, safeSpawnPos, transform.rotation);
            Debug.Log($"【{_playerDB.datas[activeIndex].name}】為專案預置物 (Prefab)，已動態生成實體於場景。");
        }
        else
        {
            // B 方案：如果是場景中已存在的實體，直接啟用並傳送定位至起點
            activeDroneInstance = selectedTarget;

            // 【安全修正】1. 先關閉 CharacterController 避免被地形擠入地下
            CharacterController cc = activeDroneInstance.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            activeDroneInstance.SetActive(true);
            activeDroneInstance.transform.position = safeSpawnPos;
            activeDroneInstance.transform.rotation = transform.rotation;

            // 【安全修正】2. 重新啟用 CharacterController
            if (cc != null) cc.enabled = true;

            Debug.Log($"【{_playerDB.datas[activeIndex].name}】為場景實體，已直接啟用並傳送至起點。");
        }

        // 🌟 3. 重置物理速度（防止殘留向下重力速度導致直接掉落穿地）
        if (activeDroneInstance != null)
        {
            Rigidbody rb = activeDroneInstance.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        // 🌟 4. 停用場景中其他未選中的「實體無人機」（避免物理與控制干擾）
        for (int i = 0; i < _playerDB.datas.Length; i++)
        {
            GameObject otherDrone = _playerDB.datas[i].playerObj;
            if (otherDrone == null || i == activeIndex) continue;

            // 確保只有當該無人機是「場景物件」時才關閉，避免修改到資料夾裡的 Prefab 檔案
            if (otherDrone.scene.name != null)
            {
                otherDrone.SetActive(false);
            }
        }

        // 🌟 5. 將正在運作的無人機對接填入各個場景腳本的欄位中
        if (activeDroneInstance != null)
        {
            if (courseBuilder != null) courseBuilder.drone = activeDroneInstance;
            if (vrCameraFollow != null) vrCameraFollow.target = activeDroneInstance.transform;
            if (gameManager != null) gameManager.droneTransform = activeDroneInstance.transform;

            // 6. 偵測飛控腳本並完成初始化與 HUD 對接
            DroneController oldCtrl = activeDroneInstance.GetComponent<DroneController>();
            DroneController1 newCtrl = activeDroneInstance.GetComponent<DroneController1>();

            if (oldCtrl != null)
            {
                Debug.Log("初始化完成：使用舊版飛控 (DroneController)");
                if (Camera.main != null) oldCtrl.cameraTransform = Camera.main.transform;
                if (droneHUD != null) droneHUD.SetDrone(oldCtrl);
            }
            else if (newCtrl != null)
            {
                Debug.Log("初始化完成：使用新版飛控 (DroneController1)");
                newCtrl.isArmed = true; // 啟動新飛控引擎
                if (droneHUD != null) droneHUD.SetDrone(newCtrl);
            }
            else
            {
                Debug.LogWarning("注意：生成的無人機上找不到任何 DroneController 或 DroneController1 飛控腳本！");
            }
        }
    }
}
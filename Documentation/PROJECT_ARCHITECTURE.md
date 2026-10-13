# 專案架構（Project Architecture）

> 2026-10-12 盤點。只記錄已在程式碼中確認的內容。

## 1. 專案環境

- Unity 版本：6000.0.58f1
- Render Pipeline：URP 17.0.4
- Input System：新版 Input System 1.14.2（同時啟用舊版 Input Manager；`OptionsMenu`、`VRHUDFollower` 仍使用舊 API）
- 主要平台：PC Standalone；XR（OpenXR、XR Interaction Toolkit 3.0.11）
- 版本控制：Git（GitHub `Yayaika/Drone_Project202607`），`core.autocrlf=true`
- 重要套件：DOTween（`Assets/Plugins/Demigiant`，已定義 `DOTWEEN`）、Cinemachine 3.1.6、Splines、AI Navigation、Funplay Unity MCP

## 2. 目錄結構

| 路徑 | 用途 | 備註 |
|---|---|---|
| `Assets/Script_1/` | 目前使用中的無人機核心：`DroneController1`、`DroneFlightHUD`、`DronePickupHandler`、`DroneCargo`、`VRHUDFollower` | 掛在兩個無人機 Prefab 上 |
| `Assets/Scripts/` | 遊戲流程：`GameManager`、`SpawnCtrl`、`CourseBuilder`、選單/UI | |
| `Assets/Scripts/Gameplay/` | 新增：關卡目錄、場景切換、排行榜、搬運任務、導航目標、執行時材質 | |
| `Assets/Scripts/UI/` | UI：選無人機、`UIPanelCtrl`、新增 `RuntimeUI`/`UITween`/`UIButtonTweenFx`/`ModeSelectPanel`/`ResultScreen` | |
| `Assets/Scripts/Level/` | 新增：`CityGenerator` 程序化城市；`OrganicCityGenerator` 有機城市（獨立測試，與關卡無相依） | |
| `Assets/Scripts/Audio/` | 新增：`GameAudio` 音效服務 | |
| `Assets/Scripts/Editor/` | 新增：`DroneGameSetupMenu`（選單「無人機遊戲/…」） | |
| `Assets/Mapscript/` | City2 的 Spline 賽道：`RaceManager`(`RaceManger.cs`)、`GateTrigger`(`Cricle.cs`) | |
| `Assets/Prefabs/` | 無人機、貨物、Spawner、閘門 Prefab | |
| `Assets/Download/` | 第三方素材（城市包、無人機模型、天空盒、準星） | 不要直接修改 |
| `Assets/Resources/Audio/` | 遊戲音效（依檔名載入） | 用選單 3 建立 |

## 3. 主要場景

| 場景名稱 | 用途 | 入口／切換方式 | 狀態 |
|---|---|---|---|
| StartMenu | 主選單 | Build index 0 | 已存在 |
| PlayerSelectSettings | 選無人機 → 開啟模式/關卡面板 | `UIPlayerSelectCtrl.EnterStage` | 已擴充 |
| SampleScene | 訓練場（`CourseBuilder` 隨機穿圈） | 關卡目錄 | 已存在 |
| City2 | 手工城市＋Spline 賽道 | 關卡目錄 | 已存在 |
| CityProcedural | 程序化城市範本（執行時生成，測試用） | 選單 1 從 SampleScene 複製建立 | 已存在；關卡目錄不再直接使用 |
| CityResidential / CityDowntown | 由 CityProcedural 烘焙的固定關卡（住宅區 / 市中心） | 關卡目錄；選單 7 重新烘焙（會覆蓋手動修改） | 已存在 |
| OrganicCityTest | 有機城市生成器測試（自由飛行相機） | 無（未加入 Build Settings / LevelCatalog） | 選單 4 可重建 |
| NextSecondScene、DronePickupTest、SceneTestDrone、PlayerSetting2 | 測試用 | — | 未納入流程 |

## 4. 主要程式與責任

| 類別／元件 | 職責 | 依賴關係 | 注意事項 |
|---|---|---|---|
| `DroneController1` | 飛行物理、輸入、相機切換、防吸附、遙測、重生 | Rigidbody、XR Origin、`cameraPositions` | `canControl` 由 GameManager 控制（倒數/結算時關閉） |
| `DroneFlightHUD` | 執行時建立 OSD | `DroneController1` 遙測、`DronePickupHandler`、`NavigationTarget` | 掛在 Drone_HUD_Canvas；TPV 時 Canvas 會被移出無人機階層 |
| `DronePickupHandler` / `DroneCargo` | 抓取/放下、代理碰撞體、耐久與事件 | DOTween | 載貨時貨物碰撞體關閉，改由 `PayloadProxyCollider` 代表 |
| `GameManager` | 倒數、計時、檢查點進度、重生點、結算、排行提交 | `LevelCatalog`、`ResultScreen`、`LeaderboardStore` | 場景單例 `Instance`；保留原本所有序列化欄位 |
| `SpawnCtrl` | 依 `GameManager1.playerIndex` 生成無人機 | `PlayerDB` | 未修改 |
| `LevelCatalog`（static） | 關卡清單、目前模式/關卡、下一關 | `SceneLoader` | 新增關卡在 `All` 加一筆 |
| `DeliveryMissionManager` | 搬運訂單流程與計分 | `IDeliverySpotProvider`、`DeliveryZone`、`GameManager` | 搬運模式下由 CityGenerator 或 GameManager 自動建立 |
| `CityGenerator` | 程序化城市、起飛廣場、屋頂/街道降落點、競速路線；`TaiwanStreets` 部分類別負責台灣街景（標線、騎樓街屋、招牌、路燈、停放機車） | `CourseBuilder.CreateCheckpointRing`、`CityMeshBuilder` | `DefaultExecutionOrder(-200)`，需早於 `SpawnCtrl.Awake`；Play 時在 Awake 建立車流與路人 |
| `CityTraffic` | 紅綠燈時相與燈號、汽車/機車車道、跟車、停等、左轉讓行、轉彎禮讓行人 | `CityGrid`（路網資訊）、`CityPedestrians.Instance` | 掛在 `GeneratedCity` 根物件，單一元件統一更新所有車輛 |
| `CityPedestrians` | 路人沿人行道繞行、等行人綠燈過馬路、登記斑馬線佔用 | `CityTraffic.Instance` | 同上；停用 CityPeople 自帶的隨機動畫腳本 |
| `CityMeshBuilder` | 把方塊/柱體依材質合併成網格，世界座標投影 UV | — | 世界座標建構 |
| `DistanceCuller` | 依相機距離切換路人/車輛的 Renderer 與 Animator | — | 由 CityTraffic / CityPedestrians 使用 |
| `EnvironmentController` | 晝夜、天氣、雨、閃電、風力、夜間前燈 | `EnvironmentProfile`（Resources）、`DroneController1` 讀取 `Wind` | 自動加入有 GameManager 的場景 |
| `SceneLoader` | 非同步載入 + Loading 畫面（Screen Space - Camera） | `RuntimeUI` | 跨場景常駐，所有場景切換都應呼叫 `SceneLoader.Load` |
| `MenuSceneDecor` | 選單場景動態背景、巡航無人機、展示台 | `UIPlayerSelectCtrl`、`CityGenerator`（背景城市） | 選單 6 佈置 |
| `CityLevelBaker`（Editor） | 烘焙固定關卡、填入 LowpolyStreetPack 素材 | `CityGenerator`、`LevelCatalog` | 選單 7 |
| `GameAudio`（static） | SFX/BGM 播放 | `Resources/Audio` | 缺檔靜音並記錄 |

## 5. 系統關係

- 啟動流程：StartMenu → PlayerSelectSettings →（EnterStage）`ModeSelectPanel` → `LevelCatalog.Load` → `SceneLoader`（DOTween 淡出入）→ 關卡場景
- 關卡內：`SpawnCtrl.Awake` 生成無人機 → `GameManager.Start` 等一幀 → 倒數（`canControl=false`）→ GO → `OnRaceStarted` 事件（搬運任務在此開始）
- 競速：`RingTrigger`/`CheckpointRing`/`RaceManager` → `GameManager.CheckpointPassed(index, transform)` → 全數通過 → `CompleteLevel`
- 搬運：`DeliveryMissionManager` → `GameManager.SetProgress / ShowCenterMessage / CompleteLevel`
- 結算：`GameManager.CompleteLevel` → `LeaderboardStore.Submit` → `ResultScreen.Show`（建立在無人機 HUD Canvas，VR 可見）
- HUD 導航：規則腳本呼叫 `NavigationTarget.Set(...)`；換場景時自動清除
- UI 動態：`UIButtonTweenFx` 在每次載入場景時自動掛到所有 `Button`
- 資料儲存：PlayerPrefs（`MasterVolume`、`Leaderboard_<關卡Id>_<模式>`）

## 6. 資料與資產規則

- ScriptableObject：`PlayerDB`（無人機清單）
- 新功能的 UI/HUD 皆以程式建立，**不需修改既有 Prefab/場景**；唯一的 Prefab 修改是指定起飛音效
- `.meta`：新腳本的 .meta 由 Unity 產生；`Leaderboard.cs` 曾改名為 `LeaderboardStore.cs`（非 MonoBehaviour，無引用）

## 7. 外部套件與服務

| 名稱 | 用途 | 是否必要 | 版本／設定位置 |
|---|---|---|---|
| DOTween | UI 與物件補間動畫 | 是（新 UI 依賴） | `Assets/Plugins/Demigiant/DOTween`、`Resources/DOTweenSettings` |
| XR Interaction Toolkit / OpenXR | VR 操作 | 是 | `Packages/manifest.json` |
| Funplay Unity MCP | Editor 自動化 | 否 | `com.gamebooom.unity.mcp` |

## 8. 架構限制與已知風險

- 有兩套飛控：`DroneController`（舊，未在主要流程使用）與 `DroneController1`（使用中）
- `GameManager1` 是靜態類別（只存 playerIndex），與 `GameManager` 名稱相近容易混淆
- `SceneLoader` 的淡出畫面是 Screen Space Overlay，VR 頭顯中看不到（切換仍正常）
- 執行時 `Shader.Find` 依賴 URP Lit/Unlit 被包含在建置中

## 9. 修改前應檢查的依賴

- `DroneController1`：`DroneFlightHUD`、`DronePickupHandler`、`GameManager`、`RaceManager`、`SpawnCtrl`、`UIPlayerSelectCtrl`、兩個無人機 Prefab 的序列化欄位
- `GameManager`：SampleScene/City2/PlayerSelectSettings 中的序列化欄位、`CourseBuilder`、`RaceManager`、`DeliveryMissionManager`、`CityGenerator`
- `LevelCatalog`：`ModeSelectPanel`、`GameManager`、`CityGenerator`、`DroneGameSetupMenu`

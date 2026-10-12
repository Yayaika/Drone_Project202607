# 重要決策紀錄（Decision Log）

> 記錄會影響後續實作的選擇，避免 AI 在不同工作階段反覆改變方向。

## 記錄格式

每個決策請記錄日期、背景、決定、原因、替代方案與影響。尚未獲使用者確認的內容應標記為「提案」，不能當成正式決策。

## D-001：初始專案盤點

- **日期：** 2026-10-12
- **狀態：** 已確認
- **背景：** 需要讓 AI 先理解既有 Unity 專案，再決定後續工作。
- **決定：** 先盤點現況並更新文件，不在尚未理解架構前大規模重構。
- **原因：** 降低破壞既有功能與資產引用的風險。
- **影響：** 後續開發任務需參照 `CURRENT_STATUS.md` 與 `PROJECT_ARCHITECTURE.md`。

## D-002：新 UI/HUD 以程式執行時建立

- **日期：** 2026-10-12
- **狀態：** 已確認（實作）
- **背景：** 此工作階段沒有可用的 Unity MCP 工具，無法直接編輯場景/Prefab；兩個無人機 Prefab 結構龐大。
- **決定：** OSD、模式選擇面板、結算畫面、任務區域皆由程式建立（`RuntimeUI`），不修改既有 Prefab 階層。
- **原因：** 避免手改 YAML 破壞引用；兩台無人機自動共用同一套 UI。
- **替代方案：** 在 Prefab 中手工擺放 UI（較好微調，但需 Editor 操作）。
- **影響範圍：** 版面位置以程式數值控制（`DroneFlightHUD` 的 `osdScale`、`sideColumnOffset`、`topBottomOffset`）。

## D-003：第三人稱鏡頭只跟隨航向

- **日期：** 2026-10-12
- **狀態：** 已確認（使用者回報傾斜造成暈眩）
- **決定：** TPV 鏡頭掛在「只跟隨航向、平滑跟隨」的世界座標支架；FPV 維持真實傾斜，可用 `fpvHorizonStabilization` 調整。
- **相關檔案：** `Script_1/DroneController1.cs`

## D-004：遊戲模式與關卡以靜態目錄管理

- **日期：** 2026-10-12
- **狀態：** 已確認（實作）
- **決定：** `LevelCatalog` 以程式清單定義關卡；同一個 `CityProcedural` 場景依種子/規模參數組成多個關卡。直接從 Editor 開啟關卡場景時預設為競速模式。
- **替代方案：** ScriptableObject 關卡資料（之後若關卡變多可改用）。

## D-005：排行榜存在本機

- **日期：** 2026-10-12
- **狀態：** 提案（使用者未指定線上/本機）
- **決定：** PlayerPrefs JSON，每關每模式前 10 名；玩家名稱暫以無人機名稱代替。

## D-006：中文字型統一為 YuPearl-Medium SDF 1

- **日期：** 2026-10-12
- **狀態：** 已被 D-008 取代
- **背景：** 舊的「YuPearl-Medium SDF」缺字（Console 出現 `回 was not found` 等警告），中文顯示成方塊。
- **決定：** 場景/Prefab 中所有舊 SDF 引用改為 SDF 1；會在執行時顯示中文的 `ProgressText`、`[Text]Finish` 由 LiberationSans 改為 SDF 1；TMP Settings 全域後備字型加入 SDF 1；程式建立的 UI 一律使用 SDF 1（`RuntimeUI.ChineseFontName`）。
- **未變更：** 只顯示英文/數字的 HUD 文字仍用 LiberationSans；舊 SDF 資產檔保留未刪除。
- **相關檔案：** 兩個無人機 Prefab、`鍵盤控制Canvas.prefab`、`StartMenu.unity`、`PlayerSetting2.unity`、`TMP Settings.asset`、`RuntimeUI.cs`、`DroneFlightHUD.cs`

## D-007：面板優化與 placeholder 音效以執行時程式處理

- **日期：** 2026-10-12
- **狀態：** 已確認（使用者要求優化面板動畫、文字圖示與按鈕音效）
- **決定：**
  - `PanelEnhancer` 在每次載入場景時，依物件名稱（`[Panel]設置`、`操作介紹面板`、`介紹面板`、主選單 `SettingPanel`）在執行時優化面板，不修改 Prefab/場景。
  - `UIPopupPanel` 為以 `SetActive` 開關的面板加上彈出/收合動畫，並以反射讀取按鈕的 Inspector 事件，把「SetActive(false)」改接成動畫收合。
  - 操作說明集中在 `ControlsHelp`（鍵盤＋手把），按鍵圖示使用 TMP Sprite Asset「all input icons same size」，TMP Settings 的 Default Sprite Asset 已改為它（原為 EmojiOne，專案中沒有使用 emoji）。
  - `GameAudio` 缺音效檔時改用 `SynthSfx` 合成的 placeholder 音效；BGM 仍需真實檔案。
- **替代方案：** 以 Editor 工具把 UI 直接寫進 Prefab（可在 Inspector 微調，但需能操作 Editor）。
- **影響：** 版面數值寫在 `PanelEnhancer` / `ControlsHelp`；若 Prefab 中的物件改名，對應優化會失效（不會報錯）。

## D-008：中文字型改回統一使用 YuPearl-Medium SDF

- **日期：** 2026-10-12
- **狀態：** 已確認（使用者要求，取代 D-006）
- **背景：** 使用者確認「YuPearl-Medium SDF」字元較完整。實測兩個字型皆為 Dynamic、同一 TTF；缺字主因是單張 1024² atlas（90pt、padding 9）約 125 字即塞滿，之後動態新增字元失敗而顯示方塊（例如 `名單` 的 教、學、陳）。
- **決定：** 場景/Prefab/TMP Settings 中所有 SDF 1（含其 Atlas Material）引用改為 SDF；`RuntimeUI.ChineseFontName` 改為 `YuPearl-Medium SDF`；SDF 開啟 Multi Atlas Textures，atlas 滿了會自動新增一張。
- **未變更：** 只顯示英文/數字的 LiberationSans 文字不動；SDF 1 資產檔保留未刪除（已無場景/Prefab 引用）。
- **相關檔案：** `Drone_Bomb.prefab`、`Drone_Parent Variant.prefab`、`鍵盤控制Canvas.prefab`、`CityProcedural`／`DronePickupTest`／`PlayerSelectSettings`／`PlayerSetting2`／`SampleScene`／`StartMenu.unity`、`TMP Settings.asset`、`YuPearl-Medium SDF.asset`、`RuntimeUI.cs`

## D-009：有機城市生成器以獨立場景測試

- **日期：** 2026-10-12
- **狀態：** 已確認（使用者要求「獨立功能測試，不修改其他關卡」）
- **背景：** 既有 `CityGenerator` 為棋盤格，街區四四方方不自然。
- **決定：** 新增 `OrganicCityGenerator`（道路生長演算法：主幹道自圓環向外彎曲延伸、支路/巷道分岔並在碰到其他道路時接合成環、不規則環狀道路、Chaikin 平滑；建築沿道路切線排列並以縮小重試填滿街廓；噪聲公園與市中心高樓分布），放在獨立場景 `OrganicCityTest`，不加入 Build Settings 與 `LevelCatalog`，`CityGenerator` 與既有關卡完全未修改。
- **取捨：** 生成內容會存進場景（約 7.6 MB）。
- **更新（同日，使用者要求採用 POLYGON city pack）：** 先前判斷「模型尺寸極小」是誤判——FBX 根節點帶 X 270° 旋轉，被程式覆寫後模型倒地才造成假的縮放。現改為外層物件負責朝向/縮放、模型保留自身旋轉；依目標高度挑選模型並以 0.65–1.1 倍等比放置（垂直最多拉伸 1.25 倍）；以「門」子網格法線自動偵測模型正面朝向道路；素材包 FBX 材質皆為預設灰，改在實例上依子網格名稱套用素材包 `Materials/Colors_buildings`、`color_windows`，招牌子網格（`Texture*`）套用 `Materials/Material_meshs` 的貼圖材質。**素材包資產本身未修改。** 碰撞改用 BoxCollider（MeshCollider 烘焙過慢）。
- **注意：** 既有 `CityGenerator.cs` 也以 `Quaternion.Euler(0, yaw, 0)` 覆寫模型旋轉，同樣會讓素材包模型倒地（本次未修改，待使用者決定）。
- **相關檔案：** `Level/OrganicCityGenerator.cs`、`Level/OrganicCityFlyCamera.cs`、`Editor/OrganicCityGeneratorEditor.cs`、`Scenes/OrganicCityTest.unity`、`Materials/OrganicCity/*.mat`

## D-010：有機城市的街道設施（LowpolyStreetPack）與中央圓環

- **日期：** 2026-10-12
- **狀態：** 已確認（使用者要求移除中央圓形區域、加入 LowpolyStreetPack 設施並可自行添加）
- **決定：**
  - 中央圓環（原為起飛廣場）改為 `centralPlaza` 開關，預設關閉；關閉時主幹道自中心點放射。
  - 新增 `OrganicCityGenerator.StreetProps.cs`：以「設施規則」陣列放置設施，放置方式有「沿人行道／路口轉角／公園內」，可設間距、機率、人行道位置、朝向、旋轉修正、縮放、間隔與適用道路等級；選單/按鈕填入 15 條預設規則（路燈、紅綠燈、路牌、電線桿、行道樹、消防栓、長椅、郵筒等）。重新指定素材時不覆蓋已有規則，另有「重設為預設值」按鈕。
  - 樹木改用 LowpolyStreetPack `Foliage/Trees`（`Trees_Big` 資料夾的 Prefab 網格遺失，已排除）。
  - LowpolyStreetPack 材質全為 Built-in 著色器（URP 下呈洋紅色）：在 `Assets/Materials/OrganicCity/LowpolyStreetPack/` 建立 URP Lit 副本，生成時以材質替換表套用。**素材包本身未修改。**
  - 預設移除設施內建 Light（數百盞即時光源）並隱藏路燈光錐模型，可在 Inspector 關閉此行為。
- **附註：** POLYGON city pack 有 24 個材質檔出現 `_Color` 由 1.0 變為 0.8 的差異，是先前字型任務呼叫 `AssetDatabase.SaveAssets()` 時一併寫出 Unity 自動同步的舊屬性；URP 以 `_BaseColor` 渲染，外觀不變。是否還原由使用者決定。

## 後續決策

新增決策時複製以下格式：

### D-XXX：決策標題

- **日期：**
- **狀態：** 提案／已確認／已取代
- **背景：**
- **決定：**
- **原因：**
- **替代方案：**
- **影響範圍：**
- **相關檔案：**

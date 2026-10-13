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
- **注意：** 既有 `CityGenerator.cs` 原本也有同樣問題，已於 D-012 修正。
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

## D-011：有機城市地面改用 LowpolyStreetPack 貼圖

- **日期：** 2026-10-13
- **狀態：** 已確認（使用者要求）
- **決定：** 道路 `pavement_03`、人行道 `pavement_01`、圓環廣場 `pavement_02`、城外地面與公園 `grass.png`（地面略暗略黃以區分公園），材質為 `Assets/Materials/OrganicCity/` 下的 URP Lit（`Road_Asphalt`、`Sidewalk_Tiles`、`Plaza_Tiles`、`Ground_Grass`、`Park_Grass`）。道路、人行道、草地網格改用世界座標 UV，貼圖大小由 `roadTextureSize`／`sidewalkTextureSize`／`grassTextureSize` 調整；地面由放大的 Cube 改為平面網格 + BoxCollider。素材包貼圖與匯入設定未修改。

## D-012：修正關卡城市 (CityGenerator) 的素材包建築

- **日期：** 2026-10-13
- **狀態：** 已確認（使用者要求修正原本遊戲場景內的建築城市）
- **背景：** `CityGenerator` 以 `Euler(0, yaw, 0)` 覆寫模型根節點的 X 270° 旋轉，模型倒地後被縮放 41–150 倍，且材質全為灰色；路邊樹籬/圍欄同樣倒地。
- **決定：** 只修正建築與路邊道具的擺放與配色，**棋盤路網、競速路線、搬運降落點與關卡參數不變**。抽出共用的 `PolygonCityModels`（量測、正面偵測、依子網格套配色），`CityGenerator` 與 `OrganicCityGenerator` 共用；編輯器端 `PolygonCityPackAssets` 共用配色填入。建築改為外層物件負責朝向/縮放、模型保留自身旋轉，等比縮放限制 0.55–1.1，縮不進地塊改用其他模型或方塊大樓；`CityProcedural.unity` 已填入配色並將 `maxVerticalStretch` 由 2 改為 1.3。
- **影響：** 同一關卡種子的建築挑選與擺放會與修正前不同（隨機序列改變）；道路與競速路線不受影響。
- **相關檔案：** `Level/CityGenerator.cs`、`Level/PolygonCityModels.cs`、`Level/OrganicCityGenerator.cs`、`Editor/PolygonCityPackAssets.cs`、`Editor/OrganicCityGeneratorEditor.cs`、`Editor/DroneGameSetupMenu.cs`、`Scenes/CityProcedural.unity`

## D-013：關卡城市 (CityProcedural) 改為台灣街景並加入車流與路人

- **日期：** 2026-10-13
- **狀態：** 已確認（使用者要求「目前可遊玩的城市場景」改成符合現實、類似台灣街道，並有汽車與路人的合理移動）
- **背景：** 可遊玩的城市關卡是 `CityProcedural`（住宅區、市中心兩關，執行時由 `CityGenerator` 生成）。`City2`（手工城市）與 `OrganicCityTest` 未變動。
- **決定：**
  - 保留棋盤路網、街區尺寸、起飛廣場、競速路線與搬運降落點的產生方式，只替換街景內容；`CityGenerator.taiwanStreetStyle` 關閉即恢復舊版。
  - 道路：雙黃線、車道虛線、斑馬線、停止線、機車停等區、路口紅線、路緣黃線；靠右行駛，每向兩車道（內側 1.5m 直行/左轉、外側 4.4m 直行/右轉）。
  - 建築：一般地塊改為沿街 L 形連棟街屋（騎樓、鐵窗／陽台／華廈玻璃帶、冷氣機、直立與橫式中文招牌、頂樓女兒牆、水塔、鐵皮加蓋，中庭鐵皮屋，地塊間隙即防火巷）；目標樓高 > 26m 的市中心地塊有 70% 機率仍用素材包大樓。屋頂前半部加入搬運降落點。
  - 街道設施：路燈、行道樹（住宅區較多）；沒有建築的空地 60% 改為平面停車場（停放車輛同樣使用提供的汽車素材，含收費亭）；兩時相紅綠燈（綠 14s、黃 3s、全紅 1.5s，各路口錯開）與行人燈（剩 5 秒閃爍）。
  - 車流 `CityTraffic`：**只使用使用者提供的車輛素材**（Awbmecreations 低多邊形車，排除飛機/軍車/怪獸卡車），不自行生成機車模型（專案內沒有機車素材）；跟車距離、紅燈停在停止線、黃燈兩難區判斷、左轉讓對向直行、轉彎禮讓斑馬線行人。運動學剛體，無人機可撞到。
  - 路人 `CityPedestrians`：CityPeople 模型（排除施工、義肢與道具資料夾），沿人行道繞行、在路口等行人綠燈走斑馬線、偶爾停下；15% 站在店門口。停用素材包隨機動畫腳本（會播放跳舞），改播行走/待機動畫。
  - 幾何以 `CityMeshBuilder` 合併為每街區少數網格；碰撞體為 BoxCollider。
- **原因：** 不改變關卡流程與玩法資料的前提下提高真實感；合併網格避免數萬個 GameObject。
- **替代方案：** NavMesh 路人／車輛（需烘焙且路網為執行時生成，較複雜）；改用 OrganicCityGenerator（未接入關卡）。
- **影響範圍：** 同一種子的建築與道路裝飾和舊版不同；競速路線不變。招牌文字使用 `YuPearl-Medium SDF`（動態字型）。若日後提供機車素材，可在 `CityTraffic` 加入外側車道機車與路邊停放。
- **相關檔案：** `Level/CityGenerator.cs`、`Level/CityGenerator.TaiwanStreets.cs`、`Level/CityMeshBuilder.cs`、`Level/CityTraffic.cs`、`Level/CityPedestrians.cs`、`Gameplay/RuntimeMaterials.cs`、`Editor/DroneGameSetupMenu.cs`

## D-014：選單場景動態背景與 Loading 畫面

- **日期：** 2026-10-14
- **狀態：** 已確認（使用者要求開始選單/選擇無人機場景更有趣，並有簡潔的非同步載入動畫）
- **決定：**
  - `SceneLoader` 改為非同步載入 + Loading 畫面（旋轉螺旋槳的無人機圖示、進度條、百分比、操作提示），`allowSceneActivation` 搭配最短顯示 0.9 秒；Canvas 使用 Screen Space - Camera 綁定 Main Camera，PC 與 VR 都看得到。`SceneChanger.LoadScene`、`GameManager1.LoadScene` 改走 `SceneLoader`。
  - `MenuSceneDecor`：開始選單＝晨間天空盒、遠景霧、從高樓眺望台灣街景城市、主角無人機懸浮擺動、三架帶光軌的巡航無人機；選擇場景＝屋頂停機坪（女兒牆、欄杆、水塔）、發光展示台、選中的無人機水平旋轉懸浮，原地形隱藏。選擇按鈕錯字「Selete Game」改為「開始任務」。
  - 由選單「無人機遊戲/6」一鍵佈置（只新增 `MenuDecor`、`MenuCity` 物件）。
- **相關檔案：** `Gameplay/SceneLoader.cs`、`UI/MenuSceneDecor.cs`、`Editor/MenuSceneSetup.cs`、`SceneChanger.cs`、`Manager/GameManager1.cs`、`Scenes/StartMenu.unity`、`Scenes/PlayerSelectSettings.unity`

## D-015：城市關卡烘焙為固定場景、優先使用素材、競速障礙物

- **日期：** 2026-10-14
- **狀態：** 已確認（使用者要求：關卡內容固定不要隨機、盡量使用準備的素材且材質寫實、避免長時間載入與卡頓、加入障礙物增加趣味、檢查點縮小）
- **決定：**
  - 新增選單「無人機遊戲/7. 烘焙固定關卡場景」：依 `LevelCatalog` 參數在編輯器生成一次，網格存到 `Assets/GeneratedLevels/<場景>/`、執行時建立的材質存到 `Assets/GeneratedLevels/SharedMaterials/`，產生固定場景 `CityResidential`（住宅區）與 `CityDowntown`（市中心），並烘焙兩個選單背景城市。`CityGenerator.baked = true` 時執行時不再生成，只建立車流/路人與競速檢查點；降落點、起飛點與競速路線隨場景保存。烘焙後的場景可直接在 Editor 手動調整。
  - `LevelCatalog` 兩個新城區關卡改指向固定場景；原 `CityProcedural` 保留作為範本與測試。
  - 建築：有建築的地塊 85% 使用 POLYGON city pack 建築模型（放不下改為騎樓街屋），不再使用方塊大樓。
  - 街道設施改用 LowpolyStreetPack：路燈（LampPost_D/J）、行道樹、電線桿與電線、消防栓/長椅/郵筒/電箱/報攤/電話亭、交通錐與路障、行人天橋；放置時套用 `Assets/Materials/OrganicCity/LowpolyStreetPack/*_URP.mat`、移除內建燈光與光錐。
  - 道路/人行道/草地改用有貼圖的材質（`Road_Asphalt`、`Sidewalk_Tiles`、`Ground_Grass`），`CityMeshBuilder` 以世界座標投影 UV，貼圖平鋪不拉伸。
  - 競速路線每段道路放一種固定障礙：行人天橋、跨街電線、跨街布條（中文標語）、施工塔吊（吊臂橫越道路、吊塊懸空、施工圍籬與交通錐）、綠色門架標誌。第一段不放。
  - 檢查圈半徑：城市 4.5 → 3.5m；訓練場 3.5 → 3.0m。City2 的檢查門 Prefab 未調整。
  - 路人超過 90m、車輛超過 160m 時不繪製（持續移動），分批檢查避免單幀尖峰。
- **原因：** 執行時生成會在場景啟用時卡住主執行緒（市中心生成 1.55 秒以上）；固定場景可非同步讀取且內容可手動設計。
- **限制：** 使用者提供的建築/街道素材本身是低多邊形風格，「寫實」程度受素材限制；要更寫實需要更高品質的建築與 PBR 貼圖素材。
- **相關檔案：** `Editor/CityLevelBaker.cs`、`Level/CityGenerator.cs`、`Level/CityGenerator.TaiwanStreets.cs`、`Level/CityMeshBuilder.cs`、`Level/DistanceCuller.cs`、`Level/CityTraffic.cs`、`Level/CityPedestrians.cs`、`Gameplay/LevelCatalog.cs`、`CourseBuilder.cs`、`Scenes/CityResidential.unity`、`Scenes/CityDowntown.unity`

## D-016：台北式高密度街區與 Anime Nature 植物

- **日期：** 2026-10-14
- **狀態：** 已確認（使用者要求植物寫實、所有場景都改，並偏向台灣街景：窄路、沒有人行道、很多騎樓、像台北的高密度）
- **決定：**
  - 植物一律使用 Anime Nature（專案內為 `Assets/Download/Anime Trees`，與 Asset Store 快取中的 Hipernt「Anime Nature」為同一套件）：Tree_02~08（Tree_01 約 9 萬面不使用）、Bush_01~04、Grass。沒有 LOD 的模型放置時加上 LODGroup，在畫面上很小時不繪製。
  - 城市關卡（含選單背景）：行道樹約 6.5m、口袋公園/廣場有樹、灌木與草叢、巷內盆栽。
  - 其他場景（選單「無人機遊戲/8」）：訓練場 `CourseBuilder` 樹木/灌木/草叢改用 Anime Nature；有機城市的行道樹與灌木規則改用並重新生成；City2 的 86 段低模樹籬隱藏外觀（保留碰撞）改排灌木，草地步道加草叢。
  - 街區：每個街區中央一條 5m、沒有人行道的巷子（紅黃邊線、單側電線桿與拉到對面外牆的電線、路邊停車、盆栽）；地塊面大馬路為連續騎樓街屋、面巷子為 3~7 樓公寓（35% 有騎樓）；素材包大樓機率依目標樓高 0.175~0.5，多出現在市中心高樓地塊。建築密度：住宅區 0.92、市中心 0.98。
  - 街屋外牆使用 LowpolyStreetPack `pavement_01` 磁磚貼圖 × 牆色（模擬二丁掛）。
- **限制：** Anime Nature 為動畫風格（比原本低模植物自然，但不是照片級寫實）；Asset Store 快取另有「Anime Natural Environment」「Idyllic Fantasy Nature」「Stylized Downtown Street」尚未匯入。
- **相關檔案：** `Level/CityGenerator.TaiwanStreets.cs`、`Level/CityGenerator.cs`、`CourseBuilder.cs`、`Editor/VegetationSetup.cs`、`Editor/CityLevelBaker.cs`、`Gameplay/LevelCatalog.cs`

## D-017：晝夜與天氣系統

- **日期：** 2026-10-14
- **狀態：** 已確認（使用者要求）
- **決定：** `EnvironmentController` 自動加入有 `GameManager` 的遊戲關卡（選單不加入）。預設一天 12 分鐘、從 09:30 開始；太陽由東到西、黃昏暖色、夜晚月光 + 夜空天空盒（`Resources/EnvironmentProfile.asset`，選單 9 建立）；夜間/濃霧時玩家相機有前燈。天氣晴/多雲/雨/暴風雨/濃霧每 3~6 分鐘依權重隨機轉換（20 秒漸變），雨天有跟隨相機的雨絲、暴風雨有閃電；風力（陣風）由 `DroneController1` 以加速度套用，載貨越重受影響越小。F6 快轉 3 小時、F7 切換天氣，畫面上方顯示提示。環境光改為 Trilight；天空盒使用材質副本，不改資產。
- **限制：** 路面不會變濕反光、沒有雷聲音效、城市路燈不會實際照亮（只有店面自發光與前燈）。
- **相關檔案：** `Gameplay/EnvironmentController.cs`、`Gameplay/EnvironmentProfile.cs`、`Editor/EnvironmentSetup.cs`、`Script_1/DroneController1.cs`

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

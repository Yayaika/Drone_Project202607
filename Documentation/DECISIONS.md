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

## D-018：擬真化飛行控制與螺旋槳視覺

- **日期：** 2026-10-19
- **狀態：** 已確認（使用者要求「更接近現實無人機機動性能」）
- **背景：** 原本油門直接對應推力（一推即彈射、下降近似自由落體），水平有非物理的 `horizontalSpeedBoost`，阻力為固定 linearDamping 1.0；螺旋槳只依搖桿決定同一轉速，且以「RPM」當作度/秒旋轉，在 Editor 20~45 fps 時每幀轉 90°~140°，3 葉槳（每 120° 對稱）出現倒轉/靜止的車輪效應。
- **決定：**
  - 油門改為控制目標垂直速度（爬升 5 m/s、下降 3 m/s、垂直加速度上限 6 m/s²），回中即定高；推力經馬達反應延遲（0.08 s）；載重降低可用推力。
  - 移除水平推力輔助（兩個無人機 Prefab 設為 0），最大傾角 20°→30°，改用二次方空氣阻力（傾 30° 極速約 11.5 m/s）。
  - 自平（ANGLE）模式放開搖桿時，自動反向傾斜煞車（類似實機 GPS 定點）；CRUISE／VERT 模式不變。
  - 螺旋槳各自計算轉速：轉速 ∝ √推力，加上 X 型混控（前傾時後槳加速、滾轉時對側加速、偏航時同轉向一對加速）；每幀轉角上限 35°，消除車輪效應；熄火後慢慢停轉。懸停視覺轉速 2800→2000。
- **原因：** 接近消費級無人機（DJI 類）的升降速度與煞車行為，螺旋槳轉速變化與實際推力一致。
- **替代方案：** 改為完整速度控制模式（搖桿對應目標水平速度）——改變操控方式較大，未採用。
- **影響範圍：** 所有使用 `DroneController1` 的無人機；競速關卡圈速可能改變（極速略升、起步加速略慢、放開搖桿會自動停下）。
- **相關檔案：** `Script_1/DroneController1.cs`、`Prefabs/Drone_Bomb.prefab`、`Prefabs/Drone_Parent Variant.prefab`

## D-019：設置按鈕/ESC、檢查圈改版、City2 賽道、炸彈、空中飛機、街道設施碰撞

- **日期：** 2026-10-19
- **狀態：** 已確認（使用者要求）
- **決定：**
  - HUD「設置」按鈕執行時移到畫面上方正中央（航向帶上方）、加大並顯示 ESC 圖示；ESC／手把 Start 開關設置面板，面板開啟時暫停（timeScale = 0）。有 HUD 設置面板時 `OptionsMenu` 不再處理 ESC。
  - 檢查圈改用 `Prefabs/ring target 20`（圈面朝飛行方向），以薄盒觸發區判定穿越；未通過的檢查圈以 LineRenderer 依序連線，通過後該圈縮小消失。素材集中在 `Resources/GameplayAssets.asset`。
  - City2：`RaceManager` 改為沿 Spline 一次生成整串檢查圈（約每 30 m，含終點），長度不足 100 m 的草稿 Spline 不抽中，同一路線可反向飛；重生、計時、結算統一交給 GameManager。舊的單一移動閘門 (`GateTrigger`) 保留為空元件避免 Missing Script。
  - 炸彈無人機：B／右扳機投下機腹炸彈（`Prefabs/Bomb.prefab`，3 秒裝填），落地爆炸（War FX `WFX_Explosion`），8 m 內行人倒地、行駛車輛停下（40 秒後清除）、路邊停車燒毀，附 War FX 火焰與黑煙；其他剛體被衝擊波推開。
  - 空中飛機：遊戲關卡自動加入 3 架 `Air Plane_1`，橢圓航線在生成時以 SphereCast 檢查不碰到任何碰撞體與檢查圈，各機高度層至少相差 9 m；運動學剛體，無人機撞到會墜機。
  - 街道設施碰撞：`CityGenerator` 放置的 LowpolyStreetPack 設施改為與網格相同的 MeshCollider（光錐不碰撞）；已烘焙的 CityResidential、CityDowntown 與 OrganicCityTest 一併修正。
- **限制：** War FX 使用內建管線粒子 Shader，URP 下顯示需實測；行人倒地為整體旋轉（無專用倒地動畫）；City2 沒有車流/行人，炸彈在 City2 只有爆炸與衝擊波。
- **相關檔案：** `Script_1/DroneFlightHUD.cs`、`Scripts/OptionsMenu.cs`、`Scripts/UI/ControlsHelp.cs`、`Scripts/CourseBuilder.cs`、`Mapscript/RaceManger.cs`、`Mapscript/Cricle.cs`、`Scripts/GameManager.cs`、`Scripts/Gameplay/GameplayAssets.cs`、`Scripts/Gameplay/Bombing.cs`、`Script_1/DroneBombDropper.cs`、`Scripts/Level/AircraftTraffic.cs`、`Scripts/Level/CityTraffic.cs`、`Scripts/Level/CityPedestrians.cs`、`Scripts/Level/CityGenerator.TaiwanStreets.cs`

## D-020：檢查圈連貫配置、炸彈無人機機動性與爆炸調整、低空小飛機

- **日期：** 2026-10-20
- **狀態：** 已確認（使用者要求）
- **決定：**
  - HUD 設置按鈕再往右移到右上方（檢查點進度上方），避開中央航向帶與導航提示（`DroneFlightHUD.settingsButtonOffsetX`）。
  - 檢查圈：取消連線 (LineRenderer)；所有地圖共用 `RaceCourseLayout`——手繪 Spline 先以 Douglas-Peucker 還原轉折點再圓角；檢查圈朝向取「上一個→下一個」連線方向；城市直角路口先在路口內圓角 (碰撞檢查，不切過街角建築)，沿路線連續擺放，直線約 18 m 一個、轉彎處自動加密 (相鄰方向變化約 30°)，左右/高低錯落，每三個有一個刻意貼近側邊建築/路燈或壓低貼近地面。所有檢查圈尺寸 × 0.65 (`CourseBuilder.RingSizeScale`)。
  - 炸彈衝擊波改為有上限的速度變化 (無人機最多 4.5 m/s、以水平推開為主)，修正靠太近被瞬間彈到高空；爆炸點地面殘留 War FX 火焰 18 秒。
  - 掛著炸彈的無人機撞擊速度 ≥ 6 m/s 時炸彈在機腹引爆，之後照常裝填。
  - Drone_Bomb 單獨提高機動性（傾角 40°、阻力 0.02 → 極速約 17 m/s、爬升 7 / 下降 4 m/s、垂直加速度 9、姿態增益 22、搖桿反應 11、偏航角加速度 1200、馬達反應 0.06 s）；Drone_Parent Variant 不變。
  - 飛機縮小為 0.5 倍，最低離地 6 m：收集所有可行航線 (城市生成器關卡為沿街道上空繞街區的 `CityGenerator.GetStreetLoops`，其他場景如 City2 為檢查圈附近網格化的圓形航線)，每條取最低淨空高度，越低、越接近檢查圈越優先，並盡量分散；仍以 SphereCast 保證不碰任何模型，航線水平範圍重疊的飛機高度相差至少 6 m。
- **相關檔案：** `Scripts/Gameplay/RaceCourseLayout.cs`、`Scripts/CourseBuilder.cs`、`Scripts/Level/CityGenerator.cs`、`Mapscript/RaceManger.cs`、`Scripts/Gameplay/Bombing.cs`、`Script_1/DroneBombDropper.cs`、`Scripts/Level/AircraftTraffic.cs`、`Prefabs/Drone_Bomb.prefab`

## D-021：飛控反應加快

- **日期：** 2026-10-20
- **狀態：** 已確認（使用者回報「飛控反應太慢」）
- **原因：** 姿態增益低 (stabilizerStrength 15 × attitudeGain 2，自然頻率約 5.5 rad/s) 且程式固定剛體角阻尼 6，從水平傾到目標角度約 0.4–0.5 秒；搖桿平滑 8/s、垂直加速度上限 6 m/s² 也增加延遲。
- **決定：** 剛體角阻尼改為可調 `bodyAngularDamping` (預設 2)；兩台無人機 stabilizerStrength 40、attitudeDamping 5.5 (阻尼比約 0.73，不會過衝)、altitudeHoldStrength 8、馬達反應 0.05 s；一般無人機搖桿反應 15、垂直加速度 10、偏航角加速度 1500；炸彈無人機搖桿反應 18、垂直加速度 13、偏航角加速度 2000。
- **相關檔案：** `Script_1/DroneController1.cs`、`Prefabs/Drone_Parent Variant.prefab`、`Prefabs/Drone_Bomb.prefab`

## D-022：水平移動改為速度輔助、傾斜幅度縮小

- **日期：** 2026-10-20
- **狀態：** 已確認（使用者回報「前後左右反應太久」「傾斜幅度改小」）
- **原因：** 原本純物理：搖桿只決定傾角 (最多 30°)，加速度受限於 g·tan(傾角) ≈ 5.7 m/s²，起步到極速超過 2 秒；放開時煞車只用 24° 傾角，2–3 秒才停。
- **決定：** 自平 (ANGLE) 模式改為速度輔助：搖桿決定目標水平速度 (一般 12 m/s、炸彈無人機 17 m/s)，以 `horizontalResponse` (1/秒) 追上、加減速上限 `maxHorizontalAccel` (12 / 16 m/s²)，並補償空氣阻力；不足的水平加速度由輔助力提供，因此機身只需小幅傾斜 (`tiltVisualScale` 0.6，傾角上限一般 20°、炸彈無人機 25°)。取代原本的自動煞車參數。CRUISE / VERT 模式不變。
- **取捨：** 水平運動不再完全由傾角物理決定 (較像遊戲/實機 GPS 模式的手感)，傾斜主要是視覺表現。
- **相關檔案：** `Script_1/DroneController1.cs`、`Prefabs/Drone_Parent Variant.prefab`、`Prefabs/Drone_Bomb.prefab`

## D-023：City2 移到世界中心並豐富場景物件

- **日期：** 2026-10-20
- **狀態：** 已確認（使用者要求）
- **決定：**
  - City2 所有根物件整體平移 (-127.7, -187.95, +579)，可玩範圍 (邊界牆) 中心位於世界原點、路面高度 y = 0；賽道 Spline、起點、Spawner 一併移動，執行時邏輯不依賴座標。
  - 新增編輯器工具「無人機遊戲/10. 豐富 City2 場景物件」(`Editor/City2Decorator.cs`)：以 1m 網格辨識道路 / 人行道 / 空地，擺放路燈 (LampPost_D/J，燈臂朝向馬路)、行道樹 (Anime Nature)、街道設施 (長椅、消防栓、郵筒…)、路邊停車、停車場 (成排車位) 與小綠地 (灌木樹籬、樹、長椅、公園燈)。物件放在場景根物件「City2_Decor」下，可重複執行 (固定亂數種子)。
  - 不擋賽道：所有可抽中的賽道 Spline 水平 3.5m 內與起點 6m 內不放；放置後以 OverlapBox 檢查，碰到建築或其他物件即移除，避免穿模；碰撞體與模型同形 (街道設施 MeshCollider、車輛方盒、樹幹膠囊)。
- **相關檔案：** `Scenes/City2.unity`、`Scripts/Editor/City2Decorator.cs`、`Scripts/Editor/CityLevelBaker.cs`

## D-024：地圖空氣牆、起飛朝向第一圈、路燈與草叢位置修正

- **日期：** 2026-10-20
- **狀態：** 已確認（使用者要求）
- **決定：**
  - 空氣牆：新增 `Level/MapBoundary.cs`，執行時建立四面牆＋天花板 (只有 BoxCollider、無外觀)。程序化城市 (`CityGenerator`) 牆在外環道路外 20m、天花板 = 最高樓設定 + 40m；訓練場 (`CourseBuilder`) 左右 ±60m、起終點外 40m、天花板 45m。City2 沿用場景內既有 BoundWall。
  - 競速開場：`GameManager.StartSequence` 把無人機放在第一個檢查圈前方 14m (沿「第二圈→第一圈」反方向、與第一圈同高、離地 ≥2.5m)，機頭面向第一圈；位置被擋時改試其他距離/偏角，全部失敗才保留場景出生點只轉向。不再依賴各場景 Spawner 的固定位置。
  - 機頭方向：無人機模型的機頭 (第一人稱鏡頭) 是 transform 的 -Z，`DroneController1.RespawnAtCheckpoint` 內建 +180°。原 `GameManager.RecordRespawnPoint` 又抵銷 180° 導致 R 重生後背對前進方向；改為以 `FirstPersonAnchor` 量出機頭偏角後計算旋轉。R 重生面向「下一個要穿的檢查圈」。
  - 紅綠燈：`LampPost_D` 是「路燈＋紅綠燈」模型，被當路燈擺在路段中間；City2 / 住宅區 / 市中心 / 開始選單的 LampPost_D 全部換成純路燈 `LampPost_J` (同位置同朝向)，生成工具也只用 LampPost_J。`CityTraffic` 行人燈改為「本方向綠燈才可通行」(原本與車流相反)。
  - 草叢：刪除 City2 12 叢浮在商店屋頂/路面上方 1.4–6.3m 的草；住宅區/市中心刪除長在長椅上的草與灌木；廣場植物避開起飛平台、訓練場植物避開起終點平台。
- **相關檔案：** `Scripts/Level/MapBoundary.cs`、`GameManager.cs`、`DroneController1.cs`、`Mapscript/RaceManger.cs`、`CourseBuilder.cs`、`Level/CityGenerator*.cs`、`Level/CityTraffic.cs`、`Editor/CityLevelBaker.cs`、`Editor/City2Decorator.cs`、4 個場景

## D-025：HUD 設置面板手把操作

- **日期：** 2026-10-20
- **狀態：** 已確認
- **決定：** 設置面板開啟時自動選取第一個可互動元件 (手把搖桿導覽、A 鍵確認才有起點)，關閉時清除選取；開關鍵新增 VR 左手 Menu 鍵 (原有 ESC、手把 Start)。VR 手把射線點擊沿用 XRUIInputModule + TrackedDeviceGraphicRaycaster。
- **相關檔案：** `Script_1/DroneFlightHUD.cs`

## D-026：新增打擊模式 (STRIKE)

- **日期：** 2026-10-20
- **狀態：** 已確認（使用者要求：獨立第三模式、只有炸彈機能玩、撞擊目標放遮蔽物下、撞毀即成功後復活、目標少一點並加入移動車輛）
- **決定：**
  - `GameModeType.Strike`，`LevelCatalog` 新增 `StrikeBombTargets / StrikeRamTargets / StrikeVehicleTargets / StrikeTimeLimit`；住宅區、市中心支援。`CityGenerator`／`GameManager` 在打擊模式建立 `StrikeMissionManager`（不建立競速檢查圈）。
  - 撞擊目標位置以射線取樣：地面上方 2.8–12m 有實心遮蔽、8 個水平方向至少一個 9m 內淨空且終點上方開闊；不放在車道上。判斷「上方開闊」時忽略 `MapBoundary` 天花板。
  - `BombDamage.Apply` 先處理 `StrikeTarget`：需視線；撞擊目標只接受撞擊。車輛目標摧毀時呼叫 `CityTraffic.TryWreck` 讓車停下燃燒。
  - 是否可投彈以 PlayerDB 中無人機 Prefab 是否有 `DroneBombDropper` 判斷（`LevelCatalog.SelectedDroneCanBomb`，在選擇無人機時設定）。
- **相關檔案：** `Gameplay/StrikeMissionManager.cs`、`Gameplay/StrikeTarget.cs`、`Gameplay/Bombing.cs`、`Gameplay/LevelCatalog.cs`、`Level/CityGenerator.cs`、`Level/CityTraffic.cs`、`GameManager.cs`、`UI/ModeSelectPanel.cs`、`UI/UIPlayerSelectCtrl.cs`、`UI/ControlsHelp.cs`

## D-027：HUD 永遠顯示在最上層

- **日期：** 2026-10-20
- **狀態：** 已確認（使用者要求）
- **決定：** 無人機 HUD 為世界空間 Canvas，貼近牆面時會被遮住。新增 `UI/HudAlwaysOnTop.cs`：HUD 底下所有 Graphic 加上 `UIZTestAlways`（IMaterialModifier，複製材質並把 `unity_GUIZTestMode` 設為 Always），每 0.5 秒補上新建立的 UI。不調整 Canvas 排序（會影響點擊判定）。由 `DroneFlightHUD.Start` 自動掛上。
- **相關檔案：** `UI/HudAlwaysOnTop.cs`、`Script_1/DroneFlightHUD.cs`

## D-028：打擊模式專屬場景「港區・打擊演習」

- **日期：** 2026-10-20
- **狀態：** 已確認（使用者選擇：新建專屬場景、目標點位手動擺放）
- **決定：**
  - 打擊模式只在 `StrikePort` 場景進行；住宅區/市中心改回只支援競速與搬運（自動取樣程式保留為沒有點位時的備援）。
  - 場景由 `Editor/StrikeSceneBuilder.cs`（選單「無人機遊戲/11. 建立打擊關卡場景 (港區)」）建立：以 SampleScene 的系統物件為基礎，移除 CourseBuilder 與地面；結構以 CityMeshBuilder 建網格（世界座標 UV）存於 `Assets/GeneratedLevels/StrikePort`，材質於 `Assets/Materials/StrikePort`。重新執行只重建 `StrikePort_Level`。
  - 目標點位 `StrikeTargetPoint`（含撞擊飛入點）、車道 `StrikeVehicleRoute`（環狀路徑等速行駛、殘骸排隊後清除）、邊界 `StrikeArena`（MapBoundary，天花板 60m）皆為場景物件。
  - 沒有軍事類素材，結構全為貼圖方塊/圓柱 + 既有車輛與路燈素材（低多邊形風格，屬 placeholder 等級，可日後替換）。
- **相關檔案：** `Editor/StrikeSceneBuilder.cs`、`Gameplay/StrikeTargetPoint.cs`、`Level/StrikeVehicleRoute.cs`、`Level/StrikeArena.cs`、`Gameplay/StrikeMissionManager.cs`、`Gameplay/LevelCatalog.cs`、`Scenes/StrikePort.unity`

## D-029：HUD 設置按鈕可點擊

- **日期：** 2026-10-20
- **狀態：** 已確認（使用者回報按鈕無法按）
- **背景：** 無人機 Prefab 的 HUD Canvas 上 `GraphicRaycaster` 為停用狀態（只有 VR 用的 TrackedDeviceGraphicRaycaster），且設置按鈕位於巢狀 Canvas、沒有自己的 Raycaster，滑鼠點擊判定不到。
- **決定：** `DroneFlightHUD` 執行時啟用 HUD 根 Canvas 的 GraphicRaycaster，並為設置按鈕的巢狀 Canvas 補上 GraphicRaycaster 與根 Canvas 相同類型的 VR Raycaster。
- **相關檔案：** `Script_1/DroneFlightHUD.cs`、`UI/HudAlwaysOnTop.cs`

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

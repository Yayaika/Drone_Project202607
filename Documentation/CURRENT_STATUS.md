# 目前開發狀態（Current Status）

## 最後更新

- 日期：2026-10-13
- 更新者：Claude（AI 輔助）
- 可信度：程式碼層級已盤點並以命令列編譯驗證；**尚未在 Unity Play Mode 實測**

## 1. 專案目前狀況

- Unity 版本：6000.0.58f1（URP 17.0.4、Input System 1.14.2、XR Interaction Toolkit 3.0.11、OpenXR、Cinemachine 3.1.6、DOTween）
- 專案是否可正常開啟：是（作者持續開發中）
- 是否有編譯錯誤：以 Unity 產生的 csproj 用 dotnet 編譯 `Assembly-CSharp` 與 `Assembly-CSharp-Editor`，**0 錯誤**。Unity Editor 曾因匯入時序快取而短暫顯示 `LeaderboardEntry` 找不到，已把檔案改名為 `LeaderboardStore.cs` 強制重新匯入，**待在 Unity Console 確認**
- 主要入口場景：`StartMenu` → `PlayerSelectSettings`（選無人機）→ 模式/關卡選擇面板 → 關卡
- 目前可實際遊玩的流程（依程式推論，待實測）：選無人機 → 選「競速/搬運」→ 選關卡 → 倒數 → 遊玩 → 結算/排行 → 重玩/下一關/返回
- 目前最主要的阻礙：新功能尚未在 Editor 內實際遊玩驗證；程序化城市場景需先執行選單「無人機遊戲/1」建立；缺少音效素材

## 2. 功能狀態

| 功能 | 狀態 | 證據／相關檔案 | 尚缺內容 |
|---|---|---|---|
| 飛行控制（手感） | 部分完成（2026-10-19 Play Mode 數據驗證：定高、爬升 4.7 m/s、下降 2.9 m/s、30° 極速 11.4 m/s、放開搖桿約 4.7 s 內煞停） | `Script_1/DroneController1.cs`：Expo＋平滑輸入、偏航角加速度、PD 姿態、載重靈活度、氣流擾動；擬真升降速度控制、二次方阻力、自動煞車、馬達延遲（D-018） | 需玩家實際試飛確認手感；螺旋槳視覺未在畫面上目視確認 |
| 碰撞防吸附 | 部分完成（待實測） | `DroneController1`：接觸法線收集、移除朝牆速度與推力、外推力 | 需在城市建築間實測 |
| 第三人稱防暈鏡頭 | 部分完成（待實測） | `DroneController1`：TPV 改掛「只跟隨航向」的穩定支架；FPV 可選地平線穩定 | VR 頭顯舒適度待測 |
| 擬真 HUD（OSD） | 部分完成（待實測） | `Script_1/DroneFlightHUD.cs`：俯仰梯、航向帶、速度/高度/AGL/VS、油門、模擬電池電壓、返航、導航、警告；H 鍵切換 | 版面位置需在畫面上確認 |
| 搬運（抓取/放下） | 部分完成（待實測） | `DronePickupHandler.cs`、`DroneCargo.cs`：平滑吸附、貨物碰撞代理、範圍提示、耐久與事件 | — |
| 搬運任務規則 | 部分完成（待實測） | `Scripts/Gameplay/DeliveryMissionManager.cs`、`DeliveryZone.cs` | 送貨點分佈需實測調整 |
| 競速（檢查點/計時） | 部分完成（CityResidential Play Mode 數據：25 個檢查圈、相鄰方向變化 ≤34°、間距 7.6–21m、高度 3.2–14.7m 錯落、無連線） | `GameManager.cs`、`CourseBuilder.cs`(RingTrigger)、`Gameplay/RaceCourseLayout.cs`、`RaceManger.cs`、`CityGenerator.cs`（D-019、D-020） | 完整跑完一圈未實測；難度需試飛調整 |
| HUD 設置按鈕 / ESC | 部分完成（Play Mode 截圖確認按鈕位於上方中央） | `DroneFlightHUD.cs`、`OptionsMenu.cs` | ESC 開關與暫停待手動按鍵確認 |
| 炸彈無人機投彈 | 部分完成（CityResidential Play Mode 確認：爆炸、4 輛車燒毀、1 名路人倒地，War FX 火焰黑煙在 URP 正常顯示；投彈/隱藏機腹炸彈/裝填開始正常） | `Script_1/DroneBombDropper.cs`、`Gameplay/Bombing.cs`、`Prefabs/Bomb.prefab`、`Resources/GameplayAssets.asset` | 實際按 B 鍵投彈未以真實按鍵測試 |
| 空中飛機障礙 | 部分完成（0.5 倍尺寸；CityResidential 沿街道上空低飛，最低約離地 22m，距最近檢查圈 10m） | `Level/AircraftTraffic.cs`、`CityGenerator.GetStreetLoops` | 撞機手感待實測 |
| 街道設施碰撞體 | 已修正（CityResidential 236、CityDowntown 540、OrganicCityTest 443 個設施改為 MeshCollider） | `CityGenerator.TaiwanStreets.cs`（FitPropColliders） | — |
| 開場倒數 | 部分完成（待實測） | `GameManager.StartSequence` | — |
| 結算畫面與排行榜 | 部分完成（待實測） | `UI/ResultScreen.cs`、`Gameplay/LeaderboardStore.cs`（本機 PlayerPrefs） | 玩家名稱輸入（目前自動以無人機名稱命名） |
| 模式/關卡選擇面板 | 部分完成（待實測） | `UI/ModeSelectPanel.cs`、`UIPlayerSelectCtrl.EnterStage` | — |
| DOTween UI 動態 | 部分完成（待實測） | `UI/UIButtonTweenFx.cs`（自動掛到所有 Button）、`UI/UITween.cs`、`UIPanelCtrl.cs` | — |
| 晝夜天氣系統 | 部分完成（住宅區截圖確認正午/黃昏/夜晚/雨/暴風雨/濃霧，風力數值正確） | `Gameplay/EnvironmentController.cs` | 實際飛行手感、VR 中提示文字與雨景待測；無濕路面與雷聲 |
| 台北式街區與 Anime Nature 植物 | 部分完成（住宅區/市中心/訓練場截圖確認；City2 只確認執行無錯誤） | `CityGenerator.TaiwanStreets.cs`、`Editor/VegetationSetup.cs` | 植物為動畫風格；City2 灌木位置未逐一目視 |
| 選單場景背景與 Loading 畫面 | 部分完成（Play 模式截圖確認：開始選單城市背景/巡航無人機、選擇場景屋頂展示台、Loading 進度與淡出） | `UI/MenuSceneDecor.cs`、`Gameplay/SceneLoader.cs`、`Editor/MenuSceneSetup.cs` | VR 頭顯實機未測 |
| 固定關卡（烘焙） | 部分完成（CityResidential 已截圖確認；CityDowntown 已烘焙未逐段截圖） | `Editor/CityLevelBaker.cs`、`Scenes/CityResidential.unity`、`Scenes/CityDowntown.unity` | 重新執行選單 7 會覆蓋手動修改 |
| 競速障礙物 | 部分完成（住宅區 5 段截圖確認天橋/電線/布條/塔吊/門架） | `CityGenerator.TaiwanStreets.cs`（BuildRaceObstacles） | 難度與碰撞需實際飛行調整 |
| 台灣街景（CityProcedural） | 部分完成（已在 Play 模式生成並截圖確認；未實際飛行遊玩） | `Level/CityGenerator.TaiwanStreets.cs`、`CityMeshBuilder.cs`（D-013） | 需在 Editor 生成後確認比例、招牌文字、材質與效能 |
| 車流與紅綠燈 | 部分完成（Play 模式截圖確認：車頭方向正確、靠右行駛、跟車與停等正常） | `Level/CityTraffic.cs`（只用 Awbmecreations 車輛素材，無機車素材） | `CityProcedural.unity` 已填入 7 款車輛；長時間是否塞車/卡死待觀察 |
| 路人 | 部分完成（Play 模式截圖確認行走動畫與過馬路；已填入 8 款 CityPeople） | `Level/CityPedestrians.cs`（CityPeople 已移至 `Assets/Download/DenysAlmaral/`） | 動畫與步速是否吻合待確認 |
| 程序化城市/新關卡 | 部分完成（建築已修正並於 Play 模式確認） | `Level/CityGenerator.cs`、`Editor/DroneGameSetupMenu.cs`、`LevelCatalog.cs`（住宅區/市中心兩關） | 道路為純色 placeholder；從 StartMenu 完整流程未重測 |
| 有機城市生成器（獨立測試） | 部分完成（已在 Editor 生成並截圖確認、Play 中重建可用） | `Level/OrganicCityGenerator.cs`、`Level/OrganicCityFlyCamera.cs`、`Editor/OrganicCityGeneratorEditor.cs`、`Scenes/OrganicCityTest.unity`、`Materials/OrganicCity/` | 未接入關卡/玩法；建築已改用 POLYGON city pack 模型（放不下時少量方塊大樓補空）；街道設施與樹木使用 LowpolyStreetPack（規則可在 Inspector 新增）；中央圓環預設關閉；鍵盤操作未經實機按鍵測試 |
| 面板（設置/操作說明/介紹） | 部分完成（待實測） | `UI/PanelEnhancer.cs`、`UI/ControlsHelp.cs`、`UI/UIPopupPanel.cs`：彈出動畫、按鍵圖示操作說明、音量百分比、返回選單、依模式顯示過關目標 | 版面需在畫面上確認 |
| 音效 | 部分完成 | `Audio/GameAudio.cs`（依檔名從 Resources/Audio 載入，缺檔時用 `SynthSfx` 合成 placeholder）；引擎音高隨轉速變化；起飛音效已指定 | **缺 UI/倒數/檢查點/搬運/撞擊/警告音效與 BGM**，見 `ASSET_REQUESTS.md` |
| 空氣牆外圍景觀 | 部分完成（5 個關卡 Scene 視圖截圖確認；住宅區 Play Mode 確認顯示正常，約 +23 萬面、+170 批次） | `Editor/OutskirtsSceneryBuilder.cs`（選單 12，D-030） | VR 實機效能未測；建築為方塊 placeholder 等級 |
| 開場動畫（開始選單） | 部分完成（Play Mode 確認：鏡頭繞行、字幕打字與彈出、滑鼠點擊跳過、播完/跳過後進入選擇無人機；彈出音效使用 Button Pop.wav） | `Cutscene/IntroCutscene.cs`、`Cutscene/Dialogue*.cs`、`Cutscenes/IntroCutscene.playable`（選單 13，D-031） | VR 實機射線點擊與舒適度未測；角色頭像為文字 placeholder |
| 存檔／設定 | 部分完成 | 音量（`VolumeController`，PlayerPrefs）、排行榜（PlayerPrefs JSON） | — |

## 3. 已知錯誤與風險

| 問題 | 嚴重度 | 重現方式 | 相關位置 | 狀態 |
|---|---|---|---|---|
| Unity 匯入快取曾排除 `Leaderboard.cs` | 中 | Unity Console 出現 CS0246 `LeaderboardEntry` | `Scripts/Gameplay/LeaderboardStore.cs` | 已改名，待確認 |
| `OptionsMenu` 的 R 鍵整場重載與無人機 R 重生衝突 | 中 | 訓練場按 R | `Scripts/OptionsMenu.cs` | 已改為 F5 |
| 起飛音效條件在爬升期間每幀成立 | 低 | 指定起飛音效後爬升 | `DroneController1.UpdateAudio` | 已修正 |
| `Drone_Parent Variant` 內含多個 Camera/AudioListener | 低 | Console 可能出現多個 AudioListener 警告 | `Assets/Prefabs/Drone_Parent Variant.prefab` | 未處理（待確認是否刻意） |
| 第二台無人機（Drone_Bomb）碰撞箱只包住機身 | 低 | 機臂/螺旋槳可能穿入牆面 | `Drone_Bomb.prefab` 的 BoxCollider | 待使用者確認具體問題 |
| 執行時 `Shader.Find` 取得 URP Lit/Unlit | 低 | 建置版若未包含 Unlit shader，任務光柱改用 Sprites/Default | `Gameplay/RuntimeMaterials.cs` | 待建置驗證 |

## 4. 最近完成的工作

- 2026-10-27：開始選單設置面板美化：深色圓角卡片＋青綠外框、標題／副標題／分隔線、音量列（標籤｜滑桿｜百分比）不再被按鈕蓋住、「操作說明」「製作名單」同尺寸對稱、「返回」置中加大，隱藏「+」裝飾圖示（`PanelEnhancer.EnhanceMainMenuSettings`，執行時套用，場景檔不變）。Play Mode 截圖確認版面，製作名單按鈕可開啟名單面板
- 2026-10-27：R／手把 B「回到最後檢查點」改為先彈出確認視窗（「確定要重來嗎？」＋確認重來／退出按鈕，預選「退出」，B 鍵＝退出；開啟時暫停並停用操控）（`UI/RespawnConfirmDialog.cs`、`DroneController1.RequestRespawn`）；升降靈敏度第二輪提高（D-032）。dotnet 編譯 0 錯誤，**未在 Play Mode 實測**
- 2026-10-27：碰撞扣電：撞擊速度 > 1.5 m/s 時依速度扣 1~5% 電量（每 1 m/s 扣 0.4%），連續擦撞 0.5 秒內只扣一次，地面/屋頂著陸低於 5 m/s 不扣；HUD 電池欄位短暫以紅字顯示「-X%」（`DroneController1.ApplyCrashBatteryDrain`、`DroneFlightHUD`）。dotnet 編譯 0 錯誤，**未實際飛行測試**
- 2026-10-27：飛控手感：偏航最大角速度 300 → 180 °/s、角加速度約降 40%；最大爬升／下降速度與垂直加速度約提高 35~40%（兩台無人機 Prefab，D-032）。**未實際試飛**
- 2026-10-27：模式選擇三張卡片對齊同一高度與大小：`UIButtonTweenFx` 只中斷自己的縮放動畫（原本 `transform.DOKill()` 會打斷 `UITween.StaggerIn` 的進場位移，使選中／滑入／切換頁面的卡片停在錯位高度且越切越偏），模式卡片選中時不放大（`SetScaleFeedback`）。dotnet 編譯 0 錯誤，**未在 Play Mode 截圖確認**
- 2026-10-27：選擇無人機場景加入「返回主選單」按鈕（左上角，執行時建立；Esc／手把 B 也可返回 StartMenu，模式選擇面板開著時 B 只關閉面板），手把可由左切換鈕／開始任務往上選到（`UIPlayerSelectCtrl.BackToStartMenu`）。dotnet 編譯 0 錯誤，**未在 Unity Play Mode 實測版面與按鍵**
- 2026-10-27：HUD「校準視角」「設置」按鈕縮小為 120×28，並同時標示鍵盤與手把按鍵（V／R3、Esc／Menu）
- 2026-10-27：選擇無人機場景手把操作：預選「開始任務」、左右切換鈕與開始任務明確導航、LB／RB 切換無人機（`UIPlayerSelectCtrl.SetupGamepadNavigation`）；選取不會跑到 XR 模擬器除錯 UI；互動高亮統一為藍色（外框＋滑入/選中底色）
- 2026-10-27：純手把操作：XR UI Input Module 執行時改用 Input System（左搖桿/十字鍵移動、A 確認、B 取消），沒有選取時推搖桿自動選取按鈕、選單面板按 B 關閉（`UI/GamepadUINavigator.cs`）；選中按鈕加金色脈動外框＋放大＋音效（`UIButtonTweenFx`）；開始選單手把 X 鍵開始遊戲並預選開始按鈕。**模擬按鍵在 Editor 無法送達 Input System，實體 Xbox 手把未實測**
- 2026-10-27：第三人稱「校準視角」：HUD 左上常駐按鈕＋設置面板按鈕，V／手把 R3／VR 右搖桿按下，將頭部水平朝向對準無人機（`DroneController1.RecenterView`，FPV 隱藏）；製作名單依場景文字重新排成 職稱｜姓名｜學號 三欄（`PanelEnhancer.EnhanceCredits`）
- 2026-10-27：手把／VR 搖桿選單導航修正：彈出面板開啟時鎖住面板外按鈕並自動選取、關閉後還原選取（`UI/UIModalNavigation.cs`，套用於 UIPanelCtrl／UIPopupPanel／ModeSelectPanel）；主選單設置面板改為明確導航，製作名單可選到；模式面板返回後不再失去選取（原本手把完全失效）；模式面板可用 VR 手把 B／Y 返回；開場動畫字幕 UI 依視野寬高縮放到 75%。以 Play Mode 模擬導航事件驗證，**未用實體手把／VR 頭顯實測**
- 2026-10-24：開始選單按「開始」後播放約 30 秒可跳過的開場動畫（Timeline，兩人對話說明遊戲動機，D-031）
- 2026-10-24：所有關卡空氣牆外加入外圍景觀（山脈、丘陵森林、郊區建築、農田、道路、海灣/離島），選單 12 可重建（D-030）
- 2026-10-20：新增打擊模式（投彈／撞擊／移動車輛目標，D-026）與專屬場景「港區・打擊演習」（D-028）；HUD 永遠顯示在最上層（D-027）；HUD 設置按鈕恢復可點擊（D-029）
- 2026-10-20：地圖空氣牆、競速開場放在第一圈前方並面向它、R 重生面向下一圈 (修正背對前進方向)、路段紅綠燈模型換成路燈、行人燈時相修正、不合理草叢清除、HUD 設置面板手把預選 (D-024、D-025)
- 2026-10-20：City2 移到世界中心 (路面 y=0)，以選單 10 工具加入路燈 30、行道樹 21、街道設施 41、停車場車輛 30、空地植物 165（D-023）；飛控反應加快與水平速度輔助（D-021、D-022）
- 2026-10-20：檢查圈連貫配置（轉彎加密、錯落、貼近障礙、取消連線、縮小）、炸彈衝擊波上限與殘留火焰、撞擊引爆、炸彈無人機機動性、低空小飛機（D-020）
- 2026-10-19：擬真飛控（D-018）；設置按鈕/ESC、ring target 20 檢查圈與連線、City2 完整賽道、炸彈投彈與爆炸傷害、空中飛機、街道設施碰撞修正（D-019）
- 2026-10-13：CityProcedural 台灣街景、車流（汽車/紅綠燈）、路人（待 Unity 實測）
- 2026-10-12：飛控手感、防吸附、TPV 穩定鏡頭、擬真 OSD、搬運優化、模式/關卡選擇、倒數/結算/排行、搬運任務、音效服務、程序化城市與兩個新關卡、DOTween UI（皆待實測）

## 5. 目前正在進行的工作

- 任務：在 Unity 內驗證上述功能並調整參數
- 相關檔案：見第 2 節

## 6. 下一步建議

1. 在 Unity 執行選單「無人機遊戲/1. 建立程序化城市關卡場景」與「2. 檢查並加入關卡到 Build Settings」，再從 StartMenu 完整走一次競速與搬運流程。
2. 依試飛感受調整 `DroneController1` 的 `stickResponse`、`stickExpo`、`wallPushOut`、`tpvYawSharpness` 等參數。
3. 補齊 `ASSET_REQUESTS.md` 列出的音效。

## 7. 最近驗證結果

| 日期 | 檢查項目 | 結果 | 限制／備註 |
|---|---|---|---|
| 2026-10-20 | Play Mode：StrikePort 打擊模式、HUD 設置按鈕 | 8 個目標依手動點位生成（投彈 3、撞擊 3、車輛 2），撞擊目標飛入點正確，車輛沿環狀車道行駛；起飛於南側停機坪；設置按鈕以 EventSystem 射線命中並開啟面板（暫停）；編譯 0 錯誤 | 未手動飛完一局；以程式模擬點擊，未用實體滑鼠/VR 手把點擊 |
| 2026-10-20 | Play Mode 打擊模式（住宅區、市中心，Drone_Bomb） | 目標落點正確（投彈目標在屋頂／空地、撞擊目標在騎樓下、車輛跟車流移動）；投彈命中摧毀、炸在騎樓上方或騎樓前街上不影響撞擊目標、撞擊摧毀後 1.5 秒重新部署；投下真實炸彈命中目標車；全數摧毀進入結算與排行；模式選擇面板非炸彈機鎖住；HUD 貼牆時未被遮擋；編譯 0 錯誤 | 投彈/撞擊/結算以程式觸發，未實際手動飛行完成一局；難度、時限與目標數待試玩調整；測試寫入的排行紀錄已刪除 |
| 2026-10-20 | Play Mode：訓練場/City2/住宅區/市中心 起飛位置、朝向、R 重生、空氣牆；設置面板 | 起點距第一圈 14m、機頭偏差 <1°；R 重生面向下一圈 0°；空氣牆建立；設置面板開啟時預選第一個按鈕、關閉清除；Unity 編譯 0 錯誤 | 以程式呼叫 R 重生與面板開關，未用實體手把/VR 頭顯按鍵測試 (MCP 無法建立虛擬手把)；未實際撞牆飛行 |
| 2026-10-19 | 擬真飛控（CityResidential，Drone_Parent Variant，Play Mode 以模擬鍵盤輸入取數據） | Unity 編譯 0 錯誤；懸停 2000 RPM、爬升/前進時 ~2410 RPM；升降速度、極速與煞車符合設定 | 只用數值驗證，未目視螺旋槳；Drone_Bomb 未在 Play Mode 實測；Console 僅既有 XRDeviceSimulatorUI NullReference |
| 2026-10-14 | Editor 內連續載入 5 個場景（SceneLoader 計時） | 讀取約 0.9–1.4s（含 0.9s 最短顯示）；啟用 0.65–2.2s；市中心固定場景啟用比執行時生成快 | Editor 內數據受 Editor/MCP 負載影響，兩輪差異大；建置版效能待測 |
| 2026-10-13 | Unity Play Mode（CityProcedural，4×4 街區） | 無新錯誤；48 輛車、64 位路人、停車場正常 | Editor 內 CPU 約 42–54ms/幀（關閉城市時約 23ms，含 Editor 與 MCP 開銷），建置版效能待測；Console 中 XRDeviceSimulatorUI 的 NullReference 與無人機 LED 負縮放碰撞體警告為既有問題 |
| 2026-10-13 | dotnet 編譯 Assembly-CSharp / Assembly-CSharp-Editor（含新增 4 個檔案） | 0 錯誤、無新增警告 | Unity Editor 當時未回應 MCP（逾時），Editor.log 仍顯示寫檔途中的舊錯誤 `CityGrid` 找不到，需切回 Unity 重新編譯 |
| 2026-10-12 | dotnet 編譯 Assembly-CSharp / Assembly-CSharp-Editor（使用 Unity 產生的參考） | 0 錯誤 | 非 Unity 內編譯；未進入 Play Mode |
| 2026-10-12 | Unity Editor.log | 曾出現 CS0246（匯入時序），已處理 | 需使用者切回 Unity 後確認 Console |

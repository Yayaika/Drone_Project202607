# 目前開發狀態（Current Status）

## 最後更新

- 日期：2026-10-12
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
| 飛行控制（手感） | 部分完成（待實測） | `Script_1/DroneController1.cs`：Expo＋平滑輸入、偏航角加速度、PD 姿態、載重靈活度、氣流擾動 | 需實際試飛調參 |
| 碰撞防吸附 | 部分完成（待實測） | `DroneController1`：接觸法線收集、移除朝牆速度與推力、外推力 | 需在城市建築間實測 |
| 第三人稱防暈鏡頭 | 部分完成（待實測） | `DroneController1`：TPV 改掛「只跟隨航向」的穩定支架；FPV 可選地平線穩定 | VR 頭顯舒適度待測 |
| 擬真 HUD（OSD） | 部分完成（待實測） | `Script_1/DroneFlightHUD.cs`：俯仰梯、航向帶、速度/高度/AGL/VS、油門、模擬電池電壓、返航、導航、警告；H 鍵切換 | 版面位置需在畫面上確認 |
| 搬運（抓取/放下） | 部分完成（待實測） | `DronePickupHandler.cs`、`DroneCargo.cs`：平滑吸附、貨物碰撞代理、範圍提示、耐久與事件 | — |
| 搬運任務規則 | 部分完成（待實測） | `Scripts/Gameplay/DeliveryMissionManager.cs`、`DeliveryZone.cs` | 送貨點分佈需實測調整 |
| 競速（檢查點/計時） | 部分完成（待實測） | `GameManager.cs`、`CourseBuilder.cs`(RingTrigger)、`RaceManger.cs`、`CityGenerator.cs` | — |
| 開場倒數 | 部分完成（待實測） | `GameManager.StartSequence` | — |
| 結算畫面與排行榜 | 部分完成（待實測） | `UI/ResultScreen.cs`、`Gameplay/LeaderboardStore.cs`（本機 PlayerPrefs） | 玩家名稱輸入（目前自動以無人機名稱命名） |
| 模式/關卡選擇面板 | 部分完成（待實測） | `UI/ModeSelectPanel.cs`、`UIPlayerSelectCtrl.EnterStage` | — |
| DOTween UI 動態 | 部分完成（待實測） | `UI/UIButtonTweenFx.cs`（自動掛到所有 Button）、`UI/UITween.cs`、`UIPanelCtrl.cs` | — |
| 程序化城市/新關卡 | 部分完成（待實測） | `Level/CityGenerator.cs`、`Editor/DroneGameSetupMenu.cs`、`LevelCatalog.cs`（住宅區/市中心兩關） | 需執行選單建立場景；道路為純色 placeholder |
| 有機城市生成器（獨立測試） | 部分完成（已在 Editor 生成並截圖確認、Play 中重建可用） | `Level/OrganicCityGenerator.cs`、`Level/OrganicCityFlyCamera.cs`、`Editor/OrganicCityGeneratorEditor.cs`、`Scenes/OrganicCityTest.unity`、`Materials/OrganicCity/` | 未接入關卡/玩法；建築已改用 POLYGON city pack 模型（放不下時少量方塊大樓補空）；街道設施與樹木使用 LowpolyStreetPack（規則可在 Inspector 新增）；中央圓環預設關閉；鍵盤操作未經實機按鍵測試 |
| 面板（設置/操作說明/介紹） | 部分完成（待實測） | `UI/PanelEnhancer.cs`、`UI/ControlsHelp.cs`、`UI/UIPopupPanel.cs`：彈出動畫、按鍵圖示操作說明、音量百分比、返回選單、依模式顯示過關目標 | 版面需在畫面上確認 |
| 音效 | 部分完成 | `Audio/GameAudio.cs`（依檔名從 Resources/Audio 載入，缺檔時用 `SynthSfx` 合成 placeholder）；引擎音高隨轉速變化；起飛音效已指定 | **缺 UI/倒數/檢查點/搬運/撞擊/警告音效與 BGM**，見 `ASSET_REQUESTS.md` |
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
| 2026-10-12 | dotnet 編譯 Assembly-CSharp / Assembly-CSharp-Editor（使用 Unity 產生的參考） | 0 錯誤 | 非 Unity 內編譯；未進入 Play Mode |
| 2026-10-12 | Unity Editor.log | 曾出現 CS0246（匯入時序），已處理 | 需使用者切回 Unity 後確認 Console |

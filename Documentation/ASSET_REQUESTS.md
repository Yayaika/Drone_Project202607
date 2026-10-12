# 素材需求清單（Asset Requests）

> 以下素材在 `Assets/Download/` 及專案內都找不到，程式已預留位置；缺檔時會靜音略過，並在 Console 列出缺少的檔名。

## 1. 音效（放入 `Assets/Resources/Audio/`，副檔名 wav / mp3 / ogg 皆可）

可用選單「無人機遊戲/3. 建立音效資料夾」建立資料夾並列出目前缺少的檔案。**檔名必須完全一致。**

| 檔名 | 用途 | 建議長度／風格 | 狀態 |
|---|---|---|---|
| `ui_hover` | 滑鼠移到按鈕 | 極短 (<0.1s) 輕點聲 | 缺 |
| `ui_click` | 按下按鈕 | 短促確認音 | 缺 |
| `ui_panel_open` | 面板彈出 | 0.2–0.4s 刷出聲 | 缺 |
| `countdown_beep` | 倒數 3、2、1 | 0.2s 嗶聲 | 缺 |
| `countdown_go` | 倒數結束 GO | 較高音的嗶聲或號角 | 缺 |
| `checkpoint` | 通過檢查圈 | 清脆上升音 | 缺 |
| `finish` | 完成關卡 | 1–2s 勝利音效 | 缺 |
| `new_record` | 刷新第一名 | 慶祝音效 | 缺 |
| `cargo_pickup` | 抓起貨物 | 機械扣合聲 | 缺 |
| `cargo_drop` | 放下貨物 / 貨物落地 | 箱子落地悶聲 | 缺 |
| `cargo_delivered` | 送達 | 收銀機或確認音 | 缺 |
| `cargo_broken` | 貨物損毀 | 碎裂聲 | 缺 |
| `drone_crash` | 無人機撞擊 | 塑膠/金屬撞擊 | 缺 |
| `warning_beep` | HUD 警告（低電量、PULL UP、障礙物） | 急促警示嗶聲 | 缺 |
| `bgm_menu` | 選無人機畫面背景音樂（循環） | 輕快電子 | 缺 |
| `bgm_race` | 競速背景音樂（循環） | 節奏感強 | 缺 |
| `bgm_delivery` | 搬運背景音樂（循環） | 輕鬆 | 缺 |
| `bgm_result` | 結算背景音樂 | 短循環 | 缺 |

已存在並已使用：`Assets/Sound/` 內的引擎循環、起飛（本次新指定到兩台無人機 Prefab）、降落音效。

## 2. 模型／貼圖（可選，提升程序化城市品質）

| 項目 | 用途 | 目前替代方案 | 狀態 |
|---|---|---|---|
| 道路貼圖材質（可平鋪） | `CityGenerator.roadMaterial` | 深灰純色＋黃色分隔線 | 可選；POLYGON city pack 有 `Street*.png` 可自建材質後指定 |
| 人行道材質 | `CityGenerator.sidewalkMaterial` | 淺灰純色 | 可選；可用 `Sidewalk_texture.png` |
| 貨物箱模型 | 搬運任務貨物 | 已使用 `Drone_Cargo_RedCube.prefab`（紅色方塊 placeholder） | 建議補正式紙箱模型 |
| 送貨點標示（停機坪、地標） | 搬運任務 | 程式產生的半透明光圈與光柱 | 可選 |

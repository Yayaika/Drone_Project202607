---
name: unity-debugging
description: 分析 Unity 編譯錯誤、執行時錯誤、Missing Reference、Prefab／場景問題與遊戲行為異常。當使用者要求除錯或修復問題時使用。
---

# Unity 除錯流程

## 1. 重現與收集證據

- 取得完整錯誤訊息、堆疊追蹤與可重現步驟。
- 確認 Unity 版本、相關套件與問題首次出現的時間點。
- 檢查最近變更與相關檔案。
- 不要只依錯誤訊息中的第一個檔案就假定根因。

## 2. 追查根因

依問題類型檢查：
- NullReferenceException：物件生命週期、Inspector 引用、初始化順序。
- Missing Script／Missing Reference：Prefab、場景物件、資產引用與 GUID。
- 狀態／流程錯誤：事件順序、狀態切換、協程、非同步流程。
- 物理／碰撞問題：Collider、Rigidbody、Layer、Trigger 與時間步長。
- UI 問題：Canvas、EventSystem、輸入設定、排序與引用。
- 載入／切場景問題：場景設定、非同步完成條件與物件存活範圍。

## 3. 修復原則

- 先找出根因，再提出最小修復。
- 不要用大量 `try/catch`、空判斷或關閉功能掩蓋錯誤。
- 不要修改無關系統。
- 若有多個可能根因，先用低風險檢查縮小範圍。

## 4. 驗證

- 重跑原本的重現步驟。
- 檢查是否引入新的編譯錯誤或 Console 錯誤。
- 若無法在 Unity Editor 實際執行，明確說明限制與尚未驗證的結果。

## 5. 回報

列出根因、修改內容、驗證結果，以及仍可能存在的風險。

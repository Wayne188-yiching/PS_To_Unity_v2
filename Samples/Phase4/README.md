# Phase 4 驗收樣本

> **狀態**：PSD 檔請依下方指引由 Photoshop 手動建立並存到此資料夾。
> commit 這些 PSD 後、Phase 4.5 之後任何回歸測試都能重跑同一組樣本。
> 用途對應 [`OPTIMIZATION_PLAN_zh.html#phase4-decisions`](../../OPTIMIZATION_PLAN_zh.html#phase4-decisions) Step 4 / Q13-d。

---

## 需要的 4 個樣本

以下四個 PSD 都以 **1080×1920** 畫布（或其他任意尺寸，只要一致）建立，內部一個 root group 命名為 `MainPage`。

### 1. `grid_basic.psd` — 純 Grid（不掛 CanvasGroup）
- root `MainPage`
  - group `Cards[GRID]`
    - 內含 6 個 200×200 的 image layer，橫排 3 個、下方再一行 3 個，水平/垂直間距各 20 px
- **預期 layout.json**：`Cards` group 有 `"layoutType": "grid"`、`gridConstraintCount: 3`、`gridStartAxis: "horizontal"`、`gridCellSizeX/Y: 200`、`gridSpacingX/Y: 20`，`hasCanvasGroup` 不出現
- **預期 prefab**：`Cards` 節點掛上 `GridLayoutGroup`（cellSize 200×200、spacing 20/20、FixedColumnCount=3、UpperLeft）；`MainPage` 節點無 CanvasGroup；prefab 根節點有 CanvasGroup（一律掛）

### 2. `canvasgroup_basic.psd` — 純 CanvasGroup（不掛 Grid）
- root `MainPage`
  - group `Modal[CG]`
    - 內含任意 image / text layer（隨意排列，不需要成 grid）
- **預期 layout.json**：`Modal` group 有 `"hasCanvasGroup": true`，無 `layoutType` grid 相關欄位
- **預期 prefab**：`Modal` 掛上 `CanvasGroup`（alpha=1、interactable/blocksRaycasts=true）；prefab 根節點也有 CanvasGroup

### 3. `grid_canvasgroup_combo.psd` — 同一 group 同時是 Grid + CanvasGroup
- root `MainPage`
  - group `Cards[GRID][CG]`
    - 內含 4 個 150×150 image layer，2×2 排列，間距 10 px
- **預期 layout.json**：`Cards` group 同時有 `"layoutType": "grid"`（含全套 grid 欄位）與 `"hasCanvasGroup": true`
- **預期 prefab**：`Cards` 節點同時掛 `GridLayoutGroup` 與 `CanvasGroup`
- **變化測試**：把標籤順序反過來 `[CG][GRID]` 應該效果一樣（case-insensitive、位置任意）

### 4. `grid_degrade.psd` — Grid 降級（子節點寬高差 > 20%）
- root `MainPage`
  - group `Cards[GRID]`
    - 內含 3 個 image layer：**寬度分別為 200 / 200 / 300**（300 比中位數 200 高 50%，超過 20% 閾值）
- **預期 layout.json**：`Cards` group **無** `"layoutType"`（降級成普通 group）；root-level `"warnings"` 陣列含一條 `code: "GRID_DEGRADED"` 訊息，node = `Cards`
- **預期 prefab**：`Cards` 節點無 GridLayoutGroup（回到普通 group）
- **預期 Unity Console**：`[PhotoshopUiImporter] GRID_DEGRADED @ Cards — 子節點寬高差異超過 20%（最大偏差 50%）...`

（額外可選）另做一個 `grid_outlier.psd`：三個 image 寬度 200 / 200 / 220（差 10%，在 20% 內），標 `[GRID]` → 仍掛 GridLayoutGroup 但 Unity Console 出現 `GRID_OUTLIER` warning。

---

## 手動驗收 checklist（全部樣本跑完 full import 後對照）

- [ ] `grid_basic.psd` full import：`Cards` 節點掛 `GridLayoutGroup`，cellSize/spacing/constraintCount 正確
- [ ] `canvasgroup_basic.psd` full import：`Modal` 節點掛 `CanvasGroup`；prefab 根節點也掛 `CanvasGroup`
- [ ] `grid_canvasgroup_combo.psd` full import：`Cards` 同時有 `GridLayoutGroup` + `CanvasGroup`
- [ ] `grid_degrade.psd` full import：`Cards` 沒 `GridLayoutGroup`；Unity Console 有 `GRID_DEGRADED @ Cards` warning
- [ ] （選）`grid_outlier.psd` full import：仍掛 `GridLayoutGroup`，Console 有 `GRID_OUTLIER` warning
- [ ] **reskin guard 驗證**：跑完 `canvasgroup_basic.psd` full import 後，在 Unity Inspector 手動把 `Modal` 節點的 `CanvasGroup.alpha` 改成 `0.5`；接著跑 reskin flow（沿用既有 SkinTheme 流程）→ 確認 `CanvasGroup.alpha` 仍是 `0.5`（沒被覆蓋回 1）
- [ ] **回歸測試**：用手邊任一個 Phase 3 產出的 PSD 樣本重 import 一次，肉眼比對 prefab 跟 Phase 3 產出應該完全一致（沒有多掛 GridLayoutGroup、沒有多的 warning）；只有 prefab **root 節點會多一個 CanvasGroup**（Phase 4 新增，這是預期行為）

---

## 疑難排解

| 現象 | 可能原因 |
|---|---|
| `Cards[GRID]` 沒掛 GridLayoutGroup、也沒 warning | 子節點少於 2 個，會被視為「不需要 grid」而 fallback 到普通 group |
| Unity Console 沒印任何 warning | 樣本可能沒 trigger 到 warning path；或 Unity 端 `LogLayoutWarnings` 沒被呼叫 — 檢查 `PhotoshopUiImporterWindow.CheckLayout / ValidateLayout / GeneratePrefabInternal` 三處 |
| `layout.json` 有 `"schemaVersion": "2.9"` 但 Unity Console 印 `UNITY_TOOL_OUTDATED` | 你的 Unity backend 還是舊的 —— 更新 `Assets/Editor/PhotoshopUiImporter/` 全部檔案 |

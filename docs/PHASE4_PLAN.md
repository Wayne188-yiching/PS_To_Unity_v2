# Phase 4 規劃進度（自適應布局 Component 自動掛載）

> **接手說明**：這份是 Phase 4 grill-me 規劃對話的決策摘要。任何 Claude session（不論帳號 / 機器）讀完這份，就能直接從「Q5 等使用者裁決」續上。
> 更新時間：2026-06-18（grill-me 進度停在 Q5）。

---

## 任務目標
PSD → Unity Prefab 產出後，自動在應該掛的節點上掛上自適應布局相關 Component（CanvasGroup、Horizontal/Vertical/Grid LayoutGroup、ContentSizeFitter、ScrollRect、Scrollbar 等）。

---

## 路線分階段（Q1 已決議）

| 階段 | 範圍 | 狀態 |
|---|---|---|
| **Phase 4 (A)** | `GridLayoutGroup` + `CanvasGroup` + Layout child opts（`childControlWidth/Height`、`childForceExpand`、`childAlignment`） | 規劃中 |
| **Phase 4.5 (B)** | `ScrollRect` 完整套件（含 Viewport/Content 階層重組、`RectMask2D`/`Mask`、`Scrollbar`、`[SCROLL_V]/[SCROLL_H]` 命名觸發） | Phase 4 完成後再開 |

**Why 分階段：** ScrollRect 需要重組 prefab 階層（PS 是一層 group，Unity 要拆成 ScrollView > Viewport > Content 三層），跟 A 風險不同等級，獨立做比較乾淨。使用者明確同意「先走 A 之後還是要走到 B」。

---

## 已決議事項

### Q2 — 訊號源優先級 → **PS 端是 source of truth**
- JSX 偵測並寫進 JSON → Unity 端只負責照 JSON 掛 component。
- **Why:** 跟現行 `layoutType / layoutSpacing / contentSizeFitter` 同模式；除錯時看 JSON 就能對得上 prefab。
- **How to apply:** 任何新欄位先加在 `PhotoshopUiNode`，JSX 端負責寫入，Unity backend 只讀。

### Q3 — 覆蓋語意 → **每次 full import 完全照 JSON 蓋過**
- Full import：完全覆蓋 Grid / CanvasGroup / child opts（使用者手調會被吃掉，與既有 `ApplyLayoutGroup` 行為一致）。
- **reskin flow 完全不觸碰 Grid / CanvasGroup / child opts**（reskin 路徑保留使用者手調空間）。
- **Why:** 一致性 > 彈性；source of truth 是 PS。這條從 Phase 2 立下的鐵律延續。

### Q4 — Grid 觸發條件 → **只認顯式 `[GRID]` 命名標籤**
- 不做啟發式偵測。
- **Why:** Grid 誤判成本不對稱（強塞 cell size 會視覺整個錯位）；設計師打 `[GRID]` 成本低、行為可預期。也跟 Phase 4.5 `[SCROLL_V]` 顯式風格對齊。

---

## 進行中：Q5 — Grid 參數推導規則（等使用者確認）

`[GRID]` 觸發後，GridLayoutGroup 六個欄位的建議規則：

| 欄位 | 推導方式 |
|---|---|
| `cellSize` | 子節點寬高的 **(中位數寬, 中位數高)**。避 outlier；同寬同高時等價於 first。 |
| `spacing.x` | 同 Y 相鄰節點水平 gap 中位數（沿用 `calcLayoutSpacing` 邏輯，只在同 Y 對之間算）。 |
| `spacing.y` | 同 X 相鄰節點垂直 gap 中位數（只在同 X 對之間算）。 |
| `padding` | 沿用既有 `calcLayoutPadding`（group bounds vs children bounding-box 差）。 |
| `startCorner` | 固定 `UpperLeft`。 |
| `startAxis` | 依實際排列自動判：第一行同 Y 節點數 > 第一列同 X 節點數 → `Horizontal`，反之 `Vertical`。 |
| `constraint` | `FixedColumnCount`，`constraintCount` = 第一行同 Y 節點數。 |
| `childAlignment` | 固定 `UpperLeft`。 |

**待裁決子問題：**
1. `constraint` 用 FixedColumnCount（建議）vs Flexible？（Flexible 響應式時跟 PS 不一致）
2. 不等大子節點：建議報 warning 並降級成普通 group，不掛 GridLayoutGroup。是否同意？
3. cellSize outlier > 20% 是否 warning（建議：要）？

---

## 後續待問題目（Q6+）

- **Q6** CanvasGroup 觸發條件：命名標籤 `[FADE]` / `[TOGGLE]` / `[CG]`？還是所有 group 都掛？
- **Q7** Layout child opts 推導：`childControlWidth/Height` / `childForceExpand` / `childAlignment` 從什麼訊號推？
- **Q8** PS 命名標籤總表 / 大小寫 / 別名規則（`[GRID]` vs `[grid]` vs `[G]`）。
- **Q9** JSON schema 升版策略（schemaVersion bump？向下相容？）。
- **Q10** 失敗 / warning 訊息策略（PS console alert？JSON 內嵌？Unity log？）。
- **Q11** 版號規劃（Phase 4 → v2.9.0？延續既有慣例）。

---

## 關鍵程式入口（接手時直接讀）

| 用途 | 檔案路徑 | 約略位置 |
|---|---|---|
| PS 端偵測 | `PhotoshopExporter/PhotoshopUiPackageExporter.jsx` | line 596 / 1017 — `detectLayoutGroupType` / `applyLayoutGroupMetadata` |
| Unity 端掛載 | `Assets/Editor/PhotoshopUiImporter/UGuiTmpPrefabBackend.cs` | line 240 — `ApplyLayoutGroup` |
| Schema 定義 | `Assets/Editor/PhotoshopUiImporter/PhotoshopUiLayout.cs` | 整檔（`PhotoshopUiNode` 加新欄位） |
| reskin 流程 | `Assets/Editor/PhotoshopUiImporter/PsUiSkinApplier.cs` | （新 component 不可碰） |

---

## 接手 prompt 模板（換帳號後可直接貼）

> 我要繼續 Phase 4 規劃。請先讀 `docs/PHASE4_PLAN.md` 跟 memory 裡的 `phase4-plan-status.md`，然後從 Q5 的三個待裁決子問題開始 grill 我（沿用 /grill-me + /karpathy-guidelines 流程）。

# Phase 4 規劃（自適應布局 Component 自動掛載）

> **狀態**：grill-me 規劃完成（Q1–Q13 全部決議），等待開工。
> **接手**：任何 Claude session 讀完這份就能直接動手實作；按「實作計畫」一節的順序進行。
> 更新時間：2026-06-24（規劃結束）。

---

## 任務目標
PSD → Unity Prefab 產出後，自動在應該掛的節點上掛上自適應布局相關 Component（CanvasGroup、Horizontal/Vertical/Grid LayoutGroup、ContentSizeFitter、ScrollRect、Scrollbar 等）。

---

## 路線分階段（Q1）

| 階段 | 範圍 | 版號 | 狀態 |
|---|---|---|---|
| **Phase 4 (A)** | `GridLayoutGroup` + `CanvasGroup`（Layout child opts 沿用現狀不動，見 Q7） | v2.9.0 | 規劃完成、待開工 |
| **Phase 4.5 (B)** | `ScrollRect` 完整套件（含 Viewport/Content 階層重組、`RectMask2D`/`Mask`、`Scrollbar`、`[SCROLL_V]/[SCROLL_H]` 命名觸發） | 延後決定（Q11-c） | Phase 4 完成後再開規劃 |

**Why 分階段：** ScrollRect 需要重組 prefab 階層（PS 是一層 group，Unity 要拆成 ScrollView > Viewport > Content 三層），跟 A 風險不同等級，獨立做比較乾淨。

---

## 已決議事項（Q2–Q13）

### Q2 — 訊號源 → **PS 端是 source of truth**
JSX 偵測並寫進 JSON → Unity 端只負責照 JSON 掛 component。任何新欄位先加在 `PhotoshopUiNode`，JSX 寫入，Unity backend 只讀。
- **Why:** 跟現行 `layoutType / layoutSpacing / contentSizeFitter` 同模式；除錯時看 JSON 就能對得上 prefab。

### Q3 — 覆蓋語意 → **每次 full import 完全照 JSON 蓋過**
Full import 完全覆蓋 Grid / CanvasGroup（與既有 `ApplyLayoutGroup` 行為一致）；**reskin flow 完全不觸碰新 component**（保留設計師手調空間）。
- **Why:** 一致性 > 彈性；source of truth 是 PS。Phase 2 立下的鐵律延續。

### Q4 — Grid 觸發 → **顯式 `[GRID]` / `[GLAYOUT]` 命名標籤**
不做啟發式偵測。
- **Why:** Grid 誤判成本不對稱（強塞 cell size 會視覺整個錯位）；設計師打標籤成本低、行為可預期。也跟 Phase 4.5 `[SCROLL_V]` 顯式風格對齊。

### Q5 — Grid 參數推導規則

| 欄位 | 規則 |
|---|---|
| `cellSize` | 子節點寬高的 **(中位數寬, 中位數高)**。避 outlier；同寬同高時等價於 first。 |
| `spacing.x` | 同 Y 相鄰節點水平 gap 中位數（沿用 `calcLayoutSpacing`，只在同 Y 對之間算）。 |
| `spacing.y` | 同 X 相鄰節點垂直 gap 中位數（只在同 X 對之間算）。 |
| `padding` | 沿用既有 `calcLayoutPadding`（group bounds vs children bounding-box 差）。 |
| `startCorner` | 固定 `UpperLeft`（hardcode、不進 JSON，見 Q12-d）。 |
| `startAxis` | 依實際排列自動判：第一行同 Y 節點數 > 第一列同 X 節點數 → `Horizontal`，反之 `Vertical`。 |
| `constraint` | **`FixedColumnCount`**，`constraintCount` = 第一行同 Y 節點數。 |
| `childAlignment` | 固定 `UpperLeft`（hardcode、不進 JSON）。 |

**邊界條件（Q5.1 / Q5.2 / Q5.3）：**

| 子題 | 決議 | Why |
|---|---|---|
| Q5.1 `constraint` | **FixedColumnCount** | Flexible 在 runtime 因 viewport 寬度自動換行，跟 PS 看到的版面對不上 |
| Q5.2 子寬高差異 > 20% | **JSX 報 warning + 降級成普通 group**（不掛 GridLayoutGroup） | GridLayoutGroup 強制等大 cell；硬塞會視覺爆掉；降級至少不破壞排版 |
| Q5.3 cellSize outlier 差距 ≤ 20% | **仍掛 GridLayoutGroup，但報 warning** | 20% 內視為設計意圖等大、PS 圖層只是 anti-alias 飄幾 px；warning 幫設計師抓「以為是 grid 但對齊跑掉」的情境 |

### Q6 — CanvasGroup 觸發 → **root 自動掛 + 中間層 `[CG]` / `[CANVASGROUP]` 顯式**
JSON root node 一定掛 CanvasGroup（不需要標籤）；中間層 group 想掛要打標籤。**單一標籤，不分動詞**（不收 `[FADE]` / `[TOGGLE]`，因為 CanvasGroup 是同一個 component，動詞是 runtime 的事）。
- **Why:** 99% 場景是「整頁淡入淡出 / loading 遮罩 disable 互動」，root 自動掛省標籤；中間層顯式跟 `[GRID]` 對齊。

### Q6.1 — CanvasGroup 初始值 → **全部 Unity 預設**
`alpha=1` / `interactable=true` / `blocksRaycasts=true` / `ignoreParentGroups=false`。不讀 PS group opacity。
- **Why:** Phase 4 範圍 = 掛 component，不做「PS opacity → CanvasGroup.alpha」翻譯（scope creep + 語意衝突：bake 進初始 alpha 會打到 runtime fade 動畫）。

### Q7 — Layout child opts → **沿用現狀，Phase 4 不動**
- Q7-a 四個 bool（`childControlWidth/Height`、`childForceExpandWidth/Height`） → 全部 `false`（沿用）
- Q7-b `childAlignment` → 沿用三種不一致（H=MiddleLeft、V=UpperCenter、Grid=UpperLeft）
- **Why:** Phase 1–3 寫死跑了三個 phase 零抱怨；統一成 UpperLeft 是 breaking change 又收益低，YAGNI。Phase 4 對既有 H/V Layout 完全不碰。

### Q8 — 命名標籤定義

| 觸發目標 | 標籤別名集 |
|---|---|
| Grid | `[GRID]` / `[GLAYOUT]` |
| CanvasGroup | `[CG]` / `[CANVASGROUP]` |
| （參照）H Layout | `[H]` / `[HLAYOUT]`（既有） |
| （參照）V Layout | `[V]` / `[VLAYOUT]`（既有） |
| （參照）Fake thickness | `[THICK:offsetY:offsetX]`（既有） |

**通則（跟既有 convention 鎖死）：**
- 大小寫不敏感（regex `i` flag）
- 位置任意（regex 全域 replace）
- 多標籤可組合：`[GRID][CG] Cards` 兩個同時生效（Grid 跟 CanvasGroup 正交）
- 未知 token（如 typo `[GIRD]`）→ **靜默放行**（見 Q10-d）
- 架構：先沿用「各自寫 `stripXxxTags`」，第四個 tag（Phase 4.5 `[SCROLL_V]`）進來時再 refactor 成統一 parser

### Q9 — Schema 升版策略

| 子題 | 決議 | Why |
|---|---|---|
| Q9-a bump schemaVersion | **`"2.8"` → `"2.9"`**（記號用途，不加 enforce） | 跟 ToolVersion minor 對齊（Phase 4 = v2.9.0）；debug 友善 |
| Q9-b 向下相容（老 JSON 缺新欄位） | **missing = default**（不掛 component），跟 Phase 3 行為一致 | 老 JSON 重新 import 不破，新功能本來就是 opt-in |
| Q9-c 向上相容（JSX 比 Unity 新） | **Unity 端比對 schemaVersion，過新就 warning「請更新 Unity 工具」** | 防「設計師標了 `[GRID]` 但沒生效」的悶燒 bug |

### Q10 — Warning / Error 策略

| 子題 | 決議 | Why |
|---|---|---|
| Q10-a 出口 | **寫進 JSON `warnings` 陣列** → Unity backend import 時 dump 到 Unity Console | 統一 source of truth；設計師在 Unity 端看到問題才回查 |
| Q10-b 結構 | **Root-level array**：`layout.warnings = [{node, code, message}, ...]`，code 用 SCREAMING_SNAKE 常數 | 一次 foreach 印完，掃描容易 |
| Q10-c 嚴重程度 | **warning / error 兩級**（沿用既有 `LayoutReadResult.errors` 為 error 級，新增 warnings 平行擴充） | Phase 4 沒新 error 場景但結構支援；info 太瑣碎沒人看 |
| Q10-d 未知方括號 token | **靜默放行**（沿用現狀） | PS 圖層用 `[Hover]`/`[Disabled]` 描述 state 是普遍命名習慣，A 會大量誤報；typo 漏報是個別偶發 |

### Q11 — 版號規劃

| 子題 | 決議 |
|---|---|
| Q11-a Phase 4 主版號 | **v2.9.0** |
| Q11-b hotfix 策略 | 開發中 commit 不 bump；整個 Phase 4 合到 main 時打 v2.9.0 tag；之後 regression 才 v2.9.1 |
| Q11-c Phase 4.5 版號 | **延後決定**（Phase 4.5 開規劃時再 grill） |

### Q12 — JSON Schema 形狀

| 子題 | 決議 |
|---|---|
| Q12-a 整體風格 | **flat**（沿用 100% 既有 convention，無 nested object） |
| Q12-b CanvasGroup 旗標 | **`bool hasCanvasGroup`**（跟 `contentSizeFitter`、`visible` 同風格） |
| Q12-c Grid 欄位 | **複用 `layoutType` 加 `"grid"` 值** + 加 flat sibling 欄位：`gridConstraintCount` (int)、`gridStartAxis` (string `"horizontal"`/`"vertical"`)、`gridCellSizeX/Y` (float)、`gridSpacingX/Y` (float)；padding 複用既有 `layoutPaddingLeft/Right/Top/Bottom` |
| Q12-d hardcode 值（startCorner / childAlignment 都 UpperLeft） | **不寫進 JSON**，Unity 端 hardcode（跟 `childControlWidth/Height = false` 寫死同模式） |

### Q13 — 實作切分與驗證

| 子題 | 決議 |
|---|---|
| Q13-a Commit 切分 | **1 PR 多 commit**：開發過程切 4 個 commit（基礎建設 / CanvasGroup / Grid / reskin guard），最終 squash 進 main 成單一 feat（跟 Phase 1–3 慣例對齊） |
| Q13-b 實作順序 | **1 基礎 → 2 CanvasGroup → 3 Grid → 4 reskin guard**（見下方實作計畫） |
| Q13-c reskin guard 驗證 | **註解 + 手動 PSD 驗證**：在 `PsUiSkinApplier.cs` 加「不可碰清單」註解 + 跑樣本「手動改 CanvasGroup.alpha → 跑 reskin → 確認沒被吃」 |
| Q13-d 測試策略 | **手動 PSD 樣本 + commit 進 repo**（`Samples/Phase4/*.psd`），包含純 `[GRID]`、純 `[CG]`、組合、降級 case；不引入 Unity Test Framework |

---

## 實作計畫（依 Q13-b 順序）

### Step 1 — 基礎建設（schema + warning channel + Unity-端 schemaVersion warning）
- [ ] `PhotoshopUiLayout.cs` 加 `public List<PhotoshopUiWarning> warnings` 與 `PhotoshopUiWarning` class（`node`、`code`、`message` 三欄）
- [ ] `LayoutReader.cs` 把 `warnings` 反序列化進 `LayoutReadResult`
- [ ] `LayoutReader.cs`：schemaVersion 比對最低支援版本（`"2.9"`），讀到比 Unity 端認得的最高版本還新 → 加 warning「請更新 Unity 工具」
- [ ] `UGuiTmpPrefabBackend.cs`（或上游 import flow）：import 完成後把 `LayoutReadResult.warnings` 全部 `Debug.LogWarning` 到 Unity Console
- [ ] `PhotoshopUiPackageExporter.jsx` schemaVersion 改為 `"2.9"`
- [ ] `PhotoshopUiPackageExporter.jsx`：開出 root-level `warnings` array 寫入機制（helper `pushWarning(node, code, message)`）

### Step 2 — CanvasGroup
- [ ] `PhotoshopUiLayout.cs` 加 `public bool hasCanvasGroup`
- [ ] `PhotoshopUiPackageExporter.jsx`：
  - root group 強制 `hasCanvasGroup = true`
  - 中間層偵測 `[CG]` / `[CANVASGROUP]` 標籤 → `hasCanvasGroup = true`
  - 新增 `stripCanvasGroupTags` 加入 `uniqueFileName` 清理鏈
- [ ] `UGuiTmpPrefabBackend.cs` 加 `ApplyCanvasGroup`（hasCanvasGroup=true 時 AddComponent，欄位全用 Unity 預設）
- [ ] full import path 觸發 `ApplyCanvasGroup`

### Step 3 — Grid
- [ ] `PhotoshopUiLayout.cs` 加 grid 欄位：`gridConstraintCount`、`gridStartAxis`、`gridCellSizeX/Y`、`gridSpacingX/Y`
- [ ] `PhotoshopUiPackageExporter.jsx`：
  - `detectLayoutGroupType` 識別 `[GRID]` / `[GLAYOUT]` → 回傳 `"grid"`
  - 新增 `stripGridLayoutTags` 加入清理鏈
  - 新增 `calcGridParams(children, bounds)`：算 cellSize 中位數、spacing 中位數、startAxis、constraintCount
  - 寬高差異 > 20% → `pushWarning(GRID_DEGRADED)` + 降級回 `layoutType = ""`
  - cellSize outlier 差距 ≤ 20% → `pushWarning(GRID_OUTLIER)` + 仍標 grid
  - `applyLayoutGroupMetadata` 寫入 grid 欄位
- [ ] `UGuiTmpPrefabBackend.cs` 的 `ApplyLayoutGroup`：
  - `layoutType == "grid"` 走新分支 `ApplyGridLayoutGroup`
  - 設 cellSize / spacing / padding / constraint=FixedColumnCount / constraintCount / startAxis / startCorner=UpperLeft / childAlignment=UpperLeft

### Step 4 — reskin guard + 樣本驗證
- [ ] `PsUiSkinApplier.cs` 開頭加註解：
  ```
  // reskin 不可碰：CanvasGroup、GridLayoutGroup、layoutType=grid 的 group 之 layout 欄位
  // 這些屬於 full import 的 source of truth（PHASE4_PLAN.md Q3）
  ```
- [ ] 準備 3 個 PSD 樣本進 `Samples/Phase4/`：
  - `grid_basic.psd`：`[GRID]` 純 grid
  - `canvasgroup_basic.psd`：`[CG]` 純 CanvasGroup
  - `grid_canvasgroup_combo.psd`：`[GRID][CG]` 組合
  - `grid_degrade.psd`：子寬高差 > 20% 觸發降級
- [ ] 手動驗證：
  - 三個樣本走 full import → 肉眼確認 component 掛上、prefab 視覺對齊
  - `canvasgroup_basic.psd` full import 後手動改 CanvasGroup.alpha=0.5 → 跑 reskin → 確認 alpha 沒被吃
  - 既有 Phase 3 樣本重 import → 肉眼確認跟 Phase 3 產出一致（回歸測試）

---

## 關鍵程式入口

| 用途 | 檔案路徑 | 約略位置 |
|---|---|---|
| PS 端偵測 | `PhotoshopExporter/PhotoshopUiPackageExporter.jsx` | line 596 / 1017 — `detectLayoutGroupType` / `applyLayoutGroupMetadata` |
| PS 端標籤清理 | 同上 | line 2382 / 2386 — `stripLayoutGroupTags` / `stripFakeThicknessTags`（新增 `stripCanvasGroupTags`、`stripGridLayoutTags` 並列） |
| PS 端 schemaVersion | 同上 | line 1447 — `schemaVersion: "2.8"` → 改 `"2.9"` |
| Unity 端掛載 | `Assets/Editor/PhotoshopUiImporter/UGuiTmpPrefabBackend.cs` | line 240 — `ApplyLayoutGroup`（加 `ApplyGridLayoutGroup` / `ApplyCanvasGroup`） |
| Schema | `Assets/Editor/PhotoshopUiImporter/PhotoshopUiLayout.cs` | 整檔（加新欄位 + `PhotoshopUiWarning` class） |
| Schema 讀取 + Validate | `Assets/Editor/PhotoshopUiImporter/LayoutReader.cs` | line 230 — `Validate`（加 schemaVersion 比對 + warnings 反序列化） |
| reskin 流程 | `Assets/Editor/PhotoshopUiImporter/PsUiSkinApplier.cs` | 開頭加「不可碰」註解 |
| 版號 | `version.json`、`PhotoshopUiImporterWindow.cs` line 48 `ToolVersion`、`PhotoshopUiPackageExporter.jsx` line 3 `SCRIPT_VERSION` | 三處同步 bump 到 `2.9.0` |

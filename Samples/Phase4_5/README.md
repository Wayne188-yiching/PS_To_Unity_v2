# Phase 4.5 驗收樣本（ScrollRect 完整套件，v2.11.0）

> **狀態**：6 個 PSD 樣本已於 2026-07-15 建立並完成 Photoshop 2026 + Unity 2022.3.62f1 實機驗收。
> 本輪結論為 **未通過（2 個阻斷缺陷）**；詳細證據與 Claude Code 交接見本文件末段。
> 決議依據 [`OPTIMIZATION_PLAN_zh.html#phase4-5`](../../OPTIMIZATION_PLAN_zh.html#phase4-5)（Q1–Q11）。
> 另以真實 PSD（轉蛋王列表）做內部驗收，公司資產不進 repo。

---

## 共通規格

- 畫布 **1920×1080**（對齊 Unity 端預設參考解析度）。
- root group 命名 `MainPage`。
- 圖層命名全檔唯一（`Row_01`、`Row_02`…）。
- 不加圖層樣式（Drop Shadow 會觸發 bounds 補償，增加驗收變數）。

## 需要的 6 個樣本

### 1. `scroll_v_basic.psd` — 無遮色片（viewport = group bounds 退化路徑）
- `MainPage > List[SCROLL_V]`，內含 4 個 400×100 的 image row，垂直排列間距 20。
- **預期 layout.json**：`List` 節點有 `scrollDirection: "vertical"`；`contentX/Y/W/H` 與節點 `x/y/w/h` 相同（無 mask → viewport = content）。
- **預期 prefab**：`List`（ScrollRect，vertical=true、Elastic、sens=50）> `Viewport`（RectMask2D + 透明 Image raycastTarget=true）> `Content`（尺寸 = 匯出值，4 個 row）。

### 2. `scroll_v_rowmask.psd` — 遮色片掛 row（轉蛋王式畫法，C+ 主場景）
- `MainPage > List[SCROLL_V]`，5 個 400×100 row；第 5 個 row 用**圖層遮色片**裁掉下半（模擬被視窗切到）。
- **預期 layout.json**：`contentHeight` > 節點 `height`（content 全長 vs viewport 可視聯集）。
- **預期 PNG**：第 5 個 row 匯出**完整 100 高全圖**（不是半張）。
- **預期 prefab**：初始位置 content 頂對齊 viewport 頂；往下捲能看到第 5 個 row 的完整內容。

### 3. `scroll_v_groupmask.psd` — 遮色片掛 group（viewport = mask bounds）
- 同樣本 2 排法，但遮色片掛在 `List[SCROLL_V]` **群組本身**（裁出 400×350 視窗）。
- **預期 layout.json**：節點 `height` ≈ 350（mask 窗），`contentHeight` = 全列表高。
- **預期 prefab**：Viewport 350 高，捲動可見全部 row。

### 4. `scroll_grid_combo.psd` — `[SCROLL_V][GRID]`
- `MainPage > Shop[SCROLL_V][GRID]`，8 個 150×150 卡片，2 欄 4 列、間距 10。
- **預期 prefab**：`Shop`（ScrollRect）> Viewport > `Content`（**GridLayoutGroup**：FixedColumnCount=2、cell 150×150 + **ContentSizeFitter** verticalFit=PreferredSize）。

### 5. `scroll_h_grid.psd` — `[SCROLL_H][GRID]` → FixedRowCount
- `MainPage > Bar[SCROLL_H][GRID]`，6 個 100×100，**2 列 3 欄**、間距 10。
- **預期 layout.json**：`gridConstraintCount: 2`（**行數**，非欄數）。
- **預期 prefab**：Content 的 GridLayoutGroup **constraint = FixedRowCount、constraintCount = 2**；CSF horizontalFit=PreferredSize。

### 6. `prescrolled.psd` — 預捲動狀態保留（Y 翻轉驗證）
- 同樣本 3（group mask 400×350 視窗），但把 row 群組整體**上移 120px**（mask 不動）——PS 裡看起來是「已捲了一段」的狀態。
- **預期 prefab**：開場 Content 的 anchoredPosition.y ≈ +120（初始捲動位置與 PS 畫面一致，未被重設回頂部）。

### 附掛驗證（併入以上樣本，不另建檔）
- 在樣本 1 額外加一個 `Empty[SCROLL_V]` 空群組 → **`SCROLL_EMPTY`** warning、群組降級消失。
- 在樣本 1 把 `List[SCROLL_V]` 暫改名 `List[SCROLL_V][H]` 重匯 → **`SCROLL_AXIS_MISMATCH`** warning、照掛 HLayout。

---

## 手動驗收 checklist

- [ ] 樣本 1–6 逐一 full import，對照上述預期（layout.json 欄位 + prefab 結構 + Console warnings）
- [ ] 樣本 2 匯出資料夾內第 5 row 的 PNG 是完整圖（打開檔案目測）
- [ ] 任一樣本產出後進 Play mode 實際拖曳：可拖、Elastic 回彈、超出部分被裁切
- [ ] **快取驗證**：樣本 2 先「拿掉 [SCROLL_V] 匯一次」再「加回 [SCROLL_V] 匯一次」→ 第 5 row PNG 必須重匯成全圖（nomask 簽名生效，不沿用半張快取）
- [ ] **reskin guard**：對樣本 4 產出手動改 `ScrollRect.scrollSensitivity=25` + 把 Viewport 透明 Image 換掉 → 跑 reskin → 確認沒被覆蓋
- [ ] **回歸**：Phase 3 + Phase 4 全部樣本重匯,prefab 與先前一致（tag parser 統一重構後必跑）

## 疑難排解

| 現象 | 可能原因 |
|---|---|
| `[SCROLL_V]` 群組整個消失 | 群組內沒有可匯出子圖層（看 Console 的 `SCROLL_EMPTY`） |
| 匯出的 row 還是半張圖 | 該 row 走了 merged-copy 後備路徑且遮色片停用失敗（看 `SCROLL_EXPORT_DEGRADED`）；或匯出快取沿用舊圖 → 刪 `.ps_to_unity_export_cache.tsv` 重匯 |
| Unity 拖不動 | Viewport 的透明 Image `raycastTarget` 被關掉；或 EventSystem 不在場景 |
| 初始位置上下顛倒 | Y 翻轉迴歸 → 用樣本 6 回報 |

---

## 2026-07-15 實機驗收結果

### 驗收環境與前置字體

- Photoshop 2026，以 repo 內 v2.11.0 `PhotoshopUiPackageExporter.jsx` 執行真實匯出。
- Unity 2022.3.62f1，Importer_v2 v2.11.0，以 Unity MCP 產生／讀取 Prefab 並進 Play mode 測試。
- 輸出文字 Package 前，已在 Unity 建立 6 組新資產：MiSans Demibold／Semibold、HarmonyOS Sans SC／TC Medium、NotoSansCJK SC／TC Medium；每組都有 TMP Font Asset 與獨立 SDF 材質球。
- `TmpFontMap_v211` 共 8 筆映射，另沿用既有 `GenSenRounded2TC-M SDF`；`系統字-test.psd` 的 15 個文字節點全部正確對應，FontToken warning = 0。

### Phase 4.5 樣本逐項結果

| 樣本／項目 | 結果 | 實測證據 |
|---|---|---|
| 1. `scroll_v_basic` | PASS | JSON viewport/content 都是 400×460；Unity 為 ScrollRect(vertical、Elastic、sens=50) > Viewport(RectMask2D + raycast Image) > Content，4 rows；`SCROLL_EMPTY` 正確出現。 |
| 2. `scroll_v_rowmask` | PASS | JSON viewport=400×530、content=400×580；第 5 row PNG 實測 400×100；Unity Content 有 5 rows。 |
| 3. `scroll_v_groupmask` | **FAIL / BLOCKER** | PSD descriptor 實測 `hasUserMask=true`、`userMaskEnabled=true`，但 JSON viewport 仍是 400×580，未採用預期的 400×350 group mask bounds；Unity 同步產出 580 高 Viewport。 |
| 4. `scroll_grid_combo` | PASS（結構） | Content 為 GridLayoutGroup：FixedColumnCount=2、cell=150×150、spacing=10×10；CSF=Unconstrained/PreferredSize。 |
| 5. `scroll_h_grid` | PASS | Content 為 FixedRowCount=2、cell=100×100、spacing=10×10；CSF=PreferredSize/Unconstrained。 |
| 6. `prescrolled` | **FAIL / BLOCKER** | 因 group mask viewport 沒被採用，JSON viewport/content 同為 400×580；Unity Content anchoredPosition=(0,0)，預期 y≈+120。 |
| `SCROLL_AXIS_MISMATCH` | PASS | 暫改 `List[SCROLL_V][H]` 後，JSON 同時輸出 `SCROLL_AXIS_MISMATCH` 與既有 `SCROLL_EMPTY`。 |
| 快取 nomask 簽名 | PASS | 同一輸出資料夾先移除 scroll tag 再加回：第 5 row PNG 高度由 50 正確重匯為 100，未沿用半張快取。 |
| Play mode 拖曳 | PASS | Unity MCP 實測 `PLAY=True`，Content anchoredPosition 由 (0,0) 變為 (0,-49.83)，movementType=Elastic；畫面超出部分受 RectMask2D 裁切。 |
| reskin guard | **FAIL / BLOCKER** | `scrollSensitivity` 人工改 25 後能保留；但人工替換的 Viewport Image sprite 由 `row_01` 被 reskin 改成 `row_02`，違反「合成 Viewport 不可碰」。 |

### Phase 3／4 與真實 PSD 回歸

| 對象 | 結果 | 實測證據 |
|---|---|---|
| `系統字-test.psd` | PASS | layout.json 有 15 text + 2 image；Unity Prefab 有 15 TextMeshProUGUI + 2 Image。MiSans、源泉、HarmonyOS、Noto／思源全部為 TMP，且綁定各自 Font Asset／材質球。 |
| `通用視窗_Test.psd` | PASS（有 1 項資產缺口） | 8 text 全部成為 TMP、13 images、隱藏的 AcceptanceGrid／領取素材未輸出、條款文字色為 `#A05551`。`DFYuanCW9` 尚無 Font Asset，現以 GenSen fallback；屬資產缺口，不是文字轉圖片回歸。 |
| `通用視窗_NEW_202603.psd` | PASS | Photoshop 匯出 104 images、26 texts、43 groups；Unity Prefab YAML 實測 26 個 TMP component、104 個 Sprite 欄位，與昨天基準一致。 |
| Unity Console | PASS | 大型匯入期間只有 MCP 等待逾時訊息；工作完成、清空後重新讀取為 0 error／warning。 |

### 驗收結論

**Phase 4.5 v2.11.0 本輪未通過，不建議進入下一階段。** ScrollRect 三層合成、row mask、Grid 組合、cache、Play mode 拖曳與 TMP 多字型主路徑均通過；但 group mask viewport 與 reskin Viewport guard 是規格明定且可穩定重現的阻斷缺陷。

### Claude Code 修復交接

1. **Group mask viewport**
   - 重現：匯出本資料夾 `scroll_v_groupmask.psd`。
   - 現況：group mask 為啟用狀態，但 `applyScrollMetadata()` 取得的 viewport 仍為 children 完整聯集 400×580。
   - 驗收：JSON `height≈350`、`contentHeight=580`；Unity Viewport 350 高，Content 580 高。
2. **Prescrolled Y 保留**
   - 重現：匯出 `prescrolled.psd`。
   - 驗收：group viewport 正確後，Unity Content `anchoredPosition.y≈+120`。
3. **Reskin synthetic Viewport guard**
   - 重現：`scroll_grid_combo.prefab` 將 sens 改 25，Viewport Image 換成 `row_01`；SkinTheme 把 `row_01` 映射為 `row_02` 後 Apply。
   - 現況：sens=25 有保留，但 Viewport sprite 被換成 `row_02`。
   - 驗收：reskin 可替換 Content 一般 Image，但必須完全跳過 ScrollRect 合成的 Viewport／Content 節點及其元件。
4. **非阻斷資產項**
   - 若要求 `通用視窗_Test.psd` 字型外觀完全等同 Photoshop，需取得合法可放入 Unity 的 `DFYuanCW9` 字型檔，建立 TMP Font Asset／材質並在 TmpFontMap 加 `dfyuancw9`。

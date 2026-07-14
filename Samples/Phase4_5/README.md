# Phase 4.5 驗收樣本（ScrollRect 完整套件，v2.11.0）

> **狀態**：PSD 檔請依下方指引由 Photoshop 手動建立並存到此資料夾。
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

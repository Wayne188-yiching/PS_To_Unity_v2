# Photoshop To Unity V2

將 Photoshop 排版好的遊戲 UI 轉成 Unity uGUI + TextMeshPro Prefab。文字圖層保留為可編輯的 TMP 節點，非文字圖層逐一輸出為 PNG Sprite。

**目前版本：v2.13.1**

> PNG 像素去重會先依檔案大小與圖片尺寸篩選候選者；只有可能重複的圖片才會讀取完整檔案計算雜湊，而且每個候選檔案最多只計算一次。這項最佳化不會改動版面 JSON、PNG 像素或 Unity 圖片引用結果。

> 可見滑軌自動化：在 `[SCROLL_V]` / `[SCROLL_H]` 群組下加入直接子群組 `[SCROLLBAR_V]` / `[SCROLLBAR_H]`，其直接圖片子圖層以英文命名並分別加上 `[TRACK]`、`[HANDLE]`。Unity 會自動建立 `Scrollbar`、保留 PS 內距的 `SlidingArea`，並接回父層 `ScrollRect`。

> 詞庫式圖層命名：`PhotoshopLayerAutoNamer.jsx` 改用 `PhotoshopExporter/naming_glossary.tsv`（納入版控的中英對照表）做最長匹配翻譯，推不出來的圖層**維持原名不動**並把未知詞寫進 `naming_glossary_todo.tsv`，補完再跑一次即可。編號規則為「同父群組 + 同譯名 = 同族變體」，產出如 `ranking_ui_frame01`、`ranking_ui_frame01_1`。

> 形狀遮罩：群組的直接子圖層加 `[MASK]`，該層形狀成為整個群組的裁切遮罩（Unity 掛 `Image` + `Mask`，`showMaskGraphic` 關閉），遮罩層本身不生成節點。僅非矩形（圓角／圓形／不規則）才需要——純矩形的 PS 圖層遮罩會自動掛更省的 `RectMask2D`。

---

## 工具清單

| 檔案 | 說明 |
|------|------|
| `PhotoshopExporter/PhotoshopUiPackageExporter.jsx` | 主要匯出工具，輸出 PNG + Layout JSON |
| `PhotoshopExporter/PhotoshopLayerAutoNamer.jsx` | 圖層批次英文命名 |
| `PhotoshopExporter/PhotoshopToolboxHub.jsx` | PS 工具箱啟動器（選用） |
| `PhotoshopExporter/InstallPhotoshopPlugin.jsx` | 將工具安裝到 PS Scripts 選單（選用） |
| `Assets/Editor/PhotoshopUiImporter/` | Unity Editor 匯入工具 |

---

## 主流程

1. 在 Photoshop 整理 UI PSD，圖層使用英文命名。中文命名的 PSD **必須**先執行 `PhotoshopLayerAutoNamer.jsx`：匯出器會刪掉圖層名裡所有非 ASCII 字元，沒轉換的中文名會塌成 `layer.png` / `layer_002.png`，語意完全遺失。
2. 執行 `PhotoshopUiPackageExporter.jsx`，指定 PNG 輸出資料夾與 Layout JSON 路徑，點 Export。
3. 在 Unity 開啟 `Tools > Photoshop UI Importer > Importer_v2`。
4. 選擇 Package 資料夾，點「套用 Package」。
5. 填寫「專案資料夾名稱」，點「套用標準輸出路徑」。
6. 指定「預設 TMP Font Asset」（UI 含文字時必填）。所有字型預設都會保留為 TMP；多字型 PSD 可另指定「字型對應表 TmpFontMap」（fontToken 關鍵字 → Font Asset）。只有明確勾選「白名單外字型改為 PNG」或使用 `[PNG]` 標記時，文字才會轉成圖片。
7. 點「Validate」確認，再點「Generate Prefab」完成。

> 開啟 PS 匯出工具視窗時不會預掃 PSD；圖層與選取狀態只在使用者互動時才讀取（例如勾選「把目前選取的文字圖層強制輸出為 PNG」），因此大型 PSD 也能快速顯示工具視窗。

> 圖層命名會觸發哪些 Unity 端行為（`[GRID]`、`[CG]`、`[SCROLL_V]`/`[SCROLL_H]`、`BTN_`…），見 PS 匯出對話框的「命名規則說明」按鈕；此按鈕會關閉匯出主視窗，並在瀏覽器開啟可搜尋的本機速查頁，可放在 Photoshop 旁邊，邊看邊修改圖層名稱。

> 群組名稱加入 `[MERGE]`，會將目前可見內容（含文字、效果、遮色片）烘成一張 PNG，Unity 只建立一個 Image 節點。子項需要互動、動畫、換皮或改字時不要使用。工具會逐組取得可見合成像素，存檔後立即釋放暫存層；操作上仍只需按一次 Export。
>
> 群組標 `[SCROLL_V]` / `[SCROLL_H]` 會在 Unity 自動組出 ScrollView > Viewport > Content 三層（ScrollRect + RectMask2D）。群組內圖層的遮色片視為「runtime 裁切預覽」——子圖層一律匯出完整圖；群組自身的遮色片（若有）定義可視窗範圍。可與 `[GRID]`/`[V]`/`[H]` 組合，排版元件會掛在 Content 上。
> 捲動群組可再加 `[SOFTMASK_BOTTOM=64]`，Unity 會用雙層原生 `RectMask2D` 只柔化底邊 64 像素，不需要額外 runtime 套件；`[SOFTMASK_Y=64]` 保留為相容別名。

> 圖片圖層可加 `[SLICED=32]` 使用四邊相同的九宮格，或 `[SLICED=左,上,右,下]` 分別指定 Border。Unity 會自動寫入 Sprite Border 並把生成的 Image 設為 `Sliced`；未加標記時，既有 Sprite Editor 手動 Border 仍會保留並自動使用 `Sliced`。

> 生成 Prefab 時會自動建立／更新 `Atlas/SpriteAtlas.spriteatlasv2`，所有圖片完成匯入後只打包一次。Atlas 上限會依最大來源圖自動選擇 2048／4096／8192。
>
> v2.13.1 起圖集改由「這次匯入**實際引用到的 Sprite 清單**」組成，不再掛整個資料夾。PS 的 Save for Web 對相同像素會產生不同 bytes，匯出器的 bytes 雜湊去重因此常抓不到；Unity 端的像素去重會抓到並把所有 `Image` 收斂到同一顆 canonical Sprite，但別名 PNG 會保留在磁碟上，讓同一份 layout JSON 仍可重複匯入。掛資料夾時那些別名仍會被打進圖集——實測一份排行榜 Package：43 張 PNG 只有 26 種不同像素，多出來的 258 KB 全部進了包。**副作用**：手動丟進圖集資料夾的 PNG 不再自動被收錄，要經過一次 Generate 才會進圖集。
>
> 「只換字體」需求走 `Tools > Photoshop UI Importer > Font Replacer`：分析 Prefab 的 TMP 字型/材質使用 → 一鍵替換，只寫 `font`/`fontSharedMaterial` 兩欄位，排版/字級/顏色/Sprite 全不動；描邊材質自動克隆到新字型。字型資產工廠可從專案內 .ttf/.otf 一鍵建 Dynamic SDF Font Asset 並自動登記 TmpFontMap；Importer 的「掃描 Package 字型」按鈕會列出每個 fontToken 的資產狀態（已對應／缺 Font Asset 可一鍵建立／缺字型檔）。

---

## 安裝

### Photoshop 端

將 `PhotoshopExporter/` 資料夾內的 JSX 複製到 Photoshop 的 Scripts 資料夾後重啟 PS：

```
Windows：C:\Program Files\Adobe\Adobe Photoshop [版本]\Presets\Scripts\
Mac：    /Applications/Adobe Photoshop [版本]/Presets/Scripts/
```

或直接透過 `File > Scripts > Browse…` 每次手動開啟 JSX。

### Unity 端

將 `Assets/Editor/PhotoshopUiImporter/` 複製到 Unity 專案的 `Assets/Editor/` 底下，Unity 自動編譯。需要 **TextMeshPro** 套件（Package Manager 安裝）。

---

## 版本更新

- **PS 端**：執行工具後，對話框點 **Check for Updates**，自動從 GitHub 下載最新 JSX。
- **Unity 端**：Importer_v2 視窗標題點 **從 GitHub 更新工具**，自動下載並觸發重新編譯。

---

## 文件

→ [完整使用說明（GUIDE_zh.html）](GUIDE_zh.html)（圖層命名規則、文字材質球、常見問題等）

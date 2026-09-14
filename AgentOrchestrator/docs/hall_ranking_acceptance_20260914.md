# Hall_Ranking 視覺驗收進度（2026-09-14）

本紀錄涵蓋 `Hall_Ranking.psd` 這一輪的 Photoshop 端實機證據，以及過程中發現並修正的兩個 exporter 缺陷。**Unity 端尚未執行**，因此不得宣告視覺驗收完成。大型 PSD 副本與 PNG 保存在 Git 忽略的 `AgentOrchestrator/runs/hall_ranking_visual_20260914/`，不提交公司素材。

## 驗收基準與隔離

- PSD 來源：`D:\Wayne\ACD_RD8\FishHunter_Client_Trunk\Hall_Ranking\PS\Hall_Ranking.psd`
- 外部素材：`D:\Wayne\ACD_RD8\FishHunter_Client_Trunk\Hall_Ranking\PS\images`（10 個 PNG）
- 來源與隔離副本的 SHA-256 均為 `c8623efabe25eed6f93ed9774408b137d4d6aea7daa28cb1ab10bd116821d919`
- 指定驗收 Unity 專案：`E:\ACD_RD8\FishHunter_Client_Trunk_02`（Unity 6000.0.67f1）

此 PSD 與 2026-09-14 另一份紀錄的 `廳館排行榜.psd`（SHA `bac9ebb1…`）不是同一個檔案，舊證據一律不沿用。

## Photoshop 端

| 檢查 | 結果 |
|---|---|
| 隔離副本 | PASS；副本與來源 SHA-256 相同 |
| 唯讀 inspection | PASS；run ID `cbddf850aecb4fe6915a080eb4f6b5de`；2340×1080、180 層、42 群組、43 文字層 |
| 正式 export | PASS；run ID `e72f1087a318454785f359f84b3ae582`；layout schema 2.11；39 個唯一 PNG；layout SHA `c29463cdf6cc8819d505e59aad9fbfd4754406bd2c457fd30a9d3e4dd6367144` |
| package 驗證 | PASS；layout 129 節點、39 參照／39 匯出、0 unreferenced、0 issues |
| 回歸測試 | 129 項 Python 測試通過 |

layout 組成：55 image、34 group、40 text。字型 token 為 `misans_semibold`(37)、`gensenroundedtw_m`(2)、`misans_demibold`(1)。

## 本輪發現並修正的兩個缺陷

### 1. 鎖定圖層被靜默丟棄（資料遺失）

首次匯出時 layout 參照 39 張圖但只寫出 37 張，缺 `1880_002.png`（1433×755）與 `1880_3.png`（1472×794）兩張大面板底圖，exporter 記為「空白/不支援：2」。

根因不是空白判定。以 Action Manager 查證，這兩層的 `protectAll=true`（全鎖定），而同群組能正常匯出的 `矩形 1880` 為 `false`。`layer.duplicate()` 會把鎖定旗標一併帶進暫存匯出文件，`alignActiveLayerToExportOrigin` 對全鎖定圖層呼叫 `translate()` 時丟出「使用者已取消操作」，被 `exportNodeImageFastDuplicate` 的 `catch (e) { saved = false; }` 吞掉，於是計為跳過；但該節點的 `imagePath` 仍留在 layout.json 裡，指向從未產生的 PNG。

修正：在對齊前呼叫新增的 `unlockLayerForExport(duplicatedLayer)` 清除複製體的鎖定旗標。複製體是暫存文件內的拋棄式副本，解鎖不會影響來源 PSD。

修正後跳過數 2→0、匯出圖層 53→55、去重算式回復一致（55−16=39），兩張缺圖實際產生（8,933 與 12,202 bytes）。

附帶查證：這兩層的 `fillOpacity` 為 0 且圖層效果啟用（描邊、外光暈、漸層覆蓋），畫面上確實有內容（複製後直方圖總數 1,081,915、透明裁切後 978×603）。此特性與缺陷無因果關係，但確認被丟棄的是真實美術內容而非空白。

### 2. 匯出收據假 PASS

`Invoke-PhotoshopUiExport.ps1` 原本把收據 `status` 寫死為 `PASS`，從不比對 layout 參照的圖是否真的寫出，因此上述資料遺失在收據層面完全看不出來（`imageCount: 37` 與 39 個參照並存而仍宣告 PASS）。

修正：收據改為比對 layout 參照圖名與實際寫出的 PNG，不符時 `status` 記為 `BLOCKED`、附上 `referencedImageCount` 與 `missingImages`，並讓腳本以明確訊息失敗。修正後已用刻意缺檔的狀態驗證會擋下，且缺陷修好後回到 PASS。

另註：`dedupPngsByHash` 以 `if (!file.exists) continue;` 靜默容忍缺檔，且在該檢查之前就累加 `originalCount`，是報告出現「55−16≠37」這類對不上數字的原因。本輪未更動此處。

## Unity 端（經使用者授權，覆蓋後還原）

使用者授權以「覆蓋 `_02` importer → 驗收 → 還原」方式進行。覆蓋前建立兩份經逐檔比對的備份（scratchpad 與 `runs/hall_ranking_visual_20260914/importer_backup_02/`，含 SHA-256 manifest）。`_02` 的 Unity 編輯器全程開啟，因此不走 batch 模式，改以 Unity MCP 在該編輯器內執行，與前一輪驗收做法一致。

| 檢查 | 結果 |
|---|---|
| importer 同步 | v2.15.0 編譯通過；`PhotoshopUiImportService`／`PhotoshopUiBatchEntryPoint`／`PhotoshopUiImportRequest`／`PhotoshopUiPrefabNodeSnapshot` 均可在載入的 assembly 中找到；Console 0 errors。`_02` 獨有的 `SpriteAtlasService` 一併保留且可編譯（其呼叫端為 `PhotoshopUiImporterWindow` 的 private 成員，隨該檔一併替換，無懸空參照） |
| 語意 manifest | 55 筆 entry 全部 PASS、renderMode 全為 `simple`；**本 case 無待決的 reskin 人工 render 決策** |
| deterministic import | PASS；run ID `hallrank-v215-mcp-20260914-03`；Prefab `Assets/Temp/HallRankingAcceptance_v215/Prefab/Hall_Ranking.prefab`；130 prefab 節點、55 images、40 TMP；outline 警告 0、fontToken 警告 0 |
| 圖片綁定 | 55/55 image binding 全部綁到 sprite，0 缺漏。本輪修好的 `1880_002.png` 與 `1880_3.png` 均已匯入並綁定，確認 exporter 修正一路貫通到 Prefab |
| **Pipeline Validator 結構** | expected 129 = actual 129；**0 幾何／階層／圖片不符** |
| **Pipeline Validator 總判定（補上 TmpFontMap 後）** | **PASS**；0 blocking、0 review、0 issues；run ID `hallrank-v215-mcp-20260914-06`，130 prefab 節點、fontToken 警告 0、errors 0 |

還原後逐檔比對與備份完全一致（40 檔、ToolVersion 回到 2.14.0、三支 v2.15.0 檔案移除、`SpriteAtlasService` 保留），Unity 重新編譯後 Console 0 errors。驗收產物保留於 `Assets/Temp/HallRankingAcceptance_v215/`。

### 字型門檻的解法

`_02` 僅有 `MiSans-Demibold SDF`，起初看似缺少 layout 需要的 `MiSans-Semibold`（40 個文字中 37 個使用）與 GenSenRounded。第一次匯入時 importer 以 `TMP_DEFAULT_FONT_REQUIRED` 擋下；僅指定 MiSans-Demibold 為預設字型後匯入雖 PASS，但 Pipeline Validator 以 40 個 `TMP_FONT_TOKEN_FALLBACK` 阻擋——這是「帶 fontToken 的文字不得默默退回預設字型」的守門行為，屬正確結果。

實際查證後確認這不是缺件，而是專案的既定整併。`Assets/Editor/MiSansFontTakeover.cs` 記載並已執行：MiSans 的位元組原地覆蓋到 GenSenRounded 的 ttf 上，`MiSans-Demibold SDF.asset` 保留 GenSen 的 GUID 與圖集 fileID，使 573 顆材質球／968 個 Prefab／178 個舊版 Text 零改動。實機查證該資產：`faceFamily=MiSans`、`faceStyle=Demibold`、`pointSize=25`、atlas `2048×2048`、`sourceTtfGuid=9d8efb7e56ebc32478992b3d7c854724`（即腳本註解所述保留不動的 GenSen ttf guid）、已烘 894 字符，與腳本宣告完全吻合。

因此在此專案中 `misans_semibold`、`misans_demibold`、`gensenroundedtw_m` 三個 token 本就應解析到這唯一資產。建立 `Assets/Temp/HallRankingAcceptance_v215/TmpFontMap_HallRanking.asset`（`fontKeyword` 為 `misans` 與 `gensen`，皆指向該資產，三個 token 實測全部解析成功）後重跑匯入，fontToken 警告為 0，Pipeline Validator 轉為 PASS。

## 視覺逐像素對照

以附加的暫存場景（不影響開啟中的場景）將 Prefab 掛在 2340×1080 的 ScreenSpaceCamera Canvas 下渲染為 PNG，與同尺寸的 Photoshop 合成圖比較，兩者先合成到白底再取 RGB 三通道的最大差值。

| 區域 | 平均色差 | >8 | >16 | >32 | >64 |
|---|---|---|---|---|---|
| 全畫面 | 31.20 | 44.19% | 32.29% | 22.81% | 14.73% |
| 文字區域（佔畫面 11.89%） | 50.29 | 69.46% | 51.52% | 37.19% | 25.94% |
| 非文字區域 | 28.62 | 40.78% | 29.70% | 20.87% | 13.22% |

排除系統性成因：以 −3..+3 全域位移搜尋，(0,0) 即為最佳（31.20），無位移；垂直翻轉使差異惡化至 89.56，無翻轉；專案為 Linear 色彩空間，但線性↔sRGB 三種轉換組合均使差異惡化（76.78／61.32／65.99），非 gamma 問題。此與 Validator 的 129/129 幾何相符互為佐證。

差異圖顯示差異集中於三處：所有文字呈雙重疊影（MiSans-Semibold 對 Demibold 的字重差）、各面板與列的邊緣次像素抗鋸齒、以及面板底板外框的柔光邊緣；面板以外的背景區域完全一致。

結論：排版與素材正確，殘差來自專案自身已接受的字型整併與邊緣抗鋸齒，非工具缺陷。此門檻已執行並量化，但因字重替換屬既定設計取捨，不構成嚴格的像素級相等。

## 尚未執行

- **Pointer drag／Elastic rebound**：此 PSD 無法驗收。`Hall_Ranking.psd` 沒有任何 `[SCROLL_*]`、`[MASK]`、`[HANDLE]` 標籤圖層，不會生成 ScrollRect，沒有可拖曳對象。結此門檻需另一份含 scroll 群組的 PSD。

## 其他發現

- 31 個 `NON_ASCII_LAYER_NAME` 警告：此 PSD 未先執行 Layer Auto Namer，中文圖層名退化為 `layer_NNN` 流水號，語意遺失。
- `PSD_PRIMARY_ROOT_AMBIGUOUS`：此 PSD 不符合 `<PageName>/UIWindow/Animation` 的生產根結構樣板。

因此目前可宣告「Hall_Ranking 的 Photoshop 匯出閉環 PASS、Unity deterministic 結構閉環 129/129 相符且 Pipeline Validator PASS、reskin 決策無待決項、視覺逐像素對照已執行並量化，且修正兩個會造成靜默資料遺失的 exporter 缺陷」。

仍不可宣告完整視覺驗收結案：Pointer drag／Elastic rebound 因素材不含 scroll 群組而未驗，且逐像素殘差雖成因明確，並非像素級相等。`liveAcceptance` 依設計維持 `NOT_RUN`——Pipeline Validator 的 PASS 不等同最終實機視覺與操作驗收。

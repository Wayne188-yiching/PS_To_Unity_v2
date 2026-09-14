# Agent Pipeline 實機驗收進度（2026-09-14）

本紀錄是 v2.15.0 Agent／Pipeline Validator 的階段性真機證據，不代表所有模型 Runner 與視覺操作驗收均已結案。本機大型 PSD、PNG、Prefab 與完整收據保存在 Git 忽略的 `AgentOrchestrator/runs/rank_acceptance_20260913_v215/`，不提交公司素材。

## 驗收基準與隔離

- 分支：`codex/psd-agent-acceptance-hardening`。
- PSD：`C:\Wayne\PSD\廳館排行榜\廳館排行榜.psd` 的乾淨副本；來源與副本 SHA-256 均為 `bac9ebb1222632c26451d7a340c5d013692e58960813b7e834f3cb9745f50e9e`。
- Unity：2022.3.62f1；驗收輸出隔離於 `Assets/Temp/RankAcceptance_v215/`。
- Unity importer 先同步為 v2.15.0，並由 Unity MCP 執行 AssetDatabase refresh／重編譯；Console 為 0 errors、0 warnings，實際載入 assembly 可找到 `PhotoshopUiImportService`。

## Photoshop 與 package

| 檢查 | 結果 |
|---|---|
| 唯讀 inspection | PASS；run ID `41688806236947008dc2d75d5f141a4b`；2340×1080、178 nodes、47 groups、49 text layers |
| 可見文字 | 40 層；39 個 MiSans-Semibold、1 個 MiSans-Demibold。其餘 9 個文字位於隱藏分支，不輸出 |
| 正式 export | PASS；run ID `45b3344bd0d240a0b14980b2037fe70a`；layout schema 2.11；24 個唯一 PNG；layout SHA `651190af29ccae62c8637faba112a04ce09a8868c0063874d61b6e4ef0406565` |
| 透明空白檢查 | 24 個 exporter PNG 與 8 個外部排行素材的 alpha bounding box 均貼齊實際圖片邊界，未發現透明 padding |
| 精準 PS 輸出 package | PASS；43 個 image node 全部使用本次 exporter 像素，0 issues |

外部 `09_通用廳館排行` 素材路徑另被 reskin guard 正確標成 `NEEDS_REVIEW`：10 個使用點的來源像素尺寸與 PS 目標框不同，其中小底板可提出高信心 9-slice 建議，但大型排行底板仍不可自動猜測。此分支沒有被靜默核准；本次精準 PS 對照使用 exporter 當次像素，保留 guard 證據供後續 reskin 驗收。

## Unity MCP 與 Pipeline Validator

| 檢查 | 結果 |
|---|---|
| MCP transport | framed TCP handshake 與 `pong` PASS |
| deterministic import | PASS；run ID `rank-v215-mcp-20260913-01`；Prefab `Assets/Temp/RankAcceptance_v215/Prefab/Rank_layout.prefab` |
| 生成統計 | 111 個 IR nodes、43 images、40 TMP；0 errors、0 outline warnings、0 font-token warnings |
| TMP | 40/40 皆為 TMP 且以 TmpFontMap 解析；MiSans-Semibold／MiSans-Demibold Font Asset 與現有 outline 材質綁定 |
| 隱藏圖層 | 隱藏文字與隱藏分支未輸出；Pipeline Validator 未發現 hidden node leakage |
| ScrollRect | vertical、Elastic、inertia、deceleration 0.135、sensitivity 50；Viewport 具 RectMask2D 與 raycast Image |
| 外置 Scrollbar | 正確接到 verticalScrollbar；Handle 與 targetGraphic 完整，方向 BottomToTop |
| Handle 動態測試 | Unity MCP 實例化 Prefab 並強制 layout；value 0→1 時 ScrollRect normalized position 0→1、Handle world Y 移動 84.58548 px |
| Pipeline Validator | PASS；expected 111、actual 114（含 Viewport／Content／SlidingArea deterministic helpers）；0 blocking mismatches、0 review items |

Importer 的 6 個 warning 中，5 個是 `[V]` 子節點交叉軸不共中心，已安全降級為普通群組以保留 Photoshop 絕對排版；1 個是未指定單一預設 TMP 材質的建議，實際 40 個文字均有 Font Asset／材質綁定。

## 本輪發現並修正

Pipeline Validator 原先把已接線的 `[HANDLE]` 當成普通靜態圖片，比對 reparent 前的 Photoshop rectangle。Unity 為可拖曳會把 Handle 放入生成的 `SlidingArea`，位置與尺寸改由 Scrollbar runtime anchors 控制，因此造成假性 `PREFAB_GEOMETRY_MISMATCH`。

修正後僅在以下兩項同時成立時豁免靜態幾何：IR 節點角色為 `handle`，且某個實際 `Scrollbar.scrollbarHandlePath` 精確指向該 snapshot。未接線或偽標記 Handle 仍會被阻擋；Track 與所有其他圖片仍嚴格比對。新增正反向 regression 後，全套 119 項 Python 測試通過。

## 尚未結案

- 模型 Runner 的 ambiguity／NEEDS_REVIEW 語意案例仍需可用 provider 設定後再驗收；這不影響本次 deterministic Photoshop／Unity／Validator 證據。
- Unity 視覺截圖逐像素對照、實際 Pointer drag／Elastic rebound、reskin 10 個人工 render 決策仍未完成。
- 因此目前可宣告「Agent deterministic core 與排行榜結構閉環 PASS」，不可宣告完整 Multi-Agent end-to-end 最終驗收完成。

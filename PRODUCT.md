# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

- 遊戲 UI 設計師：在 Photoshop 完成介面後，需要把設計交付到 Unity。
- Unity UI 開發者：需要可維護、可換字、可換圖並保留階層的 uGUI Prefab。
- 發表觀眾：不必了解 Photoshop 腳本或 Unity Editor，也能在短時間內理解工具價值與流程。

## Product Purpose

將 Photoshop 排版完成的遊戲 UI，轉換為 Unity uGUI + TextMeshPro Prefab；減少逐層切圖、重新定位、重建文字與元件階層的重複工作，同時保留後續編輯能力。

## Positioning

工具同時處理 Photoshop 端的像素與版面語意，以及 Unity 端的 Sprite、TMP、遮罩、排版、捲動與圖集建置；輸出不是一張不可編輯的截圖，而是可繼續製作的 Prefab。

## Operating Context

1. 在 Photoshop 整理 PSD 圖層並依需要加上命名標記。
2. 匯出 PNG Sprite 與 Layout JSON。
3. 在 Unity 驗證 Package、指定字型並生成 Prefab。
4. 在 Unity 繼續換字、換圖、動畫與程式串接。

## Capabilities and Constraints

- 文字預設保留為 TextMeshPro；非文字圖層輸出為 PNG Sprite。
- 支援圖層自動英文命名、Grid／Layout、ScrollRect、遮罩、九宮格、Sprite Atlas、像素去重、多字型對應與字型替換。
- 依圖層命名與標記表達特殊行為；Unity 專案需有 TextMeshPro。
- 工具會在無法安全還原特定排版或捲動結構時警告並降級，避免產生看似成功但版面錯誤的 Prefab。

## Brand Commitments

- 名稱：Photoshop To Unity V2。
- 報告使用繁體中文，文字精簡、讓非技術觀眾能理解。
- 視覺配色嚴格遵守 60／30／10，重要資訊必須有明確層級與色彩提示。

## Evidence on Hand

- `README_zh.md` 與 `GUIDE_zh.html`：目前流程與功能說明。
- `version.json`：目前版本 v2.13.3，更新日期 2026-08-17。
- `Samples/Phase4_5/`：ScrollRect 驗收 PSD 與說明。
- 已記錄的實例：43 張 PNG 經像素去重後收斂為 26 種不同像素，避免 258 KB 重複內容進入圖集。
- 未提供可公開引用的客戶名稱、使用人數、節省工時比例或商業成效；報告不得自行捏造。

## Product Principles

- 設計到 Prefab 的路徑要短，而且能重複執行。
- 能編輯的內容保持能編輯；只有必要時才烘成圖片。
- 無法安全重建時明確警告與降級，不以錯誤輸出假裝成功。
- 用真實 PSD 與 Unity 結果驗證，而不是只做紙上規格。

## Accessibility & Inclusion

- 報告需兼顧投影與筆電閱讀，文字與背景達到清楚對比。
- 尊重 `prefers-reduced-motion`，互動與導覽可使用鍵盤完成。

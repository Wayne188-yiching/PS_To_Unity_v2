# PS_To_Unity_v2 Agent Architecture Progress

以 Vite、Vanilla HTML/CSS/JavaScript、GSAP 與 ScrollTrigger 製作的離線 Scroll-Driven Presentation。

## 啟動

```powershell
cd AgentProgressPresentation
npm install
npm run dev
```

瀏覽器會顯示 Vite 提供的本機網址。簡報不需要外部 CDN、字型服務或網路圖片。

## Build

```powershell
npm run build
npm run preview
```

正式輸出位於 `dist/`。`vite.config.js` 使用相對 base，所有必要的 JavaScript、CSS、GSAP 與 Report CJK 字型都會輸出到本地。

若要直接雙擊、不啟動本機伺服器，請開啟：

```text
dist/PS_To_Unity_Agent_Report_Offline.html
```

根目錄的 `index.html` 以 `file://` 開啟時，也會自動轉到這個單檔離線版本。

## 目錄結構

```text
AgentProgressPresentation/
  index.html   轉址頁：GitHub Pages 的資料夾入口，只含 inline script
  app/         Vite root：簡報原始碼（index.html 與 src/）
  dist/        build 產物，實際對外發布的版本
  scripts/     離線打包與驗收腳本
```

入口 `index.html` 刻意不引用任何外部 script、stylesheet 或圖片。瀏覽器的 preload scanner
會在轉址生效前先抓取子資源，因此只要這個檔案出現一個 `<script src>`，每位訪客都會多出一個
被取消的請求；把 Vite 進入點放進 `app/` 就是為了讓轉址頁保持乾淨。

## 驗證

```powershell
npm run verify        # dist/ 掛在網域根目錄 + file:// 離線版
npm run verify:pages  # GitHub Pages 專案子路徑（/PS_To_Unity_v2/）
npm run verify:trace  # scroll 驅動的三條連線
```

三者都需要系統已安裝 Chrome。`verify:pages` 以正式網域與 base path 重播三個入口
（`AgentProgressPresentation/`、`.../index.html`、`.../dist/`），
任何 404、失敗請求、console 錯誤或外部連線都會讓它失敗。

`verify:trace` 檢查三條 scroll 驅動的連線：靜止時完全不亮、填充隨捲動單調遞增、
最終確實填滿、改變視窗大小後仍正確。這些線用 `vector-effect: non-scaling-stroke`，
dash 單位是螢幕像素而非 viewBox 單位；混用 `getTotalLength()` 的 viewBox 單位會讓
虛線蓋不滿線段，圖樣重複後在右端露出跑在前面的亮段。

## 操作

- 滑鼠滾輪或 trackpad：推進／倒帶動畫。
- `Arrow Up` / `Arrow Down`：小幅推進／倒帶。
- `PageUp` / `PageDown`、`Space`：以接近一個 viewport 的距離推進。
- `Home` / `End`：跳到開頭／結尾。
- 右側章節 rail（寬螢幕）或右上角章節編號：確認目前位置。
- 頂欄 `MOTION FULL / REDUCED`：切換完整 ScrollTrigger 動畫與靜態閱讀模式。
- 系統啟用 `prefers-reduced-motion` 時預設採靜態模式；仍可用頂欄按鈕暫時切回完整動畫。

## Evidence boundary

直接來自 repository：

- deterministic Photoshop → `layout.json` → Unity Prefab pipeline 與既有 Phase 4.5 acceptance。
- AgentOrchestrator runtime contract、PSD Controller、Evidence、Structure Plan、fingerprint、checkpoint 與 terminal states。
- repo 追蹤的 `Samples/Phase4_5/scroll_v_basic.psd` demo 結構。
- 本次 worktree 的 56/56 Python tests、26/26 Photoshop naming checks 與 PNG visual dedup regression。

視覺化／示意：

- 節點位置、連線拓撲、evidence console 與狀態 transition 是為說明架構而製作的 diagram。
- `scroll_v_basic` 的 Unity target 是既有 deterministic acceptance 的預期結構，不是本次 Agent run 的新結果。

不能宣稱完成：

- PSD Agent 的 Photoshop 真機 acceptance 尚缺 inspection、apply、第二次 apply、re-inspection 與 export evidence。
- Unity Agent 尚未開發；目前只有可程式呼叫的 deterministic Unity import service / batch entry point。
- Pipeline Validator 只有 PSD package gate，尚未比較 semantic intent → IR → Unity import → generated Prefab。
- Director 只有角色骨架與 PASS guard，Multi-Agent end-to-end validation 尚未完成。
- QC_Agent / UI outsourcing workflow 是另一套系統，不屬於 PS_To_Unity runtime。

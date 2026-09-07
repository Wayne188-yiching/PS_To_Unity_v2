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

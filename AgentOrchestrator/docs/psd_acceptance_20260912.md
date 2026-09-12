# PSD Agent hardening 驗收紀錄（2026-09-12）

程式基準：`dcf9a4a`；已合併 GitHub `main` 的 `e36db91` 至 `f13825e`。開發子任務使用 GPT-6 Astra／medium，獨立驗收使用 GPT-5.6 Sol／medium。

## 結論

PSD deterministic automation 與 Controller 復原流程已取得真機證據。PSD Agent 整體仍為 **驗收中**：尚未執行實際模型 `Runner` 的語意規劃驗收，不能進入 Unity Agent 開發。

## 自動驗證

- 76 個 Python／Node 行為測試通過。
- JSX 語法與 PowerShell parser 檢查通過。
- Git whitespace 檢查通過。
- Node 測試直接執行 production JSX 函式，涵蓋一般圖層／群組、rename/move 兩種順序、重複套用及錯誤 companion 目標；此為 DOM 模擬，不取代 Photoshop 存檔證據。

## Photoshop 真機

工作副本由 `Samples/Phase4_5/scroll_v_basic.psd` 建立，位於本機忽略路徑 `AgentOrchestrator/runs/scroll_v_basic_acceptance_codex/`。測試 plan 為明確人工設計的四動作 fixture，用於驗證安全套用；不是模型語意推論成果。

| 檢查 | 實際結果 |
|---|---|
| 原始 inspection | 1920 × 1080；8 層、3 群組 |
| 首次 Apply | PASS；8 → 9 層；新增群組 1、改名 1、移動 2 |
| Repeat Apply | PASS；9 → 9 層；三種修改均為 0；alreadyAppliedCount = 4 |
| 最終 companion 修正版 Apply | PASS；9 → 9 層；三種修改均為 0；alreadyAppliedCount = 4 |
| 排序 | MainPage 保持在 BackgroundEmpty 上方；Row_04、Row_03、Row_02、Row_01 顺序未改 |
| Controller 復原 | 注入「PS 已保存、主程序未收到回執」的 APPLYING checkpoint；真實 Controller 重跑辨識 4 動作已完成，進入 STRUCTURE_APPLIED |
| 中途工具失敗 | Python 子程序中的 Windows PowerShell 無 Get-FileHash；修為 .NET SHA256 後由 STRUCTURE_APPLIED 續跑，無重套 plan |
| package 驗證 | PASS；7 個 layout nodes、4 張引用 PNG、4 張輸出 PNG、0 張未引用 |
| Controller 終態 | PACKAGE_READY / NEEDS_REVIEW；原因 SCROLL_EMPTY，樣本含 Empty[SCROLL_V] |

本次是 checkpoint fault injection，未強制終止 Photoshop 程序。原始 PSD 未被修改。

歷史證據注意：此 run 的 `psd_structure_plan_validation.json` 保留了核准持久化修復前的未核准快照；最終 controller state、apply report 與 export receipt 已複核一致。新程式的核准後同步持久化由 regression 證明，本次歷史 run 不作為完全自洽的正式交付包。

可核對的 SHA-256：

- 原始 PSD：`f0cdc33bbeeb63a10b6d5735df654048dcd74b186cfb4a9d486d7fddd42c1db1`
- 完成套用副本：`f2e28ddf51e10774d2c76457707652075297794230f4b5a666ff4e65b0256d45`
- plan（不含 approved）：`54902340f9db25543c68f392b792652fd71f68286adb8805ffe9ac9dab1f51cd`
- layout：`9f573f6ace3bff2f16aad6269c3332406f6bfb81b0b6c20715b9a90de359c9d3`
- 最終 export run ID：`f21dc26028544586a7763a4372f915f2`

## 已修復的實際缺陷

1. Pipeline Director 誤用獨立外包 approval gate。
2. 核准後、apply 前 PSD 已改變仍套用舊 plan。
3. PACKAGE_READY 只檢查 PSD，已刪除／竄改的 package 仍被標為 PASS。
4. 舊 inspection/export 缺 run ID 仍可重用；export 現另綁 layout 與 PNG 雜湊。
5. Photoshop global result 跨 DoJavaScript 呼叫丟失；改於同一次呼叫取回。
6. create/move 組合循環、duplicate layer IDs、rename 同層衝突。
7. review 未綁圖片像素、接受非有限九宮格值及舊瀏覽器決策。
8. approval validation/result 未同步持久化。
9. 秒級 backup 名稱可能衝突，已加入毫秒與隨機識別碼。

## 未完成項目

- 實際模型 Runner 的語意規劃、模糊 intent escalation、真實 PSD 多案例與文字／hidden／MERGE 語意驗收。本次環境與兩個 checkout 均未設定 OPENAI_API_KEY；不以工具層測試替代模型驗收。
- 完整程序中斷 fault injection 與更廣 Photoshop fixture matrix。
- PSD 階段通過後才進行 Unity headless pipeline、Unity Agent、Pipeline Validator 與 Director end-to-end。

本次不改動 Unity importer，不宣告 Multi-Agent end-to-end PASS，也不發布為正式完成版。

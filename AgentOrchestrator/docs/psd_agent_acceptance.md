# PSD Agent Acceptance Criteria

PSD Agent 只負責處理不確定語意、提出結構計畫與選擇既有 Photoshop deterministic tools。它不重新實作匯出器，也不能在未核准時修改 PSD。

## 通過條件

1. **唯讀規劃**：第一次執行只讀取可見圖層的結構化 inspection；PSD 與圖片內容不得傳給模型，輸出的 plan 一律強制為 `approved=false`。
2. **穩定定位**：rename、move 使用 Photoshop layer ID；每個機械動作都綁定原名稱、原 parent 與 layer kind 前置條件。
3. **安全結構**：拒絕找不到或重複的 layer ID、同名 group 衝突、自己／子孫循環 move、rename no-op；同層 move 按由下到上的穩定順序執行。
4. **人工核准**：核准前重新 inspection；PSD、inspection 或 plan 任一指紋改變，必須回到 `NEEDS_REVIEW`，不得套用舊核准。
5. **冪等與復原**：套用前先保存 `APPLYING` checkpoint；若 Photoshop 已存檔但主程序中斷，相同 plan 再次執行只能確認已完成，不得重複 create、rename 或 move。
6. **可靠 retry**：可辨識 Photoshop busy、RPC 與 timeout 為 `FAIL_RETRYABLE`；語意／前置條件衝突為 `BLOCKED`，不得盲目重試。
7. **新鮮證據**：inspection、export result 必須含產出當次 run ID 與 PSD SHA-256；工具呼叫必須核對同次回傳 ID。快取可重用前次成功證據，但 PSD SHA 必須相同；export 另外核對 layout 與所有 PNG 的 SHA-256。缺少或不相符的舊檔不能當作成功。
8. **匯出閉環**：套用後重新 inspection，再由既有 exporter 產生 Images 與 `layout.json`；引用圖片缺失、非法路徑、重複 node path 或非 ASCII runtime 名稱均阻擋。
9. **人工 render 決策**：Simple／Sliced 的人工核准必須綁定 review fingerprint；舊決策不可套到新 manifest。Mask、Scroll、LayoutGroup 仍須透過 structure plan，不由 review 決策暗改結構。
10. **責任邊界**：PSD package 驗證後只可交給下一階段；尚未存在完整 Unity Prefab 對照時，Director 必須回傳 `NEEDS_REVIEW / PIPELINE_VALIDATOR`，不能宣告全流程 PASS。

## 必要證據

- 全部自動回歸測試通過。
- Photoshop 真機在乾淨 PSD 副本完成 inspection、plan validate、apply、重跑 apply、re-inspection 與 export。
- 第二次 apply 的 `alreadyAppliedCount` 與動作數相符，PSD 不再產生額外結構變動。
- `photoshop_result.json.psdSha256` 等於完成匯出時 PSD 的實際 SHA-256。
- 原始受版本控制 PSD 的 SHA-256 在驗收前後相同。

## 尚不屬於 PSD Agent 通過範圍

- Unity Prefab 生成與 Unity Editor headless pipeline。
- PSD semantic intent 到 generated Prefab 的完整 Pipeline Validator。
- 獨立 `QC_Agent`／UI 外包驗收職責。

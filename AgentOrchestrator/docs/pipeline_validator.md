# Pipeline Validator

Pipeline Validator 是 PS_To_Unity_v2 內部的一致性角色，不是獨立 `QC_Agent`，也不處理外包規格、傳檔或供應商品質驗收。

## 驗證鏈

`PSD semantic intent → layout.json / IR → Unity importer receipt → generated Prefab`

Unity deterministic importer 會在同次生成收據內輸出 Prefab 結構快照。Controller 只接受與目前 runId、request fingerprint、layout、依賴與 Prefab SHA-256 相符的證據；Validator 再以目前 PSD package 重建結果比對：

- 可見節點是否以正確核心元件生成，隱藏節點是否未輸出。
- 非 LayoutGroup 控制的圖像／文字 Rect 是否保存 Photoshop 座標與尺寸。
- Image 是否綁定 Sprite；Sliced／Button 語意是否存在。
- 文字是否為 `TextMeshProUGUI`，且內容、字級、字距、行距、對齊、顏色／漸層一致；Font Asset／材質必須和 importer 的解析證據一致，帶 `fontToken` 的文字不得默默退回預設字型。
- Mask、RectMask2D、CanvasGroup 與 LayoutGroup 元件是否符合 IR。
- ScrollRect 的方向、Viewport／Content 接線是否存在。
- Scrollbar 是否有 `Scrollbar` 元件與可拖動的 handleRect 接線，並接到包含它的 ScrollRect 正確軸向；孤立 scrollbar 只有在全 Prefab 唯一可配對時才可通過。
- 唯一命名節點的 authored parent hierarchy 是否保持；重複名稱不猜測，回傳 NEEDS_REVIEW。

## 終態

- `PASS`：目前結構化證據一致；`validationScope=PSD_IR_TO_GENERATED_PREFAB`。
- `NEEDS_REVIEW`：重複 identity、Unity 語意警告或其他不能安全判定的項目。
- `BLOCKED`：過期／缺少證據、節點或元件遺失、隱藏素材被輸出、非 layout 幾何漂移、TMP／Sprite 未綁定。
- `FAIL_RETRYABLE`：沿用 Unity generation 的明確暫時失敗，不自行重開仍在執行的 Unity。

`liveAcceptance=NOT_RUN` 必須保留：這個 Validator 的 PASS 不是 Photoshop 與 Unity 實際畫面、字體觀感、遮罩效果或滑軌拖曳的最終驗收。

## 入口

從 `AgentOrchestrator` 執行：

```powershell
# 不呼叫模型；若通過執行與人工核准 gate，會先取得同次 Unity 生成證據再驗證。
uv run python main.py pipeline-controller --request <request.json>

# 使用 Pipeline Validator Agent 解讀同一份 deterministic 結果。
uv run python main.py pipeline --request <request.json>
```

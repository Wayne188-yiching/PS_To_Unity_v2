# Unity Agent / Controller

本階段優先實作，完整模型與 Unity 真機驗收留到最後。生成成功不等於 PSD 到 Prefab 視覺／語意一致性已通過。

## 單一生成路徑

EditorWindow、Agent、Batch／CI 應共用 `PhotoshopUiImportService.Execute(PhotoshopUiImportRequest)`。此 service 呼叫既有 ImageImportService、TmpMapper、SkinResolver、Atlas helpers 與 UGuiTmpPrefabBackend；Agent 不重新實作 Sprite、TMP、9-slice、ScrollRect、Mask、像素去重或 Prefab 生成。

Unity Agent 負責理解生成需求、選擇工具、解讀 diagnostics 與要求人工處理。UnityAgentController 負責可明確執行的前置檢查、批次程序、回執核對和有限重試。模型宣告 PASS 必須另由 Controller 的真實生成證據限制；全流程 PASS 仍受 Pipeline Validator gate 限制。

## 執行入口

從 `AgentOrchestrator` 目錄呼叫：

```powershell
# deterministic Controller，不需要模型 API Key。
# analyze 或未核准 request 不得啟動 Unity。
uv run python main.py unity-controller --request <request.json>

# 模型選擇工具／解讀診斷，使用同一個 Controller。
# 此入口才需要目前 Runner 的 API 設定。
uv run python main.py unity --request <request.json>
```

只有 request 明確設定 `execution_mode=execute`、`semantics_approved=true` 且通過 package 與依賴檢查，才可呼叫 Unity 批次生成。人工核准不能由模型自行補上。

## Request 設定

- `unity_executable`：Unity 執行檔。
- `unity_project_path`：既有 Unity 專案，必須已安裝同版本 importer；不要指向使用者正在操作的專案做並行 Batch。
- `unity_import_folder`、`unity_prefab_folder`：專案內 Assets 相對路徑。
- `unity_project_folder`、`unity_prefab_name`：生成模組資料夾與 Prefab 名稱。
- `unity_default_tmp_font_asset`、`unity_default_tmp_material_preset`、`unity_tmp_font_map`：既有 TMP 資產；有 text node 必須提供預設 Font Asset。此流程不默默下載或換字體。
- `unity_skin_map`、`unity_material_library_folder`：選用但明確指定時必須存在。
- `unity_reference_resolution_x/y`、`unity_use_responsive_anchor`、`unity_outline_thickness_multiplier`、`unity_create_sprite_atlases`：對應既有 deterministic 生成選項。
- `unity_timeout_seconds`：單次等待上限，不代表逾時就能另開一個 Editor。

## 證據與失敗邊界

每次生成在獨立 `unity_runs/<runId>/` 保存 request、result 與程序 log。回執必須對上此次 run／request／layout，並核對產生的 Prefab 路徑與檔案雜湊；舊檔、模型文字或僅有程序 exit code 不能取代產物證據。Controller 也會對來源圖片、明確指定的 Font Asset／材質／Map（含 `.meta`）及材質庫內容建立 SHA-256 快照，生成期間任何輸入變動都會使舊回執失效。

語意不明或尚未核准維持 NEEDS_REVIEW；缺依賴／非法路徑／錯誤證據為 BLOCKED。只有已辨識的暫時失敗允許有限重試。逾時後仍活躍的程序必須追蹤同一次執行，不能以檔案鎖或等待逾時假設它已結束。

本階段 basic artifact gate 不等於完整 Pipeline Validator。PSD intent、IR、Unity 元件／幾何／文字／圖像的完整對照與互動驗收尚需後續實作及最後驗證。

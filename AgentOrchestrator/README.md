# PS_To_Unity_v2 Agent Orchestrator

這裡只協調 PS_To_Unity pipeline。Photoshop JSX 與 Unity C# 是 deterministic Core；Agent 處理語意不確定性、規劃、工具選擇、診斷與人工核准。

## 目前實際完成度

| 元件 | 狀態 | 說明 |
|---|---|---|
| PSDToUnity deterministic Core | 已存在 | 既有 exporter、Sprite、9-slice、TMP、ScrollRect、Mask、Atlas、Prefab 功能不由 Agent 重寫。 |
| PSD Agent／Controller | 第一版完成，驗收強化中 | 具備 inspection、structure plan、人工核准、checkpoint、冪等套用、重新匯出及 package validation。 |
| Evidence／Structure Plan | 已存在，持續驗證 | 已加入 PSD／inspection／plan 指紋與動作前置條件。 |
| Pipeline Validator | 僅有 PSD package gate | 尚未比較 PSD semantic intent、IR、Unity import 與 generated Prefab；因此 Director 不得回傳全流程 PASS。 |
| Unity Agent | 尚未開發 | 下一階段先抽出可程式呼叫的 Unity deterministic pipeline，再建立 Agent。 |
| Director end-to-end | 僅有骨架 | 已能守住 terminal state，但閉環要等 Unity Agent 與 Pipeline Validator。 |

PSD Agent 的正式通過條件見 [docs/psd_agent_acceptance.md](docs/psd_agent_acceptance.md)。

## 資料夾

| 資料夾 | 用途 |
|---|---|
| `agent_roles/` | pipeline Agent 角色與輸出 gate |
| `ps_to_unity_agents/` | inspection、evidence、plan、controller、review 與驗證工具 |
| `cases/` | 不含內部資料的安全範例 |
| `data/` | PSD／Unity 規則與唯讀稽核資料 |
| `docs/` | runtime contract 與 acceptance criteria |
| `tests/` | deterministic regression tests |
| `runs/` | 本機驗收輸出；Git 忽略 |

## 開發與回歸測試

在 `AgentOrchestrator` 執行：

```powershell
uv sync
uv run python -m unittest discover -s tests
```

## PSD Agent 乾淨驗收

先建立不會修改版本庫樣本的工作副本：

```powershell
$request = .\cases\examples\psd\Prepare-AcceptanceCase.ps1 -RunName scroll_v_basic_acceptance_01
uv run python main.py psd-controller --request $request
```

第一個命令只產生未核准 plan。人工檢查 `psd_structure_plan.json` 後，才可執行：

```powershell
uv run python main.py psd-controller --request $request --approve-plan
```

若 Photoshop 暫時忙碌、RPC 被拒或逾時，結果會是 `FAIL_RETRYABLE` 並保留 `APPLYING` checkpoint；再次執行相同核准命令即可由 deterministic preconditions 驗證與恢復。

## 人工核准與資料保護

- PSD 與圖片位元內容只留在本機；模型只讀結構化 evidence。
- model 輸出的 plan 永遠強制 `approved=false`。
- plan 綁定 PSD、inspection 與 plan SHA-256；任一證據改變即撤銷核准。
- hidden layers 不分析、不移動、不改名、不匯出。
- `[SCROLL_H]`／`[SCROLL_V]` 的 `Viewport`、`Content` 由 Unity deterministic importer 產生，不回寫 PSD。
- review decisions 綁定 manifest fingerprint，不能跨版本沿用。

## 獨立外包工具（不屬於本 Pipeline）

`UI 發包製作人`／未來的 `QC_Agent` 是獨立工作流，不擔任 PS_To_Unity 的 Pipeline Validator。為避免角色混用，它使用獨立入口 `outsource_main.py`；現有 `Tools/啟動_UI發包製作人.bat` 仍可正常啟動。

```powershell
uv run python outsource_main.py outsource-preflight --request cases/examples/ui_outsourcing/request.json
uv run python outsource_main.py outsource --request cases/examples/ui_outsourcing/request.json
```

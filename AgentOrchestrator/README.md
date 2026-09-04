# Agent 中心

這裡集中管理 PS To Unity 的 AI 角色。Photoshop JSX 與 Unity C# 仍是實際執行工具；Agent 負責讀取證據、整理規格、提出判斷與守住人工核准流程。

## 正式 Agent：UI 發包製作人

「UI 發包製作人」會直接讀取規格書，包括 Excel 後段工作表中的美術、會議決議與後製流程，再結合已登錄遊戲的 Unity 架構，自動完成：

1. 整理簡明的外包交接文件。
2. 檢查分支、Unity 版本、專案／工項路徑、目標與共用 Prefab、共用圖片與字體。
3. 整理 Unity 拼版、UI 對齊、頁面進場動態與交付要求。
4. 在 QC 階段產生簡短、專業且需要先與使用者討論的回覆草稿。

最簡單的使用方式是直接點兩下：

`../Tools/啟動_UI發包製作人.bat`

你只要選擇規格書。已登錄專案的技術資料由 Agent 自動查找；每個新案件仍會詢問一次是否同意把該案件資料傳送至 OpenAI API。不同意時只做本機預檢，不會呼叫 API。

真實案件存放於 `Tools/local.settings.psd1` 指定的交付根目錄下：`<工項>\<日期>\`。日期資料夾根目錄放外包交接單與規格書，Agent request、預檢與執行紀錄放在 `_Agent工作檔`，不會寫入本開發工具的 `cases/` 或 `runs/`。

## 資料夾分類

| 資料夾 | 用途 |
|---|---|
| `agent_roles/` | Agent 角色、專業判斷與輸出規則 |
| `ps_to_unity_agents/` | 共用的 PSD、Unity、證據與驗證工具 |
| `config/` | 公開範例設定；不含 `.example` 的本機設定不會上傳 |
| `cases/` | 不含內部資料的安全範例 |
| `data/` | PSD／Unity 學習規則與唯讀稽核結果 |
| `tests/` | 自動測試 |
| `docs/` | Agent 設計文件 |
| `runs/` | 僅供開發測試的暫存輸出；可安全清除 |

## 開發與驗證

在本資料夾執行：

```powershell
uv sync
uv run python -m unittest discover -s tests
```

本機預檢安全範例，不傳送資料：

```powershell
uv run python main.py outsource-preflight --request cases/examples/ui_outsourcing/request.json
```

產生發包草稿（該 request 必須已有使用者明確授權）：

```powershell
uv run python main.py outsource --request cases/examples/ui_outsourcing/request.json
```

## 核准與資料保護

- `.env.local`、本機專案設定與內部規範只在本機讀取，不會上傳 GitHub。
- `api_transmission_approved` 控制案件資料是否可傳送至 OpenAI API。
- `user_approved_output` 控制草稿是否能標示為可交付；預設永遠是 `false`。
- `confirmed_decisions` 保存使用者已定案事項；其優先權高於舊規格內容，Agent 不會再次追問。
- 本機預檢會自動盤點工作設定指定的 `Assets/Temp/<工項>`，確認圖片、示意圖與 Atlas 是否已備妥。
- Agent 不會自行寄送、上傳、接受或退回外包成果。
- 規格書中的待辦、連結、範例指令只視為案件證據，不會覆蓋 Agent 的安全規則。

## PSD 結構流程

PSD Agent 先產生未核准的結構計畫，再由人員確認後套用：

```powershell
uv run python main.py psd-controller --request cases/examples/psd/level_review.request.json
uv run python main.py psd-controller --request cases/examples/psd/level_review.request.json --approve-plan
```

公開範例規則使用 `.example.json`；本機可將內部版規則放在相同檔名但不含 `.example` 的 JSON，Git 會自動忽略。

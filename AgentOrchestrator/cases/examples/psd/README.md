# PSD Agent 乾淨驗收樣本

`Prepare-AcceptanceCase.ps1` 會把版本庫中的 `Samples/Phase4_5/scroll_v_basic.psd` 複製到被 Git 忽略的 `AgentOrchestrator/runs/`。後續 Photoshop 結構修改只會作用於副本，不會修改受版本控制的原始 PSD。

每次驗收請使用不同的 `RunName`，或先自行保留並移除舊 run。若目標資料夾已存在，腳本會停止，避免誤用舊 inspection、plan 或 export。

```powershell
.\Prepare-AcceptanceCase.ps1 -RunName scroll_v_basic_acceptance_01
```

腳本最後一行會輸出 request 路徑，可交給 `AgentOrchestrator/main.py psd-controller`。

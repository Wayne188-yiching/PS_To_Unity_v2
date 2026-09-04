# 工具分類

## 正式 Agent 入口

- `啟動_UI發包製作人.bat`：直接點兩下，選擇規格書後建立發包草稿。
- `Start-UiOutsourcingProducer.ps1`：上述入口的實際啟動工具；會在本機設定的交付根目錄建立 `<工項>\<日期>\`、複製規格書、查 Unity 專案，並詢問是否同意傳送本案資料至 OpenAI API。Agent 內部紀錄集中於日期資料夾下的 `_Agent工作檔`。
- `local.settings.example.psd1`：公開設定範例。複製成 `local.settings.psd1` 後填入本機專案名稱與交付根目錄；實際設定不會上傳 GitHub。

## Photoshop 自動化

- `Invoke-PhotoshopPsdInspect.ps1`：讀取 PSD 圖層結構。
- `Invoke-PhotoshopStructurePlan.ps1`：驗證或套用已核准的 PSD 結構計畫。
- `Invoke-PhotoshopUiExport.ps1`：執行 UI Package 匯出。
- `Invoke-PhotoshopVisibleTextFontReplace.ps1`：替換可見文字圖層字體。

Photoshop JSX 正式來源仍在 `../PhotoshopExporter/`，此處只放自動化入口。

## Unity 自動化

- `Invoke-UnityUiImport.ps1`：執行 Unity UI 批次匯入。
- `Sync-UnityTestImporter.ps1`：同步目前 Importer 到隔離測試專案。

Unity Importer 正式來源仍在 `../Assets/Editor/PhotoshopUiImporter/`，此處只放批次工具。

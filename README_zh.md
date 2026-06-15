# Photoshop To Unity V2

將 Photoshop 排版好的遊戲 UI 轉成 Unity uGUI + TextMeshPro Prefab。文字圖層保留為可編輯的 TMP 節點，非文字圖層逐一輸出為 PNG Sprite。

**目前版本：v2.7.0**

---

## 工具清單

| 檔案 | 說明 |
|------|------|
| `PhotoshopExporter/PhotoshopUiPackageExporter.jsx` | 主要匯出工具，輸出 PNG + Layout JSON |
| `PhotoshopExporter/PhotoshopLayerAutoNamer.jsx` | 圖層批次英文命名 |
| `PhotoshopExporter/PhotoshopToolboxHub.jsx` | PS 工具箱啟動器（選用） |
| `PhotoshopExporter/InstallPhotoshopPlugin.jsx` | 將工具安裝到 PS Scripts 選單（選用） |
| `Assets/Editor/PhotoshopUiImporter/` | Unity Editor 匯入工具 |

---

## 主流程

1. 在 Photoshop 整理 UI PSD，圖層使用英文命名（需要時執行 `PhotoshopLayerAutoNamer.jsx`）。
2. 執行 `PhotoshopUiPackageExporter.jsx`，指定 PNG 輸出資料夾與 Layout JSON 路徑，點 Export。
3. 在 Unity 開啟 `Tools > Photoshop UI Importer > Importer_v2`。
4. 選擇 Package 資料夾，點「套用 Package」。
5. 填寫「專案資料夾名稱」，點「套用標準輸出路徑」。
6. 指定「預設 TMP Font Asset」（UI 含文字時必填）。
7. 點「Validate」確認，再點「Generate Prefab」完成。

---

## 安裝

### Photoshop 端

將 `PhotoshopExporter/` 資料夾內的 JSX 複製到 Photoshop 的 Scripts 資料夾後重啟 PS：

```
Windows：C:\Program Files\Adobe\Adobe Photoshop [版本]\Presets\Scripts\
Mac：    /Applications/Adobe Photoshop [版本]/Presets/Scripts/
```

或直接透過 `File > Scripts > Browse…` 每次手動開啟 JSX。

### Unity 端

將 `Assets/Editor/PhotoshopUiImporter/` 複製到 Unity 專案的 `Assets/Editor/` 底下，Unity 自動編譯。需要 **TextMeshPro** 套件（Package Manager 安裝）。

---

## 版本更新

- **PS 端**：執行工具後，對話框點 **Check for Updates**，自動從 GitHub 下載最新 JSX。
- **Unity 端**：Importer_v2 視窗標題點 **從 GitHub 更新工具**，自動下載並觸發重新編譯。

---

## 文件

→ [完整使用說明（GUIDE_zh.html）](GUIDE_zh.html)（圖層命名規則、文字材質球、常見問題等）

# Phase 5 驗收 checklist（Prefab 字體材質批次替換，v2.12.0）

> **狀態**：待驗收。本階段輸入是 **Unity Prefab**（依賴 GUID/meta，本倉庫非 Unity 專案）
> 且字型檔有授權限制 → **Prefab 與字型檔不進 repo**，驗收在使用者的 Unity 專案內執行。
> 決議依據 [`OPTIMIZATION_PLAN_zh.html#phase5`](../../OPTIMIZATION_PLAN_zh.html#phase5)（Q1–Q7）。

## 前置

- 把 `Assets/Editor/PhotoshopUiImporter/` 全部檔案更新到 Unity 專案（或用「從 GitHub 更新工具」）。
- 測試字型：沿用 v2.11.1 驗收建立的 MiSans / HarmonyOS Sans / Noto Sans CJK 六組 Font Asset。
- 測試 Prefab 建議直接用 Phase 4.5 驗收產出的 prefab（含 scroll Content 內文字）+ `系統字-test` prefab（15 個多字型 TMP）。

## 驗收 checklist

### A. 分析（唯讀）
- [ ] `Tools > Photoshop UI Importer > Font Replacer` 開啟;指定資料夾「分析」→ 表格列出 (Font Asset, 材質) 組合、節點數、Prefab 數,與實際相符
- [ ] 分析過程不修改任何檔案(git status 乾淨)
- [ ] 單選 Prefab 模式(ObjectField)優先於資料夾生效

### B. 替換主流程
- [ ] 選一組合設「目標 Font Asset」→ 目標材質自動預填(來源=預設材質時 → 目標字型預設材質)
- [ ] 「產生替換清單(dry-run)」→ 報告 `tmp_font_replace_report.txt` 列出每筆 prefab/節點/舊新字型材質;仍未寫入
- [ ] 「套用替換」→ 只有 `font` / `fontSharedMaterial` 變更;RectTransform、fontSize、對齊、顏色、字距在 git diff 中零變化
- [ ] 描邊材質組合:來源用 outline 材質的文字 → 套用後自動產生克隆材質(Generated 資料夾),描邊色/寬視覺一致
- [ ] 假厚度雙層文字(shadow/main)兩層都被替換,顏色不變

### C. 字型資產工廠
- [ ] 「字型資產工廠」給 .ttf + 參數模板(來源 Font Asset)→ 建立 Dynamic SDF;檢查 `faceInfo.pointSize`/`atlasPadding` 與模板一致
- [ ] 建立後 domain reload(重開 Unity)→ Font Asset 的 atlas texture / material 子資產**沒有斷連結**(Q7d 風險驗證)
- [ ] 有指定 TmpFontMap → 自動多一筆 keyword 登記;重按不重複追加(更新既有)
- [ ] Importer 視窗「掃描 Package 字型」:對 `系統字-test` 的 layout.json 顯示 fontToken 三態(已對應/缺 Font Asset/缺字型檔);缺資產者一鍵建立後狀態變「已對應」

### D. 防雷驗證(Q5)
- [ ] **Variant override 保留**:建一顆 Variant 並手動 override 某 TMP 字型 → 批次替換 base → Variant 的 override 節點不變,報告列出「override 未替換」
- [ ] **巢狀 Prefab**:A prefab 內嵌 B prefab → 只替換 A 時,B 的 TMP 不被寫成 A 的 override(檢查 A 的 overrides 面板);B 在掃描範圍內時由 B 自己被替換
- [ ] **scroll Content 內文字**:Phase 4.5 產出的 prefab 替換後,ScrollRect/Viewport/Content 結構與參數零變化

### E. 互不干擾回歸
- [ ] 替換後跑 reskin 換皮 → Sprite 正常換、TMP 字型不被動
- [ ] 換皮後再跑 Font Replacer 分析 → 結果一致
- [ ] Phase 3/4/4.5 樣本重匯 full import 全部正常(工具檔案變更不影響匯入路徑)

## 疑難排解

| 現象 | 可能原因 |
|---|---|
| 建 Font Asset 後重開 Unity 斷連結 | Q7d 已知風險 → 回報;退守 = 用 Font Asset Creator 手動建(Sampling/Padding 照工具提示),TmpFontMap 登記仍可自動 |
| 替換後描邊粗細跑掉 | 目標字型 sampling/padding 與來源差異大;建議用工廠以來源為模板重建目標字型(factor=1 零換算) |
| 某些節點沒被替換 | 屬於巢狀 instance(看報告「跳過」清單)或 Variant override(看「未替換」清單)— 預期行為 |

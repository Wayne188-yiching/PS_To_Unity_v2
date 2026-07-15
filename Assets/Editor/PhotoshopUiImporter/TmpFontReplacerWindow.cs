using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    // OPTIMIZATION_PLAN_zh.html#phase5-q3：獨立視窗（不塞進 PhotoshopUiImporterWindow）；
    // 分析/替換引擎在 TmpFontReplacer，字型資產工廠在 TmpFontAssetFactory，本類只畫 UI。
    public sealed class TmpFontReplacerWindow : EditorWindow
    {
        private string scanFolder = "Assets";
        private GameObject singlePrefab;
        private string outputFolder = TmpFontAssetFactory.DefaultOutputFolder;
        private string materialLibraryFolder = string.Empty;
        private TmpFontMap fontMap;

        private TmpFontReplacer.AnalysisResult analysis;
        private List<TmpFontReplacer.ReplacePlanEntry> lastPlan;
        private Vector2 scrollPosition;
        private string statusMessage;
        private MessageType statusType = MessageType.Info;

        // 一鍵建立 Font Asset 用（Q4e 入口一）
        private Font newFontFile;
        private TMP_FontAsset newFontTemplate;

        [MenuItem("Tools/Photoshop UI Importer/Font Replacer")]
        public static void Open()
        {
            var window = GetWindow<TmpFontReplacerWindow>("Font Replacer");
            window.minSize = new Vector2(680, 520);
            window.Show();
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            try
            {
                DrawHeader();
                DrawScanSection();
                DrawAnalysisTable();
                DrawFontFactorySection();
                DrawActionSection();

                if (!string.IsNullOrWhiteSpace(statusMessage))
                {
                    EditorGUILayout.Space(8);
                    EditorGUILayout.HelpBox(statusMessage, statusType);
                }
                EditorGUILayout.Space(12);
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (System.Exception exception)
            {
                statusMessage = $"Font Replacer UI 發生錯誤：{exception.Message}";
                statusType = MessageType.Error;
                Debug.LogException(exception);
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("TMP 字體批次替換", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("分析 Prefab 的字型/材質使用 → 一鍵替換字體。只寫 font 與 fontSharedMaterial 兩個欄位，排版/顏色/Sprite 一律不動。", EditorStyles.miniLabel);
            EditorGUILayout.Space(6);
        }

        private void DrawScanSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("1. 掃描範圍", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    scanFolder = EditorGUILayout.TextField("Prefab 資料夾（遞迴）", scanFolder);
                    if (GUILayout.Button("選擇…", GUILayout.Width(64)))
                    {
                        var selected = EditorUtility.OpenFolderPanel("選擇 Prefab 資料夾", "Assets", "");
                        if (!string.IsNullOrEmpty(selected))
                        {
                            scanFolder = PathUtility.ToProjectRelativeAssetPath(selected);
                        }
                    }
                }

                singlePrefab = (GameObject)EditorGUILayout.ObjectField("或單選 Prefab（優先）", singlePrefab, typeof(GameObject), false);
                fontMap = (TmpFontMap)EditorGUILayout.ObjectField(
                    new GUIContent("TmpFontMap（選填）", "一鍵建立 Font Asset 後自動登記到這張表。"),
                    fontMap, typeof(TmpFontMap), false);
                DrawFolderField("材質庫資料夾（選填）", ref materialLibraryFolder, "自動配對目標材質時，優先在此資料夾找 atlas 相符且描邊色相近的現成材質。");
                DrawFolderField("產出資料夾", ref outputFolder, "克隆材質與新建 Font Asset 的存放位置。");

                if (GUILayout.Button("分析（唯讀，不寫入）", GUILayout.Height(28)))
                {
                    RunAnalysis();
                }
            }
        }

        private void DrawFolderField(string label, ref string value, string tooltip)
        {
            value = EditorGUILayout.TextField(new GUIContent(label, tooltip), value);
        }

        private void RunAnalysis()
        {
            List<string> paths;
            if (singlePrefab != null)
            {
                paths = new List<string> { AssetDatabase.GetAssetPath(singlePrefab) };
            }
            else
            {
                paths = TmpFontReplacer.CollectPrefabPaths(scanFolder);
            }

            if (paths.Count == 0)
            {
                SetStatus("範圍內找不到任何 Prefab。", MessageType.Warning);
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("Font Replacer", $"分析 {paths.Count} 顆 Prefab...", 0.3f);
                analysis = TmpFontReplacer.Analyze(paths);
                lastPlan = null;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            var overrideHint = analysis.overriddenNodes.Count > 0
                ? $"　⚠ {analysis.overriddenNodes.Count} 個節點有字型 override（保留不動，見報告）"
                : string.Empty;
            SetStatus($"分析完成：{analysis.prefabCount} 顆 Prefab、{analysis.tmpNodeCount} 個 TMP、{analysis.groups.Count} 個字型/材質組合。{overrideHint}", MessageType.Info);
        }

        private void DrawAnalysisTable()
        {
            if (analysis == null || analysis.groups.Count == 0)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("2. 字型/材質組合（目標欄可逐列覆寫；留空 = 不替換）", EditorStyles.boldLabel);

                foreach (var group in analysis.groups)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.textArea))
                    {
                        EditorGUILayout.LabelField(
                            $"{TmpFontReplacer.DescribeGroup(group)} — {group.usages.Count} 節點 / {group.prefabPaths.Count} 顆 Prefab",
                            EditorStyles.boldLabel);

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var newTargetFont = (TMP_FontAsset)EditorGUILayout.ObjectField("目標 Font Asset", group.targetFont, typeof(TMP_FontAsset), false);
                            if (newTargetFont != group.targetFont)
                            {
                                group.targetFont = newTargetFont;
                                // Q1：換目標字型時重跑自動配對預填（使用者仍可再改）。
                                group.targetMaterial = TmpFontReplacer.SuggestTargetMaterial(group, newTargetFont, materialLibraryFolder);
                                lastPlan = null;
                            }
                        }

                        if (group.targetFont != null)
                        {
                            group.targetMaterial = (Material)EditorGUILayout.ObjectField(
                                new GUIContent("目標材質", "留空 = 套用時自動克隆-換底（複製來源材質視覺參數到目標字型 atlas）。"),
                                group.targetMaterial, typeof(Material), false);
                        }
                    }
                }
            }
        }

        private void DrawFontFactorySection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("3. 字型資產工廠（目標字型還沒有 Font Asset 時用）", EditorStyles.boldLabel);
                newFontFile = (Font)EditorGUILayout.ObjectField(
                    new GUIContent("字型檔（.ttf/.otf）", "只接受已放進專案 Assets 的字型檔；工具不會去系統字型資料夾複製（授權）。"),
                    newFontFile, typeof(Font), false);
                newFontTemplate = (TMP_FontAsset)EditorGUILayout.ObjectField(
                    new GUIContent("參數模板（選填）", "sampling/padding/atlas 尺寸抄這顆（建議填被替換的來源 Font Asset → 材質參數可 1:1 複製零換算）。留空用 90/9/1024。"),
                    newFontTemplate, typeof(TMP_FontAsset), false);

                using (new EditorGUI.DisabledScope(newFontFile == null))
                {
                    if (GUILayout.Button("建立 Dynamic SDF Font Asset", GUILayout.Height(24)))
                    {
                        var created = TmpFontAssetFactory.CreateDynamicFontAsset(newFontFile, newFontTemplate, outputFolder, out var error);
                        if (created != null && string.IsNullOrEmpty(error))
                        {
                            var registered = TmpFontAssetFactory.RegisterInFontMap(fontMap, TmpFontAssetFactory.NormalizeSlug(newFontFile.name), created);
                            SetStatus($"已建立 {created.name}（Dynamic，出包前建議轉 Static）。" +
                                      (registered ? "已登記 TmpFontMap。" : "未指定 TmpFontMap，跳過登記。"), MessageType.Info);
                            EditorGUIUtility.PingObject(created);
                        }
                        else
                        {
                            SetStatus(error ?? "建立失敗。", MessageType.Error);
                        }
                    }
                }
            }
        }

        private void DrawActionSection()
        {
            if (analysis == null || analysis.groups.Count == 0)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("4. 執行", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("建議在版控乾淨狀態下執行套用；工具不做自動備份，git 才是安全網。", MessageType.None);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("產生替換清單（dry-run 報告）", GUILayout.Height(28)))
                    {
                        var planErrors = new List<string>();
                        lastPlan = TmpFontReplacer.BuildPlan(analysis, outputFolder, planErrors);
                        var reportPath = TmpFontReplacer.WriteReport(outputFolder, analysis, lastPlan, null);
                        var errorHint = planErrors.Count > 0 ? $"　⚠ {planErrors.Count} 組配對失敗，見 Console" : string.Empty;
                        foreach (var error in planErrors)
                        {
                            Debug.LogWarning($"[FontReplacer] {error}");
                        }
                        SetStatus($"dry-run 完成：{lastPlan.Count} 筆待替換。報告：{reportPath}{errorHint}", MessageType.Info);
                    }

                    using (new EditorGUI.DisabledScope(lastPlan == null || lastPlan.Count == 0))
                    {
                        if (GUILayout.Button($"套用替換{(lastPlan != null ? $"（{lastPlan.Count} 筆）" : "")}", GUILayout.Height(28)))
                        {
                            if (EditorUtility.DisplayDialog("套用字體替換",
                                    $"將修改 {lastPlan.Count} 筆 TMP 節點的 font / fontSharedMaterial。\n其他欄位一律不動。確定套用？", "套用", "取消"))
                            {
                                ApplyPlan();
                            }
                        }
                    }
                }
            }
        }

        private void ApplyPlan()
        {
            TmpFontReplacer.ApplyResult applyResult;
            try
            {
                EditorUtility.DisplayProgressBar("Font Replacer", "套用替換...", 0.5f);
                applyResult = TmpFontReplacer.Apply(lastPlan);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            var reportPath = TmpFontReplacer.WriteReport(outputFolder, analysis, lastPlan, applyResult);
            foreach (var error in applyResult.errors)
            {
                Debug.LogError($"[FontReplacer] {error}");
            }

            SetStatus(
                $"套用完成：{applyResult.prefabsChanged} 顆 Prefab / {applyResult.nodesChanged} 個節點已替換" +
                (applyResult.errors.Count > 0 ? $"，{applyResult.errors.Count} 筆錯誤（見 Console）" : "") +
                $"。報告：{reportPath}",
                applyResult.errors.Count > 0 ? MessageType.Warning : MessageType.Info);

            lastPlan = null;
            RunAnalysis(); // 重新分析讓表格反映替換後狀態
        }

        private void SetStatus(string message, MessageType type)
        {
            statusMessage = message;
            statusType = type;
            Repaint();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    // OPTIMIZATION_PLAN_zh.html#phase5：Prefab 字體材質批次替換——分析與替換引擎（UI 在
    // TmpFontReplacerWindow）。
    // 鐵律（#phase5-q1 / #phase5-q6）：每個 TMP 節點只寫 font（m_fontAsset）與
    // fontSharedMaterial（m_sharedMaterial）兩個序列化欄位；RectTransform / fontSize /
    // 對齊 / 字距 / 顏色 / 漸層 / Sprite / 排版元件一律不讀不寫。與 PsUiSkinApplier 對稱：
    // 換皮不碰 TMP，本引擎不碰 Sprite。
    public static class TmpFontReplacer
    {
        private const string FontAssetProperty = "m_fontAsset";
        private const string SharedMaterialProperty = "m_sharedMaterial";

        // ── 資料模型 ─────────────────────────────────────────────────────

        // 一列 = 一個 (Font Asset, 材質球) 組合（分析表的一行）。
        public sealed class FontUsageGroup
        {
            public TMP_FontAsset sourceFont;
            public Material sourceMaterial;
            public readonly List<UsageRecord> usages = new List<UsageRecord>();
            public readonly HashSet<string> prefabPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 目標（Q1 的 C：自動配對預填、可逐列覆寫）；targetFont == null = 此列不替換。
            public TMP_FontAsset targetFont;
            public Material targetMaterial;
        }

        public sealed class UsageRecord
        {
            public string prefabPath;
            public string nodePath;
        }

        public sealed class AnalysisResult
        {
            public readonly List<FontUsageGroup> groups = new List<FontUsageGroup>();
            public int prefabCount;
            public int tmpNodeCount;
            // Q5c：屬於巢狀 Prefab instance 的 TMP 節點——本 Prefab 內跳過，由來源 asset 處理。
            public readonly List<string> skippedNestedNodes = new List<string>();
            // Q5b：字型/材質被 override 的節點——保留不動，報告供人工決定。
            public readonly List<string> overriddenNodes = new List<string>();
            public readonly List<string> errors = new List<string>();
        }

        public sealed class ReplacePlanEntry
        {
            public string prefabPath;
            public string nodePath;
            public TMP_FontAsset oldFont;
            public Material oldMaterial;
            public TMP_FontAsset newFont;
            public Material newMaterial;
        }

        public sealed class ApplyResult
        {
            public int prefabsChanged;
            public int nodesChanged;
            public readonly List<string> errors = new List<string>();
        }

        // ── 分析 ─────────────────────────────────────────────────────────

        public static List<string> CollectPrefabPaths(string folder)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(folder) || !AssetDatabase.IsValidFolder(folder))
            {
                return result;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                result.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        // 掃描 = 唯讀（Q5d：「分析」永遠只讀，實際寫入走 Apply）。
        // 用 LoadPrefabContents 而非 LoadAssetAtPath：v2.11.1 驗收證實 Prefab Asset 物件上的
        // 部分 PrefabUtility API 不可靠；stage 副本上 IsPartOfPrefabInstance 行為正確。
        public static AnalysisResult Analyze(IReadOnlyList<string> prefabPaths)
        {
            var result = new AnalysisResult();
            var groupLookup = new Dictionary<(TMP_FontAsset, Material), FontUsageGroup>();

            foreach (var prefabPath in prefabPaths)
            {
                GameObject contentRoot = null;
                try
                {
                    contentRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                    result.prefabCount++;

                    foreach (var text in contentRoot.GetComponentsInChildren<TMP_Text>(true))
                    {
                        result.tmpNodeCount++;
                        var nodePath = BuildNodePath(contentRoot.transform, text.transform);

                        // Q5c：巢狀 instance（含 Variant 繼承自 base 的部分）→ 跳過，由來源 asset 處理。
                        if (PrefabUtility.IsPartOfPrefabInstance(text.gameObject))
                        {
                            result.skippedNestedNodes.Add($"{prefabPath} :: {nodePath}");
                            // Q5b：instance 上若有字型/材質 override，如實報告（保留不動）。
                            if (HasFontOverride(text))
                            {
                                result.overriddenNodes.Add($"{prefabPath} :: {nodePath}");
                            }
                            continue;
                        }

                        var font = text.font;
                        var material = text.fontSharedMaterial;
                        var key = (font, material);
                        if (!groupLookup.TryGetValue(key, out var group))
                        {
                            group = new FontUsageGroup { sourceFont = font, sourceMaterial = material };
                            groupLookup[key] = group;
                            result.groups.Add(group);
                        }

                        group.usages.Add(new UsageRecord { prefabPath = prefabPath, nodePath = nodePath });
                        group.prefabPaths.Add(prefabPath);
                    }
                }
                catch (Exception exception)
                {
                    result.errors.Add($"{prefabPath}：{exception.Message}");
                }
                finally
                {
                    if (contentRoot != null)
                    {
                        PrefabUtility.UnloadPrefabContents(contentRoot);
                    }
                }
            }

            // 穩定排序：使用次數多的在前（分析表可讀性）。
            result.groups.Sort((a, b) => b.usages.Count.CompareTo(a.usages.Count));
            return result;
        }

        private static bool HasFontOverride(TMP_Text text)
        {
            try
            {
                var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(text.gameObject);
                if (instanceRoot == null)
                {
                    return false;
                }

                var modifications = PrefabUtility.GetPropertyModifications(instanceRoot);
                if (modifications == null)
                {
                    return false;
                }

                foreach (var modification in modifications)
                {
                    if (modification.target is TMP_Text &&
                        (modification.propertyPath == FontAssetProperty || modification.propertyPath == SharedMaterialProperty))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            }

            return false;
        }

        // ── 自動配對（Q1：預填值，UI 可逐列覆寫）────────────────────────

        // 優先序：來源 == 來源字型預設材質 → 目標字型預設材質；
        // 否則 → 材質庫資料夾找視覺相近且 atlas 相符的現成材質（FindLibraryMaterial 同款邏輯的
        // 簡化版：atlas 必須等於目標字型）；再否則 → 留空（Apply 時走克隆-換底）。
        public static Material SuggestTargetMaterial(
            FontUsageGroup group,
            TMP_FontAsset targetFont,
            string materialLibraryFolder)
        {
            if (group == null || targetFont == null || targetFont.material == null)
            {
                return null;
            }

            if (group.sourceFont != null && group.sourceMaterial == group.sourceFont.material)
            {
                return targetFont.material;
            }

            if (!string.IsNullOrWhiteSpace(materialLibraryFolder) && AssetDatabase.IsValidFolder(materialLibraryFolder) &&
                group.sourceMaterial != null && group.sourceMaterial.HasProperty(ShaderUtilities.ID_OutlineColor))
            {
                var sourceColor = group.sourceMaterial.GetColor(ShaderUtilities.ID_OutlineColor);
                const float colorTolerance = 0.08f;

                foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { materialLibraryFolder }))
                {
                    var candidate = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (candidate == null || candidate.mainTexture != targetFont.atlasTexture ||
                        !candidate.HasProperty(ShaderUtilities.ID_OutlineColor))
                    {
                        continue;
                    }

                    var candidateColor = candidate.GetColor(ShaderUtilities.ID_OutlineColor);
                    if (Mathf.Abs(candidateColor.r - sourceColor.r) < colorTolerance &&
                        Mathf.Abs(candidateColor.g - sourceColor.g) < colorTolerance &&
                        Mathf.Abs(candidateColor.b - sourceColor.b) < colorTolerance)
                    {
                        return candidate;
                    }
                }
            }

            return null; // Apply 時以克隆-換底補齊
        }

        // ── 建立替換計畫（dry-run 的內容 = 這份清單）─────────────────────

        public static List<ReplacePlanEntry> BuildPlan(AnalysisResult analysis, string generatedMaterialFolder, List<string> planErrors)
        {
            var plan = new List<ReplacePlanEntry>();

            foreach (var group in analysis.groups)
            {
                if (group.targetFont == null)
                {
                    continue; // 此列不替換
                }
                if (group.targetFont == group.sourceFont && group.targetMaterial == null)
                {
                    continue; // 同字型且無指定材質 = 無事可做
                }

                var targetMaterial = group.targetMaterial;
                if (targetMaterial == null)
                {
                    // Q1 克隆-換底：自動配對沒填時，Apply 前為此組生成/重用克隆材質。
                    targetMaterial = TmpFontAssetFactory.CloneMaterialForFont(
                        group.sourceMaterial, group.sourceFont, group.targetFont,
                        generatedMaterialFolder, out var cloneError);
                    if (targetMaterial == null)
                    {
                        planErrors?.Add($"{DescribeGroup(group)}：{cloneError}");
                        continue;
                    }
                    group.targetMaterial = targetMaterial; // 回填 UI 顯示
                }

                foreach (var usage in group.usages)
                {
                    plan.Add(new ReplacePlanEntry
                    {
                        prefabPath = usage.prefabPath,
                        nodePath = usage.nodePath,
                        oldFont = group.sourceFont,
                        oldMaterial = group.sourceMaterial,
                        newFont = group.targetFont,
                        newMaterial = targetMaterial
                    });
                }
            }

            return plan;
        }

        // ── 套用（只寫 m_fontAsset / m_sharedMaterial 兩個欄位）──────────

        public static ApplyResult Apply(List<ReplacePlanEntry> plan)
        {
            var result = new ApplyResult();

            // 依 prefab 分組，一顆載入一次。
            var byPrefab = new Dictionary<string, List<ReplacePlanEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in plan)
            {
                if (!byPrefab.TryGetValue(entry.prefabPath, out var list))
                {
                    list = new List<ReplacePlanEntry>();
                    byPrefab[entry.prefabPath] = list;
                }
                list.Add(entry);
            }

            foreach (var pair in byPrefab)
            {
                GameObject contentRoot = null;
                try
                {
                    contentRoot = PrefabUtility.LoadPrefabContents(pair.Key);
                    var changed = 0;

                    foreach (var text in contentRoot.GetComponentsInChildren<TMP_Text>(true))
                    {
                        if (PrefabUtility.IsPartOfPrefabInstance(text.gameObject))
                        {
                            continue; // Q5c：巢狀節點永不在外層寫 override
                        }

                        var nodePath = BuildNodePath(contentRoot.transform, text.transform);
                        foreach (var entry in pair.Value)
                        {
                            if (entry.nodePath != nodePath || text.font != entry.oldFont || text.fontSharedMaterial != entry.oldMaterial)
                            {
                                continue;
                            }

                            // 用 SerializedObject 直寫兩個欄位，避開 TMP property setter 的連鎖副作用
                            //（font setter 會自動重設材質為新字型預設材質，蓋掉我們指定的克隆材質）。
                            var serialized = new SerializedObject(text);
                            serialized.FindProperty(FontAssetProperty).objectReferenceValue = entry.newFont;
                            serialized.FindProperty(SharedMaterialProperty).objectReferenceValue = entry.newMaterial;
                            serialized.ApplyModifiedPropertiesWithoutUndo();
                            changed++;
                            break;
                        }
                    }

                    if (changed > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(contentRoot, pair.Key, out var saved);
                        if (!saved)
                        {
                            result.errors.Add($"{pair.Key}：Prefab 儲存失敗。");
                            continue;
                        }
                        result.prefabsChanged++;
                        result.nodesChanged += changed;
                    }
                }
                catch (Exception exception)
                {
                    result.errors.Add($"{pair.Key}：{exception.Message}");
                }
                finally
                {
                    if (contentRoot != null)
                    {
                        PrefabUtility.UnloadPrefabContents(contentRoot);
                    }
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return result;
        }

        // ── 報告（Q5d：文字檔，export_report 慣例）───────────────────────

        public static string WriteReport(
            string folder,
            AnalysisResult analysis,
            List<ReplacePlanEntry> plan,
            ApplyResult applyResult)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Photoshop UI Importer — TMP 字體批次替換報告");
            builder.AppendLine($"時間：{DateTime.Now:yyyy-MM-dd HH:mm:ss}　模式：{(applyResult == null ? "dry-run（未寫入）" : "已套用")}");
            builder.AppendLine(new string('─', 60));
            builder.AppendLine($"掃描 Prefab：{analysis.prefabCount}　TMP 節點：{analysis.tmpNodeCount}　組合數：{analysis.groups.Count}");
            builder.AppendLine();

            builder.AppendLine("【字型/材質組合】");
            foreach (var group in analysis.groups)
            {
                builder.AppendLine($"  {DescribeGroup(group)} — {group.usages.Count} 節點 / {group.prefabPaths.Count} 顆 Prefab" +
                                   (group.targetFont != null ? $" → {group.targetFont.name} / {(group.targetMaterial != null ? group.targetMaterial.name : "(自動克隆)")}" : "（不替換）"));
            }

            if (plan != null && plan.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine($"【替換清單】共 {plan.Count} 筆");
                foreach (var entry in plan)
                {
                    builder.AppendLine($"  {entry.prefabPath} :: {entry.nodePath}");
                    builder.AppendLine($"    {NameOf(entry.oldFont)} / {NameOf(entry.oldMaterial)} → {NameOf(entry.newFont)} / {NameOf(entry.newMaterial)}");
                }
            }

            if (analysis.overriddenNodes.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine($"【Variant/巢狀 override 未替換，請人工確認】共 {analysis.overriddenNodes.Count} 筆");
                foreach (var node in analysis.overriddenNodes)
                {
                    builder.AppendLine($"  {node}");
                }
            }

            if (analysis.skippedNestedNodes.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine($"【巢狀 Prefab 節點（由來源 asset 處理，本次跳過）】共 {analysis.skippedNestedNodes.Count} 筆");
                foreach (var node in analysis.skippedNestedNodes)
                {
                    builder.AppendLine($"  {node}");
                }
            }

            if (applyResult != null)
            {
                builder.AppendLine();
                builder.AppendLine($"【套用結果】Prefab 變更：{applyResult.prefabsChanged}　節點變更：{applyResult.nodesChanged}");
                foreach (var error in applyResult.errors)
                {
                    builder.AppendLine($"  錯誤：{error}");
                }
            }

            foreach (var error in analysis.errors)
            {
                builder.AppendLine($"  分析錯誤：{error}");
            }

            var reportFolder = string.IsNullOrWhiteSpace(folder) ? "Assets" : folder;
            var reportPath = $"{reportFolder.TrimEnd('/')}/tmp_font_replace_report.txt";
            File.WriteAllText(PathUtility.ToAbsolutePath(reportPath), builder.ToString(), Encoding.UTF8);
            AssetDatabase.Refresh();
            return reportPath;
        }

        public static string DescribeGroup(FontUsageGroup group)
        {
            return $"{NameOf(group.sourceFont)} / {NameOf(group.sourceMaterial)}";
        }

        private static string NameOf(UnityEngine.Object asset)
        {
            return asset != null ? asset.name : "(無)";
        }

        private static string BuildNodePath(Transform root, Transform node)
        {
            var parts = new List<string>();
            var current = node;
            while (current != null && current != root)
            {
                parts.Insert(0, current.name);
                current = current.parent;
            }
            return string.Join("/", parts);
        }
    }
}

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PhotoshopToUnity.EditorImporter
{
    // ── reskin flow 不可碰清單（OPTIMIZATION_PLAN_zh.html#phase4-decisions Q3）────────────────────────────
    // 以下由 full import（UGuiTmpPrefabBackend）視為 source of truth，reskin 完全不觸碰：
    //   * CanvasGroup（含 root 自動掛的那個）
    //   * GridLayoutGroup（layoutType == "grid" 的節點）
    //   * HorizontalLayoutGroup / VerticalLayoutGroup 的參數（含 padding / spacing / childControl 等）
    //   * ContentSizeFitter
    // reskin 只做：同名圖檔覆蓋、Prefab 內 Sprite 參照替換。設計師如果手動改上述 component，
    // reskin 不會吃掉他的手調 —— 但下次 full import 一定會覆蓋（因為 PS 是 source of truth）。
    // ─────────────────────────────────────────────────────────────────────────
    public static class PsUiSkinApplier
    {
        public struct Result
        {
            public int prefabsChanged;
            public int spritesReplaced;
            public int filesOverwritten;
            public List<string> missingFiles;
            public string errorMessage;
        }

        public static Result Apply(PsUiSkinTheme theme)
        {
            var result = new Result { missingFiles = new List<string>() };

            if (theme == null)
            {
                result.errorMessage = "SkinTheme 為空。";
                return result;
            }

            if (theme.entries == null || theme.entries.Count == 0)
            {
                result.errorMessage = "沒有任何換皮項目。";
                return result;
            }

            // ── 把 entries 分成兩組 ──────────────────────────────────────────
            // 同名模式：newSprite 為空 或 名稱相同 → 用檔案覆蓋
            // 換參照模式：newSprite 不同 → 改 Prefab 裡的 sprite 參照
            var fileCopyEntries   = new List<SkinThemeEntry>();
            var refSwapMap        = new Dictionary<string, Sprite>(); // key → newSprite

            foreach (var entry in theme.entries)
            {
                if (entry.oldSprite == null) continue;

                bool sameNameMode = entry.newSprite == null ||
                    string.Equals(entry.oldSprite.name, entry.newSprite.name,
                                  System.StringComparison.OrdinalIgnoreCase);

                if (sameNameMode)
                {
                    fileCopyEntries.Add(entry);
                }
                else
                {
                    var key = SpriteKey(entry.oldSprite);
                    if (key != null)
                        refSwapMap[key] = entry.newSprite;
                }
            }

            // ── 模式 A：檔案覆蓋 ────────────────────────────────────────────
            if (fileCopyEntries.Count > 0)
            {
                var sourceDir = theme.sourceArtFolder;
                if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
                {
                    if (fileCopyEntries.Count > 0)
                        result.missingFiles.Add(
                            $"（美術來源資料夾未設定或不存在，{fileCopyEntries.Count} 筆同名項目無法覆蓋）");
                }
                else
                {
                    bool anyFileCopied = false;
                    foreach (var entry in fileCopyEntries)
                    {
                        var oldAssetPath = AssetDatabase.GetAssetPath(entry.oldSprite);
                        // 只允許個別 PNG（非 Atlas 子資產）
                        if (!string.Equals(
                                Path.GetFileNameWithoutExtension(oldAssetPath),
                                entry.oldSprite.name,
                                System.StringComparison.OrdinalIgnoreCase))
                        {
                            result.missingFiles.Add(
                                $"{entry.oldSprite.name}（在 Atlas 內，無法直接覆蓋）");
                            continue;
                        }

                        var ext         = Path.GetExtension(oldAssetPath); // .png / .jpg
                        var sourceFile  = Path.Combine(sourceDir, entry.oldSprite.name + ext);
                        if (!File.Exists(sourceFile))
                            sourceFile = Path.Combine(sourceDir, entry.oldSprite.name + ".png");

                        if (!File.Exists(sourceFile))
                        {
                            result.missingFiles.Add(entry.oldSprite.name + ext);
                            continue;
                        }

                        var destAbsPath = Path.GetFullPath(
                            Path.Combine(Application.dataPath, "..", oldAssetPath));
                        File.Copy(sourceFile, destAbsPath, overwrite: true);
                        result.filesOverwritten++;
                        anyFileCopied = true;
                    }

                    if (anyFileCopied)
                        AssetDatabase.Refresh();
                }
            }

            // ── 模式 B：Sprite 參照替換 ─────────────────────────────────────
            if (refSwapMap.Count > 0)
            {
                var targetFolder = theme.targetPrefabFolderAsset != null
                    ? AssetDatabase.GetAssetPath(theme.targetPrefabFolderAsset)
                    : null;

                if (string.IsNullOrWhiteSpace(targetFolder) || !AssetDatabase.IsValidFolder(targetFolder))
                {
                    result.missingFiles.Add("（目標 Prefab 資料夾未設定，參照替換已跳過）");
                }
                else
                {
                    var excluded = new HashSet<string>();
                    if (theme.excludedPrefabs != null)
                        foreach (var go in theme.excludedPrefabs)
                        {
                            if (go == null) continue;
                            excluded.Add(AssetDatabase.GetAssetPath(go));
                        }

                    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { targetFolder }))
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        if (excluded.Contains(path)) continue;

                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (prefab == null) continue;

                        bool changed = false;
                        foreach (var img in prefab.GetComponentsInChildren<Image>(true))
                        {
                            if (img.sprite == null) continue;
                            var key = SpriteKey(img.sprite);
                            if (key == null || !refSwapMap.TryGetValue(key, out var newSprite)) continue;

                            var so = new SerializedObject(img);
                            so.FindProperty("m_Sprite").objectReferenceValue = newSprite;
                            so.ApplyModifiedPropertiesWithoutUndo();
                            result.spritesReplaced++;
                            changed = true;
                        }

                        if (changed)
                        {
                            PrefabUtility.SavePrefabAsset(prefab);
                            result.prefabsChanged++;
                        }
                    }

                    AssetDatabase.SaveAssets();
                }
            }

            return result;
        }

        /// <summary>
        /// 掃描 theme.targetPrefabFolderAsset 下所有 Prefab 的 Image 元件，
        /// 把尚未在 entries 裡的 Sprite 自動加入（oldSprite 填入，newSprite 留空）。
        /// </summary>
        public static int ScanAndFillOldSprites(PsUiSkinTheme theme)
        {
            if (theme == null) return 0;

            var targetFolder = theme.targetPrefabFolderAsset != null
                ? AssetDatabase.GetAssetPath(theme.targetPrefabFolderAsset)
                : null;

            if (string.IsNullOrWhiteSpace(targetFolder) || !AssetDatabase.IsValidFolder(targetFolder))
                return 0;

            var existingKeys = new HashSet<string>();
            foreach (var entry in theme.entries)
            {
                var k = SpriteKey(entry.oldSprite);
                if (k != null) existingKeys.Add(k);
            }

            var excluded = new HashSet<string>();
            if (theme.excludedPrefabs != null)
                foreach (var go in theme.excludedPrefabs)
                {
                    if (go == null) continue;
                    excluded.Add(AssetDatabase.GetAssetPath(go));
                }

            var added    = 0;
            var seenKeys = new HashSet<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { targetFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (excluded.Contains(path)) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                foreach (var img in prefab.GetComponentsInChildren<Image>(true))
                {
                    if (img.sprite == null) continue;
                    var key = SpriteKey(img.sprite);
                    if (key == null || existingKeys.Contains(key) || !seenKeys.Add(key)) continue;

                    theme.entries.Add(new SkinThemeEntry { oldSprite = img.sprite });
                    added++;
                }
            }

            if (added > 0)
                EditorUtility.SetDirty(theme);

            return added;
        }

        private static string SpriteKey(Sprite sprite)
        {
            if (sprite == null) return null;
            var path = AssetDatabase.GetAssetPath(sprite);
            return string.IsNullOrEmpty(path) ? null : path + "#" + sprite.name;
        }
    }
}

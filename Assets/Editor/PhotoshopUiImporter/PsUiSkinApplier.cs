using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PhotoshopToUnity.EditorImporter
{
    public static class PsUiSkinApplier
    {
        public struct Result
        {
            public int prefabsChanged;
            public int spritesReplaced;
            public string errorMessage;
        }

        public static Result Apply(PsUiSkinTheme theme)
        {
            var result = new Result();

            if (theme == null)
            {
                result.errorMessage = "SkinTheme 為空。";
                return result;
            }

            var targetFolder = theme.targetPrefabFolderAsset != null
                ? AssetDatabase.GetAssetPath(theme.targetPrefabFolderAsset)
                : null;

            if (string.IsNullOrWhiteSpace(targetFolder) || !AssetDatabase.IsValidFolder(targetFolder))
            {
                result.errorMessage = "請先拖入目標 Prefab 資料夾。";
                return result;
            }

            // 建立舊 Sprite key → 新 Sprite 的對應表
            // key 格式：assetPath#spriteName，跨 session 穩定
            var map = new Dictionary<string, Sprite>();
            foreach (var entry in theme.entries)
            {
                if (entry.oldSprite == null || entry.newSprite == null) continue;
                var key = SpriteKey(entry.oldSprite);
                if (key != null)
                    map[key] = entry.newSprite;
            }

            if (map.Count == 0)
            {
                result.errorMessage = "沒有完整的換皮項目（請確認每筆項目的舊圖和新圖都已填入）。";
                return result;
            }

            // 建立排除清單
            var excluded = new HashSet<string>();
            if (theme.excludedPrefabs != null)
            {
                foreach (var go in theme.excludedPrefabs)
                {
                    if (go == null) continue;
                    excluded.Add(AssetDatabase.GetAssetPath(go));
                }
            }

            // 掃描目標資料夾下所有 Prefab
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { targetFolder });
            foreach (var guid in guids)
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
                    if (key == null || !map.TryGetValue(key, out var newSprite)) continue;

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

            // 已在 entries 裡的 key 不重複加
            var existingKeys = new HashSet<string>();
            foreach (var entry in theme.entries)
            {
                var k = SpriteKey(entry.oldSprite);
                if (k != null) existingKeys.Add(k);
            }

            // 排除清單（與 Apply 保持一致）
            var excluded = new HashSet<string>();
            if (theme.excludedPrefabs != null)
                foreach (var go in theme.excludedPrefabs)
                {
                    if (go == null) continue;
                    excluded.Add(AssetDatabase.GetAssetPath(go));
                }

            var added = 0;
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

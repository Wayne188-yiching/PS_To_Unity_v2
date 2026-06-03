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

            if (string.IsNullOrWhiteSpace(theme.targetPrefabFolder) ||
                !AssetDatabase.IsValidFolder(theme.targetPrefabFolder))
            {
                result.errorMessage = "目標資料夾無效，請在 SkinTheme 裡填入有效的 Assets 路徑。";
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
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { theme.targetPrefabFolder });
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

        private static string SpriteKey(Sprite sprite)
        {
            if (sprite == null) return null;
            var path = AssetDatabase.GetAssetPath(sprite);
            return string.IsNullOrEmpty(path) ? null : path + "#" + sprite.name;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    [CreateAssetMenu(menuName = "Photoshop UI Importer/Skin Theme", fileName = "SkinTheme")]
    public sealed class PsUiSkinTheme : ScriptableObject
    {
        public UnityEngine.Object targetPrefabFolderAsset;
        /// <summary>新美術 PNG 來源資料夾（可在 Assets 外，如 PS 匯出路徑）</summary>
        public string sourceArtFolder = string.Empty;
        public List<SkinThemeEntry> entries = new List<SkinThemeEntry>();
        public List<GameObject> excludedPrefabs = new List<GameObject>();
    }

    [Serializable]
    public sealed class SkinThemeEntry
    {
        public Sprite oldSprite;
        public Sprite newSprite;
    }
}

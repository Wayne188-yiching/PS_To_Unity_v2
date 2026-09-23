using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    [CreateAssetMenu(menuName = "Photoshop UI Importer/Skin Theme", fileName = "SkinTheme")]
    public sealed class PsUiSkinTheme : ScriptableObject
    {
        public UnityEngine.Object targetPrefabFolderAsset;
        /// <summary>已換好新圖的對照 Prefab 資料夾，用於依相同節點位置建立 Sprite 對照。</summary>
        public UnityEngine.Object referencePrefabFolderAsset;
        /// <summary>新美術 PNG 來源資料夾（可在 Assets 外，如 PS 匯出路徑）</summary>
        public string sourceArtFolder = string.Empty;
        /// <summary>僅供尺寸候選比對的新 Sprite 資料夾；必須在 Unity Assets 內。</summary>
        public UnityEngine.Object candidateSpriteFolderAsset;
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

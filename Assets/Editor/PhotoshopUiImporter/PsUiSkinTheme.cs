using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    [CreateAssetMenu(menuName = "Photoshop UI Importer/Skin Theme", fileName = "SkinTheme")]
    public sealed class PsUiSkinTheme : ScriptableObject
    {
        /// <summary>要換皮的 Prefab 資料夾：預覽／執行只寫這裡；已手動換上新圖的節點也在這裡。</summary>
        public UnityEngine.Object targetPrefabFolderAsset;
        /// <summary>舊版來源 Prefab 資料夾（唯讀）：只拿來和目標資料夾比對同一節點，學出舊圖 → 新圖；換皮不會寫入這裡。</summary>
        public UnityEngine.Object sourcePrefabFolderAsset;
        /// <summary>v2.17.1 以前的「已換好新圖的對照資料夾」（舊語意：target＝舊版、reference＝新版）。只供轉換，新流程不讀。</summary>
        [HideInInspector] public UnityEngine.Object referencePrefabFolderAsset;
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

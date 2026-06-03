using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    [CreateAssetMenu(menuName = "Photoshop UI Importer/Skin Theme", fileName = "SkinTheme")]
    public sealed class PsUiSkinTheme : ScriptableObject
    {
        public UnityEngine.Object targetPrefabFolderAsset;
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

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    [CreateAssetMenu(menuName = "Photoshop UI Importer/Skin Theme", fileName = "SkinTheme")]
    public sealed class PsUiSkinTheme : ScriptableObject
    {
        public string targetPrefabFolder = string.Empty;
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

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    [CreateAssetMenu(menuName = "Photoshop UI Importer/Skin Map", fileName = "SkinMap")]
    public sealed class SkinMap : ScriptableObject
    {
        public List<SkinMapEntry> entries = new List<SkinMapEntry>();

        public bool TryGetSprite(string skinKey, out Sprite sprite)
        {
            sprite = null;

            if (string.IsNullOrWhiteSpace(skinKey))
            {
                return false;
            }

            foreach (var entry in entries)
            {
                if (entry == null || entry.sprite == null)
                {
                    continue;
                }

                if (string.Equals(entry.skinKey, skinKey, StringComparison.OrdinalIgnoreCase))
                {
                    sprite = entry.sprite;
                    return true;
                }
            }

            return false;
        }
    }

    [Serializable]
    public sealed class SkinMapEntry
    {
        public string skinKey;
        public Sprite sprite;
    }
}

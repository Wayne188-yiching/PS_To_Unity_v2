using System.Collections.Generic;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    public sealed class SkinResolver
    {
        private readonly SkinMap skinMap;
        private readonly IReadOnlyDictionary<string, Sprite> importedSprites;

        public SkinResolver(SkinMap skinMap, IReadOnlyDictionary<string, Sprite> importedSprites)
        {
            this.skinMap = skinMap;
            this.importedSprites = importedSprites;
        }

        public Sprite Resolve(PhotoshopUiNode node)
        {
            if (node == null)
            {
                return null;
            }

            if (skinMap != null && skinMap.TryGetSprite(node.skinKey, out var skinSprite))
            {
                return skinSprite;
            }

            var key = PathUtility.NormalizeAssetKey(node.imagePath);
            if (!string.IsNullOrEmpty(key) && importedSprites != null && importedSprites.TryGetValue(key, out var importedSprite))
            {
                return importedSprite;
            }

            return null;
        }
    }
}

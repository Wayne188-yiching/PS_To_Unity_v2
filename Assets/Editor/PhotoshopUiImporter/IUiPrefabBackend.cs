using System.Collections.Generic;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    public interface IUiPrefabBackend
    {
        string DisplayName { get; }
        GameObject GeneratePrefab(PrefabGenerationContext context);
    }

    public sealed class PrefabGenerationContext
    {
        public PhotoshopUiLayout layout;
        public IReadOnlyDictionary<string, Sprite> importedSprites;
        public SkinResolver skinResolver;
        public TmpMapper tmpMapper;
        public string prefabOutputFolder;
        public string prefabName;
        public Vector2 referenceResolution = new Vector2(1920f, 1080f);
    }
}

using System.IO;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace PhotoshopToUnity.EditorImporter
{
    internal static class PhotoshopUiAssetUtility
    {
        internal static void EnsureAssetFolder(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                return;
            var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent))
                return;
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetPath));
        }

        internal static string ResolveSpriteAtlasFolder(string imageImportFolder)
        {
            var normalized = PathUtility.NormalizeAssetKey(imageImportFolder).TrimEnd('/');
            const string marker = "/SpriteAtlas";
            var markerIndex = normalized.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
                return normalized.Substring(0, markerIndex + marker.Length);

            // The advanced import path may point at the Atlas parent. Never treat that
            // parent as the SpriteAtlas root, or Base/CHS/CHT/EN will be created beside
            // the real SpriteAtlas folder.
            return normalized.EndsWith("/Atlas", System.StringComparison.OrdinalIgnoreCase)
                ? normalized + marker
                : normalized;
        }

        // [Client] 多語系字圖資料夾與 SpriteAtlas 包圖規範：每個語系一顆 atlas，且每顆
        // atlas 的 packables 只指到「單一語系資料夾」，不逐張圖丟——美術之後補圖只要丟進
        // 資料夾，不必回頭改 atlas。規範明文禁止一顆 atlas 直接指模組父夾（那會把 Base
        // 與所有語系收進同一張貼圖）。
        //
        // v2.13.1 曾改成只丟「這次被引用的 Sprite 清單」，用來擋掉像素重複的別名 PNG 進包。
        // 規範要求指資料夾，故改由 ImageImportService 在像素去重後刪掉「本工具複製進來的」
        // 別名 PNG，讓資料夾本身就不含冗餘檔案——去重回到它該在的層次，兩邊需求同時滿足。
        private static readonly string[] AtlasLanguages = { "Base", "CHS", "CHT", "EN" };

        internal static void CreateOrUpdateSpriteAtlases(string atlasRootFolder)
        {
            atlasRootFolder = PathUtility.NormalizeAssetKey(atlasRootFolder).TrimEnd('/');
            EnsureAssetFolder(atlasRootFolder);

            var packed = new System.Collections.Generic.List<SpriteAtlas>();

            foreach (var language in AtlasLanguages)
            {
                var languageFolder = $"{atlasRootFolder}/{language}";
                var atlasPath = ResolveLanguageAtlasPath(atlasRootFolder, language);
                if (string.IsNullOrEmpty(atlasPath))
                    continue;

                // 規範允許「資料夾先建好留空」，四個語系夾一律確保存在——順帶讓底下的
                // FindAssets 不會對無效路徑發出 Unity 錯誤。
                EnsureAssetFolder(languageFolder);

                // 規範：語系還沒有圖時不要建空 atlas（空 atlas 會產生無意義的資產與警告）。
                // 已存在的 atlas 仍會被重新設定，避免圖被清空後留下錯誤的 importer 設定。
                var hasSprites = AssetDatabase.FindAssets("t:Texture2D", new[] { languageFolder }).Length > 0;
                if (!hasSprites && SpriteAtlasAsset.Load(atlasPath) == null)
                    continue;

                var atlas = ConfigureLanguageAtlas(atlasPath, languageFolder);
                if (atlas != null)
                    packed.Add(atlas);
            }

            if (packed.Count > 0)
                SpriteAtlasUtility.PackAtlases(packed.ToArray(), EditorUserBuildSettings.activeBuildTarget);

            AssetDatabase.SaveAssets();
        }

        // Base -> "{模組名}_Atlas.spriteatlasv2"、其餘 -> "{模組名}_Atlas_{語系}.spriteatlasv2"，
        // 與規範的 <AtlasName>_<lang> 後綴式命名一致。
        private static string ResolveLanguageAtlasPath(string atlasRootFolder, string language)
        {
            atlasRootFolder = PathUtility.NormalizeAssetKey(atlasRootFolder).TrimEnd('/');
            var atlasParent = Path.GetDirectoryName(atlasRootFolder)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(atlasParent))
                return null;

            var suffix = string.Equals(language, "Base", System.StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : "_" + language;
            return $"{atlasParent}/{ResolveModuleName(atlasRootFolder)}_Atlas{suffix}.spriteatlasv2";
        }

        // Assets/Temp/<模組名>/Atlas/SpriteAtlas -> "<模組名>"
        private static string ResolveModuleName(string atlasRootFolder)
        {
            var atlasParent = Path.GetDirectoryName(atlasRootFolder)?.Replace('\\', '/');
            var moduleFolder = string.IsNullOrEmpty(atlasParent)
                ? null
                : Path.GetDirectoryName(atlasParent)?.Replace('\\', '/');

            var name = string.IsNullOrEmpty(moduleFolder) ? null : Path.GetFileName(moduleFolder);
            if (string.IsNullOrWhiteSpace(name))
                name = string.IsNullOrEmpty(atlasParent) ? null : Path.GetFileName(atlasParent);
            return string.IsNullOrWhiteSpace(name) ? "Module" : name;
        }

        private static SpriteAtlas ConfigureLanguageAtlas(string atlasPath, string languageFolder)
        {
            var atlas = SpriteAtlasAsset.Load(atlasPath);
            if (atlas == null)
            {
                // 先存一顆空的，讓 importer 設定可以在掛上 packable 前完整套用。
                atlas = new SpriteAtlasAsset();
                SpriteAtlasAsset.Save(atlas, atlasPath);
                AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
            }

            var atlasImporter = AssetImporter.GetAtPath(atlasPath) as SpriteAtlasImporter;
            if (atlasImporter == null)
                return null;

            const int maxTextureSize = 2048;
            var packing = atlasImporter.packingSettings;
            packing.enableRotation = false;
            packing.enableTightPacking = false;
            packing.padding = 4;

            var texture = atlasImporter.textureSettings;
            texture.readable = false;
            texture.generateMipMaps = false;
            texture.sRGB = true;
            texture.filterMode = FilterMode.Bilinear;

            atlasImporter.includeInBuild = true;
            atlasImporter.packingSettings = packing;
            atlasImporter.textureSettings = texture;
            atlasImporter.SetPlatformSettings(new TextureImporterPlatformSettings
            {
                name = "DefaultTexturePlatform",
                overridden = true,
                maxTextureSize = maxTextureSize,
                format = TextureImporterFormat.Automatic,
            });
            atlasImporter.SetPlatformSettings(CreateAtlasPlatformSettings("Android", maxTextureSize));
            atlasImporter.SetPlatformSettings(CreateAtlasPlatformSettings("iPhone", maxTextureSize));
            atlasImporter.SaveAndReimport();

            // packables 指單一語系資料夾，不逐張圖加入。
            atlas = new SpriteAtlasAsset();
            var folderObject = AssetDatabase.LoadAssetAtPath<Object>(languageFolder);
            if (folderObject != null)
                atlas.Add(new Object[] { folderObject });
            SpriteAtlasAsset.Save(atlas, atlasPath);
            AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);

            return AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
        }

        // Every language atlas is detached before TextureImporter work, otherwise each
        // SaveAndReimport triggers a full repack. CreateOrUpdateSpriteAtlases reattaches
        // the folders and packs exactly once at the end.
        internal static void DetachSpriteAtlasFolderForImageImport(string atlasRootFolder)
        {
            foreach (var language in AtlasLanguages)
            {
                var atlasPath = ResolveLanguageAtlasPath(atlasRootFolder, language);
                if (string.IsNullOrEmpty(atlasPath) || SpriteAtlasAsset.Load(atlasPath) == null)
                    continue;

                SpriteAtlasAsset.Save(new SpriteAtlasAsset(), atlasPath);
                AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
            }
        }

        private static TextureImporterPlatformSettings CreateAtlasPlatformSettings(string platformName, int maxTextureSize)
        {
            return new TextureImporterPlatformSettings
            {
                name = platformName,
                overridden = true,
                maxTextureSize = maxTextureSize,
                format = TextureImporterFormat.ASTC_6x6,
                compressionQuality = (int)TextureCompressionQuality.Best,
            };
        }

        private static void CreateOrUpdateLegacySpriteAtlas(string atlasFolder)
        {
            var atlasPath = $"{atlasFolder}/SpriteAtlas.spriteatlas";
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            if (atlas == null)
            {
                atlas = new SpriteAtlas();
                AssetDatabase.CreateAsset(atlas, atlasPath);
                atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            }

            var folderObject = AssetDatabase.LoadAssetAtPath<Object>(atlasFolder);
            if (folderObject != null)
                SpriteAtlasExtensions.Add(atlas, new Object[] { folderObject });

            // Packing 設定
            var packing = atlas.GetPackingSettings();
            packing.enableRotation     = false;
            packing.enableTightPacking = false;
            packing.padding            = 4;
            atlas.SetPackingSettings(packing);

            // Texture 設定
            var texture = atlas.GetTextureSettings();
            texture.readable        = false;
            texture.generateMipMaps = false;
            texture.sRGB            = true;
            texture.filterMode      = FilterMode.Bilinear;
            atlas.SetTextureSettings(texture);

            // Android 平台覆蓋
            atlas.SetPlatformSettings(new TextureImporterPlatformSettings
            {
                name               = "Android",
                overridden         = true,
                maxTextureSize     = 2048,
                format             = TextureImporterFormat.ASTC_6x6,
                compressionQuality = (int)TextureCompressionQuality.Best,
            });

            // iOS 平台覆蓋（Unity 內部名稱為 "iPhone"）
            atlas.SetPlatformSettings(new TextureImporterPlatformSettings
            {
                name               = "iPhone",
                overridden         = true,
                maxTextureSize     = 2048,
                format             = TextureImporterFormat.ASTC_6x6,
                compressionQuality = (int)TextureCompressionQuality.Best,
            });

            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();
        }

    }
}

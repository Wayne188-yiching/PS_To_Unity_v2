using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    [Serializable]
    public sealed class PhotoshopUiImportRequest
    {
        public string layoutJsonPath;
        public string sourceImageFolder;
        public string importFolder;
        public string prefabFolder;
        public string prefabName;
        public string projectFolder;
        public string defaultTmpFontAssetPath;
        public string defaultTmpMaterialPresetPath;
        public string tmpFontMapPath;
        public string skinMapPath;
        public string materialLibraryFolder;
        public float referenceResolutionX;
        public float referenceResolutionY;
        public float outlineThicknessMultiplier = 1f;
        public bool useResponsiveAnchor;
        public bool createSpriteAtlases = true;
    }

    [Serializable]
    public sealed class PhotoshopUiImportResult
    {
        public string status = "BLOCKED";
        public string prefabAssetPath;
        public int nodeCount;
        public int imageCount;
        public int textCount;
        public int dedupedSpriteCount;
        public long dedupedSpriteBytes;
        public List<string> errors = new List<string>();
        public List<string> warnings = new List<string>();

        public bool IsSuccess => string.Equals(status, "PASS", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Public, non-GUI entry point that reuses the existing deterministic importer.
    /// Agents may choose when to call it, but do not own image import or prefab logic.
    /// </summary>
    public static class PhotoshopUiImportService
    {
        public static PhotoshopUiImportResult Execute(PhotoshopUiImportRequest request)
        {
            var result = new PhotoshopUiImportResult();
            if (request == null)
            {
                result.errors.Add("Import request is null.");
                return result;
            }

            if (!LayoutReader.TryRead(request.layoutJsonPath, out var layout, out var readResult))
            {
                result.errors.AddRange(readResult.errors);
                return result;
            }

            foreach (var warning in readResult.warnings)
            {
                result.warnings.Add($"{warning.code}：{warning.node} {warning.message}".Trim());
            }

            result.nodeCount = CountNodes(layout.nodes);
            CountNodeTypes(layout.nodes, ref result.imageCount, ref result.textCount);

            var importFolder = NormalizeAssetFolder(request.importFolder);
            var prefabFolder = NormalizeAssetFolder(request.prefabFolder);
            if (string.IsNullOrEmpty(importFolder))
                result.errors.Add("importFolder must be inside this Unity project's Assets folder.");
            if (string.IsNullOrEmpty(prefabFolder))
                result.errors.Add("prefabFolder must be inside this Unity project's Assets folder.");
            if (string.IsNullOrWhiteSpace(request.sourceImageFolder) || !Directory.Exists(request.sourceImageFolder))
                result.errors.Add($"Source image folder does not exist: {request.sourceImageFolder}");
            if (result.errors.Count > 0)
                return result;

            var defaultFont = LoadOptionalAsset<TMP_FontAsset>(request.defaultTmpFontAssetPath, result);
            var defaultMaterial = LoadOptionalAsset<Material>(request.defaultTmpMaterialPresetPath, result);
            var tmpFontMap = LoadOptionalAsset<TmpFontMap>(request.tmpFontMapPath, result);
            var skinMap = LoadOptionalAsset<SkinMap>(request.skinMapPath, result);
            if (result.textCount > 0 && defaultFont == null)
            {
                result.errors.Add("TMP_DEFAULT_FONT_REQUIRED：This package contains text nodes; provide defaultTmpFontAssetPath.");
                return result;
            }

            PhotoshopUiImporterWindow.EnsureAssetFolder(importFolder);
            PhotoshopUiImporterWindow.EnsureAssetFolder(prefabFolder);

            var atlasRoot = PhotoshopUiImporterWindow.ResolveSpriteAtlasFolder(importFolder);
            if (request.createSpriteAtlases)
                PhotoshopUiImporterWindow.DetachSpriteAtlasFolderForImageImport(atlasRoot);

            var importResult = ImageImportService.ImportImages(layout, request.sourceImageFolder, importFolder);
            result.errors.AddRange(importResult.errors);
            result.warnings.AddRange(importResult.warnings);
            result.dedupedSpriteCount = importResult.dedupedSpriteCount;
            result.dedupedSpriteBytes = importResult.dedupedSpriteBytes;
            if (!importResult.IsValid)
                return result;

            if (request.createSpriteAtlases)
                PhotoshopUiImporterWindow.CreateOrUpdateSpriteAtlases(atlasRoot);

            var generatedMaterialFolder = string.IsNullOrWhiteSpace(request.projectFolder)
                ? "Assets/GeneratedMaterials"
                : $"Assets/Temp/{request.projectFolder}/Font/GeneratedMaterials";
            var tmpMapper = new TmpMapper(
                defaultFont,
                defaultMaterial,
                generatedMaterialFolder,
                string.IsNullOrWhiteSpace(request.materialLibraryFolder) ? null : request.materialLibraryFolder,
                request.outlineThicknessMultiplier <= 0f ? 1f : request.outlineThicknessMultiplier,
                tmpFontMap);
            var skinResolver = new SkinResolver(skinMap, importResult.sprites);
            var referenceResolution = new Vector2(
                request.referenceResolutionX > 0f ? request.referenceResolutionX : layout.canvas?.width ?? 1920f,
                request.referenceResolutionY > 0f ? request.referenceResolutionY : layout.canvas?.height ?? 1080f);

            var backend = new UGuiTmpPrefabBackend();
            var prefab = backend.GeneratePrefab(new PrefabGenerationContext
            {
                layout = layout,
                importedSprites = importResult.sprites,
                skinResolver = skinResolver,
                tmpMapper = tmpMapper,
                prefabOutputFolder = prefabFolder,
                prefabName = string.IsNullOrWhiteSpace(request.prefabName)
                    ? Path.GetFileNameWithoutExtension(request.layoutJsonPath)
                    : request.prefabName,
                referenceResolution = referenceResolution,
                useResponsiveAnchor = request.useResponsiveAnchor
            });

            result.warnings.AddRange(tmpMapper.OutlineOverflowWarnings);
            result.warnings.AddRange(tmpMapper.FontTokenWarnings);
            result.prefabAssetPath = AssetDatabase.GetAssetPath(prefab);
            result.status = "PASS";
            AssetDatabase.SaveAssets();
            return result;
        }

        private static string NormalizeAssetFolder(string path)
        {
            if (PathUtility.IsAssetPath(path))
                return PathUtility.NormalizeAssetKey(path).TrimEnd('/');
            return PathUtility.ToProjectRelativeAssetPath(path).TrimEnd('/');
        }

        private static T LoadOptionalAsset<T>(string assetPath, PhotoshopUiImportResult result) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;
            var normalized = NormalizeAssetFolder(assetPath);
            var asset = AssetDatabase.LoadAssetAtPath<T>(normalized);
            if (asset == null)
                result.warnings.Add($"DEPENDENCY_NOT_FOUND：{typeof(T).Name} {assetPath}");
            return asset;
        }

        private static int CountNodes(IReadOnlyList<PhotoshopUiNode> nodes)
        {
            if (nodes == null) return 0;
            var count = 0;
            foreach (var node in nodes)
                count += node == null ? 0 : 1 + CountNodes(node.children);
            return count;
        }

        private static void CountNodeTypes(IReadOnlyList<PhotoshopUiNode> nodes, ref int images, ref int texts)
        {
            if (nodes == null) return;
            foreach (var node in nodes)
            {
                if (node == null) continue;
                if (node.NormalizedType == "image") images++;
                if (node.NormalizedType == "text") texts++;
                CountNodeTypes(node.children, ref images, ref texts);
            }
        }
    }
}

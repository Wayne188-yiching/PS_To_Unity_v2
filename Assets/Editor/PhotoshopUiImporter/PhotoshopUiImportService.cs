using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    [Serializable]
    public sealed class PhotoshopUiImportRequest
    {
        public string runId;
        public string requestFingerprint;
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
    public sealed class PhotoshopUiImportDiagnostic
    {
        public string code;
        public string severity;
        public string phase;
        // Kept during the transition so existing receipt readers do not break.
        public string stage;
        public string node;
        public string message;
        public List<string> evidence = new List<string>();
        public string suggestedAction;
        public bool safeToAutoFix;
    }

    [Serializable]
    public sealed class PhotoshopUiImportResult
    {
        public string status = "BLOCKED";
        public string stage = "REQUEST";
        public string runId;
        public string requestFingerprint;
        public string layoutSha256;
        public string prefabSha256;
        public string prefabAssetPath;
        public int nodeCount;
        public int imageCount;
        public int textCount;
        public int dedupedSpriteCount;
        public long dedupedSpriteBytes;
        public int outlineWarningCount;
        public int fontTokenWarningCount;
        public List<string> errors = new List<string>();
        public List<string> warnings = new List<string>();
        public List<PhotoshopUiImportDiagnostic> diagnostics = new List<PhotoshopUiImportDiagnostic>();

        public bool IsSuccess => string.Equals(status, "PASS", StringComparison.OrdinalIgnoreCase) && errors.Count == 0;

        public void AddError(
            string code,
            string message,
            string node = null,
            List<string> evidence = null,
            string suggestedAction = null,
            bool safeToAutoFix = false)
        {
            status = "BLOCKED";
            errors.Add(message);
            diagnostics.Add(new PhotoshopUiImportDiagnostic
            {
                code = code,
                severity = "error",
                phase = stage,
                stage = stage,
                node = node,
                message = message,
                evidence = evidence ?? new List<string>(),
                suggestedAction = suggestedAction,
                safeToAutoFix = safeToAutoFix,
            });
        }

        public void AddWarning(
            string code,
            string message,
            string node = null,
            List<string> evidence = null,
            string suggestedAction = null,
            bool safeToAutoFix = false)
        {
            warnings.Add(message);
            diagnostics.Add(new PhotoshopUiImportDiagnostic
            {
                code = code,
                severity = "warning",
                phase = stage,
                stage = stage,
                node = node,
                message = message,
                evidence = evidence ?? new List<string>(),
                suggestedAction = suggestedAction,
                safeToAutoFix = safeToAutoFix,
            });
        }
    }

    /// <summary>
    /// Public, non-GUI entry point that reuses the existing deterministic importer.
    /// Agents may choose when to call it, but do not own image import or prefab logic.
    /// </summary>
    public static class PhotoshopUiImportService
    {
        public static PhotoshopUiImportResult Execute(PhotoshopUiImportRequest request)
        {
            return Execute(request, new UGuiTmpPrefabBackend());
        }

        internal static PhotoshopUiImportResult Execute(PhotoshopUiImportRequest request, IUiPrefabBackend backend)
        {
            var result = new PhotoshopUiImportResult { runId = request?.runId, requestFingerprint = request?.requestFingerprint };
            try
            {
                ExecuteCore(request, backend, result);
            }
            catch (Exception exception)
            {
                result.AddError("IMPORT_EXCEPTION", exception.ToString());
            }
            return result;
        }

        private static void ExecuteCore(PhotoshopUiImportRequest request, IUiPrefabBackend backend, PhotoshopUiImportResult result)
        {
            if (request == null)
            {
                result.AddError("REQUEST_NULL", "Import request is null.");
                return;
            }

            result.stage = "LAYOUT";
            result.layoutSha256 = File.Exists(request.layoutJsonPath) ? Sha256File(request.layoutJsonPath) : null;
            if (!LayoutReader.TryRead(request.layoutJsonPath, out var layout, out var readResult))
            {
                foreach (var error in readResult.errors) result.AddError("LAYOUT_INVALID", error);
                return;
            }

            foreach (var warning in readResult.warnings)
            {
                result.AddWarning(warning.code, $"{warning.code}：{warning.node} {warning.message}".Trim());
            }

            result.nodeCount = CountNodes(layout.nodes);
            CountNodeTypes(layout.nodes, ref result.imageCount, ref result.textCount);

            result.stage = "DEPENDENCIES";
            var importFolder = NormalizeAssetFolder(request.importFolder);
            var prefabFolder = NormalizeAssetFolder(request.prefabFolder);
            if (string.IsNullOrEmpty(importFolder))
                result.AddError("IMPORT_FOLDER_INVALID", "importFolder must be inside this Unity project's Assets folder.");
            if (string.IsNullOrEmpty(prefabFolder))
                result.AddError("PREFAB_FOLDER_INVALID", "prefabFolder must be inside this Unity project's Assets folder.");
            var sourceFolder = PathUtility.IsAssetPath(request.sourceImageFolder)
                ? PathUtility.ToAbsolutePath(request.sourceImageFolder) : request.sourceImageFolder;
            if (result.imageCount > 0 && (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder)))
                result.AddError("SOURCE_IMAGES_MISSING", $"Source image folder does not exist: {request.sourceImageFolder}");
            var prefabName = string.IsNullOrWhiteSpace(request.prefabName)
                ? Path.GetFileNameWithoutExtension(request.layoutJsonPath) : request.prefabName;
            if (!IsSinglePathSegment(prefabName))
                result.AddError("PREFAB_NAME_INVALID", "prefabName must be a single file name, without path separators or traversal.");
            if (!string.IsNullOrWhiteSpace(request.projectFolder) && !IsSinglePathSegment(request.projectFolder))
                result.AddError("PROJECT_FOLDER_INVALID", "projectFolder must be a single folder name, without path separators or traversal.");
            var generatedMaterialFolder = NormalizeAssetFolder(string.IsNullOrWhiteSpace(request.projectFolder)
                ? "Assets/GeneratedMaterials" : $"Assets/Temp/{request.projectFolder}/Font/GeneratedMaterials");
            if (string.IsNullOrEmpty(generatedMaterialFolder))
                result.AddError("MATERIAL_FOLDER_INVALID", "Generated materials must stay within Assets.");
            string materialLibrary = null;
            if (!string.IsNullOrWhiteSpace(request.materialLibraryFolder))
            {
                materialLibrary = NormalizeAssetFolder(request.materialLibraryFolder);
                if (string.IsNullOrEmpty(materialLibrary) || !AssetDatabase.IsValidFolder(materialLibrary))
                    result.AddError("MATERIAL_LIBRARY_MISSING", $"Material library folder not found: {request.materialLibraryFolder}");
            }
            if (result.errors.Count > 0)
                return;

            var defaultFont = LoadOptionalAsset<TMP_FontAsset>(request.defaultTmpFontAssetPath, result);
            var defaultMaterial = LoadOptionalAsset<Material>(request.defaultTmpMaterialPresetPath, result);
            var tmpFontMap = LoadOptionalAsset<TmpFontMap>(request.tmpFontMapPath, result);
            var skinMap = LoadOptionalAsset<SkinMap>(request.skinMapPath, result);
            if (result.textCount > 0 && defaultFont == null)
            {
                result.AddError(
                    "TMP_DEFAULT_FONT_REQUIRED",
                    "TMP_DEFAULT_FONT_REQUIRED：This package contains text nodes; provide defaultTmpFontAssetPath.",
                    suggestedAction: "Create or select a TMP Font Asset, then pass its project-relative Assets path.");
                return;
            }

            if (result.textCount > 0 && defaultMaterial == null && string.IsNullOrWhiteSpace(request.defaultTmpMaterialPresetPath))
                result.AddWarning(
                    "TMP_DEFAULT_MATERIAL_RECOMMENDED",
                    "此 UI Package 含文字節點。未指定額外材質球時，會使用 Font Asset 或 TmpFontMap 內的材質；若要精確重現樣式，請明確指定。",
                    suggestedAction: "Assign a matching TMP material preset, or verify every mapped font uses its intended Font Asset material.");
            if (result.errors.Count > 0) return;

            PhotoshopUiAssetUtility.EnsureAssetFolder(importFolder);
            PhotoshopUiAssetUtility.EnsureAssetFolder(prefabFolder);

            result.stage = "IMAGE_IMPORT";
            var atlasRoot = PhotoshopUiAssetUtility.ResolveSpriteAtlasFolder(importFolder);
            if (request.createSpriteAtlases)
                PhotoshopUiAssetUtility.DetachSpriteAtlasFolderForImageImport(atlasRoot);

            ImageImportResult importResult;
            try
            {
                importResult = ImageImportService.ImportImages(layout, sourceFolder, importFolder);
            }
            finally
            {
                // Restore folder packables even when image import fails.
                if (request.createSpriteAtlases)
                {
                    result.stage = "ATLAS";
                    PhotoshopUiAssetUtility.CreateOrUpdateSpriteAtlases(atlasRoot);
                }
            }
            result.stage = "IMAGE_IMPORT";
            foreach (var error in importResult.errors) result.AddError("IMAGE_IMPORT_FAILED", error);
            foreach (var warning in importResult.warnings) result.AddWarning("IMAGE_IMPORT_WARNING", warning);
            result.dedupedSpriteCount = importResult.dedupedSpriteCount;
            result.dedupedSpriteBytes = importResult.dedupedSpriteBytes;
            if (importResult.redundantSourceImages.Count > 0)
                result.AddWarning("ATLAS_REDUNDANT_SOURCE_IMAGES",
                    $"[Atlas] {importResult.redundantSourceImages.Count} 張像素重複的 PNG 位於來源資料夾內，" +
                    "無法自動刪除（刪了會破壞來源），它們仍會被打進圖集。建議在 Photoshop 端讓重複圖層共用同一個名稱：\n" +
                    string.Join("\n", importResult.redundantSourceImages));
            if (!importResult.IsValid)
                return;

            result.stage = "PREFAB";
            var tmpMapper = new TmpMapper(
                defaultFont,
                defaultMaterial,
                generatedMaterialFolder,
                materialLibrary,
                request.outlineThicknessMultiplier <= 0f ? 1f : request.outlineThicknessMultiplier,
                tmpFontMap);
            var skinResolver = new SkinResolver(skinMap, importResult.sprites);
            var referenceResolution = new Vector2(
                request.referenceResolutionX > 0f ? request.referenceResolutionX : layout.canvas?.width ?? 1920f,
                request.referenceResolutionY > 0f ? request.referenceResolutionY : layout.canvas?.height ?? 1080f);

            GameObject prefab;
            try
            {
                prefab = backend.GeneratePrefab(new PrefabGenerationContext
            {
                layout = layout,
                importedSprites = importResult.sprites,
                skinResolver = skinResolver,
                tmpMapper = tmpMapper,
                prefabOutputFolder = prefabFolder,
                prefabName = prefabName,
                referenceResolution = referenceResolution,
                useResponsiveAnchor = request.useResponsiveAnchor
                });
            }
            finally
            {
                result.outlineWarningCount = tmpMapper.OutlineOverflowWarnings.Count;
                result.fontTokenWarningCount = tmpMapper.FontTokenWarnings.Count;
                foreach (var warning in tmpMapper.OutlineOverflowWarnings) result.AddWarning("OUTLINE_OVERFLOW", warning);
                foreach (var warning in tmpMapper.FontTokenWarnings) result.AddWarning("FONT_TOKEN_UNMAPPED", warning);
            }
            if (prefab == null)
            {
                result.AddError("PREFAB_NULL", "The backend returned no prefab asset.");
                return;
            }
            result.prefabAssetPath = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(result.prefabAssetPath) || !PrefabUtility.IsPartOfPrefabAsset(prefab))
            {
                result.AddError("PREFAB_NOT_SAVED", "The backend did not return a saved prefab asset.");
                return;
            }
            result.stage = "SAVE";
            AssetDatabase.SaveAssets();
            result.prefabSha256 = Sha256File(PathUtility.ToAbsolutePath(result.prefabAssetPath));
            if (result.layoutSha256 != Sha256File(request.layoutJsonPath))
            {
                result.AddError("LAYOUT_CHANGED", "Layout changed during generation; rerun with stable input.");
                return;
            }
            result.stage = "COMPLETE";
            result.status = "PASS";
        }

        internal static string NormalizeAssetFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var assets = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetParent(assets).FullName, path));
            var comparison = Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(full, assets, comparison)) return "Assets";
            if (!full.StartsWith(assets + Path.DirectorySeparatorChar, comparison)) return string.Empty;
            return "Assets/" + full.Substring(assets.Length + 1).Replace('\\', '/');
        }

        private static bool IsSinglePathSegment(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value != "." && value != ".." &&
                value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && value.IndexOf('/') < 0 && value.IndexOf('\\') < 0;
        }

        internal static string Sha256File(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static T LoadOptionalAsset<T>(string assetPath, PhotoshopUiImportResult result) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;
            var normalized = NormalizeAssetFolder(assetPath);
            var asset = string.IsNullOrEmpty(normalized) ? null : AssetDatabase.LoadAssetAtPath<T>(normalized);
            if (asset == null)
                result.AddError("DEPENDENCY_NOT_FOUND", $"DEPENDENCY_NOT_FOUND：{typeof(T).Name} {assetPath}");
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

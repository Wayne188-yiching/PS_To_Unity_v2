using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

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
    public sealed class PhotoshopUiPrefabNodeSnapshot
    {
        public string path;
        public string name;
        public bool activeSelf;
        public int siblingIndex;
        public float x;
        public float y;
        public float width;
        public float height;
        public List<string> components = new List<string>();
        public string spriteAssetPath;
        public string imageType;
        public bool imageRaycastTarget;
        public string text;
        public string fontAssetPath;
        public string materialAssetPath;
        public float fontSize;
        public float characterSpacing;
        public float lineSpacing;
        public string textAlignment;
        public string textColor;
        public bool textVertexGradient;
        public string textGradientTopLeft;
        public string textGradientBottomLeft;
        public bool scrollHorizontal;
        public bool scrollVertical;
        public string viewportPath;
        public string contentPath;
        public string horizontalScrollbarPath;
        public string verticalScrollbarPath;
        public string scrollbarHandlePath;
        public int rectMaskSoftnessX;
        public int rectMaskSoftnessY;
        public bool maskShowGraphic;
        public float layoutSpacing;
        public int layoutPaddingLeft;
        public int layoutPaddingRight;
        public int layoutPaddingTop;
        public int layoutPaddingBottom;
        public string layoutChildAlignment;
        public bool layoutChildControlWidth;
        public bool layoutChildControlHeight;
        public bool layoutChildForceExpandWidth;
        public bool layoutChildForceExpandHeight;
        public float gridCellSizeX;
        public float gridCellSizeY;
        public float gridSpacingX;
        public float gridSpacingY;
        public string gridStartAxis;
        public string gridConstraint;
        public int gridConstraintCount;
        public string contentSizeHorizontalFit;
        public string contentSizeVerticalFit;
        public string scrollMovementType;
        public bool scrollInertia;
        public float scrollDecelerationRate;
        public float scrollSensitivity;
        public string scrollbarDirection;
        public string scrollbarTargetGraphicPath;
    }

    [Serializable]
    public sealed class PhotoshopUiImageBinding
    {
        public string sourceName;
        public string imagePath;
        public string skinKey;
        public string role;
        public string sourceKind;
        public string spriteAssetPath;
    }

    [Serializable]
    public sealed class PhotoshopUiImportedImage
    {
        public string imagePath;
        public string spriteAssetPath;
        public string sourcePixelHash;
        public string spritePixelHash;
    }

    [Serializable]
    public sealed class PhotoshopUiTextBinding
    {
        public string sourceName;
        public string fontToken;
        public string materialToken;
        public string role;
        public string resolutionSource;
        public string matchedFontKeyword;
        public string fontAssetPath;
        public string materialAssetPath;
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
        public List<PhotoshopUiPrefabNodeSnapshot> prefabNodes = new List<PhotoshopUiPrefabNodeSnapshot>();
        public List<PhotoshopUiImageBinding> imageBindings = new List<PhotoshopUiImageBinding>();
        public List<PhotoshopUiImportedImage> importedImages = new List<PhotoshopUiImportedImage>();
        public List<PhotoshopUiTextBinding> textBindings = new List<PhotoshopUiTextBinding>();
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
            foreach (var pair in importResult.sprites)
            {
                var sourceImagePath = Path.Combine(
                    sourceFolder, pair.Key.Replace('/', Path.DirectorySeparatorChar));
                var spriteAssetPath = pair.Value == null ? null : AssetDatabase.GetAssetPath(pair.Value);
                result.importedImages.Add(new PhotoshopUiImportedImage
                {
                    imagePath = pair.Key,
                    spriteAssetPath = spriteAssetPath,
                    sourcePixelHash = ImageImportService.ComputePixelHashFromFile(sourceImagePath),
                    spritePixelHash = string.IsNullOrWhiteSpace(spriteAssetPath)
                        ? null : ImageImportService.ComputePixelHashFromFile(PathUtility.ToAbsolutePath(spriteAssetPath)),
                });
            }
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

            var generationContext = new PrefabGenerationContext
            {
                layout = layout,
                importedSprites = importResult.sprites,
                skinResolver = skinResolver,
                tmpMapper = tmpMapper,
                prefabOutputFolder = prefabFolder,
                prefabName = prefabName,
                referenceResolution = referenceResolution,
                useResponsiveAnchor = request.useResponsiveAnchor
            };
            GameObject prefab;
            try
            {
                prefab = backend.GeneratePrefab(generationContext);
            }
            finally
            {
                result.outlineWarningCount = tmpMapper.OutlineOverflowWarnings.Count;
                result.fontTokenWarningCount = tmpMapper.FontTokenWarnings.Count;
                foreach (var warning in tmpMapper.OutlineOverflowWarnings) result.AddWarning("OUTLINE_OVERFLOW", warning);
                foreach (var warning in tmpMapper.FontTokenWarnings) result.AddWarning("FONT_TOKEN_UNMAPPED", warning);
            }
            result.imageBindings = generationContext.imageBindings;
            result.textBindings = generationContext.textBindings;
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
            result.prefabNodes = BuildPrefabSnapshot(prefab);
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

        private static List<PhotoshopUiPrefabNodeSnapshot> BuildPrefabSnapshot(GameObject prefab)
        {
            var snapshots = new List<PhotoshopUiPrefabNodeSnapshot>();
            if (prefab == null) return snapshots;
            var root = prefab.transform as RectTransform;
            if (root == null) return snapshots;
            AddSnapshot(root, root, EscapePathSegment(root.name), snapshots);
            return snapshots;
        }

        private static void AddSnapshot(
            RectTransform root,
            RectTransform current,
            string path,
            List<PhotoshopUiPrefabNodeSnapshot> snapshots)
        {
            var snapshot = new PhotoshopUiPrefabNodeSnapshot
            {
                path = path,
                name = current.name,
                activeSelf = current.gameObject.activeSelf,
                siblingIndex = current.GetSiblingIndex(),
            };

            var corners = new Vector3[4];
            current.GetWorldCorners(corners);
            var first = root.InverseTransformPoint(corners[0]);
            var minX = first.x;
            var maxX = first.x;
            var minY = first.y;
            var maxY = first.y;
            for (var index = 1; index < corners.Length; index++)
            {
                var point = root.InverseTransformPoint(corners[index]);
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }
            snapshot.x = minX + root.rect.width * root.pivot.x;
            snapshot.y = root.rect.height * (1f - root.pivot.y) - maxY;
            snapshot.width = maxX - minX;
            snapshot.height = maxY - minY;

            foreach (var component in current.GetComponents<Component>())
            {
                if (component != null) snapshot.components.Add(component.GetType().Name);
            }

            var image = current.GetComponent<Image>();
            if (image != null)
            {
                snapshot.spriteAssetPath = image.sprite == null ? null : AssetDatabase.GetAssetPath(image.sprite);
                snapshot.imageType = image.type.ToString();
                snapshot.imageRaycastTarget = image.raycastTarget;
            }

            var text = current.GetComponent<TextMeshProUGUI>();
            if (text != null)
            {
                snapshot.text = text.text;
                snapshot.fontAssetPath = text.font == null ? null : AssetDatabase.GetAssetPath(text.font);
                snapshot.materialAssetPath = text.fontSharedMaterial == null ? null : AssetDatabase.GetAssetPath(text.fontSharedMaterial);
                snapshot.fontSize = text.fontSize;
                snapshot.characterSpacing = text.characterSpacing;
                snapshot.lineSpacing = text.lineSpacing;
                snapshot.textAlignment = text.alignment.ToString();
                snapshot.textColor = "#" + ColorUtility.ToHtmlStringRGBA(text.color);
                snapshot.textVertexGradient = text.enableVertexGradient;
                snapshot.textGradientTopLeft = "#" + ColorUtility.ToHtmlStringRGBA(text.colorGradient.topLeft);
                snapshot.textGradientBottomLeft = "#" + ColorUtility.ToHtmlStringRGBA(text.colorGradient.bottomLeft);
            }

            var scrollRect = current.GetComponent<ScrollRect>();
            if (scrollRect != null)
            {
                snapshot.scrollHorizontal = scrollRect.horizontal;
                snapshot.scrollVertical = scrollRect.vertical;
                snapshot.viewportPath = SnapshotPath(root, scrollRect.viewport);
                snapshot.contentPath = SnapshotPath(root, scrollRect.content);
                snapshot.horizontalScrollbarPath = SnapshotPath(root, scrollRect.horizontalScrollbar == null ? null : scrollRect.horizontalScrollbar.transform);
                snapshot.verticalScrollbarPath = SnapshotPath(root, scrollRect.verticalScrollbar == null ? null : scrollRect.verticalScrollbar.transform);
                snapshot.scrollMovementType = scrollRect.movementType.ToString();
                snapshot.scrollInertia = scrollRect.inertia;
                snapshot.scrollDecelerationRate = scrollRect.decelerationRate;
                snapshot.scrollSensitivity = scrollRect.scrollSensitivity;
            }

            var scrollbar = current.GetComponent<Scrollbar>();
            if (scrollbar != null)
            {
                snapshot.scrollbarHandlePath = SnapshotPath(root, scrollbar.handleRect);
                snapshot.scrollbarDirection = scrollbar.direction.ToString();
                snapshot.scrollbarTargetGraphicPath = SnapshotPath(
                    root, scrollbar.targetGraphic == null ? null : scrollbar.targetGraphic.transform);
            }

            var rectMask = current.GetComponent<RectMask2D>();
            if (rectMask != null)
            {
                snapshot.rectMaskSoftnessX = rectMask.softness.x;
                snapshot.rectMaskSoftnessY = rectMask.softness.y;
            }
            var mask = current.GetComponent<Mask>();
            if (mask != null) snapshot.maskShowGraphic = mask.showMaskGraphic;

            var layoutGroup = current.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (layoutGroup != null)
            {
                snapshot.layoutSpacing = layoutGroup.spacing;
                snapshot.layoutPaddingLeft = layoutGroup.padding.left;
                snapshot.layoutPaddingRight = layoutGroup.padding.right;
                snapshot.layoutPaddingTop = layoutGroup.padding.top;
                snapshot.layoutPaddingBottom = layoutGroup.padding.bottom;
                snapshot.layoutChildAlignment = layoutGroup.childAlignment.ToString();
                snapshot.layoutChildControlWidth = layoutGroup.childControlWidth;
                snapshot.layoutChildControlHeight = layoutGroup.childControlHeight;
                snapshot.layoutChildForceExpandWidth = layoutGroup.childForceExpandWidth;
                snapshot.layoutChildForceExpandHeight = layoutGroup.childForceExpandHeight;
            }
            var grid = current.GetComponent<GridLayoutGroup>();
            if (grid != null)
            {
                snapshot.layoutPaddingLeft = grid.padding.left;
                snapshot.layoutPaddingRight = grid.padding.right;
                snapshot.layoutPaddingTop = grid.padding.top;
                snapshot.layoutPaddingBottom = grid.padding.bottom;
                snapshot.layoutChildAlignment = grid.childAlignment.ToString();
                snapshot.gridCellSizeX = grid.cellSize.x;
                snapshot.gridCellSizeY = grid.cellSize.y;
                snapshot.gridSpacingX = grid.spacing.x;
                snapshot.gridSpacingY = grid.spacing.y;
                snapshot.gridStartAxis = grid.startAxis.ToString();
                snapshot.gridConstraint = grid.constraint.ToString();
                snapshot.gridConstraintCount = grid.constraintCount;
            }
            var contentSize = current.GetComponent<ContentSizeFitter>();
            if (contentSize != null)
            {
                snapshot.contentSizeHorizontalFit = contentSize.horizontalFit.ToString();
                snapshot.contentSizeVerticalFit = contentSize.verticalFit.ToString();
            }

            snapshots.Add(snapshot);
            for (var index = 0; index < current.childCount; index++)
            {
                if (!(current.GetChild(index) is RectTransform child)) continue;
                var childPath = path + "/" + child.GetSiblingIndex() + ":" + EscapePathSegment(child.name);
                AddSnapshot(root, child, childPath, snapshots);
            }
        }

        private static string SnapshotPath(RectTransform root, Transform target)
        {
            if (target == null) return null;
            var segments = new List<string>();
            var current = target;
            while (current != null)
            {
                if (current == root)
                {
                    segments.Add(EscapePathSegment(current.name));
                    segments.Reverse();
                    return string.Join("/", segments);
                }
                segments.Add(current.GetSiblingIndex() + ":" + EscapePathSegment(current.name));
                current = current.parent;
            }
            return null;
        }

        private static string EscapePathSegment(string value)
        {
            return (value ?? string.Empty).Replace("%", "%25").Replace("/", "%2F");
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

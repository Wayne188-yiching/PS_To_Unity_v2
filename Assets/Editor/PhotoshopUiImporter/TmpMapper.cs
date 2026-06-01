using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    public sealed class TmpMapper
    {
        private readonly string generatedMaterialFolder;
        private readonly string materialLibraryFolder;
        private readonly TMP_FontAsset defaultFontAsset;
        private readonly Material defaultMaterialPreset;

        public TmpMapper(TMP_FontAsset defaultFontAsset, Material defaultMaterialPreset, string generatedMaterialFolder = null, string materialLibraryFolder = null)
        {
            this.defaultFontAsset = defaultFontAsset;
            this.defaultMaterialPreset = defaultMaterialPreset;
            this.generatedMaterialFolder = string.IsNullOrWhiteSpace(generatedMaterialFolder)
                ? "Assets/GeneratedMaterials"
                : generatedMaterialFolder;
            this.materialLibraryFolder = materialLibraryFolder;
        }

        public void Apply(TextMeshProUGUI target, PhotoshopUiNode node)
        {
            if (target == null || node == null)
            {
                return;
            }

            target.text = node.text ?? string.Empty;
            target.raycastTarget = false;
            target.enableAutoSizing = false;
            target.enableWordWrapping = false;
            target.overflowMode = TextOverflowModes.Overflow;
            target.margin = Vector4.zero;
            target.fontSize = node.fontSize > 0 ? node.fontSize : 24;
            target.characterSpacing = node.characterSpacing;
            target.lineSpacing = node.lineSpacing;
            target.color = ParseColor(node.color, Color.white);
            target.alignment = ParseAlignment(node.alignment, TextAlignmentOptions.Left);

            var fontAsset = defaultFontAsset != null ? defaultFontAsset : TMP_Settings.defaultFontAsset;
            if (fontAsset != null)
            {
                target.font = fontAsset;
            }

            // outline 材質：有 outline 資料時解析專屬材質，否則 fallback 到 defaultMaterialPreset
            var resolvedMaterial = ResolveGeneratedMaterial(defaultMaterialPreset, node) ?? defaultMaterialPreset;
            if (resolvedMaterial != null)
            {
                target.fontSharedMaterial = resolvedMaterial;
            }
        }

        private Material ResolveGeneratedMaterial(Material baseMaterial, PhotoshopUiNode node)
        {
            if (baseMaterial == null || node == null || node.outlineWidth <= 0f || string.IsNullOrWhiteSpace(node.outlineColor))
            {
                return baseMaterial;
            }

            if (!ColorUtility.TryParseHtmlString(node.outlineColor, out var outlineColor))
            {
                return baseMaterial;
            }

            outlineColor.a = Mathf.Clamp01(node.outlineOpacity <= 0f ? 1f : node.outlineOpacity);

            // 先從材質庫尋找相近的現成材質球，找到就直接用，不新增
            var targetWidth = ConvertPhotoshopStrokeWidth(node.outlineWidth, node.fontSize);
            var libraryMatch = FindLibraryMaterial(outlineColor, targetWidth);
            if (libraryMatch != null)
                return libraryMatch;

            Directory.CreateDirectory(PathUtility.ToAbsolutePath(generatedMaterialFolder));

            var token = string.IsNullOrWhiteSpace(node.materialToken) ? BuildOutlineToken(node) : node.materialToken;
            var materialName = $"{MakeSafeFileName(baseMaterial.name)}_{MakeSafeFileName(token)}";
            var materialPath = $"{generatedMaterialFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            if (material == null)
            {
                material = new Material(baseMaterial)
                {
                    name = materialName
                };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else
            {
                material.CopyPropertiesFromMaterial(baseMaterial);
            }

            if (material.HasProperty(ShaderUtilities.ID_OutlineColor))
            {
                material.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
            }

            if (material.HasProperty(ShaderUtilities.ID_OutlineWidth))
            {
                material.SetFloat(ShaderUtilities.ID_OutlineWidth, targetWidth);
            }

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static float ConvertPhotoshopStrokeWidth(float strokeWidthPixels, float fontSize)
        {
            // TMP SDF formula: _OutlineWidth = strokePx × (SamplingPointSize / Padding) / fontSize
            // Derived from: outlinePixels = _OutlineWidth × Padding × (fontSize / SamplingPointSize)
            // Common TMP default: SamplingPointSize=25, Padding=5 → ratio = 5
            // Clamp upper bound raised to 1.0 to support large strokes
            var referenceSize = fontSize > 0f ? fontSize : 40f;
            const float sdfRatio = 5f; // SamplingPointSize(25) / Padding(5)
            return Mathf.Clamp(strokeWidthPixels * sdfRatio / referenceSize, 0.01f, 1.0f);
        }

        private Material FindLibraryMaterial(Color targetOutlineColor, float targetOutlineWidth)
        {
            if (string.IsNullOrWhiteSpace(materialLibraryFolder) || !AssetDatabase.IsValidFolder(materialLibraryFolder))
                return null;

            var guids = AssetDatabase.FindAssets("t:Material", new[] { materialLibraryFolder });
            var libraryRoot = materialLibraryFolder.TrimEnd('/');

            // 第一步：找所有顏色相近的候選材質球
            // 顏色是材質球的主要識別依據（綠框、黑框、金框），Thickness 是次要條件
            const float colorTolerance = 0.08f; // ±20/255 per channel
            Material bestMatch = null;
            float bestWidthDiff = float.MaxValue;

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                // 只比對最上層資料夾，不遞迴子資料夾
                if (!string.Equals(
                        Path.GetDirectoryName(path)?.Replace('\\', '/'),
                        libraryRoot,
                        System.StringComparison.OrdinalIgnoreCase))
                    continue;

                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null ||
                    !mat.HasProperty(ShaderUtilities.ID_OutlineColor) ||
                    !mat.HasProperty(ShaderUtilities.ID_OutlineWidth))
                    continue;

                var matColor = mat.GetColor(ShaderUtilities.ID_OutlineColor);

                // 顏色不符直接跳過
                if (Mathf.Abs(matColor.r - targetOutlineColor.r) >= colorTolerance ||
                    Mathf.Abs(matColor.g - targetOutlineColor.g) >= colorTolerance ||
                    Mathf.Abs(matColor.b - targetOutlineColor.b) >= colorTolerance)
                    continue;

                // 顏色符合：以 Thickness 差距排序，選最接近的
                var matWidth = mat.GetFloat(ShaderUtilities.ID_OutlineWidth);
                var widthDiff = targetOutlineWidth > 0f
                    ? Mathf.Abs(matWidth / targetOutlineWidth - 1f)
                    : Mathf.Abs(matWidth);

                if (widthDiff < bestWidthDiff)
                {
                    bestWidthDiff = widthDiff;
                    bestMatch = mat;
                }
            }

            return bestMatch;
        }

        private static string BuildOutlineToken(PhotoshopUiNode node)
        {
            var color = string.IsNullOrWhiteSpace(node.outlineColor) ? "outline" : node.outlineColor.Trim().TrimStart('#');
            return $"outline_{color}_{Mathf.RoundToInt(node.outlineWidth)}";
        }

        private static string MakeSafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "material";
            }

            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidChar, '_');
            }

            return value.Replace('#', '_').Replace(' ', '_');
        }

        private static Color ParseColor(string value, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            if (ColorUtility.TryParseHtmlString(value, out var color))
            {
                return color;
            }

            return fallback;
        }

        private static TextAlignmentOptions ParseAlignment(string value, TextAlignmentOptions fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "left":
                case "top-left":
                case "topleft":
                    return TextAlignmentOptions.Left;
                case "center":
                case "middle":
                    return TextAlignmentOptions.Center;
                case "right":
                case "top-right":
                case "topright":
                    return TextAlignmentOptions.Right;
                case "justified":
                case "justify":
                    return TextAlignmentOptions.Justified;
                case "top":
                case "top-center":
                case "topcenter":
                    return TextAlignmentOptions.Top;
                case "bottom":
                case "bottom-center":
                case "bottomcenter":
                    return TextAlignmentOptions.Bottom;
                default:
                    return fallback;
            }
        }
    }
}

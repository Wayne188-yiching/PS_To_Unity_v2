using System.Collections.Generic;
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

        // F3: 描邊換算超出 SDF 上限時收集警告，由呼叫端在 Generate 結束時集中顯示。
        public readonly List<string> OutlineOverflowWarnings = new List<string>();

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
            var fontAssetForSdf = defaultFontAsset != null ? defaultFontAsset : TMP_Settings.defaultFontAsset;
            var targetWidth = ConvertPhotoshopStrokeWidth(node, fontAssetForSdf);
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

        // F2 + F3: 把 PS 描邊像素換算為 TMP _OutlineWidth (0..1)
        // 公式推導：
        //   outlinePixels = _OutlineWidth × gradientScale × (fontSize / samplingPointSize)
        //   其中 gradientScale = atlasPadding + 1（TMP 的 SDF 有效擴張範圍）
        //   反解 _OutlineWidth = strokePx × samplingPointSize / gradientScale / fontSize
        //
        // 舊版寫死 sdfRatio = 5（假設 SamplingPointSize=25 / Padding=5），換字型就會錯。
        // 改為從 Font Asset 動態讀取 samplingPointSize 與 atlasPadding，並把分母從
        // atlasPadding 改為 atlasPadding + 1，修正之前系統性偏厚的問題。
        //
        // 上限超過 1.0 時不再無聲 clamp，會記錄到 OutlineOverflowWarnings，讓呼叫端集中提示。
        private float ConvertPhotoshopStrokeWidth(PhotoshopUiNode node, TMP_FontAsset fontAsset)
        {
            var strokeWidthPixels = node.outlineWidth;
            var referenceSize = node.fontSize > 0f ? node.fontSize : 40f;

            // 從 Font Asset 動態讀取，讀不到才退回保守預設值（25 / 5）。
            float samplingPointSize = 25f;
            float atlasPadding = 5f;
            if (fontAsset != null)
            {
                samplingPointSize = fontAsset.faceInfo.pointSize > 0f
                    ? fontAsset.faceInfo.pointSize
                    : samplingPointSize;
                atlasPadding = fontAsset.atlasPadding > 0
                    ? fontAsset.atlasPadding
                    : atlasPadding;
            }

            var gradientScale = atlasPadding + 1f;
            var ratio = samplingPointSize / gradientScale; // 等價於舊 sdfRatio
            var rawWidth = strokeWidthPixels * ratio / referenceSize;

            // F3：超出 SDF 物理上限時記錄警告，並提示重建 Font Asset 所需的 padding。
            if (rawWidth > 1.0f)
            {
                var maxPixelsAtCurrentPadding = referenceSize / ratio;
                var requiredPadding = Mathf.CeilToInt(
                    strokeWidthPixels * samplingPointSize / referenceSize) - 1;
                var nodeName = string.IsNullOrWhiteSpace(node.name) ? "(unnamed)" : node.name;
                var fontName = fontAsset != null ? fontAsset.name : "(no font asset)";
                OutlineOverflowWarnings.Add(
                    $"「{nodeName}」描邊 {strokeWidthPixels:0.##}px @ 字級 {referenceSize:0.##} 已超出 SDF 上限（{maxPixelsAtCurrentPadding:0.##}px，字型 {fontName} atlasPadding={atlasPadding:0}）。" +
                    $"建議重建該字型 Font Asset，atlasPadding ≥ {requiredPadding}。");
            }

            return Mathf.Clamp(rawWidth, 0.01f, 1.0f);
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

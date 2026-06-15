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
        private readonly float outlineThicknessMultiplier;

        // F3: 描邊換算超出 SDF 上限時收集警告，由呼叫端在 Generate 結束時集中顯示。
        public readonly List<string> OutlineOverflowWarnings = new List<string>();

        public TmpMapper(
            TMP_FontAsset defaultFontAsset,
            Material defaultMaterialPreset,
            string generatedMaterialFolder = null,
            string materialLibraryFolder = null,
            float outlineThicknessMultiplier = 1.0f)
        {
            this.defaultFontAsset = defaultFontAsset;
            this.defaultMaterialPreset = defaultMaterialPreset;
            this.generatedMaterialFolder = string.IsNullOrWhiteSpace(generatedMaterialFolder)
                ? "Assets/GeneratedMaterials"
                : generatedMaterialFolder;
            this.materialLibraryFolder = materialLibraryFolder;
            this.outlineThicknessMultiplier = Mathf.Clamp(outlineThicknessMultiplier, 0.3f, 1.5f);
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
            // v2.7.2：PS Point Text 圖層 bbox 通常比實際字形寬，且 PS 視覺上字形是「居中」於 bbox。
            // 原 fallback = Left 會讓沒帶 alignment 欄位的短文字（按鈕標題等）貼在 bbox 左邊緣，
            // 與旁邊獨立的 icon 圖層合起來會像「文字 + 空隙 + icon」造成順序顛倒的錯覺。
            // 改成 Center 預設更貼近 PS 視覺；exporter 有寫 alignment 時仍以該值為準。
            target.alignment = ParseAlignment(node.alignment, TextAlignmentOptions.Center);

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

            // PS 端 materialToken 不含字級（如 outline_ffffff_8），跨字級會撞名、
            // 各字級共用同一顆材質導致描邊比例全錯，一律改用含字級的 BuildOutlineToken
            var token = BuildOutlineToken(node);
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

            // 實測（校正板 bbox 量測）：TMP outline 以字緣為中心向內外各擴 targetWidth，
            // 內側會吃掉字面；PS 描邊是「外部」語意（字面不動、全部朝外）。
            // 把字面同步外推 targetWidth（= strokePx 換算值的一半），描邊內緣剛好
            // 落回原字緣 → 字面不變、描邊全朝外，與 PS 一致。
            if (material.HasProperty(ShaderUtilities.ID_FaceDilate))
            {
                material.SetFloat(ShaderUtilities.ID_FaceDilate, targetWidth);
            }

            // 注意：不要嘗試設定 _ScaleRatioA — TMP 的 ShaderUtilities.GetPadding 會在
            // 材質被文字使用時自動呼叫 UpdateShaderRatios 重算覆寫（實測 ratioA =
            // (gradientScale-1)/gradientScale，已納入 ConvertPhotoshopStrokeWidth 公式）。

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

            // 實測模型（校正板 bbox 量測，三組數據吻合）：
            //   外擴 px = (_FaceDilate + _OutlineWidth) × ratioA × gradientScale × fontSize / sampling
            //   其中 TMP 自動維護 ratioA = (gradientScale − 1) / gradientScale（UpdateShaderRatios），
            //   gradientScale = atlasPadding + 1，故 ratioA × gradientScale 化簡為 atlasPadding。
            // 設 _FaceDilate = _OutlineWidth = W：字面外推 W、描邊內緣落回原字緣，
            // 重現 PS「外部描邊」語意（字面不縮、N px 全朝外）。
            //   N = 2W × atlasPadding × fontSize / sampling
            //   → W = N × sampling / (2 × atlasPadding × fontSize)
            var ratio = samplingPointSize / atlasPadding;
            var rawWidth = strokeWidthPixels * ratio / referenceSize; // 外擴 N px 所需的 2W 總量
            var halfWidth = rawWidth * 0.5f;

            // F3：超出 SDF 物理上限時記錄警告，並提示重建 Font Asset 所需的 padding。
            if (rawWidth > 1.0f)
            {
                var maxPixelsAtCurrentPadding = referenceSize / ratio;
                var requiredPadding = Mathf.CeilToInt(
                    strokeWidthPixels * samplingPointSize / referenceSize);
                var nodeName = string.IsNullOrWhiteSpace(node.name) ? "(unnamed)" : node.name;
                var fontName = fontAsset != null ? fontAsset.name : "(no font asset)";
                OutlineOverflowWarnings.Add(
                    $"「{nodeName}」描邊 {strokeWidthPixels:0.##}px @ 字級 {referenceSize:0.##} 已超出 SDF 上限（{maxPixelsAtCurrentPadding:0.##}px，字型 {fontName} atlasPadding={atlasPadding:0}）。" +
                    $"建議重建該字型 Font Asset，atlasPadding ≥ {requiredPadding}。");
            }

            // 補償係數：SDF 描邊邊緣是半透明 falloff（非硬邊），即使物理寬度與 PS 相同，
            // 視覺重心會比 PS 略寬。Window 提供 0.3~1.5 的滑桿讓使用者用校準板回推合適值。
            // 預設 1.0（不補償，維持物理寬度等於 PS）。
            var compensated = halfWidth * outlineThicknessMultiplier;

            // 上限 0.5：dilate(0.5) + outline 外擴(0.5) = 1.0 恰為 SDF 物理極限
            return Mathf.Clamp(compensated, 0.005f, 0.5f);
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
            // F5：Phase 1 把描邊換算系統性偏厚的 20% 修掉後，材質庫比對放寬到舊容差會把
            // 不夠像的材質誤認成「夠近」。改為硬性 10% 門檻，超過直接淘汰，讓上層改走
            // 「新增材質球」路徑而非借用視覺差異明顯的舊材質。
            const float maxWidthDiffRatio = 0.10f;
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

                // F5：超過 10% 直接淘汰，避免借用視覺差異明顯的舊材質
                if (widthDiff > maxWidthDiffRatio)
                    continue;

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
            // _OutlineWidth 是「相對字級」的比值：同樣 2px 描邊，24pt 與 72pt 需要的
            // _OutlineWidth 差三倍，因此產生的材質球必須以「顏色+描邊px+字級」為單位，
            // 不能跨字級共用（否則後處理的字級會覆蓋先處理的，造成各字級粗細全錯）。
            var sizeKey = Mathf.RoundToInt(node.fontSize > 0f ? node.fontSize : 40f);
            return $"outline_{color}_{Mathf.RoundToInt(node.outlineWidth)}_{sizeKey}";
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

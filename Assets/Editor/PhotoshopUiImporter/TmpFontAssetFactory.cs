using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace PhotoshopToUnity.EditorImporter
{
    // OPTIMIZATION_PLAN_zh.html#phase5-q4：字型資產工廠（共用 service，兩個入口：
    // Font Replacer 的「一鍵建立」與 Importer 的「掃描 Package 字型」）。
    // 職責：專案內字型檔掃描配對、Dynamic SDF Font Asset 建立（含子資產持久化）、
    // 材質克隆-換底、TmpFontMap 自動登記。
    // 授權紅線：只掃專案 Assets 內的 .ttf/.otf，不碰系統字型資料夾。
    public static class TmpFontAssetFactory
    {
        public const string DefaultOutputFolder = "Assets/Fonts/Generated";

        // ── 專案字型檔掃描與 fontToken 配對 ─────────────────────────────

        public sealed class FontFileCandidate
        {
            public Font font;
            public string assetPath;
            public string matchedName;   // 命中的名稱（asset 名或 font 內含名）
        }

        // 在專案 Assets 內尋找與 fontToken 最匹配的字型檔。
        // 比對語意鏡像 TmpFontMap：兩邊 slug 化（小寫英數）後互相 Contains，取最長命中。
        public static FontFileCandidate FindProjectFontFile(string fontToken)
        {
            var tokenSlug = NormalizeSlug(fontToken);
            if (tokenSlug.Length == 0)
            {
                return null;
            }

            FontFileCandidate best = null;
            var bestScore = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Font"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension != ".ttf" && extension != ".otf")
                {
                    continue;
                }

                var font = AssetDatabase.LoadAssetAtPath<Font>(path);
                if (font == null)
                {
                    continue;
                }

                // 候選名：檔名 + 字型內含名（family 等）
                var names = new List<string> { Path.GetFileNameWithoutExtension(path), font.name };
                try
                {
                    if (font.fontNames != null)
                    {
                        names.AddRange(font.fontNames);
                    }
                }
                catch (Exception)
                {
                }

                foreach (var name in names)
                {
                    var nameSlug = NormalizeSlug(name);
                    if (nameSlug.Length == 0)
                    {
                        continue;
                    }

                    int score;
                    if (nameSlug == tokenSlug)
                    {
                        score = int.MaxValue; // 完全相等直接勝出
                    }
                    else if (tokenSlug.Contains(nameSlug) || nameSlug.Contains(tokenSlug))
                    {
                        score = Math.Min(nameSlug.Length, tokenSlug.Length); // 命中越長越可信
                    }
                    else
                    {
                        continue;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new FontFileCandidate { font = font, assetPath = path, matchedName = name };
                    }
                }
            }

            return best;
        }

        // ── Dynamic SDF Font Asset 建立（含子資產持久化）─────────────────

        // OPTIMIZATION_PLAN_zh.html#phase5-q4：一律建 Dynamic SDF（免字集檔、CJK 直接可用）。
        // sampling/padding 抄 template（替換流程 = 被替換的來源 Font Asset；匯入流程 = 預設字型）
        // → SDF 參數空間相同，材質參數可 1:1 複製零換算。
        // 已知風險（Q7d）：atlas texture / material 必須 AddObjectToAsset 掛成子資產，
        // 否則 domain reload 後斷連結。失敗時回 null + errorMessage，由呼叫端引導人工走
        // Font Asset Creator（退守方案）。
        public static TMP_FontAsset CreateDynamicFontAsset(
            Font sourceFontFile,
            TMP_FontAsset parameterTemplate,
            string outputFolder,
            out string errorMessage)
        {
            errorMessage = null;

            if (sourceFontFile == null)
            {
                errorMessage = "未指定字型檔。";
                return null;
            }

            var samplingPointSize = 90;
            var atlasPadding = 9;
            var atlasWidth = 1024;
            var atlasHeight = 1024;
            var renderMode = GlyphRenderMode.SDFAA;

            if (parameterTemplate != null)
            {
                if (parameterTemplate.faceInfo.pointSize > 0)
                {
                    samplingPointSize = Mathf.RoundToInt(parameterTemplate.faceInfo.pointSize);
                }
                if (parameterTemplate.atlasPadding > 0)
                {
                    atlasPadding = parameterTemplate.atlasPadding;
                }
                if (parameterTemplate.atlasWidth > 0)
                {
                    atlasWidth = parameterTemplate.atlasWidth;
                }
                if (parameterTemplate.atlasHeight > 0)
                {
                    atlasHeight = parameterTemplate.atlasHeight;
                }
                renderMode = parameterTemplate.atlasRenderMode;
            }

            try
            {
                var folder = string.IsNullOrWhiteSpace(outputFolder) ? DefaultOutputFolder : outputFolder.TrimEnd('/');
                Directory.CreateDirectory(PathUtility.ToAbsolutePath(folder));
                AssetDatabase.Refresh();

                var assetName = $"{sourceFontFile.name} SDF";
                var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{MakeSafeFileName(assetName)}.asset");

                var fontAsset = TMP_FontAsset.CreateFontAsset(
                    sourceFontFile, samplingPointSize, atlasPadding, renderMode,
                    atlasWidth, atlasHeight, AtlasPopulationMode.Dynamic, true);
                if (fontAsset == null)
                {
                    errorMessage = "TMP_FontAsset.CreateFontAsset 回傳 null（字型檔可能無法解析）。";
                    return null;
                }

                fontAsset.name = assetName;
                AssetDatabase.CreateAsset(fontAsset, assetPath);

                // 子資產持久化——與 TMP 官方建立選單相同的模式。
                if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0 && fontAsset.atlasTextures[0] != null)
                {
                    fontAsset.atlasTextures[0].name = $"{assetName} Atlas";
                    AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
                }
                if (fontAsset.material != null)
                {
                    fontAsset.material.name = $"{assetName} Material";
                    AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(assetPath);

                var reloaded = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
                if (reloaded == null || reloaded.material == null || reloaded.atlasTexture == null)
                {
                    errorMessage = $"Font Asset 已建立但子資產驗證失敗（{assetPath}）。請改用 Window > TextMeshPro > Font Asset Creator 手動建立。";
                    return reloaded;
                }

                return reloaded;
            }
            catch (Exception exception)
            {
                errorMessage = $"建立 Font Asset 失敗：{exception.Message}。退守方案：Window > TextMeshPro > Font Asset Creator 手動建立（Sampling={samplingPointSize}、Padding={atlasPadding}、Atlas={atlasWidth}x{atlasHeight}、Dynamic）。";
                return null;
            }
        }

        // ── 材質克隆-換底 ────────────────────────────────────────────────

        // OPTIMIZATION_PLAN_zh.html#phase5-q1：以來源材質為底整份複製（保留 outline/underlay/
        // 漸變等全部視覺參數與 shader keywords），再把 atlas 相關屬性換到目標字型，
        // SDF 空間參數依 sampling/padding 差異重縮放（兩邊相同時 factor=1，即 1:1）。
        public static Material CloneMaterialForFont(
            Material sourceMaterial,
            TMP_FontAsset sourceFont,
            TMP_FontAsset targetFont,
            string outputFolder,
            out string errorMessage)
        {
            errorMessage = null;

            if (sourceMaterial == null || targetFont == null || targetFont.material == null)
            {
                errorMessage = "來源材質或目標字型不完整。";
                return null;
            }

            // 來源材質 == 來源字型預設材質 → 直接用目標字型預設材質，不生新資產。
            if (sourceFont != null && sourceMaterial == sourceFont.material)
            {
                return targetFont.material;
            }

            try
            {
                var folder = string.IsNullOrWhiteSpace(outputFolder) ? DefaultOutputFolder : outputFolder.TrimEnd('/');
                Directory.CreateDirectory(PathUtility.ToAbsolutePath(folder));

                var materialName = BuildCloneMaterialName(sourceMaterial, sourceFont, targetFont);
                var materialPath = $"{folder}/{MakeSafeFileName(materialName)}.mat";

                // 同名已生成過 → 直接重用（批次替換多顆 Prefab 共用同一顆克隆材質）。
                var existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (existing != null)
                {
                    return existing;
                }

                var material = new Material(sourceMaterial) { name = materialName };

                // 換底：atlas 相關屬性指向目標字型。
                if (material.HasProperty(ShaderUtilities.ID_MainTex))
                {
                    material.SetTexture(ShaderUtilities.ID_MainTex, targetFont.atlasTexture);
                }
                if (material.HasProperty(ShaderUtilities.ID_TextureWidth))
                {
                    material.SetFloat(ShaderUtilities.ID_TextureWidth, targetFont.atlasWidth);
                }
                if (material.HasProperty(ShaderUtilities.ID_TextureHeight))
                {
                    material.SetFloat(ShaderUtilities.ID_TextureHeight, targetFont.atlasHeight);
                }
                if (material.HasProperty(ShaderUtilities.ID_GradientScale))
                {
                    material.SetFloat(ShaderUtilities.ID_GradientScale, targetFont.atlasPadding + 1);
                }
                if (material.HasProperty(ShaderUtilities.ID_WeightNormal))
                {
                    material.SetFloat(ShaderUtilities.ID_WeightNormal, targetFont.normalStyle);
                }
                if (material.HasProperty(ShaderUtilities.ID_WeightBold))
                {
                    material.SetFloat(ShaderUtilities.ID_WeightBold, targetFont.boldStyle);
                }

                // SDF 空間重縮放：外擴 px ≈ W × atlasPadding × fontSize / sampling（TmpMapper 實測模型）。
                // 同 fontSize 下維持視覺 px 等值 → factor = (srcPadding/dstPadding) × (dstSampling/srcSampling)。
                // sampling/padding 抄來源建立（Q4b）時 factor = 1，等於零換算。
                var factor = ComputeSdfRescaleFactor(sourceFont, targetFont);
                if (!Mathf.Approximately(factor, 1f))
                {
                    RescaleSdfProperty(material, ShaderUtilities.ID_FaceDilate, factor);
                    RescaleSdfProperty(material, ShaderUtilities.ID_OutlineWidth, factor);
                    RescaleSdfProperty(material, ShaderUtilities.ID_OutlineSoftness, factor);
                    RescaleSdfProperty(material, ShaderUtilities.ID_UnderlayOffsetX, factor);
                    RescaleSdfProperty(material, ShaderUtilities.ID_UnderlayOffsetY, factor);
                    RescaleSdfProperty(material, ShaderUtilities.ID_UnderlayDilate, factor);
                    RescaleSdfProperty(material, ShaderUtilities.ID_UnderlaySoftness, factor);
                }

                AssetDatabase.CreateAsset(material, materialPath);
                AssetDatabase.SaveAssets();
                return material;
            }
            catch (Exception exception)
            {
                errorMessage = $"克隆材質失敗：{exception.Message}";
                return null;
            }
        }

        private static float ComputeSdfRescaleFactor(TMP_FontAsset sourceFont, TMP_FontAsset targetFont)
        {
            if (sourceFont == null || targetFont == null)
            {
                return 1f;
            }

            float sourcePadding = sourceFont.atlasPadding > 0 ? sourceFont.atlasPadding : 5f;
            float targetPadding = targetFont.atlasPadding > 0 ? targetFont.atlasPadding : 5f;
            float sourceSampling = sourceFont.faceInfo.pointSize > 0 ? sourceFont.faceInfo.pointSize : 25f;
            float targetSampling = targetFont.faceInfo.pointSize > 0 ? targetFont.faceInfo.pointSize : 25f;

            return (sourcePadding / targetPadding) * (targetSampling / sourceSampling);
        }

        private static void RescaleSdfProperty(Material material, int propertyId, float factor)
        {
            if (!material.HasProperty(propertyId))
            {
                return;
            }

            material.SetFloat(propertyId, Mathf.Clamp(material.GetFloat(propertyId) * factor, -1f, 1f));
        }

        private static string BuildCloneMaterialName(Material sourceMaterial, TMP_FontAsset sourceFont, TMP_FontAsset targetFont)
        {
            var sourceName = sourceMaterial.name;
            if (sourceFont != null && !string.IsNullOrEmpty(sourceFont.name) && sourceName.Contains(sourceFont.name))
            {
                // 「GenSen SDF_outline_ffffff_8_40」→「MiSans SDF_outline_ffffff_8_40」
                return sourceName.Replace(sourceFont.name, targetFont.name);
            }
            return $"{targetFont.name}_{sourceName}";
        }

        // ── TmpFontMap 自動登記 ─────────────────────────────────────────

        // OPTIMIZATION_PLAN_zh.html#phase5-q4：建完自動寫一筆（keyword = fontToken slug），
        // 下次匯入直接生效。已有同 keyword 項目則更新其 fontAsset，不重複追加。
        public static bool RegisterInFontMap(TmpFontMap fontMap, string fontToken, TMP_FontAsset fontAsset)
        {
            if (fontMap == null || fontAsset == null || string.IsNullOrWhiteSpace(fontToken))
            {
                return false;
            }

            var tokenSlug = NormalizeSlug(fontToken);
            if (tokenSlug.Length == 0)
            {
                return false;
            }

            foreach (var entry in fontMap.entries)
            {
                if (entry != null && NormalizeSlug(entry.fontKeyword) == tokenSlug)
                {
                    entry.fontAsset = fontAsset;
                    EditorUtility.SetDirty(fontMap);
                    AssetDatabase.SaveAssets();
                    return true;
                }
            }

            fontMap.entries.Add(new TmpFontMapEntry
            {
                fontKeyword = fontToken,
                fontAsset = fontAsset,
                materialPreset = null
            });
            EditorUtility.SetDirty(fontMap);
            AssetDatabase.SaveAssets();
            return true;
        }

        // 鏡像 TmpFontMap.NormalizeSlug 的比對語意（只留 a-z0-9）。
        public static string NormalizeSlug(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var character in value.ToLowerInvariant())
            {
                if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }

        private static string MakeSafeFileName(string value)
        {
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidChar, '_');
            }

            return string.IsNullOrWhiteSpace(value) ? "font_asset" : value;
        }
    }
}

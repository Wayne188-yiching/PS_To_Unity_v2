using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    public sealed class ImageImportResult
    {
        public readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        public readonly List<string> errors = new List<string>();
        public readonly List<string> missingSourceImages = new List<string>();
        public readonly List<string> warnings = new List<string>();
        // v2.8.1 像素內容去重統計（解碼後 raw RGBA 相同的 PNG 合併到同一個 sprite）
        public int dedupedSpriteCount;
        public long dedupedSpriteBytes;
        // 本次匯入由工具「複製」進 Assets 的檔案（來源本來就在 Assets 內、直接沿用的不算）。
        // 只有這些檔案可以在去重後安全刪除——來源 Package 仍保有完整檔案，重複匯入不受影響。
        public readonly HashSet<string> copiedAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 來源即目的地（PS 直接輸出到 Assets）時無法刪除的冗餘別名，交由呼叫端提示使用者。
        public readonly List<string> redundantSourceImages = new List<string>();
        // v2.16 自動九宮格：每張圖的決定與量測證據（key = normalized imagePath）。
        public readonly Dictionary<string, AutoSliceRecord> autoSlices =
            new Dictionary<string, AutoSliceRecord>(StringComparer.OrdinalIgnoreCase);

        public bool IsValid => errors.Count == 0;
    }

    public sealed class AutoSliceRecord
    {
        public string imagePath;
        public string spriteAssetPath;
        // applied：已切並設 border｜proposal：量得到但不動（既有 Sprite，可能被別的 Prefab 以 Simple 引用）
        // keptManual：美術手設或手改過 border，工具不碰｜cleared：工具上次切過、這次新圖不再可切，已還原
        public string decision;
        public string reason;
        public int originalWidth;
        public int originalHeight;
        public int slicedWidth;
        public int slicedHeight;
        public Vector4 border;              // L, B, R, T（Unity spriteBorder 順序）
        public long savedPixels;
        public int reconstructionMaxDiff = -1;
    }

    public static class ImageImportService
    {
        private const int FileOperationRetryCount = 8;
        private const int FileOperationRetryDelayMs = 120;

        public static ImageImportResult ImportImages(PhotoshopUiLayout layout, string sourceRoot, string importFolder, bool autoNineSlice = true)
        {
            var result = new ImageImportResult();

            if (layout == null)
            {
                result.errors.Add("無法匯入圖片：layout 為空。");
                return result;
            }

            if (!PathUtility.IsAssetPath(importFolder))
            {
                result.errors.Add("Unity 匯入資料夾必須位於 Assets 之下。");
                return result;
            }

            var imageNodes = new Dictionary<string, PhotoshopUiNode>(StringComparer.OrdinalIgnoreCase);
            var maskImagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectImageNodes(layout.nodes, imageNodes, maskImagePaths);

            if (imageNodes.Count == 0)
            {
                return result;
            }

            var sourceRootPath = PathUtility.IsAssetPath(sourceRoot) ? PathUtility.ToAbsolutePath(sourceRoot) : sourceRoot;
            if (string.IsNullOrWhiteSpace(sourceRootPath) || !Directory.Exists(sourceRootPath))
            {
                result.errors.Add("找不到圖片來源資料夾。");
                return result;
            }

            Directory.CreateDirectory(PathUtility.ToAbsolutePath(importFolder));

            foreach (var pair in imageNodes)
            {
                ImportOneImage(sourceRootPath, importFolder, pair.Key, pair.Value, result,
                    autoNineSlice && !maskImagePaths.Contains(pair.Key));
            }

            AssetDatabase.Refresh();

            // v2.8.1：在所有 PNG 匯入完成後，對 raw RGBA 算 MD5 去重。
            // 補強 v2.8.0 JSX 端 FNV PNG-bytes hash 的盲點——PS ExportOptionsSaveForWeb
            // 對相同像素產生的 PNG bytes 並不穩定（內嵌資訊與壓縮策略），bytes hash 抓不到，
            // 必須解碼成 raw RGBA 再 hash 才能識別「視覺相同」。
            DedupSpritesByPixelContent(result);

            return result;
        }

        private static void DedupSpritesByPixelContent(ImageImportResult result)
        {
            if (result.sprites.Count == 0)
            {
                return;
            }

            // 第一輪：對 sprites 字典裡每一個唯一 asset 算 pixel hash
            var hashToCanonical = new Dictionary<string, string>(StringComparer.Ordinal);
            var pathToCanonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var sprite in result.sprites.Values)
            {
                if (sprite == null)
                {
                    continue;
                }

                var assetPath = AssetDatabase.GetAssetPath(sprite);
                if (string.IsNullOrEmpty(assetPath) || !processedPaths.Add(assetPath))
                {
                    continue;
                }

                var pixelHash = ComputePixelHash(assetPath);
                if (pixelHash == null)
                {
                    continue;
                }

                if (hashToCanonical.TryGetValue(pixelHash, out var canonical))
                {
                    pathToCanonical[assetPath] = canonical;
                }
                else
                {
                    hashToCanonical[pixelHash] = assetPath;
                }
            }

            if (pathToCanonical.Count == 0)
            {
                return;
            }

            // 第二輪：把 sprites 字典內所有指到重複 asset 的 entry 重指到 canonical sprite。
            // 原始 PNG 保留，Atlas 只接收 canonical Sprite，確保可重複匯入。
            var dedupedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var canonicalSpriteCache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            var spriteKeys = new List<string>(result.sprites.Keys);

            foreach (var key in spriteKeys)
            {
                var sprite = result.sprites[key];
                if (sprite == null)
                {
                    continue;
                }

                var assetPath = AssetDatabase.GetAssetPath(sprite);
                if (!pathToCanonical.TryGetValue(assetPath, out var canonicalPath))
                {
                    continue;
                }

                if (!canonicalSpriteCache.TryGetValue(canonicalPath, out var canonicalSprite))
                {
                    canonicalSprite = AssetDatabase.LoadAssetAtPath<Sprite>(canonicalPath);
                    canonicalSpriteCache[canonicalPath] = canonicalSprite;
                }

                if (canonicalSprite == null)
                {
                    continue;
                }

                result.sprites[key] = canonicalSprite;

                if (dedupedPaths.Add(assetPath))
                {
                    var absPath = PathUtility.ToAbsolutePath(assetPath);
                    try
                    {
                        var fileLength = File.Exists(absPath) ? new FileInfo(absPath).Length : 0L;
                        if (!File.Exists(absPath))
                        {
                            continue;
                        }

                        result.dedupedSpriteCount++;
                        result.dedupedSpriteBytes += fileLength;

                        // Atlas 的 packables 指整個語系資料夾（[Client] 包圖規範），所以留在
                        // 資料夾裡的別名 PNG 一定會被打進圖集。只刪本次由工具複製進來的檔案：
                        // 來源 Package 仍保有原檔，同一份 layout JSON 重複匯入照樣找得到。
                        if (result.copiedAssetPaths.Contains(PathUtility.NormalizeAssetKey(assetPath)))
                        {
                            AssetDatabase.DeleteAsset(assetPath);
                        }
                        else
                        {
                            // 來源即目的地（PS 直接輸出到 Assets）：刪了會破壞來源，只能回報。
                            result.redundantSourceImages.Add(assetPath);
                        }
                    }
                    catch
                    {
                        // 刪不掉就跳過，sprite 已經重指，留檔最多讓圖集多佔點空間，不影響行為
                    }
                }
            }

            if (result.dedupedSpriteCount > 0)
            {
                AssetDatabase.Refresh();
            }
        }

        private static string ComputePixelHash(string assetPath)
        {
            return ComputePixelHashFromFile(PathUtility.ToAbsolutePath(assetPath));
        }

        internal static string ComputePixelHashFromFile(string absolutePath)
        {
            Texture2D tex = null;
            try
            {
                if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
                {
                    return null;
                }

                var bytes = File.ReadAllBytes(absolutePath);
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes, markNonReadable: false))
                {
                    return null;
                }

                var raw = tex.GetRawTextureData();
                if (raw == null || raw.Length == 0)
                {
                    return null;
                }

                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    var hash = md5.ComputeHash(raw);
                    // 維度納入 hash key，避免相同 byte 長度但不同維度誤判
                    return tex.width.ToString() + "x" + tex.height.ToString() + "_" + BitConverter.ToString(hash).Replace("-", string.Empty);
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                if (tex != null)
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                }
            }
        }

        private static void CollectImageNodes(
            List<PhotoshopUiNode> nodes,
            Dictionary<string, PhotoshopUiNode> imageNodes,
            HashSet<string> maskImagePaths)
        {
            if (nodes == null)
            {
                return;
            }

            foreach (var node in nodes)
            {
                if (node == null || !node.visible)
                {
                    continue;
                }

                if (node.NormalizedType == "image")
                {
                    var key = PathUtility.NormalizeAssetKey(node.imagePath);
                    if (!string.IsNullOrEmpty(key))
                    {
                        // [MASK] 的遮罩圖固定以 Simple 裁切，切成九宮格會讓遮罩形狀跑掉。
                        if (node.IsMaskShape)
                        {
                            maskImagePaths.Add(key);
                        }

                        if (!imageNodes.TryGetValue(key, out var existing) ||
                            (!existing.RequestsSlicedImage && node.RequestsSlicedImage))
                        {
                            imageNodes[key] = node;
                        }
                    }
                }

                CollectImageNodes(node.children, imageNodes, maskImagePaths);
            }
        }

        private static void ImportOneImage(
            string sourceRoot,
            string importFolder,
            string imagePath,
            PhotoshopUiNode node,
            ImageImportResult result,
            bool autoNineSlice)
        {
            if (Path.IsPathRooted(imagePath))
            {
                result.errors.Add($"圖片路徑必須是相對路徑：{imagePath}");
                return;
            }

            if (imagePath.Contains("../") || imagePath.Contains("..\\"))
            {
                result.errors.Add($"圖片路徑不可包含上一層目錄：{imagePath}");
                return;
            }

            var sourcePath = ResolveSourceImagePath(sourceRoot, imagePath);

            if (!File.Exists(sourcePath))
            {
                result.errors.Add($"找不到圖片：{Path.Combine(sourceRoot, imagePath.Replace('/', Path.DirectorySeparatorChar))}。已搜尋 Atlas/SpriteAtlas/Base、CHS、CHT、EN 與來源資料夾子目錄。");
                return;
            }

            var sourceAssetPath = PathUtility.ToProjectRelativeAssetPath(sourcePath);
            var destinationAssetPath = PathUtility.IsAssetPath(sourceAssetPath)
                ? sourceAssetPath
                : $"{PathUtility.NormalizeAssetKey(importFolder).TrimEnd('/')}/{imagePath}";
            var destinationFullPath = PathUtility.ToAbsolutePath(destinationAssetPath);
            var destinationDirectory = Path.GetDirectoryName(destinationFullPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            var sameFileAsSource = IsSameFilePath(sourcePath, destinationFullPath);
            var destinationExisted = File.Exists(destinationFullPath);
            if (!sameFileAsSource)
            {
                if (!TryCopyFile(sourcePath, destinationFullPath, result))
                {
                    return;
                }

                result.copiedAssetPaths.Add(PathUtility.NormalizeAssetKey(destinationAssetPath));
            }

            AssetDatabase.ImportAsset(destinationAssetPath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(destinationAssetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.isReadable = false;
                importer.GetSourceTextureWidthAndHeight(out var sourceWidth, out var sourceHeight);
                importer.maxTextureSize = Mathf.Clamp(
                    Mathf.NextPowerOfTwo(Mathf.Max(sourceWidth, sourceHeight)),
                    2048,
                    8192);
                importer.textureCompression = TextureImporterCompression.Uncompressed;

                // Explicit Photoshop metadata owns the border. When no [SLICED] tag is
                // present, leave an artist-authored Sprite Editor border untouched.
                if (node != null && node.RequestsSlicedImage)
                {
                    var requestedBorder = node.SpriteBorder;
                    var appliedBorder = ClampSpriteBorder(importer, requestedBorder);
                    importer.spriteBorder = appliedBorder;
                    if ((requestedBorder - appliedBorder).sqrMagnitude > 0.0001f)
                    {
                        result.warnings.Add(
                            $"SPRITE_BORDER_CLAMPED：{imagePath} requested={FormatBorder(requestedBorder)} applied={FormatBorder(appliedBorder)}");
                    }
                }
                else if (autoNineSlice && !sameFileAsSource)
                {
                    // 來源即目的地（PS 直接輸出到 Assets）時不改寫：那是使用者的匯出檔，不是工具的副本。
                    ApplyAutoNineSlice(importer, destinationAssetPath, destinationFullPath,
                        PathUtility.NormalizeAssetKey(imagePath), destinationExisted, result);
                }

                importer.SaveAndReimport();
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(destinationAssetPath);
            if (sprite == null)
            {
                result.errors.Add($"圖片已匯入但無法載入為 Sprite：{destinationAssetPath}");
                return;
            }

            result.sprites[PathUtility.NormalizeAssetKey(imagePath)] = sprite;
        }

        private static Vector4 ClampSpriteBorder(TextureImporter importer, Vector4 border)
        {
            importer.GetSourceTextureWidthAndHeight(out var width, out var height);
            border.x = Mathf.Max(0f, border.x);
            border.y = Mathf.Max(0f, border.y);
            border.z = Mathf.Max(0f, border.z);
            border.w = Mathf.Max(0f, border.w);
            ClampBorderPair(ref border.x, ref border.z, width);
            ClampBorderPair(ref border.y, ref border.w, height);
            return border;
        }

        private static string FormatBorder(Vector4 border)
        {
            return $"L{border.x:0.###},B{border.y:0.###},R{border.z:0.###},T{border.w:0.###}";
        }

        private static void ClampBorderPair(ref float first, ref float second, int availablePixels)
        {
            var available = Mathf.Max(0f, availablePixels);
            var total = first + second;
            if (total <= available || total <= 0f)
            {
                return;
            }

            var scale = available / total;
            first *= scale;
            second *= scale;
        }

        private static string ResolveSourceImagePath(string sourceRoot, string imagePath)
        {
            var normalizedImagePath = imagePath.Replace('/', Path.DirectorySeparatorChar);
            var directPath = Path.Combine(sourceRoot, normalizedImagePath);
            if (File.Exists(directPath))
            {
                return directPath;
            }

            var fileName = Path.GetFileName(normalizedImagePath);
            var knownFolders = new[]
            {
                Path.Combine(sourceRoot, "SpriteAtlas", "Base"),
                Path.Combine(sourceRoot, "SpriteAtlas", "CHS"),
                Path.Combine(sourceRoot, "SpriteAtlas", "CHT"),
                Path.Combine(sourceRoot, "SpriteAtlas", "EN"),
                Path.Combine(sourceRoot, "Base"),
                Path.Combine(sourceRoot, "CHS"),
                Path.Combine(sourceRoot, "CHT"),
                Path.Combine(sourceRoot, "EN")
            };

            foreach (var folder in knownFolders)
            {
                var candidate = Path.Combine(folder, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            try
            {
                foreach (var candidate in Directory.GetFiles(sourceRoot, fileName, SearchOption.AllDirectories))
                {
                    return candidate;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return directPath;
        }

        private static bool IsSameFilePath(string firstPath, string secondPath)
        {
            if (string.IsNullOrWhiteSpace(firstPath) || string.IsNullOrWhiteSpace(secondPath))
            {
                return false;
            }

            var firstFullPath = Path.GetFullPath(firstPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var secondFullPath = Path.GetFullPath(secondPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(firstFullPath, secondFullPath, StringComparison.OrdinalIgnoreCase);
        }

        // v2.16：原地覆寫，保留 .meta。舊版先 DeleteAsset 再複製，每次重新匯入 GUID 都會換掉
        //（其他 Prefab 對這張 Sprite 的參照全部斷掉），美術在 Sprite Editor 設的 border 也一併消失。
        private static bool TryCopyFile(string sourcePath, string destinationFullPath, ImageImportResult result)
        {
            for (var attempt = 1; attempt <= FileOperationRetryCount; attempt++)
            {
                try
                {
                    if (File.Exists(destinationFullPath))
                    {
                        File.SetAttributes(destinationFullPath, FileAttributes.Normal);
                    }
                    File.Copy(sourcePath, destinationFullPath, true);
                    return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(FileOperationRetryDelayMs);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(FileOperationRetryDelayMs);
                }
            }

            result.errors.Add($"圖片複製失敗：{sourcePath} -> {destinationFullPath}。請確認檔案未被其他程式鎖定。");
            return false;
        }

        // ── v2.16 自動九宮格（量測式、無損）─────────────────────────────────────
        // 手工 UI（例：OceanKingRankPanel）把框、底條做成「四角 + 幾 px 中段」的小圖再用 Sliced 拉回原尺寸；
        // 工具原本每個框都匯出整張大圖（Hall_Ranking：55 張圖 0 張 Sliced）。這裡量測每一欄／列：與區段
        // 起點逐像素最大差 <= AutoSliceTolerance 的最長連續區段就是可拉伸的中段，只留 AutoSliceKeepCenter px
        //（兩欄都寫成區段起點那一欄），其餘切掉並設 border。RectTransform 仍用 Photoshop 尺寸，Sliced 把中段拉回，
        // 重建誤差 <= AutoSliceTolerance / 255（實際值記在 reconstructionMaxDiff，Pipeline Validator 據此驗收）。
        // 不猜：只有量到的均勻區段才切；既有、非工具切過的 Sprite 只提案不改（可能被別的 Prefab 以 Simple 引用）。
        internal const int AutoSliceTolerance = 2;
        private const int AutoSliceKeepCenter = 2;
        private const int AutoSliceMinRun = 24;
        private const float AutoSliceMinSaving = 0.25f;
        private const string AutoSliceUserDataKey = "ps2u.autoSlice=";

        private static void ApplyAutoNineSlice(TextureImporter importer, string assetPath, string fullPath,
            string imageKey, bool destinationExisted, ImageImportResult result)
        {
            var record = new AutoSliceRecord { imagePath = imageKey, spriteAssetPath = assetPath };
            var toolBorder = ReadToolBorder(importer.userData);
            var current = importer.spriteBorder;

            if (destinationExisted && toolBorder == null && current.sqrMagnitude > 0f)
            {
                record.decision = "keptManual";
                record.reason = "既有 Sprite 已有手設 border，工具不自動切割。";
                record.border = current;
                result.autoSlices[imageKey] = record;
                return;
            }

            if (destinationExisted && toolBorder != null && (current - toolBorder.Value).sqrMagnitude > 0.01f)
            {
                // 美術改過工具設的 border：border 從此歸美術，工具退出（border 以邊緣為基準，換成整張原圖仍成立）。
                importer.userData = WriteToolBorder(importer.userData, null);
                record.decision = "keptManual";
                record.reason = $"工具上次設 {FormatBorder(toolBorder.Value)}，已被改為 {FormatBorder(current)}；保留手改值，不再自動切割。";
                record.border = current;
                result.autoSlices[imageKey] = record;
                return;
            }

            if (!TryMeasureSlice(File.ReadAllBytes(fullPath), out var slice))
            {
                if (toolBorder != null)
                {
                    importer.spriteBorder = Vector4.zero;
                    importer.userData = WriteToolBorder(importer.userData, null);
                    record.decision = "cleared";
                    record.reason = "新圖已無可拉伸的均勻區段，已移除工具上次設定的 border。";
                    result.autoSlices[imageKey] = record;
                }
                return;
            }

            record.originalWidth = slice.originalWidth;
            record.originalHeight = slice.originalHeight;
            record.slicedWidth = slice.width;
            record.slicedHeight = slice.height;
            record.border = slice.border;
            record.savedPixels = (long)slice.originalWidth * slice.originalHeight - (long)slice.width * slice.height;
            record.reconstructionMaxDiff = slice.reconstructionMaxDiff;

            if (destinationExisted && toolBorder == null)
            {
                record.decision = "proposal";
                record.reason = "既有 Sprite（非本工具切割）可能被其他 Prefab 以 Simple 原尺寸引用，切割會讓它們變形；只提案不改。" +
                                "確認沒有其他引用後，刪除這張圖再重新匯入即可自動套用。";
                result.autoSlices[imageKey] = record;
                return;
            }

            File.WriteAllBytes(fullPath, slice.png);
            importer.spriteBorder = slice.border;
            importer.userData = WriteToolBorder(importer.userData, slice.border);
            record.decision = "applied";
            record.reason = $"{slice.originalWidth}x{slice.originalHeight} -> {slice.width}x{slice.height}，border {FormatBorder(slice.border)}，" +
                            $"重建最大誤差 {slice.reconstructionMaxDiff}/255。";
            result.autoSlices[imageKey] = record;
        }

        internal sealed class SliceResult
        {
            public int originalWidth;
            public int originalHeight;
            public int width;
            public int height;
            public Vector4 border;
            public int reconstructionMaxDiff;
            public byte[] png;
        }

        // Measures the longest uniform column run and row run and, when worth it, returns the compressed PNG.
        // Pixels are Unity's bottom-up rows; border y is bottom and w is top, as TextureImporter expects.
        internal static bool TryMeasureSlice(byte[] pngBytes, out SliceResult slice)
        {
            slice = null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(pngBytes, markNonReadable: false))
                {
                    return false;
                }

                var w = texture.width;
                var h = texture.height;
                var px = texture.GetPixels32();
                LongestRun(px, w, h, true, out var colStart, out var colLength);
                LongestRun(px, w, h, false, out var rowStart, out var rowLength);
                var sliceX = colLength >= AutoSliceMinRun && colLength > AutoSliceKeepCenter;
                var sliceY = rowLength >= AutoSliceMinRun && rowLength > AutoSliceKeepCenter;
                if (!sliceX && !sliceY)
                {
                    return false;
                }

                var newW = sliceX ? w - colLength + AutoSliceKeepCenter : w;
                var newH = sliceY ? h - rowLength + AutoSliceKeepCenter : h;
                var saved = 1f - (float)newW * newH / ((float)w * h);
                if (saved < AutoSliceMinSaving)
                {
                    return false;
                }

                // Map every output column/row to a source one; the kept center repeats the run's first column/row.
                var mapX = new int[newW];
                for (var x = 0; x < newW; x++)
                {
                    mapX[x] = !sliceX || x < colStart ? x
                        : x < colStart + AutoSliceKeepCenter ? colStart
                        : x - AutoSliceKeepCenter + colLength;
                }
                var mapY = new int[newH];
                for (var y = 0; y < newH; y++)
                {
                    mapY[y] = !sliceY || y < rowStart ? y
                        : y < rowStart + AutoSliceKeepCenter ? rowStart
                        : y - AutoSliceKeepCenter + rowLength;
                }

                var output = new Color32[newW * newH];
                for (var y = 0; y < newH; y++)
                {
                    for (var x = 0; x < newW; x++)
                    {
                        output[y * newW + x] = px[mapY[y] * w + mapX[x]];
                    }
                }

                // Reconstruction as Sliced draws it at the original size: center pixels come from the run's first column/row.
                var maxDiff = 0;
                for (var y = 0; y < h; y++)
                {
                    var sy = sliceY && y >= rowStart && y < rowStart + rowLength ? rowStart : y;
                    for (var x = 0; x < w; x++)
                    {
                        var sx = sliceX && x >= colStart && x < colStart + colLength ? colStart : x;
                        maxDiff = Math.Max(maxDiff, MaxChannelDiff(px[y * w + x], px[sy * w + sx]));
                    }
                }
                if (maxDiff > AutoSliceTolerance)
                {
                    return false;
                }

                var outTexture = new Texture2D(newW, newH, TextureFormat.RGBA32, false);
                try
                {
                    outTexture.SetPixels32(output);
                    outTexture.Apply(false, false);
                    slice = new SliceResult
                    {
                        originalWidth = w,
                        originalHeight = h,
                        width = newW,
                        height = newH,
                        border = new Vector4(
                            sliceX ? colStart : 0,
                            sliceY ? rowStart : 0,
                            sliceX ? w - colStart - colLength : 0,
                            sliceY ? h - rowStart - rowLength : 0),
                        reconstructionMaxDiff = maxDiff,
                        png = outTexture.EncodeToPNG(),
                    };
                    return true;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(outTexture);
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        // Greedy scan: a run grows while every column (or row) stays within tolerance of the run's first one,
        // so the whole run can be drawn from that first column without exceeding the tolerance.
        private static void LongestRun(Color32[] px, int w, int h, bool columns, out int bestStart, out int bestLength)
        {
            bestStart = 0;
            bestLength = 0;
            var count = columns ? w : h;
            var start = 0;
            for (var i = 1; i <= count; i++)
            {
                var sameAsStart = i < count && LinesWithinTolerance(px, w, h, columns, start, i);
                if (sameAsStart)
                {
                    continue;
                }
                if (i - start > bestLength)
                {
                    bestLength = i - start;
                    bestStart = start;
                }
                start = i;
            }
        }

        private static bool LinesWithinTolerance(Color32[] px, int w, int h, bool columns, int a, int b)
        {
            if (columns)
            {
                for (var y = 0; y < h; y++)
                {
                    if (MaxChannelDiff(px[y * w + a], px[y * w + b]) > AutoSliceTolerance) return false;
                }
            }
            else
            {
                for (var x = 0; x < w; x++)
                {
                    if (MaxChannelDiff(px[a * w + x], px[b * w + x]) > AutoSliceTolerance) return false;
                }
            }
            return true;
        }

        private static int MaxChannelDiff(Color32 a, Color32 b)
        {
            return Math.Max(Math.Max(Math.Abs(a.r - b.r), Math.Abs(a.g - b.g)), Math.Max(Math.Abs(a.b - b.b), Math.Abs(a.a - b.a)));
        }

        private static Vector4? ReadToolBorder(string userData)
        {
            if (string.IsNullOrEmpty(userData)) return null;
            var index = userData.IndexOf(AutoSliceUserDataKey, StringComparison.Ordinal);
            if (index < 0) return null;
            var start = index + AutoSliceUserDataKey.Length;
            var end = userData.IndexOf(';', start);
            var parts = (end < 0 ? userData.Substring(start) : userData.Substring(start, end - start)).Split(',');
            if (parts.Length != 4) return null;
            var values = new float[4];
            for (var i = 0; i < 4; i++)
            {
                if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out values[i]))
                    return null;
            }
            return new Vector4(values[0], values[1], values[2], values[3]);
        }

        // userData may carry other tools' data; only our "ps2u.autoSlice=L,B,R,T;" token is added or removed.
        private static string WriteToolBorder(string userData, Vector4? border)
        {
            var text = userData ?? string.Empty;
            var index = text.IndexOf(AutoSliceUserDataKey, StringComparison.Ordinal);
            if (index >= 0)
            {
                var end = text.IndexOf(';', index);
                text = text.Remove(index, (end < 0 ? text.Length : end + 1) - index);
            }
            if (border.HasValue)
            {
                var b = border.Value;
                text += AutoSliceUserDataKey + string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3};", b.x, b.y, b.z, b.w);
            }
            return text;
        }
    }
}

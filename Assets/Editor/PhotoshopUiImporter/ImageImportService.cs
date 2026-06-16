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
        // v2.8.1 像素內容去重統計（解碼後 raw RGBA 相同的 PNG 合併到同一個 sprite）
        public int dedupedSpriteCount;
        public long dedupedSpriteBytes;

        public bool IsValid => errors.Count == 0;
    }

    public static class ImageImportService
    {
        private const int FileOperationRetryCount = 8;
        private const int FileOperationRetryDelayMs = 120;

        public static ImageImportResult ImportImages(PhotoshopUiLayout layout, string sourceRoot, string importFolder)
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

            var imagePaths = new HashSet<string>();
            CollectImagePaths(layout.nodes, imagePaths);

            if (imagePaths.Count == 0)
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

            foreach (var imagePath in imagePaths)
            {
                ImportOneImage(sourceRootPath, importFolder, imagePath, result);
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

            // 第二輪：把 sprites 字典內所有指到重複 asset 的 entry 重指到 canonical sprite，
            // 然後實體刪除重複的 PNG asset。
            var deletedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

                if (deletedPaths.Add(assetPath))
                {
                    var absPath = PathUtility.ToAbsolutePath(assetPath);
                    try
                    {
                        var fileLength = File.Exists(absPath) ? new FileInfo(absPath).Length : 0L;
                        if (AssetDatabase.DeleteAsset(assetPath))
                        {
                            result.dedupedSpriteCount++;
                            result.dedupedSpriteBytes += fileLength;
                        }
                    }
                    catch
                    {
                        // 刪不掉就跳過，sprite 已經重指，留檔在 Atlas 內最多佔點空間，不影響行為
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
            Texture2D tex = null;
            try
            {
                var absPath = PathUtility.ToAbsolutePath(assetPath);
                if (!File.Exists(absPath))
                {
                    return null;
                }

                var bytes = File.ReadAllBytes(absPath);
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

        private static void CollectImagePaths(List<PhotoshopUiNode> nodes, HashSet<string> imagePaths)
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
                        imagePaths.Add(key);
                    }
                }

                CollectImagePaths(node.children, imagePaths);
            }
        }

        private static void ImportOneImage(string sourceRoot, string importFolder, string imagePath, ImageImportResult result)
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

            if (!IsSameFilePath(sourcePath, destinationFullPath) &&
                !PrepareDestinationForCopy(destinationAssetPath, destinationFullPath, result))
            {
                return;
            }

            if (!IsSameFilePath(sourcePath, destinationFullPath) &&
                !TryCopyFile(sourcePath, destinationFullPath, result))
            {
                return;
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

        private static bool PrepareDestinationForCopy(string destinationAssetPath, string destinationFullPath, ImageImportResult result)
        {
            if (!File.Exists(destinationFullPath))
            {
                return true;
            }

            AssetDatabase.DeleteAsset(destinationAssetPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            if (!File.Exists(destinationFullPath))
            {
                return true;
            }

            for (var attempt = 1; attempt <= FileOperationRetryCount; attempt++)
            {
                try
                {
                    File.SetAttributes(destinationFullPath, FileAttributes.Normal);
                    File.Delete(destinationFullPath);
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

            result.errors.Add($"圖片被 Unity 或其他程式鎖定，無法覆寫：{destinationFullPath}。請稍等 Unity 匯入完成後重試，或改用新的 Unity 圖片匯入資料夾。");
            return false;
        }

        private static bool TryCopyFile(string sourcePath, string destinationFullPath, ImageImportResult result)
        {
            for (var attempt = 1; attempt <= FileOperationRetryCount; attempt++)
            {
                try
                {
                    File.Copy(sourcePath, destinationFullPath, false);
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
    }
}

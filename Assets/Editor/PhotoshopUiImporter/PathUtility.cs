using System;
using System.IO;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    public static class PathUtility
    {
        public static string NormalizeAssetKey(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/').Trim().TrimStart('/');
        }

        public static bool IsAssetPath(string path)
        {
            var normalized = NormalizeAssetKey(path);
            return normalized == "Assets" || normalized.StartsWith("Assets/");
        }

        public static string ToProjectRelativeAssetPath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return string.Empty;
            }

            if (IsAssetPath(absolutePath))
            {
                return NormalizeAssetKey(absolutePath);
            }

            var projectRoot = GetProjectRoot();
            if (string.IsNullOrEmpty(projectRoot))
            {
                return string.Empty;
            }

            var fullPath = Path.GetFullPath(absolutePath).Replace('\\', '/').TrimEnd('/');
            var normalizedRoot = Path.GetFullPath(projectRoot).Replace('\\', '/').TrimEnd('/');

            if (!string.Equals(fullPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
                !fullPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return NormalizeAssetKey(fullPath.Substring(normalizedRoot.Length).TrimStart('/'));
        }

        public static string ToAbsolutePath(string assetPath)
        {
            var normalized = NormalizeAssetKey(assetPath);
            var projectRoot = GetProjectRoot();
            return string.IsNullOrEmpty(projectRoot) ? string.Empty : Path.Combine(projectRoot, normalized);
        }

        private static string GetProjectRoot()
        {
            return Directory.GetParent(Application.dataPath)?.FullName;
        }
    }
}

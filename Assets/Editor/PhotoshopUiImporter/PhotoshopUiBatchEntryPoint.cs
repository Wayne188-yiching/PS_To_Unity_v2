using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    public static class PhotoshopUiBatchEntryPoint
    {
        public static void Run()
        {
            var requestPath = GetArgument("-psToUnityRequest");
            var resultPath = GetArgument("-psToUnityResult");
            var result = new PhotoshopUiImportResult();

            try
            {
                if (string.IsNullOrWhiteSpace(requestPath) || !File.Exists(requestPath))
                    throw new FileNotFoundException("Missing -psToUnityRequest JSON file.", requestPath);
                if (string.IsNullOrWhiteSpace(resultPath))
                    throw new ArgumentException("Missing -psToUnityResult path.");

                var request = JsonUtility.FromJson<PhotoshopUiImportRequest>(File.ReadAllText(requestPath));
                result = PhotoshopUiImportService.Execute(request);
            }
            catch (Exception exception)
            {
                result.status = "BLOCKED";
                result.errors.Add(exception.ToString());
            }

            if (!string.IsNullOrWhiteSpace(resultPath))
            {
                var parent = Path.GetDirectoryName(resultPath);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                File.WriteAllText(resultPath, JsonUtility.ToJson(result, true));
            }

            if (!result.IsSuccess)
            {
                Debug.LogError($"[PS_To_Unity Batch] {result.status}: {string.Join(" | ", result.errors)}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[PS_To_Unity Batch] PASS: {result.prefabAssetPath}");
            EditorApplication.Exit(0);
        }

        private static string GetArgument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var index = 0; index < args.Length - 1; index++)
            {
                if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                    return args[index + 1];
            }
            return null;
        }
    }
}

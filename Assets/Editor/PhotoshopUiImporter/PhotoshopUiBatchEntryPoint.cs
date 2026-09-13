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
                result.AddError("BATCH_REQUEST_FAILED", exception.ToString());
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(resultPath))
                {
                    var parent = Path.GetDirectoryName(resultPath);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    var temporary = resultPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllText(temporary, JsonUtility.ToJson(result, true));
                    if (File.Exists(resultPath)) File.Replace(temporary, resultPath, null);
                    else File.Move(temporary, resultPath);
                }
            }
            catch (Exception exception)
            {
                result.stage = "RECEIPT";
                result.AddError("BATCH_RECEIPT_WRITE_FAILED", exception.ToString());
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

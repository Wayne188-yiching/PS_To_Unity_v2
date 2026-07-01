using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    public static class LayoutReader
    {
        // 這個 Unity backend 認得的 JSON schema 最高版本。
        // 讀到 major.minor 比這個大的 JSON → 加 warning 提示更新 Unity 工具（PHASE4_PLAN.md Q9-c）。
        private const string HighestSupportedSchemaVersion = "2.9";

        private static readonly HashSet<string> SupportedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "group",
            "image",
            "text"
        };

        public static bool TryRead(string jsonPath, out PhotoshopUiLayout layout, out LayoutReadResult result)
        {
            layout = null;
            result = new LayoutReadResult();

            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                result.errors.Add("請先選擇 layout.json。");
                return false;
            }

            if (!File.Exists(jsonPath))
            {
                result.errors.Add($"找不到 layout.json：{jsonPath}");
                return false;
            }

            try
            {
                var json = File.ReadAllText(jsonPath);
                layout = ParseLayout(json);
            }
            catch (Exception exception)
            {
                result.errors.Add($"layout.json 解析失敗：{exception.Message}");
                return false;
            }

            Validate(layout, result);
            if (layout?.warnings != null)
            {
                result.warnings.AddRange(layout.warnings);
            }
            return result.IsValid;
        }

        private static PhotoshopUiLayout ParseLayout(string json)
        {
            var root = SimpleJsonReader.Parse(json) as Dictionary<string, object>;
            if (root == null)
            {
                return null;
            }

            var layout = new PhotoshopUiLayout
            {
                schemaVersion = GetString(root, "schemaVersion"),
                canvas = ParseCanvas(GetObject(root, "canvas")),
                nodes = ParseNodes(GetArray(root, "nodes")),
                warnings = ParseWarnings(GetArray(root, "warnings"))
            };

            return layout;
        }

        private static List<PhotoshopUiWarning> ParseWarnings(List<object> raw)
        {
            var result = new List<PhotoshopUiWarning>();
            if (raw == null)
            {
                return result;
            }

            foreach (var item in raw)
            {
                var source = item as Dictionary<string, object>;
                if (source == null)
                {
                    continue;
                }

                result.Add(new PhotoshopUiWarning
                {
                    node = GetString(source, "node"),
                    code = GetString(source, "code"),
                    message = GetString(source, "message")
                });
            }

            return result;
        }

        private static PhotoshopUiCanvas ParseCanvas(Dictionary<string, object> source)
        {
            if (source == null)
            {
                return null;
            }

            return new PhotoshopUiCanvas
            {
                width = GetFloat(source, "width"),
                height = GetFloat(source, "height")
            };
        }

        private static List<PhotoshopUiNode> ParseNodes(List<object> rawNodes)
        {
            var result = new List<PhotoshopUiNode>();
            if (rawNodes == null)
            {
                return result;
            }

            foreach (var rawNode in rawNodes)
            {
                var source = rawNode as Dictionary<string, object>;
                if (source != null)
                {
                    result.Add(ParseNode(source));
                }
            }

            return result;
        }

        private static PhotoshopUiNode ParseNode(Dictionary<string, object> source)
        {
            return new PhotoshopUiNode
            {
                name = GetString(source, "name"),
                type = GetString(source, "type"),
                x = GetFloat(source, "x"),
                y = GetFloat(source, "y"),
                width = GetFloat(source, "width"),
                height = GetFloat(source, "height"),
                visible = GetBool(source, "visible", true),
                anchorPreset = GetString(source, "anchorPreset"),
                anchorMin = GetVector2(source, "anchorMin", new Vector2(0, 1)),
                anchorMax = GetVector2(source, "anchorMax", new Vector2(0, 1)),
                pivot = GetVector2(source, "pivot", new Vector2(0, 1)),
                imagePath = GetString(source, "imagePath"),
                skinKey = GetString(source, "skinKey"),
                text = GetString(source, "text"),
                fontToken = GetString(source, "fontToken"),
                materialToken = GetString(source, "materialToken"),
                fontSize = GetFloat(source, "fontSize"),
                color = GetString(source, "color"),
                outlineColor = GetString(source, "outlineColor"),
                outlineWidth = GetFloat(source, "outlineWidth"),
                outlineOpacity = GetFloat(source, "outlineOpacity", 1f),
                alignment = GetString(source, "alignment"),
                gradientStartColor = GetString(source, "gradientStartColor"),
                gradientEndColor = GetString(source, "gradientEndColor"),
                gradientAngle = GetFloat(source, "gradientAngle"),
                characterSpacing = GetFloat(source, "characterSpacing"),
                lineSpacing = GetFloat(source, "lineSpacing"),
                fakeThicknessOffsetX = GetFloat(source, "fakeThicknessOffsetX"),
                fakeThicknessOffsetY = GetFloat(source, "fakeThicknessOffsetY"),
                layoutType = GetString(source, "layoutType"),
                layoutSpacing = GetFloat(source, "layoutSpacing"),
                layoutPaddingLeft = GetFloat(source, "layoutPaddingLeft"),
                layoutPaddingRight = GetFloat(source, "layoutPaddingRight"),
                layoutPaddingTop = GetFloat(source, "layoutPaddingTop"),
                layoutPaddingBottom = GetFloat(source, "layoutPaddingBottom"),
                contentSizeFitter = GetBool(source, "contentSizeFitter", false),
                hasCanvasGroup = GetBool(source, "hasCanvasGroup", false),
                gridConstraintCount = (int)GetFloat(source, "gridConstraintCount"),
                gridStartAxis = GetString(source, "gridStartAxis"),
                gridCellSizeX = GetFloat(source, "gridCellSizeX"),
                gridCellSizeY = GetFloat(source, "gridCellSizeY"),
                gridSpacingX = GetFloat(source, "gridSpacingX"),
                gridSpacingY = GetFloat(source, "gridSpacingY"),
                children = ParseNodes(GetArray(source, "children"))
            };
        }

        private static Dictionary<string, object> GetObject(Dictionary<string, object> source, string key)
        {
            return source != null && source.TryGetValue(key, out var value) ? value as Dictionary<string, object> : null;
        }

        private static List<object> GetArray(Dictionary<string, object> source, string key)
        {
            return source != null && source.TryGetValue(key, out var value) ? value as List<object> : null;
        }

        private static string GetString(Dictionary<string, object> source, string key)
        {
            if (source == null || !source.TryGetValue(key, out var value) || value == null)
            {
                return string.Empty;
            }

            return value.ToString();
        }

        private static float GetFloat(Dictionary<string, object> source, string key)
        {
            if (source == null || !source.TryGetValue(key, out var value) || value == null)
            {
                return 0f;
            }

            if (value is double number)
            {
                return (float)number;
            }

            return float.TryParse(value.ToString(), out var parsed) ? parsed : 0f;
        }

        private static Vector2 GetVector2(Dictionary<string, object> source, string key, Vector2 fallback)
        {
            var value = GetObject(source, key);
            if (value == null)
            {
                return fallback;
            }

            return new Vector2(
                GetFloat(value, "x", fallback.x),
                GetFloat(value, "y", fallback.y));
        }

        private static float GetFloat(Dictionary<string, object> source, string key, float fallback)
        {
            if (source == null || !source.TryGetValue(key, out var value) || value == null)
            {
                return fallback;
            }

            if (value is double number)
            {
                return (float)number;
            }

            return float.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
        }

        private static bool GetBool(Dictionary<string, object> source, string key, bool fallback)
        {
            if (source == null || !source.TryGetValue(key, out var value) || value == null)
            {
                return fallback;
            }

            if (value is bool boolean)
            {
                return boolean;
            }

            return bool.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
        }

        public static LayoutReadResult Validate(PhotoshopUiLayout layout)
        {
            var result = new LayoutReadResult();
            Validate(layout, result);
            return result;
        }

        private static void Validate(PhotoshopUiLayout layout, LayoutReadResult result)
        {
            if (layout == null)
            {
                result.errors.Add("layout.json 內容為空或不是有效 JSON 物件。");
                return;
            }

            if (string.IsNullOrWhiteSpace(layout.schemaVersion))
            {
                result.errors.Add("schemaVersion 為必填。");
            }
            else if (IsSchemaVersionNewerThanSupported(layout.schemaVersion))
            {
                // PHASE4_PLAN.md Q9-c：JSON 比 Unity backend 認得的還新，提示更新工具。
                result.warnings.Add(new PhotoshopUiWarning
                {
                    node = "",
                    code = "UNITY_TOOL_OUTDATED",
                    message = $"layout.json schemaVersion 為 {layout.schemaVersion}，" +
                              $"高於此 Unity 工具支援的最高版本 {HighestSupportedSchemaVersion}。" +
                              "請更新 PhotoshopUiImporter 到最新版，否則部分新欄位將被忽略。"
                });
            }

            if (layout.canvas == null)
            {
                result.errors.Add("缺少 canvas 區塊。");
            }
            else
            {
                if (layout.canvas.width <= 0)
                {
                    result.errors.Add("canvas.width 必須大於 0。");
                }

                if (layout.canvas.height <= 0)
                {
                    result.errors.Add("canvas.height 必須大於 0。");
                }
            }

            if (layout.nodes == null || layout.nodes.Count == 0)
            {
                result.errors.Add("nodes 至少需要一個節點。");
                return;
            }

            var uniqueNodeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < layout.nodes.Count; i++)
            {
                ValidateNode(layout.nodes[i], $"nodes[{i}]", result, uniqueNodeNames);
            }
        }

        private static void ValidateNode(PhotoshopUiNode node, string path, LayoutReadResult result, HashSet<string> uniqueNodeNames)
        {
            if (node == null)
            {
                result.errors.Add($"{path} 是空節點。");
                return;
            }

            if (string.IsNullOrWhiteSpace(node.name))
            {
                result.errors.Add($"{path}.name 為必填。");
            }
            else if (!uniqueNodeNames.Add(node.name.Trim()))
            {
                result.errors.Add($"{path}.name 重複：{node.name}。同一份 layout.json 內的 node name 必須唯一。");
            }

            if (string.IsNullOrWhiteSpace(node.type))
            {
                result.errors.Add($"{path}.type 為必填。");
            }
            else if (!SupportedTypes.Contains(node.type))
            {
                result.errors.Add($"{path}.type 不支援：{node.type}。支援值為 group、image、text。");
            }

            if (node.width <= 0)
            {
                result.errors.Add($"{path}.width 必須大於 0。");
            }

            if (node.height <= 0)
            {
                result.errors.Add($"{path}.height 必須大於 0。");
            }

            ValidateNormalizedVector(node.anchorMin, $"{path}.anchorMin", result);
            ValidateNormalizedVector(node.anchorMax, $"{path}.anchorMax", result);
            ValidateNormalizedVector(node.pivot, $"{path}.pivot", result);
            if (node.anchorMin.x > node.anchorMax.x || node.anchorMin.y > node.anchorMax.y)
            {
                result.errors.Add($"{path}.anchorMin 不可大於 anchorMax。");
            }

            switch (node.NormalizedType)
            {
                case "image":
                    if (string.IsNullOrWhiteSpace(node.imagePath))
                    {
                        result.errors.Add($"{path}.imagePath 為 image 節點必填。");
                    }
                    else if (Path.IsPathRooted(node.imagePath))
                    {
                        result.errors.Add($"{path}.imagePath 必須是相對於圖片來源資料夾的路徑。");
                    }
                    else if (node.imagePath.Contains("../") || node.imagePath.Contains("..\\"))
                    {
                        result.errors.Add($"{path}.imagePath 不可包含上一層目錄。");
                    }
                    break;
                case "text":
                    if (string.IsNullOrWhiteSpace(node.text))
                    {
                        result.errors.Add($"{path}.text 為 text 節點必填。");
                    }
                    break;
            }

            if (node.children == null)
            {
                return;
            }

            for (var i = 0; i < node.children.Count; i++)
            {
                ValidateNode(node.children[i], $"{path}.children[{i}]", result, uniqueNodeNames);
            }
        }

        private static void ValidateNormalizedVector(Vector2 value, string path, LayoutReadResult result)
        {
            if (value.x < 0f || value.x > 1f || value.y < 0f || value.y > 1f)
            {
                result.errors.Add($"{path} 必須在 0 到 1 之間。");
            }
        }

        // 比對 major.minor（例："2.10" > "2.9" > "2.8"）。無法 parse 的字串當成同版，不阻擋 import。
        private static bool IsSchemaVersionNewerThanSupported(string jsonSchemaVersion)
        {
            if (!TryParseMajorMinor(jsonSchemaVersion, out var jsonMajor, out var jsonMinor))
            {
                return false;
            }
            if (!TryParseMajorMinor(HighestSupportedSchemaVersion, out var supportedMajor, out var supportedMinor))
            {
                return false;
            }

            if (jsonMajor != supportedMajor)
            {
                return jsonMajor > supportedMajor;
            }
            return jsonMinor > supportedMinor;
        }

        private static bool TryParseMajorMinor(string version, out int major, out int minor)
        {
            major = 0;
            minor = 0;
            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            var parts = version.Split('.');
            if (parts.Length < 1 || !int.TryParse(parts[0], out major))
            {
                return false;
            }
            if (parts.Length >= 2 && !int.TryParse(parts[1], out minor))
            {
                minor = 0;
            }
            return true;
        }
    }
}

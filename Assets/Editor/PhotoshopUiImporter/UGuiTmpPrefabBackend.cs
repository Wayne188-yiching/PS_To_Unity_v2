using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PhotoshopToUnity.EditorImporter
{
    public sealed class UGuiTmpPrefabBackend : IUiPrefabBackend
    {
        public string DisplayName => "uGUI + TextMeshPro";

        public GameObject GeneratePrefab(PrefabGenerationContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (context.layout == null || context.layout.canvas == null)
            {
                throw new InvalidOperationException("Prefab 生成失敗：layout 或 canvas 為空。");
            }

            if (!PathUtility.IsAssetPath(context.prefabOutputFolder))
            {
                throw new InvalidOperationException("Prefab 輸出資料夾必須位於 Assets 之下。");
            }

            Directory.CreateDirectory(PathUtility.ToAbsolutePath(context.prefabOutputFolder));
            AssetDatabase.Refresh();

            var rootName = string.IsNullOrWhiteSpace(context.prefabName) ? "PhotoshopUiPrefab" : context.prefabName;
            var root = new GameObject(rootName, typeof(RectTransform));

            try
            {
                var rootRect = root.GetComponent<RectTransform>();
                var referenceWidth = context.referenceResolution.x > 0f ? context.referenceResolution.x : 1920f;
                var referenceHeight = context.referenceResolution.y > 0f ? context.referenceResolution.y : 1080f;
                var cropOffsetX = Mathf.Max(0f, (context.layout.canvas.width - referenceWidth) * 0.5f);
                var cropOffsetY = Mathf.Max(0f, (context.layout.canvas.height - referenceHeight) * 0.5f);
                ApplyRootRect(rootRect, referenceWidth, referenceHeight);

                if (context.layout.nodes != null)
                {
                    foreach (var node in context.layout.nodes)
                    {
                        CreateNode(node, rootRect, cropOffsetX, cropOffsetY, referenceWidth, referenceHeight, rootRect.pivot, context);
                    }
                }

                var prefabPath = $"{PathUtility.NormalizeAssetKey(context.prefabOutputFolder).TrimEnd('/')}/{MakeSafeFileName(rootName)}.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out var success);
                if (!success || prefab == null)
                {
                    throw new InvalidOperationException($"Prefab 儲存失敗：{prefabPath}");
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void CreateNode(PhotoshopUiNode node, RectTransform parent, float parentX, float parentY, float parentWidth, float parentHeight, Vector2 parentPivot, PrefabGenerationContext context)
        {
            if (node == null || !node.visible)
            {
                return;
            }

            var gameObject = CreateGameObject(node);
            var rectTransform = gameObject.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);
            var isVisualNode = node.NormalizedType == "image" || node.NormalizedType == "text";
            if (isVisualNode)
            {
                ApplyNodeRect(rectTransform, node, parentX, parentY, parentWidth, parentHeight, parentPivot);
            }
            else
            {
                ApplyCoordinateSpaceRect(rectTransform, parentWidth, parentHeight);
            }

            switch (node.NormalizedType)
            {
                case "image":
                    ApplyImage(gameObject, node, context);
                    break;
                case "text":
                    ApplyText(gameObject, node, context);
                    break;
            }

            if (node.children == null)
            {
                return;
            }

            foreach (var child in node.children)
            {
                if (isVisualNode)
                {
                    CreateNode(child, rectTransform, node.x, node.y, node.width, node.height, rectTransform.pivot, context);
                }
                else
                {
                    CreateNode(child, rectTransform, parentX, parentY, parentWidth, parentHeight, rectTransform.pivot, context);
                }
            }
        }

        private static GameObject CreateGameObject(PhotoshopUiNode node)
        {
            var objectName = string.IsNullOrWhiteSpace(node.name) ? node.NormalizedType : node.name;

            switch (node.NormalizedType)
            {
                case "image":
                    return new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                case "text":
                    return new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                default:
                    return new GameObject(objectName, typeof(RectTransform));
            }
        }

        private static void ApplyRootRect(RectTransform rectTransform, float referenceWidth, float referenceHeight)
        {
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(referenceWidth, referenceHeight);
            rectTransform.localScale = Vector3.one;
            rectTransform.localRotation = Quaternion.identity;
        }

        private static void ApplyNodeRect(RectTransform rectTransform, PhotoshopUiNode node, float parentX, float parentY, float parentWidth, float parentHeight, Vector2 parentPivot)
        {
            var left = node.x - parentX;
            var top = node.y - parentY;
            var width = node.width;
            var height = node.height;

            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(
                left + width * 0.5f - parentWidth * 0.5f,
                parentHeight * 0.5f - top - height * 0.5f);
            rectTransform.sizeDelta = new Vector2(width, height);

            rectTransform.localScale = Vector3.one;
            rectTransform.localRotation = Quaternion.identity;
        }

        private static void ApplyCoordinateSpaceRect(RectTransform rectTransform, float parentWidth, float parentHeight)
        {
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(parentWidth, parentHeight);
            rectTransform.localScale = Vector3.one;
            rectTransform.localRotation = Quaternion.identity;
        }

        private static Vector2 AnchorPointFromTopLeft(Vector2 anchor, float parentWidth, float parentHeight, Vector2 parentPivot)
        {
            return new Vector2(
                (anchor.x - parentPivot.x) * parentWidth,
                (anchor.y - parentPivot.y) * parentHeight);
        }

        private static Vector2 Clamp01(Vector2 value)
        {
            return new Vector2(Mathf.Clamp01(value.x), Mathf.Clamp01(value.y));
        }

        private static void ApplyImage(GameObject gameObject, PhotoshopUiNode node, PrefabGenerationContext context)
        {
            var image = gameObject.GetComponent<Image>();
            image.raycastTarget = false;
            image.sprite = context.skinResolver?.Resolve(node);
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
        }

        private static void ApplyText(GameObject gameObject, PhotoshopUiNode node, PrefabGenerationContext context)
        {
            var text = gameObject.GetComponent<TextMeshProUGUI>();
            context.tmpMapper?.Apply(text, node);
        }

        private static string MakeSafeFileName(string value)
        {
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidChar, '_');
            }

            return string.IsNullOrWhiteSpace(value) ? "PhotoshopUiPrefab" : value;
        }
    }
}

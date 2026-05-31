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
            var isLayoutGroup = node.NormalizedType == "group" && !string.IsNullOrWhiteSpace(node.layoutType);

            if (isVisualNode || isLayoutGroup)
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
                    if (node.fakeThicknessOffsetX != 0f || node.fakeThicknessOffsetY != 0f)
                    {
                        ApplyFakeThicknessText(gameObject, rectTransform, node, context);
                        return;
                    }
                    ApplyText(gameObject, node, context);
                    break;
                case "group":
                    if (isLayoutGroup)
                        ApplyLayoutGroup(gameObject, node);
                    break;
            }

            if (node.children == null)
            {
                return;
            }

            foreach (var child in node.children)
            {
                if (isVisualNode || isLayoutGroup)
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

        private static void ApplyLayoutGroup(GameObject gameObject, PhotoshopUiNode node)
        {
            var padding = new RectOffset(
                Mathf.RoundToInt(node.layoutPaddingLeft),
                Mathf.RoundToInt(node.layoutPaddingRight),
                Mathf.RoundToInt(node.layoutPaddingTop),
                Mathf.RoundToInt(node.layoutPaddingBottom));

            var isHorizontal = string.Equals(node.layoutType, "horizontal", StringComparison.OrdinalIgnoreCase);

            if (isHorizontal)
            {
                var lg = gameObject.AddComponent<HorizontalLayoutGroup>();
                lg.spacing = node.layoutSpacing;
                lg.padding = padding;
                lg.childAlignment = TextAnchor.MiddleLeft;
                lg.childControlWidth = false;
                lg.childControlHeight = false;
                lg.childForceExpandWidth = false;
                lg.childForceExpandHeight = false;
            }
            else
            {
                var lg = gameObject.AddComponent<VerticalLayoutGroup>();
                lg.spacing = node.layoutSpacing;
                lg.padding = padding;
                lg.childAlignment = TextAnchor.UpperCenter;
                lg.childControlWidth = false;
                lg.childControlHeight = false;
                lg.childForceExpandWidth = false;
                lg.childForceExpandHeight = false;
            }

            if (node.contentSizeFitter)
            {
                var csf = gameObject.AddComponent<ContentSizeFitter>();
                csf.horizontalFit = isHorizontal
                    ? ContentSizeFitter.FitMode.PreferredSize
                    : ContentSizeFitter.FitMode.Unconstrained;
                csf.verticalFit = isHorizontal
                    ? ContentSizeFitter.FitMode.Unconstrained
                    : ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        private static void ApplyText(GameObject gameObject, PhotoshopUiNode node, PrefabGenerationContext context)
        {
            var text = gameObject.GetComponent<TextMeshProUGUI>();
            context.tmpMapper?.Apply(text, node);
        }

        // 假厚度：在原 group GameObject 下疊兩層 TMP（shadow 在下、main 在上）
        private static void ApplyFakeThicknessText(GameObject group, RectTransform groupRect, PhotoshopUiNode node, PrefabGenerationContext context)
        {
            // shadow TMP（偏移，顏色加深）
            var shadowGo = new GameObject($"{group.name}_shadow", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var shadowRect = shadowGo.GetComponent<RectTransform>();
            shadowRect.SetParent(groupRect, false);
            ApplyChildTmpRect(shadowRect, node.width, node.height, node.fakeThicknessOffsetX, node.fakeThicknessOffsetY);
            var shadowTmp = shadowGo.GetComponent<TextMeshProUGUI>();
            context.tmpMapper?.Apply(shadowTmp, node);
            var c = shadowTmp.color;
            shadowTmp.color = new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, c.a);

            // main TMP（置中，原始設定）
            var mainGo = new GameObject($"{group.name}_main", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var mainRect = mainGo.GetComponent<RectTransform>();
            mainRect.SetParent(groupRect, false);
            ApplyChildTmpRect(mainRect, node.width, node.height, 0f, 0f);
            context.tmpMapper?.Apply(mainGo.GetComponent<TextMeshProUGUI>(), node);

            // 子節點掛在 group 上，座標空間與原 node 一致
            if (node.children == null) return;
            foreach (var child in node.children)
                CreateNode(child, groupRect, node.x, node.y, node.width, node.height, groupRect.pivot, context);
        }

        private static void ApplyChildTmpRect(RectTransform rect, float width, float height, float offsetX, float offsetY)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(offsetX, -offsetY); // Unity Y 軸向上，PS 向下
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
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

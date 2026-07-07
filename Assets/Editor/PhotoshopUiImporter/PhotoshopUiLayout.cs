using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    [Serializable]
    public sealed class PhotoshopUiLayout
    {
        public string schemaVersion;
        public PhotoshopUiCanvas canvas;
        public List<PhotoshopUiNode> nodes = new List<PhotoshopUiNode>();
        public List<PhotoshopUiWarning> warnings = new List<PhotoshopUiWarning>();
    }

    [Serializable]
    public sealed class PhotoshopUiWarning
    {
        public string node;
        public string code;
        public string message;
    }

    [Serializable]
    public sealed class PhotoshopUiCanvas
    {
        public float width;
        public float height;
    }

    [Serializable]
    public sealed class PhotoshopUiNode
    {
        public string name;
        public string type;
        public float x;
        public float y;
        public float width;
        public float height;
        public bool visible = true;
        public string anchorPreset;
        public Vector2 anchorMin = new Vector2(0, 1);
        public Vector2 anchorMax = new Vector2(0, 1);
        public Vector2 pivot = new Vector2(0, 1);
        public string imagePath;
        public string skinKey;
        public string text;
        public string fontToken;
        public string materialToken;
        public float fontSize;
        public float characterSpacing;
        public float lineSpacing;
        public string color;
        public string outlineColor;
        public float outlineWidth;
        public float outlineOpacity = 1f;
        public string alignment;
        // Phase 3 漸層文字（PS Gradient Overlay）：兩端色 + 角度
        // PS Gradient Overlay 預設 angle = 90 = 視覺上「上→下」漸層；first stop = 上、last stop = 下
        // 不填則 TmpMapper 不套用漸層（保留原 color 單色行為）。
        public string gradientStartColor;
        public string gradientEndColor;
        public float gradientAngle;
        public float fakeThicknessOffsetX;
        public float fakeThicknessOffsetY;
        public string layoutType;           // "horizontal" | "vertical" | ""
        public float layoutSpacing;
        public float layoutPaddingLeft;
        public float layoutPaddingRight;
        public float layoutPaddingTop;
        public float layoutPaddingBottom;
        public bool contentSizeFitter;
        // Phase 4：JSX 偵測 [CG] / [CANVASGROUP] 標籤時填 true（root GameObject 由 Unity 端 hardcode 掛 CanvasGroup，不看此欄）。
        public bool hasCanvasGroup;
        // Phase 4 Grid：僅在 layoutType == "grid" 時有效。startCorner / childAlignment 由 Unity 端固定為 UpperLeft，不進 JSON（OPTIMIZATION_PLAN_zh.html#phase4-decisions Q12-d）。
        public int gridConstraintCount;
        public string gridStartAxis;    // "horizontal" | "vertical"
        public float gridCellSizeX;
        public float gridCellSizeY;
        public float gridSpacingX;
        public float gridSpacingY;
        public List<PhotoshopUiNode> children = new List<PhotoshopUiNode>();

        public string NormalizedType => string.IsNullOrWhiteSpace(type) ? string.Empty : type.Trim().ToLowerInvariant();
    }

    public sealed class LayoutReadResult
    {
        public readonly List<string> errors = new List<string>();
        public readonly List<PhotoshopUiWarning> warnings = new List<PhotoshopUiWarning>();

        public bool IsValid => errors.Count == 0;
    }
}

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
        public List<PhotoshopUiNode> children = new List<PhotoshopUiNode>();

        public string NormalizedType => string.IsNullOrWhiteSpace(type) ? string.Empty : type.Trim().ToLowerInvariant();
    }

    public sealed class LayoutReadResult
    {
        public readonly List<string> errors = new List<string>();

        public bool IsValid => errors.Count == 0;
    }
}

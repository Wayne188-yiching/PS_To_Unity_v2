using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    // v2.10：fontToken → TMP Font Asset 對應表（多字型支援）。
    // JSX 端把 PS 字型 PostScript 名寫進 layout.json 的 fontToken（小寫底線 slug），
    // Unity 端以「關鍵字包含」比對：兩邊都正規化成小寫英數後做 Contains，
    // 所以 keyword 填 dfhei 就能涵蓋 DFHeiStd-W5 / DFHei-Bd 等整個家族。
    [CreateAssetMenu(menuName = "Photoshop UI Importer/Tmp Font Map", fileName = "TmpFontMap")]
    public sealed class TmpFontMap : ScriptableObject
    {
        public List<TmpFontMapEntry> entries = new List<TmpFontMapEntry>();

        public bool TryGetEntry(string fontToken, out TmpFontMapEntry match)
        {
            match = null;
            var token = NormalizeSlug(fontToken);
            if (token.Length == 0)
            {
                return false;
            }

            foreach (var entry in entries)
            {
                if (entry == null || entry.fontAsset == null)
                {
                    continue;
                }

                var keyword = NormalizeSlug(entry.fontKeyword);
                if (keyword.Length == 0)
                {
                    continue;
                }

                if (token.Contains(keyword))
                {
                    match = entry;
                    return true;
                }
            }

            return false;
        }

        // 鏡像 JSX normalizeAsciiSlug 的比對語意：只留 a-z0-9（底線等分隔符一併剝掉，比對更寬鬆）。
        private static string NormalizeSlug(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var character in value.ToLowerInvariant())
            {
                if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }
    }

    [Serializable]
    public sealed class TmpFontMapEntry
    {
        [Tooltip("字型關鍵字，比對 layout.json 的 fontToken（如 gensen、dfhei、notosans）。部分符合即生效。")]
        public string fontKeyword;

        public TMP_FontAsset fontAsset;

        [Tooltip("選填：此字型的預設 TMP 材質球；留空時用 fontAsset 自帶的預設材質。")]
        public Material materialPreset;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PhotoshopToUnity.EditorImporter
{
    internal static class SimpleJsonReader
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var parser = new Parser(json);
            var value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.IsComplete)
            {
                throw new FormatException("JSON 內容尾端還有未解析字元。");
            }

            return value;
        }

        private sealed class Parser
        {
            private readonly string json;
            private int index;

            public Parser(string json)
            {
                this.json = json;
            }

            public bool IsComplete => index >= json.Length;

            public void SkipWhitespace()
            {
                while (!IsComplete && char.IsWhiteSpace(json[index]))
                {
                    index++;
                }
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (IsComplete)
                {
                    throw new FormatException("JSON 意外結束。");
                }

                switch (json[index])
                {
                    case '{':
                        return ParseObject();
                    case '[':
                        return ParseArray();
                    case '"':
                        return ParseString();
                    case 't':
                        ConsumeLiteral("true");
                        return true;
                    case 'f':
                        ConsumeLiteral("false");
                        return false;
                    case 'n':
                        ConsumeLiteral("null");
                        return null;
                    default:
                        return ParseNumber();
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                Expect('{');
                SkipWhitespace();
                if (TryConsume('}'))
                {
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    var key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    result[key] = ParseValue();
                    SkipWhitespace();

                    if (TryConsume('}'))
                    {
                        return result;
                    }

                    Expect(',');
                }
            }

            private List<object> ParseArray()
            {
                var result = new List<object>();
                Expect('[');
                SkipWhitespace();
                if (TryConsume(']'))
                {
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();

                    if (TryConsume(']'))
                    {
                        return result;
                    }

                    Expect(',');
                }
            }

            private string ParseString()
            {
                Expect('"');
                var builder = new StringBuilder();

                while (!IsComplete)
                {
                    var ch = json[index++];
                    if (ch == '"')
                    {
                        return builder.ToString();
                    }

                    if (ch != '\\')
                    {
                        builder.Append(ch);
                        continue;
                    }

                    if (IsComplete)
                    {
                        throw new FormatException("JSON 字串跳脫序列不完整。");
                    }

                    var escaped = json[index++];
                    switch (escaped)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            builder.Append(escaped);
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'u':
                            builder.Append(ParseUnicode());
                            break;
                        default:
                            throw new FormatException($"不支援的 JSON 跳脫字元：\\{escaped}");
                    }
                }

                throw new FormatException("JSON 字串缺少結尾引號。");
            }

            private char ParseUnicode()
            {
                if (index + 4 > json.Length)
                {
                    throw new FormatException("JSON Unicode 跳脫序列不完整。");
                }

                var hex = json.Substring(index, 4);
                index += 4;
                return (char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            private double ParseNumber()
            {
                var start = index;
                while (!IsComplete)
                {
                    var ch = json[index];
                    if (char.IsDigit(ch) || ch == '-' || ch == '+' || ch == '.' || ch == 'e' || ch == 'E')
                    {
                        index++;
                        continue;
                    }

                    break;
                }

                if (start == index)
                {
                    throw new FormatException($"JSON 值格式錯誤，位置 {index}。");
                }

                var raw = json.Substring(start, index - start);
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    throw new FormatException($"JSON 數值格式錯誤：{raw}");
                }

                return number;
            }

            private void ConsumeLiteral(string literal)
            {
                if (index + literal.Length > json.Length ||
                    !string.Equals(json.Substring(index, literal.Length), literal, StringComparison.Ordinal))
                {
                    throw new FormatException($"JSON 常值格式錯誤：預期 {literal}");
                }

                index += literal.Length;
            }

            private bool TryConsume(char expected)
            {
                SkipWhitespace();
                if (!IsComplete && json[index] == expected)
                {
                    index++;
                    return true;
                }

                return false;
            }

            private void Expect(char expected)
            {
                SkipWhitespace();
                if (IsComplete || json[index] != expected)
                {
                    throw new FormatException($"JSON 格式錯誤：預期 '{expected}'。");
                }

                index++;
            }
        }
    }
}

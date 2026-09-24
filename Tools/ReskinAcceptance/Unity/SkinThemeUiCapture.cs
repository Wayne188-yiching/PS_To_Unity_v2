using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PhotoshopToUnity.EditorImporter;
using UnityEditor;
using UnityEngine;

// SkinTheme 介面擷取（需要非 batchmode 的 Unity：IMGUI 只有在真的畫出來時才有內容）。
// 每個情境開一個浮動 Inspector（或 Importer_v2 視窗），等它重繪後：
//   * 用 GUIViewDebugger 取出實際畫出的每一個文字元件（文字、tooltip、樣式、位置）→ ui_dump.json
//   * 機械檢查：標籤 / 按鈕文字被截斷、兩個文字元件互相重疊
//   * GrabPixels 存截圖（給人看，不給 Jev）
// 同一支程式跑 BEFORE（-ImporterRef 舊版，預設英文 Inspector）與 AFTER，情境與資料完全相同。
namespace PsUiReskinAcceptance
{
    public static class SkinThemeUiCapture
    {
        private const string Root = "Assets/UiCaptureFixture";
        private const string OptionalFoldoutKey = "PhotoshopUiImporter.SkinThemeInspector.Advanced";

        private sealed class Scenario
        {
            public string name, kind, themePath;
            public int width, height;
            public bool optionalOpen;
        }

        private static readonly List<Scenario> Scenarios = new List<Scenario>();
        private static readonly List<object> Dumps = new List<object>();
        private static int index, frame;
        private static EditorWindow current;

        public static void Run()
        {
            Application.logMessageReceived += OnLog;
            try
            {
                BuildFixture();
                foreach (var w in new[] { 360, 520 })
                {
                    Scenarios.Add(new Scenario { name = "inspector_new_empty", kind = "inspector", themePath = Root + "/SkinTheme_Empty.asset", width = w, height = 700 });
                    Scenarios.Add(new Scenario { name = "inspector_configured", kind = "inspector", themePath = Root + "/SkinTheme_Configured.asset", width = w, height = 900 });
                    Scenarios.Add(new Scenario { name = "inspector_configured_optional_open", kind = "inspector", themePath = Root + "/SkinTheme_Configured.asset", width = w, height = 900, optionalOpen = true });
                }
                Scenarios.Add(new Scenario { name = "inspector_legacy", kind = "inspector", themePath = Root + "/SkinTheme_Legacy.asset", width = 520, height = 600 });
                Scenarios.Add(new Scenario { name = "inspector_conflict", kind = "inspector", themePath = Root + "/SkinTheme_Conflict.asset", width = 520, height = 700 });
                Scenarios.Add(new Scenario { name = "window_configured", kind = "window", themePath = Root + "/SkinTheme_Configured.asset", width = 560, height = 1000 });
                EditorApplication.update += Tick;
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private static readonly List<string> ConsoleErrors = new List<string>();

        private static void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) ConsoleErrors.Add(condition);
        }

        private static void Tick()
        {
            try
            {
                frame++;
                if (index >= Scenarios.Count)
                {
                    Finish();
                    return;
                }
                var s = Scenarios[index];
                if (frame == 1) Open(s);
                else if (frame == 40) StartDebug(current);
                else if (frame == 70) { Capture(s); Close(); index++; frame = 0; }
                else if (frame % 10 == 0 && current != null) current.Repaint();
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        // ── 情境 ─────────────────────────────────────────────────────────────
        private static void Open(Scenario s)
        {
            SessionState.SetBool(OptionalFoldoutKey, s.optionalOpen);
            var theme = AssetDatabase.LoadAssetAtPath<PsUiSkinTheme>(s.themePath);
            if (s.kind == "inspector")
            {
                var inspectorType = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
                current = (EditorWindow)ScriptableObject.CreateInstance(inspectorType);
                current.Show();
                Selection.activeObject = theme;
            }
            else
            {
                var windowType = typeof(PsUiSkinTheme).Assembly.GetType("PhotoshopToUnity.EditorImporter.PhotoshopUiImporterWindow");
                current = EditorWindow.GetWindow(windowType, false, "Importer_v2");
                SetField(current, "activeSkinTheme", theme);
                SetField(current, "showReskinFoldout", true);
                SetField(current, "scrollPosition", new Vector2(0f, 100000f));
                Selection.activeObject = null;
            }
            current.position = new Rect(80, 80, s.width, s.height);
            current.Focus();
            current.Repaint();
        }

        private static void Close()
        {
            StopDebug();
            if (current != null) current.Close();
            current = null;
        }

        private static void Capture(Scenario s)
        {
            var items = ReadInstructions();
            if (s.kind == "window")
            {
                // 只保留換皮工具區（它是視窗最後一區）。
                var start = items.FindIndex(i => i.text.StartsWith("換皮工具"));
                if (start >= 0) items = items.Skip(start).ToList();
            }
            var truncated = new List<object>();
            var overlaps = new List<object>();
            var textItems = items.Where(i => i.checkable).ToList();
            foreach (var i in textItems)
                if (i.clips && i.needWidth > i.rect.width + 1.5f)
                    truncated.Add(new JObj { { "text", i.text }, { "style", i.style }, { "width", Round(i.rect.width) }, { "needWidth", Round(i.needWidth) } });
            // 重疊用「實際畫出的範圍」：會溢出的樣式（Foldout 等）以文字需要的寬度計。
            Rect Drawn(DrawnItem i) => i.clips ? i.rect : new Rect(i.rect.x, i.rect.y, Mathf.Max(i.rect.width, i.needWidth), i.rect.height);
            for (var a = 0; a < textItems.Count; a++)
                for (var b = a + 1; b < textItems.Count; b++)
                {
                    var r1 = Drawn(textItems[a]);
                    var r2 = Drawn(textItems[b]);
                    if (r1 == r2 && textItems[a].text == textItems[b].text) continue;
                    var ix = Mathf.Min(r1.xMax, r2.xMax) - Mathf.Max(r1.xMin, r2.xMin);
                    var iy = Mathf.Min(r1.yMax, r2.yMax) - Mathf.Max(r1.yMin, r2.yMin);
                    if (ix > 2f && iy > 2f)
                        overlaps.Add(new JObj { { "a", textItems[a].text }, { "b", textItems[b].text }, { "overlapPx", new[] { Round(ix), Round(iy) } } });
                }

            var png = $"{s.name}_{s.width}.png";
            var shot = Screenshot(current, Path.Combine(ReskinFixture.OutDir, png));
            // Unity 6 的預設 Inspector 用 UI Toolkit 畫，GUIViewDebugger 抓不到；它畫的就是每個可見序列化欄位的
            // displayName + tooltip，所以另外列出來（兩版都列，Jev 那邊依 editorType 決定用哪一份）。
            string editorType = null;
            var fields = new List<object>();
            if (s.kind == "inspector")
            {
                var theme = AssetDatabase.LoadAssetAtPath<PsUiSkinTheme>(s.themePath);
                var editor = Editor.CreateEditor(theme);
                editorType = editor.GetType().Name;
                UnityEngine.Object.DestroyImmediate(editor);
                var so = new SerializedObject(theme);
                var it = so.GetIterator();
                for (var enter = true; it.NextVisible(enter); enter = false)
                {
                    if (it.propertyPath == "m_Script") continue;
                    fields.Add(new JObj
                    {
                        { "displayName", it.displayName }, { "tooltip", string.IsNullOrEmpty(it.tooltip) ? null : it.tooltip },
                        { "value", it.propertyType == SerializedPropertyType.ObjectReference
                            ? (it.objectReferenceValue != null ? AssetDatabase.GetAssetPath(it.objectReferenceValue) : "None")
                            : it.isArray && it.propertyType != SerializedPropertyType.String ? $"{it.arraySize} elements" : it.stringValue },
                    });
                }
            }
            Dumps.Add(new JObj
            {
                { "scenario", s.name }, { "kind", s.kind }, { "width", s.width }, { "optionalOpen", s.optionalOpen },
                { "editorType", editorType }, { "serializedFields", fields },
                { "screenshot", shot ? png : null },
                { "items", items.Select(i => (object)new JObj
                    {
                        { "text", i.text }, { "tooltip", string.IsNullOrEmpty(i.tooltip) ? null : i.tooltip }, { "style", i.style },
                        { "rect", new[] { Round(i.rect.x), Round(i.rect.y), Round(i.rect.width), Round(i.rect.height) } },
                    }).ToList() },
                { "truncated", truncated }, { "overlaps", overlaps },
            });
        }

        private static void Finish()
        {
            EditorApplication.update -= Tick;
            var result = new JObj
            {
                { "toolVersion", ToolVersion() }, { "unity", Application.unityVersion }, { "pixelsPerPoint", EditorGUIUtility.pixelsPerPoint },
                { "consoleErrors", ConsoleErrors }, { "scenarios", Dumps },
            };
            File.WriteAllText(Path.Combine(ReskinFixture.OutDir, "ui_dump.json"), result.ToJson());
            EditorApplication.Exit(0);
        }

        private static void Fail(Exception e)
        {
            EditorApplication.update -= Tick;
            File.WriteAllText(Path.Combine(ReskinFixture.OutDir, "ui_capture_exception.txt"), e.ToString());
            EditorApplication.Exit(1);
        }

        private static string ToolVersion()
        {
            var windowType = typeof(PsUiSkinTheme).Assembly.GetType("PhotoshopToUnity.EditorImporter.PhotoshopUiImporterWindow");
            var f = windowType?.GetField("ToolVersion", BindingFlags.NonPublic | BindingFlags.Static);
            return f?.GetValue(null) as string;
        }

        // ── 測試資料：兩個資料夾、三張圖、四份 SkinTheme ─────────────────────────
        private static void BuildFixture()
        {
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            AssetDatabase.CreateFolder("Assets", "UiCaptureFixture");
            AssetDatabase.CreateFolder(Root, "CatAct");
            AssetDatabase.CreateFolder(Root, "LiveRoom");
            var oldIcon = MakeSprite(Root + "/CatAct/Icon_Old.png", 1);
            var oldFrame = MakeSprite(Root + "/CatAct/Frame_Old.png", 2);
            var newIcon = MakeSprite(Root + "/LiveRoom/Icon_New.png", 3);
            var catAct = AssetDatabase.LoadAssetAtPath<DefaultAsset>(Root + "/CatAct");
            var liveRoom = AssetDatabase.LoadAssetAtPath<DefaultAsset>(Root + "/LiveRoom");

            Save(ScriptableObject.CreateInstance<PsUiSkinTheme>(), "SkinTheme_Empty.asset");

            var configured = ScriptableObject.CreateInstance<PsUiSkinTheme>();
            configured.targetPrefabFolderAsset = liveRoom;
            configured.sourcePrefabFolderAsset = catAct;
            configured.entries.Add(new SkinThemeEntry { oldSprite = oldIcon, newSprite = newIcon });
            configured.entries.Add(new SkinThemeEntry { oldSprite = oldFrame, newSprite = newIcon });
            configured.entries.Add(new SkinThemeEntry { oldSprite = oldFrame });
            Save(configured, "SkinTheme_Configured.asset");

            var legacy = ScriptableObject.CreateInstance<PsUiSkinTheme>();
            legacy.targetPrefabFolderAsset = catAct;
            legacy.referencePrefabFolderAsset = liveRoom;
            legacy.entries.Add(new SkinThemeEntry { oldSprite = oldIcon });
            Save(legacy, "SkinTheme_Legacy.asset");

            var conflict = ScriptableObject.CreateInstance<PsUiSkinTheme>();
            conflict.targetPrefabFolderAsset = liveRoom;
            conflict.sourcePrefabFolderAsset = liveRoom;
            conflict.entries.Add(new SkinThemeEntry { oldSprite = oldIcon, newSprite = newIcon });
            Save(conflict, "SkinTheme_Conflict.asset");
            AssetDatabase.SaveAssets();
        }

        private static void Save(PsUiSkinTheme theme, string file) => AssetDatabase.CreateAsset(theme, Root + "/" + file);

        private static Sprite MakeSprite(string path, int seed)
        {
            ReskinFixture.WritePng(ReskinFixture.Abs(path), 32, 32, new Color32(90, 110, 140, 255), new Color32(230, 120, 40, 255), seed);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ── GUIViewDebugger（內部 API，反射）────────────────────────────────────
        private sealed class DrawnItem
        {
            public string text, tooltip, style;
            public Rect rect;
            public float needWidth;
            public bool checkable, clips;
        }

        private static Type debuggerType, instructionType;

        private static Type FindType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName)).FirstOrDefault(t => t != null);

        private static object ParentView(EditorWindow w) =>
            typeof(EditorWindow).GetField("m_Parent", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(w);

        private static void StartDebug(EditorWindow w)
        {
            debuggerType = debuggerType ?? FindType("UnityEditor.GUIViewDebuggerHelper");
            instructionType = instructionType ?? FindType("UnityEditor.IMGUIDrawInstruction");
            if (debuggerType == null || instructionType == null)
                throw new Exception("GUIViewDebuggerHelper / IMGUIDrawInstruction not found in this Unity version.");
            var debugWindow = debuggerType.GetMethod("DebugWindow", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (debugWindow == null)
                throw new Exception("GUIViewDebuggerHelper.DebugWindow missing; methods: " +
                                    string.Join(", ", debuggerType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).Select(m => m.Name)));
            debugWindow.Invoke(null, new[] { ParentView(w) });
            w.Repaint();
        }

        private static void StopDebug()
        {
            debuggerType?.GetMethod("StopDebugging", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        }

        private static List<DrawnItem> ReadInstructions()
        {
            var listType = typeof(List<>).MakeGenericType(instructionType);
            var list = (IList)Activator.CreateInstance(listType);
            var candidates = debuggerType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "GetDrawInstructions").ToList();
            // Unity 6000.0：GetDrawInstructions(List<IMGUIDrawInstruction>, bool extraInfo)；舊版只有 list 一個參數。
            var get = candidates.FirstOrDefault(m => m.GetParameters().Length >= 1 && m.GetParameters()[0].ParameterType == listType &&
                                                     m.GetParameters().Skip(1).All(p => p.ParameterType == typeof(bool)));
            if (get == null)
                throw new Exception("GetDrawInstructions signature not supported: " + string.Join(" | ", debuggerType
                    .GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.Name.StartsWith("Get") || m.Name.Contains("Debug"))
                    .Select(m => $"{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})")));
            get.Invoke(null, new object[] { list }.Concat(get.GetParameters().Skip(1).Select(_ => (object)false)).ToArray());
            const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var rectField = instructionType.GetField("rect", F);
            var styleField = instructionType.GetField("usedGUIStyle", F);
            var contentField = instructionType.GetField("usedGUIContent", F);
            if (rectField == null || styleField == null || contentField == null)
                throw new Exception("IMGUIDrawInstruction fields: " + string.Join(", ", instructionType.GetFields(F).Select(f => f.Name)));

            var items = new List<DrawnItem>();
            foreach (var ins in list)
            {
                var content = contentField.GetValue(ins) as GUIContent;
                var style = styleField.GetValue(ins) as GUIStyle;
                var text = content?.text ?? string.Empty;
                var tooltip = content?.tooltip ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(tooltip)) continue;
                var rect = (Rect)rectField.GetValue(ins);
                var styleName = style?.name ?? string.Empty;
                var lower = styleName.ToLowerInvariant();
                // 截斷／重疊只檢查「一行文字」類元件：標籤、按鈕、摺疊標題。欄位內容（物件名稱、路徑）本來就可能被裁切；
                // Inspector 標題列（LargeLabel，顯示資產檔名）是 Unity 畫的，長度取決於使用者取的檔名，也不列入。
                // 樣式允許溢出（clipping = Overflow，例如 Foldout）時文字不會被切，只算重疊、不算截斷。
                var checkable = style != null && !style.wordWrap && !string.IsNullOrWhiteSpace(text) && lower != "largelabel" &&
                                (lower.Contains("label") || lower.Contains("button") || lower.Contains("foldout"));
                items.Add(new DrawnItem
                {
                    text = text, tooltip = tooltip, style = styleName, rect = rect, checkable = checkable,
                    clips = checkable && style.clipping != TextClipping.Overflow,
                    needWidth = checkable ? style.CalcSize(new GUIContent(text)).x : 0f,
                });
            }
            return items;
        }

        private static bool Screenshot(EditorWindow w, string path)
        {
            try
            {
                var view = ParentView(w);
                var grab = view?.GetType().GetMethod("GrabPixels", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (grab == null) return false;
                var ppp = EditorGUIUtility.pixelsPerPoint;
                var pw = Mathf.RoundToInt(w.position.width * ppp);
                var ph = Mathf.RoundToInt(w.position.height * ppp);
                var rt = new RenderTexture(pw, ph, 0, RenderTextureFormat.ARGB32);
                grab.Invoke(view, new object[] { rt, new Rect(0, 0, pw, ph) });
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(pw, ph, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, pw, ph), 0, 0);
                RenderTexture.active = prev;
                // GrabPixels 的列序是由下往上，存檔前翻正。
                var px = tex.GetPixels32();
                var flipped = new Color32[px.Length];
                for (var y = 0; y < ph; y++) Array.Copy(px, y * pw, flipped, (ph - 1 - y) * pw, pw);
                tex.SetPixels32(flipped);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("screenshot failed: " + e.Message);
                return false;
            }
        }

        private static void SetField(object target, string name, object value)
        {
            var f = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (f == null) throw new Exception($"{target.GetType().Name}.{name} not found");
            f.SetValue(target, value);
        }

        private static float Round(float v) => Mathf.Round(v * 10f) / 10f;
    }
}

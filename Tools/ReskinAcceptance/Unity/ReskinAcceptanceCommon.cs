using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PhotoshopToUnity.EditorImporter;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

// 換皮驗收 harness（只放在 TestProject 用，不屬於 importer 發佈內容，不進 update_manifest.txt）。
// 這裡的檢查刻意「不呼叫」PsUiSkinApplier 的任何程式：它是 applier 內建機械檢查之外的第二道網，
// 兩邊各自實作、各自比對，才抓得到 applier 自己算錯卻自己說通過的情況。
namespace PsUiReskinAcceptance
{
    public static class ReskinFixture
    {
        public const string Root = "Assets/ReskinFixture";
        public const string SpritesFolder = Root + "/Sprites";
        public const string NewSpritesFolder = Root + "/NewSprites";
        public const string PrefabsFolder = Root + "/Prefabs";
        public const string CommonFolder = Root + "/Common";
        public const string ThemePath = Root + "/SkinTheme_Fixture.asset";
        public const string AtlasPath = Root + "/Fixture_Atlas.spriteatlasv2";

        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
        public static string NewArtFolder => ProjectRoot + "/FixtureNewArt";

        public static string OutDir
        {
            get
            {
                var dir = Environment.GetEnvironmentVariable("RESKIN_ACCEPTANCE_OUT");
                if (string.IsNullOrEmpty(dir)) dir = ProjectRoot + "/AcceptanceOut";
                Directory.CreateDirectory(dir);
                return dir.Replace('\\', '/');
            }
        }

        public static string Abs(string assetPath) => ProjectRoot + "/" + assetPath;

        public static void BuildBatch() => BatchRunner.Run("build", Build);

        // 固定內容的測試資料：每個 case 對應一個 P0/P1 問題，內容由程式決定，重跑結果逐位元相同。
        public static void Build()
        {
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            if (Directory.Exists(NewArtFolder)) Directory.Delete(NewArtFolder, true);
            Directory.CreateDirectory(NewArtFolder);
            foreach (var folder in new[] { Root, SpritesFolder, NewSpritesFolder, PrefabsFolder, CommonFolder })
                EnsureFolder(folder);

            var o1 = new Color32(90, 110, 140, 255);
            var o2 = new Color32(200, 200, 210, 255);
            var n1 = new Color32(230, 120, 40, 255);
            var n2 = new Color32(250, 230, 180, 255);
            var b20 = new Vector4(20, 20, 20, 20);

            var btnFeed = MakeSprite(SpritesFolder, "Btn_Feed", 200, 80, b20, o1, o2, 1);
            var panelBg = MakeSprite(SpritesFolder, "Panel_BG", 300, 200, new Vector4(30, 30, 30, 30), o1, o2, 2);
            var iconStar = MakeSprite(SpritesFolder, "Icon_Star", 64, 64, Vector4.zero, o1, o2, 3);
            var iconCoin = MakeSprite(SpritesFolder, "Icon_Coin", 64, 64, Vector4.zero, o1, o2, 4);
            var btnPressed = MakeSprite(SpritesFolder, "Btn_Pressed", 200, 80, b20, o1, o2, 5);
            var banner = MakeSprite(SpritesFolder, "Tex_Banner", 256, 64, Vector4.zero, o1, o2, 6);
            var btnOld = MakeSprite(SpritesFolder, "Btn_Old", 200, 80, b20, o1, o2, 7);
            var deco = MakeSprite(SpritesFolder, "Deco_Line", 240, 12, new Vector4(4, 4, 4, 4), o1, o2, 8);
            var btnClose = MakeSprite(SpritesFolder, "Btn_Close", 64, 64, Vector4.zero, o1, o2, 9);
            var iconSame = MakeSprite(SpritesFolder, "Icon_Same", 32, 32, Vector4.zero, o1, o2, 10);
            var sheetA = MakeSheet(SpritesFolder, "Sheet_UI", o1, o2, 11);
            var iconShared = MakeSprite(SpritesFolder, "Icon_Shared", 48, 48, Vector4.zero, o1, o2, 12);

            var btnSupport = MakeSprite(NewSpritesFolder, "Btn_Support", 200, 80, b20, n1, n2, 21);
            var decoV2 = MakeSprite(NewSpritesFolder, "Deco_Line_v2", 240, 12, Vector4.zero, n1, n2, 22);
            var iconGem = MakeSprite(NewSpritesFolder, "Icon_Gem", 64, 64, Vector4.zero, n1, n2, 23);

            // 新美術（Assets 外）：同尺寸 / 尺寸變了 / 有 border 且尺寸變了 / 內容完全相同 / 缺件。
            WritePng(NewArtFolder + "/Btn_Feed.png", 200, 80, n1, n2, 31);
            WritePng(NewArtFolder + "/Panel_BG.png", 320, 200, n1, n2, 32);
            WritePng(NewArtFolder + "/Icon_Star.png", 72, 72, n1, n2, 33);
            WritePng(NewArtFolder + "/Btn_Pressed.png", 200, 80, n1, n2, 35);
            WritePng(NewArtFolder + "/Tex_Banner.png", 256, 64, n1, n2, 36);
            WritePng(NewArtFolder + "/Sheet_A.png", 64, 64, n1, n2, 37);
            WritePng(NewArtFolder + "/Icon_Shared.png", 48, 48, n1, n2, 38);
            File.Copy(Abs(SpritesFolder + "/Icon_Same.png"), NewArtFolder + "/Icon_Same.png", true);
            // 刻意不提供 Icon_Coin.png（缺件）。

            // ── Prefab ───────────────────────────────────────────────────
            var itemRoot = Node("Item", null);
            Img(Node("ItemBG", itemRoot.transform), btnOld, Image.Type.Sliced);
            Img(Node("ItemIcon", itemRoot.transform), iconStar);
            var itemPrefab = SavePrefab(itemRoot, PrefabsFolder + "/Item.prefab");

            // 範圍外（target 資料夾之外）的共用 prefab，被 Main 巢狀引用。
            var badgeRoot = Node("Shared_Badge", null);
            Img(Node("Badge", badgeRoot.transform), btnOld, Image.Type.Sliced);
            Img(Node("BadgeIcon", badgeRoot.transform), iconShared); // 範圍外也引用同一張圖（CatAct 複製頁情境）
            var badgePrefab = SavePrefab(badgeRoot, CommonFolder + "/Shared_Badge.prefab");

            var main = Node("Main", null);
            main.AddComponent<CanvasGroup>().alpha = 0.9f;
            var t = main.transform;
            Img(Node("BG", t), panelBg, Image.Type.Sliced);

            var btnGo = Node("Btn", t);
            var btnImg = Img(btnGo, btnFeed, Image.Type.Sliced);
            var button = btnGo.AddComponent<Button>();
            button.targetGraphic = btnImg;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState { pressedSprite = btnPressed, highlightedSprite = btnOld };

            Img(Node("Icon", t), iconStar);
            Img(Node("Coin", t), iconCoin);
            Node("Banner", t).AddComponent<RawImage>().texture = banner.texture;
            Img(Node("Deco", t), deco, Image.Type.Sliced);
            var closeGo = Node("Close", t);
            var closeImg = Img(closeGo, btnClose);
            closeGo.AddComponent<Button>().targetGraphic = closeImg;
            Img(Node("SheetIcon", t), sheetA);
            Img(Node("Same", t), iconSame);
            Img(Node("Shared", t), iconShared);

            var scrollGo = Node("Scroll", t);
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 7.5f;
            var viewport = Node("Viewport", scrollGo.transform);
            Img(viewport, btnOld);
            viewport.AddComponent<RectMask2D>();
            var content = Node("Content", viewport.transform);
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = (RectTransform)content.transform;

            var itemInst = (GameObject)PrefabUtility.InstantiatePrefab(itemPrefab);
            itemInst.transform.SetParent(content.transform, false);
            var overriddenIcon = itemInst.transform.Find("ItemIcon").GetComponent<Image>();
            overriddenIcon.sprite = btnOld; // 外層既有 override
            PrefabUtility.RecordPrefabInstancePropertyModifications(overriddenIcon);

            var grid = Node("Grid", t);
            grid.AddComponent<GridLayoutGroup>().cellSize = new Vector2(50, 50);
            Img(Node("Cell", grid.transform), iconStar);
            Node("Row", t).AddComponent<HorizontalLayoutGroup>().spacing = 3;

            var badgeInst = (GameObject)PrefabUtility.InstantiatePrefab(badgePrefab);
            badgeInst.transform.SetParent(t, false);

            var mainPrefab = SavePrefab(main, PrefabsFolder + "/Main.prefab");

            var variantInst = (GameObject)PrefabUtility.InstantiatePrefab(mainPrefab);
            var variantIcon = variantInst.transform.Find("Icon").GetComponent<Image>();
            variantIcon.sprite = btnOld; // Variant 自己的 override
            PrefabUtility.RecordPrefabInstancePropertyModifications(variantIcon);
            PrefabUtility.SaveAsPrefabAsset(variantInst, PrefabsFolder + "/Main_Variant.prefab");
            UnityEngine.Object.DestroyImmediate(variantInst);

            // ── SkinTheme ────────────────────────────────────────────────
            var theme = ScriptableObject.CreateInstance<PsUiSkinTheme>();
            theme.targetPrefabFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(PrefabsFolder);
            theme.sourceArtFolder = NewArtFolder;
            void Entry(Sprite oldSprite, Sprite newSprite) =>
                theme.entries.Add(new SkinThemeEntry { oldSprite = oldSprite, newSprite = newSprite });
            Entry(btnFeed, null);
            Entry(panelBg, null);
            Entry(iconStar, null);
            Entry(iconCoin, null);
            Entry(btnPressed, null);
            Entry(banner, null);
            Entry(sheetA, null);
            Entry(iconSame, null);
            Entry(iconShared, null);
            Entry(btnOld, btnSupport);
            Entry(deco, decoV2);
            Entry(btnClose, iconGem);
            AssetDatabase.CreateAsset(theme, ThemePath);

            var atlas = new SpriteAtlasAsset();
            atlas.Add(new UnityEngine.Object[] { AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(SpritesFolder) });
            SpriteAtlasAsset.Save(atlas, AtlasPath);
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            File.WriteAllText(OutDir + "/fixture_built.txt", DateTime.Now.ToString("s"));
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        public static void WritePng(string absPath, int w, int h, Color32 a, Color32 b, int seed)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color32[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var edge = x < 3 || y < 3 || x >= w - 3 || y >= h - 3;
                    px[y * w + x] = edge ? b : (((x / 8) + (y / 8) + seed) % 2 == 0 ? a : b);
                }
            tex.SetPixels32(px);
            File.WriteAllBytes(absPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        private static Sprite MakeSprite(string folder, string name, int w, int h, Vector4 border, Color32 a, Color32 b, int seed)
        {
            var path = folder + "/" + name + ".png";
            WritePng(Abs(path), w, h, a, b, seed);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spriteBorder = border;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Sprite MakeSheet(string folder, string name, Color32 a, Color32 b, int seed)
        {
            var path = folder + "/" + name + ".png";
            WritePng(Abs(path), 128, 64, a, b, seed);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Multiple;
            ti.mipmapEnabled = false;
#pragma warning disable 618
            ti.spritesheet = new[]
            {
                new SpriteMetaData { name = "Sheet_A", rect = new Rect(0, 0, 64, 64), pivot = new Vector2(0.5f, 0.5f) },
                new SpriteMetaData { name = "Sheet_B", rect = new Rect(64, 0, 64, 64), pivot = new Vector2(0.5f, 0.5f) },
            };
#pragma warning restore 618
            ti.SaveAndReimport();
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().First(s => s.name == "Sheet_A");
        }

        private static GameObject Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private static Image Img(GameObject go, Sprite sprite, Image.Type type = Image.Type.Simple)
        {
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.type = type;
            return img;
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }
    }

    // ── 快照 ────────────────────────────────────────────────────────────
    public sealed class RefRecord
    {
        public string prefab, path, component, property, value;
        public string Key => prefab + "|" + path + "|" + component;
    }

    public sealed class Snapshot
    {
        public readonly Dictionary<string, string> hash = new Dictionary<string, string>();
        public readonly Dictionary<string, string> guid = new Dictionary<string, string>();
        public readonly Dictionary<string, Dictionary<string, string>> fingerprint = new Dictionary<string, Dictionary<string, string>>();
        public readonly Dictionary<string, HashSet<string>> syntheticScrollPaths = new Dictionary<string, HashSet<string>>();
        public readonly List<RefRecord> refs = new List<RefRecord>();

        public static Snapshot Take()
        {
            var s = new Snapshot();
            var rootAbs = ReskinFixture.Abs(ReskinFixture.Root);
            foreach (var file in Directory.GetFiles(rootAbs, "*", SearchOption.AllDirectories))
            {
                var rel = ReskinFixture.Root + file.Substring(rootAbs.Length).Replace('\\', '/');
                s.hash[rel] = Sha256(file);
                if (rel.EndsWith(".meta")) s.guid[rel] = ReadMetaGuid(file);
            }

            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { ReskinFixture.Root }))
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                s.fingerprint[path] = Fingerprint.Of(root, out var synthetic);
                s.syntheticScrollPaths[path] = synthetic;
                CollectRefs(path, root, s.refs);
            }
            return s;
        }

        public string RefValue(string key, string property)
        {
            var r = refs.FirstOrDefault(x => x.Key == key && x.property == property);
            return r?.value;
        }

        private static void CollectRefs(string prefabPath, GameObject root, List<RefRecord> into)
        {
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var p = Fingerprint.HierPath(root.transform, c.transform, false);
                if (c is Image img)
                    into.Add(new RefRecord { prefab = prefabPath, path = p, component = "Image", property = "m_Sprite", value = Key(img.sprite) });
                if (c is Selectable sel)
                {
                    var st = sel.spriteState;
                    var n = sel.GetType().Name;
                    into.Add(new RefRecord { prefab = prefabPath, path = p, component = n + ".highlighted", property = "m_SpriteState.m_HighlightedSprite", value = Key(st.highlightedSprite) });
                    into.Add(new RefRecord { prefab = prefabPath, path = p, component = n + ".pressed", property = "m_SpriteState.m_PressedSprite", value = Key(st.pressedSprite) });
                    into.Add(new RefRecord { prefab = prefabPath, path = p, component = n + ".selected", property = "m_SpriteState.m_SelectedSprite", value = Key(st.selectedSprite) });
                    into.Add(new RefRecord { prefab = prefabPath, path = p, component = n + ".disabled", property = "m_SpriteState.m_DisabledSprite", value = Key(st.disabledSprite) });
                }
                if (c is RawImage raw)
                    into.Add(new RefRecord { prefab = prefabPath, path = p, component = "RawImage", property = "m_Texture", value = raw.texture == null ? null : AssetDatabase.GetAssetPath(raw.texture) });
            }
        }

        public static string Key(Sprite s) => s == null ? null : AssetDatabase.GetAssetPath(s) + "#" + s.name;

        public static string Sha256(string absPath)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(absPath))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        public static string ReadMetaGuid(string metaAbsPath)
        {
            foreach (var line in File.ReadAllLines(metaAbsPath))
                if (line.StartsWith("guid:")) return line.Substring(5).Trim();
            return null;
        }
    }

    // 以 SerializedObject 逐欄位拍 prefab 指紋（含隱藏欄位），浮點數以位元表示，逐位元比對。
    public static class Fingerprint
    {
        public static Dictionary<string, string> Of(GameObject root, out HashSet<string> syntheticScrollPaths)
        {
            var d = new Dictionary<string, string>();
            syntheticScrollPaths = new HashSet<string>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var goPath = HierPath(root.transform, t, true);
                var comps = t.GetComponents<Component>();
                d[goPath + "|#components"] = string.Join(",", comps.Select(c => c == null ? "Missing" : c.GetType().Name));
                Add(d, goPath + "|GameObject", t.gameObject, root);
                var counts = new Dictionary<string, int>();
                foreach (var c in comps)
                {
                    if (c == null) continue;
                    var n = c.GetType().Name;
                    counts.TryGetValue(n, out var i);
                    counts[n] = i + 1;
                    Add(d, goPath + "|" + n + "#" + i, c, root);
                    if (c is ScrollRect sr)
                    {
                        if (sr.viewport != null) syntheticScrollPaths.Add(HierPath(root.transform, sr.viewport, true));
                        if (sr.content != null) syntheticScrollPaths.Add(HierPath(root.transform, sr.content, true));
                    }
                }
            }
            return d;
        }

        public static string HierPath(Transform root, Transform t, bool withIndex)
        {
            var parts = new List<string>();
            for (var cur = t; cur != null; cur = cur.parent)
            {
                parts.Add(withIndex && cur != root ? cur.name + "[" + cur.GetSiblingIndex() + "]" : cur.name);
                if (cur == root) break;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static void Add(Dictionary<string, string> d, string prefix, UnityEngine.Object obj, GameObject root)
        {
            var so = new SerializedObject(obj);
            var it = so.GetIterator();
            var enter = true;
            while (it.Next(enter))
            {
                enter = true;
                var key = prefix + "|" + it.propertyPath;
                switch (it.propertyType)
                {
                    case SerializedPropertyType.String:
                        d[key] = "s:" + it.stringValue;
                        enter = false;
                        break;
                    case SerializedPropertyType.Integer:
                        d[key] = "i:" + it.longValue.ToString(CultureInfo.InvariantCulture);
                        break;
                    case SerializedPropertyType.Boolean:
                        d[key] = "b:" + it.boolValue;
                        break;
                    case SerializedPropertyType.Float:
                        d[key] = "f:" + BitConverter.DoubleToInt64Bits(it.doubleValue).ToString("x16");
                        break;
                    case SerializedPropertyType.Enum:
                    case SerializedPropertyType.LayerMask:
                    case SerializedPropertyType.ArraySize:
                    case SerializedPropertyType.Character:
                        d[key] = "e:" + it.intValue.ToString(CultureInfo.InvariantCulture);
                        break;
                    case SerializedPropertyType.ObjectReference:
                        d[key] = "o:" + RefKey(it.objectReferenceValue, root);
                        enter = false; // 子欄位 m_FileID / m_PathID 是記憶體 instance ID，不可比
                        break;
                    default:
                        if (!it.hasChildren) d[key] = "t:" + it.propertyType;
                        break;
                }
            }
        }

        private static string RefKey(UnityEngine.Object obj, GameObject root)
        {
            if (obj == null) return "null";
            if (obj is Component c && c.transform.IsChildOf(root.transform))
                return "local:" + HierPath(root.transform, c.transform, true) + ":" + c.GetType().Name;
            if (obj is GameObject go && go.transform.IsChildOf(root.transform))
                return "local:" + HierPath(root.transform, go.transform, true);
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out var g, out long id) && !string.IsNullOrEmpty(g))
                return g + ":" + id;
            return "transient:" + obj.GetType().Name;
        }
    }

    // ── 第二道機械檢查（與 applier 各自獨立實作）─────────────────────────────
    public static class IndependentChecks
    {
        private static readonly HashSet<string> ProtectedComponents = new HashSet<string>
        {
            "CanvasGroup", "GridLayoutGroup", "HorizontalLayoutGroup", "VerticalLayoutGroup",
            "ContentSizeFitter", "ScrollRect", "RectMask2D",
        };

        public static JObj Run(Snapshot before, Snapshot after, Dictionary<string, object> report, List<string> consoleErrors)
        {
            var failures = new List<object>();
            var violations = new List<object>();

            // 1. .meta GUID 不變、沒有多出或少掉資產
            var guidStable = true;
            foreach (var kv in before.guid)
            {
                if (!after.guid.TryGetValue(kv.Key, out var g) || g != kv.Value)
                {
                    guidStable = false;
                    failures.Add($"guid changed or meta removed: {kv.Key} {kv.Value} -> {g ?? "(missing)"}");
                }
            }
            foreach (var k in after.guid.Keys.Where(k => !before.guid.ContainsKey(k)))
            {
                guidStable = false;
                failures.Add($"unexpected new asset meta: {k}");
            }

            // 2. 不可碰清單 + 「換皮只換 Sprite 參照」：除允許欄位外，prefab 指紋必須逐位元相同
            foreach (var kv in before.fingerprint)
            {
                if (!after.fingerprint.TryGetValue(kv.Key, out var fpAfter))
                {
                    violations.Add(new JObj { { "prefab", kv.Key }, { "kind", "prefabRemoved" } });
                    continue;
                }
                var synthetic = before.syntheticScrollPaths[kv.Key];
                foreach (var key in kv.Value.Keys.Union(fpAfter.Keys))
                {
                    kv.Value.TryGetValue(key, out var a);
                    fpAfter.TryGetValue(key, out var b);
                    if (a == b || IsAllowedSpriteField(key)) continue;
                    var parts = key.Split('|');
                    var comp = parts.Length > 1 ? parts[1].Split('#')[0] : "";
                    var isProtected = ProtectedComponents.Contains(comp) || synthetic.Contains(parts[0]) ||
                                      (comp == "RectTransform" && synthetic.Any(s => parts[0] == s));
                    violations.Add(new JObj
                    {
                        { "prefab", kv.Key }, { "field", key }, { "before", a }, { "after", b },
                        { "kind", isProtected ? "protectedComponent" : "unexpectedChange" },
                    });
                }
            }

            // 3. summary 與 items 逐筆統計一致
            var items = (report.TryGetValue("items", out var it) ? it as List<object> : null) ?? new List<object>();
            var summary = report.TryGetValue("summary", out var sm) ? sm as Dictionary<string, object> : null;
            var recount = Recount(items);
            var summaryConsistent = summary != null;
            if (summary != null)
            {
                foreach (var kv in recount)
                {
                    var claimed = summary.TryGetValue(kv.Key, out var v) ? Convert.ToInt32(v) : -1;
                    if (claimed != kv.Value)
                    {
                        summaryConsistent = false;
                        failures.Add($"summary.{kv.Key}={claimed} but items give {kv.Value}");
                    }
                }
            }
            else failures.Add("report has no summary");

            // 4. hash / 觀察值與 status 一致
            var hashConsistent = true;
            var claimedWrittenPrefabs = new HashSet<string>();
            foreach (var o in items)
            {
                var item = (Dictionary<string, object>)o;
                var action = Str(item, "action");
                var status = Str(item, "status");
                var oldSprite = Str(item, "oldSprite");
                var target = Str(item, "targetAsset");
                var usages = (item.TryGetValue("usedBy", out var u) ? u as List<object> : null) ?? new List<object>();

                if (action == "fileOverwrite" || (action == "skipped" && !string.IsNullOrEmpty(target)))
                {
                    if (string.IsNullOrEmpty(target) || !before.hash.ContainsKey(target))
                    {
                        if (status == "ok") { hashConsistent = false; failures.Add($"{oldSprite}: ok fileOverwrite without a known targetAsset"); }
                        continue;
                    }
                    var changed = before.hash[target] != after.hash[target];
                    if (status == "ok" && action == "fileOverwrite")
                    {
                        var src = Str(item, "newSource");
                        var srcHash = !string.IsNullOrEmpty(src) && File.Exists(src) ? Snapshot.Sha256(src) : null;
                        if (!changed) { hashConsistent = false; failures.Add($"{oldSprite}: status ok but file hash unchanged"); }
                        else if (srcHash != after.hash[target]) { hashConsistent = false; failures.Add($"{oldSprite}: status ok but file content != newSource"); }
                        if (item.TryGetValue("borderValidAfter", out var bv) && bv is bool bvb && !bvb)
                        {
                            hashConsistent = false;
                            failures.Add($"{oldSprite}: status ok although borderValidAfter=false (9-slice broken)");
                        }
                    }
                    else if (changed)
                    {
                        hashConsistent = false;
                        failures.Add($"{oldSprite}: status {status} but file hash changed");
                    }
                    continue;
                }

                if (action == "refSwap" || action == "skipped")
                {
                    var newSource = Str(item, "newSource");
                    foreach (var uo in usages)
                    {
                        var usage = (Dictionary<string, object>)uo;
                        var key = Str(usage, "prefab") + "|" + Str(usage, "path") + "|" + Str(usage, "component");
                        var prop = PropertyFor(Str(usage, "component"));
                        var observed = after.RefValue(key, prop);
                        var original = before.RefValue(key, prop);
                        var expectedNew = Str(usage, "component") == "RawImage" ? TexturePathOf(newSource) : newSource;
                        var replaced = observed == expectedNew && observed != original;
                        var written = usage.TryGetValue("writtenHere", out var w) ? w as bool? : null;
                        if (status == "ok" && action == "refSwap")
                        {
                            if (!replaced)
                            {
                                hashConsistent = false;
                                failures.Add($"{oldSprite}: status ok but {key} still references {observed}");
                            }
                            var claimedApplied = usage.TryGetValue("applied", out var ap) && ap is bool apb && apb;
                            if (claimedApplied && written != false) claimedWrittenPrefabs.Add(Str(usage, "prefab"));
                        }
                        else if (observed != original)
                        {
                            hashConsistent = false;
                            failures.Add($"{oldSprite}: status {status} but {key} changed {original} -> {observed}");
                        }
                    }
                }
            }
            foreach (var p in before.fingerprint.Keys)
            {
                var changed = before.hash[p] != after.hash[p];
                if (claimedWrittenPrefabs.Contains(p) && !changed)
                {
                    hashConsistent = false;
                    failures.Add($"{p}: report claims sprite writes but prefab file hash unchanged");
                }
                if (!claimedWrittenPrefabs.Contains(p) && changed)
                {
                    hashConsistent = false;
                    failures.Add($"{p}: prefab file changed but no ok usage claims a write there");
                }
            }

            var contractClean = violations.Count == 0;
            var consoleClean = consoleErrors == null || consoleErrors.Count == 0;
            if (!consoleClean) failures.AddRange(consoleErrors.Select(e => (object)("console error: " + e)));
            return new JObj
            {
                { "passed", guidStable && contractClean && summaryConsistent && hashConsistent && consoleClean },
                { "guidStable", guidStable },
                { "contractClean", contractClean },
                { "summaryConsistent", summaryConsistent },
                { "hashConsistent", hashConsistent },
                { "consoleClean", consoleClean },
                { "summaryRecount", new JObj(recount.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value))) },
                { "violations", violations },
                { "failures", failures },
            };
        }

        public static Dictionary<string, int> Recount(List<object> items)
        {
            int files = 0, sprites = 0, missing = 0, blocked = 0, skipped = 0;
            var prefabs = new HashSet<string>();
            foreach (var o in items)
            {
                var item = (Dictionary<string, object>)o;
                var action = Str(item, "action");
                var status = Str(item, "status");
                if (status == "missing") missing++;
                if (status == "blocked") blocked++;
                if (action == "skipped" || status == "skipped") skipped++;
                if (status != "ok") continue;
                if (action == "fileOverwrite") files++;
                if (action != "refSwap") continue;
                foreach (var uo in (item.TryGetValue("usedBy", out var u) ? u as List<object> : null) ?? new List<object>())
                {
                    var usage = (Dictionary<string, object>)uo;
                    var applied = usage.TryGetValue("applied", out var a) && a is bool ab && ab;
                    var written = usage.TryGetValue("writtenHere", out var w) ? w as bool? : null;
                    if (!applied || written == false) continue;
                    sprites++;
                    prefabs.Add(Str(usage, "prefab"));
                }
            }
            return new Dictionary<string, int>
            {
                { "filesOverwritten", files }, { "spritesReplaced", sprites }, { "prefabsChanged", prefabs.Count },
                { "missing", missing }, { "blocked", blocked }, { "skipped", skipped },
            };
        }

        private static bool IsAllowedSpriteField(string key)
        {
            var parts = key.Split('|');
            if (parts.Length < 3) return false;
            var comp = parts[1].Split('#')[0];
            var prop = parts[2];
            return (comp == "Image" && prop == "m_Sprite") ||
                   prop.StartsWith("m_SpriteState.", StringComparison.Ordinal) ||
                   (comp == "RawImage" && prop == "m_Texture");
        }

        public static string PropertyFor(string component)
        {
            if (component == "Image") return "m_Sprite";
            if (component == "RawImage") return "m_Texture";
            var dot = component.LastIndexOf('.');
            var state = dot >= 0 ? component.Substring(dot + 1) : "";
            return "m_SpriteState.m_" + char.ToUpperInvariant(state[0]) + state.Substring(1) + "Sprite";
        }

        private static string TexturePathOf(string spriteKey)
        {
            if (string.IsNullOrEmpty(spriteKey)) return spriteKey;
            var i = spriteKey.IndexOf('#');
            return i < 0 ? spriteKey : spriteKey.Substring(0, i);
        }

        public static string Str(Dictionary<string, object> d, string k) =>
            d.TryGetValue(k, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
    }

    // ── 批次執行外殼：收 Console error、結束碼 ───────────────────────────────
    public static class BatchRunner
    {
        public static readonly List<string> ConsoleErrors = new List<string>();

        public static void Run(string stage, Action body)
        {
            Application.logMessageReceived += OnLog;
            var code = 0;
            try
            {
                body();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                File.WriteAllText(ReskinFixture.OutDir + "/" + stage + "_exception.txt", e.ToString());
                code = 1;
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                ConsoleErrors.Add(condition);
        }

        public static Dictionary<string, object> Parse(string json) =>
            (Dictionary<string, object>)SimpleJsonReader.Parse(json);
    }

    // ── 極小 JSON 寫出（保留欄位順序）─────────────────────────────────────
    public sealed class JObj : List<KeyValuePair<string, object>>
    {
        public JObj() { }
        public JObj(IEnumerable<KeyValuePair<string, object>> items) : base(items) { }
        public void Add(string key, object value) => Add(new KeyValuePair<string, object>(key, value));

        public object this[string key]
        {
            get { foreach (var kv in this) if (kv.Key == key) return kv.Value; return null; }
            set
            {
                for (var i = 0; i < Count; i++)
                    if (this[i].Key == key) { this[i] = new KeyValuePair<string, object>(key, value); return; }
                Add(key, value);
            }
        }

        public string ToJson() { var sb = new StringBuilder(); Write(sb, this, 0); return sb.ToString(); }

        public static void Write(StringBuilder sb, object v, int indent)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case string s: WriteString(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); return;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); return;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return;
                case JObj o:
                    sb.Append("{");
                    for (var k = 0; k < o.Count; k++)
                    {
                        sb.Append(k == 0 ? "\n" : ",\n").Append(' ', (indent + 1) * 2);
                        WriteString(sb, o[k].Key);
                        sb.Append(": ");
                        Write(sb, o[k].Value, indent + 1);
                    }
                    if (o.Count > 0) sb.Append("\n").Append(' ', indent * 2);
                    sb.Append("}");
                    return;
                case Dictionary<string, object> dict:
                    Write(sb, new JObj(dict), indent);
                    return;
                case System.Collections.IEnumerable list:
                    sb.Append("[");
                    var first = true;
                    foreach (var e in list)
                    {
                        sb.Append(first ? "" : ", ");
                        first = false;
                        Write(sb, e, indent + 1);
                    }
                    sb.Append("]");
                    return;
                default:
                    WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
                    return;
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}

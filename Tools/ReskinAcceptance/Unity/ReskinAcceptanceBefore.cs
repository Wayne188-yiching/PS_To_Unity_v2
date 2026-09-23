using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PhotoshopToUnity.EditorImporter;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// BEFORE：v2.15.0 沒有任何 JSON 報告，只有 Result 結構（三個計數 + 混雜的 missingFiles 字串）。
// 這裡把 legacy 的「宣稱」原樣放進 status / summary，另外由觀察器量測事實（尺寸、border、每處引用
// 實際有沒有被換）放進同一份 schema，讓 BEFORE / AFTER 可以逐欄對照。觀察器不替 legacy 修飾結論。
namespace PsUiReskinAcceptance
{
    public static class ReskinAcceptanceBefore
    {
        public static void RunThemeBatch() => BatchRunner.Run("before_theme", RunTheme);
        public static void RunFolderBatch() => BatchRunner.Run("before_folder", RunFolder);

        private sealed class PreFacts
        {
            public string oldPath, newSource;
            public int[] oldSize, newSize, border, newBorder;
            public bool anySliced;
        }

        public static void RunTheme()
        {
            AssetDatabase.Refresh();
            var theme = AssetDatabase.LoadAssetAtPath<PsUiSkinTheme>(ReskinFixture.ThemePath);
            var before = Snapshot.Take();
            var facts = theme.entries.Select(e => Measure(e, theme.sourceArtFolder, before)).ToList();

            var r = PsUiSkinApplierLegacy.Apply(theme);
            AssetDatabase.Refresh();
            var after = Snapshot.Take();

            var items = new List<object>();
            for (var i = 0; i < theme.entries.Count; i++)
            {
                var e = theme.entries[i];
                var f = facts[i];
                var sameName = e.newSprite == null ||
                               string.Equals(e.oldSprite.name, e.newSprite.name, StringComparison.OrdinalIgnoreCase);
                var legacyMsg = r.missingFiles.FirstOrDefault(m => m.StartsWith(e.oldSprite.name, StringComparison.Ordinal));
                var oldKey = Snapshot.Key(e.oldSprite);
                var item = new JObj
                {
                    { "oldSprite", oldKey },
                    { "action", sameName ? "fileOverwrite" : "refSwap" },
                    { "newSource", f.newSource },
                    { "status", legacyMsg != null ? "missing" : "ok" },
                    { "reason", legacyMsg ?? "" },
                    { "oldSize", f.oldSize }, { "newSize", f.newSize }, { "border", f.border },
                    { "borderValidAfter", BorderValidAfter(sameName, f) },
                    { "targetAsset", sameName ? f.oldPath : null },
                };
                if (!sameName) item.Add("newBorder", f.newBorder);

                var fileChanged = sameName && before.hash.ContainsKey(f.oldPath) && before.hash[f.oldPath] != after.hash[f.oldPath];
                var usedBy = new List<object>();
                foreach (var rec in before.refs.Where(x => x.value == oldKey ||
                                                           (x.component == "RawImage" && x.value == f.oldPath && WholeTexture(e.oldSprite))))
                {
                    var observed = after.RefValue(rec.Key, rec.property);
                    var expected = rec.component == "RawImage" && e.newSprite != null
                        ? AssetDatabase.GetAssetPath(e.newSprite.texture)
                        : Snapshot.Key(e.newSprite);
                    usedBy.Add(new JObj
                    {
                        { "prefab", rec.prefab }, { "path", rec.path }, { "component", rec.component },
                        { "applied", sameName ? fileChanged : observed == expected },
                        { "writtenHere", sameName ? (object)false : null },
                        { "observedAfter", observed },
                    });
                }
                item.Add("usedBy", usedBy);
                items.Add(item);
            }

            var report = new JObj
            {
                { "toolVersion", "2.15.0 (legacy PsUiSkinApplier, observer-wrapped)" },
                { "mode", "apply" },
                { "reportOrigin", "v2.15.0 has no dry-run and no JSON report; status/summary are the legacy Result claims, sizes/border/usedBy.applied are measured by the acceptance observer" },
                { "flow", "skinTheme" },
                { "targetFolder", AssetDatabase.GetAssetPath(theme.targetPrefabFolderAsset) },
                { "sourceArtFolder", theme.sourceArtFolder },
                { "items", items },
                { "protectedContract", new JObj { { "checked", false }, { "violations", new List<object>() } } },
                { "legacyMissingFiles", r.missingFiles },
                { "summary", new JObj
                    {
                        { "filesOverwritten", r.filesOverwritten }, { "spritesReplaced", r.spritesReplaced },
                        { "prefabsChanged", r.prefabsChanged }, { "missing", r.missingFiles.Count },
                        { "blocked", 0 }, { "skipped", 0 },
                    }
                },
            };
            Finish(report, before, after, "before_theme_report.json");
        }

        public static void RunFolder()
        {
            AssetDatabase.Refresh();
            var before = Snapshot.Take();
            var window = ScriptableObject.CreateInstance<PhotoshopUiImporterWindow>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(PhotoshopUiImporterWindow);
            type.GetField("reskinArtSourceFolder", flags).SetValue(window, ReskinFixture.NewArtFolder);
            type.GetField("reskinTargetFolder", flags).SetValue(window, ReskinFixture.SpritesFolder);
            type.GetMethod("ScanReskin", flags).Invoke(window, null);
            var pending = new List<string>((List<string>)type.GetField("reskinPendingOverwrites", flags).GetValue(window));
            var missing = new List<string>((List<string>)type.GetField("reskinMissingFiles", flags).GetValue(window));
            var facts = pending.Concat(missing).ToDictionary(n => n, n => Measure(ReskinFixture.SpritesFolder + "/" + n, ReskinFixture.NewArtFolder));
            var status = (string)type.GetField("statusMessage", flags).GetValue(window);
            // ApplyReskinOverwrites 第一行是 DisplayDialog，batchmode 會自動取消而什麼都不做。
            // 以下逐行照抄 v2.15.0 對話框之後的本體（File.Copy overwrite:true + Refresh），不加任何檢查。
            var targetAbsPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReskinFixture.SpritesFolder));
            foreach (var fileName in pending)
            {
                var sourcePath = Path.Combine(ReskinFixture.NewArtFolder, fileName);
                var targetPath = Path.Combine(targetAbsPath, fileName);
                if (File.Exists(sourcePath) && File.Exists(targetPath))
                    File.Copy(sourcePath, targetPath, overwrite: true);
            }
            AssetDatabase.Refresh();
            UnityEngine.Object.DestroyImmediate(window);
            AssetDatabase.Refresh();
            var after = Snapshot.Take();

            var items = new List<object>();
            foreach (var name in pending.Concat(missing))
            {
                var f = facts[name];
                var isMissing = missing.Contains(name);
                items.Add(new JObj
                {
                    { "oldSprite", f.oldPath }, { "action", "fileOverwrite" }, { "newSource", isMissing ? null : f.newSource },
                    { "status", isMissing ? "missing" : "ok" }, { "reason", "" },
                    { "oldSize", f.oldSize }, { "newSize", f.newSize }, { "border", f.border },
                    { "borderValidAfter", BorderValidAfter(true, f) }, { "targetAsset", f.oldPath },
                    { "usedBy", new List<object>() },
                });
            }
            var report = new JObj
            {
                { "toolVersion", "2.15.0 (legacy window ScanReskin/ApplyReskinOverwrites, observer-wrapped)" },
                { "mode", "apply" },
                { "reportOrigin", "v2.15.0 folder flow keeps only two name lists (pending / missing); no sizes, no usage, no report file" },
                { "flow", "folder" },
                { "targetFolder", ReskinFixture.SpritesFolder },
                { "sourceArtFolder", ReskinFixture.NewArtFolder },
                { "items", items },
                { "protectedContract", new JObj { { "checked", false }, { "violations", new List<object>() } } },
                { "legacyStatusMessage", status },
                { "summary", new JObj
                    {
                        { "filesOverwritten", pending.Count }, { "spritesReplaced", 0 }, { "prefabsChanged", 0 },
                        { "missing", missing.Count }, { "blocked", 0 }, { "skipped", 0 },
                    }
                },
            };
            Finish(report, before, after, "before_folder_report.json");
        }

        private static void Finish(JObj report, Snapshot before, Snapshot after, string fileName)
        {
            var parsed = BatchRunner.Parse(report.ToJson());
            report.Add("externalChecks", IndependentChecks.Run(before, after, parsed, BatchRunner.ConsoleErrors));
            File.WriteAllText(ReskinFixture.OutDir + "/" + fileName, report.ToJson());
        }

        private static object BorderValidAfter(bool fileOverwrite, PreFacts f)
        {
            if (fileOverwrite)
            {
                if (f.newSize == null) return null;
                var hasBorder = f.border != null && f.border.Any(v => v > 0);
                return !hasBorder || (f.newSize[0] == f.oldSize[0] && f.newSize[1] == f.oldSize[1]);
            }
            var newHasBorder = f.newBorder != null && f.newBorder.Any(v => v > 0);
            return !f.anySliced || newHasBorder;
        }

        private static PreFacts Measure(SkinThemeEntry e, string sourceDir, Snapshot before)
        {
            var f = new PreFacts { oldPath = AssetDatabase.GetAssetPath(e.oldSprite) };
            var sameName = e.newSprite == null ||
                           string.Equals(e.oldSprite.name, e.newSprite.name, StringComparison.OrdinalIgnoreCase);
            if (sameName)
            {
                var ext = Path.GetExtension(f.oldPath);
                var src = Path.Combine(sourceDir, e.oldSprite.name + ext).Replace('\\', '/');
                if (!File.Exists(src)) src = Path.Combine(sourceDir, e.oldSprite.name + ".png").Replace('\\', '/');
                f.newSource = src;
                f.oldSize = new[] { (int)e.oldSprite.rect.width, (int)e.oldSprite.rect.height };
                f.newSize = File.Exists(src) ? PngSize(src) : null;
                var b = e.oldSprite.border;
                f.border = new[] { (int)b.x, (int)b.y, (int)b.z, (int)b.w };
            }
            else
            {
                f.newSource = Snapshot.Key(e.newSprite);
                f.oldSize = new[] { (int)e.oldSprite.rect.width, (int)e.oldSprite.rect.height };
                f.newSize = new[] { (int)e.newSprite.rect.width, (int)e.newSprite.rect.height };
                var b = e.oldSprite.border;
                f.border = new[] { (int)b.x, (int)b.y, (int)b.z, (int)b.w };
                var nb = e.newSprite.border;
                f.newBorder = new[] { (int)nb.x, (int)nb.y, (int)nb.z, (int)nb.w };
                f.anySliced = UsesSliced(Snapshot.Key(e.oldSprite));
            }
            return f;
        }

        private static PreFacts Measure(string assetPath, string sourceDir)
        {
            var ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            var b = ti != null ? ti.spriteBorder : Vector4.zero;
            var src = Path.Combine(sourceDir, Path.GetFileName(assetPath)).Replace('\\', '/');
            return new PreFacts
            {
                oldPath = assetPath,
                newSource = src,
                oldSize = PngSize(ReskinFixture.Abs(assetPath)),
                newSize = File.Exists(src) ? PngSize(src) : null,
                border = new[] { (int)b.x, (int)b.y, (int)b.z, (int)b.w },
            };
        }

        private static bool UsesSliced(string spriteKey)
        {
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { ReskinFixture.Root }))
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                foreach (var img in root.GetComponentsInChildren<Image>(true))
                    if (Snapshot.Key(img.sprite) == spriteKey &&
                        (img.type == Image.Type.Sliced || img.type == Image.Type.Tiled))
                        return true;
            }
            return false;
        }

        private static bool WholeTexture(Sprite s) =>
            s != null && s.texture != null && (int)s.rect.width == s.texture.width && (int)s.rect.height == s.texture.height;

        public static int[] PngSize(string absPath)
        {
            var bytes = File.ReadAllBytes(absPath);
            if (bytes.Length < 24) return null;
            int w = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            int h = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            return new[] { w, h };
        }
    }
}

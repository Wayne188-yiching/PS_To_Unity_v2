using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using PhotoshopToUnity.EditorImporter;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// v2.19「舊版來源（唯讀）＋要換皮的資料夾」驗收（CatAct → LiveRoom 的縮小版）：
//   1. 節點配對：兩邊同一節點，要換皮那份已手動換上新圖 → 學到舊→新；已填值和配對不同 → 列 conflicting、保留原值
//   2. 預覽／執行只寫要換皮的資料夾：舊版來源資料夾所有檔案（含 .meta）逐位元不變，目標其餘節點換成新圖
//   3. 同名檔案覆蓋的目標檔位在舊版來源內 → blocked（sourceFolderReadOnly）
//   4. 舊格式（target＝舊版、reference＝新版）→ 不預覽、不配對；轉換後 source／target 對調
//   5. 舊版來源與要換皮的資料夾相同或互相包含 → 不預覽
namespace PsUiReskinAcceptance
{
    public static class ReskinSourceFolderAcceptance
    {
        private const string Root = "Assets/SourceFolderFixture";
        private const string OldFolder = Root + "/CatAct";
        private const string OldArt = OldFolder + "/Atlas";
        private const string NewFolder = Root + "/LiveRoom";
        private const string NewArt = NewFolder + "/Atlas";

        private static string NewArtOutsideAssets => ReskinFixture.ProjectRoot + "/SourceFolderNewArt";

        public static void RunBatch() => BatchRunner.Run("source_folder", Run);

        public static void Run()
        {
            AssetDatabase.Refresh();
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            if (Directory.Exists(NewArtOutsideAssets)) Directory.Delete(NewArtOutsideAssets, true);
            var logs = ReskinFixture.ProjectRoot + "/Logs/PsUiReskin";
            if (Directory.Exists(logs)) Directory.Delete(logs, true);
            foreach (var f in new[] { Root, OldFolder, OldArt, NewFolder, NewArt }) EnsureFolder(f);

            var gray = new Color32(90, 110, 140, 255);
            var light = new Color32(200, 200, 210, 255);
            var warm = new Color32(230, 120, 40, 255);
            var cream = new Color32(250, 230, 180, 255);
            var iconOld = MakeSprite(OldArt, "Icon_Old", 64, 64, gray, light, 1);
            var frameOld = MakeSprite(OldArt, "Frame_Old", 100, 40, gray, light, 2);
            var tagOld = MakeSprite(OldArt, "Tag_Old", 48, 48, gray, light, 3);
            var extraOld = MakeSprite(OldArt, "Extra_Old", 32, 32, gray, light, 4);
            var iconNew = MakeSprite(NewArt, "Icon_New", 70, 70, warm, cream, 11);
            var tagNew = MakeSprite(NewArt, "Tag_New", 50, 50, warm, cream, 12);
            var tagWrong = MakeSprite(NewArt, "Tag_WrongPick", 50, 50, cream, warm, 13);

            // 舊版：所有節點都是舊圖。複製版：Icon、Tag 已手動換新圖，其餘仍是舊圖，另多一個複製版才有的節點。
            MakePrefab(OldFolder + "/CatActPanel.prefab", "CatActPanel",
                ("Icon", iconOld), ("IconCopy", iconOld), ("Frame", frameOld), ("Tag", tagOld), ("TagCopy", tagOld));
            MakePrefab(NewFolder + "/LiveRoomPanel.prefab", "LiveRoomPanel",
                ("Icon", iconNew), ("IconCopy", iconOld), ("Frame", frameOld), ("Tag", tagNew), ("TagCopy", tagOld), ("Extra", extraOld));

            Directory.CreateDirectory(NewArtOutsideAssets);
            ReskinFixture.WritePng(NewArtOutsideAssets + "/Extra_Old.png", 32, 32, warm, cream, 21);

            var theme = ScriptableObject.CreateInstance<PsUiSkinTheme>();
            theme.targetPrefabFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(NewFolder);
            theme.sourcePrefabFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(OldFolder);
            theme.sourceArtFolder = NewArtOutsideAssets;
            theme.entries.Add(new SkinThemeEntry { oldSprite = tagOld, newSprite = tagWrong }); // 模擬先前依尺寸挑錯的值
            theme.entries.Add(new SkinThemeEntry { oldSprite = extraOld });                      // 同名覆蓋，但檔案在舊版來源內
            AssetDatabase.CreateAsset(theme, Root + "/SkinTheme_SourceFolder.asset");
            AssetDatabase.SaveAssets();

            var results = new JObj();
            var failures = new List<string>();
            void Check(string name, bool ok, object detail)
            {
                results.Add(name, new JObj { { "passed", ok }, { "detail", detail } });
                if (!ok) failures.Add(name);
            }

            var oldBefore = HashTree(OldFolder);

            // 1. 節點配對
            var match = PsUiSkinApplier.AutoFillFromPrefabPairs(theme);
            var iconEntry = theme.entries.FirstOrDefault(e => e.oldSprite == iconOld);
            var tagEntry = theme.entries.FirstOrDefault(e => e.oldSprite == tagOld);
            Check("pairLearnsHandSwappedNode", iconEntry != null && iconEntry.newSprite == iconNew,
                $"filled={match.filled} Icon_Old->{(iconEntry?.newSprite != null ? iconEntry.newSprite.name : "null")}");
            Check("pairKeepsAndReportsConflictingValue", match.conflicting == 1 && tagEntry != null && tagEntry.newSprite == tagWrong,
                $"conflicting={match.conflicting} Tag_Old->{(tagEntry?.newSprite != null ? tagEntry.newSprite.name : "null")} notes={string.Join(" / ", match.notes)}");
            Check("pairCountsUnchangedNode", match.unchanged == 1, $"unchanged={match.unchanged}");

            // 2 + 3. 預覽 → 執行
            var plan = PsUiSkinApplier.PlanTheme(theme);
            File.Copy(PsUiSkinApplier.ReportPath, ReskinFixture.OutDir + "/source_folder_dryrun.json", true);
            var iconItem = plan.items.FirstOrDefault(i => i.action == PsUiSkinApplier.ActionRefSwap && i.oldSprite != null && i.oldSprite.Contains("/Icon_Old.png#"));
            var extraItem = plan.items.FirstOrDefault(i => i.oldSprite != null && i.oldSprite.Contains("/Extra_Old.png#"));
            Check("planComplete", plan.planComplete, string.Join(" / ", plan.errors));
            Check("reportNamesSourceFolder", plan.sourcePrefabFolder == OldFolder, plan.sourcePrefabFolder);
            Check("refSwapOnlyInTarget", iconItem != null && iconItem.status == PsUiSkinApplier.StatusOk &&
                                          iconItem.usedBy.Count > 0 && iconItem.usedBy.All(u => u.prefab.StartsWith(NewFolder + "/")),
                iconItem == null ? "no item" : string.Join(", ", iconItem.usedBy.Select(u => u.prefab + ":" + u.path)));
            Check("fileOverwriteInsideSourceBlocked", extraItem != null && extraItem.status == PsUiSkinApplier.StatusBlocked &&
                                                      extraItem.reasonCode == "sourceFolderReadOnly",
                extraItem == null ? "no item" : extraItem.status + "/" + extraItem.reasonCode);

            var applied = PsUiSkinApplier.Execute(plan);
            File.Copy(PsUiSkinApplier.ReportPath, ReskinFixture.OutDir + "/source_folder_apply.json", true);
            AssetDatabase.Refresh();
            Check("applyCompletedAndChecksPassed", applied.execution != null && applied.execution.completed && applied.checks != null && applied.checks.passed,
                applied.execution?.reason + " " + string.Join(" / ", applied.checks?.failures ?? new List<string>()));
            var oldAfter = HashTree(OldFolder);
            var changedInSource = oldBefore.Where(kv => !oldAfter.TryGetValue(kv.Key, out var h) || h != kv.Value).Select(kv => kv.Key)
                .Concat(oldAfter.Keys.Except(oldBefore.Keys)).ToList();
            Check("sourceFolderByteIdentical", changedInSource.Count == 0, changedInSource);
            var live = AssetDatabase.LoadAssetAtPath<GameObject>(NewFolder + "/LiveRoomPanel.prefab");
            Check("targetOtherNodeNowNewArt", SpriteAt(live, "IconCopy") == iconNew, SpriteAt(live, "IconCopy")?.name);
            Check("targetUnmappedNodeUntouched", SpriteAt(live, "Frame") == frameOld, SpriteAt(live, "Frame")?.name);

            // 4. 舊格式：target＝舊版、reference＝新版
            var legacy = ScriptableObject.CreateInstance<PsUiSkinTheme>();
            legacy.targetPrefabFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(OldFolder);
            legacy.referencePrefabFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(NewFolder);
            legacy.entries.Add(new SkinThemeEntry { oldSprite = iconOld, newSprite = iconNew });
            var legacyPlan = PsUiSkinApplier.PlanTheme(legacy);
            var legacyMatch = PsUiSkinApplier.AutoFillFromPrefabPairs(legacy);
            Check("legacyNotPlanned", PsUiSkinApplier.HasLegacyReference(legacy) && !legacyPlan.planComplete &&
                                      legacyPlan.Executable.Count() == 0 && legacyPlan.errors.Contains(PsUiSkinApplier.LegacyReferenceMessage),
                string.Join(" / ", legacyPlan.errors));
            Check("legacyNotPaired", legacyMatch.filled == 0 && legacyMatch.notes.Contains(PsUiSkinApplier.LegacyReferenceMessage),
                string.Join(" / ", legacyMatch.notes));
            PsUiSkinApplier.MigrateLegacyReference(legacy);
            Check("legacyMigrationSwapsFolders",
                AssetDatabase.GetAssetPath(legacy.sourcePrefabFolderAsset) == OldFolder &&
                AssetDatabase.GetAssetPath(legacy.targetPrefabFolderAsset) == NewFolder && legacy.referencePrefabFolderAsset == null,
                $"source={AssetDatabase.GetAssetPath(legacy.sourcePrefabFolderAsset)} target={AssetDatabase.GetAssetPath(legacy.targetPrefabFolderAsset)}");

            // 5. 舊版來源與目標相同／互相包含
            var same = ScriptableObject.CreateInstance<PsUiSkinTheme>();
            same.targetPrefabFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(NewFolder);
            same.sourcePrefabFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(NewFolder);
            same.entries.Add(new SkinThemeEntry { oldSprite = iconOld, newSprite = iconNew });
            var samePlan = PsUiSkinApplier.PlanTheme(same);
            same.sourcePrefabFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(Root);
            var nestedPlan = PsUiSkinApplier.PlanTheme(same);
            Check("sameOrNestedFoldersNotPlanned", !samePlan.planComplete && !nestedPlan.planComplete,
                string.Join(" / ", samePlan.errors.Concat(nestedPlan.errors)));

            Check("noConsoleErrors", BatchRunner.ConsoleErrors.Count == 0, BatchRunner.ConsoleErrors);
            Check("sourceFolderStillIdenticalAtEnd", HashTree(OldFolder).SequenceEqual(oldBefore), null);

            results.Add("passed", failures.Count == 0);
            results.Add("failures", failures);
            File.WriteAllText(ReskinFixture.OutDir + "/source_folder_results.json", results.ToJson());
            if (failures.Count > 0)
                throw new Exception("source folder acceptance failed: " + string.Join(", ", failures));
        }

        private static Sprite SpriteAt(GameObject root, string child)
        {
            var t = root == null ? null : root.transform.Find(child);
            return t == null ? null : t.GetComponent<Image>().sprite;
        }

        private static void MakePrefab(string path, string rootName, params (string name, Sprite sprite)[] children)
        {
            var root = new GameObject(rootName, typeof(RectTransform));
            foreach (var (name, sprite) in children)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(root.transform, false);
                go.GetComponent<Image>().sprite = sprite;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static Sprite MakeSprite(string folder, string name, int w, int h, Color32 a, Color32 b, int seed)
        {
            var path = folder + "/" + name + ".png";
            ReskinFixture.WritePng(ReskinFixture.Abs(path), w, h, a, b, seed);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // 資料夾本身的 .meta 加上底下每個檔案（含 .meta）的 SHA-256，依路徑排序。
        private static SortedDictionary<string, string> HashTree(string folder)
        {
            var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var abs = ReskinFixture.Abs(folder);
            var files = Directory.GetFiles(abs, "*", SearchOption.AllDirectories).Append(abs + ".meta");
            using (var sha = SHA256.Create())
                foreach (var f in files)
                    result[f.Replace('\\', '/').Substring(ReskinFixture.ProjectRoot.Length + 1)] =
                        BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(f))).Replace("-", "");
            return result;
        }
    }
}

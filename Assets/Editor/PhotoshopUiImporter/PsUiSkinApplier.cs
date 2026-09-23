using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace PhotoshopToUnity.EditorImporter
{
    // ── reskin flow 不可碰清單（OPTIMIZATION_PLAN_zh.html#phase4-decisions Q3 / #phase4-5-q10）──────────
    // 以下由 full import（UGuiTmpPrefabBackend）視為 source of truth，reskin 完全不觸碰：
    //   * CanvasGroup（含 root 自動掛的那個）
    //   * GridLayoutGroup（layoutType == "grid" 的節點）
    //   * HorizontalLayoutGroup / VerticalLayoutGroup 的參數（含 padding / spacing / childControl 等）
    //   * ContentSizeFitter
    //   * ScrollRect（含手感參數 scrollSensitivity / movementType —— 程式常會手調）
    //   * 合成的 Viewport 節點（RectMask2D + 透明 raycast Image，程式可能換成專案自製 NonDrawingGraphic）
    //   * 合成的 Content 節點（其 LayoutGroup / CSF）與 scroll 三層的 RectTransform 參數
    // reskin 只做：同名圖檔覆蓋、Prefab 內 Sprite 參照替換。設計師如果手動改上述 component，
    // reskin 不會吃掉他的手調 —— 但下次 full import 一定會覆蓋（因為 PS 是 source of truth）。
    //
    // v2.16 補充的契約（每一條都有機械檢查，檢查失敗會自動還原）：
    //   * 允許寫入的序列化欄位只有三種：Image.m_Sprite、Selectable.m_SpriteState.*（highlighted /
    //     pressed / selected / disabled）、RawImage.m_Texture。其他欄位、階層、元件增刪、RawImage.uvRect
    //     一律不動；Image.overrideSprite 是 [NonSerialized]、Selectable.image 只是 targetGraphic 參照，
    //     兩者在 Prefab 裡沒有可換的 Sprite，不列入。
    //   * 兩段式：Plan*（dry-run，不改任何檔案）→ Execute（重新規劃並比對，內容與預覽不同就不執行）。
    //     執行前備份所有會被寫的檔案到 Logs/PsUiReskin/Backups/<runId>/，取消、例外或機械檢查失敗都自動還原。
    //   * Nested Prefab / Prefab Variant：值由誰「擁有」就只改誰。
    //       - 值在本 Prefab 自己的物件上，或本 Prefab 對 nested / base 已有 override → 在本 Prefab 改（不新增 override）。
    //       - 值繼承自範圍內的來源 Prefab → 改來源，外層自動繼承（報告 relation = inheritedFrom*）。
    //       - 值繼承自範圍外（或被排除）的來源 → 不改來源、也不在外層新增 override，整組標 blocked
    //         （ownerOutOfScope）。舊版會在外層默默新增 override，之後來源再換皮就再也傳不過去。
    //   * 檔案覆蓋會影響所有引用那張圖的地方。SkinTheme 流程若發現目標範圍外的 Prefab / Scene 也引用同一檔案
    //     （例如複製到 Temp 的 Prefab 仍指向原頁面的圖），標 blocked（sharedOutsideScope），改用參照替換。
    //   * Undo：不採用。檔案覆蓋在 Undo 系統之外，Prefab 改動若可 Undo 而圖檔不行，會留下一半新一半舊的狀態；
    //     改以備份 + RollbackLastApply() 一次還原兩者。只改 SkinTheme 資產本身的 ScanAndFillOldSprites 使用 Undo。
    //   * SpriteAtlas：覆蓋後找出 packables 含這些圖的 atlas，Sprite Packer 未停用時只重打這幾顆
    //     （SpriteAtlasUtility.PackAtlases），不呼叫 CreateOrUpdateSpriteAtlases —— 那會重設 atlas importer
    //     設定，屬結構變更，不是換皮。
    // ─────────────────────────────────────────────────────────────────────────
    public static class PsUiSkinApplier
    {
        public const string ReportRelativePath = "Logs/PsUiReskin/reskin_report.json";
        private const string BackupRelativeRoot = "Logs/PsUiReskin/Backups";
        private const string LastApplyPointer = "Logs/PsUiReskin/last_apply.txt";

        public const string ModeDryRun = "dryRun";
        public const string ModeApply = "apply";
        public const string FlowFolder = "folder";
        public const string FlowSkinTheme = "skinTheme";

        public const string ActionFileOverwrite = "fileOverwrite";
        public const string ActionRefSwap = "refSwap";
        public const string ActionSkipped = "skipped";

        public const string StatusOk = "ok";
        public const string StatusMissing = "missing";
        public const string StatusBlocked = "blocked";
        // schema 的 status 原本只有 ok|missing|blocked；刻意不做的項目若也標 ok，
        // 「status=ok 的檔案 hash 一定改變」這條機械檢查就不成立，所以另立 skipped。
        public const string StatusSkipped = "skipped";

        public const string RelationOwn = "own";
        public const string RelationExistingOverride = "existingOverride";
        public const string RelationNested = "inheritedFromNestedPrefab";
        public const string RelationVariantBase = "inheritedFromVariantBase";

        private static readonly string[] ProtectedComponentTypes =
        {
            "CanvasGroup", "GridLayoutGroup", "HorizontalLayoutGroup", "VerticalLayoutGroup",
            "ContentSizeFitter", "ScrollRect", "RectMask2D",
        };

        public static string ReportPath => ProjectRoot + "/" + ReportRelativePath;

        // 驗收用失敗注入點：每次寫檔 / 存 Prefab 前呼叫，丟例外即模擬執行中途失敗。正式流程不設定。
        internal static Action<string> BeforeWriteForTests;
        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');

        // ── 報告模型（欄位對應 reskin_report.json）──────────────────────────
        public sealed class Report
        {
            public string toolVersion, mode, runId, generatedAt, flow, targetFolder, sourceArtFolder, usageScope;
            public bool planComplete = true;
            public readonly List<Item> items = new List<Item>();
            public readonly List<string> errors = new List<string>();
            public bool contractChecked;
            public int contractCheckedPrefabs;
            public readonly List<Violation> violations = new List<Violation>();
            public Checks checks;
            public Execution execution;
            public readonly List<string> atlasesAffected = new List<string>();
            public readonly List<string> atlasesRepacked = new List<string>();
            public string atlasNote;
            public Summary summary = new Summary();

            internal PsUiSkinTheme theme;
            internal string folderSource, folderTarget, folderScope;
            internal readonly Dictionary<string, string> scannedPrefabHashes = new Dictionary<string, string>();

            public IEnumerable<Item> Executable =>
                items.Where(i => i.status == StatusOk && (i.action == ActionFileOverwrite || i.action == ActionRefSwap));
        }

        public sealed class Item
        {
            public string oldSprite, action, newSource, status, reasonCode, reason, targetAsset;
            public int[] oldSize, newSize, border, newBorder;
            public bool? borderValidAfter;
            public readonly List<string> warnings = new List<string>();
            public readonly List<Usage> usedBy = new List<Usage>();

            internal string targetHashBefore, sourceHash;
        }

        public sealed class Usage
        {
            public string prefab, path, component, property, relation, owner, note;
            public bool applied, writtenHere;

            internal Component target;
            internal UnityEngine.Object newValue;
            internal Type targetType;
            internal int targetIndex;
        }

        public sealed class Violation
        {
            public string prefab, field, before, after, kind;
        }

        public sealed class Checks
        {
            public bool passed, guidStable, contractClean, summaryConsistent, hashConsistent;
            public readonly List<string> failures = new List<string>();
            public Summary executedCounters;
        }

        public sealed class Execution
        {
            public bool completed, cancelled, rolledBack;
            public string reason, backupFolder;
        }

        public sealed class Summary
        {
            public int filesOverwritten, spritesReplaced, prefabsChanged, missing, blocked, skipped;
        }

        // ── 入口：資料夾同名覆蓋（Window 上半部）───────────────────────────────
        public static Report PlanFolder(string sourceArtFolder, string targetImageFolder, string usageScopeFolder)
        {
            var report = NewReport(ModeDryRun, FlowFolder);
            report.folderSource = sourceArtFolder;
            report.folderTarget = targetImageFolder;
            report.folderScope = usageScopeFolder;
            report.sourceArtFolder = Normalize(sourceArtFolder);
            report.targetFolder = PathUtility.NormalizeAssetKey(targetImageFolder);
            try
            {
                BuildFolderItems(report, sourceArtFolder, targetImageFolder, usageScopeFolder);
            }
            catch (OperationCanceledException)
            {
                Abort(report, "使用者取消了預覽掃描，計畫不完整，不可執行。");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            Finish(report);
            return report;
        }

        // ── 入口：SkinTheme 批次（Window 下半部）──────────────────────────────
        public static Report PlanTheme(PsUiSkinTheme theme)
        {
            var report = NewReport(ModeDryRun, FlowSkinTheme);
            report.theme = theme;
            try
            {
                BuildThemeItems(report, theme);
            }
            catch (OperationCanceledException)
            {
                Abort(report, "使用者取消了預覽掃描，計畫不完整，不可執行。");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            Finish(report);
            return report;
        }

        private static void Abort(Report report, string message)
        {
            report.planComplete = false;
            report.items.Clear();
            report.errors.Add(message);
        }

        private static void BuildFolderItems(Report report, string sourceArtFolder, string targetImageFolder, string usageScopeFolder)
        {
            var sourceValid = !string.IsNullOrWhiteSpace(sourceArtFolder) && Directory.Exists(sourceArtFolder);
            if (!sourceValid)
                report.errors.Add("美術來源資料夾未設定或不存在。");

            var target = PathUtility.NormalizeAssetKey(targetImageFolder).TrimEnd('/');
            if (!PathUtility.IsAssetPath(target) || !AssetDatabase.IsValidFolder(target))
            {
                report.errors.Add("Unity 目標資料夾必須是專案 Assets 之下已存在的資料夾。");
                report.planComplete = false;
                return;
            }

            var scopeFolder = PathUtility.NormalizeAssetKey(usageScopeFolder).TrimEnd('/');
            if (string.IsNullOrEmpty(scopeFolder)) scopeFolder = "Assets";
            if (!AssetDatabase.IsValidFolder(scopeFolder))
            {
                report.errors.Add($"Prefab 使用範圍「{scopeFolder}」不是有效資料夾，改用整個 Assets。");
                scopeFolder = "Assets";
            }
            report.usageScope = scopeFolder;

            var pngs = Directory.GetFiles(ToAbs(target), "*.png", SearchOption.TopDirectoryOnly)
                .Select(f => target + "/" + Path.GetFileName(f))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
            if (pngs.Count == 0)
            {
                report.errors.Add("Unity 目標資料夾內找不到 PNG 圖片。");
                return;
            }

            var scope = new ScanScope(scopeFolder, null);
            var hits = ScanUsages(scope, new HashSet<string>(pngs), "換皮預覽：掃描 Prefab 使用位置");
            foreach (var png in pngs)
            {
                var main = AssetDatabase.LoadAssetAtPath<Sprite>(png);
                report.items.Add(BuildFileItem(png, main, sourceArtFolder, sourceValid, hits, false, null));
            }
        }

        private static void BuildThemeItems(Report report, PsUiSkinTheme theme)
        {
            if (theme == null)
            {
                report.errors.Add("SkinTheme 為空。");
                report.planComplete = false;
                return;
            }

            report.sourceArtFolder = Normalize(theme.sourceArtFolder);
            var targetFolder = theme.targetPrefabFolderAsset != null
                ? AssetDatabase.GetAssetPath(theme.targetPrefabFolderAsset)
                : null;
            report.targetFolder = targetFolder;
            var folderValid = !string.IsNullOrWhiteSpace(targetFolder) && AssetDatabase.IsValidFolder(targetFolder);
            if (!folderValid)
                report.errors.Add("目標 Prefab 資料夾未設定或不存在：參照替換項目全部 blocked，檔案覆蓋項目無法列出使用位置。");

            var sourceValid = !string.IsNullOrWhiteSpace(theme.sourceArtFolder) && Directory.Exists(theme.sourceArtFolder);
            if (theme.entries == null || theme.entries.Count == 0)
            {
                report.errors.Add("沒有任何換皮項目。");
                return;
            }

            var scope = new ScanScope(folderValid ? targetFolder : null, theme.excludedPrefabs);
            report.usageScope = scope.folder;

            var seen = new HashSet<string>();
            var fileEntries = new List<SkinThemeEntry>();
            var swapEntries = new List<SkinThemeEntry>();
            for (var i = 0; i < theme.entries.Count; i++)
            {
                var entry = theme.entries[i];
                if (entry == null || entry.oldSprite == null)
                {
                    report.items.Add(Skip(new Item { oldSprite = "", action = ActionSkipped }, "entryWithoutOldSprite",
                        $"第 {i + 1} 筆沒有指定 Old Sprite，無法處理。"));
                    continue;
                }

                var key = SpriteKey(entry.oldSprite);
                if (key == null)
                {
                    report.items.Add(Skip(new Item { oldSprite = entry.oldSprite.name, action = ActionSkipped }, "oldSpriteNotAnAsset",
                        "Old Sprite 不是專案內的資產（可能是內建資源），無法換皮。"));
                    continue;
                }

                if (!seen.Add(key))
                {
                    report.items.Add(Skip(new Item { oldSprite = key, action = ActionSkipped }, "duplicateEntry",
                        $"第 {i + 1} 筆與前面的項目是同一張 Old Sprite，只處理第一筆。"));
                    continue;
                }

                if (IsSameNameMode(entry)) fileEntries.Add(entry);
                else swapEntries.Add(entry);
            }

            var interesting = new HashSet<string>(fileEntries.Concat(swapEntries)
                .Select(e => AssetDatabase.GetAssetPath(e.oldSprite)));
            var hits = scope.folder != null
                ? ScanUsages(scope, interesting, "換皮預覽：掃描 Prefab")
                : new List<UsageHit>();
            foreach (var h in hits.Select(h => h.prefab).Distinct())
                report.scannedPrefabHashes[h] = Sha256(ToAbs(h));

            // 檔案覆蓋會改到所有引用者：先找出範圍外也在用同一檔案的 Prefab / Scene。
            var fileTargets = new HashSet<string>(fileEntries.Select(e => AssetDatabase.GetAssetPath(e.oldSprite)));
            var outside = scope.folder != null && sourceValid
                ? FindUsersOutsideScope(fileTargets, scope)
                : new Dictionary<string, List<string>>();

            foreach (var entry in fileEntries)
            {
                var assetPath = AssetDatabase.GetAssetPath(entry.oldSprite);
                outside.TryGetValue(assetPath, out var outsideUsers);
                var item = BuildFileItem(assetPath, entry.oldSprite, theme.sourceArtFolder, sourceValid, hits, true, outsideUsers);
                report.items.Add(item);
            }

            foreach (var entry in swapEntries)
                report.items.AddRange(BuildSwapItems(entry.oldSprite, entry.newSprite, hits, scope, folderValid));
        }

        private static bool IsSameNameMode(SkinThemeEntry entry) =>
            entry.newSprite == null || entry.newSprite == entry.oldSprite;

        // ── 檔案覆蓋項目：尺寸 / 9-slice / 副檔名 / 多重 Sprite / 共用檢查 ──────────────
        private static Item BuildFileItem(string assetPath, Sprite oldSprite, string sourceDir, bool sourceValid,
            List<UsageHit> hits, bool themeFlow, List<string> usersOutsideScope)
        {
            var item = new Item
            {
                action = ActionFileOverwrite,
                targetAsset = assetPath,
                oldSprite = oldSprite != null ? SpriteKey(oldSprite) : assetPath,
            };
            var abs = ToAbs(assetPath);
            item.oldSize = ReadImageSize(abs);
            item.targetHashBefore = Sha256(abs);
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            var isSprite = importer != null && importer.textureType == TextureImporterType.Sprite;
            var border = isSprite && importer.spriteImportMode == SpriteImportMode.Single ? importer.spriteBorder : Vector4.zero;
            item.border = BorderArray(border);

            foreach (var h in hits.Where(h => h.AssetPath == assetPath))
            {
                var u = ToUsage(h, null);
                u.writtenHere = false;
                u.note = "檔案內容覆蓋，所有引用同步生效";
                item.usedBy.Add(u);
            }
            if (item.usedBy.Any(u => hits.Any(h => h.protectedInfra && h.prefab == u.prefab && h.path == u.path)))
                item.warnings.Add("ScrollRect 合成的 Viewport / Content 節點也引用此圖；檔案覆蓋會一併改變它們的外觀（不可碰清單約束的是元件參數與參照，不是圖檔內容）。");

            if (!sourceValid)
                return Block(item, "sourceArtFolderNotSet", "美術來源資料夾未設定或不存在，無法覆蓋。");

            if (isSprite && importer.spriteImportMode == SpriteImportMode.Multiple)
            {
                var count = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath).OfType<Sprite>().Count();
                return Block(item, "multiSpriteTexture",
                    $"{Path.GetFileName(assetPath)} 是多重 Sprite 貼圖（Sprite Mode = Multiple，含 {count} 個 Sprite，常見於 TexturePacker / 圖集）。" +
                    "整檔覆蓋會讓每個 Sprite 的切割框對不上新圖，也會連帶換掉同圖集的其他 Sprite；請改用參照替換（New Sprite 指到新匯入的圖）。");
            }

            if (themeFlow && oldSprite != null &&
                !string.Equals(Path.GetFileNameWithoutExtension(assetPath), oldSprite.name, StringComparison.OrdinalIgnoreCase))
            {
                return Block(item, "spriteNameDiffersFromFile",
                    $"Sprite 名稱「{oldSprite.name}」與檔名「{Path.GetFileName(assetPath)}」不同，無法用同名檔案覆蓋；請改用參照替換。");
            }

            if (usersOutsideScope != null && usersOutsideScope.Count > 0)
            {
                return Block(item, "sharedOutsideScope",
                    $"目標範圍外還有 {usersOutsideScope.Count} 個 Prefab / Scene 直接引用這個檔案（{Preview(usersOutsideScope, 3)}）。" +
                    "覆蓋檔案會連它們一起換皮；若這是從別的頁面複製來的 Prefab，請把新圖匯入新資料夾並改用參照替換。");
            }

            var fileName = Path.GetFileName(assetPath);
            var src = Normalize(Path.Combine(sourceDir, fileName));
            if (!File.Exists(src))
            {
                var alt = FindSameStemOtherExtension(sourceDir, Path.GetFileNameWithoutExtension(assetPath), Path.GetExtension(assetPath));
                if (alt != null)
                {
                    item.newSource = alt;
                    item.newSize = ReadImageSize(alt);
                    return Block(item, "extensionMismatch",
                        $"美術提供的是 {Path.GetFileName(alt)}，專案內是 {fileName}；副檔名不同直接覆蓋會讓 Unity 以錯誤格式解讀，請轉成相同格式再提供。");
                }
                item.status = StatusMissing;
                item.reasonCode = "sourceFileNotFound";
                item.reason = MissingReason(item, fileName);
                return item;
            }

            item.newSource = src;
            item.sourceHash = Sha256(src);
            item.newSize = ReadImageSize(src);
            if (item.newSize == null)
                return Block(item, "unreadableSource", $"{src} 無法讀出圖片尺寸（不是有效的 PNG / JPG），不覆蓋。");

            if (item.sourceHash == item.targetHashBefore)
            {
                item.action = ActionSkipped;
                return Skip(item, "identicalContent", "新美術與現有檔案內容完全相同，不需覆蓋。");
            }

            var sizeChanged = item.oldSize == null || item.oldSize[0] != item.newSize[0] || item.oldSize[1] != item.newSize[1];
            var hasBorder = item.border.Any(v => v > 0);
            if (sizeChanged && hasBorder)
            {
                item.borderValidAfter = false;
                var exceeds = item.border[0] + item.border[2] >= item.newSize[0] || item.border[1] + item.border[3] >= item.newSize[1];
                return Block(item, "borderInvalidAfterResize",
                    $"舊圖 {SizeText(item.oldSize)} 設有 9-slice 邊界 L{item.border[0]}/B{item.border[1]}/R{item.border[2]}/T{item.border[3]}，新圖為 {SizeText(item.newSize)}。" +
                    "邊界以像素記在 .meta，尺寸一變切點就不再對準新圖的角與邊" + (exceeds ? "，而且邊界總和已超出新圖尺寸" : "") +
                    $"，覆蓋會破圖。請美術提供 {SizeText(item.oldSize)} 的圖；若確定要改尺寸，請手動換檔並重設 Sprite Border。");
            }

            if (IsReadOnly(assetPath))
                return Block(item, "targetReadOnly", $"{assetPath} 是唯讀檔（版控未 checkout？），請先解除唯讀再重新預覽。");

            item.status = StatusOk;
            item.reasonCode = "ready";
            item.reason = "同名檔案覆蓋；.meta（GUID、importer 設定、9-slice）保留不動。";
            item.borderValidAfter = true;
            if (sizeChanged)
                item.warnings.Add($"尺寸由 {SizeText(item.oldSize)} 變為 {SizeText(item.newSize)}（無 9-slice）。RectTransform 不會跟著變，圖會依既有大小縮放顯示，請目視確認比例。");
            if (item.usedBy.Count == 0)
                item.warnings.Add("使用範圍內沒有 Prefab 引用此圖（仍會覆蓋檔案）。");
            return item;
        }

        // ── 參照替換項目：依擁有者 / 保護節點 / RawImage / 9-slice 拆組 ─────────────
        private static IEnumerable<Item> BuildSwapItems(Sprite oldSprite, Sprite newSprite, List<UsageHit> hits,
            ScanScope scope, bool folderValid)
        {
            Item NewItem() => new Item
            {
                action = ActionRefSwap,
                oldSprite = SpriteKey(oldSprite),
                newSource = SpriteKey(newSprite) ?? newSprite.name,
                oldSize = RectSize(oldSprite),
                newSize = RectSize(newSprite),
                border = BorderArray(oldSprite.border),
                newBorder = BorderArray(newSprite.border),
            };

            if (!folderValid)
            {
                yield return Block(NewItem(), "targetPrefabFolderNotSet", "目標 Prefab 資料夾未設定或不存在，無法替換參照。");
                yield break;
            }
            if (SpriteKey(newSprite) == null)
            {
                yield return Block(NewItem(), "newSpriteNotAnAsset", "New Sprite 不是專案內的資產，無法寫入 Prefab。");
                yield break;
            }

            var oldWhole = IsWholeTexture(oldSprite);
            var newWhole = IsWholeTexture(newSprite);
            var mine = hits.Where(h => h.sprite == oldSprite ||
                                       (h.texture != null && oldWhole && h.texture == oldSprite.texture)).ToList();
            if (mine.Count == 0)
            {
                var none = NewItem();
                none.action = ActionSkipped;
                yield return Skip(none, "notUsedInScope", "目標資料夾內沒有 Prefab 引用這張 Sprite。");
                yield break;
            }

            var protectedHits = mine.Where(h => h.protectedInfra).ToList();
            var rawBlocked = mine.Where(h => !h.protectedInfra && h.texture != null && !newWhole).ToList();
            var outOfScope = mine.Where(h => !h.protectedInfra && !rawBlocked.Contains(h) && !scope.Contains(h.owner)).ToList();
            var candidates = mine.Except(protectedHits).Except(rawBlocked).Except(outOfScope).ToList();

            if (candidates.Count > 0)
            {
                var item = NewItem();
                foreach (var h in candidates)
                    item.usedBy.Add(ToUsage(h, h.texture != null ? (UnityEngine.Object)newSprite.texture : newSprite));

                var oldHasBorder = oldSprite.border.sqrMagnitude > 0f;
                var newHasBorder = newSprite.border.sqrMagnitude > 0f;
                var sliced = candidates.Where(h => h.imageForType != null &&
                                                   (h.imageForType.type == Image.Type.Sliced || h.imageForType.type == Image.Type.Tiled)).ToList();
                var readOnlyOwners = item.usedBy.Where(u => u.writtenHere).Select(u => u.prefab).Distinct().Where(IsReadOnly).ToList();
                if (readOnlyOwners.Count > 0)
                {
                    yield return Block(item, "prefabReadOnly",
                        $"要寫入的 Prefab 是唯讀檔（版控未 checkout？）：{Preview(readOnlyOwners, 3)}。請先解除唯讀再重新預覽。");
                }
                else if (oldHasBorder && !newHasBorder && sliced.Count > 0)
                {
                    item.borderValidAfter = false;
                    yield return Block(item, "newSpriteHasNoBorderForSlicedImage",
                        $"{sliced.Count} 處 Image 以 Sliced / Tiled 顯示，舊 Sprite 有 9-slice 邊界，New Sprite「{newSprite.name}」沒有邊界；" +
                        "替換後會整張拉伸破圖。請先在新 Sprite 設定 Border，再重新預覽。");
                }
                else
                {
                    item.status = StatusOk;
                    item.reasonCode = "ready";
                    item.reason = "替換 Prefab 內的 Sprite 參照；只寫在值的擁有者上，不新增 override。";
                    item.borderValidAfter = !sliced.Any() || newHasBorder || !oldHasBorder;
                    if (item.oldSize != null && item.newSize != null &&
                        (item.oldSize[0] != item.newSize[0] || item.oldSize[1] != item.newSize[1]))
                        item.warnings.Add($"Sprite 尺寸由 {SizeText(item.oldSize)} 變為 {SizeText(item.newSize)}；RectTransform 不會跟著變，請目視確認比例。");
                    var inherited = item.usedBy.Count(u => !u.writtenHere);
                    if (inherited > 0)
                        item.warnings.Add($"{inherited} 處的值繼承自範圍內的來源 Prefab，改在來源上，外層自動繼承。");
                    yield return item;
                }
            }

            if (protectedHits.Count > 0)
            {
                var item = NewItem();
                item.action = ActionSkipped;
                foreach (var h in protectedHits) item.usedBy.Add(ToUsage(h, null));
                yield return Skip(item, "protectedScrollInfrastructure",
                    $"{protectedHits.Count} 處在 ScrollRect 合成的 Viewport / Content 節點上，屬不可碰清單，不替換。");
            }

            if (rawBlocked.Count > 0)
            {
                var item = NewItem();
                foreach (var h in rawBlocked) item.usedBy.Add(ToUsage(h, null));
                yield return Block(item, "rawImageNeedsWholeTexture",
                    $"{rawBlocked.Count} 處 RawImage 直接顯示整張 Texture，但 New Sprite「{newSprite.name}」只是貼圖的一部分（圖集子圖）；換過去會顯示整張圖集。請提供單張貼圖。");
            }

            if (outOfScope.Count > 0)
            {
                var item = NewItem();
                foreach (var h in outOfScope) item.usedBy.Add(ToUsage(h, null));
                var owners = outOfScope.Select(h => h.owner).Distinct().ToList();
                yield return Block(item, "ownerOutOfScope",
                    $"{outOfScope.Count} 處的 Sprite 值來自目標範圍外（或被排除）的來源 Prefab：{Preview(owners, 3)}（Nested Prefab 或 Variant 的來源）。" +
                    "換皮不改範圍外的來源，也不在外層新增 override；請先 Unpack 該 Nested Prefab，或把來源納入目標資料夾後重新預覽。");
            }
        }

        // ── 執行：重新規劃比對 → 備份 → 批次寫入 → 機械檢查 → 失敗自動還原 ──────────
        public static Report Execute(Report dryRun)
        {
            if (dryRun == null || dryRun.mode != ModeDryRun)
                throw new ArgumentException("Execute 只接受 Plan* 產生的 dry-run 報告。");

            var report = dryRun.flow == FlowSkinTheme
                ? PlanTheme(dryRun.theme)
                : PlanFolder(dryRun.folderSource, dryRun.folderTarget, dryRun.folderScope);
            report.mode = ModeApply;
            report.execution = new Execution();

            if (!dryRun.planComplete || !report.planComplete)
                return NotExecuted(report, "預覽不完整（被取消或輸入無效），不執行。");
            if (Signature(report) != Signature(dryRun))
                return NotExecuted(report, "預覽之後檔案或 SkinTheme 內容已變更，為避免執行未經確認的內容，本次不執行。請重新預覽。");

            var fileItems = report.Executable.Where(i => i.action == ActionFileOverwrite).ToList();
            var swapItems = report.Executable.Where(i => i.action == ActionRefSwap).ToList();
            if (fileItems.Count == 0 && swapItems.Count == 0)
            {
                report.execution.completed = true;
                report.execution.reason = "沒有可執行的項目。";
                Finish(report);
                return report;
            }

            var writes = swapItems.SelectMany(i => i.usedBy).Where(u => u.writtenHere)
                .GroupBy(u => u.prefab).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
            var touched = fileItems.Select(i => i.targetAsset).Concat(writes.Select(g => g.Key)).Distinct().ToList();

            var backup = Backup.Create(report.runId, touched);
            report.execution.backupFolder = backup.relativeFolder;
            var guidsBefore = touched.ToDictionary(p => p, p => ReadMetaGuid(ToAbs(p)));
            var fingerprintsBefore = writes.ToDictionary(g => g.Key, g => FingerprintPrefab(g.Key));
            var counters = new Summary();

            string failure = null;
            var cancelled = false;
            try
            {
                var total = Math.Max(1, fileItems.Count + writes.Count);
                var step = 0;
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var item in fileItems)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar("換皮：覆蓋圖檔", item.targetAsset, (float)step++ / total))
                            throw new OperationCanceledException();
                        BeforeWriteForTests?.Invoke(item.targetAsset);
                        File.Copy(item.newSource, ToAbs(item.targetAsset), true);
                        AssetDatabase.ImportAsset(item.targetAsset, ImportAssetOptions.ForceUpdate);
                        counters.filesOverwritten++;
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                // Prefab 不放進 StartAssetEditing：import 被延後時，外層 nested / variant 在記憶體裡仍是舊的來源內容，
                // 讀回來會是舊 Sprite（v2.16 驗收的第二道網抓到）。依相依順序逐一存檔：來源先存、外層後存。
                foreach (var group in OrderByDependency(writes))
                {
                    if (EditorUtility.DisplayCancelableProgressBar("換皮：替換 Prefab 參照", group.Key, (float)step++ / total))
                        throw new OperationCanceledException();
                    BeforeWriteForTests?.Invoke(group.Key);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(group.Key);
                    foreach (var u in group)
                    {
                        // 前一個來源存檔會讓 Unity 重新 import 外層，規劃時抓到的 component 可能已被換掉，依路徑重新定位。
                        var target = Locate(u) ?? throw new InvalidOperationException($"找不到 {u.prefab}:{u.path} 的 {u.component}。");
                        var so = new SerializedObject(target);
                        so.FindProperty(u.property).objectReferenceValue = u.newValue;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        counters.spritesReplaced++;
                    }
                    PrefabUtility.SavePrefabAsset(prefab);
                    counters.prefabsChanged++;
                }
                AssetDatabase.SaveAssets();
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception e)
            {
                failure = "執行時發生例外：" + e.Message;
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (!cancelled && failure == null)
            {
                foreach (var item in fileItems.Concat(swapItems))
                    foreach (var u in item.usedBy)
                        u.applied = true;
                RepackAffectedAtlases(report, fileItems.Select(i => i.targetAsset).ToList());
                report.checks = RunChecks(report, touched, guidsBefore, fingerprintsBefore, counters);
                if (!report.checks.passed)
                    failure = "機械檢查未通過：" + string.Join("；", report.checks.failures.Take(3));
            }

            if (cancelled || failure != null)
            {
                var restored = backup.Restore(out var restoreMessage);
                report.execution.cancelled = cancelled;
                report.execution.rolledBack = restored;
                report.execution.reason = (cancelled ? "使用者取消" : failure) +
                                          (restored ? "；已自動還原所有檔案。" : "；自動還原失敗：" + restoreMessage);
                if (!restored)
                    report.errors.Add($"自動還原失敗，請用備份資料夾手動還原：{backup.relativeFolder}（{restoreMessage}）");
                foreach (var item in fileItems.Concat(swapItems))
                {
                    item.status = StatusBlocked;
                    item.reasonCode = restored ? "rolledBack" : "rollbackFailed";
                    item.reason = report.execution.reason;
                    foreach (var u in item.usedBy) u.applied = false;
                }
            }
            else
            {
                report.execution.completed = true;
                backup.MarkApplied();
            }

            Finish(report);
            return report;
        }

        private static List<IGrouping<string, Usage>> OrderByDependency(List<IGrouping<string, Usage>> groups)
        {
            var remaining = groups.ToList();
            var ordered = new List<IGrouping<string, Usage>>();
            while (remaining.Count > 0)
            {
                var names = new HashSet<string>(remaining.Select(g => g.Key));
                var next = remaining.FirstOrDefault(g =>
                    !AssetDatabase.GetDependencies(g.Key, true).Any(d => d != g.Key && names.Contains(d))) ?? remaining[0];
                ordered.Add(next);
                remaining.Remove(next);
            }
            return ordered;
        }

        private static Report NotExecuted(Report report, string reason)
        {
            report.execution.reason = reason;
            foreach (var item in report.Executable.ToList())
            {
                item.status = StatusBlocked;
                item.reasonCode = "notExecuted";
                item.reason = reason;
            }
            Finish(report);
            return report;
        }

        /// <summary>還原上一次成功執行的換皮。檔案在那之後又被改過就拒絕，避免蓋掉新的手調。</summary>
        public static bool RollbackLastApply(out string message)
        {
            var pointer = ProjectRoot + "/" + LastApplyPointer;
            if (!File.Exists(pointer))
            {
                message = "沒有可還原的換皮紀錄。";
                return false;
            }
            var backup = Backup.Load(File.ReadAllText(pointer).Trim(), out message);
            if (backup == null) return false;
            var ok = backup.RestoreIfUnchangedSinceApply(out message);
            if (ok) File.Delete(pointer);
            return ok;
        }

        public static bool HasRollback => File.Exists(ProjectRoot + "/" + LastApplyPointer);

        /// <summary>
        /// 掃描 theme.targetPrefabFolderAsset 下所有 Prefab 的 Image / Selectable spriteState / RawImage，
        /// 把尚未在 entries 裡的 Sprite 自動加入（oldSprite 填入，newSprite 留空）。可 Undo。
        /// </summary>
        public static int ScanAndFillOldSprites(PsUiSkinTheme theme) => ScanAndFillOldSprites(theme, out _);

        public static int ScanAndFillOldSprites(PsUiSkinTheme theme, out List<string> notes)
        {
            notes = new List<string>();
            if (theme == null) return 0;

            var targetFolder = theme.targetPrefabFolderAsset != null
                ? AssetDatabase.GetAssetPath(theme.targetPrefabFolderAsset)
                : null;
            if (string.IsNullOrWhiteSpace(targetFolder) || !AssetDatabase.IsValidFolder(targetFolder))
                return 0;

            var existingKeys = new HashSet<string>(theme.entries.Select(e => SpriteKey(e?.oldSprite)).Where(k => k != null));
            List<UsageHit> hits;
            try
            {
                hits = ScanUsages(new ScanScope(targetFolder, theme.excludedPrefabs), null, "掃描 Prefab 內的 Sprite");
            }
            catch (OperationCanceledException)
            {
                notes.Add("已取消掃描，沒有加入任何項目。");
                return 0;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Undo.RecordObject(theme, "掃描換皮 Sprite");
            var added = 0;
            var noted = new HashSet<string>();
            foreach (var h in hits)
            {
                if (h.protectedInfra) continue;
                var sprite = h.sprite;
                if (sprite == null && h.texture != null)
                {
                    sprite = AssetDatabase.LoadAllAssetsAtPath(h.AssetPath).OfType<Sprite>().FirstOrDefault(IsWholeTexture);
                    if (sprite == null)
                    {
                        if (noted.Add(h.AssetPath))
                            notes.Add($"RawImage 使用的 {h.AssetPath} 沒有對應的整張 Sprite（Texture Type 不是 Sprite），SkinTheme 以 Sprite 為單位，無法列入。");
                        continue;
                    }
                }
                var key = SpriteKey(sprite);
                if (key == null || !existingKeys.Add(key)) continue;
                theme.entries.Add(new SkinThemeEntry { oldSprite = sprite });
                added++;
            }

            if (added > 0)
                EditorUtility.SetDirty(theme);
            return added;
        }

        public sealed class AutoMatchResult
        {
            public int pairedPrefabs, missingPrefabs, filled, ambiguous, unchanged, missingNodes, missingSprites;
            public int remaining;
            public readonly List<string> notes = new List<string>();
        }

        public sealed class DimensionCandidate
        {
            public Sprite oldSprite;
            public Sprite selectedSprite;
            public int oldWidth, oldHeight;
            public string confidence;
            public readonly List<Sprite> candidates = new List<Sprite>();
        }

        public sealed class DimensionMatchResult
        {
            public int sourceSprites, high, medium, low, noCandidate;
            public readonly List<DimensionCandidate> matches = new List<DimensionCandidate>();
            public readonly List<string> notes = new List<string>();
        }

        /// <summary>
        /// 依 Sprite rect 的寬高建立候選清單。不改 SkinTheme；呼叫端必須讓使用者確認 selectedSprite 後，
        /// 再呼叫 ApplyDimensionSelections。唯一同尺寸候選才標為高信心。
        /// </summary>
        public static DimensionMatchResult SuggestByDimensions(PsUiSkinTheme theme)
        {
            var result = new DimensionMatchResult();
            if (theme == null) { result.notes.Add("請先指定 SkinTheme。"); return result; }
            var folder = theme.candidateSpriteFolderAsset == null
                ? null
                : AssetDatabase.GetAssetPath(theme.candidateSpriteFolderAsset);
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            {
                result.notes.Add("請指定 Unity Assets 內的「候選新 Sprite 資料夾」。");
                return result;
            }

            // PNG imports are Texture2D main assets; their Sprite is often a sub-asset,
            // so searching Texture2D and expanding assets is reliable for Single/Multiple modes.
            var sprites = AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
                .SelectMany(guid => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)).OfType<Sprite>())
                .Where(sprite => sprite != null)
                .GroupBy(SpriteKey).Select(group => group.First())
                .OrderBy(SpriteKey, StringComparer.Ordinal).ToList();
            result.sourceSprites = sprites.Count;
            if (sprites.Count == 0)
            {
                result.notes.Add("候選資料夾內找不到 Sprite；請確認圖片已匯入 Unity 並設為 Sprite (2D and UI)。");
                return result;
            }

            var seen = new HashSet<Sprite>();
            foreach (var entry in theme.entries ?? new List<SkinThemeEntry>())
            {
                if (entry == null || entry.oldSprite == null || entry.newSprite != null || !seen.Add(entry.oldSprite)) continue;
                var oldWidth = Mathf.RoundToInt(entry.oldSprite.rect.width);
                var oldHeight = Mathf.RoundToInt(entry.oldSprite.rect.height);
                if (oldWidth <= 0 || oldHeight <= 0) continue;

                var ranked = sprites.Select(sprite => new { sprite, score = DimensionScore(oldWidth, oldHeight, sprite) })
                    .Where(item => item.score >= 45f)
                    .OrderByDescending(item => item.score).ThenBy(item => SpriteKey(item.sprite), StringComparer.Ordinal)
                    .Take(4).ToList();
                var match = new DimensionCandidate { oldSprite = entry.oldSprite, oldWidth = oldWidth, oldHeight = oldHeight };
                match.candidates.AddRange(ranked.Select(item => item.sprite));
                if (ranked.Count == 0)
                {
                    match.confidence = "無候選";
                    result.noCandidate++;
                }
                else
                {
                    var exactCount = sprites.Count(sprite => SpriteSizeEquals(sprite, oldWidth, oldHeight));
                    var separation = ranked.Count == 1 ? 100f : ranked[0].score - ranked[1].score;
                    match.confidence = exactCount == 1 ? "高" : ranked[0].score >= 80f && separation >= 8f ? "中" : "低";
                    // 只做暫存預選，尚未寫入 SkinTheme；使用者按「確認寫入」才會套用。
                    match.selectedSprite = match.confidence == "低" ? null : ranked[0].sprite;
                    if (match.confidence == "高") result.high++;
                    else if (match.confidence == "中") result.medium++;
                    else result.low++;
                }
                result.matches.Add(match);
            }
            return result;
        }

        public static int ApplyDimensionSelections(PsUiSkinTheme theme, IEnumerable<DimensionCandidate> matches)
        {
            if (theme == null || matches == null) return 0;
            var entries = theme.entries ?? new List<SkinThemeEntry>();
            var entryByOld = entries.Where(entry => entry != null && entry.oldSprite != null)
                .GroupBy(entry => entry.oldSprite).ToDictionary(group => group.Key, group => group.First());
            var applied = 0;
            foreach (var match in matches)
            {
                if (match == null || match.oldSprite == null || match.selectedSprite == null) continue;
                if (!entryByOld.TryGetValue(match.oldSprite, out var entry) || entry.newSprite != null) continue;
                if (applied == 0) Undo.RecordObject(theme, "確認尺寸候選 SkinTheme Sprite");
                entry.newSprite = match.selectedSprite;
                applied++;
            }
            if (applied > 0) EditorUtility.SetDirty(theme);
            return applied;
        }

        private static bool SpriteSizeEquals(Sprite sprite, int width, int height) =>
            sprite != null && Mathf.RoundToInt(sprite.rect.width) == width && Mathf.RoundToInt(sprite.rect.height) == height;

        private static float DimensionScore(int oldWidth, int oldHeight, Sprite sprite)
        {
            var newWidth = Mathf.RoundToInt(sprite.rect.width);
            var newHeight = Mathf.RoundToInt(sprite.rect.height);
            if (newWidth <= 0 || newHeight <= 0) return 0f;
            var widthError = Mathf.Abs(newWidth - oldWidth) / (float)Mathf.Max(newWidth, oldWidth);
            var heightError = Mathf.Abs(newHeight - oldHeight) / (float)Mathf.Max(newHeight, oldHeight);
            var oldRatio = oldWidth / (float)oldHeight;
            var newRatio = newWidth / (float)newHeight;
            var ratioError = Mathf.Abs(newRatio - oldRatio) / oldRatio;
            return Mathf.Max(0f, 100f - (widthError + heightError) * 45f - ratioError * 55f);
        }

        /// <summary>只採用對照 Prefab 同一節點、同一欄位已換成不同 Sprite 的確定映射。</summary>
        public static AutoMatchResult AutoFillFromPrefabPairs(PsUiSkinTheme theme)
        {
            var result = new AutoMatchResult();
            if (theme == null) { result.notes.Add("請先指定 SkinTheme。"); return result; }
            var oldFolder = theme.targetPrefabFolderAsset == null ? null : AssetDatabase.GetAssetPath(theme.targetPrefabFolderAsset);
            var newFolder = theme.referencePrefabFolderAsset == null ? null : AssetDatabase.GetAssetPath(theme.referencePrefabFolderAsset);
            if (string.IsNullOrEmpty(oldFolder) || string.IsNullOrEmpty(newFolder) ||
                !AssetDatabase.IsValidFolder(oldFolder) || !AssetDatabase.IsValidFolder(newFolder) || oldFolder == newFolder)
            {
                result.notes.Add("請指定兩個不同且有效的 Prefab 資料夾：舊目標與已換好新圖的對照資料夾。");
                return result;
            }

            var proposed = new Dictionary<Sprite, HashSet<Sprite>>();
            var unchanged = new HashSet<Sprite>();
            var excluded = new ScanScope(oldFolder, theme.excludedPrefabs);
            var oldPrefix = Path.GetFileName(oldFolder);
            var newPrefix = Path.GetFileName(newFolder);
            try
            {
                var guids = AssetDatabase.FindAssets("t:Prefab", new[] { oldFolder });
                foreach (var guid in guids)
                {
                    var oldPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (!excluded.Contains(oldPath)) continue;
                    if (EditorUtility.DisplayCancelableProgressBar("SkinTheme 自動配對", oldPath,
                        (float)(result.pairedPrefabs + result.missingPrefabs) / Math.Max(1, guids.Length)))
                        throw new OperationCanceledException();

                    var relative = oldPath.Substring(oldFolder.Length + 1);
                    var newPath = newFolder + "/" + relative;
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(newPath) == null)
                    {
                        var file = Path.GetFileName(relative);
                        if (file.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                            file = newPrefix + file.Substring(oldPrefix.Length);
                        var directory = Path.GetDirectoryName(relative)?.Replace('\\', '/');
                        newPath = newFolder + "/" + (string.IsNullOrEmpty(directory) ? "" : directory + "/") + file;
                    }
                    var oldRoot = AssetDatabase.LoadAssetAtPath<GameObject>(oldPath);
                    var newRoot = AssetDatabase.LoadAssetAtPath<GameObject>(newPath);
                    if (oldRoot == null || newRoot == null)
                    {
                        result.missingPrefabs++;
                        result.notes.Add($"找不到對應 Prefab：{oldPath}");
                        continue;
                    }
                    result.pairedPrefabs++;
                    var oldHits = new List<UsageHit>();
                    var newHits = new List<UsageHit>();
                    CollectHits(oldPath, oldRoot, oldHits, null);
                    CollectHits(newPath, newRoot, newHits, null);
                    var newBySlot = newHits.GroupBy(h => SlotKey(newRoot.transform, h))
                        .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First());
                    foreach (var oldHit in oldHits)
                    {
                        if (oldHit.protectedInfra) continue;
                        var oldSprite = HitSprite(oldHit);
                        if (oldSprite == null) continue;
                        if (!newBySlot.TryGetValue(SlotKey(oldRoot.transform, oldHit), out var newHit) || newHit.protectedInfra)
                        {
                            result.missingNodes++;
                            continue;
                        }
                        var newSprite = HitSprite(newHit);
                        var newSpritePath = newSprite == null ? null : AssetDatabase.GetAssetPath(newSprite);
                        if (newSprite == null || string.IsNullOrEmpty(newSpritePath))
                        {
                            result.missingSprites++;
                            continue;
                        }
                        if (oldSprite == newSprite || newSpritePath == oldFolder ||
                            newSpritePath.StartsWith(oldFolder + "/", StringComparison.Ordinal))
                        {
                            unchanged.Add(oldSprite);
                            continue;
                        }
                        if (!proposed.TryGetValue(oldSprite, out var choices))
                            proposed[oldSprite] = choices = new HashSet<Sprite>();
                        choices.Add(newSprite);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                result.notes.Clear();
                result.notes.Add("已取消配對，SkinTheme 未修改。");
                return result;
            }
            finally { EditorUtility.ClearProgressBar(); }

            var entries = theme.entries ?? new List<SkinThemeEntry>();
            var existing = entries.Where(e => e != null && e.oldSprite != null)
                .GroupBy(e => e.oldSprite).ToDictionary(g => g.Key, g => g.First());
            foreach (var pair in proposed.OrderBy(p => SpriteKey(p.Key), StringComparer.Ordinal))
            {
                if (pair.Value.Count != 1)
                {
                    result.ambiguous++;
                    result.notes.Add($"一張舊圖對應多張新圖，請人工確認：{SpriteKey(pair.Key)}");
                    continue;
                }
                if (existing.TryGetValue(pair.Key, out var entry) && entry.newSprite != null) continue;
                if (result.filled == 0) Undo.RecordObject(theme, "依 Prefab 配對 SkinTheme Sprite");
                if (entry == null)
                {
                    entry = new SkinThemeEntry { oldSprite = pair.Key };
                    entries.Add(entry);
                }
                entry.newSprite = pair.Value.First();
                result.filled++;
            }
            if (result.filled > 0)
            {
                theme.entries = entries;
                EditorUtility.SetDirty(theme);
            }
            result.unchanged = unchanged.Count(sprite => !proposed.ContainsKey(sprite));
            result.remaining = entries.Count(e => e != null && e.oldSprite != null && e.newSprite == null);
            if (result.pairedPrefabs > 0 && result.filled == 0 && result.unchanged > 0)
                result.notes.Add("對照 Prefab 仍引用舊 Sprite；請先讓新 Prefab 指向新圖，再重新配對。");
            return result;
        }

        private static string SlotKey(Transform root, UsageHit hit)
        {
            var path = IndexedPath(root, hit.target.transform);
            var slash = path.IndexOf('/');
            return (slash < 0 ? "" : path.Substring(slash + 1)) + "|" + hit.property + "|" +
                   Array.IndexOf(hit.target.GetComponents(hit.target.GetType()), hit.target);
        }

        private static Sprite HitSprite(UsageHit hit)
        {
            if (hit.sprite != null) return hit.sprite;
            if (hit.texture == null) return null;
            return AssetDatabase.LoadAllAssetsAtPath(hit.AssetPath).OfType<Sprite>().FirstOrDefault(IsWholeTexture);
        }

        // ── 掃描 ────────────────────────────────────────────────────────────
        private sealed class ScanScope
        {
            public readonly string folder;
            public readonly HashSet<string> excluded = new HashSet<string>();

            public ScanScope(string folder, List<GameObject> excludedPrefabs)
            {
                this.folder = string.IsNullOrEmpty(folder) ? null : folder.TrimEnd('/');
                if (excludedPrefabs != null)
                    foreach (var go in excludedPrefabs)
                        if (go != null) excluded.Add(AssetDatabase.GetAssetPath(go));
            }

            public bool Contains(string assetPath) =>
                folder != null && !string.IsNullOrEmpty(assetPath) && !excluded.Contains(assetPath) &&
                (folder == "Assets" || assetPath.StartsWith(folder + "/", StringComparison.Ordinal));
        }

        private sealed class UsageHit
        {
            public string prefab, path, component, property, owner, relation;
            public Component target;
            public Image imageForType;
            public Sprite sprite;
            public Texture texture;
            public bool protectedInfra;
            public string AssetPath => AssetDatabase.GetAssetPath(sprite != null ? (UnityEngine.Object)sprite : texture);
        }

        private static List<UsageHit> ScanUsages(ScanScope scope, HashSet<string> interestingAssets, string progressTitle)
        {
            var hits = new List<UsageHit>();
            if (scope.folder == null) return hits;
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { scope.folder });
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (i % 16 == 0 && EditorUtility.DisplayCancelableProgressBar(progressTitle, path, (float)i / guids.Length))
                    throw new OperationCanceledException();
                if (scope.excluded.Contains(path)) continue;
                // 先用 AssetDatabase 快取的相依清單篩掉不相關的 Prefab，不必逐一載入。
                if (interestingAssets != null && !AssetDatabase.GetDependencies(path, true).Any(interestingAssets.Contains))
                    continue;
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root != null) CollectHits(path, root, hits, interestingAssets);
            }
            return hits;
        }

        private static void CollectHits(string prefabPath, GameObject root, List<UsageHit> into, HashSet<string> interesting)
        {
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var infra = IsSyntheticScrollInfrastructure(c.transform);
                if (c is Image img)
                    Add(c, "Image", "m_Sprite", img.sprite, null, img, infra);
                if (c is Selectable sel)
                {
                    var st = sel.spriteState;
                    var n = sel.GetType().Name;
                    Add(c, n + ".highlighted", "m_SpriteState.m_HighlightedSprite", st.highlightedSprite, null, sel.image, infra);
                    Add(c, n + ".pressed", "m_SpriteState.m_PressedSprite", st.pressedSprite, null, sel.image, infra);
                    Add(c, n + ".selected", "m_SpriteState.m_SelectedSprite", st.selectedSprite, null, sel.image, infra);
                    Add(c, n + ".disabled", "m_SpriteState.m_DisabledSprite", st.disabledSprite, null, sel.image, infra);
                }
                if (c is RawImage raw)
                    Add(c, "RawImage", "m_Texture", null, raw.texture, null, infra);
            }

            void Add(Component comp, string label, string property, Sprite sprite, Texture texture, Image imageForType, bool infra)
            {
                if (sprite == null && texture == null) return;
                var hit = new UsageHit
                {
                    prefab = prefabPath,
                    path = HierarchyPath(root.transform, comp.transform),
                    component = label,
                    property = property,
                    target = comp,
                    imageForType = imageForType,
                    sprite = sprite,
                    texture = texture,
                    protectedInfra = infra,
                };
                if (interesting != null && !interesting.Contains(hit.AssetPath)) return;
                hit.owner = ResolveOwner(comp, property, root, out hit.relation);
                into.Add(hit);
            }
        }

        // 沿 nested / variant 來源往下找，第一個「自己有這個值」（無來源或有 override）的資產就是擁有者。
        private static string ResolveOwner(Component comp, string property, GameObject prefabRoot, out string relation)
        {
            relation = RelationOwn;
            UnityEngine.Object current = comp;
            var first = true;
            while (true)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(current);
                if (source == null) return AssetDatabase.GetAssetPath(current);

                var prop = new SerializedObject(current).FindProperty(property);
                if (prop != null && prop.prefabOverride)
                {
                    if (first) relation = RelationExistingOverride;
                    return AssetDatabase.GetAssetPath(current);
                }

                if (first)
                {
                    var nearest = PrefabUtility.GetNearestPrefabInstanceRoot(comp.gameObject);
                    relation = nearest == prefabRoot ? RelationVariantBase : RelationNested;
                    first = false;
                }
                current = source;
            }
        }

        private static Usage ToUsage(UsageHit h, UnityEngine.Object newValue) => new Usage
        {
            prefab = h.prefab,
            path = h.path,
            component = h.component,
            property = h.property,
            relation = h.relation,
            owner = h.owner,
            writtenHere = h.owner == h.prefab,
            target = h.target,
            newValue = newValue,
            targetType = h.target.GetType(),
            targetIndex = Array.IndexOf(h.target.GetComponents(h.target.GetType()), h.target),
        };

        private static Dictionary<string, List<string>> FindUsersOutsideScope(HashSet<string> assetPaths, ScanScope scope)
        {
            var result = new Dictionary<string, List<string>>();
            if (assetPaths.Count == 0) return result;
            var guids = AssetDatabase.FindAssets("t:Prefab t:Scene", new[] { "Assets" });
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (i % 64 == 0 && EditorUtility.DisplayCancelableProgressBar("換皮預覽：檢查範圍外引用", path, (float)i / guids.Length))
                    throw new OperationCanceledException();
                if (scope.Contains(path)) continue; // 被排除的 Prefab 也算範圍外：檔案覆蓋一樣會改到它
                if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var dep in AssetDatabase.GetDependencies(path, false))
                {
                    if (!assetPaths.Contains(dep)) continue;
                    if (!result.TryGetValue(dep, out var list)) result[dep] = list = new List<string>();
                    list.Add(path);
                }
            }
            return result;
        }

        private static bool IsSyntheticScrollInfrastructure(Transform transform)
        {
            if (transform == null) return false;

            // Prefab Asset（非場景 instance）上的 GetComponentInParent 不會可靠地往上找，
            // reskin 正是直接掃描 AssetDatabase.LoadAssetAtPath 取得的 Prefab Asset。
            var current = transform;
            while (current != null)
            {
                var scrollRect = current.GetComponent<ScrollRect>();
                if (scrollRect != null)
                    return scrollRect.viewport == transform || scrollRect.content == transform;
                current = current.parent;
            }

            return false;
        }

        // ── 機械檢查（程式判定，不交給模型）────────────────────────────────────
        private static Checks RunChecks(Report report, List<string> touched, Dictionary<string, string> guidsBefore,
            Dictionary<string, Dictionary<string, string>> fingerprintsBefore, Summary counters)
        {
            var c = new Checks { executedCounters = counters, guidStable = true, hashConsistent = true };

            foreach (var p in touched)
            {
                var after = ReadMetaGuid(ToAbs(p));
                if (string.IsNullOrEmpty(after) || after != guidsBefore[p])
                {
                    c.guidStable = false;
                    c.failures.Add($"{p}.meta GUID 改變：{guidsBefore[p]} → {after ?? "(無)"}");
                }
            }

            report.contractChecked = true;
            report.contractCheckedPrefabs = fingerprintsBefore.Count;
            foreach (var kv in fingerprintsBefore)
            {
                var after = FingerprintPrefab(kv.Key);
                var synthetic = SyntheticPaths(kv.Key);
                foreach (var key in kv.Value.Keys.Union(after.Keys))
                {
                    kv.Value.TryGetValue(key, out var a);
                    after.TryGetValue(key, out var b);
                    if (a == b || IsWritableField(key)) continue;
                    var parts = key.Split('|');
                    var comp = parts.Length > 1 ? parts[1].Split('#')[0] : string.Empty;
                    var kind = parts.Length > 1 && parts[1] == "#components" ? "hierarchyOrComponentSet"
                        : ProtectedComponentTypes.Contains(comp) || synthetic.Contains(parts[0]) ? "protectedComponent"
                        : "unexpectedChange";
                    report.violations.Add(new Violation { prefab = kv.Key, field = key, before = a, after = b, kind = kind });
                }
            }
            c.contractClean = report.violations.Count == 0;
            if (!c.contractClean)
                c.failures.Add($"Prefab 有 {report.violations.Count} 個非 Sprite 欄位被改動（見 protectedContract.violations）。");

            foreach (var item in report.items.Where(i => !string.IsNullOrEmpty(i.targetAsset) && i.targetHashBefore != null))
            {
                var now = Sha256(ToAbs(item.targetAsset));
                if (item.status == StatusOk && item.action == ActionFileOverwrite)
                {
                    if (now == item.targetHashBefore || now != item.sourceHash)
                    {
                        c.hashConsistent = false;
                        c.failures.Add($"{item.targetAsset} 標為 ok，但檔案內容不等於新美術。");
                    }
                }
                else if (now != item.targetHashBefore)
                {
                    c.hashConsistent = false;
                    c.failures.Add($"{item.targetAsset} 標為 {item.status}，但檔案被改動了。");
                }
            }
            var written = new HashSet<string>(report.Executable.Where(i => i.action == ActionRefSwap)
                .SelectMany(i => i.usedBy).Where(u => u.writtenHere).Select(u => u.prefab));
            foreach (var kv in report.scannedPrefabHashes)
            {
                var changed = Sha256(ToAbs(kv.Key)) != kv.Value;
                if (written.Contains(kv.Key) != changed)
                {
                    c.hashConsistent = false;
                    c.failures.Add(changed ? $"{kv.Key} 不在寫入清單中卻被改動。" : $"{kv.Key} 應被寫入但檔案沒有變化。");
                }
            }

            // 每一處宣稱已套用的引用都從資產重新讀一次，確認值真的是新 Sprite（含繼承自來源的外層）。
            foreach (var item in report.Executable.Where(i => i.action == ActionRefSwap))
            {
                foreach (var u in item.usedBy.Where(u => u.applied))
                {
                    var observed = ReadReference(u);
                    if (observed != u.newValue)
                    {
                        c.hashConsistent = false;
                        c.failures.Add($"{u.prefab}:{u.path} {u.component} 宣稱已替換，重新讀取仍是 {(observed == null ? "null" : observed.name)}。");
                    }
                }
            }

            var derived = ComputeSummary(report.items);
            c.summaryConsistent = derived.filesOverwritten == counters.filesOverwritten &&
                                  derived.spritesReplaced == counters.spritesReplaced &&
                                  derived.prefabsChanged == counters.prefabsChanged;
            if (!c.summaryConsistent)
                c.failures.Add($"實際執行計數（檔案 {counters.filesOverwritten} / 參照 {counters.spritesReplaced} / Prefab {counters.prefabsChanged}）" +
                               $"與 items 統計（{derived.filesOverwritten} / {derived.spritesReplaced} / {derived.prefabsChanged}）不一致。");

            c.passed = c.guidStable && c.contractClean && c.hashConsistent && c.summaryConsistent;
            return c;
        }

        private static Component Locate(Usage u)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(u.prefab);
            if (root == null) return null;
            var slash = u.path.IndexOf('/');
            var node = slash < 0 ? root.transform : root.transform.Find(u.path.Substring(slash + 1));
            if (node == null) return null;
            var comps = node.GetComponents(u.targetType);
            return u.targetIndex >= 0 && u.targetIndex < comps.Length ? comps[u.targetIndex] : null;
        }

        private static UnityEngine.Object ReadReference(Usage u)
        {
            var comp = Locate(u);
            return comp == null ? null : new SerializedObject(comp).FindProperty(u.property)?.objectReferenceValue;
        }

        private static bool IsWritableField(string key)
        {
            var parts = key.Split('|');
            if (parts.Length < 3) return false;
            var comp = parts[1].Split('#')[0];
            return (comp == "Image" && parts[2] == "m_Sprite") ||
                   parts[2].StartsWith("m_SpriteState.", StringComparison.Ordinal) ||
                   (comp == "RawImage" && parts[2] == "m_Texture");
        }

        // 從磁碟重新載入 Prefab（不吃記憶體中的狀態），逐欄位拍指紋；浮點數以位元比對。
        private static Dictionary<string, string> FingerprintPrefab(string path)
        {
            var d = new Dictionary<string, string>();
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var goPath = IndexedPath(root.transform, t);
                    var comps = t.GetComponents<Component>();
                    d[goPath + "|#components"] = string.Join(",", comps.Select(c => c == null ? "Missing" : c.GetType().Name));
                    AddFields(d, goPath + "|GameObject", t.gameObject, root);
                    var counts = new Dictionary<string, int>();
                    foreach (var c in comps)
                    {
                        if (c == null) continue;
                        var n = c.GetType().Name;
                        counts.TryGetValue(n, out var i);
                        counts[n] = i + 1;
                        AddFields(d, goPath + "|" + n + "#" + i, c, root);
                    }
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return d;
        }

        private static HashSet<string> SyntheticPaths(string prefabPath)
        {
            var set = new HashSet<string>();
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (root == null) return set;
            foreach (var sr in root.GetComponentsInChildren<ScrollRect>(true))
            {
                set.Add(IndexedPath(root.transform, sr.transform));
                if (sr.viewport != null) set.Add(IndexedPath(root.transform, sr.viewport));
                if (sr.content != null) set.Add(IndexedPath(root.transform, sr.content));
            }
            return set;
        }

        private static void AddFields(Dictionary<string, string> d, string prefix, UnityEngine.Object obj, GameObject root)
        {
            var it = new SerializedObject(obj).GetIterator();
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
                        d[key] = "o:" + ReferenceKey(it.objectReferenceValue, root);
                        enter = false;
                        break;
                    default:
                        if (!it.hasChildren) d[key] = "t:" + it.propertyType;
                        break;
                }
            }
        }

        private static string ReferenceKey(UnityEngine.Object obj, GameObject root)
        {
            if (obj == null) return "null";
            if (obj is Component c && c.transform.IsChildOf(root.transform))
                return "local:" + IndexedPath(root.transform, c.transform) + ":" + c.GetType().Name;
            if (obj is GameObject go && go.transform.IsChildOf(root.transform))
                return "local:" + IndexedPath(root.transform, go.transform);
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out var guid, out long id) && !string.IsNullOrEmpty(guid))
                return guid + ":" + id;
            return "transient:" + obj.GetType().Name;
        }

        // ── SpriteAtlas ─────────────────────────────────────────────────────
        private static void RepackAffectedAtlases(Report report, List<string> overwrittenTextures)
        {
            if (overwrittenTextures.Count == 0) return;
            var affected = new List<SpriteAtlas>();
            foreach (var guid in AssetDatabase.FindAssets("t:SpriteAtlas"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
                if (atlas == null) continue;
                var packables = atlas.GetPackables().Select(AssetDatabase.GetAssetPath).Where(p => !string.IsNullOrEmpty(p)).ToList();
                var hit = overwrittenTextures.Any(tex => packables.Any(p =>
                    p == tex || (AssetDatabase.IsValidFolder(p) && tex.StartsWith(p.TrimEnd('/') + "/", StringComparison.Ordinal))));
                if (!hit) continue;
                affected.Add(atlas);
                report.atlasesAffected.Add(path);
            }
            if (affected.Count == 0)
            {
                report.atlasNote = "覆蓋的圖不屬於任何 SpriteAtlas。";
                return;
            }
            if (EditorSettings.spritePackerMode == SpritePackerMode.Disabled)
            {
                report.atlasNote = "Sprite Packer 為 Disabled，atlas 不會打包；Editor 直接顯示單張圖，不影響預覽。";
                return;
            }
            SpriteAtlasUtility.PackAtlases(affected.ToArray(), EditorUserBuildSettings.activeBuildTarget);
            report.atlasesRepacked.AddRange(report.atlasesAffected);
            report.atlasNote = $"已只重打包含覆蓋圖的 {affected.Count} 顆 atlas。";
        }

        // ── 備份與還原 ──────────────────────────────────────────────────────
        private sealed class Backup
        {
            public string relativeFolder;
            private string absFolder;
            // 備份檔名扁平化（序號_檔名），不複製 Assets/... 整棵路徑，避免深層專案路徑超過 Windows 260 字元上限。
            private readonly List<(string asset, string file, string hashBefore, string hashAfter)> entries =
                new List<(string, string, string, string)>();

            public static Backup Create(string runId, List<string> assets)
            {
                var b = new Backup { relativeFolder = BackupRelativeRoot + "/" + runId };
                b.absFolder = ProjectRoot + "/" + b.relativeFolder;
                Directory.CreateDirectory(b.absFolder + "/files");
                for (var i = 0; i < assets.Count; i++)
                {
                    var asset = assets[i];
                    var file = i.ToString("D4", CultureInfo.InvariantCulture) + "_" + Path.GetFileName(asset);
                    var dst = b.absFolder + "/files/" + file;
                    File.Copy(ToAbs(asset), dst, true);
                    File.Copy(ToAbs(asset) + ".meta", dst + ".meta", true);
                    b.entries.Add((asset, file, Sha256(ToAbs(asset)), null));
                }
                b.WriteManifest(false);
                return b;
            }

            public static Backup Load(string relativeFolder, out string message)
            {
                message = null;
                var b = new Backup { relativeFolder = relativeFolder, absFolder = ProjectRoot + "/" + relativeFolder };
                var manifestPath = b.absFolder + "/manifest.json";
                if (!File.Exists(manifestPath))
                {
                    message = $"找不到備份清單：{relativeFolder}/manifest.json";
                    return null;
                }
                var root = SimpleJsonReader.Parse(File.ReadAllText(manifestPath)) as Dictionary<string, object>;
                if (root == null || !(root.TryGetValue("files", out var files) && files is List<object> list))
                {
                    message = "備份清單格式錯誤。";
                    return null;
                }
                foreach (var o in list.OfType<Dictionary<string, object>>())
                    b.entries.Add(((string)o["asset"], (string)o["file"], (string)o["hashBefore"], o.TryGetValue("hashAfter", out var h) ? h as string : null));
                return b;
            }

            public void MarkApplied()
            {
                for (var i = 0; i < entries.Count; i++)
                    entries[i] = (entries[i].asset, entries[i].file, entries[i].hashBefore, Sha256(ToAbs(entries[i].asset)));
                WriteManifest(true);
                Directory.CreateDirectory(Path.GetDirectoryName(ProjectRoot + "/" + LastApplyPointer));
                File.WriteAllText(ProjectRoot + "/" + LastApplyPointer, relativeFolder);
            }

            public bool RestoreIfUnchangedSinceApply(out string message)
            {
                var changed = entries.Where(e => e.hashAfter == null || Sha256(ToAbs(e.asset)) != e.hashAfter).Select(e => e.asset).ToList();
                if (changed.Count > 0)
                {
                    message = $"{changed.Count} 個檔案在換皮之後又被改過（{Preview(changed, 3)}），為避免蓋掉新的修改，不還原。備份在 {relativeFolder}。";
                    return false;
                }
                return Restore(out message);
            }

            public bool Restore(out string message)
            {
                try
                {
                    AssetDatabase.StartAssetEditing();
                    try
                    {
                        foreach (var e in entries)
                        {
                            // 沒被改到的檔案（例如中途失敗時還沒輪到的）不必寫回，也就不會卡在唯讀檔上。
                            if (Sha256(ToAbs(e.asset)) == e.hashBefore) continue;
                            var src = absFolder + "/files/" + e.file;
                            File.Copy(src, ToAbs(e.asset), true);
                            File.Copy(src + ".meta", ToAbs(e.asset) + ".meta", true);
                            AssetDatabase.ImportAsset(e.asset, ImportAssetOptions.ForceUpdate);
                        }
                    }
                    finally
                    {
                        AssetDatabase.StopAssetEditing();
                    }
                    var bad = entries.Where(e => Sha256(ToAbs(e.asset)) != e.hashBefore).Select(e => e.asset).ToList();
                    message = bad.Count == 0
                        ? $"已從 {relativeFolder} 還原 {entries.Count} 個檔案。"
                        : $"還原後仍有 {bad.Count} 個檔案與備份不同：{Preview(bad, 3)}";
                    return bad.Count == 0;
                }
                catch (Exception ex)
                {
                    message = ex.Message;
                    return false;
                }
            }

            private void WriteManifest(bool applied)
            {
                var files = entries.Select(e => (object)new J
                {
                    { "asset", e.asset }, { "file", e.file }, { "hashBefore", e.hashBefore }, { "hashAfter", e.hashAfter },
                }).ToList();
                var json = new J { { "relativeFolder", relativeFolder }, { "applied", applied }, { "files", files } };
                Directory.CreateDirectory(absFolder);
                File.WriteAllText(absFolder + "/manifest.json", json.ToJson());
            }
        }

        // ── 報告輸出 ────────────────────────────────────────────────────────
        private static Report NewReport(string mode, string flow) => new Report
        {
            toolVersion = PhotoshopUiImporterWindow.ReportToolVersion,
            mode = mode,
            flow = flow,
            runId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture),
            generatedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture),
        };

        // dry-run 統計「預計」數量（applied 尚未發生），apply 只統計實際套用的引用。
        public static Summary ComputeSummary(IEnumerable<Item> items, bool planned = false)
        {
            var s = new Summary();
            var prefabs = new HashSet<string>();
            foreach (var i in items)
            {
                if (i.status == StatusMissing) s.missing++;
                if (i.status == StatusBlocked) s.blocked++;
                if (i.status == StatusSkipped || i.action == ActionSkipped) s.skipped++;
                if (i.status != StatusOk) continue;
                if (i.action == ActionFileOverwrite) s.filesOverwritten++;
                if (i.action != ActionRefSwap) continue;
                foreach (var u in i.usedBy.Where(u => u.writtenHere && (u.applied || planned)))
                {
                    s.spritesReplaced++;
                    prefabs.Add(u.prefab);
                }
            }
            s.prefabsChanged = prefabs.Count;
            return s;
        }

        private static void Finish(Report report)
        {
            report.summary = ComputeSummary(report.items, report.mode == ModeDryRun);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, ToJson(report));
            }
            catch (Exception e)
            {
                report.errors.Add("寫出報告失敗：" + e.Message);
                Debug.LogException(e);
            }
        }

        public static string ToJson(Report r)
        {
            var items = r.items.Select(i => (object)new J
            {
                { "oldSprite", i.oldSprite }, { "action", i.action }, { "newSource", i.newSource },
                { "status", i.status }, { "reasonCode", i.reasonCode }, { "reason", i.reason },
                { "targetAsset", i.targetAsset },
                { "oldSize", i.oldSize }, { "newSize", i.newSize }, { "border", i.border }, { "newBorder", i.newBorder },
                { "borderValidAfter", i.borderValidAfter }, { "warnings", i.warnings },
                { "usedBy", i.usedBy.Select(u => (object)new J
                    {
                        { "prefab", u.prefab }, { "path", u.path }, { "component", u.component }, { "property", u.property },
                        { "relation", u.relation }, { "owner", u.owner }, { "writtenHere", u.writtenHere },
                        { "applied", u.applied }, { "note", u.note },
                    }).ToList()
                },
            }).ToList();

            var json = new J
            {
                { "toolVersion", r.toolVersion }, { "mode", r.mode }, { "runId", r.runId }, { "generatedAt", r.generatedAt },
                { "flow", r.flow }, { "targetFolder", r.targetFolder }, { "sourceArtFolder", r.sourceArtFolder },
                { "usageScope", r.usageScope }, { "planComplete", r.planComplete }, { "errors", r.errors },
                { "items", items },
                { "protectedContract", new J
                    {
                        { "checked", r.contractChecked }, { "checkedPrefabs", r.contractCheckedPrefabs },
                        { "protectedComponents", ProtectedComponentTypes },
                        { "writableFields", new[] { "Image.m_Sprite", "Selectable.m_SpriteState.*", "RawImage.m_Texture" } },
                        { "violations", r.violations.Select(v => (object)new J
                            {
                                { "prefab", v.prefab }, { "field", v.field }, { "before", v.before }, { "after", v.after }, { "kind", v.kind },
                            }).ToList()
                        },
                    }
                },
                { "mechanicalChecks", r.checks == null ? null : new J
                    {
                        { "passed", r.checks.passed }, { "guidStable", r.checks.guidStable },
                        { "contractClean", r.checks.contractClean }, { "summaryConsistent", r.checks.summaryConsistent },
                        { "hashConsistent", r.checks.hashConsistent }, { "failures", r.checks.failures },
                        { "executedCounters", SummaryJson(r.checks.executedCounters) },
                    }
                },
                { "execution", r.execution == null ? null : new J
                    {
                        { "completed", r.execution.completed }, { "cancelled", r.execution.cancelled },
                        { "rolledBack", r.execution.rolledBack }, { "reason", r.execution.reason },
                        { "backupFolder", r.execution.backupFolder },
                    }
                },
                { "atlases", new J { { "affected", r.atlasesAffected }, { "repacked", r.atlasesRepacked }, { "note", r.atlasNote } } },
                { "summary", SummaryJson(r.summary) },
            };
            return json.ToJson();
        }

        private static J SummaryJson(Summary s) => s == null ? null : new J
        {
            { "filesOverwritten", s.filesOverwritten }, { "spritesReplaced", s.spritesReplaced },
            { "prefabsChanged", s.prefabsChanged }, { "missing", s.missing }, { "blocked", s.blocked }, { "skipped", s.skipped },
        };

        private static string Signature(Report r)
        {
            var sb = new StringBuilder();
            foreach (var i in r.items)
            {
                sb.Append(i.oldSprite).Append('|').Append(i.action).Append('|').Append(i.status).Append('|')
                  .Append(i.reasonCode).Append('|').Append(i.newSource).Append('|').Append(i.targetHashBefore).Append('|')
                  .Append(i.sourceHash).Append('\n');
                foreach (var u in i.usedBy)
                    sb.Append("  ").Append(u.prefab).Append('|').Append(u.path).Append('|').Append(u.component)
                      .Append('|').Append(u.owner).Append('|').Append(u.relation).Append('\n');
            }
            foreach (var kv in r.scannedPrefabHashes.OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
            return sb.ToString();
        }

        // ── 小工具 ──────────────────────────────────────────────────────────
        private static Item Block(Item item, string code, string reason)
        {
            item.status = StatusBlocked;
            item.reasonCode = code;
            item.reason = reason;
            return item;
        }

        private static Item Skip(Item item, string code, string reason)
        {
            item.status = StatusSkipped;
            item.reasonCode = code;
            item.reason = reason;
            return item;
        }

        private static string MissingReason(Item item, string fileName)
        {
            var sb = new StringBuilder($"美術來源資料夾缺少 {fileName}：需 {SizeText(item.oldSize)} px");
            // 不論有沒有 9-slice 都寫明：沒寫的話，美術無法分辨「沒有邊界限制」和「工具漏給資訊」。
            sb.Append(item.border.Any(v => v > 0)
                ? $"，9-slice 邊界 L{item.border[0]}/B{item.border[1]}/R{item.border[2]}/T{item.border[3]}（尺寸需與舊圖相同）"
                : "，無 9-slice 邊界（整張顯示，建議維持相同尺寸）");
            var prefabs = item.usedBy.Select(u => u.prefab).Distinct().ToList();
            if (prefabs.Count > 0)
                sb.Append($"；用在 {prefabs.Count} 個 Prefab 的 {item.usedBy.Count} 處：")
                  .Append(Preview(item.usedBy.Select(u => Path.GetFileNameWithoutExtension(u.prefab) + ":" + u.path + "(" + u.component + ")").ToList(), 4));
            else
                sb.Append("；使用範圍內沒有 Prefab 引用。");
            return sb.Append('。').ToString();
        }

        private static string Preview(List<string> values, int max) =>
            string.Join("、", values.Take(max)) + (values.Count > max ? $" 等 {values.Count} 個" : string.Empty);

        private static string SizeText(int[] size) => size == null ? "?" : size[0] + "x" + size[1];

        private static int[] BorderArray(Vector4 b) =>
            new[] { Mathf.RoundToInt(b.x), Mathf.RoundToInt(b.y), Mathf.RoundToInt(b.z), Mathf.RoundToInt(b.w) };

        private static int[] RectSize(Sprite s) =>
            s == null ? null : new[] { Mathf.RoundToInt(s.rect.width), Mathf.RoundToInt(s.rect.height) };

        private static bool IsWholeTexture(Sprite s) =>
            s != null && s.texture != null &&
            Mathf.RoundToInt(s.rect.width) == s.texture.width && Mathf.RoundToInt(s.rect.height) == s.texture.height;

        private static string FindSameStemOtherExtension(string dir, string stem, string ext)
        {
            foreach (var candidate in new[] { ".png", ".jpg", ".jpeg", ".tga", ".psd" })
            {
                if (string.Equals(candidate, ext, StringComparison.OrdinalIgnoreCase)) continue;
                var p = Normalize(Path.Combine(dir, stem + candidate));
                if (File.Exists(p)) return p;
            }
            return null;
        }

        // PNG 直接讀 IHDR（不解碼）；其他格式才整張載入取尺寸。
        private static int[] ReadImageSize(string absPath)
        {
            if (string.IsNullOrEmpty(absPath) || !File.Exists(absPath)) return null;
            var bytes = File.ReadAllBytes(absPath);
            if (bytes.Length >= 24 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return new[]
                {
                    (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19],
                    (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23],
                };
            var tex = new Texture2D(2, 2);
            try
            {
                return tex.LoadImage(bytes) ? new[] { tex.width, tex.height } : null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        private static string Sha256(string absPath)
        {
            if (!File.Exists(absPath)) return null;
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(absPath))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string ReadMetaGuid(string absAssetOrMetaPath)
        {
            var meta = absAssetOrMetaPath.EndsWith(".meta", StringComparison.Ordinal) ? absAssetOrMetaPath : absAssetOrMetaPath + ".meta";
            if (!File.Exists(meta)) return null;
            foreach (var line in File.ReadAllLines(meta))
                if (line.StartsWith("guid:", StringComparison.Ordinal))
                    return line.Substring(5).Trim();
            return null;
        }

        private static string HierarchyPath(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (var cur = t; cur != null; cur = cur.parent)
            {
                parts.Add(cur.name);
                if (cur == root) break;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string IndexedPath(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (var cur = t; cur != null; cur = cur.parent)
            {
                parts.Add(cur == root ? cur.name : cur.name + "[" + cur.GetSiblingIndex() + "]");
                if (cur == root) break;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string ToAbs(string assetPath) => ProjectRoot + "/" + assetPath;

        private static bool IsReadOnly(string assetPath)
        {
            var abs = ToAbs(assetPath);
            return File.Exists(abs) && (File.GetAttributes(abs) & FileAttributes.ReadOnly) != 0;
        }

        private static string Normalize(string path) => string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');

        private static string SpriteKey(Sprite sprite)
        {
            if (sprite == null) return null;
            var path = AssetDatabase.GetAssetPath(sprite);
            return string.IsNullOrEmpty(path) ? null : path + "#" + sprite.name;
        }

        // 保留欄位順序的極小 JSON 物件；null 輸出為 null，陣列輸出為單行。
        private sealed class J : List<KeyValuePair<string, object>>
        {
            public void Add(string key, object value) => Add(new KeyValuePair<string, object>(key, value));

            public string ToJson()
            {
                var sb = new StringBuilder();
                Write(sb, this, 0);
                return sb.ToString();
            }

            private static void Write(StringBuilder sb, object v, int indent)
            {
                switch (v)
                {
                    case null: sb.Append("null"); return;
                    case string s: WriteString(sb, s); return;
                    case bool b: sb.Append(b ? "true" : "false"); return;
                    case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); return;
                    case J o:
                        sb.Append('{');
                        for (var k = 0; k < o.Count; k++)
                        {
                            sb.Append(k == 0 ? "\n" : ",\n").Append(' ', (indent + 1) * 2);
                            WriteString(sb, o[k].Key);
                            sb.Append(": ");
                            Write(sb, o[k].Value, indent + 1);
                        }
                        if (o.Count > 0) sb.Append('\n').Append(' ', indent * 2);
                        sb.Append('}');
                        return;
                    case System.Collections.IEnumerable list:
                        var items = list.Cast<object>().ToList();
                        var multiline = items.Any(e => e is J);
                        sb.Append('[');
                        for (var k = 0; k < items.Count; k++)
                        {
                            if (multiline) sb.Append(k == 0 ? "\n" : ",\n").Append(' ', (indent + 1) * 2);
                            else if (k > 0) sb.Append(", ");
                            Write(sb, items[k], indent + 1);
                        }
                        if (multiline && items.Count > 0) sb.Append('\n').Append(' ', indent * 2);
                        sb.Append(']');
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
}

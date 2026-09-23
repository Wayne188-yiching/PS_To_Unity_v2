using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PhotoshopToUnity.EditorImporter;
using UnityEditor;
using UnityEngine;

// AFTER：跑目前的 PsUiSkinApplier，除了主報告，另外驗證五件事：
//   1. dry-run 不寫任何檔案       2. apply 後第二道機械檢查
//   3. RollbackLastApply 回到原狀  4. 預覽後內容變更 → 拒絕執行、零寫入
//   5. 執行中途失敗 → 自動還原、零殘留
namespace PsUiReskinAcceptance
{
    public static class ReskinAcceptanceAfter
    {
        public static void RunThemeBatch() => BatchRunner.Run("after_theme", RunTheme);
        public static void RunFolderBatch() => BatchRunner.Run("after_folder", RunFolder);

        public static void RunTheme()
        {
            AssetDatabase.Refresh();
            var theme = AssetDatabase.LoadAssetAtPath<PsUiSkinTheme>(ReskinFixture.ThemePath);
            RunScenario("theme", () => PsUiSkinApplier.PlanTheme(theme));
        }

        public static void RunFolder()
        {
            AssetDatabase.Refresh();
            RunScenario("folder", () => PsUiSkinApplier.PlanFolder(ReskinFixture.NewArtFolder, ReskinFixture.SpritesFolder, ReskinFixture.Root));
        }

        private static void RunScenario(string tag, Func<PsUiSkinApplier.Report> plan)
        {
            var scenarios = new JObj();
            var pristine = Snapshot.Take();

            // 1. dry-run
            var dry = plan();
            File.Copy(PsUiSkinApplier.ReportPath, $"{ReskinFixture.OutDir}/after_{tag}_dryrun.json", true);
            var afterDry = Snapshot.Take();
            scenarios.Add("dryRunWritesNothing", Diff(pristine, afterDry).Count == 0);

            // 2. apply + 第二道機械檢查
            var applied = PsUiSkinApplier.Execute(dry);
            AssetDatabase.Refresh();
            var afterApply = Snapshot.Take();
            var report = BatchRunner.Parse(File.ReadAllText(PsUiSkinApplier.ReportPath));
            var external = IndependentChecks.Run(pristine, afterApply, report, BatchRunner.ConsoleErrors);
            var reportObj = new JObj(report);
            reportObj.Add("externalChecks", external);
            File.WriteAllText($"{ReskinFixture.OutDir}/after_{tag}_report.json", reportObj.ToJson());
            scenarios.Add("applyCompleted", applied.execution != null && applied.execution.completed);
            scenarios.Add("applierChecksPassed", applied.checks != null && applied.checks.passed);
            scenarios.Add("externalChecksPassed", (bool)external["passed"]);

            // 3. rollback
            var rolledBack = PsUiSkinApplier.RollbackLastApply(out var rollbackMessage);
            AssetDatabase.Refresh();
            var rollbackDiff = Diff(pristine, Snapshot.Take());
            scenarios.Add("rollbackRestoresPristine", rolledBack && rollbackDiff.Count == 0);
            scenarios.Add("rollbackMessage", rollbackMessage);
            scenarios.Add("rollbackDiff", rollbackDiff);

            // 4. 預覽後改動來源 → 必須拒絕執行
            var stalePlan = plan();
            var ok = stalePlan.items.FirstOrDefault(i => i.status == PsUiSkinApplier.StatusOk && i.action == PsUiSkinApplier.ActionFileOverwrite);
            var staleOk = false;
            if (ok != null)
            {
                var original = File.ReadAllBytes(ok.newSource);
                ReskinFixture.WritePng(ok.newSource, ok.newSize[0], ok.newSize[1], new Color32(1, 2, 3, 255), new Color32(4, 5, 6, 255), 99);
                var before = Snapshot.Take();
                var stale = PsUiSkinApplier.Execute(stalePlan);
                var diff = Diff(before, Snapshot.Take());
                staleOk = stale.execution != null && !stale.execution.completed && diff.Count == 0 &&
                          stale.items.All(i => i.status != PsUiSkinApplier.StatusOk);
                File.WriteAllBytes(ok.newSource, original);
                scenarios.Add("staleReason", stale.execution?.reason);
            }
            scenarios.Add("stalePlanRefused", ok == null ? (object)"n/a" : staleOk);

            // 5. 中途失敗 → 自動還原：前幾次寫入放行，最後一次寫入前丟例外
            var failPlan = plan();
            var writesPlanned = failPlan.summary.filesOverwritten + failPlan.summary.prefabsChanged;
            if (writesPlanned >= 2)
            {
                var calls = 0;
                PsUiSkinApplier.BeforeWriteForTests = path =>
                {
                    if (++calls == writesPlanned) throw new IOException("acceptance: injected failure before writing " + path);
                };
                PsUiSkinApplier.Report failed;
                try { failed = PsUiSkinApplier.Execute(failPlan); }
                finally { PsUiSkinApplier.BeforeWriteForTests = null; }
                // 注入的例外會被 applier 以 Debug.LogException 記錄，這是預期中的，不算 Console 新增 error。
                BatchRunner.ConsoleErrors.RemoveAll(e => e.Contains("acceptance: injected failure"));
                AssetDatabase.Refresh();
                var diff = Diff(pristine, Snapshot.Take());
                scenarios.Add("midwayWritesBeforeFailure", writesPlanned - 1);
                scenarios.Add("midwayFailureRolledBack",
                    failed.execution != null && failed.execution.rolledBack && diff.Count == 0 &&
                    failed.items.All(i => i.status != PsUiSkinApplier.StatusOk));
                scenarios.Add("midwayFailureReason", failed.execution?.reason);
                scenarios.Add("midwayFailureDiff", diff);
                File.Copy(PsUiSkinApplier.ReportPath, $"{ReskinFixture.OutDir}/after_{tag}_midway_failure.json", true);
            }
            else
            {
                scenarios.Add("midwayFailureRolledBack", "n/a");
            }

            scenarios.Add("consoleErrors", BatchRunner.ConsoleErrors.ToList());
            File.WriteAllText($"{ReskinFixture.OutDir}/after_{tag}_scenarios.json", scenarios.ToJson());
        }

        // 回傳內容或 GUID 有差異的檔案（Logs/ 下的報告與備份在 Assets 外，不會出現在這裡）。
        private static List<string> Diff(Snapshot a, Snapshot b)
        {
            var diff = new List<string>();
            foreach (var k in a.hash.Keys.Union(b.hash.Keys))
            {
                a.hash.TryGetValue(k, out var x);
                b.hash.TryGetValue(k, out var y);
                if (x != y) diff.Add(k);
            }
            return diff;
        }
    }
}

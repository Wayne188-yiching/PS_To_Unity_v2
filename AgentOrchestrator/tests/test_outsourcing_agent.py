import asyncio
import io
import json
import os
import tempfile
import unittest
import zipfile
from contextlib import redirect_stdout
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

from ps_to_unity_agents.models import (
    OutsourcingAgentDecision,
    OutsourcingQcFinding,
    OutsourcingRequest,
    Status,
)
from agent_roles.ui_outsourcing_producer import (
    AGENT_DISPLAY_NAME,
    OutsourcingCaseTools,
    build_outsourcing_agent,
    enforce_approval_gate,
    load_outsourcing_knowledge,
    load_outsourcing_projects,
    require_api_transmission_approval,
    _extract_asset_paths,
    _inventory_task_assets,
    _read_source,
    render_qc_markdown,
)
from main import run_outsourcing_agent


TEST_TEMP_ROOT = Path(__file__).resolve().parents[1] / ".test_tmp"
TEST_TEMP_ROOT.mkdir(exist_ok=True)


class OutsourcingAgentTests(unittest.TestCase):
    def make_request(self, root: Path, *, workflow: str = "brief") -> OutsourcingRequest:
        spec = root / "spec.md"
        spec.write_text("製作活動頁，包含可領取與已領取狀態。", encoding="utf-8")
        return OutsourcingRequest(
            case_id="test_outsource",
            task_title="活動頁 UI",
            workflow=workflow,
            spec_paths=[spec],
            output_folder=root / "run",
        )

    def test_curated_knowledge_contains_unity_and_approval_rules(self):
        knowledge = load_outsourcing_knowledge()
        self.assertGreaterEqual(len(knowledge["sources"]), 5)
        self.assertIn("unityArchitecture", knowledge)
        self.assertTrue(any("正式外包回覆" in rule for rule in knowledge["qcPolicy"]["feedbackStyle"]))

        atlas_rules = "\n".join(knowledge["unityArchitecture"]["atlasLocalization"])
        self.assertIn("<Module>_Atlas_CHS", atlas_rules)
        self.assertIn("Packables 只指向單一資料夾", atlas_rules)
        self.assertIn("Sprite Mode = Single", atlas_rules)

        tool_validation = knowledge["unityArchitecture"]["psToUnityToolValidation"]
        self.assertEqual("CONFIRMED_AGAINST_CURRENT_SOURCE", tool_validation["status"])
        self.assertTrue(any("一次選一個語系" in rule for rule in tool_validation["operatorConstraints"]))

    def test_ps_to_unity_source_matches_registered_atlas_rules(self):
        workspace = Path(__file__).resolve().parents[2]
        exporter = (workspace / "PhotoshopExporter" / "PhotoshopUiPackageExporter.jsx").read_text(
            encoding="utf-8-sig"
        )
        importer_window = (
            workspace / "Assets" / "Editor" / "PhotoshopUiImporter" / "PhotoshopUiImporterWindow.cs"
        ).read_text(encoding="utf-8-sig")
        image_importer = (
            workspace / "Assets" / "Editor" / "PhotoshopUiImporter" / "ImageImportService.cs"
        ).read_text(encoding="utf-8-sig")

        self.assertIn('"/Atlas/SpriteAtlas/" + language', exporter)
        self.assertIn('language === "Base" ? "" : "_" + language', exporter)
        self.assertIn('{ "Base", "CHS", "CHT", "EN" }', importer_window)
        self.assertIn('atlas.Add(new Object[] { folderObject })', importer_window)
        self.assertIn('importer.spriteImportMode = SpriteImportMode.Single', image_importer)

    def test_registered_project_contains_store_work_profile(self):
        project = load_outsourcing_projects()["projects"]["DemoGame"]
        store_profile = next(item for item in project["workProfiles"] if "商城" in item["name"])
        context = "\n".join(store_profile["taskContext"])
        self.assertIn("feature/store-ui", context)
        self.assertIn("Assets/Temp/Shop", context)
        self.assertIn("StorePanel.prefab", context)
        self.assertIn("MiSans-Demibold SDF.asset", context)

    def test_valid_brief_case_collects_requirements(self):
        with tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT) as temp:
            request = self.make_request(Path(temp))
            evidence = OutsourcingCaseTools(request).inspect()
            self.assertEqual("PASS", evidence["status"])
            self.assertEqual("brief", evidence["workflow"])
            self.assertEqual("spec.md", evidence["sources"][0]["name"])

    def test_xlsx_reader_prioritizes_late_postproduction_content(self):
        with tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT) as temp:
            workbook = Path(temp) / "spec.xlsx"
            with zipfile.ZipFile(workbook, "w") as archive:
                archive.writestr("xl/workbook.xml", """
                    <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                      <sheets><sheet name="會議記錄" sheetId="1" r:id="rId1"/></sheets>
                    </workbook>
                """)
                archive.writestr("xl/_rels/workbook.xml.rels", """
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      <Relationship Id="rId1" Target="worksheets/sheet1.xml" Type="worksheet"/>
                    </Relationships>
                """)
                archive.writestr("xl/sharedStrings.xml", """
                    <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                      <si><t>範例專案後製流程</t></si>
                    </sst>
                """)
                archive.writestr("xl/worksheets/sheet1.xml", """
                    <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                      <sheetData><row r="306"><c r="C306" t="s"><v>0</v></c></row></sheetData>
                    </worksheet>
                """)
            content = _read_source(workbook)
            self.assertIn("[活頁簿關鍵內容]", content)
            self.assertIn("第306列", content)
            self.assertIn("範例專案後製流程", content)

    def test_asset_path_reader_splits_chinese_list_separators(self):
        paths = _extract_asset_paths(
            "共用 Prefab：Assets/Prefab/A.prefab、Assets/Prefab/B.prefab "
            "與 Assets/Prefab/C.prefab；請在 Assets/Scene/Lobby.unity 的 PopWindow 下驗證。"
        )
        self.assertEqual({
            "Assets/Prefab/A.prefab",
            "Assets/Prefab/B.prefab",
            "Assets/Prefab/C.prefab",
            "Assets/Scene/Lobby.unity",
        }, paths)

    def test_asset_path_reader_stops_before_chinese_prose(self):
        paths = _extract_asset_paths(
            "共用素材根目錄 Assets/BundleSources/Share/Sorted/Atlas/SpriteAtlas 是唯讀共用區。"
        )
        self.assertEqual({
            "Assets/BundleSources/Share/Sorted/Atlas/SpriteAtlas",
        }, paths)

    def test_task_asset_inventory_counts_existing_inputs_without_meta_files(self):
        with tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT) as temp:
            project = Path(temp)
            task_folder = project / "Assets" / "Temp" / "Shop"
            task_folder.mkdir(parents=True)
            for name in ("layout.png", "reference.jpg", "shop.spriteatlasv2", "layout.png.meta"):
                (task_folder / name).write_text("fixture", encoding="utf-8")

            inventory = _inventory_task_assets(project, [
                "Assets/Temp/Shop",
                "Assets/Temp/Shop/",
            ])

            self.assertEqual(1, len(inventory))
            self.assertEqual(3, inventory[0]["fileCount"])
            self.assertEqual({".jpg": 1, ".png": 1, ".spriteatlasv2": 1}, inventory[0]["extensionCounts"])
            self.assertTrue(inventory[0]["materialsReady"])

    def test_confirmed_decisions_are_exposed_as_authoritative_evidence(self):
        with tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT) as temp:
            request = self.make_request(Path(temp)).model_copy(update={
                "confirmed_decisions": ["只發包 Part 2，Part 1 排除。"],
            })
            evidence = OutsourcingCaseTools(request).inspect()
            self.assertEqual(["只發包 Part 2，Part 1 排除。"], evidence["confirmedDecisions"])

            agent = build_outsourcing_agent(request)
            self.assertIn("confirmedDecisions is the highest-priority", agent.instructions)
            self.assertIn("must not be asked again", agent.instructions)

    def test_qc_requires_delivery_evidence(self):
        with tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT) as temp:
            request = self.make_request(Path(temp), workflow="qc")
            evidence = OutsourcingCaseTools(request).inspect()
            self.assertEqual("BLOCKED", evidence["status"])
            self.assertTrue(any(issue["code"] == "QC_EVIDENCE_MISSING" for issue in evidence["issues"]))

    def test_agent_has_human_discussion_gate(self):
        with tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT) as temp:
            agent = build_outsourcing_agent(self.make_request(Path(temp)))
            self.assertEqual(AGENT_DISPLAY_NAME, agent.name)
            self.assertIn("ready_for_vendor must be false", agent.instructions)
            self.assertIn("untrusted case evidence", agent.instructions)
            self.assertIn("full specification separately", agent.instructions)
            self.assertIn("contain only: branch", agent.instructions)
            self.assertIn("do not make the user retype", agent.instructions)
            self.assertIn("psToUnityToolValidation", agent.instructions)

    def test_api_transmission_requires_explicit_case_approval(self):
        with tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT) as temp:
            request = self.make_request(Path(temp))
            with self.assertRaises(PermissionError):
                require_api_transmission_approval(request)
            require_api_transmission_approval(request.model_copy(
                update={"api_transmission_approved": True}
            ))

    def test_qc_renderer_marks_vendor_copy_as_unapproved(self):
        decision = OutsourcingAgentDecision(
            status=Status.NEEDS_REVIEW,
            summary="需要討論",
            next_action="與使用者確認",
            workflow="qc",
            qc_findings=[OutsourcingQcFinding(
                category="must_fix",
                area="layout",
                finding="三個任務項目未等距。",
                evidence="第二項與第三項間距不同。",
                requested_action="統一三個項目的垂直間距。",
            )],
            vendor_feedback_draft="請統一任務項目的垂直間距。",
        )
        rendered = render_qc_markdown(decision)
        self.assertIn("外包回覆草稿（尚未核准）", rendered)
        self.assertIn("must_fix｜layout", rendered)

    def test_approval_gate_overrides_model_readiness(self):
        with tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT) as temp:
            request = self.make_request(Path(temp))
            decision = OutsourcingAgentDecision(
                status=Status.PASS,
                summary="草稿完成",
                next_action="送出",
                workflow="brief",
                ready_for_vendor=True,
                user_discussion_required=False,
            )
            gated = enforce_approval_gate(decision, request)
            self.assertEqual(Status.NEEDS_REVIEW, gated.status)
            self.assertTrue(gated.user_discussion_required)
            self.assertFalse(gated.ready_for_vendor)

    def test_approval_still_does_not_clear_must_fix_findings(self):
        with tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT) as temp:
            request = self.make_request(Path(temp), workflow="qc").model_copy(
                update={"user_approved_output": True}
            )
            decision = OutsourcingAgentDecision(
                status=Status.PASS,
                summary="QC 完成",
                next_action="送出",
                workflow="qc",
                qc_findings=[OutsourcingQcFinding(
                    category="must_fix",
                    area="unity",
                    finding="SortingLayer 錯誤。",
                    evidence="目前為 Default。",
                    requested_action="改為 UI。",
                )],
            )
            gated = enforce_approval_gate(decision, request)
            self.assertEqual(Status.NEEDS_REVIEW, gated.status)
            self.assertFalse(gated.ready_for_vendor)

    def test_live_outsourcing_run_applies_approval_gate_before_writing(self):
        with tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT) as temp:
            request = self.make_request(Path(temp)).model_copy(update={
                "api_transmission_approved": True,
            })
            model_decision = OutsourcingAgentDecision(
                status=Status.PASS,
                summary="模型誤判為可交付",
                next_action="交付",
                workflow="brief",
                ready_for_vendor=True,
                user_discussion_required=False,
            )
            with (
                patch.dict(os.environ, {"OPENAI_API_KEY": "test-key"}, clear=False),
                patch("main.load_local_key"),
                patch("main.build_outsourcing_agent", return_value=object()),
                patch("main.Runner.run", new=AsyncMock(return_value=SimpleNamespace(
                    final_output=model_decision
                ))),
            ):
                with redirect_stdout(io.StringIO()):
                    exit_code = asyncio.run(run_outsourcing_agent(request))

            saved = json.loads((request.output_folder / "outsourcing_agent_result.json").read_text(
                encoding="utf-8"
            ))
            self.assertEqual(0, exit_code)
            self.assertEqual("NEEDS_REVIEW", saved["status"])
            self.assertTrue(saved["user_discussion_required"])
            self.assertFalse(saved["ready_for_vendor"])


if __name__ == "__main__":
    unittest.main()

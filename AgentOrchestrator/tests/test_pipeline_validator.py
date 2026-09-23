import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from ps_to_unity_agents.fingerprints import sha256_file
from ps_to_unity_agents.models import AgentDecision, PipelineRequest, Status
from ps_to_unity_agents.pipeline_validator import PipelineValidatorController


class PipelineValidatorTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.request = PipelineRequest(
            case_id="validator", psd_path=root / "input.psd", layout_json_path=root / "layout.json",
            artist_asset_folder=root / "artist", exporter_asset_folder=root / "images", output_folder=root / "output",
            execution_mode="execute", semantics_approved=True,
            unity_default_tmp_font_asset="Assets/Fonts/Main.asset",
            unity_default_tmp_material_preset="Assets/Fonts/Main.mat",
        )
        self.layout = {
            "canvas": {"width": 400, "height": 300},
            "nodes": [
                {"name": "BTN_Play", "type": "image", "x": 10, "y": 20, "width": 100, "height": 40,
                 "visible": True, "imageType": "sliced", "imagePath": "play.png"},
                {"name": "Title", "type": "text", "x": 20, "y": 70, "width": 200, "height": 30,
                 "visible": True, "text": "Hello", "fontSize": 24, "color": "#FFFFFFFF"},
            ],
        }
        self.snapshots = [
            {"path": "Prefab", "name": "Prefab", "components": ["RectTransform", "CanvasGroup", "RectMask2D"]},
            {"path": "Prefab/0:BTN_Play", "name": "BTN_Play", "x": 10, "y": 20, "width": 100, "height": 40,
             "components": ["RectTransform", "Image", "Button"], "spriteAssetPath": "Assets/Sprites/play.png", "imageType": "Sliced"},
            {"path": "Prefab/1:Title", "name": "Title", "x": 20, "y": 70, "width": 200, "height": 30,
             "components": ["RectTransform", "TextMeshProUGUI"], "text": "Hello", "fontSize": 24,
             "characterSpacing": 0, "lineSpacing": 0, "textAlignment": "Center", "textColor": "#FFFFFFFF",
             "textVertexGradient": False,
             "fontAssetPath": "Assets/Fonts/Main.asset", "materialAssetPath": "Assets/Fonts/Main.mat"},
        ]

    def result(self, status="PASS"):
        return {"status": status, "runId": "run", "importResult": {
            "prefabNodes": self.snapshots,
            "imageBindings": [{
                "sourceName": "BTN_Play", "imagePath": "play.png", "role": "image", "sourceKind": "imported",
                "spriteAssetPath": "Assets/Sprites/play.png",
            }],
            "importedImages": [{
                "imagePath": "play.png", "spriteAssetPath": "Assets/Sprites/play.png",
                "sourcePixelHash": "pixels", "spritePixelHash": "pixels",
            }],
            "textBindings": [{
                "sourceName": "Title", "fontToken": "", "materialToken": "", "role": "text",
                "resolutionSource": "default", "matchedFontKeyword": None,
                "fontAssetPath": "Assets/Fonts/Main.asset", "materialAssetPath": "Assets/Fonts/Main.mat",
            }],
        }}

    def test_core_image_text_contract_passes(self):
        result = PipelineValidatorController(self.request)._compare(self.layout, self.snapshots, self.result())
        self.assertEqual("PASS", result["status"])
        self.assertEqual("PSD_IR_TO_GENERATED_PREFAB", result["validationScope"])
        self.assertEqual("NOT_RUN", result["liveAcceptance"])

    def test_geometry_and_tmp_asset_mismatch_block(self):
        self.snapshots[1]["x"] = 99
        self.snapshots[2]["materialAssetPath"] = ""
        result = PipelineValidatorController(self.request)._compare(self.layout, self.snapshots, self.result())
        self.assertEqual("BLOCKED", result["status"])
        codes = {issue["code"] for issue in result["issues"]}
        self.assertIn("PREFAB_GEOMETRY_MISMATCH", codes)
        self.assertIn("TMP_ASSET_NOT_BOUND", codes)

    def test_wired_scrollbar_handle_uses_runtime_geometry(self):
        node = {
            "name": "Handle", "type": "image", "visible": True,
            "x": 10, "y": 20, "width": 30, "height": 100,
            "imagePath": "handle.png", "scrollbarRole": "handle",
        }
        snapshot = {
            "path": "Prefab/0:Bar/1:SlidingArea/0:Handle", "name": "Handle",
            "x": 0, "y": 0, "width": 0, "height": 0,
            "components": ["RectTransform", "Image"],
            "spriteAssetPath": "Assets/Sprites/handle.png", "imageType": "Simple",
        }
        scrollbar = {
            "path": "Prefab/0:Bar", "name": "Bar",
            "components": ["RectTransform", "Scrollbar"],
            "scrollbarHandlePath": snapshot["path"],
        }
        binding = [{
            "sourceName": "Handle", "imagePath": "handle.png", "role": "image",
            "sourceKind": "imported", "spriteAssetPath": "Assets/Sprites/handle.png",
        }]
        imported = {"handle.png": {
            "imagePath": "handle.png", "spriteAssetPath": "Assets/Sprites/handle.png",
            "sourcePixelHash": "pixels", "spritePixelHash": "pixels",
        }}
        issues = []
        PipelineValidatorController(self.request)._validate_node(
            {"node": node, "sourcePath": "0:Bar/0:Handle", "parentSourcePath": "",
             "parentName": None, "layoutAncestor": False},
            snapshot, [self.snapshots[0], scrollbar, snapshot],
            {"canvas": {"width": 400, "height": 300}}, issues, binding, imported, [],
        )
        self.assertNotIn("PREFAB_GEOMETRY_MISMATCH", {issue["code"] for issue in issues})

        scrollbar["scrollbarHandlePath"] = ""
        issues = []
        PipelineValidatorController(self.request)._validate_node(
            {"node": node, "sourcePath": "0:Bar/0:Handle", "parentSourcePath": "",
             "parentName": None, "layoutAncestor": False},
            snapshot, [self.snapshots[0], scrollbar, snapshot],
            {"canvas": {"width": 400, "height": 300}}, issues, binding, imported, [],
        )
        self.assertIn("PREFAB_GEOMETRY_MISMATCH", {issue["code"] for issue in issues})

    def test_hidden_node_must_not_be_exported(self):
        self.layout["nodes"].append({"name": "HiddenGuide", "type": "image", "visible": False})
        self.snapshots.append({"path": "Prefab/2:HiddenGuide", "name": "HiddenGuide", "components": ["RectTransform", "Image"]})
        result = PipelineValidatorController(self.request)._compare(self.layout, self.snapshots, self.result())
        self.assertEqual("BLOCKED", result["status"])
        self.assertIn("HIDDEN_NODE_EXPORTED", {issue["code"] for issue in result["issues"]})

    def test_hidden_visible_name_collision_blocks_when_extra_node_exists(self):
        self.layout["nodes"].append({"name": "BTN_Play", "type": "group", "visible": False})
        self.snapshots.append({
            "path": "Prefab/2:BTN_Play", "name": "BTN_Play", "components": ["RectTransform"],
        })
        result = PipelineValidatorController(self.request)._compare(self.layout, self.snapshots, self.result())
        self.assertEqual("BLOCKED", result["status"])
        self.assertIn("HIDDEN_NODE_EXPORTED", {issue["code"] for issue in result["issues"]})

    def test_scroll_and_scrollbar_wiring_are_required(self):
        layout = {"canvas": {"width": 400, "height": 300}, "nodes": [{
            "name": "List", "type": "group", "visible": True, "scrollDirection": "vertical",
            "children": [{"name": "Bar", "type": "group", "visible": True, "scrollbarDirection": "vertical"}],
        }]}
        snapshots = [
            self.snapshots[0],
            {"path": "Prefab/0:List", "name": "List", "components": ["RectTransform", "ScrollRect"],
             "scrollHorizontal": False, "scrollVertical": True, "viewportPath": "Prefab/0:List/0:Viewport",
             "contentPath": "Prefab/0:List/0:Viewport/0:Content", "verticalScrollbarPath": "Prefab/0:List/1:Bar",
             "scrollMovementType": "Elastic", "scrollInertia": True, "scrollDecelerationRate": 0.135,
             "scrollSensitivity": 50},
            {"path": "Prefab/0:List/0:Viewport", "name": "Viewport",
             "components": ["RectTransform", "RectMask2D", "Image"], "imageRaycastTarget": True},
            {"path": "Prefab/0:List/0:Viewport/0:Content", "name": "Content",
             "components": ["RectTransform"], "x": 0, "y": 0, "width": 0, "height": 0},
            {"path": "Prefab/0:List/1:Bar", "name": "Bar", "components": ["RectTransform", "Scrollbar"],
             "scrollbarHandlePath": "Prefab/0:List/1:Bar/2:SlidingArea/0:Handle",
             "scrollbarDirection": "BottomToTop", "scrollbarTargetGraphicPath": "Prefab/0:List/1:Bar/0:Handle"},
        ]
        result = PipelineValidatorController(self.request)._compare(layout, snapshots, {"status": "PASS", "runId": "run"})
        self.assertEqual("PASS", result["status"])
        snapshots[4]["scrollbarHandlePath"] = ""
        result = PipelineValidatorController(self.request)._compare(layout, snapshots, {"status": "PASS", "runId": "run"})
        self.assertEqual("BLOCKED", result["status"])
        self.assertIn("SCROLLBAR_HANDLE_MISSING", {issue["code"] for issue in result["issues"]})

    def test_scrollbar_must_attach_to_containing_scrollrect_on_correct_axis(self):
        layout = {"canvas": {"width": 400, "height": 300}, "nodes": [{
            "name": "List", "type": "group", "visible": True, "scrollDirection": "vertical",
            "children": [{"name": "Bar", "type": "group", "visible": True, "scrollbarDirection": "vertical"}],
        }]}
        snapshots = [
            self.snapshots[0],
            {"path": "Prefab/0:List", "name": "List", "components": ["RectTransform", "ScrollRect"],
             "scrollHorizontal": False, "scrollVertical": True, "viewportPath": "Prefab/0:List/0:Viewport",
             "contentPath": "Prefab/0:List/0:Viewport/0:Content", "verticalScrollbarPath": "Prefab/1:OtherBar",
             "scrollMovementType": "Elastic", "scrollInertia": True, "scrollDecelerationRate": 0.135,
             "scrollSensitivity": 50},
            {"path": "Prefab/0:List/0:Viewport", "name": "Viewport",
             "components": ["RectTransform", "RectMask2D", "Image"], "imageRaycastTarget": True},
            {"path": "Prefab/0:List/0:Viewport/0:Content", "name": "Content", "components": ["RectTransform"]},
            {"path": "Prefab/0:List/1:Bar", "name": "Bar", "components": ["RectTransform", "Scrollbar"],
             "scrollbarHandlePath": "Prefab/0:List/1:Bar/1:SlidingArea/0:Handle",
             "scrollbarDirection": "BottomToTop", "scrollbarTargetGraphicPath": "Prefab/0:List/1:Bar/1:SlidingArea/0:Handle"},
            {"path": "Prefab/2:Other", "name": "Other", "components": ["RectTransform", "ScrollRect"],
             "scrollVertical": True, "verticalScrollbarPath": "Prefab/0:List/1:Bar"},
        ]
        result = PipelineValidatorController(self.request)._compare(layout, snapshots, {"status": "PASS", "runId": "run"})
        self.assertEqual("BLOCKED", result["status"])
        self.assertIn("SCROLLBAR_NOT_ATTACHED", {issue["code"] for issue in result["issues"]})

    def test_no_unity_evidence_needs_review(self):
        result = PipelineValidatorController(self.request).validate()
        self.assertEqual("NEEDS_REVIEW", result["status"])
        self.assertEqual("UNITY_EVIDENCE_REQUIRED", result["issues"][0]["code"])

    def test_missing_prefab_snapshot_blocks_after_package_gate(self):
        package = {"status": "PASS", "layoutPath": str(self.request.layout_json_path)}
        self.request.layout_json_path.write_text(json.dumps(self.layout))
        receipt = {"layoutSha256": sha256_file(self.request.layout_json_path)}
        with patch.object(PipelineValidatorController, "_load_existing_package", return_value=package), \
             patch("ps_to_unity_agents.pipeline_validator.validate_psd_package_manifest", return_value={"status": "PASS"}):
            result = PipelineValidatorController(self.request).validate({"status": "PASS", "runId": "run", "importResult": receipt})
        self.assertEqual("BLOCKED", result["status"])
        self.assertEqual("PREFAB_SNAPSHOT_MISSING", result["issues"][0]["code"])

    def test_model_cannot_claim_pass_without_deterministic_result(self):
        controller = PipelineValidatorController(self.request)
        decision = AgentDecision(status=Status.PASS, summary="_z", next_action="done")
        self.assertEqual(Status.NEEDS_REVIEW, controller.enforce_decision(decision).status)

    def test_faux_bold_must_reach_tmp(self):
        self.layout["nodes"][1]["fauxBold"] = True
        result = PipelineValidatorController(self.request)._compare(self.layout, self.snapshots, self.result())
        self.assertNotEqual("PASS", result["status"])
        self.snapshots[2]["textBold"] = True
        result = PipelineValidatorController(self.request)._compare(self.layout, self.snapshots, self.result())
        self.assertEqual("PASS", result["status"])

    def _auto_sliced(self, border="20,0,20,0", diff=1, image_type="Sliced"):
        self.layout["nodes"][0].pop("imageType")
        self.snapshots[1]["imageType"] = image_type
        payload = self.result()
        payload["importResult"]["importedImages"][0].update(
            spritePixelHash="compressed", nineSliceBorder=border, reconstructedMaxChannelDiff=diff)
        return PipelineValidatorController(self.request)._compare(self.layout, self.snapshots, payload)

    def test_auto_nine_slice_with_reconstruction_evidence_passes(self):
        self.assertEqual("PASS", self._auto_sliced()["status"])

    def test_auto_nine_slice_without_evidence_blocks(self):
        result = self._auto_sliced(border="")
        self.assertIn("SPRITE_PIXEL_IDENTITY_MISMATCH", {issue["code"] for issue in result["issues"]})

    def test_auto_nine_slice_over_tolerance_blocks(self):
        result = self._auto_sliced(diff=5)
        self.assertIn("SPRITE_PIXEL_IDENTITY_MISMATCH", {issue["code"] for issue in result["issues"]})

    def test_auto_sliced_sprite_drawn_simple_blocks(self):
        result = self._auto_sliced(image_type="Simple")
        self.assertIn("NINE_SLICE_NOT_APPLIED", {issue["code"] for issue in result["issues"]})

    def test_swapped_sprite_binding_blocks(self):
        result_payload = self.result()
        result_payload["importResult"]["imageBindings"][0]["spriteAssetPath"] = "Assets/Sprites/wrong.png"
        result = PipelineValidatorController(self.request)._compare(self.layout, self.snapshots, result_payload)
        self.assertEqual("BLOCKED", result["status"])
        self.assertIn("SPRITE_BINDING_MISMATCH", {issue["code"] for issue in result["issues"]})

    def test_visible_descendant_of_hidden_parent_is_still_hidden(self):
        layout = {"canvas": {"width": 100, "height": 100}, "nodes": [{
            "name": "HiddenParent", "type": "group", "visible": False,
            "children": [{"name": "VisibleByItself", "type": "image", "visible": True}],
        }]}
        snapshots = [self.snapshots[0], {
            "path": "Prefab/0:VisibleByItself", "name": "VisibleByItself", "components": ["RectTransform", "Image"],
        }]
        result = PipelineValidatorController(self.request)._compare(layout, snapshots, {"status": "PASS", "importResult": {}})
        self.assertEqual("BLOCKED", result["status"])
        self.assertIn("HIDDEN_NODE_EXPORTED", {issue["code"] for issue in result["issues"]})

    def test_layout_parameters_are_compared(self):
        layout = {"canvas": {"width": 400, "height": 300}, "nodes": [{
            "name": "Rows", "type": "group", "visible": True,
            "x": 10, "y": 20, "width": 300, "height": 120,
            "layoutType": "horizontal", "layoutSpacing": 8,
            "layoutPaddingLeft": 1, "layoutPaddingRight": 2,
            "layoutPaddingTop": 3, "layoutPaddingBottom": 4,
            "contentSizeFitter": True,
        }]}
        snapshots = [self.snapshots[0], {
            "path": "Prefab/0:Rows", "name": "Rows", "x": 10, "y": 20, "width": 300, "height": 120,
            "components": ["RectTransform", "HorizontalLayoutGroup", "ContentSizeFitter"],
            "layoutSpacing": 8, "layoutPaddingLeft": 1, "layoutPaddingRight": 2,
            "layoutPaddingTop": 3, "layoutPaddingBottom": 4, "layoutChildAlignment": "MiddleLeft",
            "layoutChildControlWidth": False, "layoutChildControlHeight": False,
            "layoutChildForceExpandWidth": False, "layoutChildForceExpandHeight": False,
            "contentSizeHorizontalFit": "PreferredSize", "contentSizeVerticalFit": "Unconstrained",
        }]
        result = PipelineValidatorController(self.request)._compare(layout, snapshots, {"status": "PASS", "importResult": {}})
        self.assertEqual("PASS", result["status"])
        snapshots[1]["layoutSpacing"] = 12
        result = PipelineValidatorController(self.request)._compare(layout, snapshots, {"status": "PASS", "importResult": {}})
        self.assertEqual("BLOCKED", result["status"])
        self.assertIn("LAYOUT_PARAMETER_MISMATCH", {issue["code"] for issue in result["issues"]})

    def test_visible_node_cannot_be_inactive(self):
        self.snapshots[2]["activeSelf"] = False
        result = PipelineValidatorController(self.request)._compare(self.layout, self.snapshots, self.result())
        self.assertEqual("BLOCKED", result["status"])
        self.assertIn("VISIBLE_NODE_INACTIVE", {issue["code"] for issue in result["issues"]})

    def test_tmp_text_normalization_default_size_and_style_match(self):
        self.layout["nodes"][1].update({
            "text": "First\rSecond", "fontSize": 0, "characterSpacing": 2, "lineSpacing": 3,
            "alignment": "right", "color": "#12ab34",
        })
        self.snapshots[2].update({
            "text": "First\nSecond", "fontSize": 24, "characterSpacing": 2, "lineSpacing": 3,
            "textAlignment": "Right", "textColor": "#12AB34FF",
        })
        result = PipelineValidatorController(self.request)._compare(self.layout, self.snapshots, self.result())
        self.assertEqual("PASS", result["status"])

    def test_tmp_color_mismatch_blocks(self):
        self.snapshots[2]["textColor"] = "#FF0000FF"
        result = PipelineValidatorController(self.request)._compare(self.layout, self.snapshots, self.result())
        self.assertEqual("BLOCKED", result["status"])
        self.assertIn("TMP_STYLE_MISMATCH", {issue["code"] for issue in result["issues"]})

    def test_wrong_tmp_font_and_material_binding_blocks(self):
        receipt = self.result()
        self.snapshots[2].update({
            "fontAssetPath": "Assets/Fonts/Wrong.asset", "materialAssetPath": "Assets/Fonts/Wrong.mat",
        })
        receipt["importResult"]["textBindings"][0].update({
            "fontAssetPath": "Assets/Fonts/Wrong.asset", "materialAssetPath": "Assets/Fonts/Wrong.mat",
        })
        result = PipelineValidatorController(self.request)._compare(self.layout, self.snapshots, receipt)
        self.assertEqual("BLOCKED", result["status"])
        codes = {issue["code"] for issue in result["issues"]}
        self.assertIn("TMP_DEFAULT_FONT_MISMATCH", codes)
        self.assertIn("TMP_DEFAULT_MATERIAL_MISMATCH", codes)

    def test_font_token_must_resolve_through_font_map(self):
        self.layout["nodes"][1]["fontToken"] = "noto_sans"
        receipt = self.result()
        receipt["importResult"]["textBindings"][0].update({
            "fontToken": "noto_sans", "resolutionSource": "defaultFallback",
        })
        result = PipelineValidatorController(self.request)._compare(self.layout, self.snapshots, receipt)
        self.assertEqual("BLOCKED", result["status"])
        self.assertIn("TMP_FONT_TOKEN_FALLBACK", {issue["code"] for issue in result["issues"]})

    def test_tmp_gradient_direction_is_compared(self):
        self.layout["nodes"][1].update({
            "gradientStartColor": "#FFFFFFFF", "gradientEndColor": "#000000FF", "gradientAngle": 180,
        })
        self.snapshots[2].update({
            "textVertexGradient": True,
            "textGradientTopLeft": "#000000FF", "textGradientBottomLeft": "#FFFFFFFF",
        })
        result = PipelineValidatorController(self.request)._compare(self.layout, self.snapshots, self.result())
        self.assertEqual("PASS", result["status"])
        self.snapshots[2]["textGradientTopLeft"] = "#FFFFFFFF"
        result = PipelineValidatorController(self.request)._compare(self.layout, self.snapshots, self.result())
        self.assertEqual("BLOCKED", result["status"])

    def test_fake_text_helpers_must_be_under_their_owner(self):
        self.layout["nodes"].append({
            "name": "Thick", "type": "text", "visible": True, "text": "T", "fontSize": 24,
            "fakeThicknessOffsetX": 1, "color": "#FFFFFFFF",
        })
        self.snapshots.extend([
            {"path": "Prefab/2:Thick", "name": "Thick", "components": ["RectTransform"]},
            {"path": "Prefab/3:Thick_main", "name": "Thick_main", "components": ["RectTransform", "TextMeshProUGUI"],
             "text": "T", "fontSize": 24, "characterSpacing": 0, "lineSpacing": 0,
             "textAlignment": "Center", "textColor": "#FFFFFFFF", "textVertexGradient": False,
             "fontAssetPath": "Assets/Fonts/Main.asset", "materialAssetPath": "Assets/Fonts/Main.mat"},
        ])
        result = PipelineValidatorController(self.request)._compare(self.layout, self.snapshots, self.result())
        self.assertEqual("BLOCKED", result["status"])
        self.assertIn("TMP_FAKE_THICKNESS_INVALID", {issue["code"] for issue in result["issues"]})


if __name__ == "__main__":
    unittest.main()

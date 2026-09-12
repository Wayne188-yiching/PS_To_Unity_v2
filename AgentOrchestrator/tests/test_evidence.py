import json
import tempfile
import unittest
from pathlib import Path

from PIL import Image

from ps_to_unity_agents.evidence import prepare_case, validate_psd_package_manifest
from ps_to_unity_agents.models import PipelineRequest
from ps_to_unity_agents.reviewer import save_decision

TEST_TEMP_ROOT = Path(__file__).resolve().parents[1] / ".test_tmp"
TEST_TEMP_ROOT.mkdir(exist_ok=True)


class EvidenceTests(unittest.TestCase):
    def make_case(self, explicit_sliced: bool, border: int = 0):
        temporary = tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT)
        root = Path(temporary.name)
        artist = root / "artist"
        exporter = root / "exporter"
        artist.mkdir()
        exporter.mkdir()
        Image.new("RGBA", (10, 10), (20, 80, 160, 255)).save(artist / "panel.png")
        Image.new("RGBA", (100, 20), (20, 80, 160, 255)).save(exporter / "panel.png")
        node = {
            "name": "panel",
            "type": "image",
            "x": 0,
            "y": 0,
            "width": 100,
            "height": 20,
            "visible": True,
            "imagePath": "panel.png",
            "children": [],
        }
        if explicit_sliced:
            node.update({
                "imageType": "sliced",
                "spriteBorderLeft": border,
                "spriteBorderRight": border,
                "spriteBorderTop": 0,
                "spriteBorderBottom": 0,
            })
        layout = root / "layout.json"
        layout.write_text(json.dumps({
            "schemaVersion": "2.11",
            "canvas": {"width": 100, "height": 20},
            "nodes": [node],
        }), encoding="utf-8")
        request = PipelineRequest(
            case_id="test",
            psd_path=root / "test.psd",
            layout_json_path=layout,
            artist_asset_folder=artist,
            exporter_asset_folder=exporter,
            output_folder=root / "run",
        )
        return temporary, request

    def test_size_mismatch_requires_review_but_is_not_an_error(self):
        temporary, request = self.make_case(False)
        try:
            result = prepare_case(request)
            self.assertEqual("NEEDS_REVIEW", result["status"])
            self.assertTrue(all(issue["severity"] != "error" for issue in result["issues"]))
        finally:
            temporary.cleanup()

    def test_explicit_sliced_size_mismatch_passes(self):
        temporary, request = self.make_case(True, border=4)
        try:
            prepare_case(request)
            self.assertEqual("PASS", validate_psd_package_manifest(request)["status"])
        finally:
            temporary.cleanup()

    def test_sliced_border_larger_than_asset_is_blocked(self):
        temporary, request = self.make_case(True, border=6)
        try:
            prepare_case(request)
            result = validate_psd_package_manifest(request)
            self.assertEqual("BLOCKED", result["status"])
            self.assertTrue(any(issue["code"] == "SPRITE_BORDER_EXCEEDS_ASSET" for issue in result["issues"]))
        finally:
            temporary.cleanup()

    def test_approved_sliced_review_is_applied_to_prepared_layout(self):
        temporary, request = self.make_case(False)
        try:
            prepare_case(request)
            manifest_path = request.output_folder / "semantic_manifest.json"
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            save_decision(request.output_folder, manifest, {
                "nodePath": "panel",
                "decision": "approved",
                "mode": "sliced",
                "spriteBorder": {"left": 4, "right": 4, "top": 0, "bottom": 0},
            })
            result = prepare_case(request)
            prepared = json.loads(Path(result["layoutPath"]).read_text(encoding="utf-8"))
            self.assertEqual("PASS", result["status"])
            self.assertEqual("sliced", prepared["nodes"][0]["imageType"])
            self.assertEqual(4, prepared["nodes"][0]["spriteBorderLeft"])
        finally:
            temporary.cleanup()

    def test_pixel_changes_invalidate_approval_even_when_sizes_and_proposals_match(self):
        for folder in ("artist_asset_folder", "exporter_asset_folder"):
            with self.subTest(folder=folder):
                temporary, request = self.make_case(False)
                try:
                    prepare_case(request)
                    path = request.output_folder / "semantic_manifest.json"
                    manifest = json.loads(path.read_text(encoding="utf-8"))
                    save_decision(request.output_folder, manifest, {
                        "nodePath": "panel", "decision": "approved", "mode": "simple",
                    })
                    asset = getattr(request, folder) / "panel.png"
                    with Image.open(asset) as original:
                        changed = original.copy()
                    changed.putpixel((0, 0), (21, 80, 160, 255))
                    changed.save(asset)
                    self.assertEqual("NEEDS_REVIEW", prepare_case(request)["status"])
                    current = json.loads(path.read_text(encoding="utf-8"))
                    self.assertNotEqual(manifest["reviewFingerprint"], current["reviewFingerprint"])
                    self.assertEqual(0, current["appliedReviewDecisionCount"])
                finally:
                    temporary.cleanup()

    def test_nonfinite_and_negative_explicit_borders_are_blocked(self):
        for value in (float("nan"), float("inf"), -1):
            with self.subTest(value=value):
                temporary, request = self.make_case(True, border=value)
                try:
                    prepare_case(request)
                    self.assertEqual("BLOCKED", validate_psd_package_manifest(request)["status"])
                finally:
                    temporary.cleanup()

    def test_invalid_persisted_approval_never_mutates_layout(self):
        for value in (float("nan"), float("inf"), -1, 11, "bad"):
            with self.subTest(value=value):
                temporary, request = self.make_case(False)
                try:
                    prepare_case(request)
                    path = request.output_folder / "semantic_manifest.json"
                    manifest = json.loads(path.read_text(encoding="utf-8"))
                    (request.output_folder / "review_decisions.json").write_text(json.dumps({
                        "reviewFingerprint": manifest["reviewFingerprint"],
                        "decisions": {"panel": {"decision": "approved", "mode": "sliced",
                            "spriteBorder": {"left": value}}},
                    }), encoding="utf-8")
                    result = prepare_case(request)
                    self.assertEqual("BLOCKED", result["status"])
                    prepared = json.loads(Path(result["layoutPath"]).read_text(encoding="utf-8"))
                    self.assertNotIn("imageType", prepared["nodes"][0])
                    self.assertEqual(0, json.loads(path.read_text(encoding="utf-8"))["appliedReviewDecisionCount"])
                finally:
                    temporary.cleanup()


if __name__ == "__main__":
    unittest.main()

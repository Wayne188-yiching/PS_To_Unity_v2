import json
import tempfile
import unittest
from pathlib import Path

from PIL import Image

from ps_to_unity_agents.evidence import prepare_case, qa_manifest
from ps_to_unity_agents.models import PipelineRequest

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
            self.assertEqual("PASS", qa_manifest(request)["status"])
        finally:
            temporary.cleanup()

    def test_sliced_border_larger_than_asset_is_blocked(self):
        temporary, request = self.make_case(True, border=6)
        try:
            prepare_case(request)
            result = qa_manifest(request)
            self.assertEqual("BLOCKED", result["status"])
            self.assertTrue(any(issue["code"] == "SPRITE_BORDER_EXCEEDS_ASSET" for issue in result["issues"]))
        finally:
            temporary.cleanup()


if __name__ == "__main__":
    unittest.main()

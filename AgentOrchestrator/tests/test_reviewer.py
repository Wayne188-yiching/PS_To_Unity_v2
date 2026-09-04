import json
import tempfile
import unittest
from pathlib import Path

from ps_to_unity_agents.reviewer import load_decisions, render_page, save_decision


TEST_TEMP_ROOT = Path(__file__).resolve().parents[1] / ".test_tmp"
TEST_TEMP_ROOT.mkdir(exist_ok=True)


class ReviewerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT)
        self.output = Path(self.temporary.name)
        self.manifest = {
            "caseId": "review-test",
            "entries": [{
                "nodePath": "root/panel",
                "assetPath": "panel.png",
                "assetPixelSize": {"width": 100, "height": 20},
                "expectedUnityRectSize": {"width": 300, "height": 20},
                "status": "NEEDS_REVIEW",
                "proposal": {
                    "mode": "sliced",
                    "border": {"left": 40, "top": 0, "right": 40, "bottom": 0},
                },
            }],
        }

    def tearDown(self):
        self.temporary.cleanup()

    def test_page_contains_review_item_and_proposed_border(self):
        page = render_page(self.manifest, {"decisions": {}})
        self.assertIn("root/panel", page)
        self.assertIn('value="40"', page)
        self.assertIn("已處理", page)

    def test_decision_is_saved_and_reloaded(self):
        save_decision(self.output, self.manifest, {
            "nodePath": "root/panel",
            "decision": "approved",
            "mode": "sliced",
            "spriteBorder": {"left": 40, "right": 40, "top": 0, "bottom": 0},
            "note": "keep both ends",
        })
        saved = load_decisions(self.output)["decisions"]["root/panel"]
        self.assertEqual("approved", saved["decision"])
        self.assertEqual(40, saved["spriteBorder"]["left"])

    def test_invalid_sliced_border_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "exceed"):
            save_decision(self.output, self.manifest, {
                "nodePath": "root/panel",
                "decision": "approved",
                "mode": "sliced",
                "spriteBorder": {"left": 60, "right": 60, "top": 0, "bottom": 0},
            })


if __name__ == "__main__":
    unittest.main()

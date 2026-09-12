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
            "reviewFingerprint": "review-context-1",
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

    def test_decisions_from_another_manifest_are_replaced(self):
        save_decision(self.output, self.manifest, {
            "nodePath": "root/panel", "decision": "approved", "mode": "simple",
        })
        changed = dict(self.manifest, reviewFingerprint="review-context-2")
        save_decision(self.output, changed, {
            "nodePath": "root/panel", "decision": "unsure", "mode": "simple",
        })
        payload = load_decisions(self.output)
        self.assertEqual("review-context-2", payload["reviewFingerprint"])
        self.assertEqual("unsure", payload["decisions"]["root/panel"]["decision"])

    def test_nonfinite_and_negative_borders_are_rejected_without_saving(self):
        for value in (float("nan"), float("inf"), float("-inf"), -1, "NaN"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                save_decision(self.output, self.manifest, {
                    "nodePath": "root/panel", "decision": "approved", "mode": "sliced",
                    "spriteBorder": {"left": value},
                })
        self.assertFalse((self.output / "review_decisions.json").exists())

    def test_stale_or_unbound_decisions_are_not_displayed(self):
        for fingerprint in (None, "old-context"):
            with self.subTest(fingerprint=fingerprint):
                page = render_page(self.manifest, {
                    "reviewFingerprint": fingerprint,
                    "decisions": {"root/panel": {"decision": "approved", "mode": "mask", "note": "stale-note"}},
                })
                self.assertIn('data-saved=""', page)
                self.assertNotIn('value="mask" selected', page)
                self.assertNotIn("stale-note", page)

    def test_matching_decisions_are_displayed(self):
        page = render_page(self.manifest, {
            "reviewFingerprint": self.manifest["reviewFingerprint"],
            "decisions": {"root/panel": {"decision": "approved", "mode": "simple"}},
        })
        self.assertIn('data-saved="approved"', page)

    def test_unbound_legacy_decisions_are_not_carried_forward(self):
        (self.output / "review_decisions.json").write_text(json.dumps({
            "decisions": {"old/panel": {"decision": "approved", "mode": "simple"}},
        }), encoding="utf-8")
        save_decision(self.output, self.manifest, {
            "nodePath": "root/panel", "decision": "unsure", "mode": "simple",
        })
        self.assertNotIn("old/panel", load_decisions(self.output)["decisions"])

    def test_stale_browser_payload_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "changed"):
            save_decision(self.output, self.manifest, {
                "nodePath": "root/panel", "decision": "approved", "mode": "simple",
                "reviewFingerprint": "old-context",
            })
        self.assertFalse((self.output / "review_decisions.json").exists())


if __name__ == "__main__":
    unittest.main()

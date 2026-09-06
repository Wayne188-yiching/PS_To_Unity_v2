from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from ps_to_unity_agents.unity_layout_audit import audit_prefab
from agent_roles.pipeline_agents import load_unity_layout_knowledge


class UnityLayoutAuditTests(unittest.TestCase):
    def test_curated_layout_knowledge_is_safe_to_load(self):
        knowledge = load_unity_layout_knowledge()
        self.assertTrue(knowledge["referencePrefabs"])
        self.assertTrue(knowledge["holdouts"])
        self.assertIn("never a failure", " ".join(knowledge["rules"]))

    def test_flags_risky_scale_and_unmasked_scroll(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            prefab = root / "RiskPanel.prefab"
            prefab.write_text(
                """--- !u!1 &1
GameObject:
  m_Name: RiskPanel
--- !u!224 &2
RectTransform:
  m_GameObject: {fileID: 1}
  m_LocalScale: {x: -1, y: 1, z: 1}
  m_AnchorMin: {x: 0, y: 0}
  m_AnchorMax: {x: 1, y: 1}
--- !u!114 &3
MonoBehaviour:
  m_Script: {fileID: 11500000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}
  m_Content: {fileID: 0}
  m_Viewport: {fileID: 0}
""",
                encoding="utf-8",
            )
            result = audit_prefab(prefab, root, {"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa": "ScrollRect"})
            self.assertEqual("MANUAL_REVIEW", result["status"])
            self.assertIn("ZERO_OR_NEGATIVE_SCALE", result["flags"])
            self.assertIn("SCROLL_WITHOUT_MASK", result["flags"])
            self.assertIn("SCROLL_LINKS_INCOMPLETE", result["flags"])

    def test_sliced_image_is_only_a_reference_candidate(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            prefab = root / "FramePanel.prefab"
            prefab.write_text(
                """--- !u!1 &1
GameObject:
  m_Name: FramePanel
--- !u!224 &2
RectTransform:
  m_GameObject: {fileID: 1}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_AnchorMin: {x: 0, y: 0}
  m_AnchorMax: {x: 1, y: 1}
--- !u!114 &3
MonoBehaviour:
  m_Script: {fileID: 11500000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}
  m_Type: 1
""",
                encoding="utf-8",
            )
            result = audit_prefab(prefab, root, {"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb": "Image"})
            self.assertEqual("REFERENCE_CANDIDATE", result["status"])
            self.assertEqual(1, result["imageTypes"]["Sliced"])


if __name__ == "__main__":
    unittest.main()

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from ps_to_unity_agents.shared_assets import match_shared_assets


TEST_TEMP_ROOT = Path(__file__).resolve().parents[1] / ".test_tmp"
TEST_TEMP_ROOT.mkdir(exist_ok=True)


class SharedAssetTests(unittest.TestCase):
    def write_inspection(self, root: Path, layers: list[dict]):
        path = root / "inspection.json"
        path.write_text(json.dumps({"layers": layers}), encoding="utf-8")
        return path

    def test_unique_exact_name_is_confirmed(self):
        with tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT) as temporary:
            root = Path(temporary)
            shared = root / "Share"
            shared.mkdir()
            (shared / "skill_thunder.png").write_bytes(b"png")
            inspection = self.write_inspection(root, [{
                "id": 1, "name": "skill_thunder", "nodeType": "layer",
                "layerKind": "LayerKind.SMARTOBJECT", "visible": True,
            }])
            result = match_shared_assets(inspection, [shared])
            self.assertEqual(1, result["confirmedMatchCount"])
            self.assertEqual("skill_thunder", result["matches"][0]["targetName"])

    def test_duplicate_basename_needs_review(self):
        with tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT) as temporary:
            root = Path(temporary)
            shared = root / "Share"
            (shared / "A").mkdir(parents=True)
            (shared / "B").mkdir(parents=True)
            (shared / "A" / "coin.png").write_bytes(b"png")
            (shared / "B" / "coin.png").write_bytes(b"png")
            inspection = self.write_inspection(root, [{
                "id": 1, "name": "coin", "nodeType": "layer",
                "layerKind": "LayerKind.SMARTOBJECT", "visible": True,
            }])
            result = match_shared_assets(inspection, [shared])
            self.assertEqual(1, result["ambiguousMatchCount"])
            self.assertIsNone(result["matches"][0]["targetName"])

    def test_smart_object_original_filename_overrides_localized_layer_name(self):
        with tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT) as temporary:
            root = Path(temporary)
            shared = root / "Share"
            shared.mkdir()
            (shared / "skill_ice.png").write_bytes(b"png")
            inspection = self.write_inspection(root, [{
                "id": 1, "name": "skill_冰凍", "nodeType": "layer",
                "layerKind": "LayerKind.SMARTOBJECT", "visible": True,
                "smartObject": {"fileReference": "skill_ice.png"},
            }])
            result = match_shared_assets(inspection, [shared])
            self.assertEqual(1, result["confirmedMatchCount"])
            self.assertEqual("smartObject.fileReference", result["matches"][0]["source"])
            self.assertEqual("skill_ice", result["matches"][0]["targetName"])

    def test_hidden_layer_is_ignored(self):
        with tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT) as temporary:
            root = Path(temporary)
            shared = root / "Share"
            shared.mkdir()
            (shared / "coin.png").write_bytes(b"png")
            inspection = self.write_inspection(root, [{
                "id": 1, "name": "coin", "nodeType": "layer",
                "layerKind": "LayerKind.SMARTOBJECT", "visible": False,
            }])
            result = match_shared_assets(inspection, [shared])
            self.assertEqual(0, result["confirmedMatchCount"])

    def test_texture_packer_sprite_name_is_indexed(self):
        with tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT) as temporary:
            root = Path(temporary)
            shared = root / "Share"
            shared.mkdir()
            (shared / "Common_06_Atlas.tpsheet").write_text(
                ":texture=Common_06_Atlas.png\ncoin_r;1;23;40;40; 0.5;0.5; 0;0;0;0\n",
                encoding="utf-8",
            )
            inspection = self.write_inspection(root, [{
                "id": 1, "name": "coin_r", "nodeType": "layer",
                "layerKind": "LayerKind.SMARTOBJECT", "visible": True,
            }])
            result = match_shared_assets(inspection, [shared])
            match = result["matches"][0]
            self.assertEqual("CONFIRMED_EXACT", match["status"])
            self.assertEqual("texture_packer_sprite", match["candidates"][0]["assetType"])
            self.assertEqual("1,23,40,40", match["candidates"][0]["sourceRect"])

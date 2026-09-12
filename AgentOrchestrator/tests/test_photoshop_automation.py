from __future__ import annotations

import unittest
from pathlib import Path


WORKSPACE = Path(__file__).resolve().parents[2]


class PhotoshopAutomationContractTests(unittest.TestCase):
    def test_export_wrapper_rejects_stale_or_foreign_results(self):
        source = (WORKSPACE / "Tools" / "Invoke-PhotoshopUiExport.ps1").read_text(encoding="utf-8-sig")
        self.assertIn("automationRunId", source)
        self.assertIn("Remove-Item -LiteralPath $stalePath", source)
        self.assertIn("PS_TO_UNITY_V2_AUTOMATION_RESULT", source)
        self.assertIn("psdSha256", source)
        self.assertIn("New-PhotoshopComApplication", source)

    def test_inspector_uses_version_tolerant_photoshop_com_resolution(self):
        source = (WORKSPACE / "Tools" / "Invoke-PhotoshopPsdInspect.ps1").read_text(encoding="utf-8-sig")
        common = (WORKSPACE / "Tools" / "PhotoshopAutomationCommon.ps1").read_text(encoding="utf-8-sig")
        self.assertIn("New-PhotoshopComApplication", source)
        self.assertIn("Photoshop.Application", common)

    def test_structure_applier_has_recovery_contract(self):
        source = (WORKSPACE / "PhotoshopExporter" / "PhotoshopStructurePlanApplier.jsx").read_text(encoding="utf-8-sig")
        self.assertIn("alreadyAppliedCount", source)
        self.assertIn("validateMechanicalPreconditions", source)
        self.assertIn("planFingerprint", source)


if __name__ == "__main__":
    unittest.main()

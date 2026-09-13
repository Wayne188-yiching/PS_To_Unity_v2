from __future__ import annotations

import json
from collections import Counter, defaultdict
from pathlib import Path

from .evidence import validate_psd_package_manifest
from .fingerprints import sha256_file
from .models import AgentDecision, Issue, PipelineRequest, Status


class PipelineValidatorController:
    """Validate the PSD package -> importer receipt -> generated Prefab contract."""

    def __init__(self, request: PipelineRequest, unity_controller=None):
        self.request = request
        self.unity_controller = unity_controller
        self.last_result = None

    @staticmethod
    def _issue(code, severity, message, *, node=None, evidence=None, suggested_action=None):
        return {
            "code": code,
            "owner": "PIPELINE_VALIDATOR",
            "severity": severity,
            "message": message,
            "phase": "PIPELINE",
            "node": node,
            "evidence": evidence or [],
            "suggestedAction": suggested_action,
            "safeToAutoFix": False,
            "retryable": False,
        }

    def _result(self, status, summary, issues=None, **evidence):
        result = {
            "status": status,
            "summary": summary,
            "validationScope": "PSD_IR_TO_GENERATED_PREFAB",
            "liveAcceptance": "NOT_RUN",
            "issues": issues or [],
            **evidence,
        }
        self.last_result = result
        return result

    def validate(self, unity_result=None):
        unity_result = unity_result or (self.unity_controller.last_result if self.unity_controller else None)
        if not unity_result:
            return self._result(
                "NEEDS_REVIEW",
                "No current Unity generation evidence is available.",
                [self._issue(
                    "UNITY_EVIDENCE_REQUIRED", "warning",
                    "Run the guarded Unity Agent before Pipeline Validator.",
                    suggested_action="Run Unity Agent with the same approved request, then validate its current-run receipt.",
                )],
            )
        if unity_result.get("status") not in {"PASS", "NEEDS_REVIEW"}:
            status = unity_result.get("status") if unity_result.get("status") in {"BLOCKED", "FAIL_RETRYABLE"} else "BLOCKED"
            return self._result(
                status,
                "Unity generation did not produce evidence that can be validated.",
                [self._issue(
                    "UNITY_GENERATION_NOT_READY", "error",
                    unity_result.get("summary") or "Unity generation is not ready.",
                    evidence=[unity_result.get("runId", "")],
                )],
                unityStatus=unity_result.get("status"),
            )
        if self.unity_controller and not self.unity_controller.current_evidence_matches(unity_result):
            return self._result(
                "BLOCKED",
                "Unity generation inputs, receipt, or Prefab changed after verification.",
                [self._issue(
                    "UNITY_EVIDENCE_CHANGED_AFTER_GENERATION", "error",
                    "Regenerate from the current inputs and validate the resulting Prefab without replacing its evidence.",
                )],
            )

        try:
            package = self._load_existing_package()
            package_validation = validate_psd_package_manifest(self.request)
            if package.get("status") != "PASS" or package_validation.get("status") != "PASS":
                return self._result(
                    "BLOCKED",
                    "The PSD package changed or no longer passes its deterministic gate.",
                    [self._issue(
                        "PSD_PACKAGE_STALE", "error",
                        "Rebuild and validate the PSD package before comparing it with the Prefab.",
                    )],
                    package=package,
                    packageValidation=package_validation,
                )
            layout = json.loads(Path(package["layoutPath"]).read_text(encoding="utf-8-sig"))
            receipt = unity_result.get("importResult") or {}
            if receipt.get("layoutSha256") != sha256_file(Path(package["layoutPath"])):
                return self._result(
                    "BLOCKED",
                    "The current PSD package layout does not match the Unity receipt.",
                    [self._issue(
                        "UNITY_LAYOUT_RECEIPT_STALE", "error",
                        "Regenerate the Prefab from the current package before validation.",
                    )],
                    runId=unity_result.get("runId"),
                )
            snapshots = receipt.get("prefabNodes") or []
            if not snapshots:
                return self._result(
                    "BLOCKED",
                    "The Unity receipt has no generated Prefab structure snapshot.",
                    [self._issue(
                        "PREFAB_SNAPSHOT_MISSING", "error",
                        "Regenerate with the headless importer version that emits prefabNodes.",
                    )],
                    runId=unity_result.get("runId"),
                )
            return self._compare(layout, snapshots, unity_result)
        except (OSError, ValueError, TypeError, KeyError, json.JSONDecodeError) as error:
            return self._result(
                "BLOCKED",
                "Pipeline evidence is invalid.",
                [self._issue("PIPELINE_EVIDENCE_INVALID", "error", str(error))],
            )

    def _load_existing_package(self):
        manifest_path = (self.request.output_folder / "semantic_manifest.json").resolve()
        output_root = self.request.output_folder.resolve()
        if not manifest_path.is_file() or not manifest_path.is_relative_to(output_root):
            return {"status": "BLOCKED", "reason": "semantic_manifest.json is missing or outside output_folder."}
        manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
        layout_path = Path(manifest["layoutPath"]).resolve()
        asset_folder = Path(manifest["assetFolder"]).resolve()
        if not layout_path.is_file() or not asset_folder.is_dir():
            return {"status": "BLOCKED", "reason": "Manifest layout or asset folder is missing."}
        if not layout_path.is_relative_to(output_root) or not asset_folder.is_relative_to(output_root):
            return {"status": "BLOCKED", "reason": "Manifest references evidence outside output_folder."}
        for entry in manifest.get("entries") or []:
            asset = (asset_folder / str(entry.get("assetPath") or "")).resolve()
            if (
                not asset.is_file()
                or not asset.is_relative_to(asset_folder)
                or sha256_file(asset) != entry.get("assetSha256")
            ):
                return {"status": "BLOCKED", "reason": f"Manifest asset changed: {entry.get('assetPath')}"}
        return {
            "status": "PASS",
            "layoutPath": str(layout_path),
            "assetFolder": str(asset_folder),
            "manifestPath": str(manifest_path),
            "manifest": manifest,
        }

    def _compare(self, layout, snapshots, unity_result):
        expected, hidden = [], []
        self._flatten(layout.get("nodes") or [], expected, hidden)
        actual = [node for node in snapshots if isinstance(node, dict)]
        receipt = unity_result.get("importResult") or {}
        bindings = [item for item in receipt.get("imageBindings") or [] if isinstance(item, dict)]
        text_bindings = [item for item in receipt.get("textBindings") or [] if isinstance(item, dict)]
        imported = {
            self._normalize_image_path(item.get("imagePath")): item
            for item in receipt.get("importedImages") or []
            if isinstance(item, dict) and item.get("imagePath")
        }
        issues = []
        for snapshot in actual:
            if not snapshot.get("activeSelf", True):
                issues.append(self._issue(
                    "PREFAB_NODE_INACTIVE", "error",
                    f"Generated Prefab node is inactive: {snapshot.get('name')}",
                    node=snapshot.get("path"),
                ))
        if not actual or "RectTransform" not in set(actual[0].get("components") or []):
            issues.append(self._issue("PREFAB_ROOT_INVALID", "error", "Prefab root is missing RectTransform evidence."))
        else:
            root_components = set(actual[0].get("components") or [])
            for component in ("CanvasGroup", "RectMask2D"):
                if component not in root_components:
                    issues.append(self._issue(
                        "PREFAB_ROOT_COMPONENT_MISSING", "error",
                        f"Prefab root is missing required {component}.", node=actual[0].get("name"),
                    ))

        by_name = defaultdict(list)
        for snapshot in actual[1:]:
            by_name[str(snapshot.get("name", ""))].append(snapshot)
        expected_counts = Counter(str(item["node"].get("name") or item["node"].get("type") or "") for item in expected)

        for item in expected:
            node = item["node"]
            name = str(node.get("name") or node.get("type") or "")
            candidates = [candidate for candidate in by_name.get(name, []) if self._component_compatible(node, candidate)]
            if not candidates:
                issues.append(self._issue(
                    "PREFAB_NODE_MISSING", "error",
                    f"Visible IR node was not generated with the expected component type: {name}",
                    node=item["sourcePath"], evidence=[name],
                ))
                continue
            if expected_counts[name] != 1 or len(candidates) != 1:
                if len(candidates) != expected_counts[name]:
                    issues.append(self._issue(
                        "PREFAB_NODE_COUNT_MISMATCH", "error",
                        f"Node name {name!r} expected {expected_counts[name]} compatible instances, found {len(candidates)}.",
                        node=item["sourcePath"],
                    ))
                elif item["ordinal"] == 0:
                    issues.append(self._issue(
                        "AMBIGUOUS_NODE_IDENTITY", "warning",
                        f"Node name {name!r} is duplicated; component counts match but exact hierarchy identity needs review.",
                        node=item["sourcePath"],
                        suggested_action="Give authored nodes stable unique export names before final acceptance.",
                    ))
                continue

            snapshot = candidates[0]
            self._validate_node(item, snapshot, actual, layout, issues, bindings, imported, text_bindings)

        visible_names = {str(item["node"].get("name") or item["node"].get("type") or "") for item in expected}
        for item in hidden:
            name = str(item["node"].get("name") or item["node"].get("type") or "")
            if name and name in visible_names:
                issues.append(self._issue(
                    "HIDDEN_VISIBLE_NAME_COLLISION", "warning",
                    f"Hidden and visible IR nodes share the name {name!r}; hidden export exclusion cannot be proven by name.",
                    node=item["sourcePath"],
                    suggested_action="Give visible and hidden authored nodes unique stable export names.",
                ))
                if len(by_name.get(name, [])) > expected_counts[name]:
                    issues.append(self._issue(
                        "HIDDEN_NODE_EXPORTED", "error",
                        f"More Prefab nodes named {name!r} exist than visible IR nodes; a hidden authored node was exported.",
                        node=item["sourcePath"],
                    ))
            elif name and by_name.get(name):
                issues.append(self._issue(
                    "HIDDEN_NODE_EXPORTED", "error",
                    f"Hidden IR node unexpectedly exists in the generated Prefab: {name}", node=item["sourcePath"],
                ))

        generated_paths = self._generated_helper_paths(actual, expected)
        unexpected = []
        expected_names = set(expected_counts)
        for snapshot in actual[1:]:
            name = str(snapshot.get("name", ""))
            if name in expected_names or snapshot.get("path") in generated_paths:
                continue
            unexpected.append(snapshot.get("path") or name)
        if unexpected:
            issues.append(self._issue(
                "UNEXPECTED_PREFAB_NODES", "warning",
                f"Generated Prefab contains {len(unexpected)} nodes not attributable by stable IR name.",
                evidence=unexpected[:20],
                suggested_action="Confirm these are deterministic helper nodes or give the source nodes stable names.",
            ))

        error_count = sum(issue["severity"] == "error" for issue in issues)
        warning_count = sum(issue["severity"] == "warning" for issue in issues)
        if error_count:
            status = "BLOCKED"
            summary = f"Pipeline structure validation found {error_count} blocking mismatches and {warning_count} review items."
        elif warning_count or unity_result.get("status") == "NEEDS_REVIEW":
            status = "NEEDS_REVIEW"
            summary = f"Pipeline structure matches required nodes, with {warning_count} items requiring review."
        else:
            status = "PASS"
            summary = "PSD/IR intent matches the generated Prefab structure and core component evidence."
        return self._result(
            status, summary, issues,
            runId=unity_result.get("runId"),
            expectedNodeCount=len(expected), actualNodeCount=max(0, len(actual) - 1),
            blockingMismatchCount=error_count, reviewCount=warning_count,
        )

    @staticmethod
    def _generated_helper_paths(actual, expected):
        paths = {str(snapshot.get("path")) for snapshot in actual if snapshot.get("path")}
        allowed = set()
        for snapshot in actual:
            owner_path = str(snapshot.get("path") or "")
            for key in ("viewportPath", "contentPath"):
                referenced = str(snapshot.get(key) or "")
                if referenced not in paths:
                    continue
                current = referenced
                while current.startswith(owner_path + "/"):
                    allowed.add(current)
                    current = current.rsplit("/", 1)[0]
            handle = str(snapshot.get("scrollbarHandlePath") or "")
            if handle in paths:
                parent = handle.rsplit("/", 1)[0]
                if parent in paths and parent.startswith(owner_path + "/"):
                    allowed.add(parent)
        fake_names = [
            str(item["node"].get("name") or "")
            for item in expected
            if item["node"].get("fakeThicknessOffsetX") or item["node"].get("fakeThicknessOffsetY")
        ]
        for fake_name in fake_names:
            owners = [snapshot for snapshot in actual if snapshot.get("name") == fake_name]
            if len(owners) != 1:
                continue
            owner_path = str(owners[0].get("path") or "")
            allowed.update(
                str(snapshot.get("path") or "")
                for snapshot in actual
                if str(snapshot.get("path") or "").startswith(owner_path + "/")
                and snapshot.get("name") in {fake_name + "_main", fake_name + "_shadow"}
            )
        return allowed

    def _flatten(
        self, nodes, expected, hidden, parent_path="", parent_name=None, *,
        layout_ancestor=False, ancestor_hidden=False,
    ):
        ordinals = Counter(str(node.get("name") or node.get("type") or "") for node in nodes if isinstance(node, dict))
        seen = Counter()
        for index, node in enumerate(nodes):
            if not isinstance(node, dict):
                continue
            name = str(node.get("name") or node.get("type") or "")
            ordinal = seen[name]
            seen[name] += 1
            source_path = f"{parent_path}/{index}:{name}" if parent_path else f"{index}:{name}"
            item = {"node": node, "sourcePath": source_path, "parentSourcePath": parent_path, "parentName": parent_name,
                    "layoutAncestor": layout_ancestor, "ordinal": ordinal, "siblingNameCount": ordinals[name]}
            is_hidden = ancestor_hidden or not node.get("visible", True)
            if is_hidden:
                hidden.append(item)
                self._flatten(
                    node.get("children") or [], expected, hidden, source_path, name,
                    layout_ancestor=layout_ancestor, ancestor_hidden=True,
                )
                continue
            is_mask_shape = str(node.get("maskRole") or "").lower() == "mask"
            if not is_mask_shape:
                expected.append(item)
            child_layout = layout_ancestor or bool(node.get("layoutType"))
            self._flatten(
                node.get("children") or [], expected, hidden, source_path, name,
                layout_ancestor=child_layout,
            )

    @staticmethod
    def _component_compatible(node, snapshot):
        components = set(snapshot.get("components") or [])
        kind = str(node.get("type") or "").lower()
        if kind == "image":
            return "Image" in components
        if kind == "text":
            fake = bool(node.get("fakeThicknessOffsetX") or node.get("fakeThicknessOffsetY"))
            return "RectTransform" in components if fake else "TextMeshProUGUI" in components
        return "RectTransform" in components

    def _validate_node(self, item, snapshot, all_snapshots, layout, issues, bindings, imported, text_bindings):
        node = item["node"]
        name = str(node.get("name") or node.get("type") or "")
        components = set(snapshot.get("components") or [])
        kind = str(node.get("type") or "").lower()
        if not snapshot.get("activeSelf", True):
            issues.append(self._issue(
                "VISIBLE_NODE_INACTIVE", "error",
                f"Visible IR node {name} is inactive in the generated Prefab.", node=item["sourcePath"],
            ))

        if item["parentSourcePath"]:
            parent_name = item["parentName"]
            parent_matches = [candidate for candidate in all_snapshots if candidate.get("name") == parent_name]
            if len(parent_matches) == 1 and not str(snapshot.get("path", "")).startswith(str(parent_matches[0].get("path", "")) + "/"):
                issues.append(self._issue(
                    "PREFAB_HIERARCHY_MISMATCH", "error",
                    f"{name} is not under its unique authored parent {parent_name}.", node=item["sourcePath"],
                ))

        has_own_rect = kind in {"image", "text"} or (
            kind == "group" and any((
                node.get("layoutType"), node.get("scrollDirection"), node.get("scrollbarDirection"),
                node.get("clipToBounds"), str(node.get("maskMode") or "").lower() == "sprite",
                self.request.unity_use_responsive_anchor and node.get("width") and node.get("height"),
            ))
        )
        if has_own_rect and not item["layoutAncestor"]:
            canvas = layout.get("canvas") or {}
            reference_x = self.request.unity_reference_resolution_x or float(canvas.get("width") or 1920)
            reference_y = self.request.unity_reference_resolution_y or float(canvas.get("height") or 1080)
            expected_x = float(node.get("x") or 0) - max(0.0, (float(canvas.get("width") or reference_x) - reference_x) * 0.5)
            expected_y = float(node.get("y") or 0) - max(0.0, (float(canvas.get("height") or reference_y) - reference_y) * 0.5)
            values = (("x", expected_x), ("y", expected_y), ("width", float(node.get("width") or 0)),
                      ("height", float(node.get("height") or 0)))
            differences = [f"{field}: expected {expected_value:.3f}, actual {float(snapshot.get(field) or 0):.3f}"
                           for field, expected_value in values
                           if abs(float(snapshot.get(field) or 0) - expected_value) > 0.51]
            if differences:
                issues.append(self._issue(
                    "PREFAB_GEOMETRY_MISMATCH", "error",
                    f"{name} does not preserve the non-layout Photoshop rectangle.",
                    node=item["sourcePath"], evidence=differences,
                ))

        if kind == "image":
            if not snapshot.get("spriteAssetPath"):
                issues.append(self._issue("SPRITE_NOT_BOUND", "error", f"Image {name} has no Sprite.", node=item["sourcePath"]))
            if str(node.get("imageType") or "").lower() == "sliced" and snapshot.get("imageType") != "Sliced":
                issues.append(self._issue("NINE_SLICE_NOT_APPLIED", "error", f"Image {name} requested Sliced but is not Sliced.", node=item["sourcePath"]))
            if name.upper().startswith("BTN_") and "Button" not in components:
                issues.append(self._issue("BUTTON_NOT_APPLIED", "error", f"Button image {name} has no Button component.", node=item["sourcePath"]))
            self._validate_image_binding(item, snapshot, bindings, imported, issues)
        elif kind == "text":
            fake = bool(node.get("fakeThicknessOffsetX") or node.get("fakeThicknessOffsetY"))
            text_snapshot = snapshot
            if fake:
                descendants = [candidate for candidate in all_snapshots
                               if str(candidate.get("path", "")).startswith(str(snapshot.get("path", "")) + "/")
                               and candidate.get("name") == name + "_main"]
                if len(descendants) != 1 or "TextMeshProUGUI" not in set(descendants[0].get("components") or []):
                    issues.append(self._issue("TMP_FAKE_THICKNESS_INVALID", "error", f"Text {name} is missing its generated TMP main layer.", node=item["sourcePath"]))
                    return
                text_snapshot = descendants[0]
            if self._normalize_text(text_snapshot.get("text")) != self._normalize_text(node.get("text")):
                issues.append(self._issue("TMP_TEXT_MISMATCH", "error", f"TMP content differs for {name}.", node=item["sourcePath"]))
            if not text_snapshot.get("fontAssetPath") or not text_snapshot.get("materialAssetPath"):
                issues.append(self._issue(
                    "TMP_ASSET_NOT_BOUND", "error",
                    f"TMP node {name} must bind both a Font Asset and material.", node=item["sourcePath"],
                ))
            self._validate_text_binding(item, text_snapshot, text_bindings, issues, "main" if fake else "text")
            expected_font_size = float(node.get("fontSize") or 24)
            if abs(float(text_snapshot.get("fontSize") or 0) - expected_font_size) > 0.05:
                issues.append(self._issue("TMP_FONT_SIZE_MISMATCH", "error", f"TMP font size differs for {name}.", node=item["sourcePath"]))
            text_differences = self._number_differences(text_snapshot, (
                ("characterSpacing", node.get("characterSpacing")),
                ("lineSpacing", node.get("lineSpacing")),
            ), tolerance=0.01)
            expected_alignment = self._expected_text_alignment(node.get("alignment"))
            if text_snapshot.get("textAlignment") != expected_alignment:
                text_differences.append(f"textAlignment: expected {expected_alignment}")
            if self._normalize_color(text_snapshot.get("textColor")) != self._normalize_color(node.get("color")):
                text_differences.append(
                    f"textColor: expected {self._normalize_color(node.get('color'))}, "
                    f"actual {self._normalize_color(text_snapshot.get('textColor'))}"
                )
            gradient_start = node.get("gradientStartColor")
            gradient_end = node.get("gradientEndColor")
            expects_gradient = bool(gradient_start and gradient_end)
            if bool(text_snapshot.get("textVertexGradient")) != expects_gradient:
                text_differences.append(f"textVertexGradient: expected {expects_gradient}")
            elif expects_gradient:
                angle = float(node.get("gradientAngle") or 0) % 360
                expected_top, expected_bottom = (
                    (gradient_end, gradient_start) if 135 < angle < 315 else (gradient_start, gradient_end)
                )
                if self._normalize_color(text_snapshot.get("textGradientTopLeft")) != self._normalize_color(expected_top):
                    text_differences.append("textGradientTopLeft differs from the IR gradient direction")
                if self._normalize_color(text_snapshot.get("textGradientBottomLeft")) != self._normalize_color(expected_bottom):
                    text_differences.append("textGradientBottomLeft differs from the IR gradient direction")
            if text_differences:
                issues.append(self._issue(
                    "TMP_STYLE_MISMATCH", "error",
                    f"TMP style differs for {name}.", node=item["sourcePath"], evidence=text_differences,
                ))
        elif kind == "group":
            self._validate_group(item, snapshot, all_snapshots, issues, bindings, imported, layout)

    @staticmethod
    def _normalize_image_path(value):
        return str(value or "").replace("\\", "/").lstrip("./").casefold()

    @staticmethod
    def _normalize_text(value):
        return str(value or "").replace("\r\n", "\n").replace("\r", "\n")

    @staticmethod
    def _normalize_color(value):
        color = str(value or "#FFFFFFFF").strip().upper()
        if not color.startswith("#"):
            color = "#" + color
        if len(color) == 7:
            color += "FF"
        return color

    @staticmethod
    def _expected_text_alignment(value):
        normalized = str(value or "").strip().lower().replace("-", "")
        return {
            "left": "Left", "topleft": "Left",
            "center": "Center", "middle": "Center", "": "Center",
            "right": "Right", "topright": "Right",
            "justified": "Justified", "justify": "Justified",
            "top": "Top", "topcenter": "Top",
            "bottom": "Bottom", "bottomcenter": "Bottom",
        }.get(normalized, "Center")

    def _validate_image_binding(self, item, snapshot, bindings, imported, issues, role="image"):
        node = item["node"]
        name = str(node.get("name") or "image")
        image_path = self._normalize_image_path(node.get("imagePath"))
        matches = [
            binding for binding in bindings
            if binding.get("role") == role
            and str(binding.get("sourceName") or "") == name
            and self._normalize_image_path(binding.get("imagePath")) == image_path
        ]
        if len(matches) != 1:
            issues.append(self._issue(
                "SPRITE_BINDING_EVIDENCE_MISSING", "error",
                f"Image {name} does not have one unambiguous {role} source-to-Sprite binding.", node=item["sourcePath"],
            ))
            return
        binding = matches[0]
        if binding.get("spriteAssetPath") != snapshot.get("spriteAssetPath"):
            issues.append(self._issue(
                "SPRITE_BINDING_MISMATCH", "error",
                f"Image {name} snapshot Sprite differs from its importer binding.", node=item["sourcePath"],
            ))
        if binding.get("sourceKind") == "imported":
            imported_image = imported.get(image_path)
            if not imported_image or imported_image.get("spriteAssetPath") != binding.get("spriteAssetPath"):
                issues.append(self._issue(
                    "SPRITE_SOURCE_IDENTITY_MISMATCH", "error",
                    f"Image {name} is not bound to the imported mapping for {node.get('imagePath')}.", node=item["sourcePath"],
                ))
            elif (
                not imported_image.get("sourcePixelHash")
                or imported_image.get("sourcePixelHash") != imported_image.get("spritePixelHash")
            ):
                issues.append(self._issue(
                    "SPRITE_PIXEL_IDENTITY_MISMATCH", "error",
                    f"Image {name} Sprite pixels do not match its package source.", node=item["sourcePath"],
                ))
        elif binding.get("sourceKind") == "skin":
            if not node.get("skinKey"):
                issues.append(self._issue(
                    "UNEXPECTED_SKIN_OVERRIDE", "error",
                    f"Image {name} used a SkinMap Sprite without a skinKey.", node=item["sourcePath"],
                ))
        else:
            issues.append(self._issue(
                "SPRITE_SOURCE_KIND_UNKNOWN", "error",
                f"Image {name} has no deterministic imported/skin resolution source.", node=item["sourcePath"],
            ))

    def _validate_text_binding(self, item, snapshot, bindings, issues, role):
        node = item["node"]
        name = str(node.get("name") or "text")
        matches = [
            binding for binding in bindings
            if binding.get("role") == role
            and str(binding.get("sourceName") or "") == name
            and str(binding.get("fontToken") or "") == str(node.get("fontToken") or "")
            and str(binding.get("materialToken") or "") == str(node.get("materialToken") or "")
        ]
        if len(matches) != 1:
            issues.append(self._issue(
                "TMP_BINDING_EVIDENCE_MISSING", "error",
                f"TMP node {name} does not have one unambiguous importer font/material binding.",
                node=item["sourcePath"],
            ))
            return
        binding = matches[0]
        if (
            binding.get("fontAssetPath") != snapshot.get("fontAssetPath")
            or binding.get("materialAssetPath") != snapshot.get("materialAssetPath")
        ):
            issues.append(self._issue(
                "TMP_BINDING_MISMATCH", "error",
                f"TMP node {name} snapshot assets differ from importer resolution evidence.",
                node=item["sourcePath"],
            ))
        font_token = str(node.get("fontToken") or "").strip()
        resolution_source = binding.get("resolutionSource")
        if font_token and (resolution_source != "fontMap" or not binding.get("matchedFontKeyword")):
            issues.append(self._issue(
                "TMP_FONT_TOKEN_FALLBACK", "error",
                f"TMP node {name} has fontToken {font_token!r} but did not resolve through TmpFontMap.",
                node=item["sourcePath"],
            ))
        elif not font_token:
            expected_font = self.request.unity_default_tmp_font_asset
            if resolution_source != "default" or (expected_font and binding.get("fontAssetPath") != expected_font):
                issues.append(self._issue(
                    "TMP_DEFAULT_FONT_MISMATCH", "error",
                    f"TMP node {name} did not use the requested default Font Asset.", node=item["sourcePath"],
                ))
            expected_material = self.request.unity_default_tmp_material_preset
            if (
                expected_material and not node.get("outlineWidth")
                and binding.get("materialAssetPath") != expected_material
            ):
                issues.append(self._issue(
                    "TMP_DEFAULT_MATERIAL_MISMATCH", "error",
                    f"TMP node {name} did not use the requested default material preset.", node=item["sourcePath"],
                ))

    def _validate_group(self, item, snapshot, all_snapshots, issues, bindings, imported, layout):
        node = item["node"]
        name = str(node.get("name") or "group")
        components = set(snapshot.get("components") or [])
        requirements = []
        if node.get("hasCanvasGroup"):
            requirements.append("CanvasGroup")
        if node.get("clipToBounds"):
            requirements.append("RectMask2D")
        if str(node.get("maskMode") or "").lower() == "sprite":
            requirements.extend(("Image", "Mask"))
        if node.get("scrollDirection"):
            requirements.append("ScrollRect")
        if node.get("scrollbarDirection"):
            requirements.append("Scrollbar")
        for component in requirements:
            if component not in components:
                issues.append(self._issue(
                    "SEMANTIC_COMPONENT_MISSING", "error",
                    f"Group {name} requires {component}.", node=item["sourcePath"],
                ))
        if str(node.get("maskMode") or "").lower() == "sprite":
            if snapshot.get("maskShowGraphic"):
                issues.append(self._issue(
                    "MASK_GRAPHIC_VISIBLE", "error",
                    f"Mask group {name} must hide its authored mask graphic.", node=item["sourcePath"],
                ))
            mask_nodes = [
                child for child in node.get("children") or []
                if isinstance(child, dict) and str(child.get("maskRole") or "").lower() == "mask"
            ]
            if len(mask_nodes) != 1:
                issues.append(self._issue(
                    "MASK_SOURCE_AMBIGUOUS", "error",
                    f"Mask group {name} must have exactly one direct mask source.", node=item["sourcePath"],
                ))
            else:
                mask_item = {"node": mask_nodes[0], "sourcePath": item["sourcePath"] + "/mask"}
                self._validate_image_binding(mask_item, snapshot, bindings, imported, issues, role="mask")

        direction = str(node.get("scrollDirection") or "").lower()
        if direction:
            if not snapshot.get("viewportPath") or not snapshot.get("contentPath"):
                issues.append(self._issue("SCROLL_WIRING_MISSING", "error", f"Scroll group {name} has no Viewport/Content wiring.", node=item["sourcePath"]))
            if (direction in {"horizontal", "both"}) != bool(snapshot.get("scrollHorizontal")):
                issues.append(self._issue("SCROLL_DIRECTION_MISMATCH", "error", f"Horizontal scroll setting differs for {name}.", node=item["sourcePath"]))
            if (direction in {"vertical", "both"}) != bool(snapshot.get("scrollVertical")):
                issues.append(self._issue("SCROLL_DIRECTION_MISMATCH", "error", f"Vertical scroll setting differs for {name}.", node=item["sourcePath"]))
            if (
                snapshot.get("scrollMovementType") != "Elastic"
                or not snapshot.get("scrollInertia")
                or abs(float(snapshot.get("scrollDecelerationRate") or 0) - 0.135) > 0.0001
                or abs(float(snapshot.get("scrollSensitivity") or 0) - 50.0) > 0.001
            ):
                issues.append(self._issue(
                    "SCROLL_BEHAVIOR_MISMATCH", "error",
                    f"Scroll behavior settings differ for {name}.", node=item["sourcePath"],
                ))
            viewport = self._single_snapshot(all_snapshots, snapshot.get("viewportPath"))
            content = self._single_snapshot(all_snapshots, snapshot.get("contentPath"))
            if viewport is None or "RectMask2D" not in set(viewport.get("components") or []) or not viewport.get("imageRaycastTarget"):
                issues.append(self._issue(
                    "SCROLL_VIEWPORT_INVALID", "error",
                    f"Scroll group {name} needs a masked raycast Viewport.", node=item["sourcePath"],
                ))
            if content is not None:
                content_differences = self._number_differences(
                    content,
                    (("width", node.get("contentWidth")), ("height", node.get("contentHeight"))),
                )
                if not item["layoutAncestor"]:
                    canvas = layout.get("canvas") or {}
                    ref_x = self.request.unity_reference_resolution_x or float(canvas.get("width") or 1920)
                    ref_y = self.request.unity_reference_resolution_y or float(canvas.get("height") or 1080)
                    content_differences.extend(self._number_differences(content, (
                        ("x", float(node.get("contentX") or 0) - max(0.0, (float(canvas.get("width") or ref_x) - ref_x) * 0.5)),
                        ("y", float(node.get("contentY") or 0) - max(0.0, (float(canvas.get("height") or ref_y) - ref_y) * 0.5)),
                    )))
                if content_differences:
                    issues.append(self._issue(
                        "SCROLL_CONTENT_RECT_MISMATCH", "error",
                        f"Scroll Content rectangle differs for {name}.",
                        node=item["sourcePath"], evidence=content_differences,
                    ))
            softness = round(float(node.get("scrollSoftnessY") or 0))
            soft_masks = [
                candidate for candidate in all_snapshots
                if str(candidate.get("path", "")).startswith(str(snapshot.get("path", "")) + "/")
                and candidate.get("name") == "SoftMaskBottom"
            ]
            if softness > 0 and (
                len(soft_masks) != 1 or int(soft_masks[0].get("rectMaskSoftnessY") or 0) != softness
            ):
                issues.append(self._issue(
                    "SCROLL_SOFTNESS_MISMATCH", "error",
                    f"Bottom scroll softness differs for {name}.", node=item["sourcePath"],
                ))
        if node.get("scrollbarDirection") and not snapshot.get("scrollbarHandlePath"):
            issues.append(self._issue("SCROLLBAR_HANDLE_MISSING", "error", f"Scrollbar {name} has no draggable handle wiring.", node=item["sourcePath"]))
        if node.get("scrollbarDirection"):
            expected_direction = "LeftToRight" if str(node.get("scrollbarDirection")).lower() == "horizontal" else "BottomToTop"
            if snapshot.get("scrollbarDirection") != expected_direction or not snapshot.get("scrollbarTargetGraphicPath"):
                issues.append(self._issue(
                    "SCROLLBAR_WIRING_MISMATCH", "error",
                    f"Scrollbar direction or target graphic differs for {name}.", node=item["sourcePath"],
                ))
            scroll_rects = [
                candidate for candidate in all_snapshots
                if "ScrollRect" in set(candidate.get("components") or [])
            ]
            owner_candidates = [
                candidate for candidate in all_snapshots
                if "ScrollRect" in set(candidate.get("components") or [])
                and str(snapshot.get("path") or "").startswith(str(candidate.get("path") or "") + "/")
            ]
            owner_field = "horizontalScrollbarPath" if expected_direction == "LeftToRight" else "verticalScrollbarPath"
            if owner_candidates:
                owner = max(owner_candidates, key=lambda candidate: len(str(candidate.get("path") or "")))
                correctly_attached = owner.get(owner_field) == snapshot.get("path")
            else:
                compatible = [
                    candidate for candidate in scroll_rects
                    if bool(candidate.get("scrollHorizontal" if expected_direction == "LeftToRight" else "scrollVertical"))
                ]
                correctly_attached = (
                    len(compatible) == 1 and compatible[0].get(owner_field) == snapshot.get("path")
                )
            if not correctly_attached:
                issues.append(self._issue(
                    "SCROLLBAR_NOT_ATTACHED", "error",
                    f"Scrollbar {name} is not assigned on the correct axis of its containing ScrollRect.", node=item["sourcePath"],
                ))

        layout_type = str(node.get("layoutType") or "").lower()
        if layout_type:
            component = {"horizontal": "HorizontalLayoutGroup", "vertical": "VerticalLayoutGroup", "grid": "GridLayoutGroup"}.get(layout_type)
            target = snapshot
            if direction and snapshot.get("contentPath"):
                matches = [candidate for candidate in all_snapshots if candidate.get("path") == snapshot.get("contentPath")]
                if len(matches) == 1:
                    target = matches[0]
            if component and component not in set(target.get("components") or []):
                issues.append(self._issue("LAYOUT_COMPONENT_MISSING", "error", f"Group {name} requires {component} on its layout target.", node=item["sourcePath"]))
            if component and component in set(target.get("components") or []):
                self._validate_layout_values(item, target, direction, issues)

    @staticmethod
    def _single_snapshot(snapshots, path):
        matches = [snapshot for snapshot in snapshots if snapshot.get("path") == path]
        return matches[0] if len(matches) == 1 else None

    @staticmethod
    def _number_differences(snapshot, fields, tolerance=0.51):
        differences = []
        for field, expected in fields:
            if abs(float(snapshot.get(field) or 0) - float(expected or 0)) > tolerance:
                differences.append(
                    f"{field}: expected {float(expected or 0):.3f}, actual {float(snapshot.get(field) or 0):.3f}"
                )
        return differences

    def _validate_layout_values(self, item, target, scroll_direction, issues):
        node = item["node"]
        name = str(node.get("name") or "group")
        layout_type = str(node.get("layoutType") or "").lower()
        differences = self._number_differences(target, (
            ("layoutPaddingLeft", node.get("layoutPaddingLeft")),
            ("layoutPaddingRight", node.get("layoutPaddingRight")),
            ("layoutPaddingTop", node.get("layoutPaddingTop")),
            ("layoutPaddingBottom", node.get("layoutPaddingBottom")),
        ), tolerance=0.01)
        if layout_type in {"horizontal", "vertical"}:
            differences.extend(self._number_differences(
                target, (("layoutSpacing", node.get("layoutSpacing")),), tolerance=0.01))
            expected_alignment = "MiddleLeft" if layout_type == "horizontal" else "UpperCenter"
            if target.get("layoutChildAlignment") != expected_alignment:
                differences.append(f"layoutChildAlignment: expected {expected_alignment}")
            if any(target.get(field) for field in (
                "layoutChildControlWidth", "layoutChildControlHeight",
                "layoutChildForceExpandWidth", "layoutChildForceExpandHeight",
            )):
                differences.append("layout child control/force-expand flags must all be false")
        elif layout_type == "grid":
            differences.extend(self._number_differences(target, (
                ("gridCellSizeX", node.get("gridCellSizeX")),
                ("gridCellSizeY", node.get("gridCellSizeY")),
                ("gridSpacingX", node.get("gridSpacingX")),
                ("gridSpacingY", node.get("gridSpacingY")),
                ("gridConstraintCount", max(1, int(node.get("gridConstraintCount") or 0))),
            ), tolerance=0.01))
            expected_axis = "Vertical" if str(node.get("gridStartAxis") or "").lower() == "vertical" else "Horizontal"
            expected_constraint = "FixedRowCount" if scroll_direction == "horizontal" else "FixedColumnCount"
            if target.get("gridStartAxis") != expected_axis:
                differences.append(f"gridStartAxis: expected {expected_axis}")
            if target.get("gridConstraint") != expected_constraint:
                differences.append(f"gridConstraint: expected {expected_constraint}")
            if target.get("layoutChildAlignment") != "UpperLeft":
                differences.append("layoutChildAlignment: expected UpperLeft")

        requires_fitter = bool(scroll_direction) or (
            bool(node.get("contentSizeFitter")) and layout_type in {"horizontal", "vertical"}
        )
        if requires_fitter:
            horizontal_scroll = scroll_direction in {"horizontal", "both"}
            vertical_scroll = scroll_direction in {"vertical", "both"}
            expected_horizontal = "PreferredSize" if (
                horizontal_scroll or (not scroll_direction and layout_type == "horizontal")
            ) else "Unconstrained"
            expected_vertical = "PreferredSize" if (
                vertical_scroll or (not scroll_direction and layout_type == "vertical")
            ) else "Unconstrained"
            if target.get("contentSizeHorizontalFit") != expected_horizontal:
                differences.append(f"contentSizeHorizontalFit: expected {expected_horizontal}")
            if target.get("contentSizeVerticalFit") != expected_vertical:
                differences.append(f"contentSizeVerticalFit: expected {expected_vertical}")
        if differences:
            issues.append(self._issue(
                "LAYOUT_PARAMETER_MISMATCH", "error",
                f"Layout parameters differ for {name}.", node=item["sourcePath"], evidence=differences,
            ))

    def enforce_decision(self, decision: AgentDecision) -> AgentDecision:
        result = self.last_result
        if result is None:
            deterministic = Status.NEEDS_REVIEW
        else:
            deterministic = Status(result["status"])
        severity = {Status.PASS: 0, Status.NEEDS_REVIEW: 1, Status.FAIL_RETRYABLE: 2, Status.BLOCKED: 3}
        if severity[decision.status] >= severity[deterministic]:
            return decision
        return decision.model_copy(update={
            "status": deterministic,
            "summary": "Model status is not backed by deterministic pipeline evidence.",
            "issues": [*decision.issues, Issue(
                code="PIPELINE_VALIDATION_UNVERIFIED", owner="PIPELINE_VALIDATOR",
                severity="warning" if deterministic == Status.NEEDS_REVIEW else "error",
                message="Run or resolve Pipeline Validator before changing this status.",
                phase="PIPELINE", retryable=deterministic == Status.FAIL_RETRYABLE,
            )],
            "next_action": "Resolve the deterministic Pipeline Validator result.",
        })

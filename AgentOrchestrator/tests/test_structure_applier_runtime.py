"""Execute the production JSX functions against a small Photoshop DOM mock.

This tests JavaScript control flow and mutations, not Photoshop COM or PSD saving.
"""

import json
import shutil
import subprocess
import unittest
from pathlib import Path


APPLIER = Path(__file__).resolve().parents[2] / "PhotoshopExporter" / "PhotoshopStructurePlanApplier.jsx"

DOM_FIXTURE = r"""
const ElementPlacement = {INSIDE: 'inside', PLACEBEFORE: 'before'};
let nextId = 100;
let app = {activeDocument: null};

function detach(layer) {
    const siblings = layer.parent.layers;
    const at = siblings.indexOf(layer);
    if (at < 0) throw new Error('Mock layer missing from parent');
    siblings.splice(at, 1);
}

function makeLayer(id, name, parent, isGroup) {
    const layer = {
        id, name, parent, visible: true,
        typename: isGroup ? 'LayerSet' : 'ArtLayer',
        kind: 'LayerKind.NORMAL',
        move(target, placement) {
            const destination = placement === ElementPlacement.INSIDE ? target : target.parent;
            if (!destination.layers) throw new Error('Mock destination is not a container');
            detach(this);
            this.parent = destination;
            if (placement === ElementPlacement.INSIDE) destination.layers.unshift(this);
            else if (placement === ElementPlacement.PLACEBEFORE) {
                destination.layers.splice(destination.layers.indexOf(target), 0, this);
            } else throw new Error('Unexpected placement');
        },
        remove() { detach(this); }
    };
    if (isGroup) addCollections(layer);
    parent.layers.push(layer);
    return layer;
}

function addCollections(container) {
    container.layers = [];
    Object.defineProperty(container, 'layerSets', {get() {
        const groups = this.layers.filter(layer => layer.typename === 'LayerSet');
        groups.add = () => makeLayer(nextId++, 'New group', this, true);
        return groups;
    }});
    container.artLayers = {add: () => makeLayer(nextId++, 'New layer', container, false)};
}

function setup(isGroup, moveFirst) {
    const document = {name: 'Screen.psd', width: 100, height: 100, typename: 'Document'};
    addCollections(document);
    app.activeDocument = document;
    const subject = makeLayer(1, 'Original', document, isGroup);
    if (isGroup) makeLayer(2, 'Untouched child', subject, false);
    const preconditions = {
        layer_id: 1, expected_name: 'Original', expected_parent: 'ROOT',
        expected_layer_kind: isGroup ? 'group' : 'LayerKind.NORMAL'
    };
    const rename = {...preconditions, action: 'rename', new_name: 'Renamed'};
    const move = {...preconditions, action: 'move', parent_ref: 'content'};
    const plan = {
        source_document: 'Screen.psd', canvas_width: 100, canvas_height: 100, approved: true,
        actions: [
            {action: 'create_group', ref: 'screen', new_name: 'Screen'},
            {action: 'create_group', ref: 'content', new_name: 'Content', parent_ref: 'screen'},
            ...(moveFirst ? [move, rename] : [rename, move])
        ]
    };
    return {document, subject, plan};
}

function execute(plan, document) {
    const index = buildLayerIndex(document);
    validateDocument(plan, document);
    validateActions(plan, index, 'apply');
    const result = {
        beforeLayerCount: index.count, createdGroupCount: 0,
        renamedCount: 0, movedCount: 0, alreadyAppliedCount: 0
    };
    applyActions(plan.actions, document, index, {}, result);
    const after = buildLayerIndex(document);
    result.afterLayerCount = after.count;
    result.originalIdsPreserved = Object.keys(index.ids).every(id => after.ids[id]);
    return result;
}

function snapshot(container) {
    return container.layers.map(layer => ({
        id: layer.id, name: layer.name, parent: layerParentKey(layer),
        children: layer.typename === 'LayerSet' ? snapshot(layer) : []
    }));
}

function runScenario(options) {
    const {document, subject, plan} = setup(options.isGroup, options.moveFirst);
    const first = execute(plan, document);
    const initialTree = snapshot(document);
    if (options.tamper === 'name') subject.name = 'Unrelated rename';
    if (options.tamper === 'parent') {
        const unrelated = makeLayer(90, 'Unrelated destination', document, true);
        subject.move(unrelated, ElementPlacement.INSIDE);
    }
    const beforeRetry = snapshot(document);
    let second = null;
    let error = null;
    try { second = execute(plan, document); }
    catch (failure) { error = failure.message; }
    return {first, second, error, initialTree, beforeRetry, afterRetry: snapshot(document)};
}
"""


class StructureApplierRuntimeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.node = shutil.which("node")
        if not cls.node:
            raise RuntimeError("Node.js is required for the JSX runtime regression tests.")
        source = APPLIER.read_text(encoding="utf-8-sig")
        # Omit #target and the application IIFE, but execute actual function bodies.
        cls.functions = source[source.index("function validateDocument("):]

    def run_scenario(self, **options):
        script = self.functions + "\n" + DOM_FIXTURE
        script += "\nprocess.stdout.write(JSON.stringify(runScenario(" + json.dumps(options) + ")));"
        result = subprocess.run(
            [self.node, "-"], input=script, text=True, encoding="utf-8",
            capture_output=True, timeout=15, check=False,
        )
        self.assertEqual(0, result.returncode, result.stderr)
        return json.loads(result.stdout)

    def test_mixed_create_rename_move_is_idempotent(self):
        for is_group in (False, True):
            for move_first in (False, True):
                with self.subTest(is_group=is_group, move_first=move_first):
                    result = self.run_scenario(isGroup=is_group, moveFirst=move_first)
                    self.assertIsNone(result["error"])
                    first, second = result["first"], result["second"]
                    self.assertEqual(2, first["createdGroupCount"])
                    self.assertEqual(1, first["renamedCount"])
                    self.assertEqual(1, first["movedCount"])
                    self.assertEqual(0, first["alreadyAppliedCount"])
                    self.assertEqual(first["beforeLayerCount"] + 2, first["afterLayerCount"])
                    self.assertTrue(first["originalIdsPreserved"])
                    self.assertEqual(4, second["alreadyAppliedCount"])
                    for counter in ("createdGroupCount", "renamedCount", "movedCount"):
                        self.assertEqual(0, second[counter])
                    self.assertEqual(first["afterLayerCount"], second["afterLayerCount"])
                    self.assertTrue(second["originalIdsPreserved"])
                    self.assertEqual(result["initialTree"], result["afterRetry"])
                    screen = next(layer for layer in result["afterRetry"] if layer["name"] == "Screen")
                    content = screen["children"][0]
                    self.assertEqual("Content", content["name"])
                    self.assertEqual([1], [layer["id"] for layer in content["children"]])
                    self.assertEqual("Renamed", content["children"][0]["name"])

    def test_companion_move_does_not_accept_an_unrelated_rename(self):
        for move_first in (False, True):
            with self.subTest(move_first=move_first):
                result = self.run_scenario(isGroup=False, moveFirst=move_first, tamper="name")
                self.assertIn("Layer name precondition failed", result["error"])
                self.assertIsNone(result["second"])
                self.assertEqual(result["beforeRetry"], result["afterRetry"])

    def test_companion_rename_does_not_accept_an_unrelated_move(self):
        for move_first in (False, True):
            with self.subTest(move_first=move_first):
                result = self.run_scenario(isGroup=False, moveFirst=move_first, tamper="parent")
                self.assertIn("Layer parent precondition failed", result["error"])
                self.assertIsNone(result["second"])
                self.assertEqual(result["beforeRetry"], result["afterRetry"])


if __name__ == "__main__":
    unittest.main()

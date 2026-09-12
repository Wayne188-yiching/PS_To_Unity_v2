#target photoshop

// Applies an already validated PSD structure plan without duplicating layers.
// Input globals:
//   PS_TO_UNITY_V2_STRUCTURE_PLAN
//   PS_TO_UNITY_V2_STRUCTURE_OPTIONS { mode: "validate"|"apply", outputFile: string }

(function () {
    var plan = $.global.PS_TO_UNITY_V2_STRUCTURE_PLAN || {};
    var options = $.global.PS_TO_UNITY_V2_STRUCTURE_OPTIONS || {};
    $.global.PS_TO_UNITY_V2_STRUCTURE_PLAN = null;
    $.global.PS_TO_UNITY_V2_STRUCTURE_OPTIONS = null;

    var result = {
        status: "BLOCKED",
        mode: options.mode || "validate",
        beforeLayerCount: 0,
        afterLayerCount: 0,
        createdGroupCount: 0,
        renamedCount: 0,
        movedCount: 0,
        alreadyAppliedCount: 0,
        planFingerprint: String(options.planFingerprint || ""),
        saved: false,
        errors: []
    };

    try {
        if (!app.documents.length) throw new Error("No open PSD document.");
        var document = app.activeDocument;
        var index = buildLayerIndex(document);
        result.beforeLayerCount = index.count;
        validateDocument(plan, document);
        validateActions(plan, index, options.mode);

        if (options.mode === "apply") {
            var originalIds = index.ids;
            var refs = {};
            applyActions(plan.actions || [], document, index, refs, result);
            var after = buildLayerIndex(document);
            result.afterLayerCount = after.count;
            var expectedCount = result.beforeLayerCount + result.createdGroupCount;
            if (result.afterLayerCount !== expectedCount) {
                throw new Error("Layer count invariant failed. Expected " + expectedCount + ", got " + result.afterLayerCount + ".");
            }
            for (var id in originalIds) {
                if (originalIds.hasOwnProperty(id) && !after.ids[id]) {
                    throw new Error("Original layer disappeared: " + id + ".");
                }
            }
            document.save();
            result.saved = true;
        } else {
            result.afterLayerCount = result.beforeLayerCount;
        }
        result.status = "PASS";
    } catch (error) {
        result.errors.push(String(error.message || error));
    }

    writeAsciiJson(options.outputFile, result);
    $.global.PS_TO_UNITY_V2_STRUCTURE_RESULT = result;
})();

function validateDocument(plan, document) {
    if (String(document.name) !== String(plan.source_document || "")) {
        throw new Error("Source document mismatch.");
    }
    if (Math.round(pixelValue(document.width)) !== Number(plan.canvas_width) ||
        Math.round(pixelValue(document.height)) !== Number(plan.canvas_height)) {
        throw new Error("Source canvas mismatch.");
    }
}

function validateActions(plan, index, mode) {
    if (mode !== "validate" && mode !== "apply") throw new Error("Unknown mode: " + mode + ".");
    if (mode === "apply" && !plan.approved) throw new Error("Plan is not approved.");
    var refs = {};
    var actions = plan.actions || [];
    var plannedRenames = {};
    var plannedMoves = {};
    for (var scan = 0; scan < actions.length; scan++) {
        if (actions[scan].action === "rename") plannedRenames[String(actions[scan].layer_id)] = actions[scan].new_name;
        if (actions[scan].action === "move") plannedMoves[String(actions[scan].layer_id)] = actions[scan];
    }
    for (var i = 0; i < actions.length; i++) {
        var action = actions[i];
        if (action.action === "create_group") {
            if (!/^[a-z][a-z0-9_]*$/.test(String(action.ref || ""))) throw new Error("Invalid group ref at action " + i + ".");
            if (refs[action.ref]) throw new Error("Duplicate group ref at action " + i + ".");
            validateAsciiName(action.new_name, i);
            if (action.parent_ref && !refs[action.parent_ref]) throw new Error("Parent ref must be created earlier at action " + i + ".");
            if (action.parent_layer_id !== null && action.parent_layer_id !== undefined) {
                validateVisibleGroup(index, action.parent_layer_id, i);
            }
            var validationParent = resolveParentContainer(action, index, refs, app.activeDocument);
            refs[action.ref] = { layer: validationParent ? findDirectGroup(validationParent, action.new_name, i) : null };
        } else if (action.action === "rename" || action.action === "move") {
            var entry = index.byId[String(action.layer_id)];
            if (!entry) throw new Error("Layer ID not found at action " + i + ".");
            if (!entry.visible) throw new Error("Hidden layer cannot be changed at action " + i + ".");
            if (action.action === "rename") {
                validateAsciiName(action.new_name, i);
                validateMechanicalPreconditions(entry, action, i, null, plannedRenames, plannedMoves, index, refs);
            } else {
                var hasRef = !!action.parent_ref;
                var hasId = action.parent_layer_id !== null && action.parent_layer_id !== undefined;
                if (hasRef === hasId) throw new Error("Move needs exactly one parent at action " + i + ".");
                if (hasRef && !refs[action.parent_ref]) throw new Error("Parent ref must be created earlier at action " + i + ".");
                if (hasId) validateVisibleGroup(index, action.parent_layer_id, i);
                var validationTarget = hasRef ? refs[action.parent_ref].layer : index.byId[String(action.parent_layer_id)].layer;
                validateMechanicalPreconditions(entry, action, i, validationTarget, plannedRenames, plannedMoves, index, refs);
            }
        } else {
            throw new Error("Unknown action at index " + i + ".");
        }
    }
}

function validateMechanicalPreconditions(entry, action, actionIndex, target, plannedRenames, plannedMoves, index, refs) {
    var expectedKind = String(action.expected_layer_kind || "");
    var actualKind = entry.layer.typename === "LayerSet" ? "group" : String(entry.layer.kind);
    if (!expectedKind || actualKind !== expectedKind) {
        throw new Error("Layer kind precondition failed at action " + actionIndex + ".");
    }
    var expectedName = String(action.expected_name || "");
    var currentName = String(entry.layer.name || "");
    var renameAlreadyApplied = action.action === "rename" && currentName === String(action.new_name || "");
    var currentParent = layerParentKey(entry.layer);
    var moveAlreadyApplied = action.action === "move" && target && currentParent === String(target.id);
    var layerId = String(action.layer_id);
    var renamedAsPartOfPlan = plannedRenames[layerId] && currentName === String(plannedRenames[layerId]);
    if (!expectedName || (currentName !== expectedName && !renameAlreadyApplied && !renamedAsPartOfPlan)) {
        throw new Error("Layer name precondition failed at action " + actionIndex + ".");
    }
    var companionMove = plannedMoves[layerId];
    var companionTarget = companionMove ? resolveParentContainer(companionMove, index, refs, app.activeDocument) : null;
    var movedAsPartOfPlan = companionTarget && companionTarget.id !== undefined && currentParent === String(companionTarget.id);
    if (!action.expected_parent ||
        (currentParent !== String(action.expected_parent) && !moveAlreadyApplied && !movedAsPartOfPlan)) {
        throw new Error("Layer parent precondition failed at action " + actionIndex + ".");
    }
}

function validateVisibleGroup(index, layerId, actionIndex) {
    var entry = index.byId[String(layerId)];
    if (!entry || entry.layer.typename !== "LayerSet") throw new Error("Parent is not a group at action " + actionIndex + ".");
    if (!entry.visible) throw new Error("Hidden parent cannot receive layers at action " + actionIndex + ".");
}

function validateAsciiName(value, actionIndex) {
    var name = String(value || "");
    if (!name || /[^\x20-\x7e]/.test(name)) throw new Error("Name must be printable ASCII at action " + actionIndex + ".");
}

function applyActions(actions, document, initialIndex, refs, result) {
    for (var i = 0; i < actions.length; i++) {
        var action = actions[i];
        if (action.action === "create_group") {
            var parent = resolveParentContainer(action, initialIndex, refs, document);
            var group = findDirectGroup(parent, action.new_name, i);
            if (group) {
                result.alreadyAppliedCount++;
            } else {
                group = parent.layerSets.add();
                group.name = action.new_name;
                result.createdGroupCount++;
            }
            refs[action.ref] = group;
        } else if (action.action === "rename") {
            var renameLayer = initialIndex.byId[String(action.layer_id)].layer;
            if (String(renameLayer.name) === String(action.new_name)) {
                result.alreadyAppliedCount++;
            } else {
                renameLayer.name = action.new_name;
                result.renamedCount++;
            }
        } else if (action.action === "move") {
            var target = action.parent_ref
                ? refs[action.parent_ref]
                : initialIndex.byId[String(action.parent_layer_id)].layer;
            var movingLayer = initialIndex.byId[String(action.layer_id)].layer;
            if (layerParentKey(movingLayer) === String(target.id)) {
                result.alreadyAppliedCount++;
            } else {
                moveLayerInsideGroup(movingLayer, target, document);
                result.movedCount++;
            }
        }
    }
}

function resolveParentContainer(action, index, refs, document) {
    if (action.parent_ref) {
        var ref = refs[action.parent_ref];
        return ref && ref.layer ? ref.layer : ref;
    }
    if (action.parent_layer_id !== null && action.parent_layer_id !== undefined) {
        return index.byId[String(action.parent_layer_id)].layer;
    }
    return document;
}

function findDirectGroup(container, name, actionIndex) {
    if (!container || !container.layerSets) return null;
    var match = null;
    for (var i = 0; i < container.layerSets.length; i++) {
        if (String(container.layerSets[i].name) !== String(name)) continue;
        if (match) throw new Error("Multiple matching groups at action " + actionIndex + ".");
        match = container.layerSets[i];
    }
    return match;
}

function layerParentKey(layer) {
    try {
        return layer.parent && layer.parent.typename === "LayerSet" ? String(layer.parent.id) : "ROOT";
    } catch (error) {
        return "ROOT";
    }
}

function moveLayerInsideGroup(layer, target, document) {
    if (layer.typename !== "LayerSet") {
        layer.move(target, ElementPlacement.INSIDE);
        return;
    }
    var marker = document.artLayers.add();
    marker.name = "__PS_TO_UNITY_MOVE_MARKER__";
    try {
        marker.move(target, ElementPlacement.INSIDE);
        layer.move(marker, ElementPlacement.PLACEBEFORE);
    } finally {
        try { marker.remove(); } catch (error) {}
    }
}

function buildLayerIndex(container) {
    var result = { byId: {}, ids: {}, count: 0 };
    collectLayerIndex(container, true, result, null);
    return result;
}

function collectLayerIndex(container, parentVisible, result, parentId) {
    for (var i = 0; i < container.layers.length; i++) {
        var layer = container.layers[i];
        var visible = parentVisible && layerVisible(layer);
        var id = String(layer.id);
        result.byId[id] = { layer: layer, visible: visible, parentId: parentId };
        result.ids[id] = true;
        result.count++;
        if (layer.typename === "LayerSet") collectLayerIndex(layer, visible, result, layer.id);
    }
}

function layerVisible(layer) {
    try { return !!layer.visible; } catch (error) { return true; }
}

function pixelValue(value) {
    try { return Number(value.as("px")); } catch (error) { return Number(value); }
}

function writeAsciiJson(path, payload) {
    if (!path) return;
    var file = new File(path);
    if (!file.parent.exists) file.parent.create();
    file.encoding = "ASCII";
    file.lineFeed = "Unix";
    if (!file.open("w")) return;
    file.write(toAsciiJson(payload));
    file.close();
}

function toAsciiJson(value) {
    if (value === null || value === undefined) return "null";
    if (typeof value === "string") return quoteAsciiJson(value);
    if (typeof value === "number") return isFinite(value) ? String(value) : "0";
    if (typeof value === "boolean") return value ? "true" : "false";
    var parts = [];
    var i;
    if (value instanceof Array) {
        for (i = 0; i < value.length; i++) parts.push(toAsciiJson(value[i]));
        return "[" + parts.join(",") + "]";
    }
    for (var key in value) {
        if (value.hasOwnProperty(key)) parts.push(quoteAsciiJson(key) + ":" + toAsciiJson(value[key]));
    }
    return "{" + parts.join(",") + "}";
}

function quoteAsciiJson(value) {
    var text = String(value);
    var result = '"';
    for (var i = 0; i < text.length; i++) {
        var ch = text.charAt(i);
        var code = text.charCodeAt(i);
        if (ch === '"') result += '\\"';
        else if (ch === "\\") result += "\\\\";
        else if (ch === "\b") result += "\\b";
        else if (ch === "\f") result += "\\f";
        else if (ch === "\n") result += "\\n";
        else if (ch === "\r") result += "\\r";
        else if (ch === "\t") result += "\\t";
        else if (code < 32 || code > 126) result += "\\u" + padHex4(code);
        else result += ch;
    }
    return result + '"';
}

function padHex4(value) {
    var result = value.toString(16);
    while (result.length < 4) result = "0" + result;
    return result;
}

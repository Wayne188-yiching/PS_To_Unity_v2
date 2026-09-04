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
            refs[action.ref] = true;
        } else if (action.action === "rename" || action.action === "move") {
            var entry = index.byId[String(action.layer_id)];
            if (!entry) throw new Error("Layer ID not found at action " + i + ".");
            if (!entry.visible) throw new Error("Hidden layer cannot be changed at action " + i + ".");
            if (action.action === "rename") {
                validateAsciiName(action.new_name, i);
            } else {
                var hasRef = !!action.parent_ref;
                var hasId = action.parent_layer_id !== null && action.parent_layer_id !== undefined;
                if (hasRef === hasId) throw new Error("Move needs exactly one parent at action " + i + ".");
                if (hasRef && !refs[action.parent_ref]) throw new Error("Parent ref must be created earlier at action " + i + ".");
                if (hasId) validateVisibleGroup(index, action.parent_layer_id, i);
            }
        } else {
            throw new Error("Unknown action at index " + i + ".");
        }
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
            var group;
            if (action.parent_ref) {
                group = refs[action.parent_ref].layerSets.add();
            } else if (action.parent_layer_id !== null && action.parent_layer_id !== undefined) {
                group = initialIndex.byId[String(action.parent_layer_id)].layer.layerSets.add();
            } else {
                group = document.layerSets.add();
            }
            group.name = action.new_name;
            refs[action.ref] = group;
            result.createdGroupCount++;
        } else if (action.action === "rename") {
            initialIndex.byId[String(action.layer_id)].layer.name = action.new_name;
            result.renamedCount++;
        } else if (action.action === "move") {
            var target = action.parent_ref
                ? refs[action.parent_ref]
                : initialIndex.byId[String(action.parent_layer_id)].layer;
            moveLayerInsideGroup(initialIndex.byId[String(action.layer_id)].layer, target, document);
            result.movedCount++;
        }
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
    collectLayerIndex(container, true, result);
    return result;
}

function collectLayerIndex(container, parentVisible, result) {
    for (var i = 0; i < container.layers.length; i++) {
        var layer = container.layers[i];
        var visible = parentVisible && layerVisible(layer);
        var id = String(layer.id);
        result.byId[id] = { layer: layer, visible: visible };
        result.ids[id] = true;
        result.count++;
        if (layer.typename === "LayerSet") collectLayerIndex(layer, visible, result);
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

#target photoshop

// Read-only PSD hierarchy inspector for agent/tool automation.
// Input:  $.global.PS_TO_UNITY_V2_INSPECT_OPTIONS
// Output: $.global.PS_TO_UNITY_V2_INSPECT_RESULT
(function () {
    var options = $.global.PS_TO_UNITY_V2_INSPECT_OPTIONS || {};
    $.global.PS_TO_UNITY_V2_INSPECT_OPTIONS = null;

    var openedByInspector = false;
    var document = null;

    try {
        if (options.psdPath) {
            var inputFile = new File(options.psdPath);
            if (!inputFile.exists) {
                throw new Error("PSD not found: " + inputFile.fsName);
            }
            document = app.open(inputFile);
            openedByInspector = true;
        } else if (app.documents.length > 0) {
            document = app.activeDocument;
        } else {
            throw new Error("No PSD path was provided and Photoshop has no open document.");
        }

        var payload = inspectDocument(document);
        var outputFile = new File(options.outputFile);
        if (!outputFile.parent.exists && !outputFile.parent.create()) {
            throw new Error("Could not create output folder: " + outputFile.parent.fsName);
        }
        outputFile.encoding = "UTF8";
        outputFile.lineFeed = "Unix";
        if (!outputFile.open("w")) {
            throw new Error("Could not open output file: " + outputFile.fsName);
        }
        outputFile.write(toJson(payload, 0));
        outputFile.close();

        $.global.PS_TO_UNITY_V2_INSPECT_RESULT = {
            outputFile: outputFile.fsName,
            layerCount: payload.summary.layerCount,
            groupCount: payload.summary.groupCount,
            textCount: payload.summary.textCount
        };
    } catch (error) {
        $.global.PS_TO_UNITY_V2_INSPECT_RESULT = { error: error.message };
    } finally {
        if (openedByInspector && document) {
            try {
                document.close(SaveOptions.DONOTSAVECHANGES);
            } catch (closeError) {
            }
        }
    }
})();

function inspectDocument(document) {
    var summary = { layerCount: 0, groupCount: 0, textCount: 0 };
    var nodes = [];
    inspectContainer(document, "", nodes, summary);

    return {
        schemaVersion: "1.0",
        document: {
            name: document.name,
            width: unitPixels(document.width),
            height: unitPixels(document.height),
            resolution: finiteNumber(document.resolution),
            colorMode: String(document.mode)
        },
        summary: summary,
        layers: nodes
    };
}

function inspectContainer(container, parentPath, output, summary) {
    for (var index = 0; index < container.layers.length; index++) {
        var layer = container.layers[index];
        var path = parentPath ? parentPath + "/" + layer.name : layer.name;
        var bounds = readBounds(layer);
        var node = {
            index: index,
            id: readLayerId(layer),
            name: layer.name,
            path: path,
            nodeType: layer.typename === "LayerSet" ? "group" : "layer",
            layerKind: readLayerKind(layer),
            visible: !!layer.visible,
            opacity: finiteNumber(layer.opacity),
            blendMode: String(layer.blendMode),
            bounds: bounds,
            width: Math.max(0, bounds.right - bounds.left),
            height: Math.max(0, bounds.bottom - bounds.top)
        };

        summary.layerCount++;
        if (layer.typename === "LayerSet") {
            summary.groupCount++;
            node.children = [];
            inspectContainer(layer, path, node.children, summary);
        } else if (layer.kind === LayerKind.TEXT) {
            summary.textCount++;
            node.text = readText(layer);
        } else if (layer.kind === LayerKind.SMARTOBJECT) {
            node.smartObject = readSmartObject(layer);
        }
        output.push(node);
    }
}

function readBounds(layer) {
    try {
        var bounds = layer.bounds;
        return {
            left: unitPixels(bounds[0]),
            top: unitPixels(bounds[1]),
            right: unitPixels(bounds[2]),
            bottom: unitPixels(bounds[3])
        };
    } catch (error) {
        return { left: 0, top: 0, right: 0, bottom: 0 };
    }
}

function readLayerId(layer) {
    try {
        return layer.id;
    } catch (error) {
        return null;
    }
}

function readLayerKind(layer) {
    if (layer.typename === "LayerSet") {
        return "group";
    }
    try {
        return String(layer.kind);
    } catch (error) {
        return "unknown";
    }
}

function readText(layer) {
    var item = layer.textItem;
    var result = {};
    try { result.contents = item.contents; } catch (error1) {}
    try { result.font = item.font; } catch (error2) {}
    try { result.size = unitPixels(item.size); } catch (error3) {}
    try { result.justification = String(item.justification); } catch (error4) {}
    try { result.kind = String(item.kind); } catch (error5) {}
    return result;
}

function readSmartObject(layer) {
    var result = {};
    try {
        var reference = new ActionReference();
        reference.putIdentifier(charIDToTypeID("Lyr "), layer.id);
        var descriptor = executeActionGet(reference);
        var smartObjectKey = stringIDToTypeID("smartObject");
        if (!descriptor.hasKey(smartObjectKey)) return result;
        var smartObject = descriptor.getObjectValue(smartObjectKey);
        var fileReferenceKey = stringIDToTypeID("fileReference");
        if (smartObject.hasKey(fileReferenceKey)) {
            result.fileReference = smartObject.getString(fileReferenceKey);
        }
        var linkKey = stringIDToTypeID("link");
        if (smartObject.hasKey(linkKey)) {
            var linkType = smartObject.getType(linkKey);
            if (linkType === DescValueType.ALIASTYPE) {
                result.link = smartObject.getPath(linkKey).fsName;
            } else if (linkType === DescValueType.STRINGTYPE) {
                result.link = smartObject.getString(linkKey);
            }
        }
    } catch (error) {
    }
    return result;
}

function unitPixels(value) {
    try {
        return finiteNumber(value.as("px"));
    } catch (error) {
        return finiteNumber(value);
    }
}

function finiteNumber(value) {
    var number = Number(value);
    return isFinite(number) ? number : 0;
}

function toJson(value, depth) {
    if (value === null || value === undefined) return "null";
    if (typeof value === "string") return quoteJson(value);
    if (typeof value === "number") return isFinite(value) ? String(value) : "0";
    if (typeof value === "boolean") return value ? "true" : "false";

    var indent = repeatText("  ", depth);
    var childIndent = repeatText("  ", depth + 1);
    var parts = [];
    var index;

    if (value instanceof Array) {
        for (index = 0; index < value.length; index++) {
            parts.push(childIndent + toJson(value[index], depth + 1));
        }
        return parts.length ? "[\n" + parts.join(",\n") + "\n" + indent + "]" : "[]";
    }

    for (var key in value) {
        if (value.hasOwnProperty(key)) {
            parts.push(childIndent + quoteJson(key) + ": " + toJson(value[key], depth + 1));
        }
    }
    return parts.length ? "{\n" + parts.join(",\n") + "\n" + indent + "}" : "{}";
}

function quoteJson(value) {
    // ExtendScript can corrupt non-ASCII text when File.encoding is UTF8.
    // JSON unicode escapes keep layer names lossless while the file stays ASCII-safe.
    var text = String(value);
    var result = '"';
    for (var index = 0; index < text.length; index++) {
        var character = text.charAt(index);
        var code = text.charCodeAt(index);
        if (character === '"') result += '\\"';
        else if (character === "\\") result += "\\\\";
        else if (character === "\b") result += "\\b";
        else if (character === "\f") result += "\\f";
        else if (character === "\n") result += "\\n";
        else if (character === "\r") result += "\\r";
        else if (character === "\t") result += "\\t";
        else if (code < 32 || code > 126) result += "\\u" + padHex4(code);
        else result += character;
    }
    return result + '"';
}

function padHex4(value) {
    var result = value.toString(16);
    while (result.length < 4) result = "0" + result;
    return result;
}

function repeatText(value, count) {
    var result = "";
    for (var index = 0; index < count; index++) result += value;
    return result;
}

#target photoshop

(function () {
    if (!app.documents.length) {
        alert("Open a PSD document before running Photoshop Layer Auto Namer.");
        return;
    }

    var doc = app.activeDocument;
    var approved = confirm(
        "Rename PSD layers to English production names?\n\n" +
        "This script renames Photoshop groups, normal layers, and text layers.\n" +
        "It does not export images, create folders, move layers, or change layer visibility.\n\n" +
        "Save the PSD only after reviewing the result."
    );

    if (!approved) {
        return;
    }

    var summary = autoRenameDocument(doc);
    alert(
        "Layer rename complete.\n\n" +
        "Renamed layers: " + summary.count + "\n" +
        "Naming style uses GROUP_, IMG_, TXT_, IGNORE_, and REF_ prefixes."
    );
})();

function autoRenameDocument(doc) {
    var renameState = {
        counters: {},
        count: 0
    };

    renameLayersInContainer(doc, renameState);
    return renameState;
}

function renameLayersInContainer(container, state) {
    for (var i = container.layers.length - 1; i >= 0; i--) {
        var layer = container.layers[i];

        var originalName = layer.name;
        var renamed = createEnglishLayerName(layer, originalName, state);

        if (renamed && renamed !== originalName) {
            layer.name = renamed;
            state.count++;
        }

        if (layer.typename === "LayerSet") {
            renameLayersInContainer(layer, state);
            continue;
        }
    }
}

function createEnglishLayerName(layer, originalName, state) {
    var marker = readControlPrefix(originalName);
    var prefix = resolveNamingPrefix(layer);
    var role = inferLayerRole(layer, originalName);
    var metadataSuffix = readMetadataSuffix(originalName);
    var base = marker + prefix + "_" + role;

    if (!state.counters[base]) {
        state.counters[base] = 1;
        return base + metadataSuffix;
    }

    state.counters[base]++;
    return base + "_" + pad2(state.counters[base]) + metadataSuffix;
}

function readControlPrefix(name) {
    if (startsWith(name, "IGNORE_")) {
        return "IGNORE_";
    }
    if (startsWith(name, "REF_")) {
        return "REF_";
    }
    return "";
}

function readMetadataSuffix(name) {
    var text = String(name || "");
    var tags = [];
    var pattern = /\[(MAT|MATERIAL|TMPMAT)\s*[:=]\s*([^\]]+)\]/ig;
    var match;

    while ((match = pattern.exec(text)) !== null) {
        tags.push("[MAT:" + normalizeAsciiWords(match[2]) + "]");
    }

    return tags.length > 0 ? "_" + tags.join("_") : "";
}

function resolveNamingPrefix(layer) {
    if (layer.typename === "LayerSet") {
        return "GROUP";
    }
    if (isTextLayer(layer)) {
        return "TXT";
    }
    return "IMG";
}

function inferLayerRole(layer, originalName) {
    var roleFromName = inferRoleFromExistingName(originalName);
    if (roleFromName) {
        return roleFromName;
    }

    if (layer.typename === "LayerSet") {
        return "group";
    }

    if (isTextLayer(layer)) {
        return inferTextRole(layer);
    }

    if (isSmartObjectLayer(layer)) {
        return "smart_object";
    }

    return inferImageRoleFromBounds(layer);
}

function inferRoleFromExistingName(name) {
    var plain = normalizeAsciiWords(name);
    var raw = String(name).toLowerCase();
    var rules = [
        { role: "background", words: ["background", "bg", "\u80cc\u666f", "\u5e95\u5716"] },
        { role: "panel", words: ["panel", "popup", "dialog", "\u9762\u677f", "\u8996\u7a97", "\u5f48\u7a97"] },
        { role: "button", words: ["button", "btn", "\u6309\u9215"] },
        { role: "title", words: ["title", "headline", "\u6a19\u984c"] },
        { role: "label", words: ["label", "caption", "\u6a19\u7c64", "\u8aaa\u660e"] },
        { role: "icon", words: ["icon", "\u5716\u793a"] },
        { role: "reward", words: ["reward", "prize", "\u734e\u52f5", "\u7372\u5f97"] },
        { role: "gem", words: ["gem", "jewel", "crystal", "\u5bf6\u73e0", "\u5bf6\u77f3"] },
        { role: "frame", words: ["frame", "border", "\u5916\u6846", "\u908a\u6846"] },
        { role: "mask", words: ["mask", "\u906e\u7f69"] },
        { role: "glow", words: ["glow", "shine", "\u5149", "\u5149\u6697"] },
        { role: "divider", words: ["divider", "line", "\u5206\u9694", "\u7dda"] },
        { role: "group", words: ["group", "folder", "\u7fa4\u7d44", "\u8cc7\u6599\u593e"] }
    ];

    for (var i = 0; i < rules.length; i++) {
        for (var j = 0; j < rules[i].words.length; j++) {
            var word = rules[i].words[j];
            if (plain.indexOf(word) >= 0 || raw.indexOf(word) >= 0) {
                return rules[i].role;
            }
        }
    }

    if (plain && !/^[0-9]+$/.test(plain)) {
        return shortenSlug(plain);
    }

    return "";
}

function inferTextRole(layer) {
    var content = readText(layer);
    var fontSize = readFontSize(layer);
    if (fontSize >= 40) {
        return "title";
    }
    if (String(content).length >= 18) {
        return "message";
    }
    return "label";
}

function inferImageRoleFromBounds(layer) {
    var rect = readBounds(layer);
    var docWidth = px(app.activeDocument.width);
    var docHeight = px(app.activeDocument.height);
    var longest = Math.max(rect.width, rect.height);
    var shortest = Math.max(1, Math.min(rect.width, rect.height));

    if (rect.width >= docWidth * 0.7 && rect.height >= docHeight * 0.7) {
        return "background";
    }

    if (rect.width >= docWidth * 0.45 && rect.height >= docHeight * 0.25) {
        return "panel";
    }

    if (longest / shortest >= 4 && rect.height <= docHeight * 0.14) {
        return "divider";
    }

    if (longest <= Math.min(docWidth, docHeight) * 0.18) {
        return "icon";
    }

    return "art";
}

function readBounds(layer) {
    try {
        var b = layer.bounds;
        var left = px(b[0]);
        var top = px(b[1]);
        var right = px(b[2]);
        var bottom = px(b[3]);
        return {
            width: Math.max(1, right - left),
            height: Math.max(1, bottom - top)
        };
    } catch (e) {
        return {
            width: 1,
            height: 1
        };
    }
}

function readText(layer) {
    try {
        return layer.textItem.contents || "";
    } catch (e) {
        return "";
    }
}

function readFontSize(layer) {
    try {
        return round2(px(layer.textItem.size));
    } catch (e) {
        return 24;
    }
}

function normalizeAsciiWords(name) {
    return String(stripPrefix(name))
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, "_")
        .replace(/^_+|_+$/g, "")
        .replace(/_+/g, "_");
}

function shortenSlug(value) {
    var text = String(value);
    return text.length > 36 ? text.substring(0, 36) : text;
}

function startsWith(text, prefix) {
    return String(text).indexOf(prefix) === 0;
}

function stripPrefix(name) {
    var text = String(name);
    var prefixes = ["IMG_", "BTN_", "TXT_", "SKIN_", "GROUP_", "IGNORE_", "REF_"];
    for (var i = 0; i < prefixes.length; i++) {
        if (startsWith(text, prefixes[i])) {
            return text.substring(prefixes[i].length);
        }
    }
    return text;
}

function isTextLayer(layer) {
    try {
        return layer.kind === LayerKind.TEXT;
    } catch (e) {
        return false;
    }
}

function isSmartObjectLayer(layer) {
    try {
        return typeof LayerKind.SMARTOBJECT !== "undefined" && layer.kind === LayerKind.SMARTOBJECT;
    } catch (e) {
        return false;
    }
}

function px(value) {
    try {
        return round2(value.as("px"));
    } catch (e) {
        return round2(Number(value));
    }
}

function round2(value) {
    return Math.round(value * 100) / 100;
}

function pad2(value) {
    var text = String(value);
    return text.length >= 2 ? text : "0" + text;
}

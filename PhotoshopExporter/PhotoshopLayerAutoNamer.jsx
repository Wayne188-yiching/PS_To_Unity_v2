#target photoshop

// PS To Unity v2 - Layer Auto Namer
//
// WARNING: keep this file pure ASCII. Photoshop reads .jsx without a BOM using the
// system codepage (CP950 on zh-TW), so any non-ASCII byte in the source breaks
// parsing with only "JavaScript code missing" and no line number. All CJK lives in
// naming_glossary.tsv, which is read at runtime with an explicit UTF-8 encoding.
//
// Naming model:
//   marker + IMG_/TXT_/GROUP_ + <translated base> + <family NN> [+ _<variant>] + <tags>
//   family  = distinct parent group using the same translated base -> 01, 02, 03...
//   variant = same parent group AND same translated base           -> _1, _2, _3...
//   e.g. ranking_ui_frame01, ranking_ui_frame01_1, ranking_ui_frame02, ...
//
// Layers whose name cannot be fully resolved through the glossary are LEFT ALONE and
// reported, instead of being renamed into a meaningless generic role.

var GLOSSARY_FILE = "naming_glossary.tsv";
var TODO_FILE = "naming_glossary_todo.tsv";

(function () {
    if (!app.documents.length) {
        alert("Open a PSD document before running Photoshop Layer Auto Namer.");
        return;
    }

    var glossary = loadGlossary();
    if (glossary.missing) {
        alert(
            "Glossary not found:\n" + glossary.path + "\n\n" +
            "This file ships with the tool. Restore it from the repository, then run again."
        );
        return;
    }

    var doc = app.activeDocument;
    var plan = buildPlan(doc, glossary);

    if (plan.entries.length === 0) {
        alert("No layers found.");
        return;
    }

    if (plan.unknownTerms.length > 0) {
        var todoPath = writeTodoFile(plan.unknownTerms);
        var proceed = confirm(
            "Unresolved terms: " + plan.unknownTerms.length + "\n" +
            "Layers left untouched because of them: " + plan.skipped + "\n\n" +
            "THESE LAYERS ARE NOT EXPORTABLE YET. The UI Package Exporter strips every\n" +
            "non-ASCII character from layer names, so a name it cannot convert collapses\n" +
            "to layer.png / layer_002.png with all meaning lost -- and it does so silently.\n\n" +
            "The terms were written to:\n" + todoPath + "\n\n" +
            "Fill in the English column there and paste the lines into " + GLOSSARY_FILE +
            ", then run again to cover those layers.\n\n" +
            "Continue and rename the " + plan.renameCount + " layer(s) that DID resolve?"
        );
        if (!proceed) {
            return;
        }
    }

    if (plan.renameCount === 0) {
        alert("Nothing to rename. Every layer either resolved to its current name or was skipped.");
        return;
    }

    if (!showPreview(plan)) {
        return;
    }

    var applied = applyPlan(plan);

    // Verify our own output. This tool exists to remove non-ASCII layer names; anything
    // still carrying one after a run is a layer the exporter will silently destroy.
    var leftover = collectNonAsciiLayerNames(doc);
    var summary =
        "Layer rename complete.\n\n" +
        "Renamed: " + applied + "\n" +
        "Skipped (unresolved terms): " + plan.skipped + "\n" +
        "Unchanged (already correct): " + (plan.entries.length - applied - plan.skipped);

    if (leftover.length > 0) {
        summary +=
            "\n\n----------------------------------------\n" +
            "NOT SAFE TO EXPORT: " + leftover.length + " layer(s) still have a non-ASCII name.\n\n" +
            "The UI Package Exporter cannot convert them. Each one will export as\n" +
            "layer.png / layer_002.png / layer_003.png, which still passes Unity's\n" +
            "validation, so the damage is silent.\n\n" +
            "First few:\n  " + leftover.slice(0, 8).join("\n  ") +
            (leftover.length > 8 ? "\n  ... and " + (leftover.length - 8) + " more" : "") +
            "\n\nAdd the missing terms to " + GLOSSARY_FILE + " and run again before exporting.";
    }

    alert(summary);
})();

function collectNonAsciiLayerNames(container, found) {
    if (!found) {
        found = [];
    }
    for (var i = 0; i < container.layers.length; i++) {
        var layer = container.layers[i];
        if (/[^\x00-\x7F]/.test(String(layer.name))) {
            found.push(layer.name);
        }
        if (layer.typename === "LayerSet") {
            collectNonAsciiLayerNames(layer, found);
        }
    }
    return found;
}

// -- glossary ----------------------------------------------------------------

function scriptFolder() {
    return new File($.fileName).parent;
}

function loadGlossary() {
    var file = new File(scriptFolder().fsName + "/" + GLOSSARY_FILE);
    var result = { map: {}, keys: [], path: file.fsName, missing: true };

    if (!file.exists) {
        return result;
    }

    file.encoding = "UTF-8";
    if (!file.open("r")) {
        return result;
    }
    var text = file.read();
    file.close();

    var lines = text.split(/\r\n|\r|\n/);
    for (var i = 0; i < lines.length; i++) {
        var line = lines[i];
        if (!line) {
            continue;
        }
        if (line.charAt(0) === "#") {
            continue;
        }
        var parts = line.split("\t");
        if (parts.length < 2) {
            continue;
        }
        var source = trim(parts[0]);
        var english = trim(parts[1]);
        if (!source || !english) {
            continue;
        }
        // "k_" guard so terms like "constructor" cannot collide with Object.prototype.
        if (result.map["k_" + source] === undefined) {
            result.map["k_" + source] = english;
            result.keys.push(source);
        }
    }

    // Longest first, so the segmenter's first hit is always the longest match.
    result.keys.sort(function (a, b) { return b.length - a.length; });
    result.missing = false;
    return result;
}

function writeTodoFile(terms) {
    var file = new File(scriptFolder().fsName + "/" + TODO_FILE);
    file.encoding = "UTF-8";
    file.lineFeed = "Windows";
    if (!file.open("w")) {
        return "(could not write " + file.fsName + ")";
    }
    file.write("# Unresolved terms from the last Layer Auto Namer run.\n");
    file.write("# Fill in the second column, then paste these lines into " + GLOSSARY_FILE + ".\n");
    for (var i = 0; i < terms.length; i++) {
        file.write(terms[i] + "\t\n");
    }
    file.close();
    return file.fsName;
}

// -- planning ----------------------------------------------------------------

function buildPlan(doc, glossary) {
    var plan = {
        entries: [],
        unknownTerms: [],
        unknownSeen: {},
        skipped: 0,
        renameCount: 0,
        families: {},
        familyNext: {},
        variants: {}
    };

    collectLayers(doc, "", plan, glossary);

    for (var i = 0; i < plan.entries.length; i++) {
        var entry = plan.entries[i];
        if (entry.resolved) {
            entry.newName = composeName(entry, plan);
            if (entry.newName !== entry.oldName) {
                plan.renameCount++;
            }
        } else {
            plan.skipped++;
        }
    }

    return plan;
}

function collectLayers(container, parentPath, plan, glossary) {
    for (var i = container.layers.length - 1; i >= 0; i--) {
        var layer = container.layers[i];
        var parsed = parseName(layer.name, glossary);

        var entry = {
            layer: layer,
            oldName: layer.name,
            newName: layer.name,
            parentPath: parentPath,
            marker: parsed.marker,
            tags: parsed.tags,
            prefix: resolveNamingPrefix(layer),
            base: parsed.base,
            resolved: parsed.resolved
        };
        plan.entries.push(entry);

        for (var u = 0; u < parsed.unknown.length; u++) {
            var term = parsed.unknown[u];
            if (!plan.unknownSeen["u_" + term]) {
                plan.unknownSeen["u_" + term] = true;
                plan.unknownTerms.push(term);
            }
        }

        if (layer.typename === "LayerSet") {
            collectLayers(layer, parentPath + "/" + layer.name, plan, glossary);
        }
    }
}

function parseName(rawName, glossary) {
    var marker = readControlPrefix(rawName);
    var tags = extractBracketTags(rawName);

    var body = String(rawName);
    if (marker) {
        body = body.substring(marker.length);
    }
    body = removeBracketTags(body);
    body = stripLegacyPrefix(body);
    body = stripGeneratedIndex(body);

    var seg = segment(body, glossary);

    return {
        marker: marker,
        tags: tags,
        base: seg.words.join("_"),
        unknown: seg.unknown,
        // A name is resolved only when every CJK run mapped and something is left.
        resolved: seg.unknown.length === 0 && seg.words.length > 0
    };
}

// Greedy longest-match segmentation. ASCII runs pass through as-is; CJK runs are
// looked up in the glossary; anything left over is reported instead of guessed.
function segment(body, glossary) {
    var words = [];
    var unknown = [];
    var ascii = "";
    var i = 0;

    while (i < body.length) {
        var ch = body.charAt(i);

        if (isAsciiWordChar(ch)) {
            ascii += ch;
            i++;
            continue;
        }

        if (ascii) {
            words.push(ascii.toLowerCase());
            ascii = "";
        }

        if (isSeparator(ch)) {
            i++;
            continue;
        }

        var hit = matchGlossaryAt(body, i, glossary);
        if (hit) {
            words.push(glossary.map["k_" + hit]);
            i += hit.length;
            continue;
        }

        var run = "";
        while (i < body.length) {
            var c = body.charAt(i);
            if (isAsciiWordChar(c) || isSeparator(c)) {
                break;
            }
            if (matchGlossaryAt(body, i, glossary)) {
                break;
            }
            run += c;
            i++;
        }
        if (run) {
            unknown.push(run);
        }
    }

    if (ascii) {
        words.push(ascii.toLowerCase());
    }

    return { words: words, unknown: unknown };
}

function matchGlossaryAt(text, index, glossary) {
    for (var k = 0; k < glossary.keys.length; k++) {
        var key = glossary.keys[k];
        if (text.substr(index, key.length) === key) {
            return key;
        }
    }
    return null;
}

function composeName(entry, plan) {
    var fkey = "b_" + entry.prefix + "_" + entry.base;
    var pkey = "p_" + entry.parentPath;

    if (!plan.families[fkey]) {
        plan.families[fkey] = {};
        plan.familyNext[fkey] = 0;
    }
    if (plan.families[fkey][pkey] === undefined) {
        plan.familyNext[fkey]++;
        plan.families[fkey][pkey] = plan.familyNext[fkey];
    }
    var family = plan.families[fkey][pkey];

    var vkey = fkey + "|" + pkey;
    plan.variants[vkey] = (plan.variants[vkey] || 0) + 1;
    var variant = plan.variants[vkey] - 1;

    var name = entry.marker + entry.prefix + "_" + entry.base + pad2(family);
    if (variant > 0) {
        name += "_" + variant;
    }
    if (entry.tags.length > 0) {
        name += entry.tags.join("");
    }
    return name;
}

function applyPlan(plan) {
    var applied = 0;
    for (var i = 0; i < plan.entries.length; i++) {
        var entry = plan.entries[i];
        if (!entry.resolved) {
            continue;
        }
        if (entry.newName === entry.oldName) {
            continue;
        }
        entry.layer.name = entry.newName;
        applied++;
    }
    return applied;
}

// -- preview -----------------------------------------------------------------

function showPreview(plan) {
    var win = new Window("dialog", "Layer Auto Namer - preview");
    win.orientation = "column";
    win.alignChildren = "fill";
    win.preferredSize.width = 720;

    win.add(
        "statictext",
        undefined,
        "Rename " + plan.renameCount + " layer(s). " +
        plan.skipped + " skipped (unresolved terms). Nothing is written until you press Apply."
    );

    var list = win.add("listbox", undefined, [], {
        numberOfColumns: 3,
        showHeaders: true,
        columnTitles: ["Layer", "New name", "Status"],
        columnWidths: [260, 320, 110]
    });
    list.preferredSize.height = 420;

    for (var i = 0; i < plan.entries.length; i++) {
        var entry = plan.entries[i];
        var status;
        if (!entry.resolved) {
            status = "SKIP";
        } else if (entry.newName === entry.oldName) {
            status = "unchanged";
        } else {
            status = "rename";
        }
        var row = list.add("item", entry.oldName);
        row.subItems[0].text = entry.resolved ? entry.newName : "(left as is)";
        row.subItems[1].text = status;
    }

    var buttons = win.add("group");
    buttons.alignment = "right";
    var cancel = buttons.add("button", undefined, "Cancel", { name: "cancel" });
    var apply = buttons.add("button", undefined, "Apply", { name: "ok" });

    var approved = false;
    apply.onClick = function () { approved = true; win.close(); };
    cancel.onClick = function () { approved = false; win.close(); };

    win.show();
    return approved;
}

// -- name parts --------------------------------------------------------------

function readControlPrefix(name) {
    if (startsWith(name, "IGNORE_")) {
        return "IGNORE_";
    }
    if (startsWith(name, "REF_")) {
        return "REF_";
    }
    return "";
}

// Every bracket tag is preserved verbatim. Deliberately NOT a copy of the
// exporter's KNOWN_BRACKET_TAG_PATTERNS list: a second registry would drift out of
// sync the next time a tag is added there.
function extractBracketTags(name) {
    var tags = [];
    var pattern = /\[[^\]]*\]/g;
    var match;
    while ((match = pattern.exec(String(name))) !== null) {
        tags.push(match[0]);
    }
    return tags;
}

function removeBracketTags(name) {
    return String(name).replace(/\[[^\]]*\]/g, " ");
}

function stripLegacyPrefix(name) {
    var text = String(name);
    var prefixes = ["IMG_", "BTN_", "TXT_", "SKIN_", "GROUP_", "TXTIMG_"];
    for (var i = 0; i < prefixes.length; i++) {
        if (startsWith(text, prefixes[i])) {
            return text.substring(prefixes[i].length);
        }
    }
    return text;
}

// Drops a family/variant index this script produced on an earlier run, so re-running
// is idempotent instead of stacking suffixes (frame01 -> frame0101 -> frame010101).
function stripGeneratedIndex(name) {
    return String(name).replace(/(\d{2})(_\d+)?\s*$/, "");
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

// -- helpers -----------------------------------------------------------------

function isAsciiWordChar(ch) {
    return /[A-Za-z0-9]/.test(ch);
}

function isSeparator(ch) {
    return /[\s_\-.,/\\()]/.test(ch);
}

function isTextLayer(layer) {
    try {
        return layer.kind === LayerKind.TEXT;
    } catch (e) {
        return false;
    }
}

function startsWith(text, prefix) {
    return String(text).indexOf(prefix) === 0;
}

function trim(text) {
    return String(text).replace(/^\s+|\s+$/g, "");
}

function pad2(value) {
    var text = String(value);
    return text.length >= 2 ? text : "0" + text;
}

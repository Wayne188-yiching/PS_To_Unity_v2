#target photoshop

var SCRIPT_VERSION = "2.7.2";
var GITHUB_JSX_RAW_URL = "https://raw.githubusercontent.com/Wayne188-yiching/PS_To_Unity_v2/main/PhotoshopExporter/PhotoshopUiPackageExporter.jsx";

(function () {
    if (!app.documents.length) {
        alert("Open a PSD document before running Photoshop UI Package Exporter.");
        return;
    }

    var sourceDoc = app.activeDocument;
    var options = showExportDialog(sourceDoc);

    if (!options) {
        return;
    }

    try {
        var result = exportUiPackage(sourceDoc, options);
        alert(
            "UI Package export complete.\n\n" +
            "Layers exported: " + result.imageCount + "\n" +
            "Text nodes: " + result.textCount + "\n" +
            "Groups: " + result.groupCount + "\n\n" +
            "Skipped empty/unsupported layers: " + result.skippedCount + "\n" +
            "Skipped hidden layers: " + result.skipHidden + "\n" +
            "Skipped IGNORE/REF layers: " + result.skipIgnoreRef + "\n" +
            "Skipped adjustment layers: " + result.skipAdjustment + "\n" +
            "Skipped clipping layers: " + result.skipClipping + "\n" +
            "Unchanged PNGs skipped: " + result.unchangedCount + "\n\n" +
            "PNG folder:\n" + result.imageFolder.fsName + "\n\n" +
            "Layout JSON:\n" + result.layoutJsonFile.fsName
        );
    } catch (e) {
        alert("UI Package export failed.\n\nError: " + e.message);
    }
})();

// ---------------------------------------------------------------------------
//  Self-update helpers
// ---------------------------------------------------------------------------

// Returns true only when a new version was downloaded and installed.
function checkAndUpdateScript() {
    var selfPath = $.fileName;
    if (!selfPath) {
        alert("Cannot determine script file location.\nMake sure the script is saved to disk before updating.");
        return false;
    }

    var tempPath = Folder.temp.fsName + "/.pstu_update_" + (new Date().getTime()) + ".jsx";
    var downloaded = downloadUrlToFile(GITHUB_JSX_RAW_URL, tempPath);

    if (!downloaded) {
        alert(
            "Update failed: could not connect to GitHub.\n" +
            "Please check your internet connection or visit:\n" +
            "https://github.com/Wayne188-yiching/PS_To_Unity_v2"
        );
        return false;
    }

    var tempFile = new File(tempPath);
    if (!tempFile.exists) {
        alert("Update failed: download did not complete.");
        return false;
    }

    tempFile.encoding = "UTF-8";
    tempFile.open("r");
    var newContent = tempFile.read();
    tempFile.close();
    tempFile.remove();

    if (!newContent || newContent.length < 100) {
        alert("Update failed: downloaded file appears empty or corrupt.");
        return false;
    }

    var vMatch = newContent.match(/SCRIPT_VERSION\s*=\s*["']([^"']+)["']/);
    var remoteVersion = vMatch ? vMatch[1] : "unknown";
    var cmp = compareSemver(remoteVersion, SCRIPT_VERSION);

    if (cmp === 0) {
        alert("You already have the latest version (v" + SCRIPT_VERSION + ").");
        return false;
    }

    if (cmp < 0) {
        // Local is newer than what's published on GitHub (typical during local
        // development before a push). Do not offer to overwrite - that would
        // silently downgrade unreleased work.
        alert(
            "Local version is newer than GitHub.\n\n" +
            "Local:  v" + SCRIPT_VERSION + "\n" +
            "GitHub: v" + remoteVersion + "\n\n" +
            "Nothing to do."
        );
        return false;
    }

    var msg =
        "A new version is available.\n\n" +
        "GitHub: v" + remoteVersion + "\n" +
        "Local:  v" + SCRIPT_VERSION + "\n\n" +
        "Update and overwrite the current script file?";
    if (!confirm(msg)) {
        return false;
    }

    var selfFile = new File(selfPath);
    selfFile.encoding = "UTF-8";
    if (selfFile.exists) {
        selfFile.remove();
    }
    selfFile.open("w", "TEXT");
    selfFile.lineFeed = "\n";
    selfFile.write(newContent);
    selfFile.close();

    alert("Update complete!\nRe-run the script to use version v" + remoteVersion + ".");
    return true;
}

// Returns -1 if a < b, 0 if equal, 1 if a > b. Treats non-numeric / missing
// segments as 0 so "2.6" and "2.6.0" compare equal. Unknown versions
// (parse failure) compare as 0 - caller treats that as "no update needed".
function compareSemver(a, b) {
    if (a === b) return 0;
    var as = String(a || "0").split(".");
    var bs = String(b || "0").split(".");
    var len = Math.max(as.length, bs.length);
    for (var i = 0; i < len; i++) {
        var av = parseInt(as[i], 10);
        var bv = parseInt(bs[i], 10);
        if (isNaN(av)) av = 0;
        if (isNaN(bv)) bv = 0;
        if (av < bv) return -1;
        if (av > bv) return 1;
    }
    return 0;
}

function downloadUrlToFile(url, destPath) {
    try {
        var isWin = $.os.toLowerCase().indexOf("windows") >= 0;
        if (isWin) {
            var destPathWin = destPath.replace(/\//g, "\\");
            var psCmd =
                "powershell.exe -WindowStyle Hidden -ExecutionPolicy Bypass -Command " +
                "\"(New-Object System.Net.WebClient).DownloadFile('" + url + "', '" + destPathWin + "')\"";
            app.system(psCmd);
        } else {
            app.system("curl -L -s -o '" + destPath + "' '" + url + "'");
        }
        return (new File(destPath)).exists;
    } catch (e) {
        return false;
    }
}

// ---------------------------------------------------------------------------

function showExportDialog(doc) {
    var dialog = new Window("dialog", "Photoshop UI Package Exporter");
    dialog.orientation = "column";
    dialog.alignChildren = "fill";

    // Header: version label + update button
    var headerGroup = dialog.add("group");
    headerGroup.orientation = "row";
    headerGroup.alignment = "fill";
    headerGroup.alignChildren = ["fill", "center"];
    var versionLabel = headerGroup.add("statictext", undefined, "v" + SCRIPT_VERSION);
    versionLabel.justify = "left";
    var updateButton = headerGroup.add("button", undefined, "Update Exporter Only");
    updateButton.alignment = "right";
    updateButton.onClick = function () {
        // U2: run the update check inside the dialog so configured fields survive.
        // Only close when a new version was actually installed (a re-run is required to load it).
        var updated = checkAndUpdateScript();
        if (updated) {
            dialog.close(0);
        }
    };

    var intro = dialog.add("statictext", undefined, "Export non-text layer PNGs and a named layout JSON for Unity. Text layers stay as TMP nodes.");
    intro.characters = 82;

    var defaultImageOutput = defaultImageFolder(doc);
    var defaultLayoutOutput = defaultLayoutJsonFile(doc, defaultImageOutput);
    var layoutPathTouched = false;

    var imageFolderGroup = dialog.add("group");
    imageFolderGroup.orientation = "row";
    imageFolderGroup.alignChildren = ["fill", "center"];
    imageFolderGroup.add("statictext", undefined, "PNG output folder:");
    var imageFolderText = imageFolderGroup.add("edittext", undefined, defaultImageOutput.fsName);
    imageFolderText.characters = 56;
    var browseImageButton = imageFolderGroup.add("button", undefined, "Browse...");

    var layoutGroup = dialog.add("group");
    layoutGroup.orientation = "row";
    layoutGroup.alignChildren = ["fill", "center"];
    layoutGroup.add("statictext", undefined, "Layout JSON file:");
    var layoutText = layoutGroup.add("edittext", undefined, defaultLayoutOutput.fsName);
    layoutText.characters = 56;
    var browseLayoutButton = layoutGroup.add("button", undefined, "Browse...");

    var unityPanel = dialog.add("panel", undefined, "Unity Atlas output");
    unityPanel.orientation = "column";
    unityPanel.alignChildren = "left";

    var useUnityAtlas = unityPanel.add("checkbox", undefined, "Output PNGs to Unity Atlas/SpriteAtlas language folder");
    useUnityAtlas.value = false;

    var languageGroup = unityPanel.add("group");
    languageGroup.orientation = "row";
    languageGroup.alignChildren = ["left", "center"];
    languageGroup.add("statictext", undefined, "Language folder:");
    var languageList = languageGroup.add("dropdownlist", undefined, ["Base", "CHS", "CHT", "EN"]);
    languageList.selection = 0;
    languageList.enabled = false;

    var unityNote = unityPanel.add("statictext", undefined, "When enabled, PNG output folder is treated as the Unity package root. PNGs go to Atlas/SpriteAtlas/{Language}.");
    unityNote.characters = 82;

    useUnityAtlas.onClick = function () {
        languageList.enabled = useUnityAtlas.value;
    };

    imageFolderText.onChange = function () {
        if (!layoutPathTouched && trim(imageFolderText.text)) {
            layoutText.text = defaultLayoutJsonFile(doc, new Folder(trim(imageFolderText.text))).fsName;
        }
    };

    layoutText.onChanging = function () {
        layoutPathTouched = true;
    };

    layoutText.onChange = function () {
        layoutPathTouched = true;
        layoutText.text = ensureJsonExtension(trim(layoutText.text));
    };

    browseImageButton.onClick = function () {
        var selected = Folder.selectDialog("Choose output folder for PNG files");
        if (selected) {
            imageFolderText.text = selected.fsName;
            if (!layoutPathTouched) {
                layoutText.text = defaultLayoutJsonFile(doc, selected).fsName;
            }
        }
    };

    browseLayoutButton.onClick = function () {
        var selected = File.saveDialog("Choose Layout JSON file", "JSON:*.json");
        if (selected) {
            layoutPathTouched = true;
            layoutText.text = ensureJsonExtension(selected.fsName);
        }
    };

    var optionPanel = dialog.add("panel", undefined, "Export options");
    optionPanel.orientation = "column";
    optionPanel.alignChildren = "left";

    var ignoreHidden = optionPanel.add("checkbox", undefined, "Ignore hidden / closed layers");
    ignoreHidden.value = true;

    var skipReference = optionPanel.add("checkbox", undefined, "Skip IGNORE_ and REF_ layers");
    skipReference.value = true;

    var useExportCache = optionPanel.add("checkbox", undefined, "Use export cache to skip unchanged PNGs");
    useExportCache.value = true;

    var useFastLayerDuplicate = optionPanel.add("checkbox", undefined, "Use fast layer duplicate export when possible");
    useFastLayerDuplicate.value = true;

    var textOutputGroup = optionPanel.add("group");
    textOutputGroup.orientation = "row";
    textOutputGroup.alignChildren = ["left", "center"];
    textOutputGroup.add("statictext", undefined, "Default text layers:");
    var textOutputList = textOutputGroup.add("dropdownlist", undefined, ["TMP text nodes", "PNG images"]);
    textOutputList.selection = 0;

    var selectedTextAsImage = optionPanel.add("checkbox", undefined, "Export currently selected text layers as PNG overrides");
    selectedTextAsImage.value = false;

    var autoRouteNonSourceHanFonts = optionPanel.add("checkbox", undefined, "Auto-export non-Source-Han fonts as PNG");
    autoRouteNonSourceHanFonts.value = true;

    var note = optionPanel.add("statictext", undefined, "Select text layers in Photoshop before export to bake only those as PNG. Layout JSON can stay beside the PNG output folder.");
    note.characters = 82;

    var buttons = dialog.add("group");
    buttons.orientation = "row";
    buttons.alignment = "right";
    var cancelButton = buttons.add("button", undefined, "Cancel", { name: "cancel" });
    var exportButton = buttons.add("button", undefined, "Export UI Package", { name: "ok" });

    cancelButton.onClick = function () {
        dialog.close(0);
    };

    exportButton.onClick = function () {
        var imageFolderValue = trim(imageFolderText.text);
        var layoutJsonValue = ensureJsonExtension(trim(layoutText.text));

        if (!imageFolderValue) {
            alert("Choose a PNG output folder.");
            return;
        }

        if (!layoutJsonValue) {
            alert("Choose a Layout JSON file.");
            return;
        }

        var imageFolder = resolveImageOutputFolder({
            imageFolder: imageFolderValue,
            useUnityAtlasStructure: useUnityAtlas.value,
            atlasLanguage: languageList.selection ? languageList.selection.text : "Base"
        });
        var layoutJsonFile = new File(layoutJsonValue);

        if (isPathInsideFolder(layoutJsonFile, imageFolder) && !isSameFolder(layoutJsonFile.parent, imageFolder)) {
            alert("Layout JSON file must not be inside an image subfolder.");
            return;
        }

        dialog.result = {
            imageFolder: imageFolderValue,
            layoutJsonFile: layoutJsonValue,
            ignoreHiddenLayers: ignoreHidden.value,
            skipReferenceLayers: skipReference.value,
            useExportCache: useExportCache.value,
            useFastLayerDuplicate: useFastLayerDuplicate.value,
            useUnityAtlasStructure: useUnityAtlas.value,
            atlasLanguage: languageList.selection ? languageList.selection.text : "Base",
            textLayerOutput: textOutputList.selection && textOutputList.selection.index === 1 ? "image" : "tmp",
            selectedTextLayersAsImages: selectedTextAsImage.value,
            autoRouteNonSourceHanFonts: autoRouteNonSourceHanFonts.value
        };
        dialog.close(1);
    };

    var accepted = dialog.show();
    return accepted === 1 ? dialog.result : null;
}

function exportUiPackage(sourceDoc, options) {
    var originalDoc = app.activeDocument;
    var originalRulerUnits = app.preferences.rulerUnits;
    var originalDisplayDialogs = app.displayDialogs;

    var imageFolder = resolveImageOutputFolder(options);
    var layoutJsonFile = new File(ensureJsonExtension(options.layoutJsonFile));

    if (isPathInsideFolder(layoutJsonFile, imageFolder) && !isSameFolder(layoutJsonFile.parent, imageFolder)) {
        throw new Error("Layout JSON file must not be inside an image subfolder.");
    }

    ensureFolder(imageFolder);
    ensureFolder(layoutJsonFile.parent);

    // Direction 4: capture visibility state instead of duplicating the entire PSD
    var savedVisibility = null;

    try {
        app.preferences.rulerUnits = Units.PIXELS;
        app.displayDialogs = DialogModes.NO;
        app.activeDocument = sourceDoc;

        // Capture BEFORE any modifications so we can restore later
        savedVisibility = captureVisibility(sourceDoc);

        if (!options.ignoreHiddenLayers) {
            showAllLayers(sourceDoc);
        }

        var context = {
            doc: sourceDoc,
            imageFolder: imageFolder,
            ignoreHiddenLayers: options.ignoreHiddenLayers,
            skipReferenceLayers: options.skipReferenceLayers,
            useExportCache: options.useExportCache !== false,
            useFastLayerDuplicate: options.useFastLayerDuplicate !== false,
            textLayerOutput: options.textLayerOutput || "tmp",
            selectedTextLayerIds: options.selectedTextLayersAsImages ? readSelectedLayerIdMap(sourceDoc) : {},
            autoRouteNonSourceHanFonts: options.autoRouteNonSourceHanFonts !== false,
            sourceModified: readDocumentModified(sourceDoc),
            exportCache: null,
            exportCacheDirty: false,
            counters: {},
            imageCount: 0,
            textCount: 0,
            groupCount: 0,
            unchangedCount: 0,
            skippedCount: 0,
            skipHidden: 0,
            skipIgnoreRef: 0,
            skipAdjustment: 0,
            skipClipping: 0
        };
        context.exportCache = context.useExportCache ? loadExportCache(imageFolder) : null;

        var pendingImages = [];
        var canvasBounds = {
            left: 0,
            top: 0,
            width: px(sourceDoc.width),
            height: px(sourceDoc.height)
        };
        var nodes = collectNodes(sourceDoc, true, context, pendingImages, canvasBounds);

        exportAllImages(pendingImages, context);
        writeExportCacheIfDirty(context);
        refreshGroupBounds(nodes, context.doc);

        var layout = buildLayoutJson(sourceDoc, nodes);
        writeTextFile(layoutJsonFile, layout);

        return {
            imageFolder: imageFolder,
            layoutJsonFile: layoutJsonFile,
            imageCount: context.imageCount,
            textCount: context.textCount,
            groupCount: context.groupCount,
            unchangedCount: context.unchangedCount,
            skippedCount: context.skippedCount,
            skipHidden: context.skipHidden,
            skipIgnoreRef: context.skipIgnoreRef,
            skipAdjustment: context.skipAdjustment,
            skipClipping: context.skipClipping
        };
    } finally {
        // Direction 4: restore original visibility instead of closing a workDoc copy
        if (savedVisibility) {
            restoreVisibility(savedVisibility);
        }
        app.displayDialogs = originalDisplayDialogs;
        app.preferences.rulerUnits = originalRulerUnits;
        app.activeDocument = originalDoc;
    }
}

function collectNodes(container, parentVisible, context, pendingImages, parentBounds) {
    var nodes = [];

    for (var i = container.layers.length - 1; i >= 0; i--) {
        var layer = container.layers[i];
        var effectiveVisible = parentVisible && isVisible(layer);

        if (context.ignoreHiddenLayers && !effectiveVisible) {
            context.skipHidden++;
            continue;
        }

        if (context.skipReferenceLayers && isReferenceOrIgnored(layer.name)) {
            context.skipIgnoreRef++;
            continue;
        }

        if (layer.typename === "LayerSet") {
            var groupBounds = readLayerBounds(layer);
            if (groupBounds) {
                groupBounds = clampBoundsToCanvas(groupBounds, context.doc);
            }

            var childParentBounds = groupBounds || parentBounds;
            var childNodes = collectNodes(layer, effectiveVisible, context, pendingImages, childParentBounds);
            if (childNodes.length > 0) {
                var groupNode = createGroupNode(layer, childNodes, context, parentBounds, groupBounds);
                if (groupNode) {
                    nodes.push(groupNode);
                    context.groupCount++;
                } else {
                    appendNodes(nodes, childNodes);
                }
            }
            continue;
        }

        if (layer.typename !== "ArtLayer") {
            continue;
        }

        if (isTextLayer(layer)) {
            if (shouldExportTextLayerAsImage(layer, context)) {
                if (trackSkippedImageReason(layer, context)) {
                    context.skippedCount++;
                    continue;
                }
                var textImageNode = createImageNode(layer, context, parentBounds);
                if (textImageNode) {
                    pendingImages.push({ layer: layer, node: textImageNode });
                    nodes.push(textImageNode);
                } else {
                    context.skippedCount++;
                }
                continue;
            }

            var textNode = createTextNode(layer, context, parentBounds);
            if (textNode) {
                nodes.push(textNode);
                context.textCount++;
            } else {
                context.skippedCount++;
            }
            continue;
        }

        if (trackSkippedImageReason(layer, context)) {
            context.skippedCount++;
            continue;
        }

        var imageNode = createImageNode(layer, context, parentBounds);
        if (imageNode) {
            pendingImages.push({ layer: layer, node: imageNode });
            nodes.push(imageNode);
        } else {
            context.skippedCount++;
        }
    }

    return nodes;
}

function appendNodes(target, source) {
    for (var i = 0; i < source.length; i++) {
        target.push(source[i]);
    }
}

function createGroupNode(layerSet, children, context, parentBounds, bounds) {
    var layoutType = detectLayoutGroupType(layerSet, children);
    // Auto-dedup disabled in v2.4.2: same width x height does not imply same content.
    // Function dedupeLayoutGroupImages retained for potential opt-in via layer tag in the future.

    if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
        bounds = boundsFromChildren(children, context.doc);
    }

    if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
        return null;
    }

    var node = {
        name: uniqueNodeName(layoutType ? stripLayoutGroupTags(layerSet.name) : layerSet.name, context.counters),
        type: "group",
        x: bounds.left,
        y: bounds.top,
        width: bounds.width,
        height: bounds.height,
        visible: true,
        children: children
    };

    applyLayoutMetadata(node, bounds, parentBounds, layerSet.name);
    applyLayoutGroupMetadata(node, layerSet, children, bounds);
    node._parentBounds = parentBounds;
    node._rawName = layerSet.name;
    return node;
}

function refreshGroupBounds(nodes, doc) {
    for (var i = 0; i < nodes.length; i++) {
        var node = nodes[i];
        if (!node || node.type !== "group") {
            continue;
        }

        refreshGroupBounds(node.children || [], doc);
        var bounds = boundsFromChildren(node.children || [], doc);
        if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
            continue;
        }

        node.x = bounds.left;
        node.y = bounds.top;
        node.width = bounds.width;
        node.height = bounds.height;
        applyLayoutMetadata(node, bounds, node._parentBounds, node._rawName);
        applyLayoutGroupMetadata(node, node._rawName, node.children || [], bounds);
    }
}

function boundsFromChildren(children, doc) {
    if (!children || children.length === 0) {
        return null;
    }

    var left = Number.POSITIVE_INFINITY;
    var top = Number.POSITIVE_INFINITY;
    var right = Number.NEGATIVE_INFINITY;
    var bottom = Number.NEGATIVE_INFINITY;

    for (var i = 0; i < children.length; i++) {
        var child = children[i];
        if (!child) {
            continue;
        }

        left = Math.min(left, child.x);
        top = Math.min(top, child.y);
        right = Math.max(right, child.x + child.width);
        bottom = Math.max(bottom, child.y + child.height);
    }

    if (!isFinite(left) || !isFinite(top) || !isFinite(right) || !isFinite(bottom)) {
        return null;
    }

    return clampBoundsToCanvas({
        left: left,
        top: top,
        right: right,
        bottom: bottom,
        width: Math.max(0, right - left),
        height: Math.max(0, bottom - top)
    }, doc);
}

function createImageNode(layer, context, parentBounds) {
    if (isClippingLayer(layer) || isAdjustmentLikeLayer(layer)) {
        return null;
    }

    var bounds = readLayerBounds(layer);
    if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
        return null;
    }

    bounds = clampBoundsToCanvas(bounds, context.doc);
    if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
        return null;
    }

    var safeName = uniqueFileName(layer.name, context.counters);
    var fileName = safeName + ".png";

    var node = {
        name: safeName,
        type: "image",
        x: bounds.left,
        y: bounds.top,
        width: bounds.width,
        height: bounds.height,
        visible: true,
        imagePath: fileName,
        children: []
    };

    var noEffectsBounds = readLayerBoundsNoEffects(layer);
    node._exportBounds = bounds;
    node._noEffectsBounds = noEffectsBounds;
    node._padding = calculateShadowCompensation(bounds, noEffectsBounds);
    node._parentBounds = parentBounds;
    node._rawName = layer.name;

    applyLayoutMetadata(node, bounds, parentBounds, layer.name);
    return node;
}

function exportAllImages(pendingImages, context) {
    if (pendingImages.length === 0) {
        return;
    }

    var fallbackVisibilityPrepared = false;
    if (!context.useFastLayerDuplicate) {
        hideAllLayers(context.doc);
        fallbackVisibilityPrepared = true;
    }

    // Fix bug2: limit history states to 1 to prevent RAM accumulation across iterations
    var savedHistoryStates = app.preferences.historyStates;
    app.preferences.historyStates = 1;

    // Direction 1: create exportDoc once, reuse for all images
    var exportDoc = null;
    var visibleChain = [];
    try {
        for (var i = 0; i < pendingImages.length; i++) {
            var entry = pendingImages[i];
            if (entry.node && entry.node._skipExport) {
                continue;
            }
            var file = new File(context.imageFolder.fsName + "/" + entry.node.imagePath);
            ensureFolder(file.parent);
            if (trySkipCachedImage(entry, context, file)) {
                context.unchangedCount++;
                continue;
            }

            if (!exportDoc) {
                exportDoc = app.documents.add(
                    entry.node.width, entry.node.height,
                    context.doc.resolution, "ui_export_work",
                    NewDocumentMode.RGB, DocumentFill.TRANSPARENT
                );
            }

            var result;
            if (context.useFastLayerDuplicate) {
                result = exportNodeImageFastDuplicate(entry.layer, entry.node, context, exportDoc, file);
            } else {
                visibleChain = showOnlyLayerChain(entry.layer, visibleChain);
                result = exportNodeImageReuse(entry.layer, entry.node, context, exportDoc, file);
            }
            if (result === false && context.useFastLayerDuplicate) {
                if (!fallbackVisibilityPrepared) {
                    hideAllLayers(context.doc);
                    fallbackVisibilityPrepared = true;
                    visibleChain = [];
                }
                visibleChain = showOnlyLayerChain(entry.layer, visibleChain);
                result = exportNodeImageReuse(entry.layer, entry.node, context, exportDoc, file);
            }
            if (result === "saved") {
                context.imageCount++;
                updateExportCacheRecord(entry, context, file);
            } else if (result === "unchanged") {
                context.unchangedCount++;
                updateExportCacheRecord(entry, context, file);
            } else {
                context.skippedCount++;
            }
        }
    } finally {
        hideVisibleChain(visibleChain);
        if (exportDoc) {
            app.activeDocument = exportDoc;
            exportDoc.close(SaveOptions.DONOTSAVECHANGES);
        }
        app.activeDocument = context.doc;
        app.preferences.historyStates = savedHistoryStates;
    }
}

function exportNodeImageFastDuplicate(layer, node, context, exportDoc, file) {
    var duplicatedLayer = null;
    var saved = false;
    var originalNodeState = captureNodeExportState(node);

    try {
        app.activeDocument = exportDoc;
        resetExportDocument(exportDoc, node.width, node.height);

        app.activeDocument = context.doc;
        duplicatedLayer = layer.duplicate(exportDoc, ElementPlacement.PLACEATBEGINNING);

        app.activeDocument = exportDoc;
        exportDoc.activeLayer = duplicatedLayer;
        duplicatedLayer.visible = true;
        alignActiveLayerToExportOrigin(exportDoc);

        if (!trimTransparentPixelsAndApplyPadding(exportDoc, node)) {
            saved = false;
        } else {
            saved = savePngIfChanged(exportDoc, file);
        }
    } catch (e) {
        saved = false;
    } finally {
        if (saved === false) {
            restoreNodeExportState(node, originalNodeState);
        }
        app.activeDocument = exportDoc;
        try {
            if (duplicatedLayer) {
                duplicatedLayer.remove();
            }
        } catch (ignored) {
        }
        app.activeDocument = context.doc;
    }

    return saved;
}

function alignActiveLayerToExportOrigin(doc) {
    var bounds = readLayerBounds(doc.activeLayer);
    if (!bounds) {
        return;
    }

    if (bounds.left !== 0 || bounds.top !== 0) {
        doc.activeLayer.translate(-bounds.left, -bounds.top);
    }
}

function captureNodeExportState(node) {
    return {
        x: node.x,
        y: node.y,
        width: node.width,
        height: node.height,
        anchorPreset: node.anchorPreset,
        anchorMin: node.anchorMin,
        anchorMax: node.anchorMax,
        pivot: node.pivot
    };
}

function restoreNodeExportState(node, state) {
    node.x = state.x;
    node.y = state.y;
    node.width = state.width;
    node.height = state.height;
    node.anchorPreset = state.anchorPreset;
    node.anchorMin = state.anchorMin;
    node.anchorMax = state.anchorMax;
    node.pivot = state.pivot;
}

function resetExportDocument(doc, width, height) {
    app.activeDocument = doc;
    if (Math.round(px(doc.width)) !== width || Math.round(px(doc.height)) !== height) {
        doc.resizeCanvas(width, height, AnchorPosition.TOPLEFT);
    }

    try {
        doc.selection.selectAll();
        doc.selection.clear();
        doc.selection.deselect();
    } catch (e) {
        try {
            doc.selection.deselect();
        } catch (ignored) {
        }
    }
}

function exportNodeImageReuse(layer, node, context, exportDoc, file) {
    app.activeDocument = context.doc;
    context.doc.selection.deselect();
    try {
        context.doc.selection.select([
            [node.x, node.y],
            [node.x + node.width, node.y],
            [node.x + node.width, node.y + node.height],
            [node.x, node.y + node.height]
        ]);
        context.doc.selection.copy(true);
        context.doc.selection.deselect();
    } catch (copyError) {
        try {
            context.doc.selection.deselect();
        } catch (ignored) {
        }
        return false;
    }

    var saved = false;
    try {
        app.activeDocument = exportDoc;
        // Direction 1: resize the reused canvas to this node's dimensions
        if (Math.round(px(exportDoc.width)) !== node.width || Math.round(px(exportDoc.height)) !== node.height) {
            exportDoc.resizeCanvas(node.width, node.height, AnchorPosition.TOPLEFT);
        }
        exportDoc.selection.selectAll();
        exportDoc.selection.clear();
        exportDoc.selection.deselect();
        exportDoc.paste();
        exportDoc.selection.deselect(); // Fix bug1: anchor the floating selection before next resizeCanvas
        if (!trimTransparentPixelsAndApplyPadding(exportDoc, node)) {
            saved = false;
        } else {
            saved = savePngIfChanged(exportDoc, file);
        }
    } catch (saveError) {
        saved = false;
    } finally {
        app.activeDocument = context.doc;
    }

    return saved;
}

function trimTransparentPixelsAndApplyPadding(doc, node) {
    var contentBounds = readLayerBounds(doc.activeLayer);
    if (!contentBounds || contentBounds.width <= 0 || contentBounds.height <= 0) {
        return false;
    }

    var padding = node._padding || zeroPadding();
    var padLeft = Math.max(0, Math.round(padding.left || 0));
    var padTop = Math.max(0, Math.round(padding.top || 0));
    var padRight = Math.max(0, Math.round(padding.right || 0));
    var padBottom = Math.max(0, Math.round(padding.bottom || 0));
    var finalWidth = Math.max(1, contentBounds.width + padLeft + padRight);
    var finalHeight = Math.max(1, contentBounds.height + padTop + padBottom);

    doc.crop([
        UnitValue(contentBounds.left, "px"),
        UnitValue(contentBounds.top, "px"),
        UnitValue(contentBounds.right, "px"),
        UnitValue(contentBounds.bottom, "px")
    ]);

    doc.resizeCanvas(finalWidth, finalHeight, AnchorPosition.TOPLEFT);
    if (padLeft > 0 || padTop > 0) {
        doc.activeLayer.translate(padLeft, padTop);
    }

    node.x = Math.round(node.x + contentBounds.left - padLeft);
    node.y = Math.round(node.y + contentBounds.top - padTop);
    node.width = finalWidth;
    node.height = finalHeight;
    applyLayoutMetadata(node, {
        left: node.x,
        top: node.y,
        right: node.x + node.width,
        bottom: node.y + node.height,
        width: node.width,
        height: node.height
    }, node._parentBounds, node._rawName);
    return true;
}

function clampBoundsToCanvas(bounds, doc) {
    var canvasWidth = px(doc.width);
    var canvasHeight = px(doc.height);
    var left = Math.max(0, bounds.left);
    var top = Math.max(0, bounds.top);
    var right = Math.min(canvasWidth, bounds.right);
    var bottom = Math.min(canvasHeight, bounds.bottom);

    if (right <= left || bottom <= top) {
        return null;
    }

    return {
        left: left,
        top: top,
        right: right,
        bottom: bottom,
        width: Math.max(0, right - left),
        height: Math.max(0, bottom - top)
    };
}

function applyLayoutMetadata(node, bounds, parentBounds, rawName) {
    var layout = parseLayoutTag(rawName);
    if (!layout) {
        layout = inferLayout(bounds, parentBounds);
    }

    node.anchorPreset = layout.preset;
    node.anchorMin = layout.anchorMin;
    node.anchorMax = layout.anchorMax;
    node.pivot = layout.pivot;
}

function applyLayoutGroupMetadata(node, group, children, bounds) {
    var layoutType = detectLayoutGroupType(group, children);
    if (!layoutType) {
        return;
    }

    var padding = calcLayoutPadding(bounds, children);
    node.layoutType = layoutType;
    node.layoutSpacing = calcLayoutSpacing(layoutType, children);
    node.layoutPaddingLeft = padding.padLeft;
    node.layoutPaddingRight = padding.padRight;
    node.layoutPaddingTop = padding.padTop;
    node.layoutPaddingBottom = padding.padBottom;
    node.contentSizeFitter = true;
    // Auto-dedup disabled in v2.4.2 to avoid wrongly merging same-size but different-content images.
    node.children = children;
}

function detectLayoutGroupType(group, children) {
    var rawName = typeof group === "string" ? group : (group && group.name);
    var name = String(rawName || "");

    if (/\[(?:H|HLAYOUT)\]/i.test(name)) {
        return "horizontal";
    }
    if (/\[(?:V|VLAYOUT)\]/i.test(name)) {
        return "vertical";
    }

    var visibleChildren = layoutVisibleChildren(children);
    if (visibleChildren.length < 2) {
        return null;
    }

    var sameY = true;
    var sameX = true;
    var baseY = visibleChildren[0].y;
    var baseX = visibleChildren[0].x;
    for (var i = 1; i < visibleChildren.length; i++) {
        if (Math.abs(visibleChildren[i].y - baseY) > 3) {
            sameY = false;
        }
        if (Math.abs(visibleChildren[i].x - baseX) > 3) {
            sameX = false;
        }
    }

    if (sameY) {
        return "horizontal";
    }
    if (sameX) {
        return "vertical";
    }

    return null;
}

function calcLayoutSpacing(layoutType, children) {
    var items = layoutVisibleChildren(children);
    if (items.length < 2) {
        return 0;
    }

    items.sort(function (a, b) {
        return layoutType === "horizontal" ? a.x - b.x : a.y - b.y;
    });

    var gaps = [];
    for (var i = 0; i < items.length - 1; i++) {
        var current = items[i];
        var next = items[i + 1];
        var gap = layoutType === "horizontal" ? next.x - (current.x + current.width) : next.y - (current.y + current.height);
        gaps.push(Math.max(0, round2(gap)));
    }

    gaps.sort(function (a, b) {
        return a - b;
    });

    var middle = Math.floor(gaps.length / 2);
    if (gaps.length % 2 === 1) {
        return Math.max(0, round2(gaps[middle]));
    }
    return Math.max(0, round2((gaps[middle - 1] + gaps[middle]) / 2));
}

function calcLayoutPadding(bounds, children) {
    var items = layoutVisibleChildren(children);
    if (!bounds || items.length === 0) {
        return { padLeft: 0, padTop: 0, padRight: 0, padBottom: 0 };
    }

    var left = Number.POSITIVE_INFINITY;
    var top = Number.POSITIVE_INFINITY;
    var right = Number.NEGATIVE_INFINITY;
    var bottom = Number.NEGATIVE_INFINITY;

    for (var i = 0; i < items.length; i++) {
        left = Math.min(left, items[i].x);
        top = Math.min(top, items[i].y);
        right = Math.max(right, items[i].x + items[i].width);
        bottom = Math.max(bottom, items[i].y + items[i].height);
    }

    return {
        padLeft: Math.max(0, round2(left - bounds.left)),
        padTop: Math.max(0, round2(top - bounds.top)),
        padRight: Math.max(0, round2((bounds.left + bounds.width) - right)),
        padBottom: Math.max(0, round2((bounds.top + bounds.height) - bottom))
    };
}

function layoutVisibleChildren(children) {
    var items = [];
    if (!children) {
        return items;
    }

    for (var i = 0; i < children.length; i++) {
        var child = children[i];
        if (!child || child.visible === false || child._skipExport) {
            continue;
        }
        items.push(child);
    }
    return items;
}

function dedupeLayoutGroupImages(children) {
    // Keep all nodes. Mark repeated images to share imagePath and skip PNG export.
    var firstByKey = {};
    if (!children) {
        return [];
    }

    for (var i = 0; i < children.length; i++) {
        var child = children[i];
        if (child && child.type === "image" && child.width > 0 && child.height > 0) {
            var key = jsonNumber(child.width) + "x" + jsonNumber(child.height);
            var first = firstByKey[key];
            if (first) {
                child.imagePath = first.imagePath;
                child._skipExport = true;
            } else {
                firstByKey[key] = child;
            }
        }
    }

    return children;
}

// v2.6.3 hotfix: bbox-based auto inference produced wildly wrong anchors
// (e.g. a 331x246 layer inside a 2258-wide parent matched "auto_stretch_full"
// due to ancestor bounds reuse, and ".pivot != anchor" mismatches scattered
// positions on every Generate). Game UI mockups are overwhelmingly fixed
// position + fixed size, so the conservative default is "center fixed" for
// every layer. Designers opt into responsive behavior via PS layer naming
// tags (ANCHOR_TL / STRETCH_X / etc.) which parseLayoutTag still honors.
// parentBounds is unused here but kept in the signature for compatibility.
function inferLayout(bounds, parentBounds) {
    return fixedLayout("auto_center", 0.5, 0.5);
}

function localCenterRatio(start, size, parentSize) {
    if (!parentSize || parentSize <= 0) {
        return 0;
    }

    return (start + size * 0.5) / parentSize;
}

function parseLayoutTag(rawName) {
    var text = String(rawName || "").toUpperCase();

    if (hasAny(text, ["STRETCH_FULL", "STRETCH_BOTH", "ANCHOR_STRETCH_FULL"])) {
        return withPivot(stretchLayout("stretch_full", true, true, 0.5, 0.5), text);
    }

    if (hasAny(text, ["STRETCH_X", "STRETCH_HORIZONTAL", "ANCHOR_STRETCH_X"])) {
        return withPivot(stretchLayout("stretch_x", true, false, 0.5, readAnchorY(text, 1)), text);
    }

    if (hasAny(text, ["STRETCH_Y", "STRETCH_VERTICAL", "ANCHOR_STRETCH_Y"])) {
        return withPivot(stretchLayout("stretch_y", false, true, readAnchorX(text, 0), 0.5), text);
    }

    if (hasAny(text, ["ANCHOR_TOP_LEFT", "ANCHOR_TL"])) {
        return withPivot(fixedLayout("top_left", 0, 1), text);
    }
    if (hasAny(text, ["ANCHOR_TOP_RIGHT", "ANCHOR_TR"])) {
        return withPivot(fixedLayout("top_right", 1, 1), text);
    }
    if (hasAny(text, ["ANCHOR_BOTTOM_LEFT", "ANCHOR_BL"])) {
        return withPivot(fixedLayout("bottom_left", 0, 0), text);
    }
    if (hasAny(text, ["ANCHOR_BOTTOM_RIGHT", "ANCHOR_BR"])) {
        return withPivot(fixedLayout("bottom_right", 1, 0), text);
    }
    if (hasAny(text, ["ANCHOR_TOP", "ANCHOR_TC"])) {
        return withPivot(fixedLayout("top_center", 0.5, 1), text);
    }
    if (hasAny(text, ["ANCHOR_BOTTOM", "ANCHOR_BC"])) {
        return withPivot(fixedLayout("bottom_center", 0.5, 0), text);
    }
    if (hasAny(text, ["ANCHOR_LEFT", "ANCHOR_ML"])) {
        return withPivot(fixedLayout("middle_left", 0, 0.5), text);
    }
    if (hasAny(text, ["ANCHOR_RIGHT", "ANCHOR_MR"])) {
        return withPivot(fixedLayout("middle_right", 1, 0.5), text);
    }
    if (hasAny(text, ["ANCHOR_CENTER", "ANCHOR_MIDDLE", "ANCHOR_C"])) {
        return withPivot(fixedLayout("center", 0.5, 0.5), text);
    }

    return null;
}

function fixedLayout(preset, anchorX, anchorY) {
    var pivot = readPivotFromPreset(preset, anchorX, anchorY);
    return {
        preset: preset,
        anchorMin: vector2(anchorX, anchorY),
        anchorMax: vector2(anchorX, anchorY),
        pivot: pivot
    };
}

function withPivot(layout, text) {
    layout.pivot = readPivot(text, layout.pivot);
    return layout;
}

function readPivot(text, fallback) {
    if (hasAny(text, ["PIVOT_TOP_LEFT", "PIVOT_TL"])) {
        return vector2(0, 1);
    }
    if (hasAny(text, ["PIVOT_TOP_RIGHT", "PIVOT_TR"])) {
        return vector2(1, 1);
    }
    if (hasAny(text, ["PIVOT_BOTTOM_LEFT", "PIVOT_BL"])) {
        return vector2(0, 0);
    }
    if (hasAny(text, ["PIVOT_BOTTOM_RIGHT", "PIVOT_BR"])) {
        return vector2(1, 0);
    }
    if (hasAny(text, ["PIVOT_TOP", "PIVOT_TC"])) {
        return vector2(0.5, 1);
    }
    if (hasAny(text, ["PIVOT_BOTTOM", "PIVOT_BC"])) {
        return vector2(0.5, 0);
    }
    if (hasAny(text, ["PIVOT_LEFT", "PIVOT_ML"])) {
        return vector2(0, 0.5);
    }
    if (hasAny(text, ["PIVOT_RIGHT", "PIVOT_MR"])) {
        return vector2(1, 0.5);
    }
    if (hasAny(text, ["PIVOT_CENTER", "PIVOT_MIDDLE", "PIVOT_C"])) {
        return vector2(0.5, 0.5);
    }
    return fallback;
}

function stretchLayout(preset, stretchX, stretchY, anchorX, anchorY) {
    return {
        preset: preset,
        anchorMin: vector2(stretchX ? 0 : anchorX, stretchY ? 0 : anchorY),
        anchorMax: vector2(stretchX ? 1 : anchorX, stretchY ? 1 : anchorY),
        pivot: vector2(stretchX ? 0.5 : anchorX, stretchY ? 0.5 : anchorY)
    };
}

function readPivotFromPreset(preset, anchorX, anchorY) {
    return vector2(anchorX, anchorY);
}

function readAnchorX(text, fallback) {
    if (hasAny(text, ["LEFT", "_L"])) {
        return 0;
    }
    if (hasAny(text, ["RIGHT", "_R"])) {
        return 1;
    }
    if (hasAny(text, ["CENTER", "MIDDLE", "_C"])) {
        return 0.5;
    }
    return fallback;
}

function readAnchorY(text, fallback) {
    if (hasAny(text, ["TOP", "_T"])) {
        return 1;
    }
    if (hasAny(text, ["BOTTOM", "_B"])) {
        return 0;
    }
    if (hasAny(text, ["CENTER", "MIDDLE", "_C"])) {
        return 0.5;
    }
    return fallback;
}

function anchorName(anchorX, anchorY) {
    var vertical = anchorY === 1 ? "top" : (anchorY === 0 ? "bottom" : "middle");
    var horizontal = anchorX === 0 ? "left" : (anchorX === 1 ? "right" : "center");
    if (vertical === "middle" && horizontal === "center") {
        return "center";
    }
    return vertical + "_" + horizontal;
}

function vector2(x, y) {
    return {
        x: round2(x),
        y: round2(y)
    };
}

function hasAny(text, tokens) {
    for (var i = 0; i < tokens.length; i++) {
        if (text.indexOf(tokens[i]) >= 0) {
            return true;
        }
    }
    return false;
}

function isClippingLayer(layer) {
    try {
        return !!layer.grouped;
    } catch (e) {
        return false;
    }
}

function isAdjustmentLikeLayer(layer) {
    try {
        var adjustmentKinds = [
            "BLACKANDWHITE",
            "BRIGHTNESSCONTRAST",
            "CHANNELMIXER",
            "COLORBALANCE",
            "COLORLOOKUP",
            "CURVES",
            "EXPOSURE",
            "GRADIENTMAP",
            "HUESATURATION",
            "INVERSION",
            "LEVELS",
            "PHOTOFILTER",
            "POSTERIZE",
            "SELECTIVECOLOR",
            "THRESHOLD",
            "VIBRANCE"
        ];

        for (var i = 0; i < adjustmentKinds.length; i++) {
            var key = adjustmentKinds[i];
            if (typeof LayerKind[key] !== "undefined" && layer.kind === LayerKind[key]) {
                return true;
            }
        }

        return false;
    } catch (e) {
        return false;
    }
}

function trackSkippedImageReason(layer, context) {
    if (isClippingLayer(layer)) {
        context.skipClipping++;
        return true;
    }
    if (isAdjustmentLikeLayer(layer)) {
        context.skipAdjustment++;
        return true;
    }
    return false;
}

function createTextNode(layer, context, parentBounds) {
    var bounds = readLayerBounds(layer);
    if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
        return null;
    }

    var textStyle = readTextLayerStyle(layer);
    var manualMaterialToken = readMaterialToken(layer);
    var fakeThickness = readFakeThickness(layer.name);

    var node = {
        name: uniqueNodeName(layer.name, context.counters),
        type: "text",
        x: bounds.left,
        y: bounds.top,
        width: bounds.width,
        height: bounds.height,
        visible: true,
        text: readText(layer),
        fontToken: normalizeAsciiSlug(readFontName(layer)),
        materialToken: manualMaterialToken || buildTextMaterialToken(textStyle),
        fontSize: readFontSize(layer),
        characterSpacing: readTextCharacterSpacing(layer),
        lineSpacing: readTextLineSpacing(layer),
        color: textStyle.fillColor || readTextColor(layer),
        outlineColor: textStyle.outlineColor,
        outlineWidth: textStyle.outlineWidth,
        outlineOpacity: textStyle.outlineOpacity,
        alignment: readTextAlignment(layer),
        children: []
    };

    if (fakeThickness.offsetY !== 0) {
        node.fakeThicknessOffsetY = fakeThickness.offsetY;
    }
    if (fakeThickness.offsetX !== 0) {
        node.fakeThicknessOffsetX = fakeThickness.offsetX;
    }

    applyLayoutMetadata(node, bounds, parentBounds, layer.name);
    return node;
}

function buildLayoutJson(doc, nodes) {
    var lines = [];
    lines.push("{");
    lines.push('  "schemaVersion": "2.4",');
    lines.push('  "canvas": {');
    lines.push('    "width": ' + jsonNumber(px(doc.width)) + ",");
    lines.push('    "height": ' + jsonNumber(px(doc.height)));
    lines.push("  },");
    lines.push('  "nodes": [');

    for (var i = 0; i < nodes.length; i++) {
        lines.push(nodeToJson(nodes[i], "    ") + (i < nodes.length - 1 ? "," : ""));
    }

    lines.push("  ]");
    lines.push("}");
    return lines.join("\n");
}

function nodeToJson(node, indent) {
    var childIndent = indent + "  ";
    var lines = [];
    lines.push(indent + "{");
    lines.push(childIndent + '"name": ' + quoteJson(node.name) + ",");
    lines.push(childIndent + '"type": ' + quoteJson(node.type) + ",");
    lines.push(childIndent + '"x": ' + jsonNumber(node.x) + ",");
    lines.push(childIndent + '"y": ' + jsonNumber(node.y) + ",");
    lines.push(childIndent + '"width": ' + jsonNumber(node.width) + ",");
    lines.push(childIndent + '"height": ' + jsonNumber(node.height) + ",");
    lines.push(childIndent + '"visible": ' + (node.visible ? "true" : "false") + ",");
    lines.push(childIndent + '"anchorPreset": ' + quoteJson(node.anchorPreset || "top_left") + ",");
    lines.push(childIndent + '"anchorMin": ' + vectorToJson(node.anchorMin || vector2(0, 1)) + ",");
    lines.push(childIndent + '"anchorMax": ' + vectorToJson(node.anchorMax || vector2(0, 1)) + ",");
    lines.push(childIndent + '"pivot": ' + vectorToJson(node.pivot || vector2(0, 1)) + ",");

    if (node.layoutType) {
        lines.push(childIndent + '"layoutType": ' + quoteJson(node.layoutType) + ",");
        lines.push(childIndent + '"layoutSpacing": ' + jsonNumber(node.layoutSpacing || 0) + ",");
        lines.push(childIndent + '"layoutPaddingLeft": ' + jsonNumber(node.layoutPaddingLeft || 0) + ",");
        lines.push(childIndent + '"layoutPaddingRight": ' + jsonNumber(node.layoutPaddingRight || 0) + ",");
        lines.push(childIndent + '"layoutPaddingTop": ' + jsonNumber(node.layoutPaddingTop || 0) + ",");
        lines.push(childIndent + '"layoutPaddingBottom": ' + jsonNumber(node.layoutPaddingBottom || 0) + ",");
        lines.push(childIndent + '"contentSizeFitter": ' + (node.contentSizeFitter ? "true" : "false") + ",");
    }

    if (node.type === "image") {
        lines.push(childIndent + '"imagePath": ' + quoteJson(node.imagePath) + ",");
    } else if (node.type === "text") {
        lines.push(childIndent + '"text": ' + quoteJson(node.text) + ",");
        lines.push(childIndent + '"fontToken": ' + quoteJson(node.fontToken) + ",");
        lines.push(childIndent + '"materialToken": ' + quoteJson(node.materialToken || "") + ",");
        lines.push(childIndent + '"fontSize": ' + jsonNumber(node.fontSize) + ",");
        lines.push(childIndent + '"characterSpacing": ' + jsonNumber(node.characterSpacing || 0) + ",");
        lines.push(childIndent + '"lineSpacing": ' + jsonNumber(node.lineSpacing || 0) + ",");
        lines.push(childIndent + '"color": ' + quoteJson(node.color) + ",");
        lines.push(childIndent + '"outlineColor": ' + quoteJson(node.outlineColor || "") + ",");
        lines.push(childIndent + '"outlineWidth": ' + jsonNumber(node.outlineWidth || 0) + ",");
        lines.push(childIndent + '"outlineOpacity": ' + jsonNumber(node.outlineOpacity || 1) + ",");
        lines.push(childIndent + '"alignment": ' + quoteJson(node.alignment) + ",");
        if (node.fakeThicknessOffsetY) {
            lines.push(childIndent + '"fakeThicknessOffsetY": ' + jsonNumber(node.fakeThicknessOffsetY) + ",");
        }
        if (node.fakeThicknessOffsetX) {
            lines.push(childIndent + '"fakeThicknessOffsetX": ' + jsonNumber(node.fakeThicknessOffsetX) + ",");
        }
    }

    var children = node.children || [];
    lines.push(childIndent + '"children": [');
    for (var i = 0; i < children.length; i++) {
        lines.push(nodeToJson(children[i], childIndent + "  ") + (i < children.length - 1 ? "," : ""));
    }
    lines.push(childIndent + "]");
    lines.push(indent + "}");
    return lines.join("\n");
}

function vectorToJson(value) {
    return '{"x": ' + jsonNumber(value.x) + ', "y": ' + jsonNumber(value.y) + '}';
}

function readLayerBounds(layer) {
    try {
        var b = layer.bounds;
        var left = Math.floor(px(b[0]));
        var top = Math.floor(px(b[1]));
        var right = Math.ceil(px(b[2]));
        var bottom = Math.ceil(px(b[3]));
        return {
            left: left,
            top: top,
            right: right,
            bottom: bottom,
            width: Math.max(0, right - left),
            height: Math.max(0, bottom - top)
        };
    } catch (e) {
        return null;
    }
}

function readLayerBoundsNoEffects(layer) {
    try {
        var b = layer.boundsNoEffects;
        var left = Math.floor(px(b[0]));
        var top = Math.floor(px(b[1]));
        var right = Math.ceil(px(b[2]));
        var bottom = Math.ceil(px(b[3]));
        return {
            left: left,
            top: top,
            right: right,
            bottom: bottom,
            width: Math.max(0, right - left),
            height: Math.max(0, bottom - top)
        };
    } catch (e) {
        return null;
    }
}

function calculateShadowCompensation(bounds, noEffectsBounds) {
    if (!bounds || !noEffectsBounds) {
        return zeroPadding();
    }

    var innerLeft = Math.max(bounds.left, noEffectsBounds.left);
    var innerTop = Math.max(bounds.top, noEffectsBounds.top);
    var innerRight = Math.min(bounds.right, noEffectsBounds.right);
    var innerBottom = Math.min(bounds.bottom, noEffectsBounds.bottom);

    if (innerRight <= innerLeft || innerBottom <= innerTop) {
        return zeroPadding();
    }

    var effectLeft = Math.max(0, innerLeft - bounds.left);
    var effectTop = Math.max(0, innerTop - bounds.top);
    var effectRight = Math.max(0, bounds.right - innerRight);
    var effectBottom = Math.max(0, bounds.bottom - innerBottom);

    return {
        left: effectRight,
        top: effectBottom,
        right: effectLeft,
        bottom: effectTop
    };
}

function zeroPadding() {
    return { left: 0, top: 0, right: 0, bottom: 0 };
}

function hideAllLayers(container) {
    for (var i = 0; i < container.layers.length; i++) {
        var layer = container.layers[i];
        try {
            layer.visible = false;
        } catch (e) {
        }

        if (layer.typename === "LayerSet") {
            hideAllLayers(layer);
        }
    }
}

function showAllLayers(container) {
    for (var i = 0; i < container.layers.length; i++) {
        var layer = container.layers[i];
        try {
            layer.visible = true;
        } catch (e) {
        }

        if (layer.typename === "LayerSet") {
            showAllLayers(layer);
        }
    }
}

function captureVisibility(container) {
    var states = [];
    for (var i = 0; i < container.layers.length; i++) {
        var layer = container.layers[i];
        var visible = true;
        try {
            visible = layer.visible;
        } catch (e) {
        }
        states.push({ layer: layer, visible: visible });
        if (layer.typename === "LayerSet") {
            var childStates = captureVisibility(layer);
            for (var j = 0; j < childStates.length; j++) {
                states.push(childStates[j]);
            }
        }
    }
    return states;
}

function restoreVisibility(states) {
    for (var i = 0; i < states.length; i++) {
        try {
            states[i].layer.visible = states[i].visible;
        } catch (e) {
        }
    }
}

function showLayerAndParents(layer) {
    var current = layer;
    while (current && current.typename !== "Document") {
        try {
            current.visible = true;
        } catch (e) {
        }
        current = current.parent;
    }
}

function showOnlyLayerChain(layer, previousChain) {
    var nextChain = layerChain(layer);
    var i;

    for (i = 0; i < previousChain.length; i++) {
        if (!containsLayer(nextChain, previousChain[i])) {
            setLayerVisible(previousChain[i], false);
        }
    }

    for (i = nextChain.length - 1; i >= 0; i--) {
        if (!containsLayer(previousChain, nextChain[i])) {
            setLayerVisible(nextChain[i], true);
        }
    }

    return nextChain;
}

function hideVisibleChain(chain) {
    for (var i = 0; i < chain.length; i++) {
        setLayerVisible(chain[i], false);
    }
}

function layerChain(layer) {
    var chain = [];
    var current = layer;
    while (current && current.typename !== "Document") {
        chain.push(current);
        current = current.parent;
    }
    return chain;
}

function containsLayer(layers, layer) {
    for (var i = 0; i < layers.length; i++) {
        if (layers[i] === layer) {
            return true;
        }
    }
    return false;
}

function setLayerVisible(layer, visible) {
    try {
        if (layer.visible !== visible) {
            layer.visible = visible;
        }
    } catch (e) {
    }
}

function hideLayerAndParents(layer) {
    var current = layer;
    while (current && current.typename !== "Document") {
        try {
            current.visible = false;
        } catch (e) {
        }
        current = current.parent;
    }
}

function readDocumentModified(doc) {
    try {
        if (!doc.saved) {
            return "";
        }

        var file = new File(doc.fullName);
        if (file.exists && file.modified) {
            return String(file.modified.getTime());
        }
    } catch (e) {
    }

    return "";
}

function readSelectedLayerIdMap(doc) {
    var map = {};
    var selectedIds = readSelectedLayerIdsByActionManager();

    for (var i = 0; i < selectedIds.length; i++) {
        map[String(selectedIds[i])] = true;
    }

    if (selectedIds.length === 0) {
        try {
            map[String(doc.activeLayer.id)] = true;
        } catch (e) {
        }
    }

    return map;
}

function readSelectedLayerIdsByActionManager() {
    var ids = [];

    try {
        var ref = new ActionReference();
        ref.putProperty(charIDToTypeID("Prpr"), stringIDToTypeID("targetLayers"));
        ref.putEnumerated(charIDToTypeID("Dcmn"), charIDToTypeID("Ordn"), charIDToTypeID("Trgt"));
        var descriptor = executeActionGet(ref);

        if (!descriptor.hasKey(stringIDToTypeID("targetLayers"))) {
            return ids;
        }

        var list = descriptor.getList(stringIDToTypeID("targetLayers"));
        for (var i = 0; i < list.count; i++) {
            var layerIndex = list.getReference(i).getIndex();
            var layerRef = new ActionReference();
            layerRef.putIndex(charIDToTypeID("Lyr "), layerIndex);
            var layerDescriptor = executeActionGet(layerRef);
            ids.push(layerDescriptor.getInteger(stringIDToTypeID("layerID")));
        }
    } catch (e) {
    }

    return ids;
}

function trySkipCachedImage(entry, context, file) {
    if (!context.exportCache || !context.sourceModified || !file.exists) {
        return false;
    }

    var node = entry.node;
    var record = context.exportCache[node.imagePath];
    if (!record) {
        return false;
    }

    if (record.sourceModified !== context.sourceModified) {
        return false;
    }

    if (record.signature !== buildExportSignature(entry.layer, node)) {
        return false;
    }

    if (Number(record.fileSize) !== fileSize(file)) {
        return false;
    }

    if (record.width <= 0 || record.height <= 0) {
        return false;
    }

    node.x = Number(record.x);
    node.y = Number(record.y);
    node.width = Number(record.width);
    node.height = Number(record.height);
    applyLayoutMetadata(node, {
        left: node.x,
        top: node.y,
        right: node.x + node.width,
        bottom: node.y + node.height,
        width: node.width,
        height: node.height
    }, node._parentBounds, node._rawName);

    return true;
}

function updateExportCacheRecord(entry, context, file) {
    if (!context.exportCache || !context.sourceModified || !file.exists) {
        return;
    }

    var node = entry.node;
    context.exportCache[node.imagePath] = {
        imagePath: node.imagePath,
        sourceModified: context.sourceModified,
        signature: buildExportSignature(entry.layer, node),
        fileSize: fileSize(file),
        x: node.x,
        y: node.y,
        width: node.width,
        height: node.height
    };
    context.exportCacheDirty = true;
}

function buildExportSignature(layer, node) {
    var parts = [];
    parts.push("fastalign_v2");
    parts.push(safeLayerId(layer));
    parts.push(node._rawName || "");
    appendBoundsSignature(parts, node._exportBounds);
    appendBoundsSignature(parts, node._noEffectsBounds);
    parts.push(Math.round((node._padding && node._padding.left) || 0));
    parts.push(Math.round((node._padding && node._padding.top) || 0));
    parts.push(Math.round((node._padding && node._padding.right) || 0));
    parts.push(Math.round((node._padding && node._padding.bottom) || 0));
    return parts.join(",");
}

function safeLayerId(layer) {
    try {
        return String(layer.id);
    } catch (e) {
        return "";
    }
}

function appendBoundsSignature(parts, bounds) {
    if (!bounds) {
        parts.push("null");
        return;
    }

    parts.push(Math.round(bounds.left));
    parts.push(Math.round(bounds.top));
    parts.push(Math.round(bounds.right));
    parts.push(Math.round(bounds.bottom));
}

function loadExportCache(imageFolder) {
    var cache = {};
    var file = exportCacheFile(imageFolder);
    if (!file.exists) {
        return cache;
    }

    try {
        file.encoding = "UTF-8";
        file.open("r");
        var lines = file.read().split(/\r?\n/);
        file.close();

        for (var i = 0; i < lines.length; i++) {
            var line = trim(lines[i]);
            if (!line || startsWith(line, "#")) {
                continue;
            }

            var fields = line.split("\t");
            if (fields.length < 9) {
                continue;
            }

            var imagePath = decodeCacheValue(fields[0]);
            cache[imagePath] = {
                imagePath: imagePath,
                sourceModified: decodeCacheValue(fields[1]),
                signature: decodeCacheValue(fields[2]),
                fileSize: Number(fields[3]),
                x: Number(fields[4]),
                y: Number(fields[5]),
                width: Number(fields[6]),
                height: Number(fields[7]),
                reserved: decodeCacheValue(fields[8])
            };
        }
    } catch (e) {
        try {
            file.close();
        } catch (ignored) {
        }
        return {};
    }

    return cache;
}

function writeExportCacheIfDirty(context) {
    if (!context.exportCache || !context.exportCacheDirty) {
        return;
    }

    var file = exportCacheFile(context.imageFolder);
    var lines = ["# PS_To_Unity_v2 export cache"];
    for (var key in context.exportCache) {
        if (!context.exportCache.hasOwnProperty(key)) {
            continue;
        }

        var record = context.exportCache[key];
        lines.push([
            encodeCacheValue(record.imagePath),
            encodeCacheValue(record.sourceModified),
            encodeCacheValue(record.signature),
            record.fileSize,
            record.x,
            record.y,
            record.width,
            record.height,
            ""
        ].join("\t"));
    }

    try {
        writeTextFile(file, lines.join("\n"));
    } catch (e) {
    }
}

function exportCacheFile(imageFolder) {
    return new File(imageFolder.fsName + "/.ps_to_unity_export_cache.tsv");
}

function encodeCacheValue(value) {
    try {
        return encodeURIComponent(String(value));
    } catch (e) {
        return String(value).replace(/\t/g, " ").replace(/\r?\n/g, " ");
    }
}

function decodeCacheValue(value) {
    try {
        return decodeURIComponent(String(value));
    } catch (e) {
        return String(value);
    }
}

function savePng(doc, outputFile) {
    try {
        var exportOptions = new ExportOptionsSaveForWeb();
        exportOptions.format = SaveDocumentType.PNG;
        exportOptions.PNG8 = false;
        exportOptions.transparency = true;
        exportOptions.interlaced = false;
        exportOptions.quality = 100;
        doc.exportDocument(outputFile, ExportType.SAVEFORWEB, exportOptions);
    } catch (e) {
        var options = new PNGSaveOptions();
        options.compression = 0;
        doc.saveAs(outputFile, options, true, Extension.LOWERCASE);
    }
}

function savePngIfChanged(doc, outputFile) {
    var tempFile = uniqueTempPngFile(outputFile);
    savePng(doc, tempFile);

    if (outputFile.exists && fileSize(outputFile) === fileSize(tempFile)) {
        tempFile.remove();
        return "unchanged";
    }

    if (outputFile.exists && !outputFile.remove()) {
        tempFile.remove();
        return false;
    }

    if (!tempFile.rename(outputFile.name)) {
        tempFile.remove();
        return false;
    }

    return "saved";
}

function uniqueTempPngFile(outputFile) {
    var baseName = outputFile.name.replace(/\.png$/i, "");
    var folder = outputFile.parent;
    var index = 0;
    var tempFile;

    do {
        index++;
        tempFile = new File(folder.fsName + "/." + baseName + "_tmp_" + index + ".png");
    } while (tempFile.exists);

    return tempFile;
}

function fileSize(file) {
    try {
        return file.exists ? file.length : -1;
    } catch (e) {
        return -1;
    }
}

function ensureFolder(folder) {
    if (!folder.exists && !folder.create()) {
        throw new Error("Cannot create folder: " + folder.fsName);
    }
}

function writeTextFile(file, content) {
    file.encoding = "UTF-8";
    file.parent.create();
    if (file.exists) {
        file.remove();
    }
    file.open("w", "TEXT");
    file.lineFeed = "\n";
    file.write(content);
    file.close();
}

function defaultImageFolder(doc) {
    var parent = defaultOutputFolder(doc);
    return new Folder(parent.fsName + "/" + defaultBaseName(doc.name) + "_images");
}

function resolveImageOutputFolder(options) {
    var folder = new Folder(options.imageFolder);
    if (!options.useUnityAtlasStructure) {
        return folder;
    }

    var language = normalizeAtlasLanguage(options.atlasLanguage);
    return new Folder(folder.fsName + "/Atlas/SpriteAtlas/" + language);
}

function normalizeAtlasLanguage(value) {
    var text = String(value || "Base").toUpperCase();
    if (text === "CHS" || text === "CHT" || text === "EN") {
        return text;
    }
    return "Base";
}

function defaultLayoutJsonFile(doc, imageFolder) {
    var parent = new Folder(imageFolder);
    return new File(parent.fsName + "/" + defaultBaseName(doc.name) + "_layout.json");
}

function outputParentFolder(folder) {
    try {
        var selectedFolder = new Folder(folder);
        if (selectedFolder.parent) {
            return selectedFolder.parent;
        }
    } catch (e) {
    }

    return null;
}

function defaultOutputFolder(doc) {
    try {
        if (doc.path && doc.path.exists) {
            return doc.path;
        }
    } catch (e) {
    }

    return Folder.desktop;
}

function defaultBaseName(documentName) {
    var base = String(documentName).replace(/\.[^\.]+$/, "");
    base = normalizeAsciiSlug(base);
    return base ? base : "photoshop_ui";
}

function ensureJsonExtension(path) {
    var value = trim(path);
    if (!value) {
        return "";
    }

    return /\.json$/i.test(value) ? value : value + ".json";
}

function isPathInsideFolder(file, folder) {
    var filePath = normalizeFileSystemPath(file.fsName);
    var folderPath = normalizeFileSystemPath(folder.fsName);
    return filePath.indexOf(folderPath + "/") === 0;
}

function isSameFolder(first, second) {
    return normalizeFileSystemPath(first.fsName) === normalizeFileSystemPath(second.fsName);
}

function normalizeFileSystemPath(path) {
    return String(path)
        .replace(/\\/g, "/")
        .replace(/\/+$/g, "")
        .toLowerCase();
}

function uniqueFileName(name, counters) {
    var base = normalizeAsciiSlug(stripFakeThicknessTags(stripLayoutGroupTags(stripLayoutTokens(stripControlPrefix(name)))));
    if (!base) {
        base = "layer";
    }

    if (!counters[base]) {
        counters[base] = 1;
        return base;
    }

    counters[base]++;
    return base + "_" + pad3(counters[base]);
}

function uniqueNodeName(name, counters) {
    return uniqueFileName(name, counters);
}

function stripLayoutGroupTags(name) {
    return String(name || "").replace(/\[(?:H|HLAYOUT|V|VLAYOUT)\]/ig, "");
}

function stripFakeThicknessTags(name) {
    return String(name || "").replace(/\[THICK\s*:\s*-?\d+(?:\.\d+)?\s*(?::\s*-?\d+(?:\.\d+)?)?\s*\]/ig, "");
}

function normalizeAsciiSlug(value) {
    return String(value)
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, "_")
        .replace(/^_+|_+$/g, "")
        .replace(/_+/g, "_");
}

function stripControlPrefix(name) {
    var text = String(name);
    var prefixes = ["IMG_", "TXT_", "BTN_", "SKIN_", "GROUP_", "IGNORE_", "REF_"];
    for (var i = 0; i < prefixes.length; i++) {
        if (startsWith(text, prefixes[i])) {
            return text.substring(prefixes[i].length);
        }
    }
    return text;
}

function stripLayoutTokens(name) {
    var text = String(name);
    var tokens = [
        "ANCHOR_TOP_LEFT",
        "ANCHOR_TOP_RIGHT",
        "ANCHOR_BOTTOM_LEFT",
        "ANCHOR_BOTTOM_RIGHT",
        "ANCHOR_TOP",
        "ANCHOR_BOTTOM",
        "ANCHOR_LEFT",
        "ANCHOR_RIGHT",
        "ANCHOR_CENTER",
        "ANCHOR_MIDDLE",
        "ANCHOR_TL",
        "ANCHOR_TR",
        "ANCHOR_BL",
        "ANCHOR_BR",
        "ANCHOR_TC",
        "ANCHOR_BC",
        "ANCHOR_ML",
        "ANCHOR_MR",
        "ANCHOR_C",
        "STRETCH_FULL",
        "STRETCH_BOTH",
        "STRETCH_X",
        "STRETCH_Y",
        "STRETCH_HORIZONTAL",
        "STRETCH_VERTICAL",
        "ANCHOR_STRETCH_FULL",
        "ANCHOR_STRETCH_X",
        "ANCHOR_STRETCH_Y",
        "PIVOT_TOP_LEFT",
        "PIVOT_TOP_RIGHT",
        "PIVOT_BOTTOM_LEFT",
        "PIVOT_BOTTOM_RIGHT",
        "PIVOT_TOP",
        "PIVOT_BOTTOM",
        "PIVOT_LEFT",
        "PIVOT_RIGHT",
        "PIVOT_CENTER",
        "PIVOT_MIDDLE",
        "PIVOT_TL",
        "PIVOT_TR",
        "PIVOT_BL",
        "PIVOT_BR",
        "PIVOT_TC",
        "PIVOT_BC",
        "PIVOT_ML",
        "PIVOT_MR",
        "PIVOT_C"
    ];

    for (var i = 0; i < tokens.length; i++) {
        text = replaceAllCaseInsensitive(text, tokens[i], "");
    }

    return text;
}

function replaceAllCaseInsensitive(text, token, replacement) {
    var upper = String(text).toUpperCase();
    var needle = String(token).toUpperCase();
    var index = upper.indexOf(needle);
    while (index >= 0) {
        text = text.substring(0, index) + replacement + text.substring(index + token.length);
        upper = String(text).toUpperCase();
        index = upper.indexOf(needle);
    }
    return text;
}

function isReferenceOrIgnored(name) {
    var text = String(name);
    return startsWith(text, "IGNORE_") || startsWith(text, "REF_") || text.toLowerCase().indexOf("[ignore]") >= 0;
}

function isVisible(layer) {
    try {
        return layer.visible;
    } catch (e) {
        return true;
    }
}

function isTextLayer(layer) {
    try {
        return layer.kind === LayerKind.TEXT;
    } catch (e) {
        return false;
    }
}

function shouldExportTextLayerAsImage(layer, context) {
    var text = String(layer.name || "");
    var upper = text.toUpperCase();
    var layerId = safeLayerId(layer);

    if (context.selectedTextLayerIds && context.selectedTextLayerIds[layerId]) {
        return true;
    }

    if (hasAny(upper, ["[PNG]", "[IMAGE]", "[IMG]", "TXTIMG_", "TXT_IMG_", "TEXTIMG_", "TEXT_IMG_"])) {
        return true;
    }

    if (hasAny(upper, ["[TMP]", "[TEXT]", "TMP_", "TXT_"])) {
        return false;
    }

    if (context.autoRouteNonSourceHanFonts) {
        var rawFont = readRawFontName(layer);
        if (!rawFont) {
            return true;
        }
        return !isSourceHanFamily(rawFont);
    }

    return context.textLayerOutput === "image";
}

function readText(layer) {
    try {
        return layer.textItem.contents || "";
    } catch (e) {
        return "";
    }
}

function readFontName(layer) {
    return normalizeAsciiSlug(readRawFontName(layer));
}

function readRawFontName(layer) {
    try {
        if (layer.textItem && layer.textItem.font) {
            return layer.textItem.font || "";
        }
    } catch (e) {
    }

    try {
        var textStyle = readPrimaryTextStyleDescriptor(layer);
        var fontName = getDescriptorString(textStyle, ["fontPostScriptName", "fontName"], ["FntN"], "");
        if (fontName) {
            return fontName;
        }
    } catch (ignored) {
    }

    return "";
}

function isSourceHanFamily(rawFontName) {
    var lower = String(rawFontName || "").toLowerCase();
    if (!lower) {
        return false;
    }

    if (/[\u3400-\u9fff\uf900-\ufaff]/.test(lower)) {
        return true;
    }

    return hasAny(lower, [
        "gensenrounded",
        "gensenmaru",
        "sourcehan",
        "source han",
        "noto sans cjk",
        "noto serif cjk",
        "notosanscjk",
        "notoserifcjk"
    ]);
}

function readTextLayerStyle(layer) {
    var style = {
        fillColor: "",
        outlineColor: "",
        outlineWidth: 0,
        outlineOpacity: 1
    };

    try {
        var descriptor = getLayerDescriptor(layer);
        var effects = getDescriptorObject(descriptor, ["layerEffects"], ["Lefx"]);
        if (!effects) {
            return style;
        }

        var colorOverlay = getDescriptorObject(effects, ["solidFill"], ["SoFi"]);
        if (colorOverlay && getDescriptorBoolean(colorOverlay, ["enabled"], ["enab"], true)) {
            var fillColor = getDescriptorObject(colorOverlay, ["color"], ["Clr "]);
            if (fillColor) {
                style.fillColor = descriptorColorToHex(fillColor);
            }
        }

        var stroke = getDescriptorObject(effects, ["frameFX"], ["FrFX"]);
        if (stroke && getDescriptorBoolean(stroke, ["enabled"], ["enab"], true)) {
            var strokeWidth = getDescriptorUnitDouble(stroke, ["size"], ["Sz  "], 0);
            var strokeColor = getDescriptorObject(stroke, ["color"], ["Clr "]);
            if (strokeWidth > 0 && strokeColor) {
                style.outlineColor = descriptorColorToHex(strokeColor);
                style.outlineWidth = round2(strokeWidth);
                style.outlineOpacity = round2(getDescriptorUnitDouble(stroke, ["opacity"], ["Opct"], 100) / 100);
            }
        }
    } catch (e) {
        return style;
    }

    return style;
}

function buildTextMaterialToken(textStyle) {
    if (!textStyle || !textStyle.outlineColor || textStyle.outlineWidth <= 0) {
        return "";
    }

    return normalizeAsciiSlug("outline_" + textStyle.outlineColor.replace("#", "") + "_" + Math.round(textStyle.outlineWidth));
}

function readMaterialToken(layer) {
    try {
        return readNameToken(layer.name, ["MAT", "MATERIAL", "TMPMAT"]);
    } catch (e) {
        return "";
    }
}

function readFakeThickness(name) {
    var text = String(name || "");
    var match = text.match(/\[THICK\s*:\s*(-?\d+(?:\.\d+)?)\s*(?::\s*(-?\d+(?:\.\d+)?))?\s*\]/i);
    return {
        offsetY: match ? round2(Number(match[1])) : 0,
        offsetX: match && match[2] ? round2(Number(match[2])) : 0
    };
}

function readNameToken(name, keys) {
    var text = String(name || "");

    for (var i = 0; i < keys.length; i++) {
        var bracketPattern = new RegExp("\\[" + keys[i] + "\\s*[:=]\\s*([^\\]]+)\\]", "i");
        var bracketMatch = text.match(bracketPattern);
        if (bracketMatch && bracketMatch[1]) {
            return normalizeAsciiSlug(bracketMatch[1]);
        }

        var inlinePattern = new RegExp("(?:^|[_\\s-])" + keys[i] + "[_:-]([A-Za-z0-9][A-Za-z0-9_-]*)", "i");
        var inlineMatch = text.match(inlinePattern);
        if (inlineMatch && inlineMatch[1]) {
            return normalizeAsciiSlug(inlineMatch[1]);
        }
    }

    return "";
}

function readPrimaryTextStyleDescriptor(layer) {
    try {
        var descriptor = getLayerDescriptor(layer);
        var textDescriptor = getDescriptorObject(descriptor, ["textKey"], ["Txt "]);
        if (!textDescriptor) {
            return null;
        }

        var rangeListKey = findDescriptorKey(textDescriptor, ["textStyleRange"], ["Txtt"]);
        if (!rangeListKey) {
            return null;
        }

        var ranges = textDescriptor.getList(rangeListKey);
        if (!ranges || ranges.count < 1) {
            return null;
        }

        var firstRange = ranges.getObjectValue(0);
        return getDescriptorObject(firstRange, ["textStyle"], ["TxtS"]);
    } catch (e) {
        return null;
    }
}

function getLayerDescriptor(layer) {
    var reference = new ActionReference();
    reference.putIdentifier(charIDToTypeID("Lyr "), layer.id);
    return executeActionGet(reference);
}

function getDescriptorObject(descriptor, stringKeys, charKeys) {
    var id = findDescriptorKey(descriptor, stringKeys, charKeys);
    if (!id) {
        return null;
    }

    try {
        return descriptor.getObjectValue(id);
    } catch (e) {
        return null;
    }
}

function getDescriptorBoolean(descriptor, stringKeys, charKeys, fallback) {
    var id = findDescriptorKey(descriptor, stringKeys, charKeys);
    if (!id) {
        return fallback;
    }

    try {
        return descriptor.getBoolean(id);
    } catch (e) {
        return fallback;
    }
}

function getDescriptorUnitDouble(descriptor, stringKeys, charKeys, fallback) {
    var id = findDescriptorKey(descriptor, stringKeys, charKeys);
    if (!id) {
        return fallback;
    }

    try {
        return descriptor.getUnitDoubleValue(id);
    } catch (e1) {
        try {
            return descriptor.getDouble(id);
        } catch (e2) {
            return fallback;
        }
    }
}

function getDescriptorDouble(descriptor, stringKeys, charKeys, fallback) {
    var id = findDescriptorKey(descriptor, stringKeys, charKeys);
    if (!id) {
        return fallback;
    }

    try {
        return descriptor.getDouble(id);
    } catch (e1) {
        try {
            return descriptor.getUnitDoubleValue(id);
        } catch (e2) {
            return fallback;
        }
    }
}

function getDescriptorString(descriptor, stringKeys, charKeys, fallback) {
    var id = findDescriptorKey(descriptor, stringKeys, charKeys);
    if (!id) {
        return fallback;
    }

    try {
        return descriptor.getString(id);
    } catch (e) {
        return fallback;
    }
}

function findDescriptorKey(descriptor, stringKeys, charKeys) {
    var i;
    var id;

    if (!descriptor) {
        return 0;
    }

    for (i = 0; i < stringKeys.length; i++) {
        id = stringIDToTypeID(stringKeys[i]);
        if (descriptor.hasKey(id)) {
            return id;
        }
    }

    for (i = 0; i < charKeys.length; i++) {
        id = charIDToTypeID(charKeys[i]);
        if (descriptor.hasKey(id)) {
            return id;
        }
    }

    return 0;
}

function descriptorColorToHex(colorDescriptor) {
    var red = getDescriptorDouble(colorDescriptor, ["red"], ["Rd  "], 255);
    var green = getDescriptorDouble(colorDescriptor, ["green"], ["Grn "], 255);
    var blue = getDescriptorDouble(colorDescriptor, ["blue"], ["Bl  "], 255);
    return "#" + hex2(red) + hex2(green) + hex2(blue);
}

function readFontSize(layer) {
    try {
        var textStyle = readPrimaryTextStyleDescriptor(layer);
        var impliedFontSize = getDescriptorUnitDouble(textStyle, ["impliedFontSize"], [], 0);
        if (isFinite(impliedFontSize) && impliedFontSize > 0) {
            return round2(impliedFontSize);
        }
    } catch (e1) {
    }

    try {
        var sizePx = px(layer.textItem.size);
        var docRes = readLayerDocumentResolution(layer);
        var sizePt = sizePx * 72 / docRes;
        if (isFinite(sizePt) && sizePt > 0) {
            return round2(sizePt);
        }
    } catch (e2) {
    }

    return 24;
}

function readTextCharacterSpacing(layer) {
    try {
        return round2(Number(layer.textItem.tracking || 0) / 10);
    } catch (e) {
        return 0;
    }
}

function readTextLineSpacing(layer) {
    try {
        var textStyle = readPrimaryTextStyleDescriptor(layer);
        var impliedLeading = getDescriptorUnitDouble(textStyle, ["impliedLeading"], [], 0);
        if (isFinite(impliedLeading) && impliedLeading > 0) {
            return round2(impliedLeading - readFontSize(layer));
        }
    } catch (e1) {
    }

    try {
        var leadingPx = px(layer.textItem.leading);
        var docRes = readLayerDocumentResolution(layer);
        var leadingPt = leadingPx * 72 / docRes;
        if (isFinite(leadingPt)) {
            return round2(leadingPt - readFontSize(layer));
        }
    } catch (e2) {
    }

    return 0;
}

function readLayerDocumentResolution(layer) {
    var parent = layer;
    while (parent) {
        try {
            if (parent.typename === "Document" && parent.resolution) {
                return Number(parent.resolution) || 72;
            }
            parent = parent.parent;
        } catch (e) {
            break;
        }
    }

    try {
        return Number(app.activeDocument.resolution) || 72;
    } catch (ignored) {
        return 72;
    }
}

function readTextColor(layer) {
    try {
        var color = layer.textItem.color.rgb;
        return "#" + hex2(color.red) + hex2(color.green) + hex2(color.blue);
    } catch (e) {
        return "#FFFFFF";
    }
}

function readTextAlignment(layer) {
    try {
        var justification = layer.textItem.justification;
        if (justification === Justification.CENTER) {
            return "center";
        }
        if (justification === Justification.RIGHT) {
            return "right";
        }
    } catch (e) {
    }

    return "left";
}


function quoteJson(value) {
    return '"' + String(value)
        .replace(/\\/g, "\\\\")
        .replace(/"/g, '\\"')
        .replace(/\r/g, "\\r")
        .replace(/\n/g, "\\n")
        .replace(/\t/g, "\\t") + '"';
}

function jsonNumber(value) {
    var number = round2(value);
    if (!isFinite(number)) {
        return "0";
    }
    return String(number);
}

function px(value) {
    try {
        return Number(value.as("px"));
    } catch (e) {
        return Number(value);
    }
}

function round2(value) {
    return Math.round(Number(value) * 100) / 100;
}

function pad3(value) {
    var text = String(value);
    while (text.length < 3) {
        text = "0" + text;
    }
    return text;
}

function hex2(value) {
    var text = Math.max(0, Math.min(255, Math.round(value))).toString(16).toUpperCase();
    return text.length === 1 ? "0" + text : text;
}

function startsWith(text, prefix) {
    return String(text).indexOf(prefix) === 0;
}

function trim(value) {
    return String(value).replace(/^\s+|\s+$/g, "");
}

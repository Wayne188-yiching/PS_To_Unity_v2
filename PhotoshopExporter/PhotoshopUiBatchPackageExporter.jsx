#target photoshop

// Batch wrapper for PhotoshopUiPackageExporter.jsx.
//
// Each PSD gets an independent Unity package:
//   <Package Root>/<relative PSD path without .psd>/layout.json
//   <Package Root>/<relative PSD path without .psd>/Atlas/SpriteAtlas/Base/*.png
//
// It deliberately calls the normal exporter through its automation contract
// instead of duplicating export code.  Keeping a single PNG/Layout pipeline is
// important: a batch run must produce the same package as a one-off export.

(function () {
    var scriptFolder = Folder($.fileName).parent;
    var exporterFile = new File(scriptFolder.fsName + "/PhotoshopUiPackageExporter.jsx");
    if (!exporterFile.exists) {
        alert("找不到 PhotoshopUiPackageExporter.jsx：\n" + exporterFile.fsName);
        return;
    }

    var options = showBatchDialog();
    if (!options) return;

    var psdFiles = [];
    collectPsdFiles(options.sourceFolder, options.includeSubfolders, psdFiles);
    if (psdFiles.length === 0) {
        alert("選取的資料夾內沒有 PSD 檔案。");
        return;
    }

    if (!ensureBatchFolder(options.packageRoot)) {
        alert("無法建立輸出資料夾：\n" + options.packageRoot.fsName);
        return;
    }

    var previousDialogs = app.displayDialogs;
    var completed = 0;
    var failed = 0;
    var report = [];
    report.push("PS To Unity Batch Package Export");
    report.push("Source: " + options.sourceFolder.fsName);
    report.push("Output: " + options.packageRoot.fsName);
    report.push("Files: " + psdFiles.length);
    report.push("");

    try {
        app.displayDialogs = DialogModes.NO;
        for (var i = 0; i < psdFiles.length; i++) {
            var psdFile = psdFiles[i];
            var sourceDoc = null;
            var relative = relativePsdPath(psdFile, options.sourceFolder);
            var packageFolder = new Folder(options.packageRoot.fsName + "/" + relative);
            var layoutFile = new File(packageFolder.fsName + "/layout.json");

            try {
                if (!ensureBatchFolder(packageFolder)) {
                    throw new Error("無法建立套件資料夾：" + packageFolder.fsName);
                }

                sourceDoc = app.open(psdFile);
                $.global.PS_TO_UNITY_V2_AUTOMATION_OPTIONS = {
                    imageFolder: packageFolder.fsName,
                    layoutJsonFile: layoutFile.fsName,
                    ignoreHiddenLayers: options.ignoreHiddenLayers,
                    skipReferenceLayers: options.skipReferenceLayers,
                    useExportCache: options.useExportCache,
                    useFastLayerDuplicate: true,
                    useUnityAtlasStructure: true,
                    atlasLanguage: "Base",
                    textLayerOutput: options.textLayerOutput,
                    selectedTextLayersAsImages: false,
                    autoRouteNonSourceHanFonts: options.autoRouteNonSourceHanFonts,
                    automationRunId: String(i + 1)
                };
                $.global.PS_TO_UNITY_V2_AUTOMATION_RESULT = null;
                $.evalFile(exporterFile);

                var runResult = $.global.PS_TO_UNITY_V2_AUTOMATION_RESULT;
                if (!runResult || runResult.error) {
                    throw new Error(runResult && runResult.error ? runResult.error : "匯出器沒有回傳結果。");
                }
                completed++;
                report.push("OK\t" + relative + "\timages=" + runResult.imageCount + "\ttext=" + runResult.textCount + "\tgroups=" + runResult.groupCount);
            } catch (e) {
                failed++;
                report.push("FAIL\t" + relative + "\t" + e.message);
            } finally {
                $.global.PS_TO_UNITY_V2_AUTOMATION_OPTIONS = null;
                $.global.PS_TO_UNITY_V2_AUTOMATION_RESULT = null;
                if (sourceDoc) {
                    try { sourceDoc.close(SaveOptions.DONOTSAVECHANGES); } catch (closeError) {}
                }
            }
        }
    } finally {
        app.displayDialogs = previousDialogs;
    }

    report.unshift("Result: " + completed + " succeeded, " + failed + " failed");
    var reportFile = new File(options.packageRoot.fsName + "/batch_export_report.txt");
    writeUtf8Text(reportFile, report.join("\r\n"));
    alert(
        "批次 UI Package 匯出完成。\n\n" +
        "成功：" + completed + "　失敗：" + failed + "　總數：" + psdFiles.length + "\n\n" +
        "每張 PSD 都已輸出為獨立 Unity Package（layout.json + Atlas/SpriteAtlas/Base）。\n" +
        "報告：\n" + reportFile.fsName
    );
})();

function showBatchDialog() {
    var dialog = new Window("dialog", "批次匯出 PSD 為 Unity Packages");
    dialog.orientation = "column";
    dialog.alignChildren = "fill";

    var note = dialog.add("statictext", undefined,
        "每個 PSD 都會輸出成一個獨立套件。這是『新規格重建』流程，不需要建立 Old Sprite → New Sprite 的 177 筆對照。");
    note.characters = 78;

    var sourceRow = dialog.add("group");
    sourceRow.orientation = "row";
    sourceRow.add("statictext", undefined, "PSD 來源資料夾：");
    var sourceText = sourceRow.add("edittext", undefined, "");
    sourceText.characters = 54;
    var sourceButton = sourceRow.add("button", undefined, "選擇…");

    var outputRow = dialog.add("group");
    outputRow.orientation = "row";
    outputRow.add("statictext", undefined, "Unity Package 輸出根目錄：");
    var outputText = outputRow.add("edittext", undefined, "");
    outputText.characters = 45;
    var outputButton = outputRow.add("button", undefined, "選擇…");

    var optionsPanel = dialog.add("panel", undefined, "匯出選項");
    optionsPanel.orientation = "column";
    optionsPanel.alignChildren = "left";
    var recursive = optionsPanel.add("checkbox", undefined, "包含子資料夾");
    recursive.value = true;
    var ignoreHidden = optionsPanel.add("checkbox", undefined, "忽略隱藏 / 已關閉的圖層");
    ignoreHidden.value = true;
    var skipReference = optionsPanel.add("checkbox", undefined, "略過 IGNORE_ 與 REF_ 圖層");
    skipReference.value = true;
    var useCache = optionsPanel.add("checkbox", undefined, "使用匯出快取（重跑時跳過未變更 PNG）");
    useCache.value = true;
    var textAsImage = optionsPanel.add("checkbox", undefined, "預設將文字輸出成 PNG（否則保留 TMP）");
    textAsImage.value = false;
    var unsupportedFontAsImage = optionsPanel.add("checkbox", undefined, "白名單外字型改為 PNG");
    unsupportedFontAsImage.value = false;

    sourceButton.onClick = function () {
        var folder = Folder.selectDialog("選擇含 PSD 的來源資料夾");
        if (folder) sourceText.text = folder.fsName;
    };
    outputButton.onClick = function () {
        var folder = Folder.selectDialog("選擇 Unity Package 輸出根目錄");
        if (folder) outputText.text = folder.fsName;
    };

    var buttons = dialog.add("group");
    buttons.alignment = "right";
    var cancel = buttons.add("button", undefined, "取消", { name: "cancel" });
    var exportButton = buttons.add("button", undefined, "開始批次匯出", { name: "ok" });
    cancel.onClick = function () { dialog.close(0); };
    exportButton.onClick = function () {
        var sourcePath = trimBatchText(sourceText.text);
        var outputPath = trimBatchText(outputText.text);
        if (!sourcePath || !(new Folder(sourcePath)).exists) {
            alert("請選擇存在的 PSD 來源資料夾。");
            return;
        }
        if (!outputPath) {
            alert("請選擇 Unity Package 輸出根目錄。");
            return;
        }
        dialog.result = {
            sourceFolder: new Folder(sourcePath),
            packageRoot: new Folder(outputPath),
            includeSubfolders: recursive.value,
            ignoreHiddenLayers: ignoreHidden.value,
            skipReferenceLayers: skipReference.value,
            useExportCache: useCache.value,
            textLayerOutput: textAsImage.value ? "image" : "tmp",
            autoRouteNonSourceHanFonts: unsupportedFontAsImage.value
        };
        dialog.close(1);
    };

    return dialog.show() === 1 ? dialog.result : null;
}

function collectPsdFiles(folder, includeSubfolders, files) {
    var entries = folder.getFiles();
    for (var i = 0; i < entries.length; i++) {
        var entry = entries[i];
        if (entry instanceof Folder) {
            if (includeSubfolders) collectPsdFiles(entry, true, files);
        } else if (entry instanceof File && /\.psd$/i.test(entry.name)) {
            files.push(entry);
        }
    }
}

function relativePsdPath(file, root) {
    var rootPath = String(root.fsName).replace(/\\/g, "/");
    var filePath = String(file.fsName).replace(/\\/g, "/");
    var relative = filePath.substring(rootPath.length).replace(/^\/+/, "").replace(/\.psd$/i, "");
    var parts = relative.split("/");
    for (var i = 0; i < parts.length; i++) parts[i] = safePathSegment(parts[i]);
    return parts.join("/");
}

function safePathSegment(value) {
    var cleaned = String(value).replace(/[\\/:*?\"<>|]/g, "_").replace(/^\.+$/, "package");
    return cleaned || "package";
}

function trimBatchText(value) {
    return String(value || "").replace(/^\s+|\s+$/g, "");
}

function ensureBatchFolder(folder) {
    if (folder.exists) return true;
    var parent = folder.parent;
    if (parent && !parent.exists && !ensureBatchFolder(parent)) return false;
    return folder.create();
}

function writeUtf8Text(file, text) {
    file.encoding = "UTF-8";
    if (!file.open("w")) throw new Error("無法寫入批次報告：" + file.fsName);
    file.write(text);
    file.close();
}

#target photoshop

(function () {
    var sourceFolder = resolveSourceFolder();
    var repoRoot = resolveRepoRoot(sourceFolder);

    var dialog = new Window("dialog", "PS To Unity v2");
    dialog.orientation = "column";
    dialog.alignChildren = "fill";

    var title = dialog.add("statictext", undefined, "PS To Unity v2");
    title.characters = 48;

    var sourceText = dialog.add("statictext", undefined, "Source: " + sourceFolder.fsName);
    sourceText.characters = 82;

    var versionText = dialog.add("statictext", undefined, "Version: " + readVersion(repoRoot));
    versionText.characters = 82;

    var mainPanel = dialog.add("panel", undefined, "Main Tools");
    mainPanel.orientation = "column";
    mainPanel.alignChildren = "fill";
    mainPanel.margins = 14;

    var exporterButton = mainPanel.add("button", undefined, "UI Package Exporter");
    var namerButton = mainPanel.add("button", undefined, "Layer Auto Namer");
    var spineButton = mainPanel.add("button", undefined, "Photoshop To Spine");
    var updaterButton = mainPanel.add("button", undefined, "Update Everything (git pull)");

    var devPanel = dialog.add("panel", undefined, "Calibration / Debug");
    devPanel.orientation = "column";
    devPanel.alignChildren = "fill";
    devPanel.margins = 14;

    var calibrationButton = devPanel.add("button", undefined, "Create Calibration PSD (font precision board)");

    var debugRow = devPanel.add("group");
    debugRow.orientation = "row";
    debugRow.alignChildren = "fill";
    var debugFontRoutingButton = debugRow.add("button", undefined, "Font Routing");
    var debugFontSizeButton = debugRow.add("button", undefined, "Font Size");
    var debugLayerKindButton = debugRow.add("button", undefined, "Layer Kind");
    debugFontRoutingButton.preferredSize.width = 110;
    debugFontSizeButton.preferredSize.width = 110;
    debugLayerKindButton.preferredSize.width = 110;

    var closeGroup = dialog.add("group");
    closeGroup.orientation = "row";
    closeGroup.alignment = "right";
    var closeButton = closeGroup.add("button", undefined, "Close", { name: "cancel" });

    spineButton.enabled = toolExists(sourceFolder, "PhotoshopToSpine.jsx");
    calibrationButton.enabled = toolExists(sourceFolder, "CreateCalibrationPsd.jsx");
    debugFontRoutingButton.enabled = toolExists(sourceFolder, "Debug_FontRouting.jsx");
    debugFontSizeButton.enabled = toolExists(sourceFolder, "Debug_FontSize.jsx");
    debugLayerKindButton.enabled = toolExists(sourceFolder, "Debug_LayerKind.jsx");

    exporterButton.onClick = function () {
        dialog.close(1);
        runTool(sourceFolder, "PhotoshopUiPackageExporter.jsx", "UI Package Exporter");
    };

    namerButton.onClick = function () {
        dialog.close(1);
        runTool(sourceFolder, "PhotoshopLayerAutoNamer.jsx", "Layer Auto Namer");
    };

    spineButton.onClick = function () {
        dialog.close(1);
        runTool(sourceFolder, "PhotoshopToSpine.jsx", "Photoshop To Spine");
    };

    updaterButton.onClick = function () {
        dialog.close(1);
        runTool(sourceFolder, "PhotoshopUiPackageUpdater.jsx", "Update Plugin");
    };

    calibrationButton.onClick = function () {
        dialog.close(1);
        runTool(sourceFolder, "CreateCalibrationPsd.jsx", "Create Calibration PSD");
    };

    debugFontRoutingButton.onClick = function () {
        dialog.close(1);
        runTool(sourceFolder, "Debug_FontRouting.jsx", "Debug Font Routing");
    };

    debugFontSizeButton.onClick = function () {
        dialog.close(1);
        runTool(sourceFolder, "Debug_FontSize.jsx", "Debug Font Size");
    };

    debugLayerKindButton.onClick = function () {
        dialog.close(1);
        runTool(sourceFolder, "Debug_LayerKind.jsx", "Debug Layer Kind");
    };

    closeButton.onClick = function () {
        dialog.close(0);
    };

    dialog.show();
})();

function runTool(sourceFolder, fileName, title) {
    var script = new File(sourceFolder.fsName + "/" + fileName);
    if (!script.exists) {
        alert(title + " was not found.\n\nExpected file:\n" + script.fsName);
        return;
    }

    try {
        $.evalFile(script);
    } catch (e) {
        alert(
            title + " failed to start.\n\n" +
            "File:\n" + script.fsName + "\n\n" +
            "Error:\n" + e.message + "\n\n" +
            "Line: " + (e.line || "unknown")
        );
    }
}

function toolExists(sourceFolder, fileName) {
    return new File(sourceFolder.fsName + "/" + fileName).exists;
}

function resolveSourceFolder() {
    if (typeof PS_TO_UNITY_V2_SOURCE_FOLDER !== "undefined" && PS_TO_UNITY_V2_SOURCE_FOLDER) {
        return new Folder(PS_TO_UNITY_V2_SOURCE_FOLDER);
    }

    return Folder($.fileName).parent;
}

function resolveRepoRoot(sourceFolder) {
    if (typeof PS_TO_UNITY_V2_REPO_ROOT !== "undefined" && PS_TO_UNITY_V2_REPO_ROOT) {
        return new Folder(PS_TO_UNITY_V2_REPO_ROOT);
    }

    return sourceFolder.parent;
}

function readVersion(repoRoot) {
    // version.json is the single source of truth (see bump-version.ps1).
    var versionFile = new File(repoRoot.fsName + "/version.json");
    if (!versionFile.exists) {
        return "local";
    }

    try {
        versionFile.encoding = "UTF-8";
        versionFile.open("r");
        var content = versionFile.read();
        versionFile.close();

        var match = content.match(/"version"\s*:\s*"([^"]+)"/);
        if (match && match[1]) {
            return "v" + match[1];
        }
    } catch (e) {
        try {
            versionFile.close();
        } catch (ignored) {
        }
    }

    return "local";
}

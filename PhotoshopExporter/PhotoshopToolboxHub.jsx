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

    var buttons = dialog.add("group");
    buttons.orientation = "column";
    buttons.alignChildren = "fill";

    var exporterButton = buttons.add("button", undefined, "UI Package Exporter");
    var namerButton = buttons.add("button", undefined, "Layer Auto Namer");
    var spineButton = buttons.add("button", undefined, "Photoshop To Spine");
    var updaterButton = buttons.add("button", undefined, "Update / Refresh Plugin");

    var closeGroup = dialog.add("group");
    closeGroup.orientation = "row";
    closeGroup.alignment = "right";
    var closeButton = closeGroup.add("button", undefined, "Close", { name: "cancel" });

    spineButton.enabled = toolExists(sourceFolder, "PhotoshopToSpine.jsx");

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
    var readme = new File(repoRoot.fsName + "/README.md");
    if (!readme.exists) {
        return "local";
    }

    try {
        readme.encoding = "UTF-8";
        readme.open("r");
        var content = readme.read();
        readme.close();

        var match = content.match(/Current status:\s*([^\r\n]+)/i);
        if (match && match[1]) {
            return match[1];
        }
    } catch (e) {
        try {
            readme.close();
        } catch (ignored) {
        }
    }

    return "local";
}

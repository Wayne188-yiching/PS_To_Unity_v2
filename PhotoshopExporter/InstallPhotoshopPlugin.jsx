#target photoshop

(function () {
    var sourceFolder = Folder($.fileName).parent;
    var repoRoot = sourceFolder.parent;
    var installFolder = photoshopScriptsFolder();

    // Safety: refuse to run from inside the Photoshop Scripts folder. The
    // cleanup step below deletes stray tool copies there, which would destroy
    // the only copy if the user launched this installer from such a copy.
    if (isSameOrInsideFolder(sourceFolder, installFolder)) {
        alert(
            "This installer is running from the Photoshop Scripts folder:\n" +
            sourceFolder.fsName + "\n\n" +
            "Run it from the PS_To_Unity_v2 repository instead:\n" +
            "File > Scripts > Browse... > PhotoshopExporter/InstallPhotoshopPlugin.jsx"
        );
        return;
    }

    if (!ensureFolder(installFolder) || !canWriteToFolder(installFolder)) {
        var selected = Folder.selectDialog("Choose Photoshop Presets/Scripts folder for PS_To_Unity_v2 launcher");
        if (!selected) {
            alert("Install cancelled. Photoshop Scripts folder was not writable.");
            return;
        }
        installFolder = new Folder(selected.fsName);
        if (!ensureFolder(installFolder) || !canWriteToFolder(installFolder)) {
            alert("Cannot write to install folder:\n" + installFolder.fsName + "\n\nTry running Photoshop as administrator once, then run this installer again.");
            return;
        }
    }

    writeConfig(installFolder, sourceFolder, repoRoot);
    removeOldLaunchers(installFolder);
    var cleanedCount = removeStrayToolCopies(installFolder, sourceFolder);
    writeLauncher(installFolder, "PS To Unity v2.jsx", "PhotoshopToolboxHub.jsx", "PS To Unity v2");

    var cleanupNote = cleanedCount > 0
        ? "\n\nRemoved " + cleanedCount + " stray tool cop" + (cleanedCount === 1 ? "y" : "ies") +
          " from the Scripts folder.\nAll tools now run from the repository via the single menu entry."
        : "";

    alert(
        "PS_To_Unity_v2 Photoshop plugin installed.\n\n" +
        "Restart Photoshop, then open:\n" +
        "File > Scripts > PS To Unity v2\n\n" +
        "Installed folder:\n" + installFolder.fsName +
        cleanupNote
    );
})();

function photoshopScriptsFolder() {
    return new Folder(app.path.fsName + "/Presets/Scripts");
}

function writeConfig(installFolder, sourceFolder, repoRoot) {
    var file = new File(installFolder.fsName + "/PS_To_Unity_v2.config.jsxinc");
    var lines = [];
    lines.push("var PS_TO_UNITY_V2_SOURCE_FOLDER = " + quoteJsString(normalizePath(sourceFolder.fsName)) + ";");
    lines.push("var PS_TO_UNITY_V2_REPO_ROOT = " + quoteJsString(normalizePath(repoRoot.fsName)) + ";");
    writeTextFile(file, lines.join("\n"));
}

function removeOldLaunchers(installFolder) {
    var oldFiles = [
        "PS To Unity - UI Package Exporter.jsx",
        "PS To Unity - Layer Auto Namer.jsx",
        "PS To Unity - Update Plugin.jsx"
    ];

    for (var i = 0; i < oldFiles.length; i++) {
        removeFile(new File(installFolder.fsName + "/" + oldFiles[i]));
        removeFile(new File(installFolder.fsName + "/PS_To_Unity_v2/" + oldFiles[i]));
    }
}

function removeFile(file) {
    try {
        if (file.exists) {
            file.remove();
        }
    } catch (e) {
    }
}

// Remove stray copies of our tool scripts that users copied directly into
// Presets/Scripts (the pre-v2.6.1 install instructions said to copy the whole
// PhotoshopExporter folder, cluttering the File > Scripts menu). Matches exact
// known filenames only; never touches the launcher, the config, or any
// third-party script. Returns the number of files removed.
function removeStrayToolCopies(installFolder, sourceFolder) {
    // Guard duplicated here in case this function is ever called directly.
    if (isSameOrInsideFolder(sourceFolder, installFolder)) {
        return 0;
    }

    var toolFiles = [
        "CreateCalibrationPsd.jsx",
        "Debug_FontRouting.jsx",
        "Debug_FontSize.jsx",
        "Debug_LayerKind.jsx",
        "InstallPhotoshopPlugin.jsx",
        "PhotoshopLayerAutoNamer.jsx",
        "PhotoshopToSpine.jsx",
        "PhotoshopToolboxHub.jsx",
        "PhotoshopUiPackageExporter.jsx",
        "PhotoshopUiPackageUpdater.jsx"
    ];

    var removed = 0;
    for (var i = 0; i < toolFiles.length; i++) {
        var stray = new File(installFolder.fsName + "/" + toolFiles[i]);
        if (stray.exists) {
            try {
                if (stray.remove()) {
                    removed++;
                }
            } catch (e) {
            }
        }
    }
    return removed;
}

function isSameOrInsideFolder(child, parent) {
    var childPath = String(child.fsName).toLowerCase().replace(/\\/g, "/");
    var parentPath = String(parent.fsName).toLowerCase().replace(/\\/g, "/");
    return childPath === parentPath || childPath.indexOf(parentPath + "/") === 0;
}

function writeLauncher(installFolder, launcherName, sourceScriptName, title) {
    var file = new File(installFolder.fsName + "/" + launcherName);
    var lines = [];
    lines.push("#target photoshop");
    lines.push("");
    lines.push("(function () {");
    lines.push("    var root = Folder($.fileName).parent;");
    lines.push("    var configFile = new File(root.fsName + '/PS_To_Unity_v2.config.jsxinc');");
    lines.push("    if (!configFile.exists) {");
    lines.push("        alert('PS_To_Unity_v2 config is missing. Run InstallPhotoshopPlugin.jsx again.');");
    lines.push("        return;");
    lines.push("    }");
    lines.push("    $.evalFile(configFile);");
    lines.push("    var sourceFolder = new Folder(PS_TO_UNITY_V2_SOURCE_FOLDER);");
    lines.push("    var sourceScript = new File(sourceFolder.fsName + '/" + sourceScriptName + "');");
    lines.push("    if (!sourceScript.exists) {");
    lines.push("        alert('" + title + " was not found.\\n\\nExpected file:\\n' + sourceScript.fsName + '\\n\\nRun PS To Unity - Update Plugin or reinstall the plugin.');");
    lines.push("        return;");
    lines.push("    }");
    lines.push("    $.evalFile(sourceScript);");
    lines.push("})();");
    writeTextFile(file, lines.join("\n"));
}

function ensureFolder(folder) {
    if (!folder.exists && !folder.create()) {
        return false;
    }
    return true;
}

function canWriteToFolder(folder) {
    var probe = new File(folder.fsName + "/.ps_to_unity_write_test.tmp");
    try {
        probe.open("w", "TEXT");
        probe.write("ok");
        probe.close();
        probe.remove();
        return true;
    } catch (e) {
        try {
            probe.close();
        } catch (ignored) {
        }
        try {
            probe.remove();
        } catch (ignored2) {
        }
        return false;
    }
}

function writeTextFile(file, content) {
    file.encoding = "UTF-8";
    file.open("w", "TEXT");
    file.lineFeed = "\n";
    file.write(content);
    file.close();
}

function quoteJsString(value) {
    return '"' + String(value).replace(/\\/g, "\\\\").replace(/"/g, '\\"') + '"';
}

function normalizePath(value) {
    return String(value).replace(/\\/g, "/");
}

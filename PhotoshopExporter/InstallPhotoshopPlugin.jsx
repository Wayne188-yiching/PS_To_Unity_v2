#target photoshop

(function () {
    var sourceFolder = Folder($.fileName).parent;
    var repoRoot = sourceFolder.parent;
    var installFolder = photoshopScriptsFolder();

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
    writeLauncher(installFolder, "PS To Unity v2.jsx", "PhotoshopToolboxHub.jsx", "PS To Unity v2");

    alert(
        "PS_To_Unity_v2 Photoshop plugin installed.\n\n" +
        "Restart Photoshop, then open:\n" +
        "File > Scripts > PS To Unity v2\n\n" +
        "Installed folder:\n" + installFolder.fsName
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

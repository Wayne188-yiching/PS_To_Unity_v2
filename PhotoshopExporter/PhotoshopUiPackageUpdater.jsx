#target photoshop

(function () {
    var sourceFolder = resolveSourceFolder();
    var repoRoot = resolveRepoRoot(sourceFolder);
    var installScript = new File(sourceFolder.fsName + "/InstallPhotoshopPlugin.jsx");
    var canGitPull = new Folder(repoRoot.fsName + "/.git").exists;

    var dialog = new Window("dialog", "PS_To_Unity_v2 Update Plugin");
    dialog.orientation = "column";
    dialog.alignChildren = "fill";

    var info = dialog.add("statictext", undefined, "Update the local PS_To_Unity_v2 source, then refresh Photoshop menu launchers.");
    info.characters = 78;

    var sourceText = dialog.add("statictext", undefined, "Source: " + sourceFolder.fsName);
    sourceText.characters = 78;

    var gitPull = dialog.add("checkbox", undefined, "Read latest version with git pull --ff-only");
    gitPull.value = canGitPull;
    gitPull.enabled = canGitPull;

    var reinstall = dialog.add("checkbox", undefined, "Refresh fixed Photoshop menu entries");
    reinstall.value = true;

    if (!canGitPull) {
        var noGit = dialog.add("statictext", undefined, "No .git folder was found. The updater will only refresh Photoshop menu entries.");
        noGit.characters = 78;
    }

    var buttons = dialog.add("group");
    buttons.orientation = "row";
    buttons.alignment = "right";
    var cancelButton = buttons.add("button", undefined, "Cancel", { name: "cancel" });
    var updateButton = buttons.add("button", undefined, "Update", { name: "ok" });

    cancelButton.onClick = function () {
        dialog.close(0);
    };

    updateButton.onClick = function () {
        dialog.close(1);
    };

    if (dialog.show() !== 1) {
        return;
    }

    var messages = [];

    if (gitPull.value) {
        messages.push(runGitPull(repoRoot));
    }

    if (reinstall.value) {
        if (!installScript.exists) {
            alert("InstallPhotoshopPlugin.jsx was not found.\n\nExpected file:\n" + installScript.fsName);
            return;
        }
        $.evalFile(installScript);
        messages.push("Photoshop menu entries refreshed.");
    }

    if (messages.length > 0) {
        alert(messages.join("\n\n"));
    }
})();

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

function runGitPull(repoRoot) {
    if (typeof system === "undefined" || !system.callSystem) {
        return "Photoshop cannot run system commands in this environment. Pull the repository manually, then run this updater again.";
    }

    var command;
    if (isWindows()) {
        command = 'cmd /c cd /d "' + repoRoot.fsName + '" && git pull --ff-only';
    } else {
        command = "/bin/sh -lc " + quoteShell("cd " + quoteShellPath(repoRoot.fsName) + " && git pull --ff-only");
    }

    var output = system.callSystem(command);
    if (!output) {
        output = "git pull completed with no output.";
    }

    return "Update source result:\n" + output;
}

function isWindows() {
    return String($.os).toLowerCase().indexOf("windows") >= 0;
}

function quoteShell(value) {
    return "'" + String(value).replace(/'/g, "'\\''") + "'";
}

function quoteShellPath(value) {
    return quoteShell(String(value));
}

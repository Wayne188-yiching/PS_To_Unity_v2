#target photoshop

var SCRIPT_VERSION = "2.12.3";
var GITHUB_JSX_RAW_URL = "https://raw.githubusercontent.com/Wayne188-yiching/PS_To_Unity_v2/main/PhotoshopExporter/PhotoshopUiPackageExporter.jsx";

// OPTIMIZATION_PLAN_zh.html#phase4-5-q10：統一方括號標籤註冊表（Phase 4 Q8 預告的 refactor）。
// 新增標籤 = 在這裡加一行 regex；名稱清理一律走 stripKnownTags 單一出口，
// 避免「A 處理過、B 沒處理」的錯位 bug（BTN_ bug 的病根）。各功能的偵測邏輯不在此，各自查表。
//
// v2.11.2：這個定義必須放在下方主 IIFE「之前」。function declaration 會整個 hoist，
// 但 var 的「賦值」不會 —— 定義若留在檔案後段，匯出流程（IIFE 內）呼叫 stripKnownTags 時
// 本陣列仍是 undefined，任何含 group 的 PSD 一匯出就爆「undefined 不是物件」（PS COM 實測）。
var KNOWN_BRACKET_TAG_PATTERNS = [
    /\[(?:H|HLAYOUT|V|VLAYOUT)\]/ig,                                            // H/V LayoutGroup（#phase4-decisions Q8）
    /\[(?:GRID|GLAYOUT)\]/ig,                                                   // GridLayoutGroup（#phase4-decisions Q8）
    /\[(?:CG|CANVASGROUP)\]/ig,                                                 // CanvasGroup（#phase4-decisions Q8）
    /\[(?:SCROLL_V|SCROLL_H)\]/ig,                                              // ScrollRect（#phase4-5-q1）
    /\[THICK\s*:\s*-?\d+(?:\.\d+)?\s*(?::\s*-?\d+(?:\.\d+)?)?\s*\]/ig           // 假厚度文字
];

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
        // U13：詳細統計改寫到 export_report.txt（可回看），alert 只給一句摘要 + 報告位置
        var dedupLine = "";
        if (result.dedupStats && result.dedupStats.dedupedCount > 0) {
            dedupLine = "\n像素去重合併：" + result.dedupStats.dedupedCount + " 張（省 " + formatBytes(result.dedupStats.savedBytes) + "）";
        }
        var summary =
            "UI Package 匯出完成。\n\n" +
            "圖層：" + result.imageCount + "　文字：" + result.textCount + "　群組：" + result.groupCount + dedupLine + "\n\n" +
            "詳細報告：\n" + result.reportFile.fsName;
        alert(summary);
    } catch (e) {
        alert("UI Package 匯出失敗。\n\n錯誤：" + e.message);
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

// v2.10：命名規則速查——集中列出所有會觸發 Unity 端行為的圖層命名約定。
function showNamingHelpDialog() {
    var text = ""
        + "── 群組標籤（自動掛 Unity Component）──\n"
        + "[H] / [HLAYOUT]         Horizontal Layout Group\n"
        + "[V] / [VLAYOUT]         Vertical Layout Group\n"
        + "[GRID] / [GLAYOUT]      Grid Layout Group（子圖層寬高差 >20% 自動降級成普通群組並警告）\n"
        + "[CG] / [CANVASGROUP]    Canvas Group（Prefab 根節點免標籤、一律自動掛）\n"
        + "[SCROLL_V] / [SCROLL_H] ScrollRect 滾動區（Unity 自動組 ScrollView > Viewport > Content 三層；\n"
        + "                        兩者可同標 = 雙向捲動。群組自身的遮色片 = 可視窗範圍；\n"
        + "                        群組內圖層的遮色片會自動忽略、匯出完整圖，裁切交給 Unity 端）\n"
        + "                        可與 [V]/[H]/[GRID] 組合：排版元件會掛在 Content 上\n"
        + "[THICK:下偏移:右偏移]    假厚度文字（Unity 產出上下兩層 TMP）\n"
        + "\n"
        + "── 前綴 ──\n"
        + "BTN_              此圖層在 Unity 自動掛 Button（可點擊）\n"
        + "IGNORE_ / REF_    匯出時整層略過（需勾「略過 IGNORE_ 與 REF_ 圖層」）\n"
        + "\n"
        + "── 文字圖層輸出覆寫（優先於「預設文字圖層處理」下拉）──\n"
        + "轉 PNG 圖片：[PNG] [IMAGE] [IMG]，或前綴 TXTIMG_ / TXT_IMG_ / TEXTIMG_ / TEXT_IMG_\n"
        + "保持 TMP 文字：[TMP] [TEXT]，或前綴 TMP_ / TXT_\n"
        + "\n"
        + "── 錨點 token（需在 Unity Importer 勾「啟用響應式 anchor」）──\n"
        + "STRETCH_FULL / STRETCH_X / STRETCH_Y\n"
        + "ANCHOR_TL / ANCHOR_TR / ANCHOR_BL / ANCHOR_BR\n"
        + "ANCHOR_TOP / ANCHOR_BOTTOM / ANCHOR_LEFT / ANCHOR_RIGHT / ANCHOR_CENTER\n"
        + "PIVOT_TL / PIVOT_TR / PIVOT_BL / PIVOT_BR（覆寫預設 pivot）\n"
        + "\n"
        + "── 通則 ──\n"
        + "・標籤不分大小寫、可寫在圖層名任意位置，可組合：Cards[GRID][CG]\n"
        + "・標籤與前綴會自動從 Unity 節點名移除（BTN_ 會保留在節點名）\n"
        + "・未知 [標籤] 一律放行不報錯（可自由用 [Hover] 等描述性標記）";

    var dialog = new Window("dialog", "圖層命名規則速查　v" + SCRIPT_VERSION);
    dialog.orientation = "column";
    dialog.alignChildren = "fill";
    var body = dialog.add("edittext", undefined, text, { multiline: true, scrolling: true, readonly: true });
    body.preferredSize = [660, 400];
    var closeButton = dialog.add("button", undefined, "關閉", { name: "ok" });
    closeButton.alignment = "right";
    closeButton.onClick = function () {
        dialog.close(1);
    };
    dialog.show();
}

// v2.10：TMP 字型白名單編輯——白名單內字型保持 TMP，不會被「白名單外字型自動匯出為 PNG」轉成圖。
function showFontWhitelistDialog(doc) {
    var dialog = new Window("dialog", "TMP 字型白名單");
    dialog.orientation = "column";
    dialog.alignChildren = "fill";

    var info = dialog.add("statictext", undefined,
        "一行一個關鍵字，比對字型 PostScript 名稱（不分大小寫、部分符合即生效）。\n" +
        "內建的思源／源泉／Noto CJK 系列不用列。範例：DFHei 可涵蓋華康黑體全系列。\n" +
        "注意：保持 TMP 的字型，需在 Unity Importer 的「字型對應表 TmpFontMap」建立對應 Font Asset。",
        { multiline: true });
    info.characters = 76;

    var detected = collectDocumentTextFonts(doc);
    var detectPanel = dialog.add("panel", undefined, "本文件偵測到的字型（雙擊或按「加入」放進白名單）");
    detectPanel.orientation = "column";
    detectPanel.alignChildren = "fill";
    var fontList = detectPanel.add("listbox", undefined, detected, { multiselect: true });
    fontList.preferredSize.height = 110;
    var addGroup = detectPanel.add("group");
    addGroup.orientation = "row";
    var addSelectedButton = addGroup.add("button", undefined, "加入選取字型");
    var addAllButton = addGroup.add("button", undefined, "全部加入");

    var listPanel = dialog.add("panel", undefined, "白名單關鍵字（一行一個）");
    listPanel.orientation = "column";
    listPanel.alignChildren = "fill";
    var keywordText = listPanel.add("edittext", undefined, loadUserTmpFontKeywords().join("\n"),
        { multiline: true, scrolling: true });
    keywordText.preferredSize.height = 110;

    function appendKeyword(value) {
        var current = trim(keywordText.text);
        var lines = current ? current.split(/\r?\n/) : [];
        var slug = normalizeAsciiSlug(value);
        for (var i = 0; i < lines.length; i++) {
            if (normalizeAsciiSlug(lines[i]) === slug) {
                return;
            }
        }
        keywordText.text = current ? current + "\n" + value : value;
    }

    addSelectedButton.onClick = function () {
        if (!fontList.selection) {
            return;
        }
        for (var i = 0; i < fontList.selection.length; i++) {
            appendKeyword(fontList.selection[i].text);
        }
    };
    addAllButton.onClick = function () {
        for (var i = 0; i < detected.length; i++) {
            appendKeyword(detected[i]);
        }
    };
    fontList.onDoubleClick = function () {
        if (fontList.selection && fontList.selection.length) {
            appendKeyword(fontList.selection[0].text);
        }
    };

    var buttons = dialog.add("group");
    buttons.orientation = "row";
    buttons.alignment = "right";
    var cancelButton = buttons.add("button", undefined, "取消", { name: "cancel" });
    var saveButton = buttons.add("button", undefined, "儲存", { name: "ok" });
    cancelButton.onClick = function () {
        dialog.close(0);
    };
    saveButton.onClick = function () {
        var lines = keywordText.text.split(/\r?\n/);
        var cleaned = [];
        for (var i = 0; i < lines.length; i++) {
            var line = trim(lines[i]);
            if (line) {
                cleaned.push(line);
            }
        }
        if (saveUserTmpFontKeywords(cleaned)) {
            dialog.close(1);
        } else {
            alert("寫入白名單檔案失敗：" + userTmpFontWhitelistFile().fsName);
        }
    };

    dialog.show();
}

function showExportDialog(doc) {
    var dialog = new Window("dialog", "Photoshop UI Package Exporter");
    dialog.orientation = "column";
    dialog.alignChildren = "fill";

    // Header（U9 中文化）
    var headerGroup = dialog.add("group");
    headerGroup.orientation = "row";
    headerGroup.alignment = "fill";
    headerGroup.alignChildren = ["fill", "center"];
    var versionLabel = headerGroup.add("statictext", undefined, "v" + SCRIPT_VERSION);
    versionLabel.justify = "left";
    var helpButton = headerGroup.add("button", undefined, "命名規則說明");
    helpButton.alignment = "right";
    helpButton.onClick = function () {
        showNamingHelpDialog();
    };
    var updateButton = headerGroup.add("button", undefined, "檢查更新");
    updateButton.alignment = "right";
    updateButton.onClick = function () {
        var updated = checkAndUpdateScript();
        if (updated) {
            dialog.close(0);
        }
    };

    var intro = dialog.add("statictext", undefined, "把非文字圖層輸出為 PNG + 命名 Layout JSON 給 Unity。文字圖層維持為 TMP 節點。");
    intro.characters = 82;

    var defaultImageOutput = defaultImageFolder(doc);
    var defaultLayoutOutput = defaultLayoutJsonFile(doc, defaultImageOutput);
    var layoutPathTouched = false;

    var imageFolderGroup = dialog.add("group");
    imageFolderGroup.orientation = "row";
    imageFolderGroup.alignChildren = ["fill", "center"];
    imageFolderGroup.add("statictext", undefined, "PNG 輸出資料夾：");
    var imageFolderText = imageFolderGroup.add("edittext", undefined, defaultImageOutput.fsName);
    imageFolderText.characters = 56;
    var browseImageButton = imageFolderGroup.add("button", undefined, "選擇…");

    var unityPanel = dialog.add("panel", undefined, "Unity Atlas 輸出");
    unityPanel.orientation = "column";
    unityPanel.alignChildren = "left";

    var useUnityAtlas = unityPanel.add("checkbox", undefined, "輸出到 Unity Atlas/SpriteAtlas 語言資料夾");
    useUnityAtlas.value = false;

    var languageGroup = unityPanel.add("group");
    languageGroup.orientation = "row";
    languageGroup.alignChildren = ["left", "center"];
    languageGroup.add("statictext", undefined, "語言資料夾：");
    var languageList = languageGroup.add("dropdownlist", undefined, ["Base", "CHS", "CHT", "EN"]);
    languageList.selection = 0;
    languageList.enabled = false;

    var unityNote = unityPanel.add("statictext", undefined, "啟用後 PNG 輸出資料夾視為 Unity 套件根目錄，PNG 將進 Atlas/SpriteAtlas/{Language}。");
    unityNote.characters = 82;

    useUnityAtlas.onClick = function () {
        languageList.enabled = useUnityAtlas.value;
    };

    var optionPanel = dialog.add("panel", undefined, "匯出選項");
    optionPanel.orientation = "column";
    optionPanel.alignChildren = "left";

    var ignoreHidden = optionPanel.add("checkbox", undefined, "忽略隱藏 / 已關閉的圖層");
    ignoreHidden.value = true;

    var skipReference = optionPanel.add("checkbox", undefined, "略過 IGNORE_ 與 REF_ 圖層");
    skipReference.value = true;

    var useExportCache = optionPanel.add("checkbox", undefined, "使用匯出快取（未變更的 PNG 跳過）");
    useExportCache.value = true;

    // U11：移除「Use fast layer duplicate」選項，改成永遠先試 fast、失敗自動退 merged-copy

    var textOutputGroup = optionPanel.add("group");
    textOutputGroup.orientation = "row";
    textOutputGroup.alignChildren = ["left", "center"];
    textOutputGroup.add("statictext", undefined, "預設文字圖層處理：");
    var textOutputList = textOutputGroup.add("dropdownlist", undefined, ["TMP 文字節點", "PNG 圖片"]);
    textOutputList.selection = 0;

    var selectedTextAsImage = optionPanel.add("checkbox", undefined, "把目前選取的文字圖層強制輸出為 PNG");
    selectedTextAsImage.value = false;

    // U12：顯示目前選取的文字圖層數與名稱，讓使用者勾選前就知道會影響哪些圖層
    var selectedTextSummary = summarizeSelectedTextLayers(doc);
    var selectedSummaryText = selectedTextSummary.count > 0
        ? "目前選取：" + selectedTextSummary.count + " 個文字圖層（" + selectedTextSummary.names.join("、") + "）"
        : "目前選取：0 個文字圖層";
    var selectedSummaryLabel = optionPanel.add("statictext", undefined, selectedSummaryText);
    selectedSummaryLabel.characters = 82;

    // v2.10：白名單 = 內建思源／源泉系列 + 使用者自訂關鍵字（編輯對話框維護，存於使用者資料夾）。
    var fontRouteGroup = optionPanel.add("group");
    fontRouteGroup.orientation = "row";
    fontRouteGroup.alignChildren = ["left", "center"];
    var autoRouteNonSourceHanFonts = fontRouteGroup.add("checkbox", undefined, "白名單外字型改為 PNG（預設關閉：所有文字保留 TMP）");
    autoRouteNonSourceHanFonts.value = false;
    var editFontWhitelistButton = fontRouteGroup.add("button", undefined, "編輯字型白名單…");
    editFontWhitelistButton.onClick = function () {
        showFontWhitelistDialog(doc);
    };

    var note = optionPanel.add("statictext", undefined, "勾選「目前選取的文字圖層為 PNG」前，請先在 Photoshop 圖層面板選好要轉的文字圖層。");
    note.characters = 82;

    // U10：Layout JSON 路徑收進進階折疊區，預設藏起來。
    var advCheckbox = dialog.add("checkbox", undefined, "顯示進階設定（自訂 Layout JSON 路徑）");
    advCheckbox.value = false;

    var layoutGroup = dialog.add("group");
    layoutGroup.orientation = "row";
    layoutGroup.alignChildren = ["fill", "center"];
    layoutGroup.add("statictext", undefined, "Layout JSON 檔案：");
    var layoutText = layoutGroup.add("edittext", undefined, defaultLayoutOutput.fsName);
    layoutText.characters = 56;
    var browseLayoutButton = layoutGroup.add("button", undefined, "選擇…");
    layoutGroup.visible = false;

    advCheckbox.onClick = function () {
        layoutGroup.visible = advCheckbox.value;
        dialog.layout.layout(true);
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
        var selected = Folder.selectDialog("選擇 PNG 輸出資料夾");
        if (selected) {
            imageFolderText.text = selected.fsName;
            if (!layoutPathTouched) {
                layoutText.text = defaultLayoutJsonFile(doc, selected).fsName;
            }
        }
    };

    browseLayoutButton.onClick = function () {
        var selected = File.saveDialog("選擇 Layout JSON 檔案", "JSON:*.json");
        if (selected) {
            layoutPathTouched = true;
            layoutText.text = ensureJsonExtension(selected.fsName);
        }
    };

    var buttons = dialog.add("group");
    buttons.orientation = "row";
    buttons.alignment = "right";
    var cancelButton = buttons.add("button", undefined, "取消", { name: "cancel" });
    var exportButton = buttons.add("button", undefined, "匯出 UI Package", { name: "ok" });

    cancelButton.onClick = function () {
        dialog.close(0);
    };

    exportButton.onClick = function () {
        var imageFolderValue = trim(imageFolderText.text);
        var layoutJsonValue = ensureJsonExtension(trim(layoutText.text));

        if (!imageFolderValue) {
            alert("請選擇 PNG 輸出資料夾。");
            return;
        }

        if (!layoutJsonValue) {
            alert("請選擇 Layout JSON 檔案。");
            return;
        }

        var imageFolder = resolveImageOutputFolder({
            imageFolder: imageFolderValue,
            useUnityAtlasStructure: useUnityAtlas.value,
            atlasLanguage: languageList.selection ? languageList.selection.text : "Base"
        });
        var layoutJsonFile = new File(layoutJsonValue);

        if (isPathInsideFolder(layoutJsonFile, imageFolder) && !isSameFolder(layoutJsonFile.parent, imageFolder)) {
            alert("Layout JSON 檔案不能放在圖片子資料夾內。");
            return;
        }

        dialog.result = {
            imageFolder: imageFolderValue,
            layoutJsonFile: layoutJsonValue,
            ignoreHiddenLayers: ignoreHidden.value,
            skipReferenceLayers: skipReference.value,
            useExportCache: useExportCache.value,
            // U11：fast duplicate 不再給 UI 選項，固定 true（內部已會自動 fallback 到 merged-copy）
            useFastLayerDuplicate: true,
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

// U12 輔助：在開對話框前先掃一次目前選取的文字圖層
function summarizeSelectedTextLayers(doc) {
    var result = { count: 0, names: [] };
    try {
        var idMap = readSelectedLayerIdMap(doc);
        // idMap 是 {id: true}，把圖層走一遍對名稱
        walk(doc.layers);
        function walk(layers) {
            for (var i = 0; i < layers.length; i++) {
                var layer = layers[i];
                if (layer.typename === "LayerSet") {
                    walk(layer.layers);
                    continue;
                }
                if (layer.typename !== "ArtLayer") continue;
                if (!isTextLayer(layer)) continue;
                if (idMap[layer.id]) {
                    result.count++;
                    if (result.names.length < 5) {
                        result.names.push(layer.name);
                    } else if (result.names.length === 5) {
                        result.names.push("…");
                    }
                }
            }
        }
    } catch (e) {}
    return result;
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
            // v2.10.1：TMP 是預設路徑；只有使用者明確勾選時才依白名單把文字烘成 PNG。
            autoRouteNonSourceHanFonts: options.autoRouteNonSourceHanFonts === true,
            // v2.10：使用者自訂 TMP 字型白名單（一行一關鍵字，比對 normalizeAsciiSlug 後 indexOf）
            tmpFontKeywords: loadUserTmpFontKeywords(),
            sourceModified: readDocumentModified(sourceDoc),
            exportCache: null,
            exportCacheDirty: false,
            counters: {},
            warnings: [],
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
        // Phase 3：先做像素雜湊去重，再寫 cache + buildLayoutJson，這樣 cache 與 JSON 都會反映去重後的最終狀態
        var dedupStats = dedupPngsByHash(pendingImages, imageFolder, context);
        writeExportCacheIfDirty(context);
        refreshGroupBounds(nodes, context);

        var layout = buildLayoutJson(sourceDoc, nodes, context.warnings);
        writeTextFile(layoutJsonFile, layout);

        // Phase 3：產生 export_report.txt 取代原本一閃即逝的 alert
        var textStats = countTextStats(nodes);
        var pngStats = scanPngFolderStats(imageFolder);
        var reportFile = exportReportFile(imageFolder);
        var resultObj = {
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
            skipClipping: context.skipClipping,
            outlineTextCount: textStats.outlineTextCount,
            gradientTextCount: textStats.gradientTextCount,
            dedupStats: dedupStats,
            pngStats: pngStats,
            reportFile: reportFile
        };
        writeExportReport(reportFile, resultObj, dedupStats, pngStats);
        return resultObj;
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

            // OPTIMIZATION_PLAN_zh.html#phase4-5-q2：進入 [SCROLL_*] group 時記深度，
            // 內部節點改走「boundsNoMask + 不 clamp 畫布 + 去 mask 匯出」語意。
            var scrollTag = detectScrollDirection(layer.name);
            if (scrollTag) {
                context.scrollDepth = (context.scrollDepth || 0) + 1;
            }
            var childParentBounds = groupBounds || parentBounds;
            var childNodes = collectNodes(layer, effectiveVisible, context, pendingImages, childParentBounds);
            if (scrollTag) {
                context.scrollDepth--;
            }
            if (childNodes.length > 0) {
                var groupNode = createGroupNode(layer, childNodes, context, parentBounds, groupBounds);
                if (groupNode) {
                    nodes.push(groupNode);
                    context.groupCount++;
                } else {
                    appendNodes(nodes, childNodes);
                }
            } else if (scrollTag) {
                // OPTIMIZATION_PLAN_zh.html#phase4-5-q8：scroll group 沒有任何可匯出子節點 → 降級消失 + 警告。
                pushWarning(context, String(layer.name || ""), "SCROLL_EMPTY",
                    "[SCROLL] 群組內沒有任何可匯出的子圖層，已降級忽略；請確認圖層可見性與命名。");
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

    var insideScroll = (context.scrollDepth || 0) > 0;
    if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
        bounds = boundsFromChildren(children, context.doc, insideScroll);
    }

    if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
        return null;
    }

    // OPTIMIZATION_PLAN_zh.html#phase4-5-q10：uniqueNodeName 內部已走 stripKnownTags 全清理，
    // 這裡直接傳原始名即可（節點名不會殘留 [H]/[CG]/[GRID]/[SCROLL_*] 等標籤）。
    var node = {
        name: uniqueNodeName(layerSet.name, context.counters),
        type: "group",
        x: bounds.left,
        y: bounds.top,
        width: bounds.width,
        height: bounds.height,
        visible: true,
        children: children
    };

    if (insideScroll) {
        // scroll 內的巢狀 group：bounds 不 clamp 畫布，並帶 _visibleBounds 供外層 viewport 聯集。
        node._insideScroll = true;
        node._visibleBounds = visibleBoundsFromChildren(children, context.doc);
    }

    applyLayoutMetadata(node, bounds, parentBounds, layerSet.name);
    // OPTIMIZATION_PLAN_zh.html#phase4-5-q7：先算 scroll（會改寫 node 為 viewport rect + content 欄位），
    // grid 參數（padding / sort / constraint 方向）要以 content bounds 為基準。
    var scrollContentBounds = applyScrollMetadata(node, layerSet, children, bounds, context);
    applyLayoutGroupMetadata(node, layerSet, children, scrollContentBounds || bounds, context);
    applyCanvasGroupMetadata(node, layerSet);
    node._parentBounds = parentBounds;
    node._rawName = layerSet.name;
    return node;
}

// OPTIMIZATION_PLAN_zh.html#phase4-5-q2：scroll 群組的 viewport / content 推導（C+ 自動語意）。
// 回傳 content bounds（供 grid/padding 計算），非 scroll 群組回傳 null。
// viewport 優先序：group 自身遮色片（裁切後 boundsNoEffects）→ children 可視 bounds 聯集 → group bounds。
function applyScrollMetadata(node, layerSet, children, bounds, context) {
    var rawName = typeof layerSet === "string" ? layerSet : (layerSet && layerSet.name);
    var direction = detectScrollDirection(rawName);
    if (!direction) {
        return null;
    }

    var doc = context && context.doc ? context.doc : null;
    // content = children 完整 bounds 聯集（不 clamp 畫布；scroll 內容本可超出畫面）
    var content = boundsFromChildren(children, doc, true) || bounds;

    var viewport = null;
    if (layerSet && typeof layerSet !== "string" && layerHasEnabledMask(layerSet)) {
        // 已在使用者 PS 實測：boundsNoEffects 會套用遮色片、排除效果外擴 → 即為可視窗範圍。
        viewport = readLayerBoundsNoEffects(layerSet);
        if (viewport && doc) {
            viewport = clampBoundsToCanvas(viewport, doc);
        }
    }
    if (!viewport) {
        viewport = visibleBoundsFromChildren(children, doc);
    }
    if (!viewport) {
        viewport = bounds;
    }

    node.scrollDirection = direction;
    node.x = viewport.left;
    node.y = viewport.top;
    node.width = viewport.width;
    node.height = viewport.height;
    node.contentX = content.left;
    node.contentY = content.top;
    node.contentWidth = content.width;
    node.contentHeight = content.height;

    // OPTIMIZATION_PLAN_zh.html#phase4-5-q7：排列軸 ⊥ 捲動軸 → 照標籤掛，但提醒內容不會往捲動方向長。
    var layoutType = detectLayoutGroupType(rawName, children);
    if ((direction === "vertical" && layoutType === "horizontal") ||
        (direction === "horizontal" && layoutType === "vertical")) {
        pushWarning(context, node.name, "SCROLL_AXIS_MISMATCH",
            "排列方向（" + layoutType + "）與捲動方向（" + direction + "）垂直，內容不會往捲動方向增長；請確認標籤組合是否符合預期。");
    }

    return content;
}

function refreshGroupBounds(nodes, context) {
    var doc = context && context.doc ? context.doc : context; // 保留舊呼叫端傳 doc 的相容
    for (var i = 0; i < nodes.length; i++) {
        var node = nodes[i];
        if (!node || node.type !== "group") {
            continue;
        }

        refreshGroupBounds(node.children || [], context);

        // OPTIMIZATION_PLAN_zh.html#phase4-5-q2：scroll 群組——viewport（node.x/y/w/h）在收集期已定，
        // trim 不會移動遮色片幾何，保持不動；content 依 trim 後的 children 重新聯集（不 clamp）。
        if (node.scrollDirection) {
            var refreshedContent = boundsFromChildren(node.children || [], doc, true);
            if (refreshedContent) {
                node.contentX = refreshedContent.left;
                node.contentY = refreshedContent.top;
                node.contentWidth = refreshedContent.width;
                node.contentHeight = refreshedContent.height;
            }
            var viewportRect = {
                left: node.x,
                top: node.y,
                right: node.x + node.width,
                bottom: node.y + node.height,
                width: node.width,
                height: node.height
            };
            applyLayoutMetadata(node, viewportRect, node._parentBounds, node._rawName);
            applyLayoutGroupMetadata(node, node._rawName, node.children || [], refreshedContent || viewportRect, context && context.doc ? context : null);
            continue;
        }

        var bounds = boundsFromChildren(node.children || [], doc, node._insideScroll === true);
        if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
            continue;
        }

        node.x = bounds.left;
        node.y = bounds.top;
        node.width = bounds.width;
        node.height = bounds.height;
        applyLayoutMetadata(node, bounds, node._parentBounds, node._rawName);
        applyLayoutGroupMetadata(node, node._rawName, node.children || [], bounds, context && context.doc ? context : null);
    }
}

function boundsFromChildren(children, doc, noClamp) {
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

    var union = {
        left: left,
        top: top,
        right: right,
        bottom: bottom,
        width: Math.max(0, right - left),
        height: Math.max(0, bottom - top)
    };
    // OPTIMIZATION_PLAN_zh.html#phase4-5-q2：scroll 內容允許超出畫布（noClamp=true），其餘維持 clamp。
    if (noClamp) {
        return union;
    }
    return clampBoundsToCanvas(union, doc);
}

// viewport 用：聯集 children 的「可視 bounds」（遮色片裁切後）；沒有 _visibleBounds 的 child 用其節點 bounds。
function visibleBoundsFromChildren(children, doc) {
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
        var rect = child._visibleBounds || {
            left: child.x,
            top: child.y,
            right: child.x + child.width,
            bottom: child.y + child.height
        };
        left = Math.min(left, rect.left);
        top = Math.min(top, rect.top);
        right = Math.max(right, rect.right);
        bottom = Math.max(bottom, rect.bottom);
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

    // OPTIMIZATION_PLAN_zh.html#phase4-5-q2/q9：scroll 內用未裁切完整 bounds，且不 clamp 畫布
    //（scroll 內容本可超出畫面，超出部分 runtime 由 Viewport 的 RectMask2D 裁切）。
    var insideScroll = (context.scrollDepth || 0) > 0;
    var bounds = insideScroll ? readLayerBoundsNoMask(layer) : readLayerBounds(layer);
    if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
        return null;
    }

    if (!insideScroll) {
        bounds = clampBoundsToCanvas(bounds, context.doc);
        if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
            return null;
        }
    }

    var safeName = uniqueFileName(layer.name, context.counters);
    var fileName = safeName + ".png";
    // v2.10：BTN_ 前綴保留在節點名（Unity 端據此自動掛 Button），PNG 檔名維持去前綴不變（匯出快取相容）。
    var nodeName = startsWith(String(layer.name || "").toUpperCase(), "BTN_") ? "BTN_" + safeName : safeName;

    var node = {
        name: nodeName,
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
    node._parentBounds = parentBounds;
    node._rawName = layer.name;

    if (insideScroll) {
        // OPTIMIZATION_PLAN_zh.html#phase4-5-q9：scroll 內圖層去 mask 匯全圖（快取簽名一併帶 nomask flag）。
        node._noMaskExport = true;
        // viewport 用的「可視 bounds」（遮色片裁切後、clamp 畫布）；無遮色片時＝一般 bounds。
        var visibleBounds = readLayerBounds(layer);
        visibleBounds = visibleBounds ? clampBoundsToCanvas(visibleBounds, context.doc) : null;
        node._visibleBounds = visibleBounds || bounds;
        // 有啟用遮色片時，boundsNoMask（全圖）與 boundsNoEffects（已套 mask）基準不一致，
        // 陰影補償會算出錯誤 padding → 歸零，座標交給 trim 後的實際像素回寫。
        if (layerHasEnabledMask(layer)) {
            node._padding = zeroPadding();
        } else {
            node._padding = calculateShadowCompensation(bounds, noEffectsBounds);
        }
    } else {
        node._padding = calculateShadowCompensation(bounds, noEffectsBounds);
    }

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
                result = exportNodeImageReuseWithMaskHandling(entry.layer, entry.node, context, exportDoc, file);
            }
            if (result === false && context.useFastLayerDuplicate) {
                if (!fallbackVisibilityPrepared) {
                    hideAllLayers(context.doc);
                    fallbackVisibilityPrepared = true;
                    visibleChain = [];
                }
                visibleChain = showOnlyLayerChain(entry.layer, visibleChain);
                result = exportNodeImageReuseWithMaskHandling(entry.layer, entry.node, context, exportDoc, file);
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
        // OPTIMIZATION_PLAN_zh.html#phase4-5-q9：scroll 內容匯全圖——duplicate 會帶著遮色片（PS 實測），
        // 對複製體刪除 mask 後，align + trim 自然以完整像素回寫節點座標。
        if (node._noMaskExport) {
            removeActiveLayerMasks();
        }
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

// OPTIMIZATION_PLAN_zh.html#phase4-5-q9：merged-copy 會把遮色片（含祖先的）烘進合併結果。
// scroll 內圖層（_noMaskExport）→ 暫時停用自身＋祖先鏈的遮色片，匯完恢復（同 captureVisibility/restore 模式）。
// 退守：匯出失敗（超出畫布 / 遮色片停用失敗）→ 報 SCROLL_EXPORT_DEGRADED warning，不硬做。
function exportNodeImageReuseWithMaskHandling(layer, node, context, exportDoc, file) {
    if (!node._noMaskExport) {
        return exportNodeImageReuse(layer, node, context, exportDoc, file);
    }

    app.activeDocument = context.doc;
    var restore = disableSelfAndAncestorMasks(layer);
    var result = false;
    try {
        result = exportNodeImageReuse(layer, node, context, exportDoc, file);
    } finally {
        app.activeDocument = context.doc;
        restoreMaskStates(restore);
    }

    if (result === false) {
        pushWarning(context, node.name || "", "SCROLL_EXPORT_DEGRADED",
            "scroll 內圖層以 merged-copy 後備路徑匯出失敗（可能超出畫布或遮色片停用失敗），此圖層已跳過；建議調整圖層結構使其可走 fast-duplicate 路徑。");
    }
    return result;
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

// OPTIMIZATION_PLAN_zh.html#phase4-decisions Q6 / Q8：group 名稱中偵測到 [CG] / [CANVASGROUP] → hasCanvasGroup = true。
// 「root 自動掛 CanvasGroup」由 Unity 端硬性掛在 prefab root GameObject，JSX 端不必處理 nesting level。
function applyCanvasGroupMetadata(node, group) {
    var rawName = typeof group === "string" ? group : (group && group.name);
    var name = String(rawName || "");
    if (/\[(?:CG|CANVASGROUP)\]/i.test(name)) {
        node.hasCanvasGroup = true;
    }
}

function applyLayoutGroupMetadata(node, group, children, bounds, context) {
    var layoutType = detectLayoutGroupType(group, children);
    if (!layoutType) {
        return;
    }

    var rawName = typeof group === "string" ? group : (group && group.name);
    var displayName = node && node.name ? node.name : String(rawName || "");

    if (layoutType === "grid") {
        // OPTIMIZATION_PLAN_zh.html#phase4-decisions Q5：計算 grid 參數；若子節點寬高差 > 20% → 降級成普通 group（GRID_DEGRADED）。
        // #phase4-5-q7：scroll 方向會影響 constraint 語意（scroll_h → FixedRowCount，constraintCount 改算行數）。
        var gridParams = calcGridParams(children, context, displayName, node.scrollDirection || "");
        if (!gridParams) {
            return; // 降級：node.layoutType 維持空字串，走普通 group 路徑
        }
        var gridPadding = calcLayoutPadding(bounds, children);
        node.layoutType = "grid";
        node.layoutSpacing = 0; // grid 用 gridSpacingX/Y，layoutSpacing 保留 0
        node.layoutPaddingLeft = gridPadding.padLeft;
        node.layoutPaddingRight = gridPadding.padRight;
        node.layoutPaddingTop = gridPadding.padTop;
        node.layoutPaddingBottom = gridPadding.padBottom;
        node.contentSizeFitter = false; // grid 靠 constraint + cellSize 決定尺寸，不用 ContentSizeFitter
        node.gridConstraintCount = gridParams.constraintCount;
        node.gridStartAxis = gridParams.startAxis;
        node.gridCellSizeX = gridParams.cellSizeX;
        node.gridCellSizeY = gridParams.cellSizeY;
        node.gridSpacingX = gridParams.spacingX;
        node.gridSpacingY = gridParams.spacingY;
        // 依 startAxis 對應的 sibling 填充順序重排 children，讓 Unity GridLayoutGroup 按 sibling index
        // 填入的位置對得上 PS 的視覺 (x, y)。（Angle C #2）
        node.children = sortGridChildren(children, gridParams, bounds);
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
    // H/V Layout Group uses sibling index as layout order. PS layer order is depth order,
    // so sort by visual position instead of reusing the bottom-to-top drawing order.
    node.children = sortLinearLayoutChildren(children, layoutType);
}

function sortLinearLayoutChildren(children, layoutType) {
    if (!children || children.length < 2) return children;

    var sorted = children.slice();
    sorted.sort(function (a, b) {
        var primary = layoutType === "horizontal" ? a.x - b.x : a.y - b.y;
        if (primary !== 0) return primary;
        return layoutType === "horizontal" ? a.y - b.y : a.x - b.x;
    });
    return sorted;
}

// Sort a group's children into the sibling order that Unity's GridLayoutGroup will fill visually.
// FixedColumnCount + Horizontal → row-major (row0 left→right, then row1, ...).
// FixedColumnCount + Vertical → column-major (col0 top→bottom, then col1, ...).
function sortGridChildren(children, gridParams, bounds) {
    if (!children || children.length < 2 || !gridParams) return children;
    var rowStride = (gridParams.cellSizeY || 0) + (gridParams.spacingY || 0);
    var colStride = (gridParams.cellSizeX || 0) + (gridParams.spacingX || 0);
    var originX = bounds ? bounds.left : 0;
    var originY = bounds ? bounds.top : 0;
    var sorted = children.slice();
    sorted.sort(function (a, b) {
        var aRow = rowStride > 0 ? Math.round((a.y - originY) / rowStride) : 0;
        var bRow = rowStride > 0 ? Math.round((b.y - originY) / rowStride) : 0;
        var aCol = colStride > 0 ? Math.round((a.x - originX) / colStride) : 0;
        var bCol = colStride > 0 ? Math.round((b.x - originX) / colStride) : 0;
        if (gridParams.startAxis === "vertical") {
            if (aCol !== bCol) return aCol - bCol;
            return aRow - bRow;
        }
        if (aRow !== bRow) return aRow - bRow;
        return aCol - bCol;
    });
    return sorted;
}

// OPTIMIZATION_PLAN_zh.html#phase4-decisions Q5：Grid 參數推導。
// - cellSize = 子節點寬高的 (中位數, 中位數)
// - 寬高差 > 20% → 回 null（呼叫端降級成普通 group）+ pushWarning(GRID_DEGRADED)
// - 寬高差 ≤ 20% 但非完全等大 → 仍回參數 + pushWarning(GRID_OUTLIER)
// - spacing.x = 同 Y 相鄰節點水平 gap 中位數；spacing.y = 同 X 相鄰節點垂直 gap 中位數
// - startAxis：第一行同 Y 節點數 > 第一列同 X 節點數 → horizontal，否則 vertical
// - constraintCount：主軸第一 line 節點數（配 FixedColumnCount，Unity 端固定）
function calcGridParams(children, context, nodeName, scrollDirection) {
    var items = layoutVisibleChildren(children);
    if (items.length < 2) {
        return null;
    }

    var widths = [];
    var heights = [];
    for (var i = 0; i < items.length; i++) {
        widths.push(items[i].width);
        heights.push(items[i].height);
    }
    var cellWidth = medianOfNumbers(widths);
    var cellHeight = medianOfNumbers(heights);

    // Outlier 偵測（相對於中位數的最大偏差比例）
    var maxWDev = 0;
    var maxHDev = 0;
    for (var j = 0; j < items.length; j++) {
        if (cellWidth > 0) {
            maxWDev = Math.max(maxWDev, Math.abs(items[j].width - cellWidth) / cellWidth);
        }
        if (cellHeight > 0) {
            maxHDev = Math.max(maxHDev, Math.abs(items[j].height - cellHeight) / cellHeight);
        }
    }
    var maxDev = Math.max(maxWDev, maxHDev);

    if (maxDev > 0.20) {
        pushWarning(context, nodeName || "",
            "GRID_DEGRADED",
            "子節點寬高差異超過 20%（最大偏差 " + Math.round(maxDev * 100) +
            "%），無法安排為 GridLayoutGroup；已降級為普通 group。");
        return null;
    }
    if (maxDev > 0) {
        pushWarning(context, nodeName || "",
            "GRID_OUTLIER",
            "子節點寬高存在 " + Math.round(maxDev * 100) +
            "% 的偏差；仍掛 GridLayoutGroup 但實際排列可能與 PS 有微幅差異。");
    }

    // startAxis：以第一行同 Y 節點數 vs 第一列同 X 節點數比較（3 px 容差跟 detectLayoutGroupType 一致）
    var baseY = items[0].y;
    var baseX = items[0].x;
    var firstRowCount = 0;
    var firstColCount = 0;
    for (var k = 0; k < items.length; k++) {
        if (Math.abs(items[k].y - baseY) <= 3) firstRowCount++;
        if (Math.abs(items[k].x - baseX) <= 3) firstColCount++;
    }
    var horizontal = firstRowCount >= firstColCount;
    var startAxis = horizontal ? "horizontal" : "vertical";
    // Unity Constraint.FixedColumnCount 語意始終是「欄數」，跟 startAxis 無關；startAxis 只控填充順序。
    // 先前寫法 `horizontal ? firstRowCount : firstColCount` 會讓 rows>cols 的 grid 90° 翻轉（Angle A #1）。
    // OPTIMIZATION_PLAN_zh.html#phase4-5-q7：[SCROLL_H][GRID] 內容要往「右」長 → Unity 端改掛 FixedRowCount，
    // constraintCount 語意變成「行數」＝第一欄同 X 節點數。其他情境維持 FixedColumnCount（欄數）。
    var constraintCount = scrollDirection === "horizontal" ? firstColCount : firstRowCount;

    // spacing.x：對每一 row（同 y）內排序 by x，收集所有相鄰水平 gap，取整體中位數
    var xGaps = collectGridGaps(items, true);
    var yGaps = collectGridGaps(items, false);
    var spacingX = xGaps.length ? medianOfNumbers(xGaps) : 0;
    var spacingY = yGaps.length ? medianOfNumbers(yGaps) : 0;

    return {
        cellSizeX: round2(cellWidth),
        cellSizeY: round2(cellHeight),
        spacingX: Math.max(0, round2(spacingX)),
        spacingY: Math.max(0, round2(spacingY)),
        startAxis: startAxis,
        constraintCount: Math.max(1, constraintCount)
    };
}

// 收集 grid 內「同 row」或「同 column」的相鄰 gap（3 px 容差分組）。
// horizontal=true → 分 row（同 y），算水平 gap；horizontal=false → 分 column（同 x），算垂直 gap。
function collectGridGaps(items, horizontal) {
    var gaps = [];
    if (!items || items.length < 2) return gaps;

    // 依主軸 sort
    var sorted = items.slice().sort(function (a, b) {
        return horizontal ? (a.y - b.y || a.x - b.x) : (a.x - b.x || a.y - b.y);
    });

    var lines = [];
    var currentLine = [sorted[0]];
    for (var i = 1; i < sorted.length; i++) {
        var prev = currentLine[currentLine.length - 1];
        var curr = sorted[i];
        var sameLine = horizontal ? Math.abs(curr.y - prev.y) <= 3 : Math.abs(curr.x - prev.x) <= 3;
        if (sameLine) {
            currentLine.push(curr);
        } else {
            lines.push(currentLine);
            currentLine = [curr];
        }
    }
    lines.push(currentLine);

    for (var li = 0; li < lines.length; li++) {
        var line = lines[li];
        if (line.length < 2) continue;
        line.sort(function (a, b) {
            return horizontal ? a.x - b.x : a.y - b.y;
        });
        for (var m = 0; m < line.length - 1; m++) {
            var cur = line[m];
            var nxt = line[m + 1];
            var gap = horizontal ? nxt.x - (cur.x + cur.width) : nxt.y - (cur.y + cur.height);
            if (gap >= 0) gaps.push(gap);
        }
    }
    return gaps;
}

function medianOfNumbers(values) {
    if (!values || values.length === 0) return 0;
    var sorted = values.slice().sort(function (a, b) { return a - b; });
    var mid = Math.floor(sorted.length / 2);
    if (sorted.length % 2 === 1) return sorted[mid];
    return (sorted[mid - 1] + sorted[mid]) / 2;
}

function detectLayoutGroupType(group, children) {
    var rawName = typeof group === "string" ? group : (group && group.name);
    var name = String(rawName || "");

    // OPTIMIZATION_PLAN_zh.html#phase4-decisions Q4：Grid 只認顯式標籤，不做啟發式偵測（誤判成本不對稱）。
    if (/\[(?:GRID|GLAYOUT)\]/i.test(name)) {
        return "grid";
    }
    if (/\[(?:H|HLAYOUT)\]/i.test(name)) {
        return "horizontal";
    }
    if (/\[(?:V|VLAYOUT)\]/i.test(name)) {
        return "vertical";
    }

    // v2.10.1：H/V 與 Grid 一樣只接受顯式標籤。普通 PSD 群組常因子層剛好同 X/Y
    // 被誤判成 LayoutGroup，Unity 便會重排甚至拉伸整組內容，破壞設計稿座標。
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
    // OPTIMIZATION_PLAN_zh.html#phase4-5-q2：scroll 內文字同樣用未裁切 bounds（文字不匯圖，僅座標語意）。
    var insideScroll = (context.scrollDepth || 0) > 0;
    var bounds = insideScroll ? readLayerBoundsNoMask(layer) : readLayerBounds(layer);
    if (!bounds || bounds.width <= 0 || bounds.height <= 0) {
        return null;
    }

    var textStyle = readTextLayerStyle(layer);
    var inheritedFillColor = readInheritedTextFillColor(layer);
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
        // Photoshop 的群組 Color Overlay 會覆蓋子文字；若只讀文字層本身，Unity 顏色會偏掉。
        color: inheritedFillColor || textStyle.fillColor || readTextColor(layer),
        outlineColor: textStyle.outlineColor,
        outlineWidth: textStyle.outlineWidth,
        outlineOpacity: textStyle.outlineOpacity,
        gradientStartColor: textStyle.gradientStartColor || "",
        gradientEndColor: textStyle.gradientEndColor || "",
        gradientAngle: textStyle.gradientAngle || 0,
        alignment: readTextAlignment(layer),
        children: []
    };

    if (fakeThickness.offsetY !== 0) {
        node.fakeThicknessOffsetY = fakeThickness.offsetY;
    }
    if (fakeThickness.offsetX !== 0) {
        node.fakeThicknessOffsetX = fakeThickness.offsetX;
    }

    if (insideScroll) {
        var visibleBounds = readLayerBounds(layer);
        visibleBounds = visibleBounds ? clampBoundsToCanvas(visibleBounds, context.doc) : null;
        node._visibleBounds = visibleBounds || bounds;
    }

    applyLayoutMetadata(node, bounds, parentBounds, layer.name);
    return node;
}

function buildLayoutJson(doc, nodes, warnings) {
    var lines = [];
    lines.push("{");
    lines.push('  "schemaVersion": "2.10",');
    lines.push('  "canvas": {');
    lines.push('    "width": ' + jsonNumber(px(doc.width)) + ",");
    lines.push('    "height": ' + jsonNumber(px(doc.height)));
    lines.push("  },");
    lines.push('  "nodes": [');

    for (var i = 0; i < nodes.length; i++) {
        lines.push(nodeToJson(nodes[i], "    ") + (i < nodes.length - 1 ? "," : ""));
    }

    lines.push("  ],");
    // OPTIMIZATION_PLAN_zh.html#phase4-decisions Q10-a/b：root-level warnings 陣列。空陣列亦寫出，讓 Unity backend 統一走同一段解析。
    var warningList = warnings || [];
    if (warningList.length === 0) {
        lines.push('  "warnings": []');
    } else {
        lines.push('  "warnings": [');
        for (var w = 0; w < warningList.length; w++) {
            var item = warningList[w];
            var trailing = w < warningList.length - 1 ? "," : "";
            lines.push("    {");
            lines.push('      "node": ' + quoteJson(item.node || "") + ",");
            lines.push('      "code": ' + quoteJson(item.code || "") + ",");
            lines.push('      "message": ' + quoteJson(item.message || ""));
            lines.push("    }" + trailing);
        }
        lines.push("  ]");
    }
    lines.push("}");
    return lines.join("\n");
}

// OPTIMIZATION_PLAN_zh.html#phase4-decisions Q10：把一則 warning 塞進 context.warnings，後面會 flush 到 layout.json 的 root-level warnings 陣列。
// codes 用 SCREAMING_SNAKE 常數（例：GRID_OUTLIER / GRID_DEGRADED / UNITY_TOOL_OUTDATED）。
function pushWarning(context, nodeName, code, message) {
    if (!context || !context.warnings) {
        return;
    }
    // Dedup by (node, code) — applyLayoutGroupMetadata 會在 createGroupNode 與 refreshGroupBounds
    // 各執行一次，calcGridParams 若不 dedup 會讓每個 grid 的 GRID_OUTLIER/GRID_DEGRADED 出兩次。
    var key = String(nodeName || "") + "|" + String(code || "");
    if (!context._warningKeys) context._warningKeys = {};
    if (context._warningKeys[key]) return;
    context._warningKeys[key] = true;
    context.warnings.push({
        node: nodeName || "",
        code: code || "",
        message: message || ""
    });
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
        // OPTIMIZATION_PLAN_zh.html#phase4-decisions Q12-c：grid 專屬欄位只在 layoutType==="grid" 時寫出
        if (node.layoutType === "grid") {
            lines.push(childIndent + '"gridConstraintCount": ' + jsonNumber(node.gridConstraintCount || 1) + ",");
            lines.push(childIndent + '"gridStartAxis": ' + quoteJson(node.gridStartAxis || "horizontal") + ",");
            lines.push(childIndent + '"gridCellSizeX": ' + jsonNumber(node.gridCellSizeX || 0) + ",");
            lines.push(childIndent + '"gridCellSizeY": ' + jsonNumber(node.gridCellSizeY || 0) + ",");
            lines.push(childIndent + '"gridSpacingX": ' + jsonNumber(node.gridSpacingX || 0) + ",");
            lines.push(childIndent + '"gridSpacingY": ' + jsonNumber(node.gridSpacingY || 0) + ",");
        }
    }
    if (node.hasCanvasGroup) {
        lines.push(childIndent + '"hasCanvasGroup": true,');
    }
    // OPTIMIZATION_PLAN_zh.html#phase4-5-q8：scroll 欄位只在有標籤時寫出。
    // node 自身 x/y/w/h = viewport（可視範圍）；content* = children 完整 bounds 聯集（絕對 PS 座標）。
    if (node.scrollDirection) {
        lines.push(childIndent + '"scrollDirection": ' + quoteJson(node.scrollDirection) + ",");
        lines.push(childIndent + '"contentX": ' + jsonNumber(node.contentX || 0) + ",");
        lines.push(childIndent + '"contentY": ' + jsonNumber(node.contentY || 0) + ",");
        lines.push(childIndent + '"contentWidth": ' + jsonNumber(node.contentWidth || 0) + ",");
        lines.push(childIndent + '"contentHeight": ' + jsonNumber(node.contentHeight || 0) + ",");
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
        // Phase 3：漸層只在有資料時寫出，無漸層的文字維持原 JSON 大小
        if (node.gradientStartColor) {
            lines.push(childIndent + '"gradientStartColor": ' + quoteJson(node.gradientStartColor) + ",");
            lines.push(childIndent + '"gradientEndColor": ' + quoteJson(node.gradientEndColor || "") + ",");
            lines.push(childIndent + '"gradientAngle": ' + jsonNumber(node.gradientAngle || 0) + ",");
        }
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
    // LayerSet 的 DOM boundsNoEffects 在部分 Photoshop 版本會忽略群組遮色片，
    // 但 Action Manager descriptor 的同名欄位會回傳正確的遮色片後範圍。
    if (layer && layer.typename === "LayerSet") {
        try {
            var desc = getLayerDescriptor(layer);
            var descriptorBounds = getDescriptorObject(desc, ["boundsNoEffects"], [], null);
            if (descriptorBounds) {
                var descriptorLeft = Math.floor(getDescriptorUnitDouble(descriptorBounds, ["left"], ["Left"], 0));
                var descriptorTop = Math.floor(getDescriptorUnitDouble(descriptorBounds, ["top"], ["Top "], 0));
                var descriptorRight = Math.ceil(getDescriptorUnitDouble(descriptorBounds, ["right"], ["Rght"], 0));
                var descriptorBottom = Math.ceil(getDescriptorUnitDouble(descriptorBounds, ["bottom"], ["Btom"], 0));
                if (descriptorRight > descriptorLeft && descriptorBottom > descriptorTop) {
                    return {
                        left: descriptorLeft,
                        top: descriptorTop,
                        right: descriptorRight,
                        bottom: descriptorBottom,
                        width: descriptorRight - descriptorLeft,
                        height: descriptorBottom - descriptorTop
                    };
                }
            }
        } catch (ignored) {
        }
    }

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

// OPTIMIZATION_PLAN_zh.html#phase4-5-q9：讀「未被遮色片裁切」的完整 bounds。
// descriptor key boundsNoMask 已在使用者 PS 實測可讀；缺 key（極舊版 PS）fallback 回一般 bounds。
function readLayerBoundsNoMask(layer) {
    try {
        var desc = getLayerDescriptor(layer);
        var b = getDescriptorObject(desc, ["boundsNoMask"], [], null);
        if (!b) {
            return readLayerBounds(layer);
        }
        var left = Math.floor(getDescriptorUnitDouble(b, ["left"], ["Left"], 0));
        var top = Math.floor(getDescriptorUnitDouble(b, ["top"], ["Top "], 0));
        var right = Math.ceil(getDescriptorUnitDouble(b, ["right"], ["Rght"], 0));
        var bottom = Math.ceil(getDescriptorUnitDouble(b, ["bottom"], ["Btom"], 0));
        if (right <= left || bottom <= top) {
            return readLayerBounds(layer);
        }
        return {
            left: left,
            top: top,
            right: right,
            bottom: bottom,
            width: Math.max(0, right - left),
            height: Math.max(0, bottom - top)
        };
    } catch (e) {
        return readLayerBounds(layer);
    }
}

// OPTIMIZATION_PLAN_zh.html#phase4-5-q1：scroll 標籤偵測（顯式，group 專用；非 group 圖層上的標籤會被 strip 後靜默忽略）。
function detectScrollDirection(rawName) {
    var name = String(rawName || "");
    var vertical = /\[SCROLL_V\]/i.test(name);
    var horizontal = /\[SCROLL_H\]/i.test(name);
    if (vertical && horizontal) {
        return "both";
    }
    if (vertical) {
        return "vertical";
    }
    if (horizontal) {
        return "horizontal";
    }
    return "";
}

// 讀圖層遮色片啟用狀態（user mask + vector mask）。
function readLayerMaskState(layer) {
    try {
        var desc = getLayerDescriptor(layer);
        return {
            hasUser: getDescriptorBoolean(desc, ["hasUserMask"], [], false),
            userEnabled: getDescriptorBoolean(desc, ["userMaskEnabled"], [], false),
            hasVector: getDescriptorBoolean(desc, ["hasVectorMask"], [], false),
            vectorEnabled: getDescriptorBoolean(desc, ["vectorMaskEnabled"], [], false)
        };
    } catch (e) {
        return { hasUser: false, userEnabled: false, hasVector: false, vectorEnabled: false };
    }
}

function layerHasEnabledMask(layer) {
    var state = readLayerMaskState(layer);
    return (state.hasUser && state.userEnabled) || (state.hasVector && state.vectorEnabled);
}

// 以 AM setd 切換圖層遮色片啟用狀態；userEnabled / vectorEnabled 傳 null 表示不動該項。
function setLayerMaskEnabled(layer, userEnabled, vectorEnabled) {
    try {
        var descriptor = new ActionDescriptor();
        var reference = new ActionReference();
        reference.putIdentifier(charIDToTypeID("Lyr "), layer.id);
        descriptor.putReference(charIDToTypeID("null"), reference);
        var props = new ActionDescriptor();
        if (userEnabled !== null) {
            props.putBoolean(charIDToTypeID("UsrM"), userEnabled);
        }
        if (vectorEnabled !== null) {
            props.putBoolean(stringIDToTypeID("vectorMaskEnabled"), vectorEnabled);
        }
        descriptor.putObject(charIDToTypeID("T   "), charIDToTypeID("Lyr "), props);
        executeAction(charIDToTypeID("setd"), descriptor, DialogModes.NO);
        return true;
    } catch (e) {
        return false;
    }
}

// OPTIMIZATION_PLAN_zh.html#phase4-5-q9：fast-duplicate 路徑——對「複製體」刪除遮色片（丟棄不套用），
// 源文件零接觸。scroll 語境的 PS 遮色片＝runtime 裁切預覽，由 Unity 端 RectMask2D 接手。
function removeActiveLayerMasks() {
    try {
        var descriptor = new ActionDescriptor();
        var reference = new ActionReference();
        reference.putEnumerated(charIDToTypeID("Chnl"), charIDToTypeID("Chnl"), charIDToTypeID("Msk "));
        descriptor.putReference(charIDToTypeID("null"), reference);
        descriptor.putBoolean(charIDToTypeID("Aply"), false);
        executeAction(charIDToTypeID("Dlt "), descriptor, DialogModes.NO);
    } catch (ignored) {
    }
    try {
        var vectorDescriptor = new ActionDescriptor();
        var vectorReference = new ActionReference();
        vectorReference.putEnumerated(charIDToTypeID("Path"), charIDToTypeID("Ordn"), stringIDToTypeID("vectorMask"));
        vectorDescriptor.putReference(charIDToTypeID("null"), vectorReference);
        executeAction(charIDToTypeID("Dlt "), vectorDescriptor, DialogModes.NO);
    } catch (ignored) {
    }
}

// OPTIMIZATION_PLAN_zh.html#phase4-5-q9：merged-copy 路徑——暫時停用自身＋祖先鏈上的遮色片（匯完恢復）。
function disableSelfAndAncestorMasks(layer) {
    var restore = [];
    var current = layer;
    while (current && current.typename !== "Document") {
        try {
            var state = readLayerMaskState(current);
            var disableUser = state.hasUser && state.userEnabled;
            var disableVector = state.hasVector && state.vectorEnabled;
            if (disableUser || disableVector) {
                if (setLayerMaskEnabled(current, disableUser ? false : null, disableVector ? false : null)) {
                    restore.push({
                        layer: current,
                        user: disableUser ? true : null,
                        vector: disableVector ? true : null
                    });
                }
            }
            current = current.parent;
        } catch (e) {
            break;
        }
    }
    return restore;
}

function restoreMaskStates(restore) {
    for (var i = restore.length - 1; i >= 0; i--) {
        try {
            setLayerMaskEnabled(restore[i].layer, restore[i].user, restore[i].vector);
        } catch (ignored) {
        }
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
    // OPTIMIZATION_PLAN_zh.html#phase4-5-q9：scroll 內去 mask 匯出的圖，簽名帶 nomask flag——
    // 防「先以裁切版進 cache → 加 [SCROLL_V] 後 cache hit 沿用半張圖」。只在 true 時附加，不動既有快取。
    if (node._noMaskExport) {
        parts.push("nomask");
    }
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

// Phase 3 像素雜湊去重：對匯出後的 PNG 做 FNV-1a 32-bit hash，同 hash 視為內容相同。
// ES3 / ExtendScript 無 crypto，FNV-1a 雖然不是安全雜湊，但對 UI Package 的小檔案
// 數量級（通常 < 200 張）內容完全相同才會撞值的機率極低，足以作為「同內容偵測」。
function computeFileHash(file) {
    try {
        file.encoding = "BINARY";
        if (!file.open("r")) {
            return null;
        }
        var content = file.read();
        file.close();
    } catch (e) {
        try { file.close(); } catch (e2) {}
        return null;
    }

    var hash = 0x811C9DC5; // FNV-1a 32-bit offset basis
    var len = content.length;
    for (var i = 0; i < len; i++) {
        hash = hash ^ content.charCodeAt(i);
        // FNV prime 0x01000193；| 0 強制 32-bit signed，下面 >>> 0 轉 unsigned
        hash = Math.imul ? Math.imul(hash, 0x01000193) : (hash * 0x01000193) | 0;
    }
    return ((hash >>> 0).toString(16)) + "_" + len; // 串上長度進一步降低碰撞
}

// Phase 3 dedup：呼叫前 PNG 已寫到 imageFolder。回傳 { dedupedCount, savedBytes }。
// - 同 hash 群組：保留第一個遇到的 imagePath 為 canonical，其餘檔案刪除、節點 imagePath 重指到 canonical
// - 同步清理 export cache 中對應「已刪除路徑」的紀錄，避免下次 run 認為檔案還在而誤跳過匯出
function dedupPngsByHash(pendingImages, imageFolder, context) {
    var stats = { dedupedCount: 0, savedBytes: 0, uniqueCount: 0, originalCount: 0 };
    if (!pendingImages || pendingImages.length === 0) {
        return stats;
    }

    var hashMap = {}; // hash -> canonical imagePath
    var deletedPaths = {}; // deletedPath -> canonicalPath

    for (var i = 0; i < pendingImages.length; i++) {
        var p = pendingImages[i];
        if (!p || !p.node || !p.node.imagePath) {
            continue;
        }
        stats.originalCount++;

        // 若該 node 的 imagePath 已被重指過（多個 node 共用同檔），跳過實體掃描
        if (deletedPaths.hasOwnProperty(p.node.imagePath)) {
            p.node.imagePath = deletedPaths[p.node.imagePath];
            stats.dedupedCount++;
            continue;
        }

        var file = new File(imageFolder.fsName + "/" + p.node.imagePath);
        if (!file.exists) {
            continue;
        }
        var hash = computeFileHash(file);
        if (!hash) {
            continue;
        }

        if (hashMap.hasOwnProperty(hash)) {
            var canonical = hashMap[hash];
            stats.savedBytes += file.length;
            var oldPath = p.node.imagePath;
            try { file.remove(); } catch (e) {}
            deletedPaths[oldPath] = canonical;
            p.node.imagePath = canonical;
            stats.dedupedCount++;

            // 清掉 cache 內被刪除路徑的條目
            if (context.exportCache) {
                var keysToDelete = [];
                for (var k in context.exportCache) {
                    if (context.exportCache.hasOwnProperty(k) &&
                        context.exportCache[k].imagePath === oldPath) {
                        keysToDelete.push(k);
                    }
                }
                for (var j = 0; j < keysToDelete.length; j++) {
                    delete context.exportCache[keysToDelete[j]];
                    context.exportCacheDirty = true;
                }
            }
        } else {
            hashMap[hash] = p.node.imagePath;
            stats.uniqueCount++;
        }
    }

    return stats;
}

// Phase 3 / U13：取代原本的 alert，輸出可回看的 export_report.txt
function writeExportReport(reportFile, result, dedupStats, pngStats) {
    var lines = [];
    var now = new Date();
    var pad = function (n) { return n < 10 ? "0" + n : "" + n; };
    var stamp = now.getFullYear() + "-" + pad(now.getMonth() + 1) + "-" + pad(now.getDate())
              + " " + pad(now.getHours()) + ":" + pad(now.getMinutes()) + ":" + pad(now.getSeconds());

    lines.push("PS_To_Unity_v2 匯出報告");
    lines.push("版本：v" + SCRIPT_VERSION);
    lines.push("時間：" + stamp);
    lines.push("==========================================");
    lines.push("");
    lines.push("[統計]");
    lines.push("匯出圖層：" + result.imageCount);
    lines.push("文字節點：" + result.textCount + "（含描邊：" + result.outlineTextCount + "，含漸層：" + result.gradientTextCount + "）");
    lines.push("群組：" + result.groupCount);
    lines.push("");
    lines.push("[跳過]");
    lines.push("空白/不支援：" + result.skippedCount);
    lines.push("隱藏圖層：" + result.skipHidden);
    lines.push("IGNORE/REF：" + result.skipIgnoreRef);
    lines.push("調整圖層：" + result.skipAdjustment);
    lines.push("裁切圖層：" + result.skipClipping);
    lines.push("快取未變更：" + result.unchangedCount);
    lines.push("");
    lines.push("[像素雜湊去重]");
    if (dedupStats && dedupStats.originalCount > 0) {
        lines.push("原始 PNG：" + dedupStats.originalCount);
        lines.push("合併重複：" + dedupStats.dedupedCount + " → 唯一檔案 " + dedupStats.uniqueCount);
        lines.push("省下：" + formatBytes(dedupStats.savedBytes));
    } else {
        lines.push("（無）");
    }
    lines.push("");
    lines.push("[檔案]");
    lines.push("PNG 資料夾：" + result.imageFolder.fsName);
    if (pngStats) {
        lines.push("PNG 總數：" + pngStats.fileCount);
        lines.push("PNG 總大小：" + formatBytes(pngStats.totalBytes));
        if (pngStats.largestName) {
            lines.push("最大單檔：" + pngStats.largestName + "（" + formatBytes(pngStats.largestBytes) + "）");
        }
        if (pngStats.oversized && pngStats.oversized.length > 0) {
            lines.push("⚠ 超大警告（>500KB，共 " + pngStats.oversized.length + " 筆）：");
            for (var i = 0; i < pngStats.oversized.length && i < 20; i++) {
                lines.push("  - " + pngStats.oversized[i].name + "（" + formatBytes(pngStats.oversized[i].size) + "）");
            }
            if (pngStats.oversized.length > 20) {
                lines.push("  …（其餘 " + (pngStats.oversized.length - 20) + " 筆省略）");
            }
        } else {
            lines.push("超大警告（>500KB）：0 筆");
        }
    }
    lines.push("");
    lines.push("[Layout JSON]");
    lines.push(result.layoutJsonFile.fsName);

    try {
        writeTextFile(reportFile, lines.join("\n"));
    } catch (e) {
        // 報告寫不出來不影響匯出本身
    }
}

function scanPngFolderStats(imageFolder) {
    var stats = { fileCount: 0, totalBytes: 0, largestName: "", largestBytes: 0, oversized: [] };
    try {
        var files = imageFolder.getFiles("*.png");
        for (var i = 0; i < files.length; i++) {
            var f = files[i];
            if (!(f instanceof File)) continue;
            var len = f.length;
            stats.fileCount++;
            stats.totalBytes += len;
            if (len > stats.largestBytes) {
                stats.largestBytes = len;
                stats.largestName = f.name;
            }
            if (len > 500 * 1024) {
                stats.oversized.push({ name: f.name, size: len });
            }
        }
    } catch (e) {}
    return stats;
}

function formatBytes(bytes) {
    if (!bytes) return "0 B";
    if (bytes < 1024) return bytes + " B";
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + " KB";
    return (bytes / (1024 * 1024)).toFixed(2) + " MB";
}

function exportReportFile(imageFolder) {
    return new File(imageFolder.fsName + "/export_report.txt");
}

// 掃 layout tree 計算「含描邊文字 / 含漸層文字」筆數，給報告用
function countTextStats(nodes) {
    var stats = { outlineTextCount: 0, gradientTextCount: 0 };
    walk(nodes);
    return stats;
    function walk(arr) {
        if (!arr) return;
        for (var i = 0; i < arr.length; i++) {
            var n = arr[i];
            if (!n) continue;
            if (n.type === "text") {
                if (n.outlineWidth && n.outlineWidth > 0) stats.outlineTextCount++;
                if (n.gradientStartColor) stats.gradientTextCount++;
            }
            if (n.children && n.children.length) walk(n.children);
        }
    }
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
    var base = normalizeAsciiSlug(stripKnownTags(stripLayoutTokens(stripControlPrefix(name))));
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
    // v2.10：BTN_ 前綴保留在節點名（Unity 端據此自動掛 Button）；PNG 檔名仍走 uniqueFileName 去前綴。
    var base = uniqueFileName(name, counters);
    if (startsWith(String(name || "").toUpperCase(), "BTN_")) {
        return "BTN_" + base;
    }
    return base;
}

function stripKnownTags(name) {
    var text = String(name || "");
    for (var i = 0; i < KNOWN_BRACKET_TAG_PATTERNS.length; i++) {
        text = text.replace(KNOWN_BRACKET_TAG_PATTERNS[i], "");
    }
    return text;
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
        // v2.10：內建思源／源泉系列 + 使用者自訂字型白名單，白名單內保持 TMP。
        return !isTmpWhitelistedFont(rawFont, context);
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

// v2.10：TMP 字型白名單 = 內建思源／源泉系列 + 使用者自訂關鍵字。
// 白名單內的字型保持 TMP 節點；Unity 端用 TmpFontMap（fontToken → Font Asset）套正確字型。
function isTmpWhitelistedFont(rawFontName, context) {
    if (isSourceHanFamily(rawFontName)) {
        return true;
    }

    var keywords = context ? context.tmpFontKeywords : null;
    if (!keywords || !keywords.length) {
        return false;
    }

    var slug = normalizeAsciiSlug(rawFontName);
    for (var i = 0; i < keywords.length; i++) {
        var keyword = normalizeAsciiSlug(keywords[i]);
        if (keyword && slug.indexOf(keyword) >= 0) {
            return true;
        }
    }
    return false;
}

// 白名單存於使用者資料夾（跨 PSD 共用、不進倉庫）：一行一個關鍵字。
function userTmpFontWhitelistFile() {
    var folder = new Folder(Folder.userData.fsName + "/PS_To_Unity_v2");
    if (!folder.exists) {
        folder.create();
    }
    return new File(folder.fsName + "/tmp_font_whitelist.txt");
}

function loadUserTmpFontKeywords() {
    var keywords = [];
    var file = userTmpFontWhitelistFile();
    if (!file.exists) {
        return keywords;
    }

    try {
        file.encoding = "UTF8";
        if (file.open("r")) {
            while (!file.eof) {
                var line = trim(file.readln());
                if (line) {
                    keywords.push(line);
                }
            }
            file.close();
        }
    } catch (e) {
        try { file.close(); } catch (ignored) {}
    }
    return keywords;
}

function saveUserTmpFontKeywords(keywords) {
    var file = userTmpFontWhitelistFile();
    try {
        file.encoding = "UTF8";
        if (file.open("w")) {
            for (var i = 0; i < keywords.length; i++) {
                file.writeln(keywords[i]);
            }
            file.close();
            return true;
        }
    } catch (e) {
        try { file.close(); } catch (ignored) {}
    }
    return false;
}

// 走訪整份文件收集文字圖層用到的字型（PostScript 名，去重）。
function collectDocumentTextFonts(doc) {
    var found = [];
    var seen = {};

    function walk(layers) {
        for (var i = 0; i < layers.length; i++) {
            var layer = layers[i];
            if (layer.typename === "LayerSet") {
                walk(layer.layers);
            } else {
                var isText = false;
                try { isText = layer.kind === LayerKind.TEXT; } catch (e) {}
                if (isText) {
                    var font = readRawFontName(layer);
                    if (font && !seen[font]) {
                        seen[font] = true;
                        found.push(font);
                    }
                }
            }
        }
    }

    try { walk(doc.layers); } catch (e) {}
    return found;
}

function readTextLayerStyle(layer) {
    var style = {
        fillColor: "",
        outlineColor: "",
        outlineWidth: 0,
        outlineOpacity: 1,
        // Phase 3 漸層文字
        gradientStartColor: "",
        gradientEndColor: "",
        gradientAngle: 0
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

        // Phase 3 Gradient Overlay：取第一個與最後一個 color stop + 角度，寫進 JSON
        var gradFill = getDescriptorObject(effects, ["gradientFill"], ["GrFl"]);
        if (gradFill && getDescriptorBoolean(gradFill, ["enabled"], ["enab"], true)) {
            var gradient = getDescriptorObject(gradFill, ["gradient"], ["Grdn"]);
            if (gradient) {
                var stops = getDescriptorList(gradient, ["colors"], ["Clrs"]);
                if (stops && stops.count >= 2) {
                    var firstStop = getDescriptorListItemAt(stops, 0);
                    var lastStop = getDescriptorListItemAt(stops, stops.count - 1);
                    var firstColor = firstStop ? getDescriptorObject(firstStop, ["color"], ["Clr "]) : null;
                    var lastColor = lastStop ? getDescriptorObject(lastStop, ["color"], ["Clr "]) : null;
                    if (firstColor && lastColor) {
                        style.gradientStartColor = descriptorColorToHex(firstColor);
                        style.gradientEndColor = descriptorColorToHex(lastColor);
                        style.gradientAngle = round2(getDescriptorUnitDouble(gradFill, ["angle"], ["Angl"], 90));
                    }
                }
            }
        }
    } catch (e) {
        return style;
    }

    return style;
}

// 只繼承父群組的 Color Overlay。群組 Stroke / Gradient 是對整組合成結果套用，
// 不能安全拆到每個 TMP，因此不在這裡推導。
function readInheritedTextFillColor(layer) {
    var fillColor = "";
    try {
        var parent = layer.parent;
        while (parent && parent.typename === "LayerSet") {
            var parentStyle = readTextLayerStyle(parent);
            if (parentStyle && parentStyle.fillColor) {
                // 由內往外走；外層效果最後套用，符合 Photoshop 群組合成順序。
                fillColor = parentStyle.fillColor;
            }
            parent = parent.parent;
        }
    } catch (e) {
    }
    return fillColor;
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

function getDescriptorList(descriptor, stringKeys, charKeys) {
    var id = findDescriptorKey(descriptor, stringKeys, charKeys);
    if (!id) {
        return null;
    }

    try {
        return descriptor.getList(id);
    } catch (e) {
        return null;
    }
}

function getDescriptorListItemAt(list, index) {
    if (!list || index < 0 || index >= list.count) {
        return null;
    }

    try {
        return list.getObjectValue(index);
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

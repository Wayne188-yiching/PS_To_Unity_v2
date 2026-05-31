// 診斷字型自動分流：列出 PSD 內所有文字圖層的字型名稱與分流結果
// 使用方式：開啟 PSD 後執行此腳本
(function () {
    if (!app.documents.length) {
        alert("請先開啟 PSD。");
        return;
    }
    var doc = app.activeDocument;

    function readRawFontName(layer) {
        try {
            if (layer.textItem && layer.textItem.font) {
                return String(layer.textItem.font || "");
            }
        } catch (e) {}
        try {
            var ref = new ActionReference();
            ref.putIdentifier(charIDToTypeID("Lyr "), layer.id);
            var desc = app.executeActionGet(ref);
            var txtDesc = desc.getObjectValue(charIDToTypeID("Txt "));
            var rangeList = txtDesc.getList(charIDToTypeID("Txtt"));
            if (rangeList.count > 0) {
                var range = rangeList.getObjectValue(0);
                var style = range.getObjectValue(stringIDToTypeID("textStyle"));
                try {
                    return style.getString(stringIDToTypeID("fontPostScriptName"));
                } catch (e1) {
                    try { return style.getString(stringIDToTypeID("fontName")); } catch (e2) {}
                }
            }
        } catch (e) {}
        return "";
    }

    function isSourceHanFamily(rawFontName) {
        var lower = String(rawFontName || "").toLowerCase();
        if (!lower) return false;
        if (/[㐀-鿿豈-﫿]/.test(lower)) return true;
        var keywords = [
            "gensenrounded", "gensenmaru",
            "sourcehan", "source han",
            "noto sans cjk", "noto serif cjk",
            "notosanscjk", "notoserifcjk"
        ];
        for (var i = 0; i < keywords.length; i++) {
            if (lower.indexOf(keywords[i]) !== -1) return true;
        }
        return false;
    }

    var results = [];
    function walk(container, path) {
        for (var i = 0; i < container.layers.length; i++) {
            var layer = container.layers[i];
            try {
                if (layer.typename === "LayerSet") {
                    walk(layer, path + "/" + layer.name);
                    continue;
                }
                if (layer.kind === LayerKind.TEXT) {
                    var name = layer.name;
                    var content = "";
                    try { content = layer.textItem.contents || ""; } catch (e) {}
                    var font = readRawFontName(layer);
                    var isHan = isSourceHanFamily(font);
                    var route = !font ? "→ PNG (no font detected)"
                              : (isHan ? "→ TMP (Source Han ✓)" : "→ PNG (non-Source-Han)");
                    results.push(
                        "[" + name + "]\n" +
                        "  text: " + content + "\n" +
                        "  font: " + (font || "(empty)") + "\n" +
                        "  result: " + route);
                }
            } catch (e) {
                results.push("[" + layer.name + "] error: " + e.message);
            }
        }
    }

    walk(doc, "");

    if (results.length === 0) {
        alert("PSD 內沒有找到任何文字圖層。");
    } else {
        alert("找到 " + results.length + " 個文字圖層：\n\n" + results.join("\n\n"));
    }
})();

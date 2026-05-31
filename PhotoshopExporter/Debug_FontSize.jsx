// 診斷字體大小：找出 textItem.size 與 PS 字元面板顯示的真實差異
// 使用方式：在 PS 選中要檢查的文字圖層，執行此腳本
(function () {
    if (!app.documents.length) {
        alert("請先開啟 PSD。");
        return;
    }

    var layer = app.activeDocument.activeLayer;
    var doc = app.activeDocument;
    var info = [];

    info.push("=== 圖層 ===");
    info.push("名稱: " + layer.name);

    info.push("\n=== Document ===");
    info.push("doc.resolution: " + doc.resolution + " ppi");
    info.push("doc.width: " + doc.width.as("px") + " px");

    info.push("\n=== app.preferences ===");
    try {
        info.push("typeUnits: " + app.preferences.typeUnits);
    } catch (e) { info.push("typeUnits error: " + e.message); }
    try {
        info.push("rulerUnits: " + app.preferences.rulerUnits);
    } catch (e) { info.push("rulerUnits error: " + e.message); }

    info.push("\n=== textItem.size (多種單位) ===");
    try {
        var s = layer.textItem.size;
        info.push("raw: " + s);
        info.push(".as('pt'): " + s.as("pt"));
        info.push(".as('px'): " + s.as("px"));
        info.push(".as('mm'): " + s.as("mm"));
    } catch (e) { info.push("textItem.size error: " + e.message); }

    info.push("\n=== bounds ===");
    try {
        var b = layer.bounds;
        info.push("bounds: [" + b[0] + ", " + b[1] + ", " + b[2] + ", " + b[3] + "]");
        info.push("實際渲染高度: " + (b[3].as("px") - b[1].as("px")) + " px");
    } catch (e) { info.push("bounds error: " + e.message); }

    info.push("\n=== ActionDescriptor 真實值 ===");
    try {
        var ref = new ActionReference();
        ref.putIdentifier(charIDToTypeID("Lyr "), layer.id);
        var desc = app.executeActionGet(ref);
        var txtDesc = desc.getObjectValue(charIDToTypeID("Txt "));
        var rangeList = txtDesc.getList(charIDToTypeID("Txtt"));
        if (rangeList.count > 0) {
            var range = rangeList.getObjectValue(0);
            var styleDesc = range.getObjectValue(stringIDToTypeID("textStyle"));

            try {
                var size = styleDesc.getUnitDoubleValue(charIDToTypeID("Sz  "));
                info.push("style.Sz (原始大小): " + size);
            } catch (e) { info.push("style.Sz error: " + e.message); }

            try {
                var implied = styleDesc.getUnitDoubleValue(stringIDToTypeID("impliedFontSize"));
                info.push("impliedFontSize (實際渲染): " + implied);
            } catch (e) { info.push("impliedFontSize error: " + e.message); }
        }
    } catch (e) {
        info.push("descriptor 讀取失敗: " + e.message);
    }

    info.push("\n=== Transform ===");
    try {
        var txtDesc2 = app.executeActionGet(ref).getObjectValue(charIDToTypeID("Txt "));
        if (txtDesc2.hasKey(stringIDToTypeID("transform"))) {
            var tf = txtDesc2.getObjectValue(stringIDToTypeID("transform"));
            info.push("xx: " + tf.getDouble(stringIDToTypeID("xx")));
            info.push("yy: " + tf.getDouble(stringIDToTypeID("yy")));
        } else {
            info.push("沒有 transform 鍵");
        }
    } catch (e) {
        info.push("transform 讀取失敗: " + e.message);
    }

    alert(info.join("\n"));
})();

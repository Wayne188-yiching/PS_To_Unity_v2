// 診斷腳本：檢查指定文字圖層的真實 kind
// 使用方式：在 PS 選中「游戏音乐」圖層後執行
(function () {
    if (!app.documents.length) {
        alert("請先開啟 PSD 檔案。");
        return;
    }

    var layer;
    try {
        layer = app.activeDocument.activeLayer;
    } catch (e) {
        alert("請先選中要檢查的圖層。");
        return;
    }

    var info = [];
    info.push("圖層名稱: " + layer.name);
    info.push("typename: " + layer.typename);

    var kindStr = "(讀取失敗)";
    try {
        kindStr = String(layer.kind);
    } catch (e) {
        kindStr = "throw: " + e.message;
    }
    info.push("kind: " + kindStr);
    info.push("LayerKind.TEXT 應為: " + LayerKind.TEXT);
    info.push("是否相等: " + (layer.kind === LayerKind.TEXT));

    try {
        info.push("textItem 可存取: 是");
        info.push("textItem.contents: " + (layer.textItem.contents || "(空)"));
        info.push("textItem.font: " + (layer.textItem.font || "(空)"));
        info.push("textItem.size: " + layer.textItem.size);
    } catch (e) {
        info.push("textItem 存取失敗: " + e.message);
    }

    try {
        var b = layer.bounds;
        info.push("bounds: [" + b[0] + ", " + b[1] + ", " + b[2] + ", " + b[3] + "]");
    } catch (e) {
        info.push("bounds 失敗: " + e.message);
    }

    alert(info.join("\n"));
})();

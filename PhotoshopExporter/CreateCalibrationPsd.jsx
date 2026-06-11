#target photoshop

// CreateCalibrationPsd.jsx - Phase 1 calibration document generator.
//
// Creates a fixed grid of text layers (font size x stroke width) used as the
// regression baseline for the PS -> Unity font precision pipeline (F2/F3).
// Run once, save the resulting PSD as CalibrationBoard.psd, export it with
// PhotoshopUiPackageExporter.jsx, then compare the Unity output against a
// half-transparent screenshot of this document.
//
// Layout: rows = font sizes, columns = stroke widths (0 = no stroke).

(function () {
    var FONT_SIZES = [24, 36, 48, 72];          // pt (document at 72 DPI -> px == pt)
    var STROKE_WIDTHS = [0, 2, 4, 6, 8];        // px stroke (0 = none)
    // CJK sample text via unicode escapes (U+5B57 U+6A23) -
    // ExtendScript fails to parse raw CJK source without a BOM.
    var SAMPLE_TEXT = "\u5B57\u6A23Ag18";
    var CELL_W = 360;
    var CELL_H = 140;
    var MARGIN = 60;

    var docW = MARGIN * 2 + CELL_W * STROKE_WIDTHS.length;
    var docH = MARGIN * 2 + CELL_H * FONT_SIZES.length;

    var originalRulerUnits = app.preferences.rulerUnits;
    var originalTypeUnits = app.preferences.typeUnits;
    app.preferences.rulerUnits = Units.PIXELS;
    app.preferences.typeUnits = TypeUnits.POINTS;

    try {
        var doc = app.documents.add(
            UnitValue(docW, "px"),
            UnitValue(docH, "px"),
            72,                       // 72 DPI baseline: keep px == pt for unambiguous comparison
            "CalibrationBoard",
            NewDocumentMode.RGB,
            DocumentFill.WHITE
        );

        for (var row = 0; row < FONT_SIZES.length; row++) {
            for (var col = 0; col < STROKE_WIDTHS.length; col++) {
                var fontSize = FONT_SIZES[row];
                var strokePx = STROKE_WIDTHS[col];

                var layer = doc.artLayers.add();
                layer.kind = LayerKind.TEXT;

                var nameSuffix = strokePx > 0 ? ("_s" + strokePx) : "_s0";
                layer.name = "TXT_cal_" + fontSize + "pt" + nameSuffix;

                var ti = layer.textItem;
                ti.kind = TextType.POINTTEXT;
                ti.contents = SAMPLE_TEXT;
                ti.size = UnitValue(fontSize, "pt");
                try { ti.font = "SourceHanSansTC-Bold"; } catch (eFont) { /* keep default font */ }
                ti.color = makeColor(34, 34, 34);
                ti.position = [
                    UnitValue(MARGIN + col * CELL_W, "px"),
                    UnitValue(MARGIN + row * CELL_H + fontSize, "px")
                ];

                if (strokePx > 0) {
                    applyStroke(strokePx, 255, 255, 255);
                }
            }
        }

        alert(
            "CalibrationBoard created.\n\n" +
            "Rows: font sizes " + FONT_SIZES.join(" / ") + " pt\n" +
            "Columns: stroke widths " + STROKE_WIDTHS.join(" / ") + " px\n\n" +
            "Next steps:\n" +
            "1. Save as CalibrationBoard.psd\n" +
            "2. Export with PhotoshopUiPackageExporter.jsx\n" +
            "3. Generate in Unity and overlay a 50% PS screenshot to compare"
        );
    } finally {
        app.preferences.rulerUnits = originalRulerUnits;
        app.preferences.typeUnits = originalTypeUnits;
    }

    function makeColor(r, g, b) {
        var c = new SolidColor();
        c.rgb.red = r;
        c.rgb.green = g;
        c.rgb.blue = b;
        return c;
    }

    // Apply a Layer Style outside stroke to the active layer via ActionDescriptor.
    function applyStroke(sizePx, r, g, b) {
        var desc = new ActionDescriptor();
        var ref = new ActionReference();
        ref.putProperty(charIDToTypeID("Prpr"), charIDToTypeID("Lefx"));
        ref.putEnumerated(charIDToTypeID("Lyr "), charIDToTypeID("Ordn"), charIDToTypeID("Trgt"));
        desc.putReference(charIDToTypeID("null"), ref);

        var fx = new ActionDescriptor();
        fx.putUnitDouble(charIDToTypeID("Scl "), charIDToTypeID("#Prc"), 100);

        var stroke = new ActionDescriptor();
        stroke.putBoolean(charIDToTypeID("enab"), true);
        stroke.putEnumerated(charIDToTypeID("Styl"), charIDToTypeID("FStl"), charIDToTypeID("OutF")); // outside
        stroke.putEnumerated(charIDToTypeID("PntT"), charIDToTypeID("FrFl"), charIDToTypeID("SClr"));
        stroke.putUnitDouble(charIDToTypeID("Md  "), charIDToTypeID("#Prc"), 100);
        stroke.putUnitDouble(charIDToTypeID("Opct"), charIDToTypeID("#Prc"), 100);
        stroke.putUnitDouble(charIDToTypeID("Sz  "), charIDToTypeID("#Pxl"), sizePx);

        var color = new ActionDescriptor();
        color.putDouble(charIDToTypeID("Rd  "), r);
        color.putDouble(charIDToTypeID("Grn "), g);
        color.putDouble(charIDToTypeID("Bl  "), b);
        stroke.putObject(charIDToTypeID("Clr "), charIDToTypeID("RGBC"), color);

        fx.putObject(charIDToTypeID("FrFX"), charIDToTypeID("FrFX"), stroke);
        desc.putObject(charIDToTypeID("T   "), charIDToTypeID("Lefx"), fx);
        executeAction(charIDToTypeID("setd"), desc, DialogModes.NO);
    }
})();

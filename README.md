# Photoshop To Unity V2

Photoshop To Unity V2 is a production-oriented Photoshop-to-Unity UI workflow tool.

It exports a Photoshop UI as a UI Package, then rebuilds the layout in Unity as a uGUI + TextMeshPro Prefab. Text layers remain editable TMP objects, while non-text layers are exported as PNG sprites.

## Version

v2.12.4

## Main Workflow

### Optional Photoshop menu install

Run `PhotoshopExporter/InstallPhotoshopPlugin.jsx` once from Photoshop.
After restarting Photoshop, open the fixed entries from:

`File > Scripts > PS To Unity v2`

Use `Update / Refresh Plugin` inside the toolbox to read the latest local repository updates with `git pull --ff-only` and refresh the Photoshop menu launcher.

The menu launcher reads the JSX files from this repository folder, so normal exporter updates do not require manually loading JSX files again.

If `PhotoshopExporter/PhotoshopToSpine.jsx` exists, the toolbox also exposes it as `Photoshop To Spine`.
This keeps Spine export as a separate pipeline while sharing the same Photoshop entry point and update flow.

### Export and import

1. Run `PhotoshopExporter/PhotoshopLayerAutoNamer.jsx` in Photoshop if the PSD needs normalized English layer names.
2. Run `PhotoshopExporter/PhotoshopUiPackageExporter.jsx`.
3. Export PNG files to a chosen image folder and write a custom-named layout JSON outside that image folder.
4. In Unity, open `Tools > Photoshop UI Importer > Importer_v2`.
5. Select the UI Package folder, choose a prefab output folder, assign TMP font/material assets, then generate the prefab.

Exporter speed notes:

- The exporter keeps a `.ps_to_unity_export_cache.tsv` file in the PNG output folder.
- If the PSD is saved, unchanged, and an existing PNG still matches the cached layer signature, that PNG is skipped without opening a temporary export document.
- The cache is ignored automatically when the PSD has unsaved changes.
- First-time export uses fast layer duplicate export when possible, then falls back to the safer merged-copy path for layers Photoshop cannot duplicate directly.
- Fast duplicate export no longer hides every PSD layer up front. The slower visibility-isolated path is prepared only when fallback is needed.
- PNG saving uses Photoshop Save for Web first, then falls back to normal PNG save if needed.

Unity Atlas output:

- Enable `Output PNGs to Unity Atlas/SpriteAtlas language folder` when the selected PNG output folder should be treated as the Unity package root.
- PNGs are written to `Atlas/SpriteAtlas/Base`, `Atlas/SpriteAtlas/CHS`, `Atlas/SpriteAtlas/CHT`, or `Atlas/SpriteAtlas/EN`.
- `Base` is for art sprites. `CHS`, `CHT`, and `EN` are for simplified Chinese, traditional Chinese, and English text images.
- The exporter dialog sets the default text-layer behavior.
- To bake only a few text layers as PNG, select those text layers in Photoshop before export and leave `Export currently selected text layers as PNG overrides` enabled.
- Individual text layers can also override the default by name when a persistent rule is useful:
  - `[PNG]`, `[IMAGE]`, `[IMG]`, `TXTIMG_`, `TXT_IMG_`, `TEXTIMG_`, or `TEXT_IMG_` bakes that text as a PNG image.
  - `[TMP]`, `[TEXT]`, `TMP_`, or `TXT_` keeps that text editable as a Unity TMP node.
- All text layers remain TMP by default, regardless of font family. Use a `TmpFontMap` asset (Create > Photoshop UI Importer > Tmp Font Map) to map each exported font token to the matching TMP Font Asset. Enable `白名單外字型改為 PNG` only when intentionally baking unsupported fonts; explicit `[PNG]` naming overrides still work.
- The export dialog's `命名規則說明` button closes the modal exporter and opens a searchable local reference page in the browser, so it can stay beside Photoshop while layers are renamed.
- Add `[MERGE]` to a group name to bake its visible contents (including text, effects, and masks) into one PNG and one Unity Image node. Do not use it when child layers must remain interactive, animated, reskinnable, or editable.
- Tag a group `[SCROLL_V]` / `[SCROLL_H]` to auto-build a full ScrollView > Viewport > Content hierarchy in Unity (ScrollRect + RectMask2D). Layer masks inside the group are treated as the runtime-clipping preview: children export as full images, and the group's own mask (if any) defines the viewport window. Combine with `[GRID]`/`[V]`/`[H]` to mount the layout component on Content.

Batch font replacement (`Tools > Photoshop UI Importer > Font Replacer`):

- Analyze prefabs (folder-recursive) for TMP font/material usage, then swap fonts in one click. Only `font` and `fontSharedMaterial` are ever written — layout, sizes, colors, and sprites are untouched. Outline-style materials are cloned onto the target font's atlas automatically.
- The built-in font asset factory creates Dynamic SDF Font Assets from project `.ttf/.otf` files (parameters copied from a template font for 1:1 material transfer) and auto-registers them into `TmpFontMap`. The Importer's `掃描 Package 字型` button reports every fontToken as mapped / missing font asset (one-click create) / missing font file.

## Included Tools

- `PhotoshopExporter/PhotoshopLayerAutoNamer.jsx`
- `PhotoshopExporter/PhotoshopUiPackageExporter.jsx`
- `PhotoshopExporter/PhotoshopToolboxHub.jsx`
- Optional: `PhotoshopExporter/PhotoshopToSpine.jsx`
- `Assets/Editor/PhotoshopUiImporter/`

## Documentation

- [中文 README](README_zh.md)
- [完整使用說明 GUIDE_zh.html](GUIDE_zh.html)

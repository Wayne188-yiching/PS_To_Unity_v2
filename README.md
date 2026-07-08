# Photoshop To Unity V2

Photoshop To Unity V2 is a production-oriented Photoshop-to-Unity UI workflow tool.

It exports a Photoshop UI as a UI Package, then rebuilds the layout in Unity as a uGUI + TextMeshPro Prefab. Text layers remain editable TMP objects, while non-text layers are exported as PNG sprites.

## Version

v2.9.1

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

## Included Tools

- `PhotoshopExporter/PhotoshopLayerAutoNamer.jsx`
- `PhotoshopExporter/PhotoshopUiPackageExporter.jsx`
- `PhotoshopExporter/PhotoshopToolboxHub.jsx`
- Optional: `PhotoshopExporter/PhotoshopToSpine.jsx`
- `Assets/Editor/PhotoshopUiImporter/`

## Documentation

- [中文 README](README_zh.md)
- [完整使用說明 GUIDE_zh.html](GUIDE_zh.html)

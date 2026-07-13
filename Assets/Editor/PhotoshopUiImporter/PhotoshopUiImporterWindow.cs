using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace PhotoshopToUnity.EditorImporter
{
    public sealed class PhotoshopUiImporterWindow : EditorWindow
    {
        private string packageRootPath;
        private string layoutJsonPath;
        private string sourceImageFolder;
        private string projectFolder = string.Empty;
        private string importFolder = string.Empty;
        private string prefabFolder = string.Empty;
        private SkinMap skinMap;
        private TMP_FontAsset defaultTmpFontAsset;
        private Material defaultTmpMaterialPreset;
        // v2.10：fontToken → Font Asset 對應表（選填），多字型 PSD 用；null = 全部套預設字型（舊行為）。
        private TmpFontMap tmpFontMap;
        private bool autoReferenceResolution = true;
        private Vector2 referenceResolution = new Vector2(1920f, 1080f);
        private bool useResponsiveAnchor;
        private Vector2 scrollPosition;
        private string statusMessage;
        private MessageType statusType = MessageType.Info;
        private bool showAdvancedPackage;
        private bool showAdvancedOutput;
        private bool showReskinFoldout;
        // U5：套用 Package 後立刻記錄是否含文字節點，供 Typography 區即時標紅 / Action 區擋按鈕
        private bool packageHasTextNode;
        private string materialLibraryFolder = string.Empty;
        // F2 補償係數：SDF 描邊視覺 falloff 比 PS 重，使用者用校準板回推合適值。
        // 值由 OnEnable 從 EditorPrefs 還原；範圍 0.3 ~ 1.5，預設 1.0。
        private float outlineThicknessMultiplier = 1.0f;
        private const string PrefKeyOutlineThicknessMultiplier =
            "PhotoshopUiImporter.OutlineThicknessMultiplier";
        private string reskinArtSourceFolder = string.Empty;
        private string reskinTargetFolder = string.Empty;
        private System.Collections.Generic.List<string> reskinMissingFiles;
        private Vector2 reskinMissingScrollPos;
        private System.Collections.Generic.List<string> reskinPendingOverwrites;
        private Vector2 reskinOverwriteScrollPos;
        private string reskinScannedSourceFolder;
        private string reskinScannedTargetFolder;
        private PsUiSkinTheme activeSkinTheme;
        private const string ToolVersion = "2.10.1";
        private const string GitHubUrl = "https://github.com/Wayne188-yiching/PS_To_Unity_v2";

        [MenuItem("Tools/Photoshop UI Importer/Importer_v2")]
        public static void Open()
        {
            var window = GetWindow<PhotoshopUiImporterWindow>("Importer_v2");
            window.minSize = new Vector2(560, 600);
            window.Show();
        }

        private void OnEnable()
        {
            outlineThicknessMultiplier = EditorPrefs.GetFloat(PrefKeyOutlineThicknessMultiplier, 1.0f);
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            try
            {
                DrawHeader();
                DrawPackageSection();
                DrawOutputSection();
                DrawTypographySection();
                DrawActionSection();
                DrawReskinSection();

                if (!string.IsNullOrWhiteSpace(statusMessage))
                {
                    EditorGUILayout.Space(10);
                    EditorGUILayout.HelpBox(statusMessage, statusType);
                }

                EditorGUILayout.Space(16);
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (System.Exception exception)
            {
                statusMessage = $"Importer UI 發生錯誤：{exception.Message}";
                statusType = MessageType.Error;
                Debug.LogException(exception);
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Photoshop UI Importer", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"UI Package -> Unity Prefab  v{ToolVersion}", EditorStyles.miniLabel);
                if (GUILayout.Button("從 GitHub 更新工具", EditorStyles.miniButton, GUILayout.Width(140)))
                {
                    UpdateFromGitHub();
                }
            }
            EditorGUILayout.Space(8);
        }

        private void DrawPackageSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("1. Photoshop UI Package", EditorStyles.boldLabel);
                DrawFolderPathField("Package 資料夾", ref packageRootPath, false);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("套用 Package", GUILayout.Height(28)))
                    {
                        ApplyPackageRoot();
                    }

                    if (GUILayout.Button("只驗證資料", GUILayout.Height(28)))
                    {
                        ValidateLayout();
                    }
                }

                DrawReadOnlyPath("Layout JSON", layoutJsonPath);
                DrawReadOnlyPath("PNG 來源資料夾", sourceImageFolder);

                EditorGUILayout.HelpBox(
                    "Package 資料夾通常包含一個 layout JSON，以及 images / sprites / *_images 圖片資料夾。若圖片已放在本 Unity 專案的 Assets 內，匯入時會直接使用該資料夾。",
                    MessageType.Info);

                showAdvancedPackage = EditorGUILayout.Foldout(showAdvancedPackage, "進階設定", true);
                if (showAdvancedPackage)
                {
                    DrawFilePathField("Layout JSON", ref layoutJsonPath, "json");
                    DrawFolderPathField("PNG 來源資料夾", ref sourceImageFolder, false);

                    if (GUILayout.Button("從 JSON 推測圖片資料夾", GUILayout.Height(24)))
                    {
                        GuessSourceImageFolderFromLayout();
                    }
                }
            }
        }

        private void DrawOutputSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("2. Unity 生成位置", EditorStyles.boldLabel);

                projectFolder = EditorGUILayout.TextField("專案資料夾名稱", projectFolder);
                DrawReferenceResolutionControls();
                var standardImport = GetStandardImportFolder();
                var standardPrefab = GetStandardPrefabFolder();
                ApplyStandardFoldersIfUnset(standardImport, standardPrefab);
                if (!string.IsNullOrWhiteSpace(standardImport))
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField("圖片標準路徑（Atlas）", standardImport);
                        EditorGUILayout.TextField("Prefab 標準路徑", standardPrefab);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "請填寫專案資料夾名稱，產物將存放於：\n" +
                        "  圖片：Assets/Temp/{名稱}/Atlas\n" +
                        "  Prefab：Assets/Temp/{名稱}/Prefab",
                        MessageType.Info);
                }

                var sourceAssetPath = PathUtility.ToProjectRelativeAssetPath(sourceImageFolder);
                if (PathUtility.IsAssetPath(sourceAssetPath))
                {
                    EditorGUILayout.HelpBox($"圖片來源已在 Unity 專案內，會直接使用：{sourceAssetPath}", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox($"圖片來源若在 Unity 專案外，Generate 時會複製到：{importFolder}", MessageType.None);
                }

                // U3：主流程改以「建立專案資料夾」為主視覺按鈕（最常用），其餘變體收進進階區。
                var createStyle = new GUIStyle(GUI.skin.button)
                {
                    fontStyle = FontStyle.Bold
                };
                if (GUILayout.Button("建立專案資料夾（含 Atlas / Font / Prefab 標準結構）", createStyle, GUILayout.Height(34)))
                {
                    CreateProjectFolders();
                }

                showAdvancedOutput = EditorGUILayout.Foldout(showAdvancedOutput, "進階設定（路徑變體與手動覆寫）", true);
                if (showAdvancedOutput)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("套用標準輸出路徑", GUILayout.Height(24)))
                        {
                            UseStandardOutputFolders();
                        }

                        if (GUILayout.Button("圖片位置跟隨來源", GUILayout.Height(24)))
                        {
                            AutoSelectImportFolderFromSource();
                        }
                    }
                    DrawFolderPathField("Unity 圖片匯入資料夾", ref importFolder, true);
                    DrawFolderPathField("Prefab 輸出資料夾", ref prefabFolder, true);
                }
            }
        }

        private void DrawReferenceResolutionControls()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                autoReferenceResolution = EditorGUILayout.Toggle("參考解析度自動跟隨 Layout", autoReferenceResolution);
                // U8：toggle 開啟時 Layout 尺寸會自動套用，按鈕僅在手動模式下顯示
                if (!autoReferenceResolution && GUILayout.Button("套用 Layout 尺寸", GUILayout.Width(120)))
                {
                    ApplyReferenceResolutionFromLayout(true);
                }
            }

            using (new EditorGUI.DisabledScope(autoReferenceResolution))
            {
                referenceResolution = EditorGUILayout.Vector2Field("Prefab 參考解析度", referenceResolution);
            }

            if (referenceResolution.x <= 0f)
            {
                referenceResolution.x = 1920f;
            }

            if (referenceResolution.y <= 0f)
            {
                referenceResolution.y = 1080f;
            }

            useResponsiveAnchor = EditorGUILayout.ToggleLeft(
                "啟用響應式 anchor（實驗性：套用 PS anchor 與 group 實際尺寸）",
                useResponsiveAnchor);
        }

        private void DrawTypographySection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("3. 文字與材質", EditorStyles.boldLabel);

                // U5：含文字節點而缺字型 → 欄位標紅 + 明確錯誤 HelpBox
                var needsFont = packageHasTextNode && defaultTmpFontAsset == null;
                var prevColor = GUI.color;
                if (needsFont)
                {
                    GUI.color = new Color(1f, 0.55f, 0.55f);
                }
                defaultTmpFontAsset = (TMP_FontAsset)EditorGUILayout.ObjectField("預設 TMP Font Asset", defaultTmpFontAsset, typeof(TMP_FontAsset), false);
                GUI.color = prevColor;
                if (needsFont)
                {
                    EditorGUILayout.HelpBox("此 UI Package 含文字節點，請先指定預設 TMP Font Asset，否則 Generate 無法執行。", MessageType.Error);
                }

                defaultTmpMaterialPreset = (Material)EditorGUILayout.ObjectField("預設 TMP 材質球", defaultTmpMaterialPreset, typeof(Material), false);

                // v2.10：多字型支援——依 layout.json 的 fontToken 比對關鍵字自動套字型。
                tmpFontMap = (TmpFontMap)EditorGUILayout.ObjectField(
                    new GUIContent(
                        "字型對應表（選填）",
                        "TmpFontMap：fontToken 關鍵字 → TMP Font Asset。\n" +
                        "PS 端字型白名單保持 TMP 的文字，靠這張表套正確字型；\n" +
                        "沒對到的 fontToken 用預設字型並在 Generate 後警告。\n" +
                        "建立：Project 視窗右鍵 Create > Photoshop UI Importer > Tmp Font Map。"),
                    tmpFontMap, typeof(TmpFontMap), false);
                if (tmpFontMap != null && (tmpFontMap.entries == null || tmpFontMap.entries.Count == 0))
                {
                    EditorGUILayout.HelpBox("字型對應表是空的：請在該 TmpFontMap 資產內新增 keyword → Font Asset 項目。", MessageType.Warning);
                }

                DrawFolderPathField("TMP 材質球資料夾（選填）", ref materialLibraryFolder, true);
                skinMap = (SkinMap)EditorGUILayout.ObjectField("Skin Map（選填）", skinMap, typeof(SkinMap), false);

                if (!string.IsNullOrWhiteSpace(materialLibraryFolder) && AssetDatabase.IsValidFolder(materialLibraryFolder))
                {
                    var matCount = AssetDatabase.FindAssets("t:Material", new[] { materialLibraryFolder }).Length;
                    EditorGUILayout.HelpBox($"材質球資料夾：找到 {matCount} 顆材質球，Generate 時優先比對，找不到才自動新增。", MessageType.Info);
                }

                EditorGUILayout.HelpBox(
                    "若 UI Package 含 text 節點，至少要指定預設 TMP Font Asset。若要穩定重現文字風格，建議同時指定 TMP 材質球。",
                    MessageType.Info);

                // F2 補償：用校準板比對後可微調，存 EditorPrefs，跨 session 保留。
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("描邊厚度補償（v2.6）", EditorStyles.boldLabel);
                using (var check = new EditorGUI.ChangeCheckScope())
                {
                    var newValue = EditorGUILayout.Slider(
                        new GUIContent(
                            "描邊厚度補償係數",
                            "Unity SDF 描邊邊緣是半透明 falloff，視覺重心比 PS 重。\n" +
                            "用 CalibrationBoard 比對：Unity 偏厚 → 調低（如 0.85）；偏細 → 調高。\n" +
                            "預設 1.0（不補償，物理寬度 = PS）。"),
                        outlineThicknessMultiplier, 0.3f, 1.5f);
                    if (check.changed)
                    {
                        outlineThicknessMultiplier = newValue;
                        EditorPrefs.SetFloat(PrefKeyOutlineThicknessMultiplier, newValue);
                    }
                }
                if (Mathf.Abs(outlineThicknessMultiplier - 1.0f) < 0.001f)
                {
                    EditorGUILayout.HelpBox(
                        "目前 = 1.0（不補償）。若描邊比 PS 視覺偏厚，調低（如 0.85）後重新 Generate；用 CalibrationBoard 疊圖比對找到合適值。",
                        MessageType.None);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        $"目前補償係數 = {outlineThicknessMultiplier:0.00}（預設 1.0）。Generate 時所有 _OutlineWidth 都會乘上這個數值。",
                        MessageType.Info);
                }
            }
        }

        private void DrawReskinSection()
        {
            // U7：換皮工具屬獨立、低頻、具破壞性的功能，預設摺疊收進主流程之後。
            // Foldout 必須包在 VerticalScope 內，否則 Unity 6 IMGUI 會丟出
            // kDontSaveInEditor / kAllowDontSaveObjectsToPersistent 的 assertion。
            EditorGUILayout.Space(8);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                showReskinFoldout = EditorGUILayout.Foldout(
                    showReskinFoldout,
                    "換皮工具（低頻 / 具破壞性，預設收起）",
                    true);
                if (!showReskinFoldout)
                {
                    return;
                }

                EditorGUILayout.LabelField("換皮工具（美術圖覆蓋）", EditorStyles.boldLabel);
                DrawFolderPathField("美術來源資料夾", ref reskinArtSourceFolder, false);
                DrawFolderPathField("Unity 目標資料夾", ref reskinTargetFolder, true);

                // 路徑變更後舊掃描結果失效，必須重新掃描才能覆蓋
                if (reskinPendingOverwrites != null &&
                    (reskinScannedSourceFolder != reskinArtSourceFolder ||
                     reskinScannedTargetFolder != reskinTargetFolder))
                {
                    reskinPendingOverwrites = null;
                    reskinMissingFiles = null;
                }

                if (GUILayout.Button("掃描換皮（預覽，不會修改檔案）", GUILayout.Height(34)))
                {
                    ScanReskin();
                }

                if (reskinPendingOverwrites != null)
                {
                    EditorGUILayout.Space(4);
                    var missingCount = reskinMissingFiles != null ? reskinMissingFiles.Count : 0;
                    EditorGUILayout.LabelField(
                        $"掃描結果：將覆蓋 {reskinPendingOverwrites.Count} 張 / 缺少 {missingCount} 張",
                        EditorStyles.boldLabel);

                    if (reskinPendingOverwrites.Count > 0)
                    {
                        reskinOverwriteScrollPos = EditorGUILayout.BeginScrollView(reskinOverwriteScrollPos, GUILayout.MaxHeight(120));
                        foreach (var name in reskinPendingOverwrites)
                        {
                            EditorGUILayout.LabelField(name, EditorStyles.miniLabel);
                        }
                        EditorGUILayout.EndScrollView();

                        if (GUILayout.Button($"確認覆蓋 {reskinPendingOverwrites.Count} 張 PNG", GUILayout.Height(34)))
                        {
                            ApplyReskinOverwrites();
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("美術來源資料夾內沒有與目標同名的 PNG，無可覆蓋項目。", MessageType.Info);
                    }
                }

                if (reskinMissingFiles != null && reskinMissingFiles.Count > 0)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField($"新美術待辦清單（{reskinMissingFiles.Count} 筆，請交給美術人員）", EditorStyles.boldLabel);
                    reskinMissingScrollPos = EditorGUILayout.BeginScrollView(reskinMissingScrollPos, GUILayout.MaxHeight(120));
                    foreach (var name in reskinMissingFiles)
                    {
                        EditorGUILayout.LabelField(name, EditorStyles.miniLabel);
                    }
                    EditorGUILayout.EndScrollView();
                }
            }

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("SkinTheme 批次換皮", EditorStyles.boldLabel);
                activeSkinTheme = (PsUiSkinTheme)EditorGUILayout.ObjectField(
                    "Skin Theme", activeSkinTheme, typeof(PsUiSkinTheme), false);

                if (activeSkinTheme != null)
                {
                    var folder = activeSkinTheme.targetPrefabFolderAsset != null
                        ? AssetDatabase.GetAssetPath(activeSkinTheme.targetPrefabFolderAsset)
                        : "（未設定）";
                    EditorGUILayout.LabelField($"目標資料夾：{folder}", EditorStyles.miniLabel);

                    DrawFolderPathField("新美術來源資料夾", ref activeSkinTheme.sourceArtFolder, false);
                    if (GUI.changed) EditorUtility.SetDirty(activeSkinTheme);

                    if (GUILayout.Button("掃描 Prefab，自動填入舊 Sprite", GUILayout.Height(28)))
                        ScanSkinTheme();

                    if (GUILayout.Button("套用換皮到所有 Prefab", GUILayout.Height(34)))
                        ExecuteSkinTheme();
                }
            }
        }

        private void ScanSkinTheme()
        {
            if (activeSkinTheme == null) return;

            if (activeSkinTheme.targetPrefabFolderAsset == null)
            {
                SetStatus("請先拖入目標 Prefab 資料夾再掃描。", MessageType.Warning);
                return;
            }

            var added = PsUiSkinApplier.ScanAndFillOldSprites(activeSkinTheme);
            SetStatus(
                added > 0
                    ? $"掃描完成，找到 {added} 個 Sprite。請在 SkinTheme Inspector 為每筆填入對應的新 Sprite。"
                    : "掃描完成，沒有新增項目（所有 Sprite 已在清單中）。",
                added > 0 ? MessageType.Info : MessageType.Warning);
        }

        private void ExecuteSkinTheme()
        {
            if (activeSkinTheme == null) return;

            var folder = activeSkinTheme.targetPrefabFolderAsset != null
                ? AssetDatabase.GetAssetPath(activeSkinTheme.targetPrefabFolderAsset)
                : "（未設定）";

            if (!EditorUtility.DisplayDialog(
                "套用換皮",
                $"目標資料夾：{folder}\n\n" +
                "• 同名項目（New Sprite 為空或同名）→ 從美術來源資料夾覆蓋 PNG 檔案\n" +
                "• 不同名項目（New Sprite 不同）→ 替換 Prefab 裡的 Sprite 參照\n\n" +
                "此操作直接修改檔案，確定繼續嗎？",
                "確定套用",
                "取消"))
                return;

            var r = PsUiSkinApplier.Apply(activeSkinTheme);

            if (!string.IsNullOrEmpty(r.errorMessage))
            {
                SetStatus(r.errorMessage, MessageType.Error);
                return;
            }

            var parts = new System.Collections.Generic.List<string>();
            if (r.filesOverwritten > 0)
                parts.Add($"覆蓋 {r.filesOverwritten} 個 PNG");
            if (r.prefabsChanged > 0)
                parts.Add($"{r.prefabsChanged} 個 Prefab 替換 {r.spritesReplaced} 個 Sprite 參照");
            if (r.missingFiles != null && r.missingFiles.Count > 0)
                parts.Add($"找不到 {r.missingFiles.Count} 個（見 Console）");

            var msg = parts.Count > 0
                ? "換皮完成：" + string.Join("，", parts) + "。"
                : "換皮完成，沒有任何變更。";

            if (r.missingFiles != null)
                foreach (var f in r.missingFiles)
                    Debug.LogWarning($"[SkinTheme] 找不到：{f}");

            SetStatus(msg, r.filesOverwritten + r.prefabsChanged > 0 ? MessageType.Info : MessageType.Warning);
        }

        private void ScanReskin()
        {
            reskinPendingOverwrites = null;
            reskinMissingFiles = null;

            if (string.IsNullOrWhiteSpace(reskinArtSourceFolder) || !Directory.Exists(reskinArtSourceFolder))
            {
                SetStatus("請先選擇有效的美術來源資料夾。", MessageType.Warning);
                return;
            }

            if (!PathUtility.IsAssetPath(reskinTargetFolder))
            {
                SetStatus("Unity 目標資料夾必須位於專案 Assets 之下。", MessageType.Error);
                return;
            }

            var targetAbsPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", reskinTargetFolder));
            if (!Directory.Exists(targetAbsPath))
            {
                SetStatus("Unity 目標資料夾不存在，請確認路徑。", MessageType.Error);
                return;
            }

            var targetFiles = Directory.GetFiles(targetAbsPath, "*.png", SearchOption.TopDirectoryOnly);
            if (targetFiles.Length == 0)
            {
                SetStatus("Unity 目標資料夾內找不到 PNG 圖片。", MessageType.Warning);
                return;
            }

            var pendingOverwrites = new System.Collections.Generic.List<string>();
            var missingFiles = new System.Collections.Generic.List<string>();
            foreach (var targetFile in targetFiles)
            {
                var fileName = Path.GetFileName(targetFile);
                if (File.Exists(Path.Combine(reskinArtSourceFolder, fileName)))
                {
                    pendingOverwrites.Add(fileName);
                }
                else
                {
                    missingFiles.Add(fileName);
                }
            }

            reskinPendingOverwrites = pendingOverwrites;
            reskinMissingFiles = missingFiles;
            reskinScannedSourceFolder = reskinArtSourceFolder;
            reskinScannedTargetFolder = reskinTargetFolder;

            SetStatus(
                pendingOverwrites.Count > 0
                    ? $"掃描完成：將覆蓋 {pendingOverwrites.Count} 張 / 缺少 {missingFiles.Count} 張。尚未修改任何檔案，請確認清單後按「確認覆蓋」。"
                    : $"掃描完成：沒有同名 PNG 可覆蓋，缺少 {missingFiles.Count} 張。尚未修改任何檔案。",
                pendingOverwrites.Count > 0 ? MessageType.Info : MessageType.Warning);
        }

        private void ApplyReskinOverwrites()
        {
            if (reskinPendingOverwrites == null || reskinPendingOverwrites.Count == 0)
            {
                return;
            }

            if (!EditorUtility.DisplayDialog(
                "確認換皮覆蓋",
                $"即將以「{reskinScannedSourceFolder}」的同名 PNG\n" +
                $"覆蓋「{reskinScannedTargetFolder}」內共 {reskinPendingOverwrites.Count} 張圖片。\n\n" +
                "此操作會直接覆蓋檔案且無法復原，確定繼續嗎？",
                "確定覆蓋",
                "取消"))
            {
                return;
            }

            var targetAbsPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", reskinScannedTargetFolder));
            var overwriteCount = 0;
            var skipped = new System.Collections.Generic.List<string>();
            foreach (var fileName in reskinPendingOverwrites)
            {
                var sourcePath = Path.Combine(reskinScannedSourceFolder, fileName);
                var targetPath = Path.Combine(targetAbsPath, fileName);
                if (File.Exists(sourcePath) && File.Exists(targetPath))
                {
                    File.Copy(sourcePath, targetPath, overwrite: true);
                    overwriteCount++;
                }
                else
                {
                    skipped.Add(fileName);
                }
            }

            AssetDatabase.Refresh();
            reskinPendingOverwrites = null;

            var msg = $"換皮完成。已覆蓋 {overwriteCount} 張圖片。";
            if (skipped.Count > 0)
            {
                msg += $" 有 {skipped.Count} 張在掃描後遺失，已跳過：{string.Join("、", skipped)}。";
            }
            if (reskinMissingFiles != null && reskinMissingFiles.Count > 0)
            {
                msg += $" 找不到對應美術：{reskinMissingFiles.Count} 張（見下方清單）。";
                SetStatus(msg, MessageType.Warning);
            }
            else
            {
                SetStatus(msg, skipped.Count > 0 ? MessageType.Warning : MessageType.Info);
            }
        }

        private void DrawActionSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("4. 執行", EditorStyles.boldLabel);

                // U4：就緒狀態 = 前置條件齊備才允許 Generate；未就緒列出缺項，避免按下後得到模糊錯誤。
                var missing = ComputeMissingPrerequisites();
                if (missing.Count > 0)
                {
                    EditorGUILayout.HelpBox(
                        "尚未就緒，缺少：" + string.Join("、", missing),
                        MessageType.Warning);
                }

                // U6：原 Validate + 預覽（Dry-run）合併為「檢查 Layout」，與主視覺 Generate 兩鈕並列。
                if (GUILayout.Button("檢查 Layout（不生成 Prefab）", GUILayout.Height(28)))
                {
                    CheckLayout();
                }

                using (new EditorGUI.DisabledScope(missing.Count > 0))
                {
                    var generateStyle = new GUIStyle(GUI.skin.button)
                    {
                        fontStyle = FontStyle.Bold,
                        fontSize = 14
                    };
                    if (GUILayout.Button("Generate Prefab", generateStyle, GUILayout.Height(44)))
                    {
                        GeneratePrefab();
                    }
                }
            }
        }

        // U4：彙整 Generate 前置條件，回傳缺項中文敘述。空清單代表就緒。
        private System.Collections.Generic.List<string> ComputeMissingPrerequisites()
        {
            var missing = new System.Collections.Generic.List<string>();
            if (string.IsNullOrWhiteSpace(layoutJsonPath) || !File.Exists(layoutJsonPath))
            {
                missing.Add("尚未套用 Package");
            }
            if (string.IsNullOrWhiteSpace(projectFolder))
            {
                missing.Add("尚未填寫專案資料夾名稱");
            }
            if (packageHasTextNode && defaultTmpFontAsset == null)
            {
                missing.Add("此 Package 含文字節點，請指定預設 TMP Font Asset");
            }
            return missing;
        }

        // U6：合併原 Validate（讀 layout / 報節點數）與 Preview（圖片數量、缺字型警告）為單一檢查動作。
        private void CheckLayout()
        {
            if (!LayoutReader.TryRead(layoutJsonPath, out var layout, out var result))
            {
                SetStatus(BuildErrorMessage("Layout 檢查失敗", result.errors), MessageType.Error);
                return;
            }
            LogLayoutWarnings(result);

            ApplyReferenceResolutionFromLayout(layout, false);
            packageHasTextNode = ContainsTextNode(layout?.nodes);

            var totalNodes = CountNodes(layout.nodes);
            var hasText = packageHasTextNode;

            var imageInfo = "尚未指定圖片來源資料夾";
            if (!string.IsNullOrWhiteSpace(sourceImageFolder) && Directory.Exists(sourceImageFolder))
            {
                var pngCount = Directory.GetFiles(sourceImageFolder, "*.png", SearchOption.TopDirectoryOnly).Length;
                imageInfo = $"來源資料夾找到 {pngCount} 張 PNG";
            }

            var sb = new StringBuilder();
            sb.AppendLine("Layout 檢查成功。");
            sb.AppendLine($"節點總數：{totalNodes}");
            sb.AppendLine($"含文字節點：{(hasText ? "是" : "否")}");
            sb.AppendLine($"圖片來源：{imageInfo}");
            sb.AppendLine($"圖片匯入路徑：{(string.IsNullOrWhiteSpace(importFolder) ? "（未設定）" : importFolder)}");
            sb.Append($"Prefab 路徑：{(string.IsNullOrWhiteSpace(prefabFolder) ? "（未設定）" : prefabFolder)}");
            if (hasText && defaultTmpFontAsset == null)
            {
                sb.AppendLine();
                sb.Append("⚠ 含文字節點，但尚未指定 TMP 字型資源");
            }

            SetStatus(sb.ToString(), hasText && defaultTmpFontAsset == null ? MessageType.Warning : MessageType.Info);
        }

        private void ValidateLayout()
        {
            if (!LayoutReader.TryRead(layoutJsonPath, out var layout, out var result))
            {
                SetStatus(BuildErrorMessage("Layout 驗證失敗", result.errors), MessageType.Error);
                return;
            }
            LogLayoutWarnings(result);

            ApplyReferenceResolutionFromLayout(layout, false);
            packageHasTextNode = ContainsTextNode(layout?.nodes);
            SetStatus($"Layout 驗證成功。節點數：{CountNodes(layout.nodes)}", MessageType.Info);
        }

        // U5：套用 Package 後重新讀 Layout 判斷有無文字節點，失敗時保守不阻擋
        private void RefreshPackageTextNodeFlag()
        {
            if (string.IsNullOrWhiteSpace(layoutJsonPath) || !File.Exists(layoutJsonPath))
            {
                packageHasTextNode = false;
                return;
            }

            if (LayoutReader.TryRead(layoutJsonPath, out var layout, out _))
            {
                packageHasTextNode = ContainsTextNode(layout?.nodes);
            }
        }

        private void GeneratePrefab()
        {
            try
            {
                GeneratePrefabInternal();
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                SetStatus($"Prefab 生成失敗：{exception.Message}", MessageType.Error);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void GeneratePrefabInternal()
        {
            EditorUtility.DisplayProgressBar("生成 Prefab", "讀取 Layout JSON...", 0.1f);
            if (!LayoutReader.TryRead(layoutJsonPath, out var layout, out var readResult))
            {
                SetStatus(BuildErrorMessage("Layout 驗證失敗", readResult.errors), MessageType.Error);
                return;
            }
            LogLayoutWarnings(readResult);
            ApplyReferenceResolutionFromLayout(layout, false);

            EnsureSourceImageFolderFromLayout();
            AutoSelectImportFolderFromSource();

            if (!NormalizeAssetFolder(ref importFolder, "Unity 圖片匯入資料夾") ||
                !NormalizeAssetFolder(ref prefabFolder, "Prefab 輸出資料夾"))
            {
                return;
            }

            if (!ValidateTypography(layout))
            {
                return;
            }

            EditorUtility.DisplayProgressBar("生成 Prefab", "匯入圖片...", 0.4f);
            var importResult = ImageImportService.ImportImages(layout, sourceImageFolder, importFolder);
            if (!importResult.IsValid)
            {
                SetStatus(BuildErrorMessage("圖片匯入失敗", importResult.errors), MessageType.Error);
                return;
            }

            EditorUtility.DisplayProgressBar("生成 Prefab", "生成 Prefab 節點...", 0.75f);
            var skinResolver = new SkinResolver(skinMap, importResult.sprites);
            var generatedMaterialFolder = string.IsNullOrWhiteSpace(projectFolder)
                ? "Assets/GeneratedMaterials"
                : $"Assets/Temp/{projectFolder}/Font/GeneratedMaterials";
            var tmpMapper = new TmpMapper(
                defaultTmpFontAsset,
                defaultTmpMaterialPreset,
                generatedMaterialFolder,
                string.IsNullOrWhiteSpace(materialLibraryFolder) ? null : materialLibraryFolder,
                outlineThicknessMultiplier,
                tmpFontMap);
            var backend = new UGuiTmpPrefabBackend();
            var prefabName = Path.GetFileNameWithoutExtension(layoutJsonPath);

            var prefab = backend.GeneratePrefab(new PrefabGenerationContext
            {
                layout = layout,
                importedSprites = importResult.sprites,
                skinResolver = skinResolver,
                tmpMapper = tmpMapper,
                prefabOutputFolder = prefabFolder,
                prefabName = prefabName,
                referenceResolution = referenceResolution,
                useResponsiveAnchor = useResponsiveAnchor
            });

            EditorUtility.DisplayProgressBar("生成 Prefab", "完成...", 1.0f);
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            // v2.8.1 像素去重統計（解碼後 raw RGBA 相同 → 合併到同一個 sprite，PNG 實體被刪）
            var dedupHint = importResult.dedupedSpriteCount > 0
                ? $"　像素去重合併：{importResult.dedupedSpriteCount} 張（省 {FormatByteSize(importResult.dedupedSpriteBytes)}）"
                : string.Empty;

            // F3 + v2.10：把描邊超限與 fontToken 未對應警告聚合後一次顯示，並輸出至 Console 方便回查節點名稱。
            foreach (var w in tmpMapper.OutlineOverflowWarnings)
                Debug.LogWarning($"[OutlineOverflow] {w}");
            foreach (var w in tmpMapper.FontTokenWarnings)
                Debug.LogWarning($"[FontToken] {w}");

            if (tmpMapper.OutlineOverflowWarnings.Count > 0 || tmpMapper.FontTokenWarnings.Count > 0)
            {
                var notes = new System.Collections.Generic.List<string>();
                if (tmpMapper.OutlineOverflowWarnings.Count > 0)
                    notes.Add($"{tmpMapper.OutlineOverflowWarnings.Count} 個文字描邊超出 SDF 物理上限被截斷（建議按 Console 建議值重建 Font Asset 的 atlasPadding）");
                if (tmpMapper.FontTokenWarnings.Count > 0)
                    notes.Add($"{tmpMapper.FontTokenWarnings.Count} 種字型沒對到字型對應表，已用預設字型");

                var summary = $"Prefab 生成完成（{AssetDatabase.GetAssetPath(prefab)}）。{dedupHint}\n" +
                              $"⚠ {string.Join("；", notes)}，詳見 Console 警告。";
                SetStatus(summary, MessageType.Warning);
            }
            else
            {
                SetStatus($"Prefab 生成完成：{AssetDatabase.GetAssetPath(prefab)}{dedupHint}", MessageType.Info);
            }
        }

        private static string FormatByteSize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
            return $"{bytes / (1024.0 * 1024.0):0.00} MB";
        }


        private string GetStandardImportFolder()
        {
            return string.IsNullOrWhiteSpace(projectFolder)
                ? string.Empty
                : $"Assets/Temp/{projectFolder}/Atlas";
        }

        private string GetStandardPrefabFolder()
        {
            return string.IsNullOrWhiteSpace(projectFolder)
                ? string.Empty
                : $"Assets/Temp/{projectFolder}/Prefab";
        }

        private void UseStandardOutputFolders()
        {
            var standardImport = GetStandardImportFolder();
            if (string.IsNullOrWhiteSpace(standardImport))
            {
                SetStatus("請先填寫專案資料夾名稱，才能套用標準輸出路徑。", MessageType.Warning);
                return;
            }

            importFolder = standardImport;
            prefabFolder = GetStandardPrefabFolder();
            SetStatus("已套用標準輸出路徑。", MessageType.Info);
        }

        private void CreateProjectFolders()
        {
            if (string.IsNullOrWhiteSpace(projectFolder))
            {
                SetStatus("請先填寫專案資料夾名稱。", MessageType.Warning);
                return;
            }

            var root = $"Assets/Temp/{projectFolder}";

            foreach (var folder in new[] { "Animation", "Atlas", "Font", "Fx", "Prefab", "Spine", "TimeLine" })
                EnsureAssetFolder($"{root}/{folder}");

            var atlasFolder = $"{root}/Atlas/SpriteAtlas";
            foreach (var lang in new[] { "Base", "CHS", "CHT", "EN" })
                EnsureAssetFolder($"{atlasFolder}/{lang}");

            EnsureAssetFolder($"{root}/Fx/Material");
            EnsureAssetFolder($"{root}/Fx/Texture");

            AssetDatabase.Refresh();
            CreateOrUpdateSpriteAtlas(atlasFolder);

            importFolder = GetStandardImportFolder();
            prefabFolder = GetStandardPrefabFolder();
            SetStatus($"已建立專案資料夾並設定 SpriteAtlas：{root}", MessageType.Info);
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                return;
            var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent))
                return;
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetPath));
        }

        private static void CreateOrUpdateSpriteAtlas(string atlasFolder)
        {
            var atlasPath = $"{atlasFolder}/SpriteAtlas.spriteatlas";
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            if (atlas == null)
            {
                atlas = new SpriteAtlas();
                AssetDatabase.CreateAsset(atlas, atlasPath);
                atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            }

            var folderObject = AssetDatabase.LoadAssetAtPath<Object>(atlasFolder);
            if (folderObject != null)
                SpriteAtlasExtensions.Add(atlas, new Object[] { folderObject });

            // Packing 設定
            var packing = atlas.GetPackingSettings();
            packing.enableRotation     = false;
            packing.enableTightPacking = false;
            packing.padding            = 4;
            atlas.SetPackingSettings(packing);

            // Texture 設定
            var texture = atlas.GetTextureSettings();
            texture.readable        = false;
            texture.generateMipMaps = false;
            texture.sRGB            = true;
            texture.filterMode      = FilterMode.Bilinear;
            atlas.SetTextureSettings(texture);

            // Android 平台覆蓋
            atlas.SetPlatformSettings(new TextureImporterPlatformSettings
            {
                name               = "Android",
                overridden         = true,
                maxTextureSize     = 2048,
                format             = TextureImporterFormat.ASTC_6x6,
                compressionQuality = (int)TextureCompressionQuality.Best,
            });

            // iOS 平台覆蓋（Unity 內部名稱為 "iPhone"）
            atlas.SetPlatformSettings(new TextureImporterPlatformSettings
            {
                name               = "iPhone",
                overridden         = true,
                maxTextureSize     = 2048,
                format             = TextureImporterFormat.ASTC_6x6,
                compressionQuality = (int)TextureCompressionQuality.Best,
            });

            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();
        }

        private void ApplyPackageRoot()
        {
            if (string.IsNullOrWhiteSpace(packageRootPath) || !Directory.Exists(packageRootPath))
            {
                SetStatus("請先選擇有效的 Package 資料夾。", MessageType.Warning);
                return;
            }

            var layoutPath = FindPackageLayoutJson(packageRootPath);
            if (string.IsNullOrWhiteSpace(layoutPath))
            {
                SetStatus("Package 資料夾內找不到 layout JSON。", MessageType.Error);
                return;
            }

            layoutJsonPath = layoutPath;
            ApplyReferenceResolutionFromLayout(false);
            // U5：套用 Package 當下偵測是否含文字節點，供 Typography 區即時提示
            RefreshPackageTextNodeFlag();
            sourceImageFolder = FindPackageImageFolder(packageRootPath, layoutPath);
            if (string.IsNullOrWhiteSpace(sourceImageFolder))
            {
                sourceImageFolder = Path.GetDirectoryName(layoutPath);
                SetStatus("已套用 layout JSON，但找不到明確圖片資料夾，請在進階設定手動指定 PNG 來源資料夾。", MessageType.Warning);
                return;
            }

            AutoSelectImportFolderFromSource();
            SetStatus($"已套用 Package：{Path.GetFileName(layoutJsonPath)} / {sourceImageFolder}", MessageType.Info);
        }

        private void AutoSelectImportFolderFromSource()
        {
            var sourceAssetPath = PathUtility.ToProjectRelativeAssetPath(sourceImageFolder);
            if (PathUtility.IsAssetPath(sourceAssetPath))
            {
                importFolder = sourceAssetPath;
                if (string.IsNullOrWhiteSpace(prefabFolder))
                {
                    prefabFolder = GetStandardPrefabFolder();
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(importFolder))
            {
                importFolder = GetStandardImportFolder();
            }

            if (string.IsNullOrWhiteSpace(prefabFolder))
            {
                prefabFolder = GetStandardPrefabFolder();
            }
        }

        private void ApplyReferenceResolutionFromLayout(bool force)
        {
            if (string.IsNullOrWhiteSpace(layoutJsonPath))
            {
                return;
            }

            if (!LayoutReader.TryRead(layoutJsonPath, out var layout, out _))
            {
                return;
            }

            ApplyReferenceResolutionFromLayout(layout, force);
        }

        private void ApplyReferenceResolutionFromLayout(PhotoshopUiLayout layout, bool force)
        {
            if (!force && !autoReferenceResolution)
            {
                return;
            }

            if (layout?.canvas == null || layout.canvas.width <= 0f || layout.canvas.height <= 0f)
            {
                return;
            }

            referenceResolution = new Vector2(layout.canvas.width, layout.canvas.height);
        }

        private void ApplyStandardFoldersIfUnset(string standardImport, string standardPrefab)
        {
            if (string.IsNullOrWhiteSpace(importFolder) && !string.IsNullOrWhiteSpace(standardImport))
            {
                importFolder = standardImport;
            }

            if (string.IsNullOrWhiteSpace(prefabFolder) && !string.IsNullOrWhiteSpace(standardPrefab))
            {
                prefabFolder = standardPrefab;
            }
        }

        private bool ValidateTypography(PhotoshopUiLayout layout)
        {
            if (!ContainsTextNode(layout?.nodes))
            {
                return true;
            }

            if (defaultTmpFontAsset == null)
            {
                SetStatus("此 UI Package 含文字節點。請先指定預設 TMP Font Asset。", MessageType.Error);
                return false;
            }

            if (defaultTmpMaterialPreset == null)
            {
                SetStatus("此 UI Package 含文字節點。建議指定預設 TMP 材質球，否則樣式可能不完整。", MessageType.Warning);
            }

            return true;
        }

        private static int CountTextNodes(System.Collections.Generic.IEnumerable<PhotoshopUiNode> nodes)
        {
            if (nodes == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var node in nodes)
            {
                if (node == null)
                {
                    continue;
                }

                if (node.NormalizedType == "text")
                {
                    count++;
                }

                count += CountTextNodes(node.children);
            }

            return count;
        }

        private static bool ContainsTextNode(System.Collections.Generic.IEnumerable<PhotoshopUiNode> nodes)
        {
            if (nodes == null)
            {
                return false;
            }

            foreach (var node in nodes)
            {
                if (node == null)
                {
                    continue;
                }

                if (node.NormalizedType == "text")
                {
                    return true;
                }

                if (ContainsTextNode(node.children))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountNodes(System.Collections.Generic.IEnumerable<PhotoshopUiNode> nodes)
        {
            if (nodes == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var node in nodes)
            {
                if (node == null)
                {
                    continue;
                }

                count++;
                count += CountNodes(node.children);
            }

            return count;
        }

        private static void DrawFilePathField(string label, ref string path, string extension)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                path = EditorGUILayout.TextField(label, path);
                if (GUILayout.Button("選擇", GUILayout.Width(64)))
                {
                    var selected = EditorUtility.OpenFilePanel(label, Application.dataPath, extension);
                    if (!string.IsNullOrEmpty(selected))
                    {
                        path = selected;
                    }
                }
            }
        }

        private static void DrawFolderPathField(string label, ref string path, bool mustBeAssetPath)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                path = EditorGUILayout.TextField(label, path);
                if (GUILayout.Button("選擇", GUILayout.Width(64)))
                {
                    var selected = EditorUtility.OpenFolderPanel(label, Application.dataPath, string.Empty);
                    if (string.IsNullOrEmpty(selected))
                    {
                        return;
                    }

                    path = mustBeAssetPath ? PathUtility.ToProjectRelativeAssetPath(selected) : selected;
                }
            }
        }

        private static void DrawReadOnlyPath(string label, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(label, path);
            }
        }

        private bool NormalizeAssetFolder(ref string path, string label)
        {
            path = PathUtility.ToProjectRelativeAssetPath(path);
            if (PathUtility.IsAssetPath(path))
            {
                return true;
            }

            SetStatus($"{label} 目前不在此 Unity 專案的 Assets 內，請選擇 Assets 底下的資料夾。", MessageType.Warning);
            return false;
        }

        private void EnsureSourceImageFolderFromLayout()
        {
            if (string.IsNullOrWhiteSpace(layoutJsonPath))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(sourceImageFolder) && Directory.Exists(sourceImageFolder))
            {
                return;
            }

            var guessed = FindLikelyImageFolder(layoutJsonPath);
            sourceImageFolder = string.IsNullOrWhiteSpace(guessed) ? Path.GetDirectoryName(layoutJsonPath) : guessed;
        }

        private void GuessSourceImageFolderFromLayout()
        {
            if (string.IsNullOrWhiteSpace(layoutJsonPath))
            {
                SetStatus("請先選擇 Layout JSON。", MessageType.Warning);
                return;
            }

            var guessed = FindLikelyImageFolder(layoutJsonPath);
            if (string.IsNullOrWhiteSpace(guessed))
            {
                SetStatus("無法自動找到 PNG 圖片資料夾，請手動指定。", MessageType.Warning);
                return;
            }

            sourceImageFolder = guessed;
            packageRootPath = Path.GetDirectoryName(layoutJsonPath);
            ApplyReferenceResolutionFromLayout(false);
            AutoSelectImportFolderFromSource();
            SetStatus($"已推測 PNG 圖片資料夾：{sourceImageFolder}", MessageType.Info);
        }

        private static string FindPackageLayoutJson(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                return string.Empty;
            }

            var exact = Path.Combine(rootPath, "layout.json");
            if (File.Exists(exact))
            {
                return exact;
            }

            var layoutFiles = Directory.GetFiles(rootPath, "*_layout.json", SearchOption.TopDirectoryOnly);
            System.Array.Sort(layoutFiles);
            if (layoutFiles.Length > 0)
            {
                return layoutFiles[0];
            }

            var jsonFiles = Directory.GetFiles(rootPath, "*.json", SearchOption.TopDirectoryOnly);
            System.Array.Sort(jsonFiles);
            return jsonFiles.Length > 0 ? jsonFiles[0] : string.Empty;
        }

        // 共用候選清單邏輯：images / sprites / {name}_images / {stem}_images
        private static System.Collections.Generic.List<string> BuildImageFolderCandidates(string layoutDirectory, string layoutName)
        {
            var candidates = new System.Collections.Generic.List<string>();
            AddCandidate(candidates, layoutDirectory, "images");
            AddCandidate(candidates, layoutDirectory, "sprites");
            AddCandidate(candidates, layoutDirectory, $"{layoutName}_images");

            var lowerName = layoutName.ToLowerInvariant();
            var layoutIndex = lowerName.IndexOf("_layout", System.StringComparison.Ordinal);
            if (layoutIndex > 0)
            {
                AddCandidate(candidates, layoutDirectory, $"{layoutName.Substring(0, layoutIndex)}_images");
            }

            return candidates;
        }

        private static string FindPackageImageFolder(string rootPath, string layoutPath)
        {
            var layoutDirectory = Path.GetDirectoryName(layoutPath);
            var candidates = BuildImageFolderCandidates(layoutDirectory, Path.GetFileNameWithoutExtension(layoutPath));
            candidates.Add(rootPath);

            foreach (var candidate in candidates)
            {
                if (ContainsPng(candidate, SearchOption.TopDirectoryOnly))
                {
                    return candidate;
                }
            }

            foreach (var directory in Directory.GetDirectories(rootPath))
            {
                if (ContainsPng(directory, SearchOption.AllDirectories))
                {
                    return directory;
                }
            }

            return string.Empty;
        }

        private static string FindLikelyImageFolder(string jsonPath)
        {
            var layoutDirectory = Path.GetDirectoryName(jsonPath);
            if (string.IsNullOrWhiteSpace(layoutDirectory) || !Directory.Exists(layoutDirectory))
            {
                return string.Empty;
            }

            var candidates = BuildImageFolderCandidates(layoutDirectory, Path.GetFileNameWithoutExtension(jsonPath));
            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static void AddCandidate(System.Collections.Generic.ICollection<string> candidates, string layoutDirectory, string folderName)
        {
            if (!string.IsNullOrWhiteSpace(folderName))
            {
                candidates.Add(Path.Combine(layoutDirectory, folderName));
            }
        }

        private static bool ContainsPng(string folderPath, SearchOption searchOption)
        {
            return !string.IsNullOrWhiteSpace(folderPath) &&
                   Directory.Exists(folderPath) &&
                   Directory.GetFiles(folderPath, "*.png", searchOption).Length > 0;
        }

        private void UpdateFromGitHub()
        {
            // 直接使用 raw.githubusercontent.com 下載，不呼叫 GitHub API
            // 避免 unauthenticated API 每小時 60 次的速率限制（403）
            const string rawBase =
                "https://raw.githubusercontent.com/Wayne188-yiching/PS_To_Unity_v2/main/Assets/Editor/PhotoshopUiImporter/";

            var fileNames = new[]
            {
                "IUiPrefabBackend.cs",
                "ImageImportService.cs",
                "LayoutReader.cs",
                "PathUtility.cs",
                "PhotoshopUiImporterWindow.cs",
                "PhotoshopUiLayout.cs",
                "SimpleJsonReader.cs",
                "SkinMap.cs",
                "SkinResolver.cs",
                "TMPStyleMap.cs",
                "TmpFontMap.cs",
                "TmpMapper.cs",
                "UGuiTmpPrefabBackend.cs",
                "PsUiSkinTheme.cs",
                "PsUiSkinApplier.cs",
            };

            EditorUtility.DisplayProgressBar("更新工具", "正在連線 GitHub...", 0.05f);
            try
            {
                // Step 1: 取得遠端版本號（只下載 Window 檔，不耗 API 配額）
                string remoteVersion = null;
                using (var wc = new System.Net.WebClient())
                {
                    wc.Headers["User-Agent"] = "PhotoshopUiImporter-Updater/1.0";
                    var raw = wc.DownloadString(rawBase + "PhotoshopUiImporterWindow.cs");
                    var vm = System.Text.RegularExpressions.Regex.Match(raw, @"ToolVersion\s*=\s*""([^""]+)""");
                    if (vm.Success)
                        remoteVersion = vm.Groups[1].Value;
                }

                EditorUtility.ClearProgressBar();

                // Step 2: 顯示確認 Dialog
                var versionLine = remoteVersion != null
                    ? $"GitHub 版本：v{remoteVersion}　/　本地版本：v{ToolVersion}\n\n"
                    : string.Empty;

                if (!EditorUtility.DisplayDialog(
                    "更新 Photoshop UI Importer",
                    $"{versionLine}將覆蓋 Assets/Editor/PhotoshopUiImporter/ 下共 {fileNames.Length} 個腳本。\n更新後 Unity 會自動重新編譯。\n\n確定要繼續嗎？",
                    "確定更新",
                    "取消"))
                {
                    return;
                }

                // Step 3: 找本地腳本所在目錄
                var guids = AssetDatabase.FindAssets("PhotoshopUiImporterWindow t:Script");
                if (guids.Length == 0)
                {
                    SetStatus("找不到本地腳本路徑，更新中止。", MessageType.Error);
                    return;
                }

                var scriptAssetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                var localDir = Path.GetDirectoryName(
                    Path.GetFullPath(Path.Combine(Application.dataPath, "..", scriptAssetPath)));

                // Step 4: 逐一下載並覆蓋
                using (var wc = new System.Net.WebClient())
                {
                    wc.Headers["User-Agent"] = "PhotoshopUiImporter-Updater/1.0";
                    for (var i = 0; i < fileNames.Length; i++)
                    {
                        var fileName = fileNames[i];
                        EditorUtility.DisplayProgressBar(
                            "更新工具",
                            $"下載 {fileName}（{i + 1}/{fileNames.Length}）",
                            (float)(i + 1) / fileNames.Length);
                        var content = wc.DownloadString(rawBase + fileName);
                        File.WriteAllText(Path.Combine(localDir, fileName), content, Encoding.UTF8);
                    }
                }

                AssetDatabase.Refresh();
                SetStatus($"更新完成，已下載 {fileNames.Length} 個腳本。Unity 正在重新編譯。", MessageType.Info);
            }
            catch (System.Net.WebException webEx)
            {
                SetStatus($"網路錯誤：{webEx.Message}", MessageType.Error);
                Debug.LogException(webEx);
            }
            catch (System.Exception ex)
            {
                SetStatus($"更新失敗：{ex.Message}", MessageType.Error);
                Debug.LogException(ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void SetStatus(string message, MessageType type)
        {
            statusMessage = message;
            statusType = type;
            Repaint();
        }

        private static string BuildErrorMessage(string title, System.Collections.Generic.IEnumerable<string> errors)
        {
            var builder = new StringBuilder(title);
            foreach (var error in errors)
            {
                builder.AppendLine();
                builder.Append("- ");
                builder.Append(error);
            }

            return builder.ToString();
        }

        // OPTIMIZATION_PLAN_zh.html#phase4-decisions Q10-a：LayoutReadResult.warnings 統一 dump 到 Unity Console。
        // 來源：(1) JSX 端 detect 邏輯（GRID_DEGRADED / GRID_OUTLIER 等，Step 3 起注入），
        //      (2) LayoutReader.Validate 加的 UNITY_TOOL_OUTDATED（Q9-c）。
        private static void LogLayoutWarnings(LayoutReadResult result)
        {
            if (result == null || result.warnings == null || result.warnings.Count == 0)
            {
                return;
            }
            foreach (var w in result.warnings)
            {
                if (w == null) continue;
                var where = string.IsNullOrWhiteSpace(w.node) ? "" : $" @ {w.node}";
                Debug.LogWarning($"[PhotoshopUiImporter] {w.code}{where} — {w.message}");
            }
        }
    }
}

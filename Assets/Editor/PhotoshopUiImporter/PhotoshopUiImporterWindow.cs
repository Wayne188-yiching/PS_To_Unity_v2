using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

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
        [SerializeField]
        private TmpFontMap tmpFontMap;
        // Phase 5（OPTIMIZATION_PLAN_zh.html#phase5-q4）：「掃描 Package 字型」結果快取。
        private System.Collections.Generic.List<PackageFontScanEntry> packageFontScanEntries;

        private sealed class PackageFontScanEntry
        {
            public string fontToken;
            public int nodeCount;
            public TMP_FontAsset mappedFont;                        // 已對應（TmpFontMap 查得到）
            public TmpFontAssetFactory.FontFileCandidate fontFile;  // 缺 Font Asset，但專案內找得到字型檔
            public TMP_FontAsset existingFontAsset;                 // 已建立，但尚未由 TmpFontMap 對應
        }
        private bool autoReferenceResolution = true;
        private Vector2 referenceResolution = new Vector2(1920f, 1080f);
        private bool useResponsiveAnchor;
        // v2.16：量測式無損九宮格；只作用在這次新建立（或先前由工具切過）的 Sprite。
        private bool autoNineSlice = true;
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
        private string reskinUsageScopeFolder = "Assets";
        private PsUiSkinApplier.Report reskinPlan;
        private string reskinPlanInputs;
        private Vector2 reskinPlanScrollPos;
        private PsUiSkinTheme activeSkinTheme;
        private string reskinAutoMatchSummary;
        private System.Collections.Generic.List<PsUiSkinApplier.DimensionCandidate> reskinDimensionMatches;
        private string reskinDimensionMatchSummary;
        private Vector2 reskinDimensionMatchScrollPos;
        private const string ToolVersion = "2.18.0";
        internal static string ReportToolVersion => ToolVersion;
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
                    EditorGUILayout.HelpBox($"圖片來源已在 Unity 專案內，會直接使用 {sourceAssetPath}", MessageType.Info);
                }
                else if (!string.IsNullOrWhiteSpace(importFolder))
                {
                    // 路徑未定（尚未填專案資料夾名稱）時不顯示，避免出現吊著的空句尾。
                    EditorGUILayout.HelpBox($"圖片來源若在 Unity 專案外，Generate 時會複製到 {importFolder}", MessageType.None);
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
            autoNineSlice = EditorGUILayout.ToggleLeft(
                new GUIContent("自動九宮格（量測中段均勻的框／底條，縮成小圖 + Sliced，誤差 ≤ 2/255）",
                    "只切新匯入的圖；既有且非本工具切過的 Sprite 只列提案不改，避免其他 Prefab 以 Simple 引用時變形。"),
                autoNineSlice);
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

                DrawPackageFontScanControls();

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
                EditorGUILayout.LabelField("描邊厚度補償", EditorStyles.boldLabel);
                using (var check = new EditorGUI.ChangeCheckScope())
                {
                    var newValue = EditorGUILayout.Slider(
                        new GUIContent(
                            "描邊厚度補償係數",
                            "Unity SDF 描邊邊緣是半透明 falloff，視覺重心比 PS 重。\n" +
                            "Unity 偏厚 → 調低（如 0.85）；偏細 → 調高。\n" +
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
                        "目前 = 1.0（不補償）。若描邊比 PS 視覺偏厚，調低（如 0.85）後重新 Generate。",
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

        // OPTIMIZATION_PLAN_zh.html#phase5-q4：偵測 PS 端字體——列出 layout.json 全部 fontToken，
        // 顯示「已對應 / 缺 Font Asset / 缺字型檔」三態；缺資產可一鍵建立
        //（Dynamic SDF、參數抄預設字型、自動登記 TmpFontMap）。
        private void DrawPackageFontScanControls()
        {
            if (GUILayout.Button("掃描 Package 字型（fontToken → 資產狀態）", GUILayout.Height(24)))
            {
                ScanPackageFonts();
            }

            if (packageFontScanEntries == null)
            {
                return;
            }

            if (packageFontScanEntries.Count == 0)
            {
                EditorGUILayout.HelpBox("此 Package 沒有任何 TMP 文字節點（或全部烘成了 PNG）。", MessageType.None);
                return;
            }

            foreach (var entry in packageFontScanEntries)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (entry.mappedFont != null)
                    {
                        EditorGUILayout.LabelField($"✔ {entry.fontToken}（{entry.nodeCount} 節點）→ {entry.mappedFont.name}", EditorStyles.miniLabel);
                    }
                    else if (entry.fontFile != null)
                    {
                        if (entry.existingFontAsset != null)
                        {
                            EditorGUILayout.LabelField($"✔ {entry.fontToken}（{entry.nodeCount} 節點）Font Asset 已建立：{entry.existingFontAsset.name}（尚未對應）", EditorStyles.miniLabel);
                            using (new EditorGUI.DisabledScope(true))
                            {
                                GUILayout.Button("已建立", GUILayout.Width(72));
                            }
                        }
                        else
                        {
                            EditorGUILayout.LabelField($"△ {entry.fontToken}（{entry.nodeCount} 節點）缺 Font Asset；字型檔：{entry.fontFile.matchedName}", EditorStyles.miniLabel);
                            if (GUILayout.Button("一鍵建立", GUILayout.Width(72)))
                            {
                                CreatePackageFontAsset(entry);
                                GUIUtility.ExitGUI();
                            }
                        }
                    }
                    else
                    {
                        EditorGUILayout.LabelField($"✘ {entry.fontToken}（{entry.nodeCount} 節點）缺字型檔——請把 .ttf/.otf 放進專案 Assets（工具不掃系統字型夾）", EditorStyles.miniLabel);
                    }
                }
            }
        }

        private void ScanPackageFonts()
        {
            if (!LayoutReader.TryRead(layoutJsonPath, out var layout, out var readResult))
            {
                SetStatus(BuildErrorMessage("掃描 Package 字型失敗", readResult.errors), MessageType.Error);
                return;
            }

            var tokenCounts = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            CollectFontTokens(layout.nodes, tokenCounts);

            packageFontScanEntries = new System.Collections.Generic.List<PackageFontScanEntry>();
            foreach (var pair in tokenCounts)
            {
                var entry = new PackageFontScanEntry { fontToken = pair.Key, nodeCount = pair.Value };
                if (tmpFontMap != null && tmpFontMap.TryGetEntry(pair.Key, out var mapEntry))
                {
                    entry.mappedFont = mapEntry.fontAsset;
                }
                else
                {
                    entry.fontFile = TmpFontAssetFactory.FindProjectFontFile(pair.Key);
                    if (entry.fontFile != null)
                    {
                        entry.existingFontAsset = TmpFontAssetFactory.FindExistingFontAsset(entry.fontFile.font);
                    }
                }
                packageFontScanEntries.Add(entry);
            }

            packageFontScanEntries.Sort((a, b) => b.nodeCount.CompareTo(a.nodeCount));
            SetStatus($"Package 字型掃描完成：共 {packageFontScanEntries.Count} 種 fontToken。", MessageType.Info);
        }

        private static void CollectFontTokens(System.Collections.Generic.List<PhotoshopUiNode> nodes, System.Collections.Generic.Dictionary<string, int> counts)
        {
            if (nodes == null)
            {
                return;
            }

            foreach (var node in nodes)
            {
                if (node == null)
                {
                    continue;
                }

                if (node.NormalizedType == "text" && !string.IsNullOrWhiteSpace(node.fontToken))
                {
                    counts.TryGetValue(node.fontToken, out var count);
                    counts[node.fontToken] = count + 1;
                }

                CollectFontTokens(node.children, counts);
            }
        }

        private void CreatePackageFontAsset(PackageFontScanEntry entry)
        {
            var existing = TmpFontAssetFactory.FindExistingFontAsset(entry.fontFile.font);
            if (existing != null)
            {
                entry.existingFontAsset = existing;
                SetStatus($"{existing.name} 已建立，不會重複建立；請將它加入 TmpFontMap 對應。", MessageType.Warning);
                EditorGUIUtility.PingObject(existing);
                Repaint();
                return;
            }

            var created = TmpFontAssetFactory.CreateDynamicFontAsset(
                entry.fontFile.font, defaultTmpFontAsset, TmpFontAssetFactory.DefaultOutputFolder, out var error);

            if (created != null && string.IsNullOrEmpty(error))
            {
                var registered = TmpFontAssetFactory.RegisterInFontMap(tmpFontMap, entry.fontToken, created);
                SetStatus($"已建立 {created.name}（Dynamic SDF，出包前建議轉 Static）。" +
                          (registered ? "已自動登記 TmpFontMap。" : "未指定 TmpFontMap，請手動登記對應。"), MessageType.Info);
                EditorGUIUtility.PingObject(created);
                ScanPackageFonts();
            }
            else
            {
                SetStatus(error ?? "建立 Font Asset 失敗。", MessageType.Error);
            }
        }

        private void DrawReskinSection()
        {
            // U7：換皮工具屬獨立、低頻、具破壞性的功能，預設摺疊收進主流程之後。
            // Foldout 必須包在 VerticalScope 內，否則 Unity 6 IMGUI 會丟出
            // kDontSaveInEditor / kAllowDontSaveObjectsToPersistent 的 assertion。
            // v2.16：資料夾覆蓋與 SkinTheme 兩個入口保留（輸入不同：一個只要兩個資料夾，一個要對照表），
            // 但共用 PsUiSkinApplier 同一套 預覽 → 確認執行 → 自動備份 / 還原 引擎，不再各寫一套覆蓋邏輯。
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

                EditorGUILayout.HelpBox(
                    "兩個入口共用同一套換皮引擎：先「預覽」（dry-run，不改任何檔案）→ 看清單 →「確認執行」。\n" +
                    "執行前自動備份，取消、例外或機械檢查失敗會自動還原。每次預覽 / 執行都會寫出報告：\n" +
                    PsUiSkinApplier.ReportRelativePath,
                    MessageType.None);

                EditorGUILayout.LabelField("A. 資料夾同名覆蓋（美術圖與 Unity 圖同檔名）", EditorStyles.boldLabel);
                DrawFolderPathField("美術來源資料夾", ref reskinArtSourceFolder, false);
                DrawFolderPathField("Unity 目標資料夾", ref reskinTargetFolder, true);
                DrawFolderPathField("Prefab 使用範圍", ref reskinUsageScopeFolder, true);
                if (GUILayout.Button("預覽資料夾覆蓋（不會修改檔案）", GUILayout.Height(30)))
                {
                    reskinPlan = PsUiSkinApplier.PlanFolder(reskinArtSourceFolder, reskinTargetFolder, reskinUsageScopeFolder);
                    reskinPlanInputs = ReskinInputs(PsUiSkinApplier.FlowFolder);
                    ReportReskinPlanStatus();
                }

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("B. SkinTheme 對照表（舊 Sprite → 新 Sprite，支援改名與參照替換）", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                activeSkinTheme = (PsUiSkinTheme)EditorGUILayout.ObjectField(
                    "Skin Theme", activeSkinTheme, typeof(PsUiSkinTheme), false);
                if (EditorGUI.EndChangeCheck())
                {
                    reskinDimensionMatches = null;
                    reskinDimensionMatchSummary = null;
                }

                if (activeSkinTheme != null)
                {
                    var folder = activeSkinTheme.targetPrefabFolderAsset != null
                        ? AssetDatabase.GetAssetPath(activeSkinTheme.targetPrefabFolderAsset)
                        : "（未設定）";
                    EditorGUILayout.LabelField($"目標資料夾：{folder}", EditorStyles.miniLabel);

                    EditorGUI.BeginChangeCheck();
                    var referenceFolder = EditorGUILayout.ObjectField("對照 Prefab 資料夾（已換新圖）",
                        activeSkinTheme.referencePrefabFolderAsset, typeof(DefaultAsset), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(activeSkinTheme, "設定對照 Prefab 資料夾");
                        activeSkinTheme.referencePrefabFolderAsset = referenceFolder;
                        EditorUtility.SetDirty(activeSkinTheme);
                        reskinAutoMatchSummary = null;
                    }

                    DrawFolderPathField("新美術來源資料夾", ref activeSkinTheme.sourceArtFolder, false);
                    if (GUI.changed) EditorUtility.SetDirty(activeSkinTheme);

                    EditorGUI.BeginChangeCheck();
                    var candidateFolder = EditorGUILayout.ObjectField("候選新 Sprite 資料夾（Unity Assets）",
                        activeSkinTheme.candidateSpriteFolderAsset, typeof(DefaultAsset), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(activeSkinTheme, "設定尺寸候選 Sprite 資料夾");
                        activeSkinTheme.candidateSpriteFolderAsset = candidateFolder;
                        EditorUtility.SetDirty(activeSkinTheme);
                        reskinDimensionMatches = null;
                        reskinDimensionMatchSummary = null;
                    }

                    if (GUILayout.Button("掃描 Prefab，自動填入舊 Sprite", GUILayout.Height(28)))
                        ScanSkinTheme();

                    if (GUILayout.Button("依對應 Prefab＋節點路徑填入新 Sprite", GUILayout.Height(28)))
                        AutoMatchSkinTheme();
                    if (!string.IsNullOrEmpty(reskinAutoMatchSummary))
                        EditorGUILayout.HelpBox(reskinAutoMatchSummary, MessageType.Info);

                    if (GUILayout.Button("依圖片尺寸產生候選（不寫入）", GUILayout.Height(28)))
                        SuggestSkinThemeByDimensions();
                    DrawDimensionMatchCandidates();

                    if (GUILayout.Button("預覽 SkinTheme 換皮（不會修改檔案）", GUILayout.Height(30)))
                    {
                        reskinPlan = PsUiSkinApplier.PlanTheme(activeSkinTheme);
                        reskinPlanInputs = ReskinInputs(PsUiSkinApplier.FlowSkinTheme);
                        ReportReskinPlanStatus();
                    }
                }

                // 輸入變更後舊預覽失效；SkinTheme 內容的變更由 Execute 重新規劃比對時攔下。
                if (reskinPlan != null && reskinPlanInputs != ReskinInputs(reskinPlan.flow))
                {
                    reskinPlan = null;
                }

                DrawReskinPlan();

                if (PsUiSkinApplier.HasRollback &&
                    GUILayout.Button("還原上次換皮（從備份還原圖檔與 Prefab）", GUILayout.Height(26)))
                {
                    RollbackReskin();
                }
            }
        }

        private string ReskinInputs(string flow)
        {
            if (flow == PsUiSkinApplier.FlowFolder)
                return $"{flow}|{reskinArtSourceFolder}|{reskinTargetFolder}|{reskinUsageScopeFolder}";
            if (activeSkinTheme == null)
                return flow;
            var folder = activeSkinTheme.targetPrefabFolderAsset != null
                ? AssetDatabase.GetAssetPath(activeSkinTheme.targetPrefabFolderAsset)
                : string.Empty;
            return $"{flow}|{activeSkinTheme.GetInstanceID()}|{folder}|{activeSkinTheme.sourceArtFolder}";
        }

        private void DrawReskinPlan()
        {
            if (reskinPlan == null)
            {
                return;
            }

            var plan = reskinPlan;
            var s = plan.summary;
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(
                $"預覽結果（{(plan.flow == PsUiSkinApplier.FlowSkinTheme ? "SkinTheme" : "資料夾覆蓋")}）", EditorStyles.boldLabel);
            foreach (var error in plan.errors)
            {
                EditorGUILayout.HelpBox(error, MessageType.Warning);
            }
            EditorGUILayout.LabelField(
                $"將覆蓋圖檔 {s.filesOverwritten}｜將替換參照 {s.spritesReplaced}（{s.prefabsChanged} 個 Prefab）｜" +
                $"缺圖 {s.missing}｜擋下 {s.blocked}｜略過 {s.skipped}",
                EditorStyles.miniBoldLabel);

            reskinPlanScrollPos = EditorGUILayout.BeginScrollView(reskinPlanScrollPos, GUILayout.MaxHeight(240));
            DrawReskinItems(plan, "將執行", PsUiSkinApplier.StatusOk);
            DrawReskinItems(plan, "缺圖（請交給美術）", PsUiSkinApplier.StatusMissing);
            DrawReskinItems(plan, "擋下（處理後重新預覽）", PsUiSkinApplier.StatusBlocked);
            DrawReskinItems(plan, "略過", PsUiSkinApplier.StatusSkipped);
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                var executable = System.Linq.Enumerable.Count(plan.Executable);
                using (new EditorGUI.DisabledScope(!plan.planComplete || executable == 0))
                {
                    if (GUILayout.Button($"確認執行 {executable} 項（先備份）", GUILayout.Height(32)))
                    {
                        ExecuteReskinPlan();
                    }
                }
                if (GUILayout.Button("開啟報告", GUILayout.Width(90), GUILayout.Height(32)))
                {
                    EditorUtility.RevealInFinder(PsUiSkinApplier.ReportPath);
                }
            }
        }

        private static void DrawReskinItems(PsUiSkinApplier.Report plan, string title, string status)
        {
            var items = plan.items.FindAll(i => i.status == status);
            if (items.Count == 0)
            {
                return;
            }

            EditorGUILayout.LabelField($"{title}（{items.Count}）", EditorStyles.miniBoldLabel);
            foreach (var item in items)
            {
                var name = string.IsNullOrEmpty(item.oldSprite) ? "（未指定）" : Path.GetFileName(item.oldSprite);
                EditorGUILayout.LabelField($"• {name}　{item.reason}", EditorStyles.wordWrappedMiniLabel);
                foreach (var warning in item.warnings)
                {
                    EditorGUILayout.LabelField("　　注意：" + warning, EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private void ReportReskinPlanStatus()
        {
            var s = reskinPlan.summary;
            SetStatus(
                reskinPlan.planComplete
                    ? $"預覽完成，尚未修改任何檔案：將覆蓋 {s.filesOverwritten}、替換 {s.spritesReplaced} 處參照；缺圖 {s.missing}、擋下 {s.blocked}、略過 {s.skipped}。"
                    : "預覽未完成：" + string.Join("；", reskinPlan.errors),
                reskinPlan.planComplete && s.missing + s.blocked == 0 ? MessageType.Info : MessageType.Warning);
        }

        private void ScanSkinTheme()
        {
            if (activeSkinTheme == null) return;

            if (activeSkinTheme.targetPrefabFolderAsset == null)
            {
                SetStatus("請先拖入目標 Prefab 資料夾再掃描。", MessageType.Warning);
                return;
            }

            var added = PsUiSkinApplier.ScanAndFillOldSprites(activeSkinTheme, out var notes);
            foreach (var note in notes)
                Debug.LogWarning($"[SkinTheme] {note}");
            SetStatus(
                (added > 0
                    ? $"掃描完成，找到 {added} 個 Sprite（可 Undo）。請在 SkinTheme Inspector 為每筆填入對應的新 Sprite。"
                    : "掃描完成，沒有新增項目（所有 Sprite 已在清單中）。") +
                (notes.Count > 0 ? $" 另有 {notes.Count} 則提醒見 Console。" : string.Empty),
                added > 0 ? MessageType.Info : MessageType.Warning);
        }

        private void AutoMatchSkinTheme()
        {
            var result = PsUiSkinApplier.AutoFillFromPrefabPairs(activeSkinTheme);
            reskinPlan = null;
            reskinDimensionMatches = null;
            reskinAutoMatchSummary =
                $"配對 Prefab {result.pairedPrefabs} 組；填入新 Sprite {result.filled} 筆；" +
                $"待人工處理 {result.remaining} 筆（歧義 {result.ambiguous}、對照 Prefab 未換圖 {result.unchanged}）。" +
                $" 缺少對照 Prefab {result.missingPrefabs}、節點 {result.missingNodes}、新圖 {result.missingSprites}。";
            foreach (var note in result.notes.Take(30))
                Debug.LogWarning($"[SkinTheme 自動配對] {note}");
            SetStatus(reskinAutoMatchSummary + (result.notes.Count > 0 ? " 詳情見 Console。" : ""),
                result.filled > 0 ? MessageType.Info : MessageType.Warning);
        }

        private void SuggestSkinThemeByDimensions()
        {
            var result = PsUiSkinApplier.SuggestByDimensions(activeSkinTheme);
            reskinDimensionMatches = result.matches;
            reskinDimensionMatchSummary =
                $"候選新圖 {result.sourceSprites} 張；高信心 {result.high}、中信心 {result.medium}、低信心 {result.low}、無候選 {result.noCandidate}。";
            foreach (var note in result.notes)
                Debug.LogWarning($"[SkinTheme 尺寸候選] {note}");
            SetStatus(reskinDimensionMatchSummary + (result.notes.Count > 0 ? " 詳情見 Console。" : ""),
                result.high + result.medium > 0 ? MessageType.Info : MessageType.Warning);
        }

        private void DrawDimensionMatchCandidates()
        {
            if (reskinDimensionMatches == null) return;
            EditorGUILayout.HelpBox(
                (reskinDimensionMatchSummary ?? "尺寸候選完成。") + " 僅為暫存預選；確認寫入前不會修改 SkinTheme。",
                MessageType.Info);

            reskinDimensionMatchScrollPos = EditorGUILayout.BeginScrollView(
                reskinDimensionMatchScrollPos, GUILayout.MaxHeight(260));
            foreach (var match in reskinDimensionMatches)
            {
                if (match == null || match.oldSprite == null) continue;
                EditorGUILayout.LabelField(
                    $"{match.oldSprite.name}　{match.oldWidth}×{match.oldHeight}　{match.confidence}信心",
                    EditorStyles.miniBoldLabel);
                if (match.candidates.Count == 0)
                {
                    EditorGUILayout.LabelField("　沒有尺寸足夠接近的候選。", EditorStyles.wordWrappedMiniLabel);
                    continue;
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    foreach (var candidate in match.candidates)
                    {
                        if (candidate == null) continue;
                        var selected = match.selectedSprite == candidate;
                        var previousColor = GUI.backgroundColor;
                        if (selected) GUI.backgroundColor = new Color(0.65f, 0.9f, 0.65f);
                        if (GUILayout.Button($"{candidate.name}\n{Mathf.RoundToInt(candidate.rect.width)}×{Mathf.RoundToInt(candidate.rect.height)}",
                            GUILayout.Width(136), GUILayout.Height(34)))
                            match.selectedSprite = candidate;
                        GUI.backgroundColor = previousColor;
                    }
                }
                match.selectedSprite = (Sprite)EditorGUILayout.ObjectField("確認候選", match.selectedSprite, typeof(Sprite), false);
            }
            EditorGUILayout.EndScrollView();

            var selectedCount = reskinDimensionMatches.Count(match => match != null && match.selectedSprite != null);
            using (new EditorGUI.DisabledScope(selectedCount == 0))
            {
                if (GUILayout.Button($"確認寫入 {selectedCount} 筆尺寸候選", GUILayout.Height(28)))
                {
                    var applied = PsUiSkinApplier.ApplyDimensionSelections(activeSkinTheme, reskinDimensionMatches);
                    reskinPlan = null;
                    reskinDimensionMatches = null;
                    reskinDimensionMatchSummary = null;
                    SetStatus($"已寫入 {applied} 筆尺寸候選；請再預覽 SkinTheme 換皮確認。", MessageType.Info);
                }
            }
        }

        private void ExecuteReskinPlan()
        {
            var s = reskinPlan.summary;
            if (!EditorUtility.DisplayDialog(
                "確認換皮",
                $"將覆蓋 {s.filesOverwritten} 個圖檔、替換 {s.spritesReplaced} 處 Sprite 參照（{s.prefabsChanged} 個 Prefab）。\n\n" +
                "執行前會先備份；取消、例外或機械檢查失敗會自動還原，之後也可按「還原上次換皮」。\n" +
                $"缺圖 {s.missing}、擋下 {s.blocked} 項不會執行。",
                "確定執行",
                "取消"))
                return;

            var r = PsUiSkinApplier.Execute(reskinPlan);
            reskinPlan = null;

            foreach (var item in r.items)
            {
                if (item.status == PsUiSkinApplier.StatusMissing || item.status == PsUiSkinApplier.StatusBlocked)
                    Debug.LogWarning($"[Reskin] {item.status} {item.oldSprite}：{item.reason}");
            }

            var done = r.summary;
            if (r.execution != null && r.execution.completed)
            {
                SetStatus(
                    $"換皮完成：覆蓋 {done.filesOverwritten} 個圖檔、替換 {done.spritesReplaced} 處參照（{done.prefabsChanged} 個 Prefab），機械檢查通過。" +
                    (done.missing + done.blocked > 0 ? $" 缺圖 {done.missing}、擋下 {done.blocked}（見 Console / 報告）。" : string.Empty),
                    done.missing + done.blocked > 0 ? MessageType.Warning : MessageType.Info);
            }
            else
            {
                SetStatus($"換皮未完成：{r.execution?.reason}（報告：{PsUiSkinApplier.ReportRelativePath}）", MessageType.Error);
            }
        }

        private void RollbackReskin()
        {
            if (!EditorUtility.DisplayDialog(
                "還原上次換皮",
                "將把上次換皮改過的圖檔與 Prefab 還原成備份內容。之後若檔案又被改過，會拒絕還原以免蓋掉新的修改。",
                "還原",
                "取消"))
                return;

            var ok = PsUiSkinApplier.RollbackLastApply(out var message);
            SetStatus(message, ok ? MessageType.Info : MessageType.Error);
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

            EditorUtility.DisplayProgressBar("生成 Prefab", "匯入圖片、打包圖集與生成 Prefab...", 0.4f);
            var result = PhotoshopUiImportService.Execute(new PhotoshopUiImportRequest
            {
                layoutJsonPath = layoutJsonPath,
                sourceImageFolder = sourceImageFolder,
                importFolder = importFolder,
                prefabFolder = prefabFolder,
                prefabName = Path.GetFileNameWithoutExtension(layoutJsonPath),
                projectFolder = projectFolder,
                defaultTmpFontAssetPath = AssetDatabase.GetAssetPath(defaultTmpFontAsset),
                defaultTmpMaterialPresetPath = AssetDatabase.GetAssetPath(defaultTmpMaterialPreset),
                tmpFontMapPath = AssetDatabase.GetAssetPath(tmpFontMap),
                skinMapPath = AssetDatabase.GetAssetPath(skinMap),
                materialLibraryFolder = materialLibraryFolder,
                outlineThicknessMultiplier = outlineThicknessMultiplier,
                referenceResolutionX = referenceResolution.x,
                referenceResolutionY = referenceResolution.y,
                useResponsiveAnchor = useResponsiveAnchor,
                createSpriteAtlases = true,
                autoNineSlice = autoNineSlice
            });
            foreach (var warning in result.warnings)
                Debug.LogWarning(warning);
            if (!result.IsSuccess)
            {
                SetStatus(BuildErrorMessage("Prefab 生成失敗", result.errors), MessageType.Error);
                return;
            }

            EditorUtility.DisplayProgressBar("生成 Prefab", "完成...", 1.0f);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(result.prefabAssetPath);
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            var dedupHint = result.dedupedSpriteCount > 0
                ? $"　像素去重合併：{result.dedupedSpriteCount} 張（省 {FormatByteSize(result.dedupedSpriteBytes)}）"
                : string.Empty;
            var slicedCount = result.autoSlices.FindAll(s => s.decision == "applied").Count;
            var proposalCount = result.autoSlices.FindAll(s => s.decision == "proposal").Count;
            if (slicedCount + proposalCount > 0)
            {
                dedupHint += $"　自動九宮格：{slicedCount} 張" + (proposalCount > 0 ? $"（另 {proposalCount} 張既有圖只提案，見 Console）" : string.Empty);
                foreach (var slice in result.autoSlices)
                    if (slice.decision == "proposal")
                        Debug.Log($"[九宮格提案] {slice.imagePath}：{slice.originalWidth}x{slice.originalHeight} -> {slice.slicedWidth}x{slice.slicedHeight}，border {slice.border}。{slice.reason}");
            }
            var notes = new System.Collections.Generic.List<string>();
            if (result.outlineWarningCount > 0)
                notes.Add($"{result.outlineWarningCount} 個文字描邊超出 SDF 物理上限被截斷（建議按 Console 建議值重建 Font Asset 的 atlasPadding）");
            if (result.fontTokenWarningCount > 0)
                notes.Add($"{result.fontTokenWarningCount} 種字型沒對到字型對應表，已用預設字型");
            if (result.warnings.Count > 0)
                notes.Add($"共 {result.warnings.Count} 項警告，詳見 Console");
            var warningHint = notes.Count > 0 ? "\n⚠ " + string.Join("；", notes) : string.Empty;
            SetStatus($"Prefab 生成完成：{result.prefabAssetPath}{dedupHint}{warningHint}",
                result.warnings.Count > 0 ? MessageType.Warning : MessageType.Info);
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
                : $"Assets/Temp/{projectFolder}/Atlas/SpriteAtlas/Base";
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
            CreateOrUpdateSpriteAtlases(atlasFolder);

            importFolder = GetStandardImportFolder();
            prefabFolder = GetStandardPrefabFolder();
            SetStatus($"已建立專案資料夾並設定 SpriteAtlas：{root}", MessageType.Info);
        }

        // Compatibility shims: deterministic asset work is shared with batch imports.
        internal static void EnsureAssetFolder(string assetPath) => PhotoshopUiAssetUtility.EnsureAssetFolder(assetPath);
        internal static string ResolveSpriteAtlasFolder(string folder) => PhotoshopUiAssetUtility.ResolveSpriteAtlasFolder(folder);
        internal static void CreateOrUpdateSpriteAtlases(string folder) => PhotoshopUiAssetUtility.CreateOrUpdateSpriteAtlases(folder);
        internal static void DetachSpriteAtlasFolderForImageImport(string folder) => PhotoshopUiAssetUtility.DetachSpriteAtlasFolderForImageImport(folder);

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

        // v2.12.1：更新清單改由 repo 的 update_manifest.txt 驅動（bump-version.ps1 每次出版自動重生成）。
        // 根治「新增檔案忘了加進寫死清單 → 更新後編譯壞掉」的慣犯（v2.10.1 漏 TmpFontMap.cs、
        // v2.12.0 漏 Phase 5 三檔）。manifest 抓不到時退回內建清單，至少能自我修復到讀得懂 manifest 的版本。
        private void UpdateFromGitHub()
        {
            // 直接使用 raw.githubusercontent.com 下載，不呼叫 GitHub API
            // 避免 unauthenticated API 每小時 60 次的速率限制（403）
            const string rawBase =
                "https://raw.githubusercontent.com/Wayne188-yiching/PS_To_Unity_v2/main/Assets/Editor/PhotoshopUiImporter/";
            const string manifestFileName = "update_manifest.txt";

            // 後備清單：只在 manifest 下載失敗時使用（新增檔案「不需要」改這裡，改 manifest 即可）。
            var fallbackFileNames = new[]
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
                "TmpFontAssetFactory.cs",
                "TmpFontMap.cs",
                "TmpFontReplacer.cs",
                "TmpFontReplacerWindow.cs",
                "TmpMapper.cs",
                "UGuiTmpPrefabBackend.cs",
                "PsUiSkinTheme.cs",
                "PsUiSkinApplier.cs",
            };

            EditorUtility.DisplayProgressBar("更新工具", "正在連線 GitHub...", 0.05f);
            try
            {
                // Step 1: 取得遠端版本號 + 檔案清單（manifest）
                string remoteVersion = null;
                string[] fileNames = null;
                string manifestText = null;
                using (var wc = new System.Net.WebClient())
                {
                    // 不設 Encoding 時 WebClient 用系統 ANSI 解碼，中文註解會變亂碼——必須指定 UTF-8。
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers["User-Agent"] = "PhotoshopUiImporter-Updater/1.0";
                    var raw = wc.DownloadString(rawBase + "PhotoshopUiImporterWindow.cs");
                    var vm = System.Text.RegularExpressions.Regex.Match(raw, @"ToolVersion\s*=\s*""([^""]+)""");
                    if (vm.Success)
                        remoteVersion = vm.Groups[1].Value;

                    try
                    {
                        wc.Headers["User-Agent"] = "PhotoshopUiImporter-Updater/1.0";
                        var manifest = wc.DownloadString(rawBase + manifestFileName);
                        var names = new System.Collections.Generic.List<string>();
                        foreach (var line in manifest.Split('\n'))
                        {
                            var trimmed = line.Trim();
                            if (trimmed.Length > 0 && !trimmed.StartsWith("#") && trimmed.EndsWith(".cs"))
                            {
                                names.Add(trimmed);
                            }
                        }
                        if (names.Count > 0)
                        {
                            fileNames = names.ToArray();
                            manifestText = manifest;
                        }
                    }
                    catch (System.Exception)
                    {
                        // 遠端還沒有 manifest（老版本 repo）→ 用後備清單。
                    }
                }

                var usedManifest = fileNames != null;
                if (fileNames == null)
                {
                    fileNames = fallbackFileNames;
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

                // Step 4: 先全部下載到記憶體，全數成功才寫入磁碟——
                // 中途斷線不會留下「半套新腳本」的編譯壞死狀態。
                var downloaded = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>();
                using (var wc = new System.Net.WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    for (var i = 0; i < fileNames.Length; i++)
                    {
                        var fileName = fileNames[i];
                        EditorUtility.DisplayProgressBar(
                            "更新工具",
                            $"下載 {fileName}（{i + 1}/{fileNames.Length}）",
                            (float)(i + 1) / (fileNames.Length + 1));
                        // WebClient 每次請求後會清掉自訂 header，UA 要逐次補。
                        wc.Headers["User-Agent"] = "PhotoshopUiImporter-Updater/1.0";
                        var content = wc.DownloadString(rawBase + fileName);
                        downloaded.Add(new System.Collections.Generic.KeyValuePair<string, string>(fileName, content));
                    }
                }

                EditorUtility.DisplayProgressBar("更新工具", "寫入檔案...", 1f);
                foreach (var pair in downloaded)
                {
                    File.WriteAllText(Path.Combine(localDir, pair.Key), pair.Value, Encoding.UTF8);
                }

                // manifest 本身不在下載清單裡（清單只收 .cs，避免更新器被誘導寫入任意檔案），
                // 但本地留一份舊的容易讓人誤判清單內容——用剛抓到的內容覆蓋。
                if (manifestText != null)
                {
                    File.WriteAllText(Path.Combine(localDir, manifestFileName), manifestText, Encoding.UTF8);
                }

                AssetDatabase.Refresh();
                SetStatus(
                    $"更新完成，已下載 {fileNames.Length} 個腳本（清單來源：{(usedManifest ? "update_manifest.txt" : "內建後備清單")}）。Unity 正在重新編譯。",
                    MessageType.Info);
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

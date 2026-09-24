using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace PhotoshopToUnity.EditorImporter
{
    /// <summary>
    /// SkinTheme 欄位的中文名稱與說明。Inspector 與 Importer_v2 換皮工具共用同一組字串，兩邊看到的名稱一致。
    /// </summary>
    internal static class SkinThemeLabels
    {
        public static readonly GUIContent Theme = new GUIContent("Skin Theme",
            "換皮對照表資產。沒有的話按右邊「新建」，或在 Project 視窗右鍵 → Create → Photoshop UI Importer → Skin Theme。");
        public static readonly GUIContent Target = new GUIContent("要換皮的 Prefab 資料夾",
            "必填。預覽與執行只會寫入這個資料夾裡的 Prefab；通常是從舊頁面複製出來的那一份，例如 Assets/Temp/LiveRoom。");
        public static readonly GUIContent Source = new GUIContent("舊版 Prefab 資料夾",
            "選填、唯讀。被複製的原頁面，例如 Assets/ThemeActivity/CatAct。只用來和「要換皮的 Prefab 資料夾」比對同一節點、學出舊圖 → 新圖，換皮不會寫入。");
        public static readonly GUIContent ArtFolder = new GUIContent("覆蓋用 PNG 資料夾",
            "選填。只在對照表的新 Sprite 留空時使用：以這個資料夾裡同檔名的 PNG 覆蓋舊圖檔。新圖已經匯入 Unity（用新 Sprite 欄位指定）就留空。");
        public static readonly GUIContent Excluded = new GUIContent("不換皮的 Prefab",
            "選填。「要換皮的 Prefab 資料夾」裡，放在這個清單的 Prefab 會被略過。");
        public static readonly GUIContent OldColumn = new GUIContent("舊 Sprite（目前 Prefab 用的）",
            "要被換掉的圖。可用 Importer_v2 的「掃描 Prefab，自動填入舊 Sprite」自動列出。");
        public static readonly GUIContent NewColumn = new GUIContent("新 Sprite（留空＝同名覆蓋）",
            "換上去的圖。可手動拖入，或用 Importer_v2 的「依舊版 Prefab＋節點路徑填入新 Sprite」自動填入。");

        public const string TargetNote = "必填。預覽與執行只寫這裡（例如複製出來的 LiveRoom）。";
        public const string SourceNote = "選填、唯讀。被複製的原頁面（例如 CatAct），只拿來比對同一節點、學出對應，不會被寫入。";
        public const string ArtFolderNote = "選填。新 Sprite 留空時，用這裡同檔名的 PNG 覆蓋舊圖檔；新圖已匯入 Unity 就留空。";

        // 說明文字對齊欄位（標籤欄右側），讀起來是「這個欄位的補充」而不是另一個欄位。
        public static void DrawNote(string text)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth + 2f);
                GUILayout.Label(text, EditorStyles.wordWrappedMiniLabel);
            }
        }
    }

    /// <summary>對照表每一列畫成一行：舊 Sprite → 新 Sprite。上百筆時也不會每列展開三行。</summary>
    [CustomPropertyDrawer(typeof(SkinThemeEntry))]
    internal sealed class SkinThemeEntryDrawer : PropertyDrawer
    {
        public const float ArrowWidth = 18f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            position.height = EditorGUIUtility.singleLineHeight;
            var half = (position.width - ArrowWidth) / 2f;
            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            EditorGUI.PropertyField(new Rect(position.x, position.y, half, position.height),
                property.FindPropertyRelative(nameof(SkinThemeEntry.oldSprite)), GUIContent.none);
            EditorGUI.LabelField(new Rect(position.x + half, position.y, ArrowWidth, position.height), "→", EditorStyles.centeredGreyMiniLabel);
            EditorGUI.PropertyField(new Rect(position.x + half + ArrowWidth, position.y, half, position.height),
                property.FindPropertyRelative(nameof(SkinThemeEntry.newSprite)), GUIContent.none);
            EditorGUI.indentLevel = indent;
        }
    }

    /// <summary>
    /// SkinTheme 的中文 Inspector。編排依使用順序：先兩個 Prefab 資料夾（必填在上），選填設定收進摺疊區，
    /// 可能上百筆的對照表放最後，避免把短設定擠到畫面外。這裡只存設定，操作按鈕在 Importer_v2。
    /// </summary>
    [CustomEditor(typeof(PsUiSkinTheme))]
    internal sealed class PsUiSkinThemeEditor : Editor
    {
        private const float LabelWidth = 160f;
        private const string AdvancedFoldoutKey = "PhotoshopUiImporter.SkinThemeInspector.Advanced";

        private SerializedProperty targetFolder, sourceFolder, artFolder, entries, excluded;
        private ReorderableList entryList;
        private string folderWarning;

        private void OnEnable()
        {
            targetFolder = serializedObject.FindProperty(nameof(PsUiSkinTheme.targetPrefabFolderAsset));
            sourceFolder = serializedObject.FindProperty(nameof(PsUiSkinTheme.sourcePrefabFolderAsset));
            artFolder = serializedObject.FindProperty(nameof(PsUiSkinTheme.sourceArtFolder));
            entries = serializedObject.FindProperty(nameof(PsUiSkinTheme.entries));
            excluded = serializedObject.FindProperty(nameof(PsUiSkinTheme.excludedPrefabs));
            entryList = new ReorderableList(serializedObject, entries, true, true, true, true)
            {
                elementHeight = EditorGUIUtility.singleLineHeight + 4f,
                drawHeaderCallback = DrawEntryHeader,
                drawNoneElementCallback = rect => EditorGUI.LabelField(rect, "（尚無項目）", EditorStyles.miniLabel),
                drawElementCallback = (rect, index, active, focused) =>
                {
                    rect.y += 2f;
                    EditorGUI.PropertyField(rect, entries.GetArrayElementAtIndex(index), GUIContent.none);
                },
            };
        }

        public override void OnInspectorGUI()
        {
            var theme = (PsUiSkinTheme)target;
            serializedObject.Update();
            var previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = LabelWidth;
            try
            {
                EditorGUILayout.HelpBox("換皮對照表。這裡只存設定；掃描、配對、預覽與執行請到 Importer_v2 的「換皮工具」。", MessageType.None);
                if (GUILayout.Button("在 Importer_v2 開啟這份 SkinTheme", GUILayout.Height(24)))
                    PhotoshopUiImporterWindow.OpenSkinTheme(theme);

                if (PsUiSkinApplier.HasLegacyReference(theme))
                {
                    DrawLegacy(theme);
                    return;
                }

                Section("1. Prefab 資料夾");
                DrawFolderField(targetFolder, SkinThemeLabels.Target, SkinThemeLabels.TargetNote);
                DrawFolderField(sourceFolder, SkinThemeLabels.Source, SkinThemeLabels.SourceNote);
                if (!string.IsNullOrEmpty(folderWarning))
                    EditorGUILayout.HelpBox(folderWarning, MessageType.Warning);
                serializedObject.ApplyModifiedProperties();
                var conflict = PsUiSkinApplier.SourceTargetConflict(theme);
                if (conflict != null)
                    EditorGUILayout.HelpBox(conflict, MessageType.Error);

                DrawOptional();
                DrawEntries(theme);
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
                serializedObject.ApplyModifiedProperties();
            }
        }

        private void DrawLegacy(PsUiSkinTheme theme)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                PsUiSkinApplier.LegacyReferenceMessage + "\n" +
                $"轉換後：舊版 Prefab 資料夾（唯讀）＝{FolderLabel(theme.targetPrefabFolderAsset)}；" +
                $"要換皮的 Prefab 資料夾＝{FolderLabel(theme.referencePrefabFolderAsset)}。",
                MessageType.Warning);
            if (GUILayout.Button("轉換為新格式", GUILayout.Height(26)))
            {
                serializedObject.ApplyModifiedProperties();
                PsUiSkinApplier.MigrateLegacyReference(theme);
                serializedObject.Update();
                GUIUtility.ExitGUI();
            }
        }

        private void DrawOptional()
        {
            var setCount = (string.IsNullOrWhiteSpace(artFolder.stringValue) ? 0 : 1) + (excluded.arraySize > 0 ? 1 : 0);
            EditorGUILayout.Space(6);
            var open = SessionState.GetBool(AdvancedFoldoutKey, false);
            var title = setCount > 0 ? $"2. 選填設定（已設定 {setCount} 項）" : "2. 選填設定";
            var nowOpen = EditorGUILayout.Foldout(open, title, true, EditorStyles.foldoutHeader);
            if (nowOpen != open) SessionState.SetBool(AdvancedFoldoutKey, nowOpen);
            if (!nowOpen) return;

            // 用框把兩個選填項目圈在一起；不用縮排，免得長標籤被擠掉。
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(artFolder, SkinThemeLabels.ArtFolder);
                    if (GUILayout.Button("選擇", GUILayout.Width(52)))
                    {
                        var picked = EditorUtility.OpenFolderPanel(SkinThemeLabels.ArtFolder.text, artFolder.stringValue, string.Empty);
                        if (!string.IsNullOrEmpty(picked)) artFolder.stringValue = picked;
                        GUIUtility.keyboardControl = 0;
                    }
                }
                FieldNote(SkinThemeLabels.ArtFolderNote);
                EditorGUILayout.PropertyField(excluded, SkinThemeLabels.Excluded, true);
            }
        }

        private void DrawEntries(PsUiSkinTheme theme)
        {
            var total = theme.entries?.Count ?? 0;
            var filled = theme.entries?.Count(e => e != null && e.newSprite != null) ?? 0;
            Section("3. 對照表");
            EditorGUILayout.LabelField($"共 {total} 筆・已填新 Sprite {filled}・未填 {total - filled}", EditorStyles.miniLabel);
            if (total == 0)
                EditorGUILayout.LabelField("還沒有項目：到 Importer_v2 按「掃描 Prefab，自動填入舊 Sprite」，或按清單右下的 + 手動新增。",
                    EditorStyles.wordWrappedMiniLabel);
            entryList.DoLayoutList();
        }

        private static void DrawEntryHeader(Rect rect)
        {
            // 標題列對齊下方每一列的兩個欄位（清單內容左側有拖曳把手）。
            rect.xMin += 14f;
            var half = (rect.width - SkinThemeEntryDrawer.ArrowWidth) / 2f;
            EditorGUI.LabelField(new Rect(rect.x, rect.y, half, rect.height), SkinThemeLabels.OldColumn, EditorStyles.miniBoldLabel);
            EditorGUI.LabelField(new Rect(rect.x + half + SkinThemeEntryDrawer.ArrowWidth, rect.y, half, rect.height),
                SkinThemeLabels.NewColumn, EditorStyles.miniBoldLabel);
        }

        private void DrawFolderField(SerializedProperty property, GUIContent label, string note)
        {
            EditorGUI.BeginChangeCheck();
            var value = EditorGUILayout.ObjectField(label, property.objectReferenceValue, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                if (value != null && !AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(value)))
                {
                    folderWarning = $"「{label.text}」只能放資料夾，{Path.GetFileName(AssetDatabase.GetAssetPath(value))} 不是資料夾。";
                }
                else
                {
                    folderWarning = null;
                    property.objectReferenceValue = value;
                }
            }
            FieldNote(note);
        }

        private static void Section(string title)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static void FieldNote(string text) => SkinThemeLabels.DrawNote(text);

        private static string FolderLabel(Object folderAsset) =>
            folderAsset != null ? AssetDatabase.GetAssetPath(folderAsset) : "（未設定）";
    }
}

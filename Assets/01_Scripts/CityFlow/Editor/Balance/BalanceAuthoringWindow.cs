#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using CityFlow.Configs;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.EditorTools.Save;
using CityFlow.Gameplay.Research;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityFlow.EditorTools.Balance
{
    public sealed class BalanceAuthoringWindow : EditorWindow
    {
        internal enum ResearchBalanceSection
        {
            BuildingUnlock,
            Expansion
        }

        internal const string SourceScenePath =
            "Assets/00_Scenes/CityFlowIntegrated_cmt.unity";
        internal const string BalanceScenePath =
            "Assets/00_Scenes/Debug/CityFlowBalance_Lee.unity";
        internal const string ResearchCatalogPath =
            "Assets/05_ScriptableObjects/Resources/CityFlow/ResearchCatalog.asset";
        internal const string WorkingResearchCatalogPath =
            "Assets/05_ScriptableObjects/Balance/Editor/ResearchCatalog_Balance.asset";
        private const string WorkingRoot =
            "Assets/05_ScriptableObjects/Balance/Editor";

        private sealed class BalanceEntry
        {
            public readonly string Group;
            public readonly string Label;
            public readonly string SourcePath;
            public readonly string WorkingPath;

            public BalanceEntry(
                string group,
                string label,
                string sourcePath,
                string workingName)
            {
                Group = group;
                Label = label;
                SourcePath = sourcePath;
                WorkingPath = $"{WorkingRoot}/{workingName}.asset";
            }
        }

        private static readonly BalanceEntry[] Entries =
        {
            new(
                "핵심",
                "시뮬레이션",
                "Assets/05_ScriptableObjects/SimConfig_Integrated.asset",
                "SimConfig_Integrated_Balance"),
            new(
                "핵심",
                "경제",
                "Assets/05_ScriptableObjects/EconomyConfig.asset",
                "EconomyConfig_Balance"),
            new(
                "핵심",
                "거리 보상",
                "Assets/05_ScriptableObjects/DistanceRewardConfig.asset",
                "DistanceRewardConfig_Balance"),
            new(
                "핵심",
                "인구",
                "Assets/05_ScriptableObjects/CityFlow/PopulationConfig.asset",
                "PopulationConfig_Balance"),
            new(
                "시간",
                "게임 시간",
                "Assets/05_ScriptableObjects/Resources/CityFlow/GameTimeSettings.asset",
                "GameTimeSettings_Balance"),
            new(
                "연구",
                "건물 해금 연구",
                ResearchCatalogPath,
                "ResearchCatalog_Balance"),
            new(
                "교통",
                "시내버스",
                "Assets/05_ScriptableObjects/CityFlow/Transit/CityBusDefinition.asset",
                "CityBusDefinition_Balance"),
            new(
                "교통",
                "스쿨버스",
                "Assets/05_ScriptableObjects/CityFlow/Transit/SchoolBusDefinition.asset",
                "SchoolBusDefinition_Balance"),
            new(
                "교통",
                "시내버스 운행 시간",
                "Assets/05_ScriptableObjects/CityFlow/Transit/DefaultCityBusSchedule.asset",
                "DefaultCityBusSchedule_Balance"),
            new(
                "교통",
                "스쿨버스 운행 시간",
                "Assets/05_ScriptableObjects/CityFlow/Transit/KoreanSchoolBusSchedule.asset",
                "KoreanSchoolBusSchedule_Balance"),
            new(
                "응급",
                "응급 신고와 구급차",
                "Assets/05_ScriptableObjects/CityFlow/Emergency/EmergencyIncidentConfig.asset",
                "EmergencyIncidentConfig_Balance"),
            new(
                "인프라",
                "신호등",
                "Assets/05_ScriptableObjects/CityFlow/InfrastructureData/SignalData.asset",
                "SignalData_Balance"),
            new(
                "인프라",
                "회전교차로",
                "Assets/05_ScriptableObjects/CityFlow/InfrastructureData/RoundaboutData.asset",
                "RoundaboutData_Balance"),
            new(
                "인프라",
                "고가도로",
                "Assets/05_ScriptableObjects/CityFlow/InfrastructureData/OverpassData.asset",
                "OverpassData_Balance"),
            new(
                "인프라",
                "일방통행",
                "Assets/05_ScriptableObjects/CityFlow/InfrastructureData/OnewayData.asset",
                "OnewayData_Balance"),
            new(
                "인프라",
                "회전 제한",
                "Assets/05_ScriptableObjects/CityFlow/InfrastructureData/TurnRestrictionData.asset",
                "TurnRestrictionData_Balance"),
            new(
                "인프라",
                "우선 도로",
                "Assets/05_ScriptableObjects/CityFlow/InfrastructureData/PriorityRoadData.asset",
                "PriorityRoadData_Balance"),
            new(
                "인프라",
                "고속도로",
                "Assets/05_ScriptableObjects/CityFlow/InfrastructureData/HighwayData.asset",
                "HighwayData_Balance")
        };

        private readonly List<string> validationMessages = new();
        private Vector2 scroll;
        private string selectedGroup = "핵심";
        private int selectedEntryIndex;
        private int selectedResearchIndex;
        private ResearchBalanceSection selectedResearchSection;
        private bool showResearchAdvanced;
        private Dictionary<string, string> researchUnlockLabels;
        private UnityEditor.Editor cachedAssetEditor;
        private UnityEngine.Object cachedTarget;

        internal static bool IsResearchInSection(
            ResearchCategory category,
            ResearchBalanceSection section)
        {
            bool isExpansion = category == ResearchCategory.Expansion;
            return isExpansion ==
                   (section == ResearchBalanceSection.Expansion);
        }

        [MenuItem("CityFlow/Balance/밸런스 편집기 열기")]
        public static void OpenWindow()
        {
            BalanceAuthoringWindow window =
                GetWindow<BalanceAuthoringWindow>("게임 밸런스");
            window.minSize = new Vector2(760f, 520f);
            window.Show();
        }

        [MenuItem("CityFlow/Balance/작업 공간 생성 및 열기")]
        public static void CreateAndOpenWorkspace()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EnsureWorkingAssets();
            EnsureBalanceScene();

            Scene scene = EditorSceneManager.OpenScene(
                BalanceScenePath,
                OpenSceneMode.Single);
            int changedReferences = RewireSceneToWorkingAssets(scene);

            if (changedReferences > 0)
            {
                EditorSceneManager.SaveScene(scene);
            }

            EditorSceneManager.playModeStartScene =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(BalanceScenePath);
            OpenWindow();
            Debug.Log(
                $"[Balance] 전용 Scene 준비 완료: {BalanceScenePath} " +
                $"(작업용 설정 연결 {changedReferences}개)");
        }

        private void OnDisable()
        {
            DestroyCachedEditor();
        }

        private void OnProjectChange()
        {
            researchUnlockLabels = null;
            Repaint();
        }

        private void OnGUI()
        {
            DrawHeader();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawNavigation();
                DrawSelectedAsset();
            }
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(
                "게임 밸런스 작업 공간",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "통합 Scene과 실제 설정 에셋은 직접 수정하지 않습니다. " +
                "작업용 복사본에서 먼저 플레이 테스트한 뒤, 확정 버튼으로만 실제 수치에 반영하세요.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("작업 공간 생성 / 열기", GUILayout.Height(28f)))
                {
                    CreateAndOpenWorkspace();
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button("저장", GUILayout.Height(28f)))
                {
                    AssetDatabase.SaveAssets();
                }

                if (GUILayout.Button("검증", GUILayout.Height(28f)))
                {
                    RunValidation();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(1f, 0.75f, 0.35f);
                if (GUILayout.Button(
                        "테스트 완료 후 확정값을 실제 에셋에 반영",
                        GUILayout.Height(28f)))
                {
                    PublishWorkingValues();
                }

                GUI.backgroundColor = Color.white;
            }

            using (new EditorGUI.DisabledScope(
                       EditorApplication.isPlayingOrWillChangePlaymode ||
                       EditorApplication.isCompiling ||
                       EditorApplication.isUpdating))
            {
                GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
                if (GUILayout.Button(
                        "현재 게임 진행 데이터만 초기화",
                        GUILayout.Height(24f)))
                {
                    GameProgressResetTool.ConfirmAndReset();
                    GUIUtility.ExitGUI();
                }

                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.LabelField(
                $"테스트 Scene: {BalanceScenePath}",
                EditorStyles.miniLabel);

            foreach (string message in validationMessages)
            {
                EditorGUILayout.HelpBox(message, MessageType.Warning);
            }

            EditorGUILayout.Space(4f);
        }

        private void DrawNavigation()
        {
            string[] groups = Entries
                .Select(entry => entry.Group)
                .Distinct()
                .ToArray();

            using (new EditorGUILayout.VerticalScope(
                       EditorStyles.helpBox,
                       GUILayout.Width(190f)))
            {
                EditorGUILayout.LabelField("분류", EditorStyles.boldLabel);

                foreach (string group in groups)
                {
                    bool selected = group == selectedGroup;
                    if (GUILayout.Toggle(
                            selected,
                            group,
                            EditorStyles.miniButton) &&
                        !selected)
                    {
                        selectedGroup = group;
                        selectedEntryIndex = 0;
                        scroll = Vector2.zero;
                        DestroyCachedEditor();
                    }
                }

                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("설정", EditorStyles.boldLabel);

                BalanceEntry[] groupEntries = Entries
                    .Where(entry => entry.Group == selectedGroup)
                    .ToArray();

                for (int i = 0; i < groupEntries.Length; i++)
                {
                    if (groupEntries[i].Label == "건물 해금 연구")
                    {
                        DrawResearchNavigationButton(
                            i,
                            ResearchBalanceSection.BuildingUnlock,
                            "건물 해금 연구");
                        DrawResearchNavigationButton(
                            i,
                            ResearchBalanceSection.Expansion,
                            "개척 시스템");
                        continue;
                    }

                    if (GUILayout.Toggle(
                            i == selectedEntryIndex,
                            groupEntries[i].Label,
                            EditorStyles.miniButtonLeft) &&
                        selectedEntryIndex != i)
                    {
                        selectedEntryIndex = i;
                        scroll = Vector2.zero;
                        DestroyCachedEditor();
                    }
                }
            }
        }

        private void DrawResearchNavigationButton(
            int entryIndex,
            ResearchBalanceSection section,
            string label)
        {
            bool selected = selectedEntryIndex == entryIndex &&
                            selectedResearchSection == section;
            if (!GUILayout.Toggle(
                    selected,
                    label,
                    EditorStyles.miniButtonLeft) ||
                selected)
            {
                return;
            }

            selectedEntryIndex = entryIndex;
            selectedResearchSection = section;
            selectedResearchIndex = -1;
            scroll = Vector2.zero;
            DestroyCachedEditor();
        }

        private void DrawSelectedAsset()
        {
            BalanceEntry[] groupEntries = Entries
                .Where(entry => entry.Group == selectedGroup)
                .ToArray();

            if (groupEntries.Length == 0)
            {
                return;
            }

            selectedEntryIndex = Mathf.Clamp(
                selectedEntryIndex,
                0,
                groupEntries.Length - 1);
            BalanceEntry entry = groupEntries[selectedEntryIndex];
            bool isResearchEntry = entry.Label == "건물 해금 연구";
            string selectedLabel = isResearchEntry &&
                                   selectedResearchSection ==
                                   ResearchBalanceSection.Expansion
                ? "개척 시스템"
                : entry.Label;
            UnityEngine.Object target =
                AssetDatabase.LoadMainAssetAtPath(entry.WorkingPath);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(selectedLabel, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"작업용: {entry.WorkingPath}",
                    EditorStyles.miniLabel);

                if (entry.Label == "경제")
                {
                    EditorGUILayout.HelpBox(
                        "경제 설정의 realMinutesPerGameDay는 현재 런타임에서 사용하지 않는 이전 필드입니다. " +
                        "실제 하루 길이는 '시간 > 게임 시간'에서 조정하세요.",
                        MessageType.Info);
                }
                else if (entry.Label == "시뮬레이션")
                {
                    EditorGUILayout.HelpBox(
                        "플레이 시작 시 DayLengthSeconds는 게임 시간 설정과 자동 동기화됩니다. " +
                        "하루 길이는 '시간 > 게임 시간'에서 조정하세요.",
                        MessageType.Info);
                }
                else if (isResearchEntry &&
                         selectedResearchSection ==
                         ResearchBalanceSection.BuildingUnlock)
                {
                    EditorGUILayout.HelpBox(
                        "연구별 선행 연구, 해금 조건, 비용, 게임 내 연구 시간을 조정합니다. " +
                        "조건 목록이 비어 있으면 기존 단일 조건을 사용하고, 조건을 여러 개 넣으면 모두 만족해야 합니다. " +
                        "연구 ID는 건물 해금 연결에 사용되므로 변경할 때 주의하세요.",
                        MessageType.Info);
                }
                else if (isResearchEntry)
                {
                    EditorGUILayout.HelpBox(
                        "개척 단계별 조건, 비용, 게임 내 연구 시간과 확장 단계 연결을 조정합니다. " +
                        "완료한 연구의 확장 단계 ID가 월드 확장 설정과 연결됩니다.",
                        MessageType.Info);
                }

                if (target == null)
                {
                    EditorGUILayout.HelpBox(
                        "작업용 에셋이 없습니다. '작업 공간 생성 / 열기'를 눌러 주세요.",
                        MessageType.Warning);
                    return;
                }

                if (isResearchEntry)
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    DrawResearchCatalog(target, selectedResearchSection);
                    EditorGUILayout.EndScrollView();
                    return;
                }

                if (cachedTarget != target || cachedAssetEditor == null)
                {
                    DestroyCachedEditor();
                    cachedTarget = target;
                    cachedAssetEditor =
                        UnityEditor.Editor.CreateEditor(target);
                }

                scroll = EditorGUILayout.BeginScrollView(scroll);
                cachedAssetEditor.OnInspectorGUI();
                EditorGUILayout.EndScrollView();
            }
        }

        private static void EnsureWorkingAssets()
        {
            EnsureFolder("Assets/05_ScriptableObjects/Balance");
            EnsureFolder(WorkingRoot);

            foreach (BalanceEntry entry in Entries)
            {
                if (AssetDatabase.LoadMainAssetAtPath(entry.SourcePath) == null)
                {
                    Debug.LogWarning(
                        $"[Balance] 원본 설정 에셋을 찾지 못했습니다: {entry.SourcePath}");
                    continue;
                }

                if (AssetDatabase.LoadMainAssetAtPath(entry.WorkingPath) != null)
                {
                    continue;
                }

                if (!AssetDatabase.CopyAsset(entry.SourcePath, entry.WorkingPath))
                {
                    Debug.LogError(
                        $"[Balance] 작업용 에셋 복사 실패: {entry.WorkingPath}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void EnsureBalanceScene()
        {
            EnsureFolder("Assets/00_Scenes/Debug");

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
            {
                throw new InvalidOperationException(
                    $"통합 Scene을 찾을 수 없습니다: {SourceScenePath}");
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BalanceScenePath) != null)
            {
                return;
            }

            if (!AssetDatabase.CopyAsset(SourceScenePath, BalanceScenePath))
            {
                throw new InvalidOperationException(
                    $"밸런스 Scene 복제에 실패했습니다: {BalanceScenePath}");
            }

            AssetDatabase.Refresh();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int separatorIndex = path.LastIndexOf('/');
            string parent = path.Substring(0, separatorIndex);
            string folderName = path.Substring(separatorIndex + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        private static int RewireSceneToWorkingAssets(Scene scene)
        {
            Dictionary<UnityEngine.Object, UnityEngine.Object> replacements =
                new();
            UnityEngine.Object workingResearchCatalog =
                AssetDatabase.LoadMainAssetAtPath(
                    WorkingResearchCatalogPath);

            foreach (BalanceEntry entry in Entries)
            {
                UnityEngine.Object source =
                    AssetDatabase.LoadMainAssetAtPath(entry.SourcePath);
                UnityEngine.Object working =
                    AssetDatabase.LoadMainAssetAtPath(entry.WorkingPath);

                if (source != null && working != null)
                {
                    replacements[source] = working;
                }
            }

            int changeCount = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Component component in
                         root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null)
                    {
                        continue;
                    }

                    SerializedObject serialized = new(component);
                    SerializedProperty researchCatalogProperty =
                        UsesResearchCatalog(component)
                            ? serialized.FindProperty("catalog")
                            : null;
                    SerializedProperty property = serialized.GetIterator();
                    bool enterChildren = true;
                    bool recorded = false;

                    if (researchCatalogProperty != null &&
                        workingResearchCatalog != null &&
                        researchCatalogProperty.objectReferenceValue !=
                        workingResearchCatalog)
                    {
                        Undo.RecordObject(
                            component,
                            "연구 작업용 설정 연결");
                        recorded = true;
                        researchCatalogProperty.objectReferenceValue =
                            workingResearchCatalog;
                        changeCount++;
                    }

                    while (property.Next(enterChildren))
                    {
                        enterChildren = true;
                        if (property.propertyType !=
                            SerializedPropertyType.ObjectReference)
                        {
                            continue;
                        }

                        UnityEngine.Object current = property.objectReferenceValue;
                        if (current == null ||
                            !replacements.TryGetValue(
                                current,
                                out UnityEngine.Object replacement))
                        {
                            continue;
                        }

                        if (!recorded)
                        {
                            Undo.RecordObject(component, "밸런스 작업용 설정 연결");
                            recorded = true;
                        }

                        property.objectReferenceValue = replacement;
                        changeCount++;
                    }

                    if (recorded)
                    {
                        serialized.ApplyModifiedProperties();
                        EditorUtility.SetDirty(component);
                    }
                }
            }

            if (changeCount > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            return changeCount;
        }

        private static bool UsesResearchCatalog(Component component)
        {
            string typeName = component?.GetType().FullName;
            return typeName ==
                       "CityFlow.Gameplay.Research.ResearchUnlockService" ||
                   typeName ==
                       "CityFlow.UI.ResearchPanelController";
        }

        private void RunValidation()
        {
            validationMessages.Clear();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
            {
                validationMessages.Add("통합 Scene 원본이 없습니다.");
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BalanceScenePath) == null)
            {
                validationMessages.Add("밸런스 전용 Scene이 아직 생성되지 않았습니다.");
            }

            if (EditorBuildSettings.scenes.Any(
                    scene => scene.path == BalanceScenePath))
            {
                validationMessages.Add(
                    "밸런스 전용 Scene이 Build Settings에 포함되어 있습니다. 제거해 주세요.");
            }

            foreach (BalanceEntry entry in Entries)
            {
                if (AssetDatabase.LoadMainAssetAtPath(entry.SourcePath) == null)
                {
                    validationMessages.Add($"원본 누락: {entry.Label}");
                }

                if (AssetDatabase.LoadMainAssetAtPath(entry.WorkingPath) == null)
                {
                    validationMessages.Add($"작업용 에셋 누락: {entry.Label}");
                }
            }

            ValidateTimeConsistency();
            ValidateResearchCatalog();
            ValidateLoadedSceneReferences();

            if (validationMessages.Count == 0)
            {
                ShowNotification(new GUIContent("밸런스 작업 공간 검증 완료"));
            }
        }

        private void ValidateTimeConsistency()
        {
            BalanceEntry simEntry = Entries.First(
                entry => entry.Label == "시뮬레이션");
            BalanceEntry timeEntry = Entries.First(
                entry => entry.Label == "게임 시간");

            UnityEngine.Object sim =
                AssetDatabase.LoadMainAssetAtPath(simEntry.WorkingPath);
            UnityEngine.Object time =
                AssetDatabase.LoadMainAssetAtPath(timeEntry.WorkingPath);

            if (sim == null || time == null)
            {
                return;
            }

            SerializedProperty daySeconds =
                new SerializedObject(sim).FindProperty("Value.DayLengthSeconds");
            SerializedProperty realMinutes =
                new SerializedObject(time).FindProperty("realMinutesPerGameDay");

            if (daySeconds == null || realMinutes == null)
            {
                return;
            }

            float timeAssetSeconds = realMinutes.floatValue * 60f;
            if (!Mathf.Approximately(daySeconds.floatValue, timeAssetSeconds))
            {
                validationMessages.Add(
                    $"하루 길이 불일치: 시뮬레이션 {daySeconds.floatValue:0.##}초 / " +
                    $"게임 시간 {timeAssetSeconds:0.##}초. 두 값을 같은 기준으로 맞춰 주세요.");
            }
        }

        private void DrawResearchCatalog(
            UnityEngine.Object target,
            ResearchBalanceSection section)
        {
            var serialized = new SerializedObject(target);
            serialized.Update();
            SerializedProperty entries =
                serialized.FindProperty("entries");
            if (entries == null || entries.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "편집할 연구가 없습니다.",
                    MessageType.Warning);
                return;
            }

            var matchingIndices = new List<int>();
            for (int index = 0; index < entries.arraySize; index++)
            {
                SerializedProperty entry =
                    entries.GetArrayElementAtIndex(index);
                SerializedProperty category =
                    entry.FindPropertyRelative("category");
                ResearchCategory researchCategory = category != null
                    ? (ResearchCategory)category.enumValueIndex
                    : ResearchCategory.Commercial;
                if (IsResearchInSection(researchCategory, section))
                {
                    matchingIndices.Add(index);
                }
            }

            if (matchingIndices.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    section == ResearchBalanceSection.Expansion
                        ? "설정할 개척 단계가 없습니다."
                        : "설정할 건물 해금 연구가 없습니다.",
                    MessageType.Warning);
                return;
            }

            int popupIndex = matchingIndices.IndexOf(selectedResearchIndex);
            popupIndex = popupIndex >= 0 ? popupIndex : 0;
            string[] researchLabels = matchingIndices
                .Select(index => GetResearchLabel(
                    entries.GetArrayElementAtIndex(index),
                    index))
                .ToArray();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                "1. 수정할 연구 선택",
                EditorStyles.boldLabel);
            popupIndex = EditorGUILayout.Popup(popupIndex, researchLabels);
            selectedResearchIndex = matchingIndices[popupIndex];

            SerializedProperty selectedEntry =
                entries.GetArrayElementAtIndex(
                    selectedResearchIndex);
            string selectedResearchId = (
                selectedEntry.FindPropertyRelative("researchId")
                    ?.stringValue ?? string.Empty).Trim();

            if (section == ResearchBalanceSection.BuildingUnlock)
            {
                DrawUnlockedBuildingSummary(selectedResearchId);
            }
            else
            {
                DrawExpansionStageSummary(selectedEntry);
            }

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(
                       EditorStyles.helpBox))
            {
                DrawResearchIdentity(entries, selectedEntry, section);
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.VerticalScope(
                       EditorStyles.helpBox))
            {
                DrawResearchRequirements(selectedEntry);
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.VerticalScope(
                       EditorStyles.helpBox))
            {
                DrawResearchCostAndDuration(selectedEntry);
            }

            EditorGUILayout.Space(6f);
            showResearchAdvanced = EditorGUILayout.Foldout(
                showResearchAdvanced,
                "고급 설정",
                true);
            if (showResearchAdvanced)
            {
                using (new EditorGUILayout.VerticalScope(
                           EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox(
                        "연구 ID는 건물 데이터와 저장 데이터가 사용합니다. " +
                        "이미 연결된 연구 ID는 특별한 이유가 없다면 변경하지 마세요.",
                        MessageType.Warning);
                    EditorGUILayout.PropertyField(
                        selectedEntry.FindPropertyRelative(
                            "researchId"),
                        new GUIContent("연구 ID"));
                }
            }

            if (serialized.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(target);
            }
        }

        private static void DrawExpansionStageSummary(
            SerializedProperty selectedEntry)
        {
            string stageId = (
                selectedEntry.FindPropertyRelative("worldGridStageId")
                    ?.stringValue ?? string.Empty).Trim();
            EditorGUILayout.HelpBox(
                stageId.Length > 0
                    ? $"연구 완료 후 적용할 개척 단계: {stageId}"
                    : "연결된 개척 단계가 없습니다. 확장 단계 ID를 설정해 주세요.",
                stageId.Length > 0
                    ? MessageType.Info
                    : MessageType.Warning);
        }

        private void DrawUnlockedBuildingSummary(
            string researchId)
        {
            researchUnlockLabels ??=
                BuildResearchUnlockLabels();

            if (researchId.Length > 0 &&
                researchUnlockLabels.TryGetValue(
                    researchId,
                    out string buildingNames))
            {
                EditorGUILayout.HelpBox(
                    $"이 연구가 해금하는 건물: {buildingNames}",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                "이 연구 ID에 연결된 건물이 없습니다.",
                MessageType.Warning);
        }

        private static Dictionary<string, string>
            BuildResearchUnlockLabels()
        {
            var namesByResearch =
                new Dictionary<string, List<string>>(
                    StringComparer.Ordinal);

            foreach (string guid in
                     AssetDatabase.FindAssets(
                         "t:BuildingDefinitionSO"))
            {
                string path =
                    AssetDatabase.GUIDToAssetPath(guid);
                BuildingDefinitionSO definition =
                    AssetDatabase.LoadAssetAtPath<
                        BuildingDefinitionSO>(path);
                AddResearchBuildingLabel(
                    namesByResearch,
                    definition?.RequiredResearchId,
                    definition?.buildingName);
            }

            foreach (string guid in
                     AssetDatabase.FindAssets("t:TileDataSO"))
            {
                string path =
                    AssetDatabase.GUIDToAssetPath(guid);
                TileDataSO tileData =
                    AssetDatabase.LoadAssetAtPath<TileDataSO>(
                        path);
                AddResearchBuildingLabel(
                    namesByResearch,
                    tileData?.RequiredResearchId,
                    tileData?.BuildingName);
            }

            return namesByResearch.ToDictionary(
                pair => pair.Key,
                pair => string.Join(
                    ", ",
                    pair.Value
                        .Where(name =>
                            !string.IsNullOrWhiteSpace(name))
                        .Distinct()));
        }

        private static void AddResearchBuildingLabel(
            Dictionary<string, List<string>> namesByResearch,
            string researchId,
            string buildingName)
        {
            string normalizedId =
                researchId?.Trim() ?? string.Empty;
            string normalizedName =
                buildingName?.Trim() ?? string.Empty;
            if (normalizedId.Length == 0 ||
                normalizedName.Length == 0)
            {
                return;
            }

            if (!namesByResearch.TryGetValue(
                    normalizedId,
                    out List<string> names))
            {
                names = new List<string>();
                namesByResearch.Add(normalizedId, names);
            }

            names.Add(normalizedName);
        }

        private static string GetResearchLabel(
            SerializedProperty entry,
            int index)
        {
            string displayName = (
                entry.FindPropertyRelative("displayName")
                    ?.stringValue ?? string.Empty).Trim();
            string researchId = (
                entry.FindPropertyRelative("researchId")
                    ?.stringValue ?? string.Empty).Trim();

            if (displayName.Length > 0)
            {
                return displayName;
            }

            return researchId.Length > 0
                ? researchId
                : $"이름 없는 연구 {index + 1}";
        }

        private static void DrawResearchIdentity(
            SerializedProperty entries,
            SerializedProperty selectedEntry,
            ResearchBalanceSection section)
        {
            EditorGUILayout.LabelField(
                "2. 이름과 선행 연구",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                selectedEntry.FindPropertyRelative("displayName"),
                new GUIContent(
                    "화면 표시 이름",
                    "게임 연구 화면에 보이는 이름입니다."));

            SerializedProperty category =
                selectedEntry.FindPropertyRelative("category");
            if (section == ResearchBalanceSection.BuildingUnlock)
            {
                EditorGUILayout.PropertyField(
                    category,
                    new GUIContent(
                        "해금 카테고리",
                        "게임 연구 화면에서 이 건물이 표시될 카테고리입니다."));
            }
            else
            {
                EditorGUILayout.LabelField("설정 종류", "개척");
            }

            if (category != null &&
                category.enumValueIndex ==
                (int)ResearchCategory.Expansion)
            {
                EditorGUILayout.PropertyField(
                    selectedEntry.FindPropertyRelative(
                        "worldGridStageId"),
                    new GUIContent(
                        "확장 단계 ID",
                        "연구 완료 시 해금할 WorldGridUnlockProfile 단계 ID입니다."));
            }

            DrawPrerequisitePopup(entries, selectedEntry, section);
        }

        private static void DrawPrerequisitePopup(
            SerializedProperty entries,
            SerializedProperty selectedEntry,
            ResearchBalanceSection section)
        {
            SerializedProperty selectedIdProperty =
                selectedEntry.FindPropertyRelative("researchId");
            SerializedProperty prerequisiteProperty =
                selectedEntry.FindPropertyRelative(
                    "prerequisiteId");
            string selectedId =
                selectedIdProperty?.stringValue?.Trim() ??
                string.Empty;
            string currentPrerequisite =
                prerequisiteProperty?.stringValue?.Trim() ??
                string.Empty;

            var values = new List<string> { string.Empty };
            var labels = new List<string>
            {
                "없음 — 바로 연구 가능"
            };

            for (int index = 0;
                 index < entries.arraySize;
                 index++)
            {
                SerializedProperty candidate =
                    entries.GetArrayElementAtIndex(index);
                SerializedProperty candidateCategory =
                    candidate.FindPropertyRelative("category");
                ResearchCategory candidateResearchCategory =
                    candidateCategory != null
                        ? (ResearchCategory)candidateCategory.enumValueIndex
                        : ResearchCategory.Commercial;
                if (!IsResearchInSection(
                        candidateResearchCategory,
                        section))
                {
                    continue;
                }

                string candidateId =
                    candidate.FindPropertyRelative("researchId")
                        ?.stringValue?.Trim() ?? string.Empty;
                if (candidateId.Length == 0 ||
                    candidateId == selectedId)
                {
                    continue;
                }

                values.Add(candidateId);
                labels.Add(
                    GetResearchLabel(candidate, index));
            }

            int selectedIndex = values.IndexOf(
                currentPrerequisite);
            if (selectedIndex < 0 &&
                currentPrerequisite.Length > 0)
            {
                values.Add(currentPrerequisite);
                labels.Add(
                    $"연결 오류 — {currentPrerequisite}");
                selectedIndex = values.Count - 1;
            }

            selectedIndex = EditorGUILayout.Popup(
                new GUIContent(
                    "먼저 완료할 연구",
                    "이 연구를 시작하기 전에 완료해야 하는 연구입니다."),
                Mathf.Max(0, selectedIndex),
                labels.ToArray());
            prerequisiteProperty.stringValue =
                values[selectedIndex];
        }

        private static void DrawResearchRequirements(
            SerializedProperty selectedEntry)
        {
            EditorGUILayout.LabelField(
                "3. 해금 조건",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "표시된 조건을 모두 만족하면 연구를 시작할 수 있습니다.",
                MessageType.None);

            SerializedProperty requirements =
                selectedEntry.FindPropertyRelative("requirements");
            if (requirements == null)
            {
                EditorGUILayout.HelpBox(
                    "조건 목록을 찾을 수 없습니다.",
                    MessageType.Error);
                return;
            }

            if (requirements.arraySize == 0)
            {
                EditorGUILayout.LabelField(
                    "조건 1",
                    EditorStyles.miniBoldLabel);
                DrawResearchCondition(
                    selectedEntry.FindPropertyRelative(
                        "conditionKind"),
                    selectedEntry.FindPropertyRelative(
                        "threshold"),
                    selectedEntry.FindPropertyRelative(
                        "targetTileType"));

                if (GUILayout.Button(
                        "+ 조건 하나 더 추가",
                        GUILayout.Height(24f)))
                {
                    ConvertLegacyConditionToRequirements(
                        selectedEntry,
                        requirements);
                }

                return;
            }

            int removeIndex = -1;
            for (int index = 0;
                 index < requirements.arraySize;
                 index++)
            {
                SerializedProperty requirement =
                    requirements.GetArrayElementAtIndex(index);
                using (new EditorGUILayout.VerticalScope(
                           EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            $"조건 {index + 1}",
                            EditorStyles.miniBoldLabel);
                        if (GUILayout.Button(
                                "삭제",
                                GUILayout.Width(48f)))
                        {
                            removeIndex = index;
                        }
                    }

                    DrawResearchCondition(
                        requirement.FindPropertyRelative(
                            "conditionKind"),
                        requirement.FindPropertyRelative(
                            "threshold"),
                        requirement.FindPropertyRelative(
                            "targetTileType"));
                }
            }

            if (removeIndex >= 0)
            {
                if (requirements.arraySize == 1)
                {
                    CopyCondition(
                        requirements.GetArrayElementAtIndex(0),
                        selectedEntry);
                }
                requirements.DeleteArrayElementAtIndex(
                    removeIndex);
            }

            if (GUILayout.Button(
                    "+ 조건 추가",
                    GUILayout.Height(24f)))
            {
                AddDefaultRequirement(requirements);
            }
        }

        private static void DrawResearchCondition(
            SerializedProperty conditionKind,
            SerializedProperty threshold,
            SerializedProperty targetTileType)
        {
            string[] conditionLabels =
            {
                "전날 도착 차량 수",
                "도시 인구",
                "건물 개수"
            };
            conditionKind.enumValueIndex =
                EditorGUILayout.Popup(
                    "조건 종류",
                    conditionKind.enumValueIndex,
                    conditionLabels);

            if (conditionKind.enumValueIndex ==
                (int)ResearchConditionKind.BuildingCount)
            {
                targetTileType.enumValueIndex =
                    EditorGUILayout.Popup(
                        "대상 건물",
                        targetTileType.enumValueIndex,
                        GetTileTypeLabels());
            }

            threshold.intValue = Mathf.Max(
                0,
                EditorGUILayout.IntField(
                    conditionKind.enumValueIndex ==
                    (int)ResearchConditionKind.BuildingCount
                        ? "필요 개수"
                        : "필요 수치",
                    threshold.intValue));
        }

        private static string[] GetTileTypeLabels()
        {
            return Enum.GetValues(typeof(TileType))
                .Cast<TileType>()
                .Select(type => type switch
                {
                    TileType.Empty => "빈 공간",
                    TileType.Road => "도로",
                    TileType.House => "주거 지역",
                    TileType.Office => "회사",
                    TileType.School => "학교",
                    TileType.Hospital => "병원",
                    TileType.SpecialBuilding => "특수 건물",
                    TileType.UnderConstruction => "건설 중",
                    _ => type.ToString()
                })
                .ToArray();
        }

        private static void ConvertLegacyConditionToRequirements(
            SerializedProperty selectedEntry,
            SerializedProperty requirements)
        {
            requirements.arraySize = 2;
            CopyCondition(
                selectedEntry,
                requirements.GetArrayElementAtIndex(0));
            InitializeRequirement(
                requirements.GetArrayElementAtIndex(1));
        }

        private static void AddDefaultRequirement(
            SerializedProperty requirements)
        {
            int index = requirements.arraySize;
            requirements.InsertArrayElementAtIndex(index);
            InitializeRequirement(
                requirements.GetArrayElementAtIndex(index));
        }

        private static void InitializeRequirement(
            SerializedProperty requirement)
        {
            requirement.FindPropertyRelative(
                    "conditionKind")
                .enumValueIndex =
                (int)ResearchConditionKind.BuildingCount;
            requirement.FindPropertyRelative(
                    "threshold")
                .intValue = 1;
            requirement.FindPropertyRelative(
                    "targetTileType")
                .enumValueIndex = (int)TileType.House;
        }

        private static void CopyCondition(
            SerializedProperty source,
            SerializedProperty destination)
        {
            destination.FindPropertyRelative(
                    "conditionKind")
                .enumValueIndex =
                source.FindPropertyRelative(
                        "conditionKind")
                    .enumValueIndex;
            destination.FindPropertyRelative(
                    "threshold")
                .intValue =
                source.FindPropertyRelative(
                        "threshold")
                    .intValue;
            destination.FindPropertyRelative(
                    "targetTileType")
                .enumValueIndex =
                source.FindPropertyRelative(
                        "targetTileType")
                    .enumValueIndex;
        }

        private static void DrawResearchCostAndDuration(
            SerializedProperty selectedEntry)
        {
            EditorGUILayout.LabelField(
                "4. 비용과 시간",
                EditorStyles.boldLabel);

            SerializedProperty cost =
                selectedEntry.FindPropertyRelative(
                    "researchCost");
            SerializedProperty duration =
                selectedEntry.FindPropertyRelative(
                    "researchDurationHours");

            cost.intValue = Mathf.Max(
                0,
                EditorGUILayout.IntField(
                    new GUIContent(
                        "연구 비용",
                        "연구 시작 시 한 번 지불하는 재화입니다."),
                    cost.intValue));
            duration.intValue = Mathf.Max(
                0,
                EditorGUILayout.IntField(
                    new GUIContent(
                        "연구 시간",
                        "게임 안에서 흐르는 시간 기준입니다. 0이면 즉시 완료됩니다."),
                    duration.intValue));

            EditorGUILayout.HelpBox(
                duration.intValue == 0
                    ? "연구 시작 즉시 완료됩니다."
                    : $"게임 시간으로 {duration.intValue}시간 뒤 완료됩니다.",
                MessageType.Info);
        }

        private void ValidateResearchCatalog()
        {
            UnityEngine.Object catalog =
                AssetDatabase.LoadMainAssetAtPath(
                    WorkingResearchCatalogPath);
            if (catalog == null)
            {
                return;
            }

            SerializedProperty entries =
                new SerializedObject(catalog).FindProperty("entries");
            if (entries == null)
            {
                validationMessages.Add(
                    "연구 카탈로그에서 연구 목록을 찾을 수 없습니다.");
                return;
            }

            var researchIds =
                new HashSet<string>(StringComparer.Ordinal);
            var prerequisiteIds = new List<(string ResearchId, string PrerequisiteId)>();

            for (int index = 0; index < entries.arraySize; index++)
            {
                SerializedProperty entry =
                    entries.GetArrayElementAtIndex(index);
                string researchId = (
                    entry.FindPropertyRelative("researchId")
                        ?.stringValue ?? string.Empty).Trim();
                string prerequisiteId = (
                    entry.FindPropertyRelative("prerequisiteId")
                        ?.stringValue ?? string.Empty).Trim();

                if (researchId.Length == 0)
                {
                    validationMessages.Add(
                        $"연구 {index + 1}번의 연구 ID가 비어 있습니다.");
                }
                else if (!researchIds.Add(researchId))
                {
                    validationMessages.Add(
                        $"연구 ID가 중복되었습니다: {researchId}");
                }

                if (prerequisiteId.Length > 0)
                {
                    prerequisiteIds.Add((researchId, prerequisiteId));
                }

                SerializedProperty category =
                    entry.FindPropertyRelative("category");
                string worldGridStageId = (
                    entry.FindPropertyRelative("worldGridStageId")
                        ?.stringValue ?? string.Empty).Trim();
                if (category != null &&
                    category.enumValueIndex ==
                    (int)ResearchCategory.Expansion &&
                    worldGridStageId.Length == 0)
                {
                    validationMessages.Add(
                        $"개척 연구 {researchId}의 확장 단계 ID가 비어 있습니다.");
                }

                ValidateNonNegative(
                    entry,
                    "researchCost",
                    researchId,
                    "연구 비용");
                ValidateNonNegative(
                    entry,
                    "researchDurationHours",
                    researchId,
                    "연구 시간");
                ValidateNonNegative(
                    entry,
                    "threshold",
                    researchId,
                    "단일 조건 목표치");

                SerializedProperty requirements =
                    entry.FindPropertyRelative("requirements");
                if (requirements == null)
                {
                    continue;
                }

                for (int requirementIndex = 0;
                     requirementIndex < requirements.arraySize;
                     requirementIndex++)
                {
                    ValidateNonNegative(
                        requirements.GetArrayElementAtIndex(
                            requirementIndex),
                        "threshold",
                        researchId,
                        $"조건 {requirementIndex + 1} 목표치");
                }
            }

            for (int index = 0;
                 index < prerequisiteIds.Count;
                 index++)
            {
                (string researchId, string prerequisiteId) =
                    prerequisiteIds[index];
                if (!researchIds.Contains(prerequisiteId))
                {
                    validationMessages.Add(
                        $"연구 {researchId}의 선행 연구 ID가 존재하지 않습니다: " +
                        prerequisiteId);
                }
            }
        }

        private void ValidateNonNegative(
            SerializedProperty owner,
            string propertyName,
            string researchId,
            string label)
        {
            SerializedProperty value =
                owner?.FindPropertyRelative(propertyName);
            if (value != null && value.intValue < 0)
            {
                validationMessages.Add(
                    $"연구 {researchId}: {label}는 0 이상이어야 합니다.");
            }
        }

        private void ValidateLoadedSceneReferences()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != BalanceScenePath)
            {
                validationMessages.Add(
                    "밸런스 전용 Scene이 열려 있지 않아 Scene 연결 상태는 검사하지 못했습니다.");
                return;
            }

            HashSet<UnityEngine.Object> productionAssets = Entries
                .Select(entry =>
                    AssetDatabase.LoadMainAssetAtPath(entry.SourcePath))
                .Where(asset => asset != null)
                .ToHashSet();
            UnityEngine.Object workingResearchCatalog =
                AssetDatabase.LoadMainAssetAtPath(
                    WorkingResearchCatalogPath);
            int productionReferenceCount = 0;
            int invalidResearchCatalogCount = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Component component in
                         root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null)
                    {
                        continue;
                    }

                    SerializedProperty property =
                        new SerializedObject(component).GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType ==
                                SerializedPropertyType.ObjectReference &&
                            property.objectReferenceValue != null &&
                            productionAssets.Contains(
                                property.objectReferenceValue))
                        {
                            productionReferenceCount++;
                        }
                    }

                    if (UsesResearchCatalog(component))
                    {
                        SerializedProperty catalogProperty =
                            new SerializedObject(component)
                                .FindProperty("catalog");
                        if (catalogProperty == null ||
                            catalogProperty.objectReferenceValue !=
                            workingResearchCatalog)
                        {
                            invalidResearchCatalogCount++;
                        }
                    }
                }
            }

            if (productionReferenceCount > 0)
            {
                validationMessages.Add(
                    $"Scene에 실제 설정 참조가 {productionReferenceCount}개 남아 있습니다. " +
                    "'작업 공간 생성 / 열기'를 다시 눌러 작업용 설정으로 연결하세요.");
            }

            if (invalidResearchCatalogCount > 0)
            {
                validationMessages.Add(
                    $"연구 서비스 또는 UI {invalidResearchCatalogCount}개가 " +
                    "작업용 연구 카탈로그에 연결되지 않았습니다. " +
                    "'작업 공간 생성 / 열기'를 다시 눌러 주세요.");
            }
        }

        private void PublishWorkingValues()
        {
            RunValidation();
            if (validationMessages.Any(
                    message => message.Contains("누락") ||
                               message.Contains("Build Settings")))
            {
                EditorUtility.DisplayDialog(
                    "반영 중단",
                    "필수 에셋 또는 Scene 상태를 먼저 해결해 주세요.",
                    "확인");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "밸런스 수치 확정",
                "작업용 수치를 실제 게임 설정 에셋에 반영합니다.\n" +
                "통합 Scene은 수정하지 않지만 여러 공용 설정 에셋이 변경됩니다.\n\n" +
                "계속할까요?",
                "반영",
                "취소");

            if (!confirmed)
            {
                return;
            }

            foreach (BalanceEntry entry in Entries)
            {
                UnityEngine.Object source =
                    AssetDatabase.LoadMainAssetAtPath(entry.SourcePath);
                UnityEngine.Object working =
                    AssetDatabase.LoadMainAssetAtPath(entry.WorkingPath);

                if (source == null || working == null)
                {
                    continue;
                }

                string originalName = source.name;
                Undo.RecordObject(source, "밸런스 수치 확정");
                EditorUtility.CopySerialized(working, source);
                source.name = originalName;
                EditorUtility.SetDirty(source);
            }

            AssetDatabase.SaveAssets();
            ShowNotification(new GUIContent("확정 수치를 실제 에셋에 반영했습니다."));
            Debug.Log(
                "[Balance] 작업용 밸런스 수치를 실제 설정 에셋에 반영했습니다. " +
                "통합 Scene은 변경하지 않았습니다.");
        }

        private void DestroyCachedEditor()
        {
            if (cachedAssetEditor != null)
            {
                DestroyImmediate(cachedAssetEditor);
            }

            cachedAssetEditor = null;
            cachedTarget = null;
        }
    }
}
#endif

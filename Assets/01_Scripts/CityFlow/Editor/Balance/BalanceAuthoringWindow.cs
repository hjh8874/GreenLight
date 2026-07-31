#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityFlow.EditorTools.Balance
{
    public sealed class BalanceAuthoringWindow : EditorWindow
    {
        internal const string SourceScenePath =
            "Assets/00_Scenes/CityFlowIntegrated_cmt.unity";
        internal const string BalanceScenePath =
            "Assets/00_Scenes/Debug/CityFlowBalance_Lee.unity";
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
        private UnityEditor.Editor cachedAssetEditor;
        private UnityEngine.Object cachedTarget;

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

                GUI.backgroundColor = new Color(1f, 0.75f, 0.35f);
                if (GUILayout.Button("확정값 실제 에셋에 반영", GUILayout.Height(28f)))
                {
                    PublishWorkingValues();
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
                    if (GUILayout.Toggle(
                            i == selectedEntryIndex,
                            groupEntries[i].Label,
                            EditorStyles.miniButtonLeft) &&
                        selectedEntryIndex != i)
                    {
                        selectedEntryIndex = i;
                        DestroyCachedEditor();
                    }
                }
            }
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
            UnityEngine.Object target =
                AssetDatabase.LoadMainAssetAtPath(entry.WorkingPath);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(entry.Label, EditorStyles.boldLabel);
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

                if (target == null)
                {
                    EditorGUILayout.HelpBox(
                        "작업용 에셋이 없습니다. '작업 공간 생성 / 열기'를 눌러 주세요.",
                        MessageType.Warning);
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
                    SerializedProperty property = serialized.GetIterator();
                    bool enterChildren = true;
                    bool recorded = false;

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
            int productionReferenceCount = 0;

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
                }
            }

            if (productionReferenceCount > 0)
            {
                validationMessages.Add(
                    $"Scene에 실제 설정 참조가 {productionReferenceCount}개 남아 있습니다. " +
                    "'작업 공간 생성 / 열기'를 다시 눌러 작업용 설정으로 연결하세요.");
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

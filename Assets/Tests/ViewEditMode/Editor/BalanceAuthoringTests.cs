using System.IO;
using System.Linq;
using CityFlow.Configs;
using CityFlow.Content;
using CityFlow.EditorTools.Balance;
using CityFlow.Gameplay.Research;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public sealed class BalanceAuthoringTests
    {
        private static readonly string[] WorkingAssetPaths =
        {
            "Assets/05_ScriptableObjects/Balance/Editor/SimConfig_Integrated_Balance.asset",
            "Assets/05_ScriptableObjects/Balance/Editor/EconomyConfig_Balance.asset",
            "Assets/05_ScriptableObjects/Balance/Editor/DistanceRewardConfig_Balance.asset",
            "Assets/05_ScriptableObjects/Balance/Editor/PopulationConfig_Balance.asset",
            "Assets/05_ScriptableObjects/Balance/Editor/GameTimeSettings_Balance.asset",
            "Assets/05_ScriptableObjects/Balance/Editor/ResearchCatalog_Balance.asset",
            "Assets/05_ScriptableObjects/Balance/Editor/CityBusDefinition_Balance.asset",
            "Assets/05_ScriptableObjects/Balance/Editor/SchoolBusDefinition_Balance.asset",
            "Assets/05_ScriptableObjects/Balance/Editor/DefaultCityBusSchedule_Balance.asset",
            "Assets/05_ScriptableObjects/Balance/Editor/KoreanSchoolBusSchedule_Balance.asset",
            "Assets/05_ScriptableObjects/Balance/Editor/EmergencyIncidentConfig_Balance.asset",
            "Assets/05_ScriptableObjects/Balance/Editor/SignalData_Balance.asset",
            "Assets/05_ScriptableObjects/Balance/Editor/RoundaboutData_Balance.asset",
            "Assets/05_ScriptableObjects/Balance/Editor/OverpassData_Balance.asset",
            "Assets/05_ScriptableObjects/Balance/Editor/OnewayData_Balance.asset",
            "Assets/05_ScriptableObjects/Balance/Editor/TurnRestrictionData_Balance.asset",
            "Assets/05_ScriptableObjects/Balance/Editor/PriorityRoadData_Balance.asset",
            "Assets/05_ScriptableObjects/Balance/Editor/HighwayData_Balance.asset"
        };

        private static readonly string[] ProductionGuidsReplacedInScene =
        {
            "a62fee5e6fde4d068ddb93c6b6f3d461",
            "b7a1a4dd78b44dc791a1536a75539231",
            "d399080961f84caaa2b340ca6d6a06a4",
            "dadd848057f846c49cd3469c9366b748",
            "37c77835f95061a4ea48ada1b751c57c",
            "0368696defd8f5c48831cd394b5d8643",
            "6326890673d7b4fefa366098947b09e1",
            "35a25790f99b3ba4bace62965a0591e4",
            "d9d64cdcc77b71b46a1a929badff74a2",
            "82f3ef980d6db1a4e82e91fbd8e2eaa6",
            "1eec600dab66a40738a25dc2f3ce7ec1"
        };

        [Test]
        public void BalanceScene_IsASeparateEditorOnlyClone()
        {
            SceneAsset source = AssetDatabase.LoadAssetAtPath<SceneAsset>(
                BalanceAuthoringWindow.SourceScenePath);
            SceneAsset balance = AssetDatabase.LoadAssetAtPath<SceneAsset>(
                BalanceAuthoringWindow.BalanceScenePath);

            Assert.That(source, Is.Not.Null);
            Assert.That(balance, Is.Not.Null);
            Assert.That(
                AssetDatabase.AssetPathToGUID(BalanceAuthoringWindow.SourceScenePath),
                Is.Not.EqualTo(
                    AssetDatabase.AssetPathToGUID(
                        BalanceAuthoringWindow.BalanceScenePath)));
            Assert.That(
                EditorBuildSettings.scenes.Any(
                    scene => scene.path == BalanceAuthoringWindow.BalanceScenePath),
                Is.False,
                "밸런스 전용 Scene은 Build Settings에 포함되면 안 됩니다.");
        }

        [TestCase(
            "Assets/00_Scenes/Debug/CityFlowBalance_Lee.unity",
            true)]
        [TestCase(
            "Assets/00_Scenes/Debug/CityFlowBalance_Lee1.unity",
            true)]
        [TestCase(
            "Assets/00_Scenes/CityFlowIntegrated_cmt.unity",
            false)]
        public void BalanceEditor_OnlyRewiresSupportedDebugScenes(
            string scenePath,
            bool expected)
        {
            Assert.That(
                BalanceAuthoringWindow.IsSupportedBalanceScenePath(scenePath),
                Is.EqualTo(expected));
        }

        [Test]
        public void WorkingBalanceAssets_ArePresentUnderEditorFolder()
        {
            foreach (string path in WorkingAssetPaths)
            {
                Assert.That(
                    AssetDatabase.LoadMainAssetAtPath(path),
                    Is.Not.Null,
                    $"작업용 밸런스 에셋 누락: {path}");
            }
        }

        [Test]
        public void BalanceEditor_LocalizesEveryVisibleSettingField()
        {
            foreach (string path in WorkingAssetPaths)
            {
                if (path == BalanceAuthoringWindow.WorkingResearchCatalogPath)
                {
                    // 연구 카탈로그는 전용 한국어 편집 화면을 사용한다.
                    continue;
                }

                Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                Assert.That(asset, Is.Not.Null, path);
                var serialized = new SerializedObject(asset);
                SerializedProperty property = serialized.GetIterator();
                while (property.NextVisible(true))
                {
                    string name = property.name;
                    if (property.propertyPath == "m_Script" ||
                        property.propertyPath.Contains(".Array.data[") ||
                        name == "Array" ||
                        name == "size" ||
                        name.StartsWith("data[", System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Assert.That(
                        BalanceAuthoringWindow.HasLocalizedPropertyLabel(name),
                        Is.True,
                        $"밸런스 편집기 한글 이름 누락: {path} > " +
                        $"{property.propertyPath} ({property.displayName})");
                }
            }
        }

        [TestCase("교통", "일반 차량", "Value.CarsPerHouse")]
        [TestCase("교통", "일반 차량", "Value.MaxSimCars")]
        [TestCase("교통", "일반 차량", "Value.MorningStartHour")]
        [TestCase("교통", "일반 차량", "Value.RushAmplitude")]
        [TestCase("건물", "주거 지역", "Value.CarsPerHouse")]
        [TestCase("건물", "주거 지역", "Value.ConstructionHoursHouse")]
        [TestCase("건물", "회사", "Value.OfficeCapacity")]
        [TestCase("건물", "회사", "Value.CompanyHiringSlotsPerGameHour")]
        [TestCase("건물", "회사", "Value.ConstructionHoursOffice")]
        public void BalanceEditor_ProvidesFocusedRuntimeBalanceViews(
            string group,
            string label,
            string propertyPath)
        {
            Assert.That(
                BalanceAuthoringWindow.GetVisiblePropertyPaths(group, label),
                Does.Contain(propertyPath),
                $"{group} > {label}에 실제 런타임 설정 {propertyPath}가 보여야 합니다.");
        }

        [Test]
        public void BalanceEditor_LocalizesEveryVisibleEnumOption()
        {
            foreach (string path in WorkingAssetPaths)
            {
                if (path == BalanceAuthoringWindow.WorkingResearchCatalogPath)
                {
                    continue;
                }

                Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                Assert.That(asset, Is.Not.Null, path);
                var serialized = new SerializedObject(asset);
                SerializedProperty property = serialized.GetIterator();
                while (property.NextVisible(true))
                {
                    if (property.propertyType != SerializedPropertyType.Enum)
                    {
                        continue;
                    }

                    foreach (string enumName in property.enumNames)
                    {
                        string label =
                            BalanceAuthoringWindow.GetLocalizedEnumLabel(enumName);
                        Assert.That(
                            label.Any(character =>
                                character >= '가' && character <= '힣'),
                            Is.True,
                            $"밸런스 편집기 열거형 한글 이름 누락: {path} > " +
                            $"{property.propertyPath}.{enumName} ({label})");
                    }
                }
            }
        }

        [TestCase("CityBus", "시내버스")]
        [TestCase("SchoolBus", "스쿨버스")]
        [TestCase("Roundabout", "회전교차로")]
        [TestCase("Horizontal", "가로축")]
        public void BalanceEditor_LocalizesEnumOptions(
            string enumName,
            string expected)
        {
            Assert.That(
                BalanceAuthoringWindow.GetLocalizedEnumLabel(enumName),
                Is.EqualTo(expected));
        }

        [Test]
        public void ResearchCatalog_HasSeparateWorkingCopy()
        {
            Object source = AssetDatabase.LoadMainAssetAtPath(
                BalanceAuthoringWindow.ResearchCatalogPath);
            Object working = AssetDatabase.LoadMainAssetAtPath(
                BalanceAuthoringWindow.WorkingResearchCatalogPath);

            Assert.That(source, Is.Not.Null);
            Assert.That(working, Is.Not.Null);
            Assert.That(working.GetType(), Is.EqualTo(source.GetType()));
            Assert.That(
                AssetDatabase.AssetPathToGUID(
                    BalanceAuthoringWindow.WorkingResearchCatalogPath),
                Is.Not.EqualTo(
                    AssetDatabase.AssetPathToGUID(
                    BalanceAuthoringWindow.ResearchCatalogPath)));
        }

        [TestCase(
            "research_building_video_store",
            "Building_StoreCorner_Video_Balance.asset")]
        [TestCase(
            "research_building_mall",
            "Building_Mall_Balance.asset")]
        [TestCase(
            "research_building_school",
            "SchoolData_Balance.asset")]
        [TestCase(
            "research_building_hospital",
            "HospitalTileData_Balance.asset")]
        public void ResearchBalance_ExposesLinkedConstructionSettings(
            string researchId,
            string expectedFileName)
        {
            string path = BalanceAuthoringWindow
                .GetLinkedBuildingWorkingPaths(researchId)
                .Single();

            Assert.That(
                path,
                Does.EndWith(expectedFileName));
            Assert.That(
                AssetDatabase.LoadMainAssetAtPath(path),
                Is.Not.Null,
                $"연구 {researchId}의 실제 건물 작업용 에셋이 필요합니다.");
        }

        [Test]
        public void BuildingNameEditor_ExposesEveryConstructionName()
        {
            var paths = BalanceAuthoringWindow
                .GetBuildingNameWorkingPaths();

            Assert.That(paths.Count, Is.EqualTo(12));
            Assert.That(
                paths,
                Does.Contain(
                    "Assets/05_ScriptableObjects/Balance/Editor/HouseData_Balance.asset"));
            Assert.That(
                paths,
                Does.Contain(
                    "Assets/05_ScriptableObjects/Balance/Editor/Building_Mall_Balance.asset"));

            foreach (string path in paths)
            {
                Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                Assert.That(asset, Is.Not.Null, path);

                var serialized = new SerializedObject(asset);
                Assert.That(
                    serialized.FindProperty("buildingName"),
                    Is.Not.Null,
                    $"건물 이름 설정 누락: {path}");
                Assert.That(
                    serialized.FindProperty("buildCost"),
                    Is.Not.Null,
                    $"건설 비용 설정 누락: {path}");

                if (asset is TileDataSO)
                {
                    Assert.That(
                        serialized.FindProperty("dailyCoinValue"),
                        Is.Not.Null,
                        $"툴팁 수입 설정 누락: {path}");
                    Assert.That(
                        serialized.FindProperty("prosperityValue"),
                        Is.Not.Null,
                        $"툴팁 안정도 설정 누락: {path}");
                    Assert.That(
                        serialized.FindProperty("buildingDescription"),
                        Is.Not.Null,
                        $"툴팁 설명 설정 누락: {path}");
                }
                else
                {
                    Assert.That(
                        serialized.FindProperty(
                            "visitCadence.visitsPerPeriod"),
                        Is.Not.Null,
                        $"툴팁 방문 횟수 설정 누락: {path}");
                    Assert.That(
                        serialized.FindProperty(
                            "visitCadence.periodDays"),
                        Is.Not.Null,
                        $"툴팁 방문 기간 설정 누락: {path}");
                    Assert.That(
                        serialized.FindProperty("description"),
                        Is.Not.Null,
                        $"툴팁 설명 설정 누락: {path}");
                }
            }
        }

        [Test]
        public void WorkingBuildingCatalog_UsesOnlyWorkingBuildingDefinitions()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(
                    BalanceAuthoringWindow.WorkingBuildingCatalogPath);

            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Buildings, Is.Not.Empty);
            foreach (BuildingDefinitionSO building in catalog.Buildings)
            {
                Assert.That(building, Is.Not.Null);
                Assert.That(
                    AssetDatabase.GetAssetPath(building),
                    Does.StartWith(
                        "Assets/05_ScriptableObjects/Balance/Editor/"),
                    $"작업용 건물 카탈로그에 원본 건물이 연결됨: " +
                    building.name);
            }
        }

        [Test]
        public void ProductionBuildingCatalog_DoesNotReferenceWorkingAssets()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(
                    "Assets/05_ScriptableObjects/Buildings/SpecialBuildingCatalog.asset");

            Assert.That(catalog, Is.Not.Null);
            foreach (BuildingDefinitionSO building in catalog.Buildings)
            {
                Assert.That(
                    AssetDatabase.GetAssetPath(building),
                    Does.Not.StartWith(
                        "Assets/05_ScriptableObjects/Balance/Editor/"));
            }
        }

        [TestCase(
            ResearchCategory.Commercial,
            0,
            true)]
        [TestCase(
            ResearchCategory.Infrastructure,
            0,
            true)]
        [TestCase(
            ResearchCategory.PublicService,
            0,
            true)]
        [TestCase(
            ResearchCategory.Expansion,
            0,
            false)]
        [TestCase(
            ResearchCategory.Expansion,
            1,
            true)]
        [TestCase(
            ResearchCategory.PublicService,
            1,
            false)]
        public void ResearchBalanceSections_SeparateExpansionFromBuildingUnlocks(
            ResearchCategory category,
            int sectionValue,
            bool expected)
        {
            var section =
                (BalanceAuthoringWindow.ResearchBalanceSection)sectionValue;
            Assert.That(
                BalanceAuthoringWindow.IsResearchInSection(
                    category,
                    section),
                Is.EqualTo(expected));
        }

        [Test]
        public void LockedBuildings_HavePaidTimedResearchEntries()
        {
            ResearchCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<ResearchCatalogSO>(
                    BalanceAuthoringWindow.ResearchCatalogPath);
            Assert.That(catalog, Is.Not.Null);

            var entriesById = catalog.ValidEntries()
                .ToDictionary(
                    entry => entry.researchId,
                    entry => entry);
            int linkedBuildingCount = 0;

            foreach (string guid in
                     AssetDatabase.FindAssets(
                         "t:BuildingDefinitionSO"))
            {
                BuildingDefinitionSO building =
                    AssetDatabase.LoadAssetAtPath<
                        BuildingDefinitionSO>(
                        AssetDatabase.GUIDToAssetPath(guid));
                AssertPaidTimedResearch(
                    entriesById,
                    building?.RequiredResearchId,
                    building?.buildingName,
                    ref linkedBuildingCount);
            }

            foreach (string guid in
                     AssetDatabase.FindAssets("t:TileDataSO"))
            {
                TileDataSO tile =
                    AssetDatabase.LoadAssetAtPath<TileDataSO>(
                        AssetDatabase.GUIDToAssetPath(guid));
                AssertPaidTimedResearch(
                    entriesById,
                    tile?.RequiredResearchId,
                    tile?.BuildingName,
                    ref linkedBuildingCount);
            }

            Assert.That(
                linkedBuildingCount,
                Is.GreaterThanOrEqualTo(10),
                "학교·병원과 특수 건물 8개가 연구에 연결되어야 합니다.");
        }

        private static void AssertPaidTimedResearch(
            System.Collections.Generic.IReadOnlyDictionary<
                string,
                ResearchEntry> entriesById,
            string researchId,
            string buildingName,
            ref int linkedBuildingCount)
        {
            string normalizedId =
                researchId?.Trim() ?? string.Empty;
            if (normalizedId.Length == 0)
            {
                return;
            }

            linkedBuildingCount++;
            Assert.That(
                entriesById.TryGetValue(
                    normalizedId,
                    out ResearchEntry entry),
                Is.True,
                $"{buildingName}의 연구 ID가 카탈로그에 없습니다: " +
                normalizedId);
            Assert.That(
                entry.researchCost,
                Is.GreaterThan(0),
                $"{buildingName} 연구 비용은 0보다 커야 합니다.");
            Assert.That(
                entry.researchDurationHours,
                Is.GreaterThan(0),
                $"{buildingName} 연구 시간은 0보다 커야 합니다.");
        }

        [Test]
        public void BalanceScene_DoesNotDirectlyReferenceProductionBalanceAssets()
        {
            string sceneText = File.ReadAllText(
                BalanceAuthoringWindow.BalanceScenePath);

            foreach (string guid in ProductionGuidsReplacedInScene)
            {
                Assert.That(
                    sceneText,
                    Does.Not.Contain(guid),
                    $"밸런스 Scene에 실제 설정 GUID가 남아 있습니다: {guid}");
            }
        }
    }
}

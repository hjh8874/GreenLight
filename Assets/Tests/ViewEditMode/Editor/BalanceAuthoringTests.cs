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

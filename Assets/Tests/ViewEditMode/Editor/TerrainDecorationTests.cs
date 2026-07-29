using System.Reflection;
using CityFlow.Configs;
using CityFlow.Contracts.Save;
using CityFlow.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CityFlow.Tests
{
    public sealed class TerrainDecorationTests
    {
        private const string CatalogPath =
            "Assets/02_Prefabs/Environment/Decorations/TerrainDecorationCatalog.asset";
        private const string SystemPrefabPath =
            "Assets/02_Prefabs/Environment/TerrainDecorationSystem.prefab";

        [Test]
        public void Catalog_SameSeedAndCoordinate_ProducesSameSample()
        {
            TerrainDecorationCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<TerrainDecorationCatalogSO>(
                    CatalogPath);
            Assert.NotNull(catalog);

            bool foundSpawnedTile = false;
            for (int y = 0; y < 20 && !foundSpawnedTile; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    Vector2Int tile = new Vector2Int(x, y);
                    if (!catalog.TryCreateSample(tile, 1f, out TerrainDecorationSample first))
                    {
                        continue;
                    }

                    Assert.IsTrue(
                        catalog.TryCreateSample(
                            tile,
                            1f,
                            out TerrainDecorationSample second));
                    Assert.AreSame(first.Prefab, second.Prefab);
                    Assert.AreEqual(first.Offset, second.Offset);
                    Assert.AreEqual(first.RotationDegrees, second.RotationDegrees);
                    Assert.AreEqual(first.Scale, second.Scale);
                    foundSpawnedTile = true;
                    break;
                }
            }

            Assert.IsTrue(foundSpawnedTile);
        }

        [Test]
        public void Placement_ClearsEveryFootprintTile()
        {
            var state = new TerrainDecorationState(5, 5);

            state.ApplyPlacement(
                new Vector2Int(1, 2),
                new Vector2Int(3, 2),
                isRemove: false);

            for (int y = 2; y < 4; y++)
            {
                for (int x = 1; x < 4; x++)
                {
                    Assert.IsTrue(state.IsCleared(new Vector2Int(x, y)));
                }
            }

            CollectionAssert.AreEqual(
                new[] { 11, 12, 13, 16, 17, 18 },
                state.CreateSnapshot().ClearedTileIndices);
        }

        [Test]
        public void Removal_DoesNotRestoreClearedDecoration()
        {
            var state = new TerrainDecorationState(3, 3);
            Vector2Int tile = new Vector2Int(1, 1);

            state.ApplyPlacement(tile, Vector2Int.one, isRemove: false);
            state.ApplyPlacement(tile, Vector2Int.one, isRemove: true);

            Assert.IsTrue(state.IsCleared(tile));
        }

        [Test]
        public void CenteredOrigin_KeepsSaveIndicesLocalToInitialBoard()
        {
            var state = new TerrainDecorationState(
                20,
                20,
                new Vector2Int(90, 90));

            state.ApplyPlacement(
                new Vector2Int(90, 90),
                Vector2Int.one,
                isRemove: false);

            Assert.IsTrue(state.IsCleared(new Vector2Int(90, 90)));
            Assert.IsFalse(state.IsCleared(new Vector2Int(0, 0)));
            CollectionAssert.AreEqual(
                new[] { 0 },
                state.CreateSnapshot().ClearedTileIndices);
        }

        [Test]
        public void WorldState_PersistsClearedExpandedTile()
        {
            var source = new TerrainDecorationState(200, 200);
            Vector2Int expandedTile = new Vector2Int(75, 85);
            source.ApplyPlacement(
                expandedTile,
                Vector2Int.one,
                isRemove: false);

            TerrainDecorationSaveData snapshot = source.CreateSnapshot();
            var restored = new TerrainDecorationState(200, 200);
            restored.RestoreSnapshot(snapshot);

            Assert.AreEqual(200, snapshot.GridWidth);
            Assert.AreEqual(200, snapshot.GridHeight);
            Assert.IsTrue(restored.IsCleared(expandedTile));
        }

        [Test]
        public void LegacyInitialBoardSnapshot_MigratesToWorldCenter()
        {
            var state = new TerrainDecorationState(200, 200);
            var legacySnapshot = new TerrainDecorationSaveData
            {
                ClearedTileIndices = new[] { 0, 399 }
            };

            state.RestoreSnapshot(
                legacySnapshot,
                legacyWidth: 20,
                legacyHeight: 20,
                legacyOrigin: new Vector2Int(90, 90));

            Assert.IsTrue(state.IsCleared(new Vector2Int(90, 90)));
            Assert.IsTrue(state.IsCleared(new Vector2Int(109, 109)));
            Assert.IsFalse(state.IsCleared(Vector2Int.zero));
        }

        [Test]
        public void RestoreSnapshot_IgnoresIndicesOutsideGrid()
        {
            var state = new TerrainDecorationState(2, 2);

            state.RestoreSnapshot(new TerrainDecorationSaveData
            {
                ClearedTileIndices = new[] { -1, 0, 3, 4, 99 }
            });

            CollectionAssert.AreEqual(
                new[] { 0, 3 },
                state.CreateSnapshot().ClearedTileIndices);
        }

        [Test]
        public void SystemPrefab_InstallsPrototypeFieldTilesWithOneUnit()
        {
            GameObject cityObject = null;
            GameObject systemInstance = null;

            try
            {
                cityObject = new GameObject("TestCity");
                MainCityView cityView = cityObject.AddComponent<MainCityView>();
                SetPrivateField(cityView, "width", 2);
                SetPrivateField(cityView, "height", 2);

                GameObject systemPrefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(SystemPrefabPath);
                Assert.NotNull(systemPrefab);

                systemInstance = Object.Instantiate(systemPrefab);
                TerrainDecorationView terrainSystem =
                    systemInstance.GetComponent<TerrainDecorationView>();
                Assert.NotNull(terrainSystem);
                Assert.NotNull(terrainSystem.Catalog);
                Assert.IsTrue(terrainSystem.TryInstall(cityView));
                Assert.NotNull(cityView.FieldTilePrefab);

                InvokePrivate(cityView, "BuildRoots");
                InvokePrivate(cityView, "BuildBoard");

                Assert.IsTrue(
                    cityView.TryGetGridCell(
                        Vector2Int.zero,
                        out GridCellView firstCell));
                Assert.NotNull(firstCell.Ground);
                Assert.NotNull(firstCell.Ground.GetComponent<Renderer>());
                Assert.IsTrue(
                    cityView.TryGetGridCell(
                        new Vector2Int(1, 1),
                        out GridCellView lastCell));
                Assert.NotNull(lastCell.Ground);
            }
            finally
            {
                if (systemInstance != null)
                {
                    Object.DestroyImmediate(systemInstance);
                }

                if (cityObject != null)
                {
                    Object.DestroyImmediate(cityObject);
                }
            }
        }

        private static void SetPrivateField<T>(
            object target,
            string fieldName,
            T value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, fieldName);
            field.SetValue(target, value);
        }

        private static void InvokePrivate(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method, methodName);
            method.Invoke(target, null);
        }
    }
}

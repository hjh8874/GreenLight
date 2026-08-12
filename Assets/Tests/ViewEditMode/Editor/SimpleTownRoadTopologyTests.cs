using System.Reflection;
using CityFlow.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CityFlow.Tests.ViewEditMode
{
    public sealed class SimpleTownRoadTopologyTests
    {
        [TestCase(
            SimpleTownRoadConnections.None,
            SimpleTownRoadShape.Isolated,
            0f)]
        [TestCase(
            SimpleTownRoadConnections.North,
            SimpleTownRoadShape.End,
            0f)]
        [TestCase(
            SimpleTownRoadConnections.East,
            SimpleTownRoadShape.End,
            -90f)]
        [TestCase(
            SimpleTownRoadConnections.South,
            SimpleTownRoadShape.End,
            180f)]
        [TestCase(
            SimpleTownRoadConnections.West,
            SimpleTownRoadShape.End,
            90f)]
        [TestCase(
            SimpleTownRoadConnections.North |
            SimpleTownRoadConnections.South,
            SimpleTownRoadShape.Straight,
            0f)]
        [TestCase(
            SimpleTownRoadConnections.East |
            SimpleTownRoadConnections.West,
            SimpleTownRoadShape.Straight,
            90f)]
        [TestCase(
            SimpleTownRoadConnections.North |
            SimpleTownRoadConnections.East,
            SimpleTownRoadShape.Corner,
            0f)]
        [TestCase(
            SimpleTownRoadConnections.East |
            SimpleTownRoadConnections.South,
            SimpleTownRoadShape.Corner,
            -90f)]
        [TestCase(
            SimpleTownRoadConnections.South |
            SimpleTownRoadConnections.West,
            SimpleTownRoadShape.Corner,
            180f)]
        [TestCase(
            SimpleTownRoadConnections.West |
            SimpleTownRoadConnections.North,
            SimpleTownRoadShape.Corner,
            90f)]
        [TestCase(
            SimpleTownRoadConnections.North |
            SimpleTownRoadConnections.East |
            SimpleTownRoadConnections.West,
            SimpleTownRoadShape.TIntersection,
            0f)]
        [TestCase(
            SimpleTownRoadConnections.North |
            SimpleTownRoadConnections.East |
            SimpleTownRoadConnections.South,
            SimpleTownRoadShape.TIntersection,
            -90f)]
        [TestCase(
            SimpleTownRoadConnections.East |
            SimpleTownRoadConnections.South |
            SimpleTownRoadConnections.West,
            SimpleTownRoadShape.TIntersection,
            180f)]
        [TestCase(
            SimpleTownRoadConnections.South |
            SimpleTownRoadConnections.West |
            SimpleTownRoadConnections.North,
            SimpleTownRoadShape.TIntersection,
            90f)]
        [TestCase(
            SimpleTownRoadConnections.North |
            SimpleTownRoadConnections.East |
            SimpleTownRoadConnections.South |
            SimpleTownRoadConnections.West,
            SimpleTownRoadShape.CrossIntersection,
            0f)]
        public void Resolve_SelectsExpectedShapeAndRotation(
            SimpleTownRoadConnections connections,
            SimpleTownRoadShape expectedShape,
            float expectedRotation)
        {
            SimpleTownRoadSelection selection =
                SimpleTownRoadTopology.Resolve(connections);

            Assert.AreEqual(expectedShape, selection.Shape);
            Assert.AreEqual(expectedRotation, selection.RotationDegrees);
        }

        [TestCase(
            SimpleTownRoadConnections.None,
            SimpleTownRoadTopology.All)]
        [TestCase(
            SimpleTownRoadConnections.North,
            SimpleTownRoadConnections.East |
            SimpleTownRoadConnections.South |
            SimpleTownRoadConnections.West)]
        [TestCase(
            SimpleTownRoadConnections.North |
            SimpleTownRoadConnections.South,
            SimpleTownRoadConnections.East |
            SimpleTownRoadConnections.West)]
        [TestCase(
            SimpleTownRoadConnections.North |
            SimpleTownRoadConnections.East,
            SimpleTownRoadConnections.South |
            SimpleTownRoadConnections.West)]
        [TestCase(
            SimpleTownRoadConnections.North |
            SimpleTownRoadConnections.East |
            SimpleTownRoadConnections.West,
            SimpleTownRoadConnections.South)]
        [TestCase(
            SimpleTownRoadTopology.All,
            SimpleTownRoadConnections.None)]
        public void GetPerimeterSides_ReturnsOnlyUnconnectedSides(
            SimpleTownRoadConnections connections,
            SimpleTownRoadConnections expectedPerimeter)
        {
            Assert.AreEqual(
                expectedPerimeter,
                SimpleTownRoadTopology.GetPerimeterSides(connections));
        }

        [Test]
        public void VisualSet_UsesCommonSurfaceForEveryRoadShape()
        {
            SimpleTownRoadVisualSetSO visualSet = LoadVisualSet();

            Assert.IsNotNull(visualSet);
            Assert.IsNotNull(visualSet.RoadSurfacePrefab);
            Assert.AreEqual(
                "SimpleTownRoadSurface",
                visualSet.RoadSurfacePrefab.name);

            SimpleTownRoadShape[] commonSurfaceShapes =
            {
                SimpleTownRoadShape.Isolated,
                SimpleTownRoadShape.End,
                SimpleTownRoadShape.Straight,
                SimpleTownRoadShape.Corner,
                SimpleTownRoadShape.TIntersection,
                SimpleTownRoadShape.CrossIntersection
            };
            foreach (SimpleTownRoadShape shape in commonSurfaceShapes)
            {
                Assert.AreSame(
                    visualSet.RoadSurfacePrefab,
                    visualSet.GetRoadPrefab(shape),
                    shape.ToString());
            }
        }

        [Test]
        public void VisualSet_UsesProjectOwnedPrefabCopies()
        {
            SimpleTownRoadVisualSetSO visualSet = LoadVisualSet();
            Assert.IsNotNull(visualSet.RoadSurfacePrefab);
            StringAssert.StartsWith(
                "Assets/02_Prefabs/Environment/Roads/",
                AssetDatabase.GetAssetPath(
                    visualSet.RoadSurfacePrefab));

            Assert.IsNotNull(visualSet.DrivewayPrefab);
            StringAssert.StartsWith(
                "Assets/02_Prefabs/Environment/Driveways/",
                AssetDatabase.GetAssetPath(visualSet.DrivewayPrefab));
        }

        [Test]
        public void CommonSurface_UsesNeutralDemoRoadSquareMesh()
        {
            SimpleTownRoadVisualSetSO visualSet = LoadVisualSet();
            MeshFilter[] meshFilters =
                visualSet.RoadSurfacePrefab
                    .GetComponentsInChildren<MeshFilter>();

            Assert.AreEqual(1, meshFilters.Length);
            Assert.AreEqual(
                "Assets/99_Download/SimpleTown/Models/" +
                "road_square_mesh.fbx",
                AssetDatabase.GetAssetPath(
                    meshFilters[0].sharedMesh));
        }

        [Test]
        public void RoadPrefabCopies_UseProjectOwnedLitMaterial()
        {
            const string roadMaterialPath =
                "Assets/02_Prefabs/Environment/Roads/Materials/" +
                "SimpleTownRoad_URP_Unlit.mat";
            SimpleTownRoadVisualSetSO visualSet = LoadVisualSet();

            AssertPrefabUsesLitMaterial(
                visualSet.RoadSurfacePrefab,
                roadMaterialPath,
                "CommonSurface");
            AssertPrefabUsesLitMaterial(
                visualSet.DrivewayPrefab,
                roadMaterialPath,
                "Driveway");
        }

        [TestCase(
            SimpleTownRoadConnections.North,
            true)]
        [TestCase(
            SimpleTownRoadConnections.North |
            SimpleTownRoadConnections.South,
            true)]
        [TestCase(
            SimpleTownRoadConnections.North |
            SimpleTownRoadConnections.East,
            false)]
        [TestCase(
            SimpleTownRoadConnections.North |
            SimpleTownRoadConnections.East |
            SimpleTownRoadConnections.West,
            false)]
        [TestCase(
            SimpleTownRoadTopology.All,
            false)]
        public void ShouldDrawCenterLines_HidesIntersectionTiles(
            SimpleTownRoadConnections connections,
            bool expected)
        {
            Assert.AreEqual(
                expected,
                SimpleTownRoadTopology.ShouldDrawCenterLines(
                    connections));
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void PerimeterCorner_ClosesOnlyExposedConcaveCorner(
            bool hasDiagonalRoad,
            bool expected)
        {
            SimpleTownRoadConnections connections =
                SimpleTownRoadConnections.North |
                SimpleTownRoadConnections.East;

            Assert.AreEqual(
                expected,
                SimpleTownRoadTopology.ShouldDrawPerimeterCorner(
                    connections,
                    SimpleTownRoadConnections.North,
                    SimpleTownRoadConnections.East,
                    hasDiagonalRoad));
        }

        [Test]
        public void PerimeterCorner_RequiresBothConnectedSides()
        {
            Assert.IsFalse(
                SimpleTownRoadTopology.ShouldDrawPerimeterCorner(
                    SimpleTownRoadConnections.North,
                    SimpleTownRoadConnections.North,
                    SimpleTownRoadConnections.East,
                    hasDiagonalRoad: false));
        }

        [TestCase(
            SimpleTownRoadConnections.East,
            true)]
        [TestCase(
            SimpleTownRoadConnections.West,
            true)]
        [TestCase(
            SimpleTownRoadConnections.East |
            SimpleTownRoadConnections.West,
            true)]
        [TestCase(
            SimpleTownRoadConnections.North,
            false)]
        [TestCase(
            SimpleTownRoadConnections.South,
            false)]
        [TestCase(
            SimpleTownRoadConnections.North |
            SimpleTownRoadConnections.South,
            false)]
        public void CenterLineDirection_FollowsRoadAxis(
            SimpleTownRoadConnections connections,
            bool expectedHorizontal)
        {
            Assert.AreEqual(
                expectedHorizontal,
                SimpleTownRoadTopology
                    .IsCenterLineHorizontal(connections));
        }

        [TestCase(5, 7, false)]
        [TestCase(7, 5, true)]
        public void PlacementPreview_DrawsOneCenteredLinePerStraightTile(
            int tileX,
            int tileY,
            bool expectedHorizontal)
        {
            var root = new GameObject("CenteredLinePreviewTest");
            MainCityView cityView =
                root.AddComponent<MainCityView>();
            var tileData =
                new CityFlow.Fakes.FakeFlowReader(10, 10);
            typeof(MainCityView)
                .GetField(
                    "tileData",
                    BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .SetValue(cityView, tileData);
            GameObject preview = null;

            try
            {
                Assert.IsTrue(
                    cityView.TryCreatePlacementPreview(
                        new Vector2Int(tileX, tileY),
                        CityFlow.Contracts.TileType.Road,
                        out preview));
                Transform centerLines =
                    preview.transform.Find("RoadCenterLines");

                Assert.IsNotNull(centerLines);
                Assert.AreEqual(
                    1,
                    centerLines.childCount,
                    "직선 도로 한 타일에는 중앙선이 하나만 있어야 한다.");
                Transform centerLine =
                    centerLines.GetChild(0);
                Assert.That(
                    centerLine.localPosition.x,
                    Is.EqualTo(0f).Within(0.0001f));
                Assert.That(
                    centerLine.localPosition.y,
                    Is.EqualTo(0f).Within(0.0001f));
                Assert.AreEqual(
                    expectedHorizontal,
                    centerLine.localScale.x >
                    centerLine.localScale.y);
            }
            finally
            {
                if (preview != null)
                {
                    Object.DestroyImmediate(preview);
                }
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PlacementPreview_FacesRoadSurfaceTowardCamera()
        {
            var root = new GameObject("MainCityViewTest");
            MainCityView cityView = root.AddComponent<MainCityView>();
            GameObject preview = null;

            try
            {
                Assert.IsTrue(
                    cityView.TryCreatePlacementPreview(
                        CityFlow.Contracts.TileType.Road,
                        out preview));
                Transform model = preview.transform.Find("RoadModel");

                Assert.IsNotNull(model);
                Assert.That(
                    Mathf.DeltaAngle(
                        model.localEulerAngles.x,
                        -90f),
                    Is.EqualTo(0f).Within(0.01f));

                Transform perimeter =
                    preview.transform.Find("RoadPerimeter");
                Transform centerLines =
                    preview.transform.Find("RoadCenterLines");
                Assert.IsNotNull(perimeter);
                Assert.AreEqual(4, perimeter.childCount);
                Assert.IsNotNull(centerLines);
                Assert.AreEqual(0, centerLines.childCount);
            }
            finally
            {
                if (preview != null)
                {
                    Object.DestroyImmediate(preview);
                }
                Object.DestroyImmediate(root);
            }
        }

        private static SimpleTownRoadVisualSetSO LoadVisualSet()
        {
            const string visualSetPath =
                "Assets/05_ScriptableObjects/Resources/CityFlow/" +
                "SimpleTownRoadVisualSet.asset";
            return AssetDatabase.LoadAssetAtPath<
                SimpleTownRoadVisualSetSO>(visualSetPath);
        }

        private static void AssertPrefabUsesLitMaterial(
            GameObject prefab,
            string expectedMaterialPath,
            string context)
        {
            MeshRenderer[] renderers =
                prefab.GetComponentsInChildren<MeshRenderer>();

            Assert.IsNotEmpty(renderers, context);
            foreach (MeshRenderer renderer in renderers)
            {
                Assert.AreEqual(
                    expectedMaterialPath,
                    AssetDatabase.GetAssetPath(renderer.sharedMaterial),
                    context);
                Assert.AreEqual(
                    "Universal Render Pipeline/Lit",
                    renderer.sharedMaterial.shader.name,
                    context);
                Assert.AreEqual(
                    1f,
                    renderer.sharedMaterial.color.a,
                    context);
            }
        }

    }
}

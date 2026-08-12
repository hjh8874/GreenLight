using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CityFlow.Contracts;
using CityFlow.Fakes;
using CityFlow.UI.Data;
using CityFlow.View;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Tests.ViewEditMode
{
    public sealed class RoundaboutVisualPrefabTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void VehicleGroundDepth_MatchesRoadSurface()
        {
            var host = new GameObject("VehicleGroundDepthTest");
            try
            {
                MainCityView cityView = host.AddComponent<MainCityView>();
                Assert.That(
                    cityView.VehicleGroundZ,
                    Is.EqualTo(cityView.RoadSurfaceZ).Within(0.0001f),
                    "바퀴 접촉면이 루트 0으로 정규화된 차량은 도로 표면에 직접 놓여야 한다.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void VisualSet_ProvidesLayeredRoundaboutPrefab()
        {
            SimpleTownRoadVisualSetSO visualSet =
                Resources.Load<SimpleTownRoadVisualSetSO>(
                    "CityFlow/SimpleTownRoadVisualSet");

            Assert.IsNotNull(visualSet);
            Assert.IsNotNull(visualSet.RoundaboutPrefab);

            Transform root = visualSet.RoundaboutPrefab.transform;
            Transform roadSurface = root.Find("RoadSurface");
            Transform outerCurb = root.Find("OuterCurb");
            Transform islandCurb = root.Find("IslandCurb");
            Transform islandGrass = root.Find("IslandGrass");

            Assert.IsNotNull(roadSurface);
            Assert.IsNotNull(outerCurb);
            Assert.IsNotNull(islandCurb);
            Assert.IsNotNull(islandGrass);
            Assert.IsNotNull(
                roadSurface.GetComponent<MeshRenderer>());
            Assert.IsNotNull(
                roadSurface.GetComponent<MeshFilter>()?.sharedMesh);

            Renderer regularRoadRenderer =
                visualSet.RoadSurfacePrefab
                    .GetComponentInChildren<Renderer>(true);
            Renderer roundaboutRoadRenderer =
                roadSurface.GetComponent<Renderer>();
            Assert.IsNotNull(regularRoadRenderer);
            Assert.AreSame(
                regularRoadRenderer.sharedMaterial,
                roundaboutRoadRenderer.sharedMaterial);

            Assert.That(
                roadSurface.localScale.x,
                Is.EqualTo(1.78f).Within(0.0001f));
            Assert.That(
                outerCurb.localScale.x,
                Is.EqualTo(1.78f).Within(0.0001f));
            Assert.That(
                islandCurb.localScale.x,
                Is.EqualTo(0.73f).Within(0.0001f));
            Assert.That(
                islandGrass.localScale.x,
                Is.EqualTo(0.62f).Within(0.0001f));
            Assert.Less(
                islandCurb.localPosition.z,
                roadSurface.localPosition.z);
            Assert.Less(
                islandGrass.localPosition.z,
                islandCurb.localPosition.z);
        }

        [Test]
        public void RoundaboutTopology_ChangesCommuteGeometryHash()
        {
            var host = new GameObject("RoundaboutTopologyHashTest");
            MainCityView cityView = host.AddComponent<MainCityView>();
            var facility = new FakeIntersectionFacilityService(
                new Vector2Int(6, 6));
            SetPrivateField(cityView, "intersectionFacility", facility);

            try
            {
                int placedHash = (int)InvokePrivate(
                    cityView,
                    "ComputeRoundaboutTuningHash");
                facility.ClearRoundabouts();
                int removedHash = (int)InvokePrivate(
                    cityView,
                    "ComputeRoundaboutTuningHash");

                Assert.AreNotEqual(
                    placedHash,
                    removedHash,
                    "로터리 배치나 철거는 기존 통근 경로의 지오메트리를 다시 베이크해야 합니다.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void GeneratedRoundabout_MatchesRegularRoadSurfaceAndPerimeter()
        {
            var host = new GameObject("RoundaboutGeometryTest");
            MainCityView cityView = host.AddComponent<MainCityView>();
            var tileData = new FakeFlowReader(12, 12);
            SetPrivateField(cityView, "tileData", tileData);

            SimpleTownRoadVisualSetSO visualSet =
                Resources.Load<SimpleTownRoadVisualSetSO>(
                    "CityFlow/SimpleTownRoadVisualSet");
            Assert.IsNotNull(visualSet);
            Assert.IsNotNull(visualSet.RoadSurfacePrefab);
            Renderer regularRoadRenderer =
                visualSet.RoadSurfacePrefab
                    .GetComponentInChildren<Renderer>(true);
            Assert.IsNotNull(regularRoadRenderer);
            Vector2Int center = new Vector2Int(6, 6);
            List<GameObject> roadVisuals = RegisterCrossRoadVisuals(
                cityView,
                center,
                regularRoadRenderer);
            GameObject roundabout = null;
            GameObject roadPreview = null;

            try
            {
                roundabout = (GameObject)InvokePrivate(
                    cityView,
                    "CreateRoundaboutVisual",
                    center);

                Transform surface = roundabout.transform.Find(
                    "RoundaboutRoadSurface");
                Assert.IsNotNull(surface);
                Renderer surfaceRenderer =
                    surface.GetComponent<Renderer>();
                Mesh surfaceMesh =
                    surface.GetComponent<MeshFilter>()?.sharedMesh;
                Assert.IsNotNull(surfaceRenderer);
                Assert.IsNotNull(surfaceMesh);
                Assert.AreSame(
                    regularRoadRenderer.sharedMaterial,
                    surfaceRenderer.sharedMaterial,
                    "로터리 차도는 일반 도로 재질 인스턴스를 그대로 공유해야 한다.");
                float overlayOffsetRatio = (float)
                    typeof(MainCityView)
                        .GetField(
                            "RoundaboutSurfaceOverlayOffsetRatio",
                            BindingFlags.NonPublic | BindingFlags.Static)
                        .GetRawConstantValue();
                float expectedCornerSurfaceZ =
                    cityView.RoadSurfaceZ -
                    cityView.TileSize * overlayOffsetRatio;
                float halfRoad = cityView.TileSize * 0.5f;
                Vector3[] surfaceVertices = surfaceMesh.vertices;
                Assert.That(
                    surfaceVertices.Length,
                    Is.GreaterThan(4));
                bool foundOverlayVertex = false;
                for (int vertexIndex = 0;
                     vertexIndex < surfaceVertices.Length;
                     vertexIndex++)
                {
                    Vector3 vertex = surfaceVertices[vertexIndex];
                    bool touchesRegularRoad =
                        Mathf.Abs(Mathf.Abs(vertex.x) - halfRoad) <= 0.0001f ||
                        Mathf.Abs(Mathf.Abs(vertex.y) - halfRoad) <= 0.0001f;
                    float expectedZ = touchesRegularRoad
                        ? cityView.RoadSurfaceZ
                        : expectedCornerSurfaceZ;
                    Assert.That(
                        vertex.z,
                        Is.EqualTo(expectedZ).Within(0.0001f));
                    foundOverlayVertex |= !touchesRegularRoad;
                }
                Assert.IsTrue(foundOverlayVertex);
                int[] surfaceTriangles = surfaceMesh.triangles;
                for (int triangleIndex = 0;
                     triangleIndex < surfaceTriangles.Length;
                     triangleIndex += 3)
                {
                    Vector3 first = surfaceVertices[
                        surfaceTriangles[triangleIndex]];
                    Vector3 second = surfaceVertices[
                        surfaceTriangles[triangleIndex + 1]];
                    Vector3 third = surfaceVertices[
                        surfaceTriangles[triangleIndex + 2]];
                    Assert.That(
                        Vector3.Cross(second - first, third - first).sqrMagnitude,
                        Is.GreaterThan(0.00000001f),
                        "접합 높이를 완만하게 맞춰도 퇴화 삼각형이 생기면 안 된다.");
                }
                AssertMeshCoversPoint(surfaceMesh, Vector2.zero);
                foreach (Vector2 uv in surfaceMesh.uv)
                {
                    Assert.That(uv.x, Is.EqualTo(0.75f).Within(0.0001f));
                    Assert.That(uv.y, Is.EqualTo(0.1f).Within(0.0001f));
                }

                Transform authoredModel = roundabout.transform.Find(
                    "RoundaboutModel");
                Assert.IsNotNull(authoredModel);
                Assert.IsFalse(
                    authoredModel.Find("RoadSurface")
                        .GetComponent<Renderer>().enabled);
                Assert.IsFalse(
                    authoredModel.Find("OuterCurb")
                        .GetComponent<Renderer>().enabled);

                Assert.IsTrue(
                    cityView.TryCreatePlacementPreview(
                        center + Vector2Int.right,
                        TileType.Road,
                        out roadPreview));
                Transform regularPerimeter = roadPreview.transform.Find(
                    "RoadPerimeter/Perimeter_North");
                Transform centerLine = roadPreview.transform.Find(
                    "RoadCenterLines/CenterLine");
                Assert.IsNotNull(regularPerimeter);
                Assert.IsNotNull(centerLine);

                float regularPerimeterWidth =
                    regularPerimeter.localScale.y;
                float regularPerimeterDepth =
                    regularPerimeter.localScale.z;
                float regularPerimeterMinZ =
                    cityView.FieldTileZ +
                    regularPerimeter.localPosition.z -
                    regularPerimeterDepth * 0.5f;
                float regularPerimeterMaxZ =
                    cityView.FieldTileZ +
                    regularPerimeter.localPosition.z +
                    regularPerimeterDepth * 0.5f;
                float centerLineLength = Mathf.Max(
                    centerLine.localScale.x,
                    centerLine.localScale.y);
                float expectedOuterRadius =
                    cityView.TileSize - centerLineLength * 0.5f;
                float expectedInnerRadius =
                    expectedOuterRadius - regularPerimeterWidth;

                Mesh arcMesh = roundabout.transform.Find(
                        "RoadPerimeter/PerimeterArc_0")
                    .GetComponent<MeshFilter>().sharedMesh;
                GetRadiusRange(
                    arcMesh,
                    out float actualInnerRadius,
                    out float actualOuterRadius);
                Assert.That(
                    actualOuterRadius,
                    Is.EqualTo(expectedOuterRadius).Within(0.0001f),
                    "인접 도로의 마지막 중앙선 끝이 로터리 외곽에 닿아야 한다.");
                Assert.That(
                    actualInnerRadius,
                    Is.EqualTo(expectedInnerRadius).Within(0.0001f),
                    "로터리 외곽선 폭은 일반 도로 외곽선 폭과 같아야 한다.");
                Assert.That(
                    arcMesh.bounds.min.z,
                    Is.EqualTo(regularPerimeterMinZ).Within(0.0001f));
                Assert.That(
                    arcMesh.bounds.max.z,
                    Is.EqualTo(regularPerimeterMaxZ).Within(0.0001f));

                Mesh approachMesh = roundabout.transform.Find(
                        "RoadPerimeter/Approach_0_1")
                    .GetComponent<MeshFilter>().sharedMesh;
                float roadOuterEdge = cityView.TileSize * 0.5f;
                float roadInnerEdge =
                    roadOuterEdge - regularPerimeterWidth;
                Vector2 outerSeam = new Vector2(
                    Mathf.Sqrt(
                        expectedOuterRadius * expectedOuterRadius -
                        roadOuterEdge * roadOuterEdge),
                    roadOuterEdge);
                Vector2 innerSeam = new Vector2(
                    Mathf.Sqrt(
                        expectedInnerRadius * expectedInnerRadius -
                        roadInnerEdge * roadInnerEdge),
                    roadInnerEdge);
                AssertMeshContainsPoint(arcMesh, outerSeam);
                AssertMeshContainsPoint(arcMesh, innerSeam);
                AssertMeshContainsPoint(approachMesh, outerSeam);
                AssertMeshContainsPoint(approachMesh, innerSeam);
                Assert.IsFalse(
                    HasVerticalFaceAtSegment(
                        approachMesh,
                        innerSeam,
                        outerSeam),
                    "원호 접합부를 내부 수직 cap으로 닫으면 야간에 삼각형 선이 드러난다.");
            }
            finally
            {
                if (roadPreview != null)
                {
                    UnityEngine.Object.DestroyImmediate(roadPreview);
                }
                if (roundabout != null)
                {
                    UnityEngine.Object.DestroyImmediate(roundabout);
                }
                DestroyAll(roadVisuals);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void RefreshRoundabout_HidesOnlyIntersectionCenterLine()
        {
            var host = new GameObject("RoundaboutCenterLineTest");
            MainCityView cityView = host.AddComponent<MainCityView>();
            SetPrivateField(
                cityView,
                "tileData",
                new FakeFlowReader(12, 12));
            Vector2Int center = new Vector2Int(6, 6);
            var facility = new FakeIntersectionFacilityService(center);
            SetPrivateField(cityView, "intersectionFacility", facility);

            SimpleTownRoadVisualSetSO visualSet =
                Resources.Load<SimpleTownRoadVisualSetSO>(
                    "CityFlow/SimpleTownRoadVisualSet");
            Assert.IsNotNull(visualSet);
            Assert.IsNotNull(visualSet.RoadSurfacePrefab);
            Renderer regularRoadRenderer =
                visualSet.RoadSurfacePrefab
                    .GetComponentInChildren<Renderer>(true);
            Assert.IsNotNull(regularRoadRenderer);
            List<GameObject> roadVisuals = RegisterCrossRoadVisuals(
                cityView,
                center,
                regularRoadRenderer);
            GameObject roundabout = null;

            try
            {
                InvokePrivate(cityView, "RefreshRoundabouts");
                IDictionary roundaboutVisuals = (IDictionary)
                    typeof(MainCityView)
                        .GetField("roundaboutVisuals", PrivateInstance)
                        .GetValue(cityView);
                roundabout = (GameObject)roundaboutVisuals[center];
                Assert.IsNotNull(roundabout);

                Renderer centerSurface = FindRoadVisual(
                        roadVisuals,
                        center)
                    .transform.Find("RoadSurface")
                    .GetComponent<Renderer>();
                Assert.IsFalse(
                    centerSurface.enabled,
                    "기존 중앙 도로 표면은 같은 UV의 로터리 표면으로 교체해야 한다.");

                Assert.IsFalse(
                    FindRoadVisual(roadVisuals, center)
                        .transform.Find("RoadCenterLines")
                        .gameObject.activeSelf,
                    "로터리 내부 교차로 중앙선은 숨겨야 한다.");

                Vector2Int[] approaches =
                {
                    center + Vector2Int.right,
                    center + Vector2Int.up,
                    center + Vector2Int.left,
                    center + Vector2Int.down
                };
                foreach (Vector2Int approach in approaches)
                {
                    Assert.IsTrue(
                        FindRoadVisual(roadVisuals, approach)
                            .transform.Find("RoadSurface")
                            .GetComponent<Renderer>().enabled,
                        $"인접 도로 {approach}의 표면은 유지해야 한다.");
                    Assert.IsTrue(
                        FindRoadVisual(roadVisuals, approach)
                            .transform.Find("RoadCenterLines")
                            .gameObject.activeSelf,
                        $"인접 도로 {approach}의 마지막 중앙선은 유지해야 한다.");
                }

                facility.ClearRoundabouts();
                InvokePrivate(cityView, "RefreshRoundabouts");
                Assert.IsTrue(
                    centerSurface.enabled,
                    "로터리를 철거하면 기존 중앙 도로 표면을 복원해야 한다.");
                Assert.IsTrue(
                    FindRoadVisual(roadVisuals, center)
                        .transform.Find("RoadCenterLines")
                        .gameObject.activeSelf,
                    "로터리를 철거하면 중앙 도로선도 복원해야 한다.");
                roundabout = null;
            }
            finally
            {
                if (roundabout != null)
                {
                    UnityEngine.Object.DestroyImmediate(roundabout);
                }
                DestroyAll(roadVisuals);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void RoundaboutPreview_TIntersectionUsesZeroSurfaceAndClosesMissingArm()
        {
            var host = new GameObject("RoundaboutPreviewSurfaceTest");
            MainCityView cityView = host.AddComponent<MainCityView>();
            Vector2Int center = new Vector2Int(6, 6);
            var tileData = new MutableRoadTileData(12, 12);
            tileData.AddRoad(center);
            tileData.AddRoad(center + Vector2Int.right);
            tileData.AddRoad(center + Vector2Int.up);
            tileData.AddRoad(center + Vector2Int.left);
            SetPrivateField(cityView, "tileData", tileData);

            InfrastructureDataSO data =
                ScriptableObject.CreateInstance<InfrastructureDataSO>();
            data.Kind = InfrastructureKind.Roundabout;
            GameObject preview = null;

            try
            {
                Assert.IsTrue(
                    cityView.TryCreateInfrastructurePlacementPreview(
                        data,
                        null,
                        center,
                        out preview));
                Mesh surfaceMesh = preview.transform.Find(
                        $"Roundabout_{center.x}_{center.y}/" +
                        "RoundaboutRoadSurface")
                    .GetComponent<MeshFilter>().sharedMesh;
                Assert.IsNotNull(surfaceMesh);
                float overlayOffsetRatio = (float)
                    typeof(MainCityView)
                        .GetField(
                            "RoundaboutSurfaceOverlayOffsetRatio",
                            BindingFlags.NonPublic | BindingFlags.Static)
                        .GetRawConstantValue();
                Vector3[] surfaceVertices = surfaceMesh.vertices;
                float halfRoad = cityView.TileSize * 0.5f;
                bool foundOverlayVertex = false;
                for (int vertexIndex = 0;
                     vertexIndex < surfaceVertices.Length;
                     vertexIndex++)
                {
                    Vector3 vertex = surfaceVertices[vertexIndex];
                    bool touchesRegularRoad =
                        Mathf.Abs(Mathf.Abs(vertex.x) - halfRoad) <= 0.0001f ||
                        Mathf.Abs(Mathf.Abs(vertex.y) - halfRoad) <= 0.0001f;
                    float expectedZ = touchesRegularRoad
                        ? 0f
                        : -cityView.TileSize * overlayOffsetRatio;
                    Assert.That(
                        vertex.z,
                        Is.EqualTo(expectedZ).Within(0.0001f),
                        "프리뷰 접합점은 도로 높이, 기초 겹침 내부는 시각 오프셋을 사용해야 한다.");
                    foundOverlayVertex |= !touchesRegularRoad;
                }
                Assert.IsTrue(foundOverlayVertex);

                float sampleY = -cityView.TileSize * 0.65f;
                AssertMeshCoversPoint(
                    surfaceMesh,
                    new Vector2(-cityView.TileSize * 0.05f, sampleY));
                AssertMeshCoversPoint(
                    surfaceMesh,
                    new Vector2(cityView.TileSize * 0.05f, sampleY));
            }
            finally
            {
                if (preview != null)
                {
                    UnityEngine.Object.DestroyImmediate(preview);
                }
                UnityEngine.Object.DestroyImmediate(data);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void RefreshRoundabout_RecreatesVisualWhenTopologyChanges()
        {
            var host = new GameObject("RoundaboutTopologyRefreshTest");
            MainCityView cityView = host.AddComponent<MainCityView>();
            Vector2Int center = new Vector2Int(6, 6);
            var tileData = new MutableRoadTileData(12, 12);
            tileData.AddRoad(center);
            tileData.AddRoad(center + Vector2Int.right);
            tileData.AddRoad(center + Vector2Int.up);
            tileData.AddRoad(center + Vector2Int.left);
            SetPrivateField(cityView, "tileData", tileData);
            SetPrivateField(
                cityView,
                "intersectionFacility",
                new FakeIntersectionFacilityService(center));

            SimpleTownRoadVisualSetSO visualSet =
                Resources.Load<SimpleTownRoadVisualSetSO>(
                    "CityFlow/SimpleTownRoadVisualSet");
            Renderer regularRoadRenderer =
                visualSet.RoadSurfacePrefab
                    .GetComponentInChildren<Renderer>(true);
            List<GameObject> roadVisuals = RegisterCrossRoadVisuals(
                cityView,
                center,
                regularRoadRenderer);
            GameObject firstVisual = null;
            GameObject secondVisual = null;

            try
            {
                InvokePrivate(cityView, "RefreshRoundabouts");
                firstVisual = GetRoundaboutVisual(cityView, center);
                int firstSignature = GetRoundaboutSignature(
                    cityView,
                    center);
                Mesh firstSurface = GetRoundaboutSurfaceMesh(firstVisual);
                AssertMeshCoversPoint(
                    firstSurface,
                    new Vector2(
                        cityView.TileSize * 0.05f,
                        -cityView.TileSize * 0.65f));

                tileData.AddRoad(center + Vector2Int.down);
                InvokePrivate(cityView, "RefreshRoundabouts");
                secondVisual = GetRoundaboutVisual(cityView, center);
                int secondSignature = GetRoundaboutSignature(
                    cityView,
                    center);

                Assert.AreNotSame(firstVisual, secondVisual);
                Assert.IsTrue(
                    firstVisual == null || !firstVisual.activeSelf,
                    "교체 대기 중인 이전 로터리 비주얼은 즉시 숨겨야 한다.");
                Assert.AreNotEqual(
                    firstSignature,
                    secondSignature,
                    "도로 연결 토폴로지가 바뀌면 로터리 시그니처도 갱신돼야 한다.");
                AssertMeshDoesNotCoverPoint(
                    GetRoundaboutSurfaceMesh(secondVisual),
                    new Vector2(
                        cityView.TileSize * 0.05f,
                        -cityView.TileSize * 0.65f));
            }
            finally
            {
                if (firstVisual != null)
                {
                    UnityEngine.Object.DestroyImmediate(firstVisual);
                }
                if (secondVisual != null)
                {
                    UnityEngine.Object.DestroyImmediate(secondVisual);
                }
                DestroyAll(roadVisuals);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void DestroyRoundabout_ReleasesOnlyOwnedGeneratedResources()
        {
            var host = new GameObject("RoundaboutResourceLifetimeTest");
            MainCityView cityView = host.AddComponent<MainCityView>();
            SetPrivateField(
                cityView,
                "tileData",
                new FakeFlowReader(12, 12));
            SimpleTownRoadVisualSetSO visualSet =
                Resources.Load<SimpleTownRoadVisualSetSO>(
                    "CityFlow/SimpleTownRoadVisualSet");
            Material sharedRoadMaterial =
                visualSet.RoadSurfacePrefab
                    .GetComponentInChildren<Renderer>(true)
                    .sharedMaterial;
            GameObject roundabout = null;

            try
            {
                roundabout = (GameObject)InvokePrivate(
                    cityView,
                    "CreateRoundaboutVisual",
                    new Vector2Int(6, 6));
                Component owner = roundabout.GetComponent(
                    "RoundaboutGeneratedMeshOwner");
                Assert.IsNotNull(owner);

                List<Mesh> trackedMeshes = CopyTrackedObjects<Mesh>(
                    owner,
                    "meshes");
                List<Material> trackedMaterials =
                    CopyTrackedObjects<Material>(owner, "materials");
                Assert.IsNotEmpty(trackedMeshes);
                Assert.IsNotEmpty(trackedMaterials);

                UnityEngine.Object.DestroyImmediate(roundabout);
                roundabout = null;

                foreach (Mesh mesh in trackedMeshes)
                {
                    Assert.IsTrue(
                        mesh == null,
                        "로터리 생성 Mesh는 소유 오브젝트 제거 시 함께 해제돼야 한다.");
                }
                foreach (Material material in trackedMaterials)
                {
                    Assert.IsTrue(
                        material == null,
                        "로터리 전용 Material은 소유 오브젝트 제거 시 함께 해제돼야 한다.");
                }
                Assert.IsTrue(
                    sharedRoadMaterial != null,
                    "공유 일반 도로 Material은 로터리 제거로 파괴되면 안 된다.");
            }
            finally
            {
                if (roundabout != null)
                {
                    UnityEngine.Object.DestroyImmediate(roundabout);
                }
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static List<GameObject> RegisterCrossRoadVisuals(
            MainCityView cityView,
            Vector2Int center,
            Renderer sourceRenderer)
        {
            Vector2Int[] tiles =
            {
                center,
                center + Vector2Int.right,
                center + Vector2Int.up,
                center + Vector2Int.left,
                center + Vector2Int.down
            };
            var roots = new List<GameObject>();
            foreach (Vector2Int tile in tiles)
            {
                var root = new GameObject(
                    $"RoadVisual_{tile.x}_{tile.y}");
                var surface = new GameObject("RoadSurface");
                surface.transform.SetParent(root.transform, false);
                Renderer roadRenderer =
                    surface.AddComponent<MeshRenderer>();
                roadRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
                roadRenderer.shadowCastingMode =
                    sourceRenderer.shadowCastingMode;
                roadRenderer.receiveShadows = sourceRenderer.receiveShadows;
                var centerLines = new GameObject("RoadCenterLines");
                centerLines.transform.SetParent(root.transform, false);
                var perimeter = new GameObject("RoadPerimeter");
                perimeter.transform.SetParent(root.transform, false);
                string[] perimeterParts =
                {
                    "Perimeter_North",
                    "Perimeter_East",
                    "Perimeter_South",
                    "Perimeter_West",
                    "PerimeterCorner_North_East",
                    "PerimeterCorner_East_South",
                    "PerimeterCorner_South_West",
                    "PerimeterCorner_West_North"
                };
                foreach (string partName in perimeterParts)
                {
                    var part = new GameObject(partName);
                    part.transform.SetParent(perimeter.transform, false);
                }

                RegisterRoadVisual(
                    cityView,
                    tile,
                    root,
                    roadRenderer);
                roots.Add(root);
            }

            return roots;
        }

        private static void RegisterRoadVisual(
            MainCityView cityView,
            Vector2Int tile,
            GameObject root,
            Renderer sourceRenderer)
        {
            Type tileVisualType = typeof(MainCityView).GetNestedType(
                "TileVisual",
                BindingFlags.NonPublic);
            object tileVisual = Activator.CreateInstance(
                tileVisualType,
                nonPublic: true);
            tileVisualType.GetField("Object").SetValue(tileVisual, root);
            tileVisualType.GetField("Renderer").SetValue(
                tileVisual,
                sourceRenderer);
            tileVisualType.GetField("Block").SetValue(
                tileVisual,
                new MaterialPropertyBlock());
            tileVisualType.GetField("Type").SetValue(
                tileVisual,
                TileType.Road);

            IDictionary tileVisuals = (IDictionary)
                typeof(MainCityView)
                    .GetField("tileVisuals", PrivateInstance)
                    .GetValue(cityView);
            tileVisuals.Add(tile, tileVisual);
        }

        private static GameObject FindRoadVisual(
            IReadOnlyList<GameObject> roadVisuals,
            Vector2Int tile)
        {
            string expectedName = $"RoadVisual_{tile.x}_{tile.y}";
            for (int i = 0; i < roadVisuals.Count; i++)
            {
                if (roadVisuals[i].name == expectedName)
                {
                    return roadVisuals[i];
                }
            }

            Assert.Fail($"도로 비주얼을 찾지 못했습니다: {expectedName}");
            return null;
        }

        private static GameObject GetRoundaboutVisual(
            MainCityView cityView,
            Vector2Int tile)
        {
            IDictionary visuals = (IDictionary)
                typeof(MainCityView)
                    .GetField("roundaboutVisuals", PrivateInstance)
                    .GetValue(cityView);
            return (GameObject)visuals[tile];
        }

        private static int GetRoundaboutSignature(
            MainCityView cityView,
            Vector2Int tile)
        {
            IDictionary signatures = (IDictionary)
                typeof(MainCityView)
                    .GetField(
                        "roundaboutVisualSignatures",
                        PrivateInstance)
                    .GetValue(cityView);
            return (int)signatures[tile];
        }

        private static Mesh GetRoundaboutSurfaceMesh(
            GameObject roundabout)
        {
            return roundabout.transform.Find("RoundaboutRoadSurface")
                .GetComponent<MeshFilter>().sharedMesh;
        }

        private static void GetRadiusRange(
            Mesh mesh,
            out float minimum,
            out float maximum)
        {
            minimum = float.PositiveInfinity;
            maximum = float.NegativeInfinity;
            foreach (Vector3 vertex in mesh.vertices)
            {
                float radius = new Vector2(vertex.x, vertex.y).magnitude;
                minimum = Mathf.Min(minimum, radius);
                maximum = Mathf.Max(maximum, radius);
            }
        }

        private static void AssertMeshContainsPoint(
            Mesh mesh,
            Vector2 expected)
        {
            foreach (Vector3 vertex in mesh.vertices)
            {
                if (Mathf.Abs(vertex.x - expected.x) <= 0.0001f &&
                    Mathf.Abs(vertex.y - expected.y) <= 0.0001f)
                {
                    return;
                }
            }

            Assert.Fail(
                $"메시에 연결점이 없습니다: ({expected.x}, {expected.y})");
        }

        private static bool HasVerticalFaceAtSegment(
            Mesh mesh,
            Vector2 firstEndpoint,
            Vector2 secondEndpoint)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int index = 0; index < triangles.Length; index += 3)
            {
                bool allOnSegment = true;
                float minZ = float.PositiveInfinity;
                float maxZ = float.NegativeInfinity;
                for (int corner = 0; corner < 3; corner++)
                {
                    Vector3 vertex = vertices[triangles[index + corner]];
                    Vector2 point = new Vector2(vertex.x, vertex.y);
                    bool atEndpoint =
                        Vector2.Distance(point, firstEndpoint) <= 0.0001f ||
                        Vector2.Distance(point, secondEndpoint) <= 0.0001f;
                    allOnSegment &= atEndpoint;
                    minZ = Mathf.Min(minZ, vertex.z);
                    maxZ = Mathf.Max(maxZ, vertex.z);
                }

                if (allOnSegment && maxZ - minZ > 0.0001f)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertMeshCoversPoint(
            Mesh mesh,
            Vector2 point)
        {
            Assert.IsTrue(
                MeshCoversPoint(mesh, point),
                $"메시가 도로 표면점을 덮지 않습니다: ({point.x}, {point.y})");
        }

        private static void AssertMeshDoesNotCoverPoint(
            Mesh mesh,
            Vector2 point)
        {
            Assert.IsFalse(
                MeshCoversPoint(mesh, point),
                $"연결 도로 영역을 코너 패치가 침범합니다: ({point.x}, {point.y})");
        }

        private static bool MeshCoversPoint(Mesh mesh, Vector2 point)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector2 a = vertices[triangles[i]];
                Vector2 b = vertices[triangles[i + 1]];
                Vector2 c = vertices[triangles[i + 2]];
                float first = Cross(b - a, point - a);
                float second = Cross(c - b, point - b);
                float third = Cross(a - c, point - c);
                const float tolerance = 0.0001f;
                bool hasNegative = first < -tolerance ||
                                   second < -tolerance ||
                                   third < -tolerance;
                bool hasPositive = first > tolerance ||
                                   second > tolerance ||
                                   third > tolerance;
                if (!(hasNegative && hasPositive))
                {
                    return true;
                }
            }

            return false;
        }

        private static float Cross(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        private static List<T> CopyTrackedObjects<T>(
            Component owner,
            string fieldName)
            where T : UnityEngine.Object
        {
            IList tracked = (IList)owner.GetType()
                .GetField(fieldName, PrivateInstance)
                .GetValue(owner);
            var copy = new List<T>(tracked.Count);
            for (int i = 0; i < tracked.Count; i++)
            {
                copy.Add((T)tracked[i]);
            }

            return copy;
        }

        private static void SetPrivateField(
            object target,
            string fieldName,
            object value)
        {
            typeof(MainCityView)
                .GetField(fieldName, PrivateInstance)
                .SetValue(target, value);
        }

        private static object InvokePrivate(
            object target,
            string methodName,
            params object[] arguments)
        {
            return typeof(MainCityView)
                .GetMethod(methodName, PrivateInstance)
                .Invoke(target, arguments);
        }

        private static void DestroyAll(
            IReadOnlyList<GameObject> objects)
        {
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(objects[i]);
                }
            }
        }

        private sealed class FakeIntersectionFacilityService :
            IIntersectionFacilityService
        {
            private static readonly IReadOnlyList<Vector2Int> Empty =
                Array.Empty<Vector2Int>();
            private readonly List<Vector2Int> roundabouts;

            public FakeIntersectionFacilityService(Vector2Int roundabout)
            {
                roundabouts = new List<Vector2Int> { roundabout };
            }

            public void ClearRoundabouts() => roundabouts.Clear();

            public IReadOnlyList<Vector2Int> SignalTiles => Empty;
            public IReadOnlyList<Vector2Int> RoundaboutTiles => roundabouts;
            public IReadOnlyList<Vector2Int> OverpassTiles => Empty;
            public IReadOnlyList<Vector2Int> PriorityRoadTiles => Empty;
            public bool CanPlaceSignal(Vector2Int tile) => false;
            public bool TryPlaceSignal(Vector2Int tile, int greenSlots) => false;
            public bool TryRemoveSignal(Vector2Int tile) => false;
            public bool CanPlaceRoundabout(Vector2Int tile) => false;
            public bool TryPlaceRoundabout(Vector2Int tile) => false;
            public bool TryRemoveRoundabout(Vector2Int tile) => false;
            public bool CanPlaceOverpass(Vector2Int tile) => false;
            public bool TryPlaceOverpass(Vector2Int tile) => false;
            public bool TryRemoveOverpass(Vector2Int tile) => false;
            public Axis GetPriorityAxis(Vector2Int tile) => Axis.Horizontal;
            public bool CanPlacePriorityRoad(Vector2Int tile) => false;
            public bool TryPlacePriorityRoad(
                Vector2Int tile,
                Axis mainAxis) => false;
            public bool TryRemovePriorityRoad(Vector2Int tile) => false;
        }

        private sealed class MutableRoadTileData : IReadOnlyTileData
        {
            private readonly int width;
            private readonly int height;
            private readonly HashSet<Vector2Int> roads =
                new HashSet<Vector2Int>();

            public MutableRoadTileData(int width, int height)
            {
                this.width = width;
                this.height = height;
            }

            public void AddRoad(Vector2Int tile)
            {
                roads.Add(tile);
            }

            public CongestionLevel GetCongestion(Vector2Int tile) =>
                CongestionLevel.Free;
            public float GetDensity01(Vector2Int tile) => 0f;
            public int GetQueueCount(Vector2Int tile, Dir entryDir) => 0;
            public TileType GetTileType(Vector2Int tile)
            {
                if (tile.x < 0 || tile.x >= width ||
                    tile.y < 0 || tile.y >= height)
                {
                    return TileType.Empty;
                }

                return roads.Contains(tile)
                    ? TileType.Road
                    : TileType.Empty;
            }

            public PlacementDirection GetDirection(Vector2Int tile) =>
                PlacementDirection.North;
            public Vector2Int GetFootprintSize(TileType type) =>
                TileFootprint.GetSize(type);
            public bool TryGetFootprintAnchor(
                Vector2Int tile,
                out Vector2Int anchor)
            {
                anchor = tile;
                return roads.Contains(tile);
            }

            public bool IsFootprintAnchor(Vector2Int tile) =>
                roads.Contains(tile);
            public bool TryGetConstructionProgress01(
                Vector2Int tile,
                out float progress01)
            {
                progress01 = 0f;
                return false;
            }

            public bool TryGetConstructionTargetType(
                Vector2Int tile,
                out TileType targetType)
            {
                targetType = TileType.Empty;
                return false;
            }
        }
    }
}

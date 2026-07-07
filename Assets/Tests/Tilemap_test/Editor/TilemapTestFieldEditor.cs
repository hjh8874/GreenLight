using CityFlow.Contracts;
using CityFlow.TilemapTest;
using CityFlow.View;
using UnityEditor;
using UnityEngine;

namespace CityFlow.TilemapTest.Editor
{
    [CustomEditor(typeof(TilemapTestField))]
    public sealed class TilemapTestFieldEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(12f);

            TilemapTestField field = (TilemapTestField)target;

            if (GUILayout.Button("Bake Test Field"))
            {
                BakeTestField(field);
            }

            if (GUILayout.Button("Apply Example Layout"))
            {
                ApplyExampleLayout(field);
            }

            if (GUILayout.Button("Apply Roads Only Layout"))
            {
                ApplyRoadsOnlyLayout(field);
            }

            if (GUILayout.Button("Apply Dense Demand Layout"))
            {
                ApplyDenseDemandLayout(field);
            }

            if (GUILayout.Button("Refresh Tile Colors"))
            {
                EnsureFieldMaterial(field);
                field.RefreshAllTileColors();
                EditorUtility.SetDirty(field);
            }

            if (GUILayout.Button("Repair Tile Materials"))
            {
                RepairTileMaterials(field);
            }

            if (GUILayout.Button("Create Test Vehicle Prefab"))
            {
                CreateOrAssignVehiclePrefab(field);
            }

            if (GUILayout.Button("Attach Vehicle Density Views"))
            {
                AttachVehicleDensityViews(field);
            }

            if (GUILayout.Button("Add Moving Trip Vehicle System"))
            {
                AddMovingTripVehicleSystem(field);
            }

            if (GUILayout.Button("Clear Baked Tiles"))
            {
                ClearBakedTiles(field);
            }
        }

        private static void BakeTestField(TilemapTestField field)
        {
            Undo.RegisterFullObjectHierarchyUndo(field.gameObject, "Bake Tilemap Test Field");
            EnsureFieldMaterial(field);
            Transform root = EnsureTileRoot(field);
            ClearChildren(root);

            for (int y = 0; y < field.Height; y++)
            {
                for (int x = 0; x < field.Width; x++)
                {
                    CreateTile(field, root, new Vector2Int(x, y), TileType.Empty);
                }
            }

            field.RebuildCache();
            EditorUtility.SetDirty(field);
            Debug.Log($"Baked {field.Width * field.Height} test field tiles.");
        }

        private static void ApplyExampleLayout(TilemapTestField field)
        {
            Undo.RegisterFullObjectHierarchyUndo(field.gameObject, "Apply Tilemap Test Layout");
            EnsureFieldMaterial(field);
            field.RebuildCache();

            TilemapTestTile[] tiles = field.GetComponentsInChildren<TilemapTestTile>(true);
            System.Random random = new System.Random(1101);
            foreach (TilemapTestTile tile in tiles)
            {
                TileType type = GetExampleTileType(field, tile.GridPosition);
                tile.SetTileType(type);
                ApplyDefaultFacilityData(field, tile, random, false);
                EditorUtility.SetDirty(tile);
            }

            field.RefreshAllTileColors();
            EditorUtility.SetDirty(field);
            Debug.Log("Applied example traffic-economy layout to baked tiles.");
        }

        private static void ApplyRoadsOnlyLayout(TilemapTestField field)
        {
            Undo.RegisterFullObjectHierarchyUndo(field.gameObject, "Apply Roads Only Tilemap Test Layout");
            EnsureFieldMaterial(field);
            field.RebuildCache();

            TilemapTestTile[] tiles = field.GetComponentsInChildren<TilemapTestTile>(true);
            foreach (TilemapTestTile tile in tiles)
            {
                TileType type = IsExampleRoad(tile.GridPosition) ? TileType.Road : TileType.Empty;
                tile.SetTileType(type);
                tile.SetFacilityKind(type == TileType.Road ? TilemapTestFacilityKind.Road : TilemapTestFacilityKind.None);
                EditorUtility.SetDirty(tile);
            }

            field.RefreshAllTileColors();
            EditorUtility.SetDirty(field);
            Debug.Log("Applied roads-only test layout to baked tiles.");
        }

        private static void ApplyDenseDemandLayout(TilemapTestField field)
        {
            Undo.RegisterFullObjectHierarchyUndo(field.gameObject, "Apply Dense Demand Tilemap Test Layout");
            EnsureFieldMaterial(field);
            field.RebuildCache();

            TilemapTestTile[] tiles = field.GetComponentsInChildren<TilemapTestTile>(true);
            System.Random random = new System.Random(2307);
            int roads = 0;
            int houses = 0;
            int offices = 0;
            int schools = 0;
            int shops = 0;

            foreach (TilemapTestTile tile in tiles)
            {
                TileType type = GetDenseDemandTileType(field, tile.GridPosition);
                TilemapTestFacilityKind facilityKind = GetDenseDemandFacilityKind(field, tile.GridPosition, type);
                tile.SetTileType(type);
                tile.SetFacilityKind(facilityKind);
                ApplyDefaultFacilityData(field, tile, random, true);
                EditorUtility.SetDirty(tile);

                switch (facilityKind)
                {
                    case TilemapTestFacilityKind.Road:
                        roads++;
                        break;
                    case TilemapTestFacilityKind.House:
                        houses++;
                        break;
                    case TilemapTestFacilityKind.Office:
                        offices++;
                        break;
                    case TilemapTestFacilityKind.School:
                        schools++;
                        break;
                    case TilemapTestFacilityKind.Shop:
                        shops++;
                        break;
                }
            }

            field.RefreshAllTileColors();
            EditorUtility.SetDirty(field);
            Debug.Log($"Applied dense demand layout. Roads: {roads}, Houses: {houses}, Offices: {offices}, Schools: {schools}, Shops: {shops}.");
        }

        private static void ClearBakedTiles(TilemapTestField field)
        {
            Transform root = field.TileRoot;
            if (root == null)
            {
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(field.gameObject, "Clear Tilemap Test Field");
            ClearChildren(root);
            field.RebuildCache();
            EditorUtility.SetDirty(field);
        }

        private static void RepairTileMaterials(TilemapTestField field)
        {
            Undo.RegisterFullObjectHierarchyUndo(field.gameObject, "Repair Tilemap Test Materials");
            EnsureFieldMaterial(field);

            TilemapTestTile[] tiles = field.GetComponentsInChildren<TilemapTestTile>(true);
            foreach (TilemapTestTile tile in tiles)
            {
                tile.EnsureMaterial(field.TileMaterial);
                field.RefreshTile(tile);
                EditorUtility.SetDirty(tile);
            }

            EditorUtility.SetDirty(field);
            Debug.Log($"Repaired materials for {tiles.Length} baked test tiles.");
        }

        private static void AttachVehicleDensityViews(TilemapTestField field)
        {
            Undo.RegisterFullObjectHierarchyUndo(field.gameObject, "Attach Vehicle Density Views");

            if (field.TestVehiclePrefab == null)
            {
                CreateOrAssignVehiclePrefab(field);
            }

            TilemapTestTile[] tiles = field.GetComponentsInChildren<TilemapTestTile>(true);
            int attachedCount = 0;

            foreach (TilemapTestTile tile in tiles)
            {
                VehicleDensityView existingView = tile.GetComponent<VehicleDensityView>();

                if (tile.TileType != TileType.Road)
                {
                    if (existingView != null)
                    {
                        Undo.DestroyObjectImmediate(existingView);
                    }

                    continue;
                }

                VehicleDensityView view = existingView != null
                    ? existingView
                    : Undo.AddComponent<VehicleDensityView>(tile.gameObject);

                SerializedObject serializedView = new SerializedObject(view);
                serializedView.FindProperty("tile").vector2IntValue = tile.GridPosition;
                serializedView.FindProperty("vehiclePrefab").objectReferenceValue = field.TestVehiclePrefab;
                serializedView.FindProperty("maxVehicles").intValue = field.MaxVehiclesPerRoadTile;
                serializedView.FindProperty("spacing").floatValue = field.VehicleSpacing;
                serializedView.ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.SetDirty(view);
                attachedCount++;
            }

            EditorUtility.SetDirty(field);
            Debug.Log($"Attached VehicleDensityView to {attachedCount} road tiles.");
        }

        private static void AddMovingTripVehicleSystem(TilemapTestField field)
        {
            Undo.RegisterFullObjectHierarchyUndo(field.gameObject, "Add Moving Trip Vehicle System");

            if (field.TestVehiclePrefab == null)
            {
                CreateOrAssignVehiclePrefab(field);
            }

            TilemapTestVehicleTripSystem tripSystem = field.GetComponent<TilemapTestVehicleTripSystem>();
            if (tripSystem == null)
            {
                tripSystem = Undo.AddComponent<TilemapTestVehicleTripSystem>(field.gameObject);
            }

            tripSystem.Configure(field, field.TestVehiclePrefab);
            EditorUtility.SetDirty(tripSystem);
            EditorUtility.SetDirty(field);
            Debug.Log("Added moving trip vehicle system to TilemapTestField.");
        }

        private static Transform EnsureTileRoot(TilemapTestField field)
        {
            Transform existingRoot = field.TileRoot;
            if (existingRoot != null)
            {
                return existingRoot;
            }

            Transform foundRoot = field.transform.Find("Tiles");
            if (foundRoot != null)
            {
                field.SetTileRoot(foundRoot);
                EditorUtility.SetDirty(field);
                return foundRoot;
            }

            GameObject rootObject = new GameObject("Tiles");
            Undo.RegisterCreatedObjectUndo(rootObject, "Create Tile Root");
            rootObject.transform.SetParent(field.transform, false);
            field.SetTileRoot(rootObject.transform);
            EditorUtility.SetDirty(field);
            return rootObject.transform;
        }

        private static void CreateTile(TilemapTestField field, Transform root, Vector2Int gridPosition, TileType type)
        {
            GameObject tileObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(tileObject, "Create Test Tile");
            tileObject.transform.SetParent(root, false);
            tileObject.transform.position = field.GridToWorld(gridPosition);
            tileObject.transform.localScale = new Vector3(
                field.TileSize * 0.96f,
                field.TileThickness,
                field.TileSize * 0.96f);

            TilemapTestTile tile = tileObject.AddComponent<TilemapTestTile>();
            tile.Configure(gridPosition, type);
            tile.SetFacilityKind(type == TileType.Road ? TilemapTestFacilityKind.Road : TilemapTestFacilityKind.None);
            tile.EnsureMaterial(field.TileMaterial);
            tile.ApplyColor(field.GetColor(type, CongestionLevel.Free));
            EditorUtility.SetDirty(tileObject);
        }

        private static void CreateOrAssignVehiclePrefab(TilemapTestField field)
        {
            const string prefabPath = "Assets/Tests/Tilemap_test/TilemapTestVehicle.prefab";
            GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (existingPrefab != null)
            {
                field.SetTestVehiclePrefab(existingPrefab.transform);
                EditorUtility.SetDirty(field);
                return;
            }

            GameObject vehicleRoot = new GameObject("TilemapTestVehicle");
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            visual.transform.SetParent(vehicleRoot.transform, false);
            visual.transform.localScale = new Vector3(0.28f, 0.16f, 0.42f);
            visual.transform.localPosition = Vector3.zero;

            TilemapTestVehicleLaneMotion laneMotion = vehicleRoot.AddComponent<TilemapTestVehicleLaneMotion>();
            SerializedObject serializedMotion = new SerializedObject(laneMotion);
            serializedMotion.FindProperty("visualRoot").objectReferenceValue = visual.transform;
            serializedMotion.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(vehicleRoot, prefabPath);
            Object.DestroyImmediate(vehicleRoot);

            if (prefab == null)
            {
                Debug.LogError("Failed to create TilemapTestVehicle prefab.");
                return;
            }

            field.SetTestVehiclePrefab(prefab.transform);
            EditorUtility.SetDirty(field);
            AssetDatabase.SaveAssets();
            Debug.Log($"Created test vehicle prefab at {prefabPath}.");
        }

        private static void EnsureFieldMaterial(TilemapTestField field)
        {
            if (field.TileMaterial != null)
            {
                return;
            }

            Material material = LoadOrCreateDefaultMaterial();
            field.SetTileMaterial(material);
            EditorUtility.SetDirty(field);
        }

        private static Material LoadOrCreateDefaultMaterial()
        {
            const string materialPath = "Assets/Tests/Tilemap_test/TilemapTestDefault.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader)
            {
                name = "TilemapTestDefault"
            };

            AssetDatabase.CreateAsset(material, materialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static TileType GetExampleTileType(TilemapTestField field, Vector2Int position)
        {
            int centerX = field.Width / 2;
            int centerY = field.Height / 2;
            bool isRoad = IsExampleRoad(position);

            if (isRoad)
            {
                return TileType.Road;
            }

            if (!HasAdjacentExampleRoad(position))
            {
                return TileType.Empty;
            }

            if (position.x < centerX && position.y < centerY && (position.x + position.y) % 2 == 0)
            {
                return TileType.House;
            }

            if (position.x >= centerX && position.y >= centerY && (position.x + position.y) % 2 == 0)
            {
                return TileType.Office;
            }

            if (position.x < centerX && position.y >= centerY && (position.x + position.y) % 4 == 0)
            {
                return TileType.School;
            }

            return TileType.Empty;
        }

        private static TileType GetDenseDemandTileType(TilemapTestField field, Vector2Int position)
        {
            int centerY = field.Height / 2;
            int sourceRoadX = Mathf.Clamp(3, 1, field.Width - 2);
            int schoolRoadX = Mathf.Clamp(field.Width - 7, 1, field.Width - 2);
            int officeRoadX = Mathf.Clamp(field.Width - 4, 1, field.Width - 2);
            int minY = Mathf.Max(2, centerY - 5);
            int maxY = Mathf.Min(field.Height - 3, centerY + 5);

            bool isHorizontalRoad = position.y == centerY &&
                                    position.x >= 1 &&
                                    position.x <= field.Width - 2;
            bool isSourceFeeder = position.x == sourceRoadX &&
                                  position.y >= minY &&
                                  position.y <= maxY;
            bool isSchoolFeeder = position.x == schoolRoadX &&
                                  position.y >= minY &&
                                  position.y <= maxY;
            bool isOfficeFeeder = position.x == officeRoadX &&
                                  position.y >= minY &&
                                  position.y <= maxY;

            if (isHorizontalRoad || isSourceFeeder || isSchoolFeeder || isOfficeFeeder)
            {
                return TileType.Road;
            }

            bool isHouseColumn = position.x == sourceRoadX - 1 || position.x == sourceRoadX + 1;
            if (isHouseColumn && position.y >= minY && position.y <= maxY && position.y != centerY)
            {
                return TileType.House;
            }

            bool isOfficeColumn = position.x == officeRoadX - 1 || position.x == officeRoadX + 1;
            if (isOfficeColumn && position.y >= minY && position.y <= maxY && position.y != centerY)
            {
                return TileType.Office;
            }

            bool isSchoolColumn = position.x == schoolRoadX - 1 || position.x == schoolRoadX + 1;
            if (isSchoolColumn && position.y == centerY + 3)
            {
                return TileType.School;
            }

            return TileType.Empty;
        }

        private static TilemapTestFacilityKind GetDenseDemandFacilityKind(TilemapTestField field, Vector2Int position, TileType simType)
        {
            if (simType == TileType.Road)
            {
                return TilemapTestFacilityKind.Road;
            }

            if (simType == TileType.House)
            {
                return TilemapTestFacilityKind.House;
            }

            if (simType == TileType.Office)
            {
                return TilemapTestFacilityKind.Office;
            }

            if (simType == TileType.School)
            {
                return TilemapTestFacilityKind.School;
            }

            int centerY = field.Height / 2;
            int shopRoadX = Mathf.Clamp(field.Width / 2 + 1, 1, field.Width - 2);
            bool isNearMainRoad = Mathf.Abs(position.y - centerY) == 1;
            bool isShopColumn = position.x == shopRoadX || position.x == shopRoadX + 1;

            if (isNearMainRoad && isShopColumn)
            {
                return TilemapTestFacilityKind.Shop;
            }

            return TilemapTestFacilityKind.None;
        }

        private static void ApplyDefaultFacilityData(TilemapTestField field, TilemapTestTile tile, System.Random random, bool randomizePopulation)
        {
            switch (tile.FacilityKind)
            {
                case TilemapTestFacilityKind.House:
                    int population = randomizePopulation
                        ? random.Next(field.MinHousePopulation, field.MaxHousePopulation + 1)
                        : Mathf.Clamp(4, field.MinHousePopulation, field.MaxHousePopulation);
                    SplitPopulation(field, random, population, out int workers, out int students, out int homebodies);
                    tile.ConfigureHousePopulation(population, workers, students, homebodies);
                    break;
                case TilemapTestFacilityKind.Office:
                    tile.ConfigureFacilityCapacity(TilemapTestFacilityKind.Office, random.Next(field.MinOfficeCapacity, field.MaxOfficeCapacity + 1));
                    break;
                case TilemapTestFacilityKind.School:
                    tile.ConfigureFacilityCapacity(TilemapTestFacilityKind.School, random.Next(field.MinSchoolCapacity, field.MaxSchoolCapacity + 1));
                    break;
                case TilemapTestFacilityKind.Shop:
                    tile.ConfigureFacilityCapacity(TilemapTestFacilityKind.Shop, random.Next(field.MinShopCapacity, field.MaxShopCapacity + 1));
                    break;
                default:
                    tile.ConfigureFacilityCapacity(tile.FacilityKind, 0);
                    break;
            }
        }

        private static void SplitPopulation(
            TilemapTestField field,
            System.Random random,
            int population,
            out int workers,
            out int students,
            out int homebodies)
        {
            workers = 0;
            students = 0;
            homebodies = 0;

            float workerWeight = field.WorkerPopulationWeight;
            float studentWeight = field.StudentPopulationWeight;
            float homebodyWeight = field.HomebodyPopulationWeight;
            float totalWeight = workerWeight + studentWeight + homebodyWeight;

            if (totalWeight <= 0f)
            {
                homebodies = population;
                return;
            }

            for (int i = 0; i < population; i++)
            {
                float roll = (float)random.NextDouble() * totalWeight;
                if (roll < workerWeight)
                {
                    workers++;
                    continue;
                }

                if (roll < workerWeight + studentWeight)
                {
                    students++;
                    continue;
                }

                homebodies++;
            }
        }

        private static bool IsExampleRoad(Vector2Int position)
        {
            return position.x % 4 == 2 || position.y % 4 == 2;
        }

        private static bool HasAdjacentExampleRoad(Vector2Int position)
        {
            return IsExampleRoad(position + Vector2Int.up) ||
                   IsExampleRoad(position + Vector2Int.down) ||
                   IsExampleRoad(position + Vector2Int.left) ||
                   IsExampleRoad(position + Vector2Int.right);
        }

        private static void ClearChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);
            }
        }
    }
}

/*
Unity implementation:
1. Select a GameObject with TilemapTestField in the Inspector.
2. Click Bake Test Field to create real 3D tile objects in the scene hierarchy.
3. Click Apply Roads Only Layout for a road-only field, Apply Example Layout for a balanced field, or Apply Dense Demand Layout for strong visible traffic.
4. Click Create Test Vehicle Prefab and Add Moving Trip Vehicle System to show vehicles traveling between buildings.
*/

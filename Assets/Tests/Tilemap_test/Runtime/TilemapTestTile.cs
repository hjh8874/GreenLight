using CityFlow.Contracts;
using System.Collections;
using UnityEngine;

namespace CityFlow.TilemapTest
{
    public enum TilemapTestFacilityKind
    {
        None,
        Road,
        House,
        Office,
        School,
        Shop
    }

    [RequireComponent(typeof(Renderer))]
    [RequireComponent(typeof(Collider))]
    public sealed class TilemapTestTile : MonoBehaviour
    {
        [SerializeField] private Vector2Int gridPosition;
        [SerializeField] private TileType tileType;
        [SerializeField] private TilemapTestFacilityKind facilityKind;
        [SerializeField] private int totalPopulation;
        [SerializeField] private int officeWorkers;
        [SerializeField] private int students;
        [SerializeField] private int homebodies;
        [SerializeField] private int maxCapacity;

        private Renderer cachedRenderer;
        private MaterialPropertyBlock propertyBlock;
        private Color currentColor = Color.white;
        private Coroutine warningFlashRoutine;

        public Vector2Int GridPosition { get; private set; }
        public TileType TileType { get; private set; }
        public TilemapTestFacilityKind FacilityKind { get; private set; }
        public int TotalPopulation { get; private set; }
        public int OfficeWorkers { get; private set; }
        public int Students { get; private set; }
        public int Homebodies { get; private set; }
        public int MaxCapacity { get; private set; }

        public void Configure(Vector2Int position, TileType type)
        {
            gridPosition = position;
            tileType = type;
            facilityKind = FacilityFromTileType(type);
            GridPosition = position;
            TileType = type;
            FacilityKind = facilityKind;
            name = BuildName();
        }

        public void SetTileType(TileType type)
        {
            tileType = type;
            facilityKind = FacilityFromTileType(type);
            TileType = type;
            FacilityKind = facilityKind;
            ClearFacilityStatsIfNeeded();
            name = BuildName();
        }

        public void SetFacilityKind(TilemapTestFacilityKind kind)
        {
            facilityKind = kind;
            FacilityKind = kind;
            name = BuildName();
        }

        public void ConfigureHousePopulation(int population, int workerCount, int studentCount, int homebodyCount)
        {
            totalPopulation = Mathf.Max(0, population);
            officeWorkers = Mathf.Clamp(workerCount, 0, totalPopulation);
            students = Mathf.Clamp(studentCount, 0, totalPopulation - officeWorkers);
            homebodies = Mathf.Clamp(homebodyCount, 0, totalPopulation - officeWorkers - students);

            int used = officeWorkers + students + homebodies;
            if (used < totalPopulation)
            {
                homebodies += totalPopulation - used;
            }

            maxCapacity = totalPopulation;
            SyncSerializedState();
        }

        public void ConfigureFacilityCapacity(TilemapTestFacilityKind kind, int capacity)
        {
            facilityKind = kind;
            maxCapacity = Mathf.Max(0, capacity);
            totalPopulation = 0;
            officeWorkers = 0;
            students = 0;
            homebodies = 0;
            SyncSerializedState();
            name = BuildName();
        }

        public void EnsureMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            EnsureRenderer();

            if (cachedRenderer.sharedMaterial != material)
            {
                cachedRenderer.sharedMaterial = material;
            }
        }

        public void ApplyColor(Color color)
        {
            currentColor = color;
            EnsureRenderer();
            cachedRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor("_BaseColor", color);
            propertyBlock.SetColor("_Color", color);
            cachedRenderer.SetPropertyBlock(propertyBlock);
        }

        public void FlashWarning(Color warningColor, float duration, float interval)
        {
            if (!gameObject.activeInHierarchy)
            {
                return;
            }

            if (warningFlashRoutine != null)
            {
                StopCoroutine(warningFlashRoutine);
            }

            warningFlashRoutine = StartCoroutine(FlashWarningRoutine(warningColor, Mathf.Max(0.1f, duration), Mathf.Max(0.05f, interval)));
        }

        private void Awake()
        {
            SyncSerializedState();
            EnsureRenderer();
        }

        private void OnValidate()
        {
            SyncSerializedState();
            name = BuildName();

            TilemapTestField field = GetComponentInParent<TilemapTestField>();
            if (field != null)
            {
                field.RefreshTile(this);
            }
        }

        private void SyncSerializedState()
        {
            GridPosition = gridPosition;
            TileType = tileType;
            FacilityKind = facilityKind;
            TotalPopulation = totalPopulation;
            OfficeWorkers = officeWorkers;
            Students = students;
            Homebodies = homebodies;
            MaxCapacity = maxCapacity;
        }

        private void ClearFacilityStatsIfNeeded()
        {
            if (facilityKind == TilemapTestFacilityKind.House)
            {
                return;
            }

            if (facilityKind == TilemapTestFacilityKind.Office ||
                facilityKind == TilemapTestFacilityKind.School ||
                facilityKind == TilemapTestFacilityKind.Shop)
            {
                totalPopulation = 0;
                officeWorkers = 0;
                students = 0;
                homebodies = 0;
                return;
            }

            totalPopulation = 0;
            officeWorkers = 0;
            students = 0;
            homebodies = 0;
            maxCapacity = 0;
        }

        private string BuildName()
        {
            return $"Tile_{gridPosition.x:00}_{gridPosition.y:00}_{facilityKind}";
        }

        private static TilemapTestFacilityKind FacilityFromTileType(TileType type)
        {
            switch (type)
            {
                case TileType.Road:
                    return TilemapTestFacilityKind.Road;
                case TileType.House:
                    return TilemapTestFacilityKind.House;
                case TileType.Office:
                    return TilemapTestFacilityKind.Office;
                case TileType.School:
                    return TilemapTestFacilityKind.School;
                default:
                    return TilemapTestFacilityKind.None;
            }
        }

        private void EnsureRenderer()
        {
            if (cachedRenderer == null)
            {
                cachedRenderer = GetComponent<Renderer>();
            }

            propertyBlock ??= new MaterialPropertyBlock();
        }

        private IEnumerator FlashWarningRoutine(Color warningColor, float duration, float interval)
        {
            Color restoreColor = currentColor;
            float elapsed = 0f;
            bool showWarning = true;

            while (elapsed < duration)
            {
                SetColorWithoutRemembering(showWarning ? warningColor : restoreColor);
                showWarning = !showWarning;
                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }

            ApplyColor(restoreColor);
            warningFlashRoutine = null;
        }

        private void SetColorWithoutRemembering(Color color)
        {
            EnsureRenderer();
            cachedRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor("_BaseColor", color);
            propertyBlock.SetColor("_Color", color);
            cachedRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}

/*
Unity implementation:
1. This component is added automatically by TilemapTestFieldEditor when baking test tiles.
2. Each tile stores one CityFlow grid coordinate and one TileType.
3. Keep this on generated tile objects so the baked hierarchy can be inspected and reused.
*/

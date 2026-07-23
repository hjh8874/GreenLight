using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CityFlow.UI.Controllers
{
    public sealed class FacilityInfluenceSelectionController : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private PlacementController placementController;
        [SerializeField] private BenefitHighlightRenderer highlightRenderer;
        [SerializeField] private PopulationConfigSO populationConfig;
        [SerializeField] private BuildingDefinitionSO hospitalDefinition;

        private readonly List<Vector2Int> areaTiles = new();
        private readonly List<Vector2Int> coveredHouses = new();
        private CityFlowServices services;
        private Vector2Int? selectedFacility;
        private TileType selectedFacilityType;
        private bool isSubscribed;

        public void Initialize(CityFlowServices cityFlowServices)
        {
            Unsubscribe();
            services = cityFlowServices;
            Subscribe();
        }

        private void Awake()
        {
            if (placementController == null)
            {
                placementController = GetComponent<PlacementController>();
            }

            if (highlightRenderer == null)
            {
                highlightRenderer = GetComponent<BenefitHighlightRenderer>();
            }
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ClearSelection();
        }

        private void Update()
        {
            if (services?.TileData == null ||
                placementController == null ||
                highlightRenderer == null)
            {
                return;
            }

            if (placementController.IsBuildingMode)
            {
                ClearSelectionState();
                return;
            }

            if (Mouse.current == null ||
                !Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            Vector2Int clicked = placementController.GetMouseGridCoordinate();
            if (!GridUtil.IsInside(clicked))
            {
                ClearSelection();
                return;
            }

            if (services.TileData.TryGetFootprintAnchor(clicked, out Vector2Int anchor))
            {
                clicked = anchor;
            }

            TileType type = services.TileData.GetTileType(clicked);
            int radius = type switch
            {
                TileType.School when populationConfig != null => populationConfig.SchoolCoverageRadius,
                TileType.Hospital when hospitalDefinition != null => hospitalDefinition.HospitalCoverageRadius,
                _ => 0
            };

            if (radius <= 0)
            {
                ClearSelection();
                return;
            }

            selectedFacility = clicked;
            selectedFacilityType = type;
            BuildInfluenceTiles(clicked, type, radius);
            highlightRenderer.ShowHighlights(areaTiles, coveredHouses, useXYPlane: true);
        }

        private void Subscribe()
        {
            if (isSubscribed || services?.Events == null)
            {
                return;
            }

            services.Events.Placed += OnPlaced;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed || services?.Events == null)
            {
                return;
            }

            services.Events.Placed -= OnPlaced;
            isSubscribed = false;
        }

        private void OnPlaced(PlacedEvent placedEvent)
        {
            if (!selectedFacility.HasValue ||
                highlightRenderer == null ||
                services?.TileData == null)
            {
                return;
            }

            if (placementController != null &&
                placementController.IsBuildingMode)
            {
                ClearSelectionState();
                return;
            }

            Vector2Int facility = selectedFacility.Value;
            if (placedEvent.IsRemove &&
                placedEvent.Tile == facility &&
                placedEvent.Type == selectedFacilityType)
            {
                ClearSelection();
                return;
            }

            int radius = selectedFacilityType switch
            {
                TileType.School when populationConfig != null =>
                    populationConfig.SchoolCoverageRadius,
                TileType.Hospital when hospitalDefinition != null =>
                    hospitalDefinition.HospitalCoverageRadius,
                _ => 0
            };

            if (radius <= 0 ||
                services.TileData.GetTileType(facility) !=
                    selectedFacilityType)
            {
                ClearSelection();
                return;
            }

            BuildInfluenceTiles(
                facility,
                selectedFacilityType,
                radius
            );
            highlightRenderer.ShowHighlights(
                areaTiles,
                coveredHouses,
                useXYPlane: true
            );
        }

        private void ClearSelection()
        {
            ClearSelectionState();
            highlightRenderer?.HideAll();
        }

        private void ClearSelectionState()
        {
            selectedFacility = null;
            selectedFacilityType = TileType.Empty;
        }

        private void BuildInfluenceTiles(Vector2Int facility, TileType type, int radius)
        {
            areaTiles.Clear();
            coveredHouses.Clear();

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    Vector2Int tile = facility + new Vector2Int(dx, dy);
                    if (!GridUtil.IsInside(tile))
                    {
                        continue;
                    }

                    bool isCovered = type == TileType.School
                        ? PopulationCalculator.IsWithinSchoolCoverage(tile, facility, radius)
                        : HospitalEffectCalculator.IsWithinHospitalCoverage(tile, facility, radius);
                    if (!isCovered)
                    {
                        continue;
                    }

                    areaTiles.Add(tile);
                    if (services.TileData.GetTileType(tile) == TileType.House)
                    {
                        coveredHouses.Add(tile);
                    }
                }
            }
        }
    }
}

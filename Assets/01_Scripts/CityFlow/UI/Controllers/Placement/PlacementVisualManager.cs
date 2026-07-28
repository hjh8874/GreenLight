using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using CityFlow.Contracts;
using CityFlow.Bootstrap;

namespace CityFlow.UI.Controllers.Placement
{
    internal class PlacementVisualManager
    {
        public const int GhostSortingOrder = 100;
        public const float MinimumGhostAlpha = 0.75f;

        private readonly SpriteRenderer _ghostRenderer;
        private readonly Color _colorValid;
        private readonly Color _colorInvalid;

        private readonly bool _use3DGhostVolume;
        private readonly float _ghostVolumeHeight;
        private readonly Color _volumeValidColor;
        private readonly Color _volumeInvalidColor;

        private GameObject _ghostVolumeObj;
        private Material _ghostVolumeMaterial;

        private Texture2D _footprintGhostTexture;
        private Sprite _footprintGhostSprite;

        private Vector3 _ghostBaseScale = Vector3.one;
        private bool _ghostScaleInitialized;

        private readonly BenefitHighlightRenderer _benefitRenderer;
        private readonly CityFlow.Content.PopulationConfigSO _populationConfig;
        private readonly CityFlow.Content.BuildingDefinitionSO _hospitalDefinition;

        private Vector2Int? _lastPreviewCoord = null;
        private readonly List<Vector2Int> _benefitTileBuffer = new List<Vector2Int>(32);
        private readonly List<Vector2Int> _areaTileBuffer = new List<Vector2Int>(128);

        public PlacementVisualManager(
            SpriteRenderer ghostRenderer, Color colorValid, Color colorInvalid,
            bool use3DGhostVolume, float ghostVolumeHeight, Color volumeValidColor, Color volumeInvalidColor,
            BenefitHighlightRenderer benefitRenderer,
            CityFlow.Content.PopulationConfigSO populationConfig,
            CityFlow.Content.BuildingDefinitionSO hospitalDefinition)
        {
            _ghostRenderer = ghostRenderer;
            _colorValid = colorValid;
            _colorInvalid = colorInvalid;
            _use3DGhostVolume = use3DGhostVolume;
            _ghostVolumeHeight = ghostVolumeHeight;
            _volumeValidColor = volumeValidColor;
            _volumeInvalidColor = volumeInvalidColor;
            _benefitRenderer = benefitRenderer;
            _populationConfig = populationConfig;
            _hospitalDefinition = hospitalDefinition;
        }

        public void Initialize()
        {
            if (_ghostRenderer == null) return;

            if (!_ghostScaleInitialized)
            {
                _ghostBaseScale = _ghostRenderer.transform.localScale;
                _ghostScaleInitialized = true;
            }

            _ghostRenderer.sortingOrder = Mathf.Max(_ghostRenderer.sortingOrder, GhostSortingOrder);
            CreateGhostVolume();
        }

        public void Cleanup()
        {
            SafeDestroy(_footprintGhostSprite);
            SafeDestroy(_footprintGhostTexture);
            SafeDestroy(_ghostVolumeMaterial);
            SafeDestroy(_ghostVolumeObj);
        }

        private void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        public void SetGhostActive(bool active)
        {
            if (_ghostRenderer != null) _ghostRenderer.gameObject.SetActive(active);
            if (_ghostVolumeObj != null && _ghostVolumeObj.activeSelf != active)
                _ghostVolumeObj.SetActive(active);
        }

        public void HideBenefitHighlights()
        {
            _lastPreviewCoord = null;
            if (_benefitRenderer != null) _benefitRenderer.HideAll();
        }

        public void UpdateGhostSprite(TileType currentType, CityFlow.Configs.TileDataSO[] availableTiles)
        {
            if (_ghostRenderer == null) return;

            CityFlow.Configs.TileDataSO selectedTile = null;
            if (availableTiles != null)
            {
                foreach (var tile in availableTiles)
                {
                    if (tile != null && tile.Category == currentType)
                    {
                        selectedTile = tile;
                        break;
                    }
                }
            }

            _ghostRenderer.sprite = selectedTile != null && selectedTile.BuildingIcon != null
                ? selectedTile.BuildingIcon
                : GetOrCreateFootprintGhostSprite();
        }

        public void UpdateGhostFootprint(TileType currentType, PlacementDirection direction)
        {
            if (_ghostRenderer == null || !_ghostScaleInitialized) return;

            Vector2Int footprintSize = TileFootprint.GetRotatedSize(currentType, direction);
            Vector2Int size = new Vector2Int(
                Mathf.Max(1, footprintSize.x),
                Mathf.Max(1, footprintSize.y)
            );
            _ghostRenderer.transform.localScale = new Vector3(
                _ghostBaseScale.x * size.x,
                _ghostBaseScale.y * size.y,
                _ghostBaseScale.z);

            UpdateGhostVolumeScale(size);
        }

        public void SetGhostFootprint(Vector2Int size)
        {
            if (_ghostRenderer == null || !_ghostScaleInitialized) return;

            _ghostRenderer.transform.localScale = new Vector3(
                _ghostBaseScale.x * size.x,
                _ghostBaseScale.y * size.y,
                _ghostBaseScale.z);

            UpdateGhostVolumeScale(size);
        }

        public void SyncGhostPosition(
            Vector3 position,
            float angle,
            bool useXYPlane,
            IWorldCoordinateSpace coordinateSpace = null)
        {
            if (_ghostRenderer == null) return;
            _ghostRenderer.transform.position = position;

            if (coordinateSpace != null)
            {
                _ghostRenderer.transform.rotation =
                    coordinateSpace.CoordinateRotation;
            }

            if (_ghostVolumeObj != null)
            {
                if (coordinateSpace != null)
                {
                    Quaternion surfaceRotation = Quaternion.LookRotation(
                        coordinateSpace.GridYAxis,
                        coordinateSpace.GroundNormal);
                    _ghostVolumeObj.transform.position =
                        position +
                        coordinateSpace.GroundNormal *
                        (_ghostVolumeHeight * 0.5f);
                    _ghostVolumeObj.transform.rotation =
                        Quaternion.AngleAxis(
                            angle,
                            coordinateSpace.GroundNormal) *
                        surfaceRotation;
                }
                else if (useXYPlane)
                {
                    _ghostVolumeObj.transform.position = new Vector3(
                        position.x, position.y, position.z - _ghostVolumeHeight * 0.5f);
                    _ghostVolumeObj.transform.rotation = Quaternion.Euler(0f, 0f, -angle);
                }
                else
                {
                    _ghostVolumeObj.transform.position = new Vector3(
                        position.x, _ghostVolumeHeight * 0.5f, position.z);
                    _ghostVolumeObj.transform.rotation = Quaternion.Euler(0f, angle, 0f);
                }
            }
        }

        public void UpdateColors(bool canPlace)
        {
            if (_ghostRenderer != null)
            {
                Color ghostColor = canPlace ? _colorValid : _colorInvalid;
                ghostColor.a = Mathf.Max(ghostColor.a, MinimumGhostAlpha);
                _ghostRenderer.color = ghostColor;
            }

            if (_ghostVolumeMaterial != null)
            {
                _ghostVolumeMaterial.color = canPlace ? _volumeValidColor : _volumeInvalidColor;
            }
        }

        public void UpdateBenefitPreview(Vector2Int gridCoord, TileType currentType, bool useXYPlane, CityFlowServices services)
        {
            bool isSchool = currentType == TileType.School;
            bool isHospital = currentType == TileType.Hospital;

            if (!isSchool && !isHospital)
            {
                if (_lastPreviewCoord != null)
                {
                    HideBenefitHighlights();
                }
                return;
            }

            if (_lastPreviewCoord.HasValue && _lastPreviewCoord.Value == gridCoord)
            {
                return;
            }

            _lastPreviewCoord = gridCoord;
            _benefitTileBuffer.Clear();
            _areaTileBuffer.Clear();

            int radius = isSchool
                ? (_populationConfig != null ? _populationConfig.SchoolCoverageRadius : 0)
                : (_hospitalDefinition != null ? _hospitalDefinition.HospitalCoverageRadius : 0);

            if (services != null && services.TileData != null && radius > 0)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        Vector2Int targetTile = new Vector2Int(gridCoord.x + dx, gridCoord.y + dy);
                        bool isAccessible = services?.WorldGrid != null
                            ? services.WorldGrid.IsTileUnlocked(targetTile)
                            : GridUtil.IsInside(targetTile);
                        if (!isAccessible) continue;

                        bool isCovered = isSchool
                            ? CityFlow.Content.PopulationCalculator.IsWithinSchoolCoverage(targetTile, gridCoord, radius)
                            : CityFlow.Content.HospitalEffectCalculator.IsWithinHospitalCoverage(targetTile, gridCoord, radius);

                        if (!isCovered) continue;

                        _areaTileBuffer.Add(targetTile);

                        if (services.TileData.GetTileType(targetTile) == TileType.House)
                        {
                            _benefitTileBuffer.Add(targetTile);
                        }
                    }
                }
            }

            if (_benefitRenderer != null)
            {
                _benefitRenderer.ShowHighlights(
                    _areaTileBuffer,
                    _benefitTileBuffer,
                    useXYPlane,
                    isHospital,
                    services?.WorldCoordinates);
            }
        }

        private void CreateGhostVolume()
        {
            if (!_use3DGhostVolume || _ghostVolumeObj != null) return;

            _ghostVolumeObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ghostVolumeObj.name = "GhostVolume";
            _ghostVolumeObj.hideFlags = HideFlags.HideAndDontSave;

            var collider = _ghostVolumeObj.GetComponent<Collider>();
            SafeDestroy(collider);

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _ghostVolumeMaterial = new Material(shader)
            {
                name = "GhostVolumeMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };

            _ghostVolumeMaterial.SetFloat("_Surface", 1f);
            _ghostVolumeMaterial.SetFloat("_Blend", 0f);
            _ghostVolumeMaterial.SetFloat("_AlphaClip", 0f);
            _ghostVolumeMaterial.SetOverrideTag("RenderType", "Transparent");
            _ghostVolumeMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _ghostVolumeMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _ghostVolumeMaterial.SetInt("_ZWrite", 0);
            _ghostVolumeMaterial.renderQueue = (int)RenderQueue.Transparent;
            _ghostVolumeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _ghostVolumeMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            _ghostVolumeMaterial.color = _volumeValidColor;

            var meshRenderer = _ghostVolumeObj.GetComponent<MeshRenderer>();
            meshRenderer.material = _ghostVolumeMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            _ghostVolumeObj.SetActive(false);
        }

        private void UpdateGhostVolumeScale(Vector2Int size)
        {
            if (_ghostVolumeObj == null) return;
            _ghostVolumeObj.transform.localScale = new Vector3(
                Mathf.Max(1, size.x),
                _ghostVolumeHeight,
                Mathf.Max(1, size.y)
            );
        }

        private Sprite GetOrCreateFootprintGhostSprite()
        {
            if (_footprintGhostSprite != null) return _footprintGhostSprite;

            _footprintGhostTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "PlacementFootprintGhostTexture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            _footprintGhostTexture.SetPixel(0, 0, Color.white);
            _footprintGhostTexture.Apply();

            _footprintGhostSprite = Sprite.Create(
                _footprintGhostTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
            _footprintGhostSprite.name = "PlacementFootprintGhostSprite";
            _footprintGhostSprite.hideFlags = HideFlags.HideAndDontSave;
            return _footprintGhostSprite;
        }
    }
}

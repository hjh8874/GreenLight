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
        public const float RoadPreviewThickness = 0.08f;

        private readonly SpriteRenderer _ghostRenderer;
        private readonly Color _colorValid;
        private readonly Color _colorInvalid;

        private readonly bool _use3DGhostVolume;
        private readonly float _ghostVolumeHeight;
        private float _currentGhostVolumeHeight;
        private readonly Color _volumeValidColor;
        private readonly Color _volumeInvalidColor;

        private GameObject _ghostVolumeObj;
        private Material _ghostVolumeMaterial;
        private GameObject _buildingPreviewObject;
        private Material _buildingPreviewMaterial;
        private Renderer[] _buildingPreviewRenderers =
            System.Array.Empty<Renderer>();
        private bool _ghostActive;

        private Texture2D _footprintGhostTexture;
        private Sprite _footprintGhostSprite;

        private Vector3 _ghostBaseScale = Vector3.one;
        private bool _ghostScaleInitialized;

        private readonly BenefitHighlightRenderer _benefitRenderer;
        private readonly CityFlow.Content.PopulationConfigSO _populationConfig;
        private readonly CityFlow.Content.BuildingDefinitionSO _hospitalDefinition;
        private readonly Transform _previewParent;

        private Vector2Int? _lastPreviewCoord = null;
        private readonly List<Vector2Int> _benefitTileBuffer = new List<Vector2Int>(32);
        private readonly List<Vector2Int> _areaTileBuffer = new List<Vector2Int>(128);

        public PlacementVisualManager(
            SpriteRenderer ghostRenderer, Color colorValid, Color colorInvalid,
            bool use3DGhostVolume, float ghostVolumeHeight, Color volumeValidColor, Color volumeInvalidColor,
            BenefitHighlightRenderer benefitRenderer,
            CityFlow.Content.PopulationConfigSO populationConfig,
            CityFlow.Content.BuildingDefinitionSO hospitalDefinition,
            Transform previewParent = null)
        {
            _ghostRenderer = ghostRenderer;
            _colorValid = colorValid;
            _colorInvalid = colorInvalid;
            _use3DGhostVolume = use3DGhostVolume;
            _ghostVolumeHeight = ghostVolumeHeight;
            _currentGhostVolumeHeight = ghostVolumeHeight;
            _volumeValidColor = volumeValidColor;
            _volumeInvalidColor = volumeInvalidColor;
            _benefitRenderer = benefitRenderer;
            _populationConfig = populationConfig;
            _hospitalDefinition = hospitalDefinition;
            _previewParent = previewParent;
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
            ClearBuildingPreview();
            SafeDestroy(_footprintGhostSprite);
            SafeDestroy(_footprintGhostTexture);
            SafeDestroy(_buildingPreviewMaterial);
            SafeDestroy(_ghostVolumeMaterial);
            SafeDestroy(_ghostVolumeObj);
        }

        private void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            bool isTemporaryEditorObject =
                Application.isEditor &&
                (obj.hideFlags & HideFlags.DontSave) != 0;
            if (!Application.isPlaying || isTemporaryEditorObject)
                UnityEngine.Object.DestroyImmediate(obj);
            else
                UnityEngine.Object.Destroy(obj);
        }

        public void SetGhostActive(bool active)
        {
            _ghostActive = active;
            bool footprintGhostActive =
                active &&
                _buildingPreviewObject == null;
            if (_ghostRenderer != null)
            {
                _ghostRenderer.gameObject.SetActive(
                    footprintGhostActive);
            }
            bool volumeActive =
                active &&
                _buildingPreviewObject == null;
            if (_ghostVolumeObj != null &&
                _ghostVolumeObj.activeSelf != volumeActive)
            {
                _ghostVolumeObj.SetActive(volumeActive);
            }
            if (_buildingPreviewObject != null &&
                _buildingPreviewObject.activeSelf != active)
            {
                _buildingPreviewObject.SetActive(active);
            }
        }

        public void SetBuildingPreview(GameObject preview)
        {
            ClearBuildingPreview();
            _buildingPreviewObject = preview;
            if (_buildingPreviewObject == null)
            {
                if (_ghostVolumeObj != null)
                {
                    _ghostVolumeObj.SetActive(_ghostActive);
                }
                return;
            }

            _buildingPreviewObject.hideFlags =
                HideFlags.HideAndDontSave;
            if (_previewParent != null)
            {
                _buildingPreviewObject.transform.SetParent(
                    _previewParent,
                    true);
            }
            Material previewMaterial =
                GetOrCreateBuildingPreviewMaterial();
            _buildingPreviewRenderers =
                ConfigurePreviewObject(
                    _buildingPreviewObject,
                    previewMaterial,
                    preserveSourceMaterials: true);

            UpdateBuildingPreviewColor(canPlace: true);
            _buildingPreviewObject.SetActive(_ghostActive);
            if (_ghostVolumeObj != null)
            {
                _ghostVolumeObj.SetActive(false);
            }
        }

        public void HideBenefitHighlights()
        {
            _lastPreviewCoord = null;
            if (_benefitRenderer != null) _benefitRenderer.HideAll();
        }

        public void UpdateGhostSprite(
            TileType currentType,
            CityFlow.Configs.TileDataSO[] availableTiles,
            Sprite overrideSprite = null)
        {
            if (_ghostRenderer == null) return;

            if (overrideSprite != null)
            {
                _ghostRenderer.sprite = overrideSprite;
                return;
            }

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
            _currentGhostVolumeHeight =
                currentType == TileType.Road
                    ? RoadPreviewThickness
                    : _ghostVolumeHeight;
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
            IWorldCoordinateSpace coordinateSpace = null,
            Vector3? buildingPreviewPosition = null,
            Quaternion? buildingPreviewRotation = null)
        {
            if (_ghostRenderer != null)
            {
                _ghostRenderer.transform.position = position;
            }

            if (_ghostVolumeObj != null)
            {
                if (coordinateSpace != null)
                {
                    _ghostVolumeObj.transform.position =
                        position +
                        coordinateSpace.GroundNormal *
                        (_currentGhostVolumeHeight * 0.5f);
                }
                else if (useXYPlane)
                {
                    _ghostVolumeObj.transform.position = new Vector3(
                        position.x,
                        position.y,
                        position.z -
                        _currentGhostVolumeHeight * 0.5f);
                }
                else
                {
                    _ghostVolumeObj.transform.position = new Vector3(
                        position.x,
                        _currentGhostVolumeHeight * 0.5f,
                        position.z);
                }
            }

            if (_buildingPreviewObject != null)
            {
                _buildingPreviewObject.transform.position =
                    buildingPreviewPosition ?? position;
            }

            SyncPlacementRotation(
                angle,
                useXYPlane,
                coordinateSpace,
                buildingPreviewRotation);
        }

        public void SyncPlacementRotation(
            float angle,
            bool useXYPlane,
            IWorldCoordinateSpace coordinateSpace = null,
            Quaternion? buildingPreviewRotation = null)
        {
            Quaternion placementRotation =
                GetPlacementRotation(
                    angle,
                    useXYPlane,
                    coordinateSpace);
            if (_ghostRenderer != null)
            {
                _ghostRenderer.transform.rotation =
                    placementRotation;
            }

            if (_buildingPreviewObject != null)
            {
                _buildingPreviewObject.transform.rotation =
                    buildingPreviewRotation ??
                    placementRotation;
            }

            if (_ghostVolumeObj == null)
            {
                return;
            }

            if (coordinateSpace != null)
            {
                Quaternion surfaceRotation =
                    Quaternion.LookRotation(
                        coordinateSpace.GridYAxis,
                        coordinateSpace.GroundNormal);
                _ghostVolumeObj.transform.rotation =
                    Quaternion.AngleAxis(
                        angle,
                        coordinateSpace.GroundNormal) *
                    surfaceRotation;
            }
            else if (useXYPlane)
            {
                _ghostVolumeObj.transform.rotation =
                    Quaternion.Euler(0f, 0f, -angle);
            }
            else
            {
                _ghostVolumeObj.transform.rotation =
                    Quaternion.Euler(0f, angle, 0f);
            }
        }

        public void UpdateColors(bool canPlace)
        {
            if (_ghostRenderer != null)
            {
                Color ghostColor = canPlace ? _colorValid : _colorInvalid;
                ghostColor.a = 1f;
                _ghostRenderer.color = ghostColor;
            }

            if (_ghostVolumeMaterial != null)
            {
                Color volumeColor =
                    canPlace ? _volumeValidColor : _volumeInvalidColor;
                volumeColor.a = 1f;
                SetPreviewMaterialColor(
                    _ghostVolumeMaterial,
                    volumeColor);
            }

            UpdateBuildingPreviewColor(canPlace);
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

            _ghostVolumeMaterial =
                CreateLightingIndependentPreviewMaterial(
                    "GhostVolumeMaterial");
            Color volumeColor = _volumeValidColor;
            volumeColor.a = 1f;
            SetPreviewMaterialColor(
                _ghostVolumeMaterial,
                volumeColor);

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
                _currentGhostVolumeHeight,
                Mathf.Max(1, size.y)
            );
        }

        private void UpdateBuildingPreviewColor(bool canPlace)
        {
            Color color =
                canPlace ? _colorValid : _colorInvalid;
            color.a = 1f;

            if (_buildingPreviewMaterial != null)
            {
                ApplyPreviewColor(
                    _buildingPreviewMaterial,
                    _buildingPreviewRenderers,
                    color);
                return;
            }

            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            for (int index = 0;
                 index < _buildingPreviewRenderers.Length;
                 index++)
            {
                _buildingPreviewRenderers[index]
                    .SetPropertyBlock(properties);
            }
        }

        private static Quaternion GetPlacementRotation(
            float angle,
            bool useXYPlane,
            IWorldCoordinateSpace coordinateSpace)
        {
            if (coordinateSpace != null)
            {
                return coordinateSpace.CoordinateRotation *
                       Quaternion.Euler(0f, 0f, angle);
            }

            return useXYPlane
                ? Quaternion.Euler(0f, 0f, -angle)
                : Quaternion.Euler(90f, 0f, 0f) *
                  Quaternion.Euler(0f, 0f, angle);
        }

        private Material GetOrCreateBuildingPreviewMaterial()
        {
            if (_buildingPreviewMaterial != null)
            {
                return _buildingPreviewMaterial;
            }

            _buildingPreviewMaterial =
                CreateLightingIndependentPreviewMaterial(
                    "BuildingPlacementPreviewMaterial");
            return _buildingPreviewMaterial;
        }

        internal static Renderer[] ConfigurePreviewObject(
            GameObject preview,
            Material previewMaterial,
            bool preserveSourceMaterials = false)
        {
            if (preview == null)
            {
                return System.Array.Empty<Renderer>();
            }

            preview.hideFlags =
                HideFlags.HideAndDontSave;
            Collider[] colliders =
                preview.GetComponentsInChildren<
                    Collider>(true);
            for (int index = 0;
                 index < colliders.Length;
                 index++)
            {
                colliders[index].enabled = false;
            }

            MonoBehaviour[] behaviours =
                preview.GetComponentsInChildren<
                    MonoBehaviour>(true);
            for (int index = 0;
                 index < behaviours.Length;
                 index++)
            {
                behaviours[index].enabled = false;
            }

            Renderer[] renderers =
                preview.GetComponentsInChildren<
                    Renderer>(true);
            for (int index = 0;
                 index < renderers.Length;
                 index++)
            {
                Renderer renderer = renderers[index];
                renderer.enabled = true;
                renderer.forceRenderingOff = false;
                renderer.allowOcclusionWhenDynamic =
                    false;
                renderer.shadowCastingMode =
                    ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                Material[] materials =
                    renderer.sharedMaterials;
                if (preserveSourceMaterials &&
                    AreMaterialsUsable(materials))
                {
                    continue;
                }

                if (materials.Length == 0)
                {
                    materials =
                        new[] { previewMaterial };
                }
                else
                {
                    for (int materialIndex = 0;
                         materialIndex < materials.Length;
                         materialIndex++)
                    {
                        materials[materialIndex] =
                            previewMaterial;
                    }
                }
                renderer.sharedMaterials = materials;
            }

            return renderers;
        }

        private static bool AreMaterialsUsable(
            Material[] materials)
        {
            if (materials == null || materials.Length == 0)
            {
                return false;
            }

            for (int index = 0; index < materials.Length; index++)
            {
                Material material = materials[index];
                Shader shader = material != null
                    ? material.shader
                    : null;
                if (shader == null ||
                    !shader.isSupported ||
                    shader.name.Contains("InternalErrorShader"))
                {
                    return false;
                }
            }

            return true;
        }

        internal static void ApplyPreviewColor(
            Material material,
            Renderer[] renderers,
            Color color)
        {
            color.a = 1f;
            SetPreviewMaterialColor(material, color);

            var properties =
                new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            for (int index = 0;
                 index < renderers.Length;
                 index++)
            {
                renderers[index].SetPropertyBlock(
                    properties);
            }
        }

        internal static Material
            CreateLightingIndependentPreviewMaterial(
                string materialName)
        {
            Shader shader =
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Sprites/Default");
            var material = new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetOverrideTag("RenderType", "Opaque");
            material.SetInt("_SrcBlend", (int)BlendMode.One);
            material.SetInt("_DstBlend", (int)BlendMode.Zero);
            material.SetInt("_ZWrite", 1);
            material.renderQueue = (int)RenderQueue.Geometry;
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            return material;
        }

        internal static void SetPreviewMaterialColor(
            Material material,
            Color color)
        {
            material.color = color;
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
        }

        private void ClearBuildingPreview()
        {
            SafeDestroy(_buildingPreviewObject);
            _buildingPreviewObject = null;
            _buildingPreviewRenderers =
                System.Array.Empty<Renderer>();
        }

        internal GameObject BuildingPreviewObject =>
            _buildingPreviewObject;

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

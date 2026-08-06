using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using CityFlow.Contracts;
using CityFlow.UI.Controllers;
using CityFlow.Bootstrap;
using CityFlow.View;

namespace CityFlow.UI
{
    public class TileSelectionController : MonoBehaviour, ICityFlowServiceConsumer
    {
        private CityFlowServices _services;

        public void Initialize(CityFlowServices services)
        {
            _services = services;
        }
        [Header("References")]
        [SerializeField] private AnalysisCardController analysisCard;
        [SerializeField] private BuildingInfoCardController buildingInfoCard;
        [SerializeField] private PlacementController placementController;
        private InfrastructurePlacementCoordinator _infraCoordinator;
        private MainCityView _mainCityView;

        private float _searchTimer = 0f;
        private Vector2Int? _lastHoveredBuildingCoord;
        private Vector2Int? _selectedCoord;

        [Header("Visuals")]
        [Tooltip("타일을 선택했을 때 바닥에 표시될 강조(하이라이트) 박스")]
        [SerializeField] private GameObject highlightBox; 
        [SerializeField] private bool useXYPlane = false;
        private readonly List<GameObject> _selectionOverlayObjects =
            new();
        private Material _selectionOverlayMaterial;

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");
        private const float SelectionOverlayAlpha = 0.08f;

        public void Configure(
            AnalysisCardController analysis,
            PlacementController placement,
            GameObject highlight)
        {
            analysisCard = analysis;
            placementController = placement;
            ClearSelectedVisual();
            highlightBox = highlight;
            DisableLegacyHighlight();
        }

        public void SetUseXYPlane(bool isOn)
        {
            useXYPlane = isOn;
        }

        private void Start()
        {
            if (placementController == null)
            {
                placementController = FindAnyObjectByType<PlacementController>(FindObjectsInactive.Include);
            }
            if (_infraCoordinator == null)
            {
                _infraCoordinator = FindAnyObjectByType<InfrastructurePlacementCoordinator>(FindObjectsInactive.Include);
            }
            if (buildingInfoCard == null)
            {
                buildingInfoCard = FindAnyObjectByType<BuildingInfoCardController>(FindObjectsInactive.Include);
            }
            // 시작 시 상세 카드와 하이라이트 박스는 숨겨둡니다.
            DisableLegacyHighlight();
            DeselectTile();
        }

        private void OnDestroy()
        {
            ClearSelectedVisual();
        }

        private void Update()
        {
            ClearSelectionIfRemoved();

            if (WeeklySettlementPopup.IsInteractionBlocked)
            {
                return;
            }

            // 동적 생성되는 컨트롤러들을 위해 Update에서 지연 검색 지원 (최적화: 0.5초 스로틀링)
            if (placementController == null || _infraCoordinator == null)
            {
                _searchTimer += Time.deltaTime;
                if (_searchTimer >= 0.5f)
                {
                    _searchTimer = 0f;
                    if (placementController == null)
                    {
                        placementController = FindAnyObjectByType<PlacementController>(FindObjectsInactive.Include);
                    }
                    if (_infraCoordinator == null)
                    {
                        _infraCoordinator = FindAnyObjectByType<InfrastructurePlacementCoordinator>(FindObjectsInactive.Include);
                    }
                }
            }

            bool isBuilding = (placementController != null && placementController.IsBuildingMode) || 
                              (_infraCoordinator != null && _infraCoordinator.IsBuildingMode);

            if (isBuilding)
            {
                DeselectTile(); // 건설 모드 켜지면 분석 카드도 바로 닫음
                return;
            }

            // --- 2. Hover 감지 (방치형 건물 정보 팝업용) ---
            HandleHover();

            // 3. 마우스 좌클릭 감지 (New Input System)
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                // UI(버튼 등) 위를 클릭했다면 무시
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    return;

                Vector2Int? gridCoord = TryGetGridCoordinate();
                
                if (gridCoord.HasValue && IsAccessible(gridCoord.Value))
                {
                    bool isEmpty = false;
                    if (_services != null && _services.TileData != null)
                    {
                        if (_services.TileData.TryGetFootprintAnchor(gridCoord.Value, out Vector2Int anchor))
                        {
                            gridCoord = anchor;
                        }

                        isEmpty = _services.TileData.GetTileType(gridCoord.Value) == TileType.Empty;
                    }

                    if (!isEmpty)
                    {
                        SelectTile(gridCoord.Value);
                    }
                    else
                    {
                        DeselectTile();
                    }
                }
                else
                {
                    DeselectTile(); // 맵 바깥이나 허공 클릭 시 해제
                }
            }
        }

        private void HandleHover()
        {
            if (buildingInfoCard == null) return;

            if (TryGetSelectedBuilding(
                    out Vector2Int selectedBuilding,
                    out TileType selectedType))
            {
                ShowBuildingInfoCard(
                    selectedBuilding,
                    selectedType);
                return;
            }

            // UI 위에 있으면 호버 해제
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                HideBuildingInfoCard();
                return;
            }

            Vector2Int? gridCoord = TryGetGridCoordinate();
            if (gridCoord.HasValue && IsAccessible(gridCoord.Value))
            {
                Vector2Int coord = gridCoord.Value;
                if (_services != null && _services.TileData != null)
                {
                    if (_services.TileData.TryGetFootprintAnchor(coord, out Vector2Int anchor))
                    {
                        coord = anchor;
                    }

                    TileType type = _services.TileData.GetTileType(coord);
                    if (TileFootprint.IsBuilding(type))
                    {
                        // 새로운 건물 위에 올라갔을 때만 연다 (중복 호출 방지)
                        ShowBuildingInfoCard(coord, type);
                        return;
                    }
                }
            }

            // 건물이 아닌 곳이거나 허공이면 닫기
            HideBuildingInfoCard();
        }

        private bool TryGetSelectedBuilding(
            out Vector2Int coord,
            out TileType type)
        {
            coord = default;
            type = TileType.Empty;
            if (!_selectedCoord.HasValue ||
                _services?.TileData == null)
            {
                return false;
            }

            coord = _selectedCoord.Value;
            type = _services.TileData.GetTileType(coord);
            return TileFootprint.IsBuilding(type);
        }

        private void ShowBuildingInfoCard(
            Vector2Int coord,
            TileType type)
        {
            if (buildingInfoCard == null)
            {
                return;
            }

            if (_lastHoveredBuildingCoord != coord ||
                !buildingInfoCard.IsOpen)
            {
                buildingInfoCard.OpenCard(coord, type);
            }

            _lastHoveredBuildingCoord = coord;
        }

        private void HideBuildingInfoCard()
        {
            if (buildingInfoCard != null &&
                buildingInfoCard.IsOpen)
            {
                buildingInfoCard.CloseCard();
            }

            _lastHoveredBuildingCoord = null;
        }

        private Vector2Int? TryGetGridCoordinate()
        {
            Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

            if (Camera.main == null)
            {
                return null;
            }

            Ray ray = Camera.main.ScreenPointToRay(mousePos);
            if (_services?.WorldCoordinates != null)
            {
                return _services.WorldCoordinates.TryRayToGrid(
                    ray,
                    out Vector2Int tile,
                    out _)
                    ? tile
                    : null;
            }

            if (useXYPlane)
            {
                Plane xyPlane = new Plane(Vector3.forward, Vector3.zero);
                if (xyPlane.Raycast(ray, out float xyEnter))
                {
                    Vector3 worldPos = ray.GetPoint(xyEnter);
                    return new Vector2Int(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.y));
                }

                return null;
            }

            Plane groundPlane = new Plane(Vector3.up, Vector3.zero); // Y=0 바닥
            
            if (groundPlane.Raycast(ray, out float enter))
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                return new Vector2Int(Mathf.RoundToInt(hitPoint.x), Mathf.RoundToInt(hitPoint.z));
            }
            return null;
        }

        private bool IsAccessible(Vector2Int tile)
        {
            return _services?.WorldGrid != null
                ? _services.WorldGrid.IsTileUnlocked(tile)
                : GridUtil.IsInside(tile);
        }

        /// <summary>
        /// 외부에서 특정 타일을 선택시킨다(아이들북 피드의 글 클릭 등).
        /// 하이라이트 표시와 분석 카드 열기가 함께 일어난다 —
        /// 클릭 경로를 새로 만들지 않고 이 경로를 그대로 재사용한다.
        /// </summary>
        public void SelectTile(Vector2Int coord)
        {
            TileType selectedType = _services != null && _services.TileData != null
                ? _services.TileData.GetTileType(coord)
                : TileType.Empty;

            ClearSelectedVisual();
            _selectedCoord = coord;

            DisableLegacyHighlight();
            ApplySelectedVisual(coord, selectedType);

            // 상세 분석 카드 열기
            if (analysisCard != null)
            {
                analysisCard.OpenCard(coord);
            }

            if (TileFootprint.IsBuilding(selectedType))
            {
                ShowBuildingInfoCard(coord, selectedType);
            }
            else
            {
                HideBuildingInfoCard();
            }

            if (selectedType == TileType.School)
            {
                _mainCityView ??= FindAnyObjectByType<MainCityView>(
                    FindObjectsInactive.Include);
                if (_mainCityView != null)
                {
                    _mainCityView.LogSchoolVisualDiagnostics(coord);
                }
                else
                {
                    Debug.LogWarning(
                        "[SchoolVisualDiagnostics] School was selected, but MainCityView " +
                        "was not found. The runtime school model cannot be inspected.",
                        this);
                }
            }
            
            Debug.Log($"[TileSelection] 타일 선택됨: {coord}");
        }

        private void DeselectTile()
        {
            _selectedCoord = null;
            ClearSelectedVisual();
            if (highlightBox != null) highlightBox.SetActive(false);
            if (analysisCard != null) analysisCard.CloseCard();
            HideBuildingInfoCard();
        }

        private void ClearSelectionIfRemoved()
        {
            if (!_selectedCoord.HasValue ||
                _services?.TileData == null ||
                _services.TileData.GetTileType(
                    _selectedCoord.Value) != TileType.Empty)
            {
                return;
            }

            DeselectTile();
        }

        private void DisableLegacyHighlight()
        {
            if (highlightBox != null)
            {
                highlightBox.SetActive(false);
            }
        }

        private void ApplySelectedVisual(
            Vector2Int coord,
            TileType type)
        {
            Vector2Int visualAnchor = ResolveVisualAnchor(coord);
            Renderer[] renderers = null;
            if (type == TileType.SpecialBuilding)
            {
                SpecialBuildingView specialView =
                    FindAnyObjectByType<SpecialBuildingView>(
                        FindObjectsInactive.Include);
                specialView?.TryGetVisualRenderers(
                    visualAnchor,
                    out renderers);
            }

            if (renderers == null || renderers.Length == 0)
            {
                MainCityView cityView =
                    FindAnyObjectByType<MainCityView>(
                        FindObjectsInactive.Include);
                cityView?.TryGetTileVisualRenderers(
                    visualAnchor,
                    out renderers);
            }

            if (renderers == null)
            {
                return;
            }

            for (int index = 0; index < renderers.Length; index++)
            {
                ApplySelectedRenderer(renderers[index]);
            }
        }

        private Vector2Int ResolveVisualAnchor(Vector2Int coord)
        {
            return _services?.TileData != null &&
                   _services.TileData.TryGetFootprintAnchor(
                       coord,
                       out Vector2Int anchor)
                ? anchor
                : coord;
        }

        private void ApplySelectedRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            CreateSelectionOverlayMaterial();
            if (_selectionOverlayMaterial == null)
            {
                return;
            }

            var overlayObject = new GameObject(
                renderer.gameObject.name + " (Selection Overlay)");
            overlayObject.hideFlags = HideFlags.HideAndDontSave;
            overlayObject.layer = renderer.gameObject.layer;
            overlayObject.transform.SetParent(
                renderer.transform,
                false);

            Renderer overlayRenderer = null;
            Mesh mesh = null;
            if (renderer is SkinnedMeshRenderer sourceSkinned &&
                sourceSkinned.sharedMesh != null)
            {
                var overlaySkinned =
                    overlayObject.AddComponent<SkinnedMeshRenderer>();
                overlaySkinned.sharedMesh = sourceSkinned.sharedMesh;
                overlaySkinned.bones = sourceSkinned.bones;
                overlaySkinned.rootBone = sourceSkinned.rootBone;
                overlaySkinned.localBounds = sourceSkinned.localBounds;
                overlaySkinned.updateWhenOffscreen =
                    sourceSkinned.updateWhenOffscreen;
                overlayRenderer = overlaySkinned;
                mesh = sourceSkinned.sharedMesh;
            }
            else if (renderer is MeshRenderer &&
                     renderer.TryGetComponent(
                         out MeshFilter sourceFilter) &&
                     sourceFilter.sharedMesh != null)
            {
                var overlayFilter =
                    overlayObject.AddComponent<MeshFilter>();
                overlayFilter.sharedMesh = sourceFilter.sharedMesh;
                overlayRenderer =
                    overlayObject.AddComponent<MeshRenderer>();
                mesh = sourceFilter.sharedMesh;
            }

            if (overlayRenderer == null || mesh == null)
            {
                DestroySelectionObject(overlayObject);
                return;
            }

            int materialCount = Mathf.Max(1, mesh.subMeshCount);
            var materials = new Material[materialCount];
            for (int index = 0; index < materials.Length; index++)
            {
                materials[index] = _selectionOverlayMaterial;
            }

            overlayRenderer.sharedMaterials = materials;
            overlayRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            overlayRenderer.receiveShadows = false;
            overlayRenderer.lightProbeUsage =
                UnityEngine.Rendering.LightProbeUsage.Off;
            overlayRenderer.reflectionProbeUsage =
                UnityEngine.Rendering.ReflectionProbeUsage.Off;
            overlayRenderer.sortingLayerID = renderer.sortingLayerID;
            overlayRenderer.sortingOrder = renderer.sortingOrder + 1;
            _selectionOverlayObjects.Add(overlayObject);
        }

        private void CreateSelectionOverlayMaterial()
        {
            if (_selectionOverlayMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find(
                "Universal Render Pipeline/Unlit");
            shader ??= Shader.Find("Sprites/Default");
            if (shader == null)
            {
                return;
            }

            Color overlayColor = GetSelectionOverlayColor();
            _selectionOverlayMaterial = new Material(shader)
            {
                name = "Tile Selection Overlay (Runtime)",
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = 3000
            };
            _selectionOverlayMaterial.SetOverrideTag(
                "RenderType",
                "Transparent");
            if (_selectionOverlayMaterial.HasProperty(BaseColorId))
            {
                _selectionOverlayMaterial.SetColor(
                    BaseColorId,
                    overlayColor);
            }
            if (_selectionOverlayMaterial.HasProperty(ColorId))
            {
                _selectionOverlayMaterial.SetColor(
                    ColorId,
                    overlayColor);
            }
            SetMaterialFloatIfPresent(
                _selectionOverlayMaterial,
                "_Surface",
                1f);
            SetMaterialFloatIfPresent(
                _selectionOverlayMaterial,
                "_Blend",
                0f);
            SetMaterialFloatIfPresent(
                _selectionOverlayMaterial,
                "_SrcBlend",
                (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            SetMaterialFloatIfPresent(
                _selectionOverlayMaterial,
                "_DstBlend",
                (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            SetMaterialFloatIfPresent(
                _selectionOverlayMaterial,
                "_ZWrite",
                0f);
            _selectionOverlayMaterial.EnableKeyword(
                "_SURFACE_TYPE_TRANSPARENT");
            _selectionOverlayMaterial.SetShaderPassEnabled(
                "ShadowCaster",
                false);
        }

        private static void SetMaterialFloatIfPresent(
            Material material,
            string propertyName,
            float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        private static Color GetSelectionOverlayColor() =>
            new Color(
                0.55f,
                0.82f,
                1f,
                SelectionOverlayAlpha);

        private static void DestroySelectionObject(
            Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private void ClearSelectedVisual()
        {
            for (int index =
                     _selectionOverlayObjects.Count - 1;
                 index >= 0;
                 index--)
            {
                DestroySelectionObject(
                    _selectionOverlayObjects[index]);
            }

            _selectionOverlayObjects.Clear();
            DestroySelectionObject(_selectionOverlayMaterial);
            _selectionOverlayMaterial = null;
        }
    }
}

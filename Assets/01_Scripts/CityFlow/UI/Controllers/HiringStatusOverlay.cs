using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    // 정원 미달 회사 위에 현재 채용 인원을 띄운다. 표시 전담이며,
    // 채용 상태와 시뮬레이션에는 영향을 주지 않는다.
    public sealed class HiringStatusOverlay
        : MonoBehaviour, ICityFlowServiceConsumer
    {
        private const float RefreshInterval = 0.2f;
        private const int OverlaySortingOrder = -10;

        [Header("Indicator")]
        [SerializeField] private HiringStatusIndicatorView indicatorTemplate;
        [SerializeField] private float heightOffset = 1.2f;

        private CityFlowServices _services;
        private readonly HashSet<Vector2Int> _trackedAnchors = new();
        private readonly Dictionary<Vector2Int, HiringStatusIndicatorView>
            _indicators = new();
        private readonly List<Vector2Int> _removedAnchors = new();
        private bool _subscribed;
        private float _nextRefreshTime;
        private BuildingInfoCardController _buildingInfoCard;
        private Vector2Int? _suppressedAnchor;
        private RectTransform _overlayCanvasRect;

        public void Initialize(CityFlowServices services)
        {
            Unsubscribe();
            _services = services;
            BindBuildingInfoCard();

            if (_services?.Events == null) return;
            _services.Events.Placed += OnPlaced;
            if (_services.Save != null)
            {
                _services.Save.RestoreCompleted += OnRestoreCompleted;
            }
            _subscribed = true;
            _nextRefreshTime = 0f;

            CollectExistingSites();
        }

        private void OnRestoreCompleted(RestoreCompletedEvent _) =>
            CollectExistingSites();

        // 복원은 PlacedEvent 를 쏘지 않으므로 씬 진입·복원마다 한 번 수집한다.
        // 프레임별 전수 스캔은 하지 않고 등록된 회사만 Update 에서 갱신한다.
        private void CollectExistingSites()
        {
            IReadOnlyTileData tiles = _services?.TileData;
            IWorldGridAccess grid = _services?.WorldGrid;
            if (tiles == null) return;

            int worldWidth = grid?.WorldWidth ?? GridUtil.DefaultWidth;
            int worldHeight = grid?.WorldHeight ?? GridUtil.DefaultHeight;

            for (int y = 0; y < worldHeight; y++)
            {
                for (int x = 0; x < worldWidth; x++)
                {
                    var tile = new Vector2Int(x, y);
                    TryRegister(tile);
                }
            }
        }

        private void OnPlaced(PlacedEvent e)
        {
            if (e.IsRemove || e.Type != TileType.Office) return;
            TryRegister(e.Tile);
        }

        private void TryRegister(Vector2Int anchor)
        {
            if (_trackedAnchors.Contains(anchor)) return;

            IReadOnlyTileData tiles = _services?.TileData;
            IReadOnlyCityStats stats = _services?.Stats;
            if (tiles == null || stats == null) return;
            if (!tiles.IsFootprintAnchor(anchor)) return;
            if (tiles.GetTileType(anchor) != TileType.Office) return;
            if (!stats.TryGetCompanyStaffing(
                    anchor,
                    out CompanyStaffing staffing)) return;

            _trackedAnchors.Add(anchor);
            RefreshIndicator(
                anchor,
                staffing,
                _services.WorldCoordinates,
                Camera.main);
        }

        private HiringStatusIndicatorView CreateIndicator(Vector2Int tile)
        {
            if (indicatorTemplate == null) return null;

            EnsureOverlayCanvas();
            if (_overlayCanvasRect == null) return null;

            HiringStatusIndicatorView indicator = Instantiate(
                indicatorTemplate,
                _overlayCanvasRect);
            indicator.name = $"HiringStatus_{tile.x}_{tile.y}";
            indicator.gameObject.SetActive(true);
            return indicator;
        }

        private void EnsureOverlayCanvas()
        {
            if (_overlayCanvasRect != null)
            {
                return;
            }

            var canvasObject = new GameObject(
                "HiringStatusCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = OverlaySortingOrder;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _overlayCanvasRect =
                canvasObject.GetComponent<RectTransform>();
        }

        private void Update()
        {
            if (_trackedAnchors.Count == 0) return;

            UpdateIndicatorScreenPositions(Camera.main);
            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + RefreshInterval;
            RefreshTrackedCompanies();
        }

        private void RefreshTrackedCompanies()
        {
            if (_trackedAnchors.Count == 0) return;

            IReadOnlyCityStats stats = _services?.Stats;
            IWorldCoordinateSpace space = _services?.WorldCoordinates;
            if (stats == null) return;
            Camera cam = Camera.main;

            foreach (Vector2Int anchor in _trackedAnchors)
            {
                if (!stats.TryGetCompanyStaffing(
                        anchor,
                        out CompanyStaffing staffing))
                {
                    _removedAnchors.Add(anchor);
                    continue;
                }

                RefreshIndicator(anchor, staffing, space, cam);
            }

            for (int i = 0; i < _removedAnchors.Count; i++)
            {
                Vector2Int anchor = _removedAnchors[i];
                RemoveIndicator(anchor);
                _trackedAnchors.Remove(anchor);
            }

            _removedAnchors.Clear();
        }

        private void RefreshIndicator(
            Vector2Int anchor,
            CompanyStaffing staffing,
            IWorldCoordinateSpace space = null,
            Camera cam = null)
        {
            if (staffing.Capacity <= 0 ||
                staffing.Filled >= staffing.Capacity)
            {
                RemoveIndicator(anchor);
                return;
            }

            if (!_indicators.TryGetValue(
                    anchor,
                    out HiringStatusIndicatorView indicator) ||
                indicator == null)
            {
                indicator = CreateIndicator(anchor);
                if (indicator == null) return;
                _indicators[anchor] = indicator;
            }

            indicator.Configure(staffing.Filled, staffing.Capacity);
            UpdateIndicatorScreenPosition(
                anchor,
                indicator,
                space,
                cam);
        }

        private void UpdateIndicatorScreenPositions(Camera cam)
        {
            IWorldCoordinateSpace space = _services?.WorldCoordinates;

            foreach (KeyValuePair<Vector2Int, HiringStatusIndicatorView>
                     entry in _indicators)
            {
                if (entry.Value != null)
                {
                    UpdateIndicatorScreenPosition(
                        entry.Key,
                        entry.Value,
                        space,
                        cam);
                }
            }
        }

        private void UpdateIndicatorScreenPosition(
            Vector2Int anchor,
            HiringStatusIndicatorView indicator,
            IWorldCoordinateSpace space,
            Camera cam)
        {
            bool projected = indicator.TrySetScreenPosition(
                _overlayCanvasRect,
                cam,
                ResolveIndicatorPosition(anchor, space));
            bool shouldShow = projected &&
                              (!_suppressedAnchor.HasValue ||
                               _suppressedAnchor.Value != anchor);
            if (indicator.gameObject.activeSelf != shouldShow)
            {
                indicator.gameObject.SetActive(shouldShow);
            }
        }

        private Vector3 ResolveIndicatorPosition(
            Vector2Int anchor,
            IWorldCoordinateSpace space)
        {
            Vector2Int footprint = TileFootprint.GetSize(TileType.Office);
            Vector2Int far = anchor + footprint - Vector2Int.one;
            if (space != null)
            {
                return (space.GridToWorld(anchor, heightOffset) +
                        space.GridToWorld(far, heightOffset)) * 0.5f;
            }

            return (GridUtil.GridToWorld(anchor) +
                    GridUtil.GridToWorld(far)) * 0.5f +
                   Vector3.back * heightOffset;
        }

        private void RemoveIndicator(Vector2Int anchor)
        {
            if (_indicators.TryGetValue(
                    anchor,
                    out HiringStatusIndicatorView indicator) &&
                indicator != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(indicator.gameObject);
                }
                else
                {
                    DestroyImmediate(indicator.gameObject);
                }
            }

            _indicators.Remove(anchor);
        }

        private void OnDestroy() => Unsubscribe();

        private void BindBuildingInfoCard()
        {
            _buildingInfoCard = FindAnyObjectByType<BuildingInfoCardController>(
                FindObjectsInactive.Include);
            if (_buildingInfoCard == null)
            {
                return;
            }

            _buildingInfoCard.VisibilityChanged +=
                OnBuildingInfoVisibilityChanged;
            if (_buildingInfoCard.IsVisible)
            {
                OnBuildingInfoVisibilityChanged(
                    _buildingInfoCard.DisplayedTile,
                    true);
            }
        }

        private void OnBuildingInfoVisibilityChanged(
            Vector2Int anchor,
            bool visible)
        {
            if (visible)
            {
                if (_suppressedAnchor.HasValue &&
                    _suppressedAnchor.Value != anchor)
                {
                    Vector2Int previousAnchor = _suppressedAnchor.Value;
                    _suppressedAnchor = null;
                    RefreshIndicatorForAnchor(previousAnchor);
                }

                _suppressedAnchor = anchor;
                if (_indicators.TryGetValue(
                        anchor,
                        out HiringStatusIndicatorView indicator) &&
                    indicator != null)
                {
                    indicator.gameObject.SetActive(false);
                }

                return;
            }

            if (!_suppressedAnchor.HasValue ||
                _suppressedAnchor.Value != anchor)
            {
                return;
            }

            _suppressedAnchor = null;
            RefreshIndicatorForAnchor(anchor);
        }

        private void RefreshIndicatorForAnchor(Vector2Int anchor)
        {
            IReadOnlyCityStats stats = _services?.Stats;
            if (stats == null ||
                !stats.TryGetCompanyStaffing(
                    anchor,
                    out CompanyStaffing staffing))
            {
                RemoveIndicator(anchor);
                _trackedAnchors.Remove(anchor);
                return;
            }

            RefreshIndicator(
                anchor,
                staffing,
                _services?.WorldCoordinates,
                Camera.main);
        }

        private void Unsubscribe()
        {
            if (_buildingInfoCard != null)
            {
                _buildingInfoCard.VisibilityChanged -=
                    OnBuildingInfoVisibilityChanged;
                _buildingInfoCard = null;
            }

            _suppressedAnchor = null;
            if (_subscribed && _services?.Events != null)
            {
                _services.Events.Placed -= OnPlaced;
                if (_services.Save != null)
                {
                    _services.Save.RestoreCompleted -= OnRestoreCompleted;
                }
            }

            _subscribed = false;
        }
    }
}

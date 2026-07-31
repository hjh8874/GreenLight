using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using TMPro;
using UnityEngine;

namespace CityFlow.UI
{
    // 정원 미달 회사 위에 현재 채용 인원을 띄운다. 표시 전담이며,
    // 채용 상태와 시뮬레이션에는 영향을 주지 않는다.
    public sealed class HiringStatusOverlay
        : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("Label")]
        [SerializeField] private TextMeshPro labelTemplate;
        [SerializeField] private float heightOffset = 1.2f;

        private CityFlowServices _services;
        private readonly HashSet<Vector2Int> _trackedAnchors = new();
        private readonly Dictionary<Vector2Int, TextMeshPro> _labels = new();
        private readonly List<Vector2Int> _removedAnchors = new();
        private readonly List<Vector2Int> _finishedLabels = new();
        private bool _subscribed;

        public void Initialize(CityFlowServices services)
        {
            Unsubscribe();
            _services = services;

            if (_services?.Events == null) return;
            _services.Events.Placed += OnPlaced;
            if (_services.Save != null)
            {
                _services.Save.RestoreCompleted += OnRestoreCompleted;
            }
            _subscribed = true;

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
            if (tiles == null || grid == null) return;

            for (int y = 0; y < grid.WorldHeight; y++)
            {
                for (int x = 0; x < grid.WorldWidth; x++)
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
            UpdateLabel(anchor, staffing);
        }

        private TextMeshPro CreateLabel(Vector2Int tile)
        {
            if (labelTemplate == null) return null;

            TextMeshPro label =
                Instantiate(labelTemplate, labelTemplate.transform.parent);
            label.name = $"HiringStatus_{tile.x}_{tile.y}";
            label.gameObject.SetActive(true);
            return label;
        }

        private void Update()
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

                if (staffing.Filled >= staffing.Capacity)
                {
                    _finishedLabels.Add(anchor);
                    continue;
                }

                UpdateLabel(anchor, staffing, space, cam);
            }

            for (int i = 0; i < _finishedLabels.Count; i++)
            {
                RemoveLabel(_finishedLabels[i]);
            }

            for (int i = 0; i < _removedAnchors.Count; i++)
            {
                Vector2Int anchor = _removedAnchors[i];
                RemoveLabel(anchor);
                _trackedAnchors.Remove(anchor);
            }

            _finishedLabels.Clear();
            _removedAnchors.Clear();
        }

        private void UpdateLabel(
            Vector2Int anchor,
            CompanyStaffing staffing,
            IWorldCoordinateSpace space = null,
            Camera cam = null)
        {
            if (!_labels.TryGetValue(anchor, out TextMeshPro label) || label == null)
            {
                label = CreateLabel(anchor);
                if (label == null) return;
                _labels[anchor] = label;
            }

            label.text = $"채용중 {staffing.Filled}/{staffing.Capacity}";
            if (space != null)
            {
                label.transform.position =
                    space.GridToWorld(anchor, heightOffset);
                if (space.Plane == WorldCoordinatePlane.XY)
                {
                    label.transform.rotation = space.CoordinateRotation;
                }
                else if (cam != null)
                {
                    label.transform.rotation = cam.transform.rotation;
                }
            }
        }

        private void RemoveLabel(Vector2Int anchor)
        {
            if (_labels.TryGetValue(anchor, out TextMeshPro label) &&
                label != null)
            {
                Destroy(label.gameObject);
            }

            _labels.Remove(anchor);
        }

        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
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

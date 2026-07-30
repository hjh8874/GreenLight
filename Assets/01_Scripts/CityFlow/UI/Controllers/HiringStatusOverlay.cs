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
        private readonly Dictionary<Vector2Int, TextMeshPro> _labels = new();
        private readonly List<Vector2Int> _finished = new();
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
            if (_labels.ContainsKey(anchor)) return;

            IReadOnlyTileData tiles = _services?.TileData;
            IReadOnlyCityStats stats = _services?.Stats;
            if (tiles == null || stats == null) return;
            if (!tiles.IsFootprintAnchor(anchor)) return;
            if (tiles.GetTileType(anchor) != TileType.Office) return;
            if (!stats.TryGetCompanyStaffing(
                    anchor,
                    out CompanyStaffing staffing)) return;
            if (staffing.Filled >= staffing.Capacity) return;

            TextMeshPro label = CreateLabel(anchor);
            if (label != null)
            {
                _labels.Add(anchor, label);
            }
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
            if (_labels.Count == 0) return;

            IReadOnlyCityStats stats = _services?.Stats;
            IWorldCoordinateSpace space = _services?.WorldCoordinates;
            if (stats == null) return;
            Camera cam = Camera.main;

            foreach (KeyValuePair<Vector2Int, TextMeshPro> pair in _labels)
            {
                if (!stats.TryGetCompanyStaffing(
                        pair.Key,
                        out CompanyStaffing staffing) ||
                    staffing.Filled >= staffing.Capacity)
                {
                    _finished.Add(pair.Key);
                    continue;
                }

                pair.Value.text =
                    $"채용중 {staffing.Filled}/{staffing.Capacity}";
                if (space != null)
                {
                    pair.Value.transform.position =
                        space.GridToWorld(pair.Key, heightOffset);
                    if (space.Plane == WorldCoordinatePlane.XY)
                    {
                        pair.Value.transform.rotation =
                            space.CoordinateRotation;
                    }
                    else if (cam != null)
                    {
                        pair.Value.transform.rotation =
                            cam.transform.rotation;
                    }
                }
            }

            for (int i = 0; i < _finished.Count; i++)
            {
                if (_labels.TryGetValue(
                        _finished[i],
                        out TextMeshPro label) &&
                    label != null)
                {
                    Destroy(label.gameObject);
                }

                _labels.Remove(_finished[i]);
            }

            _finished.Clear();
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

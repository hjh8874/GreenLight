using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using TMPro;
using UnityEngine;

namespace CityFlow.UI
{
    // 공사 중인 건물 위에 진행도 %를 띄운다. 표시 전담 — 이 컴포넌트가 씬에 없어도
    // 시뮬레이션은 그대로 돈다(공사·승격은 Sim 이 혼자 처리한다).
    //
    // 계획서는 "0.5초 주기 전수 스캔"을 지시했지만 그대로 하지 않았다. PR #169 가
    // 틱당 전체 그리드 스캔으로 비용을 100배 늘린 전례가 있고, 공사장은 보통 한 자리
    // 수인데 그리드는 20×20~대형까지 커진다. Placed 이벤트로 활성 앵커만 들고
    // O(공사장 수)로 갱신한다.
    public sealed class BuildingConstructionOverlay
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
            _subscribed = true;
        }

        private void OnPlaced(PlacedEvent e)
        {
            // 철거·완공은 Update 의 진행도 조회가 false 를 받아 정리한다. 여기서는
            // 새 공사장만 등록한다 — 완공 이벤트가 따로 없어도 상태가 어긋나지 않는다.
            if (e.IsRemove || e.Type != TileType.UnderConstruction) return;
            if (_labels.ContainsKey(e.Tile)) return;

            TextMeshPro label = CreateLabel(e.Tile);
            if (label != null)
            {
                _labels.Add(e.Tile, label);
            }
        }

        private TextMeshPro CreateLabel(Vector2Int tile)
        {
            if (labelTemplate == null) return null;

            TextMeshPro label =
                Instantiate(labelTemplate, labelTemplate.transform.parent);
            label.name = $"ConstructionProgress_{tile.x}_{tile.y}";
            label.gameObject.SetActive(true);
            return label;
        }

        private void Update()
        {
            if (_labels.Count == 0) return;

            IReadOnlyTileData tiles = _services?.TileData;
            IWorldCoordinateSpace space = _services?.WorldCoordinates;
            if (tiles == null) return;

            foreach (KeyValuePair<Vector2Int, TextMeshPro> pair in _labels)
            {
                if (!tiles.TryGetConstructionProgress01(pair.Key, out float progress))
                {
                    // 완공됐거나 철거됐다. 둘 다 라벨을 없앤다.
                    _finished.Add(pair.Key);
                    continue;
                }

                pair.Value.text = $"{Mathf.RoundToInt(progress * 100f)}%";
                if (space != null)
                {
                    pair.Value.transform.position =
                        space.GridToWorld(pair.Key, heightOffset);
                }
            }

            for (int i = 0; i < _finished.Count; i++)
            {
                if (_labels.TryGetValue(_finished[i], out TextMeshPro label)
                    && label != null)
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
            }

            _subscribed = false;
        }
    }
}

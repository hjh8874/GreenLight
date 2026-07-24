using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using TMPro;
using UnityEngine;

namespace CityFlow.UI.Controllers
{
    /// <summary>
    /// 정원 미달(구인 중) 회사 머리 위에 "구인 N/M" 라벨을 상시 표시하는 글랜스 오버레이.
    /// 채용 램프(시간당 채용)의 진행이 지도만 봐도 읽히게 하는 방치형 글랜스 밸류 담당.
    /// 구인 완료된 회사의 라벨은 자동으로 사라진다.
    /// 데이터는 IReadOnlyCityStats.TryGetCompanyStaffing 하나만 읽는다(회사 앵커 타일에서만
    /// 응답하므로 2x2 풋프린트 중복 라벨이 원천 차단됨). 이벤트 구독 대신 주기 전수 스캔 —
    /// 세이브 로드처럼 PlacedEvent 없이 회사가 생기는 경로까지 자동 커버된다.
    /// FlowBurstFloatingText와 같은 XZ(3D 쿼터뷰)/XY(2D) 이중 지원.
    /// </summary>
    public sealed class CompanyHiringGaugeOverlay : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("Grid")]
        // IReadOnlyTileData엔 그리드 크기가 없어서 여기서 들고 있음(SimDebugOverlay와 동일 관례).
        [SerializeField] private int width = GridUtil.DefaultWidth;
        [SerializeField] private int height = GridUtil.DefaultHeight;

        [Header("Refresh")]
        [Tooltip("전수 스캔 주기(초). 회사 수가 적어 0.5s 스캔은 무시 가능한 비용.")]
        [SerializeField] private float refreshInterval = 0.5f;

        [Header("Visuals")]
        [SerializeField] private Color hiringColor = new Color(1f, 0.6f, 0.1f);   // 주황(회사 팔레트)
        [SerializeField] private float fontSize = 4f;
        [SerializeField] private float heightOffset = 1.6f;

        [Header("View Settings")]
        [Tooltip("true = XY 평면(2D), false = XZ 평면(3D 쿼터뷰)")]
        [SerializeField] private bool useXYPlane = false;

        private CityFlowServices _services;
        private Transform _labelRoot;
        private readonly Dictionary<Vector2Int, TextMeshPro> _labels = new();
        private readonly List<Vector2Int> _staleBuffer = new();
        private float _nextRefreshAt;

        public void Initialize(CityFlowServices services)
        {
            _services = services;
            _nextRefreshAt = 0f;   // 다음 Update에서 즉시 1회 갱신
        }

        private void OnDisable()
        {
            foreach (TextMeshPro label in _labels.Values)
            {
                if (label != null) label.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (_services == null) return;
            if (Time.time >= _nextRefreshAt)
            {
                _nextRefreshAt = Time.time + Mathf.Max(0.1f, refreshInterval);
                RefreshLabels();
            }
            BillboardLabels();
        }

        private void RefreshLabels()
        {
            IReadOnlyTileData tiles = _services.TileData;
            IReadOnlyCityStats stats = _services.Stats;

            _staleBuffer.Clear();
            _staleBuffer.AddRange(_labels.Keys);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var tile = new Vector2Int(x, y);
                    if (tiles.GetTileType(tile) != TileType.Office) continue;
                    // 앵커가 아닌 풋프린트 타일은 staffing 조회가 실패한다 — 앵커당 라벨 1개.
                    if (!stats.TryGetCompanyStaffing(tile, out CompanyStaffing staffing)) continue;
                    if (staffing.Filled >= staffing.Capacity) continue;   // 구인 완료 = 라벨 소멸

                    TextMeshPro label = EnsureLabel(tile);
                    label.text = $"구인 {staffing.Filled}/{staffing.Capacity}";
                    label.gameObject.SetActive(true);
                    _staleBuffer.Remove(tile);
                }
            }

            // 이번 스캔에서 구인 중이 아니었던 라벨(완료·철거) 정리.
            for (int i = 0; i < _staleBuffer.Count; i++)
            {
                Vector2Int tile = _staleBuffer[i];
                if (_labels.TryGetValue(tile, out TextMeshPro label) && label != null)
                {
                    Destroy(label.gameObject);
                }
                _labels.Remove(tile);
            }
        }

        private TextMeshPro EnsureLabel(Vector2Int anchor)
        {
            if (_labels.TryGetValue(anchor, out TextMeshPro existing) && existing != null)
            {
                return existing;
            }

            if (_labelRoot == null)
            {
                var rootGo = new GameObject("CompanyHiringGaugeRoot");
                rootGo.transform.SetParent(transform);
                _labelRoot = rootGo.transform;
            }

            var go = new GameObject($"UI_HiringGauge_{anchor.x}_{anchor.y}");
            go.transform.SetParent(_labelRoot);
            go.transform.position = LabelPosition(anchor);

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = fontSize;
            tmp.color = hiringColor;
            tmp.sortingOrder = 200;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            if (go.TryGetComponent<MeshRenderer>(out var renderer))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            _labels[anchor] = tmp;
            return tmp;
        }

        private Vector3 LabelPosition(Vector2Int anchor)
        {
            // 2x2 풋프린트 중심 = 앵커 타일 중심 + 반 타일. (회전해도 2x2 정사각이라 중심 동일)
            Vector2Int size = TileFootprint.GetSize(TileType.Office);
            float cx = (anchor.x + size.x * 0.5f) * GridUtil.TileSize;
            float cy = (anchor.y + size.y * 0.5f) * GridUtil.TileSize;
            return useXYPlane
                ? new Vector3(cx, cy, -0.1f)
                : new Vector3(cx, heightOffset, cy);
        }

        private void BillboardLabels()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Quaternion rotation = useXYPlane ? Quaternion.identity : cam.transform.rotation;
            foreach (TextMeshPro label in _labels.Values)
            {
                if (label != null) label.transform.rotation = rotation;
            }
        }
    }
}

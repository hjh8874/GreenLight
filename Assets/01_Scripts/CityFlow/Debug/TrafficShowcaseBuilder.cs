using System;
using System.Reflection;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CityFlow.DebugTools
{
    // 오늘 기능 종합 테스트용 쇼케이스 도시. 디버그 씬에서만 붙이고 F4로 한 번 생성한다.
    public sealed class TrafficShowcaseBuilder : MonoBehaviour, ICityFlowServiceConsumer
    {
        private const string FeedbackPrefabPath = "Assets/02_Prefabs/UI/CongestionFeedbackSystem.prefab";

        [SerializeField] private GameObject congestionFeedbackPrefab;
        private CityFlowServices _services;
        private bool _built;

        public void Initialize(CityFlowServices services)
        {
            _services = services;
        }

        private void Update()
        {
            if (_services == null || _built || Keyboard.current == null) return;
            if (Keyboard.current.f4Key.wasPressedThisFrame)
            {
                BuildShowcase();
            }
        }

        private void BuildShowcase()
        {
            IPlacementService placement = _services.Placement;
            if (placement == null)
            {
                Debug.LogWarning("[TrafficShowcaseBuilder] Placement 서비스가 없습니다.", this);
                return;
            }

            // 2개 가로축 + 3개 세로 연결축. (6,6), (10,6)은 장치 비교용 교차로다.
            for (int x = 2; x <= 17; x++)
            {
                placement.Place(new Vector2Int(x, 6), TileType.Road);
                placement.Place(new Vector2Int(x, 12), TileType.Road);
            }
            for (int x = 6; x <= 14; x += 4)
                for (int y = 6; y <= 12; y++)
                    placement.Place(new Vector2Int(x, y), TileType.Road);

            // 채용 램프가 빨리 보이도록 도로 가까이에 밀집 배치한다(거주지는 1x2, 회사는 2x2 풋프린트).
            Vector2Int[] houses =
            {
                new Vector2Int(2, 7), new Vector2Int(8, 7), new Vector2Int(12, 7),
                new Vector2Int(2, 13), new Vector2Int(8, 13), new Vector2Int(12, 13)
            };
            Vector2Int[] offices =
            {
                new Vector2Int(2, 15), new Vector2Int(8, 15), new Vector2Int(12, 15)
            };
            foreach (Vector2Int tile in houses) placement.Place(tile, TileType.House);
            foreach (Vector2Int tile in offices) placement.Place(tile, TileType.Office);

            IIntersectionFacilityService facilities = placement as IIntersectionFacilityService;
            bool signal = facilities != null && facilities.TryPlaceSignal(new Vector2Int(6, 6), 8);
            bool roundabout = facilities != null && facilities.TryPlaceRoundabout(new Vector2Int(10, 6));

            IBusStopInfrastructureService busStops = placement as IBusStopInfrastructureService;
            bool stopA = busStops != null && busStops.TryPlaceBusStop(new Vector2Int(4, 5));
            bool stopB = busStops != null && busStops.TryPlaceBusStop(new Vector2Int(12, 11));

            EnsureCongestionFeedbackSystem();
            _built = true;

            Debug.Log("[TrafficShowcaseBuilder] 쇼케이스 완료 — 관찰 포인트: 트럭 20%, 무신호 줄서기 vs 신호, 효과 팝(2게임일 후), 정류장 커버 감축. "
                + $"장치 signal={signal}, roundabout={roundabout}, stops={stopA}/{stopB}", this);
        }

        private void EnsureCongestionFeedbackSystem()
        {
            if (FindFirstObjectByType<CityFlow.View.InfrastructureEffectPopView>() != null)
                return;

            GameObject prefab = congestionFeedbackPrefab;
#if UNITY_EDITOR
            if (prefab == null)
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FeedbackPrefabPath);
#endif
            if (prefab == null)
            {
                Debug.LogWarning("[TrafficShowcaseBuilder] CongestionFeedbackSystem 프리팹을 찾지 못했습니다.", this);
                return;
            }

            GameObject instance = Instantiate(prefab);
            instance.name = "CongestionFeedbackSystem (Runtime)";
            foreach (MonoBehaviour component in instance.GetComponentsInChildren<MonoBehaviour>(true))
            {
                MethodInfo initialize = component.GetType().GetMethod(
                    "Initialize", BindingFlags.Instance | BindingFlags.Public,
                    null, new[] { typeof(CityFlowServices) }, null);
                initialize?.Invoke(component, new object[] { _services });
            }
        }

        private void OnGUI()
        {
            if (_services == null) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 18, normal = { textColor = Color.white } };
            GUI.Label(new Rect(12, 70, 900, 30), _built
                ? "F4 쇼케이스 생성 완료 — 신호·로터리/무신호/정류장 커버를 관찰하세요."
                : "F4 오늘 기능 종합 테스트 키트 생성 — 밀집 교통 회랑 + 신호/로터리/무신호 비교", style);
        }
    }
}

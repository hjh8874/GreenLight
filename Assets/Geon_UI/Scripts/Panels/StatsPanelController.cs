using System.Collections;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using TMPro;
using UnityEngine;

namespace CityFlow.UI.Geon
{
    public class StatsPanelController : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("UI Elements")]
        [SerializeField] private TMP_Text txtJamCount;
        [SerializeField] private TMP_Text txtCoinsPerMinute;

        private CityFlowServices _services;
        private Coroutine _updateRoutine;

        public void Initialize(CityFlowServices services)
        {
            _services = services;
        }

        private void OnEnable()
        {
            // 패널이 켜질 때 스로틀링 업데이트 시작
            if (_updateRoutine != null) StopCoroutine(_updateRoutine);
            _updateRoutine = StartCoroutine(UpdateStatsRoutine());
        }

        private void OnDisable()
        {
            if (_updateRoutine != null) StopCoroutine(_updateRoutine);
        }

        private IEnumerator UpdateStatsRoutine()
        {
            WaitForSeconds wait = new WaitForSeconds(1.0f); // 통계창은 1초 주기로 갱신 (성능 최적화)

            while (true)
            {
                if (_services != null && _services.TileData != null)
                {
                    // 1. 정체 구역 카운터: 전체 맵을 돌며 밀도(Density)가 0.7 이상인 구역을 카운트
                    int jamCount = 0;
                    // (맵이 20x20 이라는 전제 하에 단순 무식하게 순회)
                    for (int y = 0; y < 20; y++)
                    {
                        for (int x = 0; x < 20; x++)
                        {
                            if (_services.TileData.GetDensity01(new Vector2Int(x, y)) > 0.7f)
                            {
                                jamCount++;
                            }
                        }
                    }

                    if (txtJamCount != null) txtJamCount.text = $"Jam Zones: {jamCount}";

                    // 2. 분당 수입 지표 (현재는 임시 가짜 계산식)
                    // 실제 엔진 이벤트(ArrivalEvent) 누적치를 통해 분당 계산을 해야 하나 1차 빌드에선 단순화
                    int cpm = Mathf.RoundToInt(_services.TileData.Stability01 * 300f); 
                    if (txtCoinsPerMinute != null) txtCoinsPerMinute.text = $"Income: {cpm}/min";
                }

                yield return wait;
            }
        }
    }
}

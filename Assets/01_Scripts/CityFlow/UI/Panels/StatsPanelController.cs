using System.Collections;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using TMPro;
using UnityEngine;

namespace CityFlow.UI
{
    public class StatsPanelController : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("UI Elements")]
        [SerializeField] private TMP_Text txtJamCount;
        [SerializeField] private TMP_Text txtCoinsPerMinute;

        private CityFlowServices _services;
        private Coroutine _updateRoutine;
        private readonly Queue<ArrivalCoinSample> _arrivalCoinSamples = new();
        private long _arrivalCoinsInLastMinute;

        private readonly struct ArrivalCoinSample
        {
            public readonly float Time;
            public readonly int Coins;

            public ArrivalCoinSample(float time, int coins)
            {
                Time = time;
                Coins = coins;
            }
        }

        private void Awake()
        {
            EnsureTextElements();
        }

        public void Configure(TMP_Text jamCount, TMP_Text coinsPerMinute)
        {
            txtJamCount = jamCount;
            txtCoinsPerMinute = coinsPerMinute;
        }

        public void Initialize(CityFlowServices services)
        {
            if (_services != null)
            {
                _services.Events.Arrival -= OnArrival;
            }

            _services = services;
            _services.Events.Arrival += OnArrival;
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

        private void OnDestroy()
        {
            if (_services != null)
            {
                _services.Events.Arrival -= OnArrival;
            }
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

                    if (txtJamCount != null) txtJamCount.text = $"정체 구역: {jamCount}곳";

                    RemoveExpiredArrivalSamples();
                    if (txtCoinsPerMinute != null)
                    {
                        txtCoinsPerMinute.text = $"도착 수입: 분당 {_arrivalCoinsInLastMinute:N0}";
                    }
                }
                yield return wait;
            }
        }

        private void OnArrival(ArrivalEvent arrival)
        {
            if (arrival.Coins <= 0)
            {
                return;
            }

            _arrivalCoinSamples.Enqueue(new ArrivalCoinSample(Time.unscaledTime, arrival.Coins));
            _arrivalCoinsInLastMinute += arrival.Coins;
            RemoveExpiredArrivalSamples();
        }

        private void RemoveExpiredArrivalSamples()
        {
            float cutoff = Time.unscaledTime - 60f;
            while (_arrivalCoinSamples.Count > 0 && _arrivalCoinSamples.Peek().Time < cutoff)
            {
                _arrivalCoinsInLastMinute -= _arrivalCoinSamples.Dequeue().Coins;
            }
        }

        private void EnsureTextElements()
        {
            if (txtJamCount == null)
            {
                txtJamCount = CreateText("JamCountText", new Vector2(30f, -30f));
            }

            if (txtCoinsPerMinute == null)
            {
                txtCoinsPerMinute = CreateText("CoinsPerMinuteText", new Vector2(30f, -75f));
            }
        }

        private TMP_Text CreateText(string objectName, Vector2 anchoredPosition)
        {
            GameObject textObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(transform, false);

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(420f, 40f);

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.fontSize = 26f;
            text.color = new Color(0.12f, 0.12f, 0.12f, 1f);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }
    }
}

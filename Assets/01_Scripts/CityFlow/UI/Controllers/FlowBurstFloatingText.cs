using System.Collections;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using TMPro;
using UnityEngine;

namespace CityFlow.UI.Controllers
{
    /// <summary>
    /// FlowBurst(그린웨이브 보너스) 발생 시 타일 위로 "+N" 코인 텍스트가
    /// 떠오르며 페이드아웃되는 플로팅 연출을 담당합니다.
    /// 오브젝트 풀링(Queue) + 코루틴 기반으로 GC 할당을 최소화합니다.
    /// XZ(3D 쿼터뷰)와 XY(2D) 평면을 모두 지원합니다.
    /// </summary>
    public sealed class FlowBurstFloatingText : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("Animation")]
        [SerializeField] private float floatDuration = 1.2f;
        [SerializeField] private float floatHeight = 1.5f;
        [SerializeField] private float initialScale = 0.8f;
        [SerializeField] private float peakScale = 1.3f;

        [Header("Visuals")]
        [SerializeField] private Color coinTextColor = new Color(1f, 0.85f, 0.15f); // 골드
        [SerializeField] private float fontSize = 5f;

        [Header("View Settings")]
        [Tooltip("true = XY 평면(2D), false = XZ 평면(3D 쿼터뷰)")]
        [SerializeField] private bool useXYPlane = false;

        [Header("Pool")]
        [SerializeField] private int initialPoolSize = 5;

        private CityFlowServices _services;
        private readonly Queue<TextMeshPro> _pool = new Queue<TextMeshPro>();
        private Transform _poolRoot;
        private bool _isSubscribed = false;

        public void Initialize(CityFlowServices services)
        {
            if (_services == services) return;

            Unsubscribe();

            _services = services;
            
            if (isActiveAndEnabled)
            {
                Subscribe();
            }

            EnsurePool();
        }

        private void Subscribe()
        {
            if (_services != null && !_isSubscribed)
            {
                _services.Events.FlowBurst += OnFlowBurst;
                _isSubscribed = true;
            }
        }

        private void Unsubscribe()
        {
            if (_services != null && _isSubscribed)
            {
                _services.Events.FlowBurst -= OnFlowBurst;
                _isSubscribed = false;
            }
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();

            if (_poolRoot == null) return;
            
            // 모든 코루틴이 정지되므로, 활성화된 텍스트들을 모두 회수하여 풀을 초기화합니다.
            _pool.Clear();
            foreach (Transform child in _poolRoot)
            {
                child.gameObject.SetActive(false);
                if (child.TryGetComponent<TextMeshPro>(out var tmp))
                {
                    _pool.Enqueue(tmp);
                }
            }
        }

        private void EnsurePool()
        {
            if (_poolRoot == null)
            {
                var rootGo = new GameObject("FlowBurstFloatingTextPool");
                rootGo.transform.SetParent(transform);
                _poolRoot = rootGo.transform;
            }

            while (_pool.Count < initialPoolSize)
            {
                _pool.Enqueue(CreateTextInstance());
            }
        }

        private TextMeshPro CreateTextInstance()
        {
            var go = new GameObject("UI_FlowBurstText");
            go.transform.SetParent(_poolRoot);

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = fontSize;
            tmp.color = coinTextColor;
            tmp.sortingOrder = 200;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;

            // MeshRenderer 설정: 그림자 비활성화
            if (go.TryGetComponent<MeshRenderer>(out var renderer))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            go.SetActive(false);
            return tmp;
        }

        private TextMeshPro GetFromPool()
        {
            EnsurePool();

            if (_pool.Count > 0)
            {
                var tmp = _pool.Dequeue();
                if (tmp != null) return tmp;
            }

            return CreateTextInstance();
        }

        private void ReturnToPool(TextMeshPro tmp)
        {
            if (tmp == null) return;
            tmp.gameObject.SetActive(false);
            _pool.Enqueue(tmp);
        }

        private void OnFlowBurst(FlowBurstEvent e)
        {
            var tmp = GetFromPool();
            tmp.text = $"+{e.Reward}";
            tmp.color = coinTextColor;

            // 타일 좌표 → 월드 좌표 변환 (XY / XZ 분기)
            Vector3 startPos = GetWorldPosition(e.Tile);
            tmp.transform.position = startPos;

            // 카메라를 향하도록 빌보드 회전 적용
            ApplyBillboardRotation(tmp.transform);

            tmp.transform.localScale = Vector3.one * initialScale;
            tmp.gameObject.SetActive(true);

            StartCoroutine(FloatAndFadeCoroutine(tmp, startPos));
        }

        private Vector3 GetWorldPosition(Vector2Int tile)
        {
            if (useXYPlane)
            {
                // 2D (XY 평면): GridUtil.GridToWorld 사용
                Vector3 pos = GridUtil.GridToWorld(tile);
                pos.z = -0.1f; // 카메라 앞쪽으로 살짝 이동
                return pos;
            }
            else
            {
                // 3D (XZ 평면): 타일 중심 + Y 오프셋
                return new Vector3(tile.x + 0.5f, 0.5f, tile.y + 0.5f);
            }
        }

        private void ApplyBillboardRotation(Transform target)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            if (useXYPlane)
            {
                // 2D: 정면을 바라봄
                target.rotation = Quaternion.identity;
            }
            else
            {
                // 3D: 카메라를 향해 빌보드
                target.rotation = cam.transform.rotation;
            }
        }

        private IEnumerator FloatAndFadeCoroutine(TextMeshPro tmp, Vector3 startPos)
        {
            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, floatDuration);
            Color baseColor = coinTextColor;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // 위치: 위로 떠오르기
                float yOffset = floatHeight * EaseOutQuad(t);
                tmp.transform.position = startPos + new Vector3(0f, yOffset, 0f);

                // 스케일: 빠르게 커졌다가 원래 크기로 안정
                float scaleT = t < 0.3f
                    ? Mathf.Lerp(initialScale, peakScale, t / 0.3f)
                    : Mathf.Lerp(peakScale, 1f, (t - 0.3f) / 0.7f);
                tmp.transform.localScale = Vector3.one * scaleT;

                // 투명도: 후반 50%부터 서서히 사라짐
                float alpha = t < 0.5f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.5f) / 0.5f);
                baseColor.a = alpha;
                tmp.color = baseColor;

                // 빌보드 유지
                ApplyBillboardRotation(tmp.transform);

                yield return null;
            }

            ReturnToPool(tmp);
        }

        private static float EaseOutQuad(float t)
        {
            return 1f - (1f - t) * (1f - t);
        }
    }
}

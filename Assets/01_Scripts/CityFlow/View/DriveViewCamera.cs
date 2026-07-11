using System.Collections.Generic;
using CityFlow.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityFlow.View
{
    // 드라이브 뷰(스펙 2026-07-12): 활성 통근 경로를 저공 1인칭으로 달리는 우상단 PiP.
    // 씬 배선 0 — MainCityView.Initialize가 런타임 AddComponent. 신호에 안 멈춤(그린웨이브 환상이 의도).
    public sealed class DriveViewCamera : MonoBehaviour
    {
        [SerializeField] private float speed = 2f;            // 타일/초
        [SerializeField] private float height = 0.9f;         // 보드 위 높이(−z 방향)
        [SerializeField] private float lookDown = 0.45f;      // 전방의 아래(+z) 성분 = 틸트
        [SerializeField] private Rect viewport = new Rect(0.72f, 0.72f, 0.27f, 0.27f);

        private SimEngine simEngine;
        private float tileSize;
        private Camera cam;
        private List<Vector2Int> route;
        private float phase;
        private bool enabledByUser = true;                    // 기본 ON — 경로 없으면 자동 숨김

        public void Init(SimEngine engine, Transform viewRoot, float tile)
        {
            simEngine = engine;
            tileSize = tile;

            GameObject go = new GameObject("DriveViewCamera");
            go.transform.SetParent(viewRoot, false);
            cam = go.AddComponent<Camera>();                  // AudioListener 없음 — 메인과 중복 방지
            cam.rect = viewport;
            cam.depth = (Camera.main != null ? Camera.main.depth : 0f) + 1f;
            cam.fieldOfView = 65f;
            cam.nearClipPlane = 0.05f;
        }

        private void OnDestroy()
        {
            if (cam != null)
            {
                Destroy(cam.gameObject);
            }
        }

        private void Update()
        {
            if (cam == null || simEngine == null)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.dKey.wasPressedThisFrame)
            {
                enabledByUser = !enabledByUser;
            }

            if (!enabledByUser || !EnsureRoute())
            {
                cam.enabled = false;
                return;
            }

            cam.enabled = true;
            phase += Time.deltaTime * speed;
            if (phase >= route.Count - 1)
            {
                route = null;                                 // 종점 — 다음 프레임 최장 경로 재선택
                phase = 0f;
                return;
            }

            int index = Mathf.FloorToInt(phase);
            float t = phase - index;
            Vector3 a = TileToLocal(route[index]);
            Vector3 b = TileToLocal(route[index + 1]);
            Vector3 dir = (b - a).normalized;
            cam.transform.localPosition = Vector3.Lerp(a, b, t) + new Vector3(0f, 0f, -height);
            // 전방 = 진행 방향 + 아래 틸트, up = 보드에서 카메라 쪽(−z) — 큐브 도시를 스치는 저공 시점.
            Vector3 forward = (dir + new Vector3(0f, 0f, lookDown)).normalized;
            cam.transform.localRotation = Quaternion.LookRotation(forward, Vector3.back);
        }

        // 최장 활성 경로 선택 — 신호를 가장 많이 지나는 경로가 그린웨이브 과시에 최적(스펙 §핵심결정).
        // 경로 리스트는 솔버 캐시 참조 — topology 변경으로 비면 Count 가드가 다음 프레임 재선택.
        private bool EnsureRoute()
        {
            if (route != null && route.Count >= 2)
            {
                return true;
            }

            IReadOnlyList<List<Vector2Int>> routes = simEngine.ActiveRoutes;
            List<Vector2Int> longest = null;
            for (int i = 0; i < routes.Count; i++)
            {
                if (routes[i] != null && routes[i].Count >= 2
                    && (longest == null || routes[i].Count > longest.Count))
                {
                    longest = routes[i];
                }
            }

            route = longest;
            phase = 0f;
            return route != null;
        }

        private Vector3 TileToLocal(Vector2Int tile)
        {
            return new Vector3((tile.x + 0.5f) * tileSize, (tile.y + 0.5f) * tileSize, 0f);
        }
    }
}

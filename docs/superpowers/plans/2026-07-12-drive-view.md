# 드라이브 뷰 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 활성 통근 경로를 저공 1인칭으로 달리는 우상단 PiP 카메라 — `D` 토글, 씬 배선 0.

**Architecture:** 신규 `View/DriveViewCamera.cs`(런타임 카메라 생성·경로 추적), MainCityView.Initialize에서 `AddComponent` 한 줄 배선. 뷰 전용 — 엔진·계약·세이브·씬 무변경.

**Tech Stack:** Unity(URP base 카메라 viewport rect, InputSystem), Unity MCP. 스펙: `docs/superpowers/specs/2026-07-12-drive-view-design.md`.

## Global Constraints

- 브랜치 `feat-drive-view-hwan`(스택: feat-view-popups-hwan 위). 커밋 접두 `[Feat]`.
- 씬 파일·프리팹 변경 금지(런타임 생성만). AudioListener 추가 금지(메인 카메라와 중복 경고).
- `simEngine`이 null일 수 있음(Fake 환경) — Update가 조용히 no-op.
- 기존 스위트 151개 그린(컴파일 게이트). Unity MCP: refresh(force)→콘솔 CS→run_tests job 폴링. Thread.Sleep 금지. Play 스모크 후 워킹트리 클린.

---

### Task 1: DriveViewCamera + 배선

**Files:**
- Create: `Assets/01_Scripts/CityFlow/View/DriveViewCamera.cs`
- Modify: `Assets/01_Scripts/CityFlow/View/MainCityView.cs` (Initialize 말미 1줄)

**Interfaces:**
- Consumes: `SimEngine.ActiveRoutes`(IReadOnlyList<List<Vector2Int>>, public — 뷰 연동용 기존 창구), MainCityView의 `simEngine`/`transform`/`tileSize`.
- Produces: 뷰 전용 — 후속 소비자 없음.

- [ ] **Step 1: DriveViewCamera.cs 신규**

```csharp
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
```

- [ ] **Step 2: MainCityView 배선** — `Initialize`의 `RefreshVehicles();` 아래(마지막 줄)에:

```csharp
            gameObject.AddComponent<DriveViewCamera>().Init(simEngine, transform, tileSize);
```

- [ ] **Step 3: 컴파일 확인** — `refresh_unity`(force) → `read_console` CS 에러 0. 신규 파일 .meta 생성 확인.

- [ ] **Step 4: Play 프로그래매틱 스모크** — 비포커스 규약(isPaused+Step 펌핑):
  1. Play 진입 → 도시 구성(도로 일자+집+회사 → 경로 생성) → 펌핑 → `GameObject.Find("DriveViewCamera")`의 Camera: `enabled==true`, `rect` 우상단(0.72, 0.72), `depth` 메인+1 확인.
  2. 펌핑 전후 카메라 `localPosition` 변화 확인(주행 증거) — 두 샘플 거리 > 0.1.
  3. 도시 철거(전체 Remove) 또는 빈 도시로 재시작 → 경로 0 → `cam.enabled == false` 확인(자동 숨김).
  4. Play 종료 → 워킹트리 클린 확인. 환경 문제로 막히면 컴파일+151 그린으로 DONE_WITH_CONCERNS.

- [ ] **Step 5: 전체 회귀** — EditMode 151/151.

- [ ] **Step 6: 커밋**

```bash
cd ~/Gamemaker/GreenLight
git add Assets/01_Scripts/CityFlow/View/DriveViewCamera.cs Assets/01_Scripts/CityFlow/View/DriveViewCamera.cs.meta Assets/01_Scripts/CityFlow/View/MainCityView.cs
git commit -m "[Feat] 드라이브 뷰 — 최장 경로 저공 1인칭 PiP, D 토글, 씬 배선 0"
```

---

## 완료 기준

- EditMode 151/151(신규 테스트 없음 — 뷰 전용).
- Play 스모크: PiP 카메라 존재·주행(위치 변화)·경로 0일 때 자동 숨김, 워킹트리 클린.
- 씬 파일 diff 0(런타임 생성 검증).

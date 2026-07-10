# 오버라이드 그린 코리도어 버스트 + FlowBurstJuice — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 오버라이드를 "일자 라인 신호 최대 3개를 3초 강제 초록으로 뚫는 짧은 버스트"로 바꾸고, FlowBurst에 사운드·카메라 펀치를 더한다.

**Architecture:** A(엔진) = `SimEngine.TryOverrideSignal`을 코리도어 수집(순수 grid walk)로 확장 + SimConfig 파라미터. A(뷰) = MainCityView가 E-1 계약(`GetOverrideSecondsLeft`)으로 오버라이드 라인 차량 가속 + 신호 펄스 FX. B = `FlowBurstJuice` 독립 컴포넌트가 FlowBurst 이벤트로 SoundManager 사운드 + DOTween 카메라 셰이크.

**Tech Stack:** Unity 6000.5, C#, EditMode 테스트(NUnit), DOTween(Assets/Plugins/Demigiant), Unity MCP(컴파일·테스트 검증).

## Global Constraints

- 결정론 불변: 엔진 변경은 순수함수(grid walk 고정 순서). 세이브에 오버라이드 저장 안 함(현행 유지).
- 브랜치: `feat-override-corridor-hwan` (이미 체크아웃됨, E-1 `91cc33d` 위, 스펙 커밋 `45c5ffe` 포함).
- 커밋 접두 `[Feat]` (팀 규칙). 커밋 끝에 `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- 검증은 Unity MCP: 편집 후 `AssetDatabase.Refresh(ForceUpdate)`로 강제 임포트(외부 편집은 강제 리프레시 안 하면 콘솔이 stale) → `read_console` 0 에러 확인 → `run_tests EditMode`.
- baseline EditMode = 99 통과. 회귀 0 유지.

---

### Task 1: FlowBurstJuice (B — 사운드 + 카메라 펀치)

**Files:**
- Create: `Assets/01_Scripts/CityFlow/View/FlowBurstJuice.cs`
- Scene(수동, Step 4): `Assets/00_Scenes/CityFlowIntegrated_cmt.unity` — 빈 GameObject에 컴포넌트 부착

> **유닛 테스트 없음(환 승인 2026-07-10)**: EditMode 테스트 asmdef(`CityFlow.Sim.Tests`)는 Assembly-CSharp(View 소속)를 참조할 수 없고(Unity asmdef 규칙), Reward 매핑은 클램프 한두 줄 수학(trivial). 검증 = 컴파일 0에러 + 플레이.

**Interfaces:**
- Consumes: `CityFlow.Bootstrap.ICityFlowServiceConsumer.Initialize(CityFlowServices)`; `CityFlowServices.Events.FlowBurst` (event of `FlowBurstEvent{Vector2Int Tile; int Reward}`); `CityFlow.Managers.SoundManager.Instance?.PlaySfx(string, float)`.
- Produces: 없음(독립 연출 컴포넌트). `VolumeFor`/`ShakeStrengthFor`는 내부 static 순수 매핑.

- [ ] **Step 1: FlowBurstJuice 구현**

`Assets/01_Scripts/CityFlow/View/FlowBurstJuice.cs`:

```csharp
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Managers;
using DG.Tweening;
using UnityEngine;

namespace CityFlow.View
{
    // FlowBurst(체증 해소 보상) 청각·카메라 연출 전용. 엔진 이벤트만 듣는 독립 유닛 —
    // 버스트 비주얼(FlowBurstView·MainCityView)과 무관, 뷰 교체·중복에 안 흔들림.
    public sealed class FlowBurstJuice : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private string burstSfxId = "flow_burst";
        [SerializeField] private float shakeDuration = 0.2f;

        public const float MaxShakeStrength = 0.4f;   // 멀미 방지 상한(월드 유닛)

        private CityFlowServices services;

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled) return;
            this.services = services;
            services.Events.FlowBurst += OnFlowBurst;
        }

        private void OnDestroy()
        {
            if (services != null) services.Events.FlowBurst -= OnFlowBurst;
        }

        private void OnFlowBurst(FlowBurstEvent e)
        {
            // 사운드: 카탈로그에 클립 없으면 SoundManager가 조용히 no-op(에셋 없어도 무사고).
            SoundManager.Instance?.PlaySfx(burstSfxId, VolumeFor(e.Reward));

            // 카메라 펀치: 2D 직교라 xy만. SetUpdate(true) = 일시정지 무관.
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.transform.DOKill();
                cam.transform.DOShakePosition(shakeDuration, ShakeStrengthFor(e.Reward))
                    .SetUpdate(true);
            }
        }

        // Reward → SFX 볼륨 [0,1]. 보상 10에서 대략 최대치 근처(로그 완만).
        public static float VolumeFor(int reward)
        {
            if (reward <= 0) return 0f;
            return Mathf.Clamp01(reward / 10f);
        }

        // Reward → 카메라 셰이크 세기 [0, MaxShakeStrength].
        public static float ShakeStrengthFor(int reward)
        {
            if (reward <= 0) return 0f;
            return Mathf.Min(MaxShakeStrength, MaxShakeStrength * (reward / 10f));
        }
    }
}
```

- [ ] **Step 2: 컴파일 검증**

Unity MCP: `execute_code` → `UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceUpdate);` (도메인 리로드로 타임아웃 정상) → `editor/state` 리소스로 `is_compiling:false` 확인 → `read_console` types=["Error"] → 0 에러. 이어서 `run_tests` mode=EditMode(전체)로 기존 99 회귀 없음 확인.

- [ ] **Step 3: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/View/FlowBurstJuice.cs
git commit -m "[Feat] FlowBurstJuice — FlowBurst 사운드+카메라 펀치 (B)

SoundManager 경유 SFX(Reward 비례, 카탈로그 없으면 no-op) + DOTween 카메라 셰이크.
독립 컴포넌트(엔진 이벤트 구독) — 버스트 비주얼과 무관.
유닛테스트 없음(asmdef 경계 — plan 참조), 검증=컴파일+플레이.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

- [ ] **Step 4: 씬 배선(수동, 플레이 검증 시)**

`CityFlowIntegrated_cmt.unity` 열어 빈 GameObject("FlowBurstJuice") 생성 → `FlowBurstJuice` 컴포넌트 부착. CityBootstrap.InstallServices가 씬의 ICityFlowServiceConsumer를 자동 Initialize하므로 추가 배선 불필요. (Task 3 플레이 검증 때 함께 확인.)

---

### Task 2: 코리도어 오버라이드 (A — 엔진)

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimConfig.cs` (필드 추가 + Default 튜닝)
- Modify: `Assets/05_ScriptableObjects/SimConfig.asset` (신규 필드값 + Duration/Cooldown)
- Modify: `Assets/01_Scripts/CityFlow/Sim/SimEngine.cs:183-192` (TryOverrideSignal + 헬퍼)
- Test: `Assets/Tests/EditMode/SimEngineTests.cs` (테스트 추가)

**Interfaces:**
- Consumes: `SimConfig.OverrideCorridorSignals` (int), `SimConfig.OverrideDurationSeconds`, `SimConfig.OverrideCooldownSeconds`; `SignalMap.TryGet`; `CityGrid.GetTile/Width/Height` (같은 어셈블리 internal).
- Produces: `bool SimEngine.TryOverrideSignal(Vector2Int, bool)` — 이제 anchor 성공 시 코리도어 신호 전체를 강제 초록. 시그니처·반환 규약 불변(호출부 무영향).

- [ ] **Step 1: SimConfig에 코리도어 필드 추가 + Default 튜닝**

`SimConfig.cs` — 오버라이드 섹션(현재 `OverrideCooldownSeconds` 아래)에 필드 추가:

```csharp
        public float OverrideDurationSeconds;
        public float OverrideCooldownSeconds;
        public int   OverrideCorridorSignals;   // 코리도어 최대 신호 수(anchor 포함). 라인이 짧으면 그만큼만.
```

`Default()`에서 세 값 변경:

```csharp
            // 코리도어 버스트: 3초 강제 초록(일자 라인 최대 3신호) + 60초 쿨다운 = 업타임 ~5%, 짧고 강한 스킬.
            OverrideDurationSeconds = 3f,
            OverrideCooldownSeconds = 60f,
            OverrideCorridorSignals = 3,
```

- [ ] **Step 2: SimConfig.asset 갱신**

`Assets/05_ScriptableObjects/SimConfig.asset`는 old Default로 직렬화돼 신규 필드가 없으면 0(코리도어 죽음). Unity MCP로 값 반영:

`execute_code`:
```csharp
var so = UnityEditor.AssetDatabase.LoadAssetAtPath<CityFlow.Configs.SimConfigAsset>("Assets/05_ScriptableObjects/SimConfig.asset");
so.Value.OverrideDurationSeconds = 3f;
so.Value.OverrideCooldownSeconds = 60f;
so.Value.OverrideCorridorSignals = 3;
UnityEditor.EditorUtility.SetDirty(so);
UnityEditor.AssetDatabase.SaveAssets();
return "asset updated";
```
(참고: RushAmplitude 등 asset 고유 튜닝값은 건드리지 않음 — 위 3개만.)

- [ ] **Step 3: 실패 테스트 작성** — 코리도어 수집

`SimEngineTests.cs`의 override 테스트(`OverrideSignal_ForcesAxisGreen_ThenCooldownAndExpiry`) 아래에 추가:

```csharp
        [Test]
        public void OverrideSignal_Corridor_GreensCollinearSignals_UpToConfigCount()
        {
            // 가로 간선 y=2에 교차로 3개(x=2,5,8; 각 세로 가지). anchor=(5,2) → 최근접 3개 코리도어.
            var c = Cfg(0.25f);
            c.GridWidth = 12; c.GridHeight = 5;
            c.OverrideDurationSeconds = 0.5f;
            c.OverrideCooldownSeconds = 1f;
            c.OverrideCorridorSignals = 3;
            var e = new SimEngine(c, new SimEventHub());
            for (int x = 0; x <= 10; x++) e.Place(V(x, 2), TileType.Road);   // 가로 간선
            e.Place(V(2, 3), TileType.Road);   // (2,2)를 교차로로
            e.Place(V(5, 3), TileType.Road);   // (5,2)
            e.Place(V(8, 3), TileType.Road);   // (8,2)
            e.Tick(0.25f);                     // 교차로 감지

            Assert.IsTrue(e.TryOverrideSignal(V(5, 2), horizontal: true));
            Assert.Greater(e.GetOverrideSecondsLeft(V(5, 2)), 0f);   // anchor
            Assert.Greater(e.GetOverrideSecondsLeft(V(2, 2)), 0f);   // 좌 최근접
            Assert.Greater(e.GetOverrideSecondsLeft(V(8, 2)), 0f);   // 우 최근접
        }

        [Test]
        public void OverrideSignal_Corridor_SingleIntersection_BehavesLikeSingle()
        {
            // 고립 교차로 1개면 코리도어=1 → 기존 단일 오버라이드와 동일.
            var c = Cfg(0.25f);
            c.GridWidth = 9; c.GridHeight = 2;
            c.OverrideDurationSeconds = 0.5f;
            c.OverrideCooldownSeconds = 1f;
            c.OverrideCorridorSignals = 3;
            var e = new SimEngine(c, new SimEventHub());
            for (int x = 0; x <= 8; x++) e.Place(V(x, 0), TileType.Road);
            e.Place(V(4, 1), TileType.Road);
            e.Tick(0.25f);

            Assert.IsTrue(e.TryOverrideSignal(V(4, 0), horizontal: true));
            Assert.Greater(e.GetOverrideSecondsLeft(V(4, 0)), 0f);
            Assert.AreEqual(0f, e.GetOverrideSecondsLeft(V(0, 0)));   // 신호 아님 → 0
        }
```

- [ ] **Step 4: 테스트 실패 확인**

Unity MCP: `run_tests` mode=EditMode, `test_names=["CityFlow.Tests.SimEngineTests"]`.
Expected: 신규 2종 FAIL(현재 anchor만 override → (2,2)/(8,2) 는 0).

- [ ] **Step 5: TryOverrideSignal 코리도어 확장 구현**

`SimEngine.cs` — 기존 `TryOverrideSignal`(183-192) 교체 + 헬퍼 2개와 버퍼 필드 추가:

```csharp
        readonly Dictionary<Vector2Int, double> _overrideReadyAt = new();
        readonly List<Vector2Int> _corridorBuf = new();   // 코리도어 수집 재사용 버퍼(비-재진입)

        public bool TryOverrideSignal(Vector2Int tile, bool horizontal)
        {
            if (!_signals.TryGet(tile, out _)) return false;
            if (_overrideReadyAt.TryGetValue(tile, out var ready) && _simTime < ready) return false;

            CollectCorridor(tile, horizontal, _corridorBuf);   // anchor + 일자 라인 최근접 신호
            double until = _simTime + _config.OverrideDurationSeconds;
            for (int i = 0; i < _corridorBuf.Count; i++)
            {
                if (!_signals.TryGet(_corridorBuf[i], out var s)) continue;
                s.OverrideUntil = until;
                s.OverrideHorizontal = horizontal;
                _overrideReadyAt[_corridorBuf[i]] = until + _config.OverrideCooldownSeconds;
            }
            return true;
        }

        // 코리도어: anchor에서 선택 축(가로=x, 세로=y)으로 연속 도로를 걸으며 교차로 신호를
        // 양방향 최근접부터 번갈아 수집(anchor 포함 최대 OverrideCorridorSignals개). "직진만".
        void CollectCorridor(Vector2Int anchor, bool horizontal, List<Vector2Int> outTiles)
        {
            outTiles.Clear();
            outTiles.Add(anchor);
            int max = Mathf.Max(1, _config.OverrideCorridorSignals);
            var step = horizontal ? new Vector2Int(1, 0) : new Vector2Int(0, 1);
            Vector2Int fwd = anchor, bwd = anchor;
            bool fwdAlive = true, bwdAlive = true;
            while (outTiles.Count < max && (fwdAlive || bwdAlive))
            {
                if (fwdAlive)
                {
                    if (TryNextSignalAlong(ref fwd, step, out var sf)) outTiles.Add(sf);
                    else fwdAlive = false;
                }
                if (outTiles.Count >= max) break;
                if (bwdAlive)
                {
                    if (TryNextSignalAlong(ref bwd, -step, out var sb)) outTiles.Add(sb);
                    else bwdAlive = false;
                }
            }
        }

        // cursor에서 step 방향으로 도로를 걸으며 다음 교차로 신호를 찾는다. 도로 끊기면 false.
        bool TryNextSignalAlong(ref Vector2Int cursor, Vector2Int step, out Vector2Int signal)
        {
            signal = default;
            var t = cursor + step;
            while (t.x >= 0 && t.x < _grid.Width && t.y >= 0 && t.y < _grid.Height
                   && _grid.GetTile(t) == TileType.Road)   // GetTile은 OOB 미검사 → 직접 가드
            {
                if (_signals.TryGet(t, out _)) { cursor = t; signal = t; return true; }
                t += step;
            }
            return false;
        }
```

- [ ] **Step 6: 컴파일 + 전체 테스트**

Unity MCP: `execute_code` ForceUpdate refresh → `read_console` 0 에러 → `run_tests` mode=EditMode(전체).
Expected: 신규 2종 PASS + 기존 99 PASS(총 101). `OverrideSignal_ForcesAxisGreen_ThenCooldownAndExpiry` 불변(고립 교차로 → 코리도어=1).

- [ ] **Step 7: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/Sim/SimConfig.cs Assets/05_ScriptableObjects/SimConfig.asset Assets/01_Scripts/CityFlow/Sim/SimEngine.cs Assets/Tests/EditMode/SimEngineTests.cs
git commit -m "[Feat] 오버라이드 코리도어: 일자 라인 신호 최대 3개 강제 초록 (A-엔진)

TryOverrideSignal이 anchor에서 선택 축 연속 도로를 걸으며 교차로 신호를
최근접 순 최대 N개 수집해 함께 강제 초록. SimConfig 지속 20→3s·쿨 30→60s·
OverrideCorridorSignals=3 신설. grid walk 순수함수라 결정론 불변. 테스트 2종.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: 오버라이드 속도 부스트 + 신호 펄스 FX (A — 뷰)

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/View/MainCityView.cs` (필드 + MoveVehicle + ApplySignalState)

**Interfaces:**
- Consumes: `signalControl.GetOverrideSecondsLeft(Vector2Int)` (E-1 계약, `91cc33d`); 기존 `MoveVehicle`/`ApplySignalState` 지역 로직.
- Produces: 없음(순수 뷰). 검증 = 플레이(유닛 테스트 없음 — 렌더 연출).

- [ ] **Step 1: 부스트/펄스 튜닝 필드 추가**

`MainCityView.cs` — `[Header("Runtime Visuals")]` 블록에 추가:

```csharp
        [SerializeField] private float overrideSpeedMul = 2.2f;    // 오버라이드 라인 차량 속도 배율
        [SerializeField] private float overridePulseAmp = 0.25f;   // 신호 펄스 진폭
```

- [ ] **Step 2: MoveVehicle에 속도 부스트 주입**

`MoveVehicle`(487~) 안, `bool blockedBySignal = IsRouteVehicleBlocked(...)` **직전**에 삽입:

```csharp
            // 오버라이드 라인 가속: 이 차가 향하는 다음 신호가 오버라이드 중이면 시각 속도↑(순수 연출).
            if (signalControl != null && TryGetNextSignalTile(route, vehicle.Phase, out Vector2Int aheadSignal)
                && signalControl.GetOverrideSecondsLeft(aheadSignal) > 0f)
            {
                speed *= overrideSpeedMul;
            }
```

그리고 `IsRouteVehicleBlocked`가 계산하던 "다음 신호 타일"을 재사용 가능한 헬퍼로 분리 — `MainCityView.cs`에 추가:

```csharp
        // 차의 현재 위상에서 진행 방향으로 인접한 신호 타일(있으면). MoveVehicle·블록 판정 공용.
        private bool TryGetNextSignalTile(List<Vector2Int> route, float phase, out Vector2Int next)
        {
            next = default;
            if (route == null || route.Count < 2) return false;
            int segmentCount = route.Count - 1;
            float cycle = segmentCount * 2f;
            float p = Mathf.Repeat(phase, cycle);
            bool forward = p <= segmentCount;
            float folded = forward ? p : cycle - p;
            int index = Mathf.Clamp(Mathf.FloorToInt(folded), 0, segmentCount - 1);
            Vector2Int current = forward ? route[index] : route[index + 1];
            Vector2Int candidate = forward ? route[index + 1] : route[index];
            if (current == candidate || !IsSignalTile(candidate)) return false;
            next = candidate;
            return true;
        }
```

- [ ] **Step 3: ApplySignalState에 오버라이드 펄스 FX**

`ApplySignalState`(396~) 끝(`if (visual.SelectionRenderer ...)` 블록 뒤)에 추가:

```csharp
            // 오버라이드 특수효과: 코리도어 신호를 초록 방향으로 스케일 펄스(폴링 — 뷰가 매 프레임 갱신).
            bool overridden = signalControl != null && signalControl.GetOverrideSecondsLeft(tile) > 0f;
            float pulse = overridden
                ? 1f + overridePulseAmp * Mathf.Abs(Mathf.Sin(Time.time * 8f))
                : 1f;
            visual.Root.transform.localScale = Vector3.one * pulse;
```

- [ ] **Step 4: 컴파일 검증**

Unity MCP: `execute_code` ForceUpdate refresh → `read_console` types=["Error"] → 0 에러.

- [ ] **Step 5: 전체 EditMode 회귀**

Unity MCP: `run_tests` mode=EditMode(전체). Expected: 101 PASS(뷰 변경은 테스트 무영향).

- [ ] **Step 6: 플레이 검증(수동)**

`CityFlowIntegrated_cmt.unity` Play(Task 1 Step 4의 FlowBurstJuice도 부착된 상태):
- ① 신호 탭 오버라이드 → 라인 신호 최대 3개 동시 초록 ~3초, 신호 펄스 FX.
- ② 그 라인 차량 눈에 띄게 빨라짐(간격 과하게 벌어지면 `overrideSpeedMul` 낮춤).
- ③ 쿨다운 60초 동안 재탭 거절.
- ④ FlowBurst 발생(회사 증설 등) 시 카메라 톡 + (카탈로그에 클립 있으면)비프, Reward 클수록 세게.

- [ ] **Step 7: 커밋**

```bash
git add Assets/01_Scripts/CityFlow/View/MainCityView.cs
git commit -m "[Feat] 오버라이드 뷰 연출: 라인 차량 가속 + 신호 펄스 FX (A-뷰)

MainCityView가 E-1 계약(GetOverrideSecondsLeft)으로 오버라이드 라인의
다음 신호를 조회해 그쪽 향하는 차량 시각 속도↑ + 코리도어 신호 스케일 펄스.
순수 연출(처리량·코인·결정론 무관).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## 완료 후

- PR to develop (`feat-override-corridor-hwan`). **주의**: 이 브랜치는 E-1(`91cc33d`)을 포함 — E-1은 계약 변경이라 김건 합의 대상. PR 설명에 "E-1 계약 승격 포함, 오버라이드 뷰가 이에 의존" 명시.
- A-2(엔진 용량 부스트)는 플레이 후 별도 판단(스펙 비범위).
- 후속: FlowBurstView/MainCityView 버스트 비주얼 중복 정리(별개 태스크), SoundCatalog에 `flow_burst` 클립 추가(아티스트).

## Self-Review 결과

- **스펙 커버리지**: A-엔진(Task2)·A-뷰(Task3)·B(Task1)·비범위(완료후)·검증(각 Task Step)·파라미터(Task2 Step1-2) 전부 매핑됨.
- **플레이스홀더**: 없음(모든 코드 블록 실체).
- **타입 일관성**: `GetOverrideSecondsLeft`(float, E-1)·`FlowBurstEvent{Tile,Reward:int}`·`OverrideCorridorSignals`(int)·`VolumeFor/ShakeStrengthFor/MaxShakeStrength`(static) 태스크 간 일치. `TryGetNextSignalTile`은 Task3 내 정의·사용.

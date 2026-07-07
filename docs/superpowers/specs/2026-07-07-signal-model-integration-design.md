# 신호 모델 통합 설계 — 엔진 그린웨이브 ↔ 뷰 방향신호 (Gap #2)

> 2026-07-07 · 환 · CityFlow(도시흐름) 시그널 통합
> 선행(LLM_WIKI/output/designs/traffic-spirit/): `signal-integration-design-2026-07-07.md`, `dev-log-06.md` §갭2, `debugging-traffic-deadlock-2026-07-07.md`
> 대상 코드: `Assets/01_Scripts/CityFlow/Sim/SignalMath.cs`, `FlowSolver.cs`, `SimEngine.cs`, `Debug/SimTileRenderer.cs`

---

## 1. 문제 (Gap #2)

같은 `Signal` 데이터 위에 **서로 무관한 신호 공식 두 개**가 돌고 있다:

| | 엔진(처리량) | 뷰(신호등) |
|---|---|---|
| 함수 | `SignalMath.GreenWaveEfficiency(from,to,travelSlots,floor)` | `SignalMath.PhaseForAxis(s,time,horizontal)` |
| 소비 | `FlowSolver.SignalFactor` → `delivered` | `SimTileRenderer` (via `SimEngine.GetSignalPhase`) |
| 성질 | 축 없음, 초록창 길이 무시, 오프셋 정렬만 보는 스칼라 | 방향 교대(반주기) + 초록/노랑/전적색, 축별 G/Y/R |

둘 다 `OffsetSlots`를 읽지만 계산이 독립이라 **화면과 처리량이 모순**될 수 있다(예: 엔진 효율 1.0인데 화면 차는 빨간불에 정지). 코어 메커니즘("오프셋 조율 → 눈으로 보는 흐름 → 보상")의 손맛이 깨진다.

경위: 원래 엔진 그린웨이브 모델만 있었고, 뷰 데드락 응급수정으로 방향 교대 모델 `PhaseForAxis`를 **뷰에만** 넣으면서 갈라짐. 엔진 경제식은 옛날 공식 그대로.

## 2. 결정

- **진실의 원천 = 엔진.** 처리량 스칼라는 엔진이 계산(권위 유지). 뷰는 그 결과를 그려주기만.
- **단일 신호 타이밍 모델.** `SignalMath`에 **공용 초록창 프리미티브** 하나를 두고, `PhaseForAxis`(뷰)와 효율 계산(엔진)이 **둘 다 그 프리미티브에서 파생**한다 → 구조적으로 못 갈라짐.
- **엔진은 per-car 시뮬 안 함.** rate 모델(SoA·결정론·가벼움) 유지. 효율은 정상류에서 "차가 초록 잡는 비율"의 **해석적(closed-form) 값** — per-car가 수렴할 값과 동일하므로 화면과 일치하면서 싸다.
- **재화 = 도착 수 × `CoinBase`** (이미 `ArrivalEmitter`에 존재). `CoinBase`=1로 "집→회사 도착마다 +1", 거리/가치 배수는 나중(확장 손잡이만 남김).
- **재밸런싱은 나중.** 방향 교대 반영으로 처리량이 내려가는 것은 의도된 결과. 숫자는 `CoinBase`·`GreenWaveFloor`·`CycleSlots`·`GreenSlots`로 나중에 조율.

## 3. 아키텍처

### 3.1 공용 프리미티브 (`SignalMath`)

방향 교대 모델에서 한 축의 **초록창**을 반환하는 순수 함수. 오프셋·YellowFrac/ClearFrac 반영.

```
// 한 축(가로/세로)의 초록창이 열리는 시각(주기 내, 초)과 길이.
(double openTime, double greenLen) GreenWindowFor(Signal s, bool horizontal)
```

- `half = CycleSlots*SlotSeconds/2`; `greenLen = half*(1 - YellowFrac - ClearFrac)`
- 축 시작 위상 `axisStart` = 가로 0, 세로 `half`
- 오프셋 반영: `openTime = ((axisStart - OffsetSlots*SlotSeconds) mod cycle)`
- `PhaseForAxis`는 이 창을 기준으로 G/Y/R 판정하도록 내부만 리팩터 (동작·테스트 불변).

### 3.2 엔진 효율 (`SignalMath.GreenWaveEfficiency` 교체)

새 시그니처 — **축 인식** + **실제 초록창 사용**:

```
float GreenWaveEfficiency(Signal from, Signal to, int travelSlots, bool horizontal, float floor)
```

계산(그린웨이브 = 상류 초록 선두에 출발한 흐름이 하류 초록창에 안착하나):
1. `openFrom = GreenWindowFor(from, horizontal).openTime`
2. 도착 위상 `arrive = (openFrom + travelSlots*SlotSeconds) mod cycle`
3. `openTo, greenLen = GreenWindowFor(to, horizontal)`
4. `δ = (arrive - openTo) mod cycle`
5. 초록창 안(`δ ∈ [0, greenLen)`) → **효율 1.0 (플래토: 초록 어디 잡아도 통과)**
6. 밖이면 초록창까지 원형 최단거리 `gap = min(δ - greenLen, cycle - δ)`, 최악 `maxGap = (cycle - greenLen)/2`
   → `efficiency = 1 - clamp01(gap/maxGap) * (1 - floor)`

특성: 초록 안착=1, 반대 절반 한복판(깊은 빨강)=floor, 선형·연속. 초록창 길이를 쓰므로 뷰와 같은 창.

> `ponytail:` 인접 교차로 사이에서 흐름이 **회전**(출발 축 ≠ 도착 축)하면, 도착 축 기준으로 근사. 정밀 회전 위상은 2차.

### 3.3 `FlowSolver.SignalFactor` — 축 전달

경로를 훑다 신호쌍을 만나면, `to`로 **들어가는 스텝 방향**으로 축을 정해 넘긴다(뷰 `PoseOf`의 `Horizontal`과 동일 소스):

```
horizontal = |path[p].x - path[p-1].x| >= |path[p].y - path[p-1].y|
e = GreenWaveEfficiency(prev, sig, p - prevIdx, horizontal, cfg.GreenWaveFloor)
factor = min(factor, e)   // 병목 철학 유지
```
신호 0~1개면 factor=1(변화 없음). `Resolve(cfg)`(신호 null) 경로도 그대로 1.

### 3.4 뷰 (`PhaseForAxis` / `SimTileRenderer`)

- `PhaseForAxis` 내부만 `GreenWindowFor` 경유 → **화면·데드락 방지·노랑/전적색 전부 불변.**
- 배선(`SimEngine.GetSignalPhase`) 그대로. 뷰 코드 변경 없음.

### 3.5 재화 (변경 없음)

`ArrivalEmitter`가 `delivered` rate를 적분해 도착 정수 → `ArrivalEvent(tile, CoinBase)` 방출. 신호가 나빠지면 `delivered`↓ → 도착↓ → 코인↓. **A(재화)와 B(뷰)가 같은 신호 타이밍 하나에서 흐름 → 병합 완료.** `CoinBase`가 확장 손잡이(거리·가치는 나중).

## 4. 데이터 흐름 (통합 후)

```
유저가 오프셋 조율
   └─> Signal.OffsetSlots
         ├─(엔진) FlowSolver.SignalFactor → GreenWaveEfficiency(GreenWindowFor) → delivered → ArrivalEmitter → 코인
         └─(뷰)   SimEngine.GetSignalPhase → PhaseForAxis(GreenWindowFor) → 차 게이팅/신호등 색
         ▲ 같은 GreenWindowFor 하나 = 보는 것 = 버는 것
```

## 5. 결정론

- `SignalMap.Tiles` flat 인덱스 순회 유지, Dictionary 순회 의존 없음.
- per-car 랜덤 없음(효율은 해석적). 기존 "같은 입력=같은 해시" 테스트가 회귀 방지.

## 6. 범위 밖 (2차)

- 처리량 숫자 리밸런싱(용량 절반 상쇄)
- `GreenRatio`(초록 길이 → 용량 감소) 레버 통합
- 교차로 사이 회전 정밀 위상
- 다른 주기 신호쌍
- 거리/가치 기반 코인 배수
- `ISignalControl` Contracts 정식화(한주석·김건)

## 7. 테스트 (TDD)

| # | 테스트 | 검증 |
|---|---|---|
| 1 | `GreenWindowFor` 가로/세로 | 두 창이 반주기 어긋나고 겹치지 않음 |
| 2 | `PhaseForAxis` 리팩터 회귀 | 기존 G/Y/R 케이스 전부 동일 |
| 3 | 정렬된 2신호(오프셋=이동시간) | 효율 1.0 |
| 4 | 반주기 어긋난 2신호 | 효율 = floor 근처 |
| 5 | 축 구분 | 같은 오프셋도 가로/세로 경로 효율 다름 |
| 6 | 신호 0~1개 경로 | SignalFactor=1 |
| 7 | 오프셋 조작 e2e | `TrySetSignalOffsetSlots`로 정렬 → delivered 상승 |
| 8 | **일치(anti-drift) — 핵심** | 뷰가 초록 물결로 보이는 오프셋 = 엔진 효율 ≈1.0 단언. 두 모델 재분리 시 **이 테스트가 빨개짐.** |
| 9 | 결정론 | 신호 도시 동일입력=동일해시 |

테스트 8이 Gap #2 재발 방지 못(regression guard) — 통합의 핵심 자산.

## 8. 변경 파일 요약

- `SignalMath.cs`: `GreenWindowFor` 추가, `PhaseForAxis` 내부 리팩터, `GreenWaveEfficiency` 시그니처 교체(축 인수)
- `FlowSolver.cs`: `SignalFactor`가 스텝 축 계산해 전달
- `SimEngine.cs`: 변경 없음(예상) — API 그대로
- `SimTileRenderer.cs`: 변경 없음
- 테스트: `SignalMathTests`·`SignalFlowTests` 갱신 + 일치 테스트 추가

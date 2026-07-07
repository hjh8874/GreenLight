# 신호 모델 통합 설계 — 엔진 그린웨이브 ↔ 뷰 방향신호 (Gap #2)

> 2026-07-07 · 환 · CityFlow(도시흐름) 시그널 통합
> 선행(LLM_WIKI/output/designs/traffic-spirit/): `signal-integration-design-2026-07-07.md`, `dev-log-06.md` §갭2, `debugging-traffic-deadlock-2026-07-07.md`
> 대상 코드: `Assets/01_Scripts/CityFlow/Sim/SignalMath.cs` (+ 테스트). **FlowSolver·SimEngine·SimTileRenderer는 안 건드림.**

---

## 1. 문제 (Gap #2)

같은 `Signal` 데이터 위에 **서로 무관한 신호 공식 두 개**가 돈다:

| | 엔진(처리량) | 뷰(신호등) |
|---|---|---|
| 함수 | `GreenWaveEfficiency(from,to,travelSlots,floor)` | `PhaseForAxis(s,time,horizontal)` / `IsGreen(s,time)` |
| 소비 | `FlowSolver.SignalFactor` → `delivered` → 코인 | `SimTileRenderer` (via `SimEngine.GetSignalPhase`) |

**두 공식이 오프셋 부호가 정반대다:**
- 뷰(`IsGreen`/`PhaseForAxis`): `t = time + offset·SlotSeconds` → **오프셋↑ = 초록 빨라짐.**
- 엔진(`GreenWaveEfficiency`): 완벽 조건 `offsetTo − offsetFrom = travelSlots` → **오프셋↑ = 초록 느려짐.**

→ 오프셋을 돌려 엔진 처리량을 올리면 **화면 신호는 반대로 움직인다.** 게다가 엔진 공식은 **초록창 길이를 무시**(오프셋 정렬만 선형)하고 뷰는 실제 초록창(반주기·노랑·전적색)을 쓴다. 그래서 "엔진 효율 1.0인데 화면 차는 빨간불" 같은 모순이 난다. 코어 손맛("오프셋 조율 → 눈으로 흐름 → 보상")이 깨짐.

경위: 원래 엔진 그린웨이브 모델만 있었고, 뷰 데드락 응급수정으로 방향 교대 모델 `PhaseForAxis`를 **뷰에만** 넣으며 갈라짐.

## 2. 결정

- **진실의 원천 = 엔진** (처리량 스칼라는 엔진이 계산). 뷰는 결과를 그려주기만.
- **단일 신호 타이밍 모델.** `SignalMath`에 공용 **초록창 프리미티브** 하나 → `PhaseForAxis`(뷰)와 `GreenWaveEfficiency`(엔진)가 **둘 다 거기서 파생** → 구조적으로 못 갈라짐.
- **오프셋 부호 하나로 통일 = 직관 방향** (`offsetTo − offsetFrom = travelSlots` → 완벽). 즉 **뷰 쪽 부호를 뒤집어** 엔진과 맞춘다(`IsGreen`/`PhaseForAxis`의 `time + offset` → `time − offset`). 기존 뷰 테스트는 오프셋 0·6(부호 불변점)만 써서 **영향 없음.**
- **축(H/V) 인수 불필요.** 직선 경로에선 축이 인접쌍 상대 타이밍에서 **상쇄**된다(§3.3 증명). → `GreenWaveEfficiency` 시그니처 그대로 → **`FlowSolver` 무변경.** 교차로 회전(축 바뀜)은 2차.
- **엔진은 per-car 시뮬 안 함.** rate 모델 유지. 효율 = 정상류에서 "차가 초록 잡는 비율"의 해석적 값(per-car 수렴값과 동일) → 화면 일치 + 가벼움.
- **재화 = 도착 수 × `CoinBase`** (이미 `ArrivalEmitter`). `CoinBase`=1로 "집→회사 도착마다 +1", 거리/가치 배수는 나중(손잡이만).
- **재밸런싱 나중.** 방향 교대 반영으로 처리량 내려가는 건 의도. 숫자는 `CoinBase`·`GreenWaveFloor`·주기로 나중에.

## 3. 아키텍처 (변경은 `SignalMath.cs` 하나)

### 3.1 공용 프리미티브

```
// 한 축의 초록창이 열리는 시각(주기 내, 초)과 길이. public (일치 테스트가 씀).
public static (double open, double greenLen) GreenWindowFor(Signal s, bool horizontal)
```
- `cycle = CycleSlots·SlotSeconds`; `half = cycle/2`; `greenLen = half·(1 − YellowFrac − ClearFrac)`
- `axisStart = horizontal ? 0 : half`
- **통일 부호:** `open = mod(axisStart + OffsetSlots·SlotSeconds, cycle)`  (오프셋↑ = 초록 늦게 = 직관)

### 3.2 뷰 함수 리팩터 (동작 불변, 부호만 통일)

- `PhaseForAxis`: 내부를 `GreenWindowFor` 기준 판정으로. `t = time − OffsetSlots·SlotSeconds`로 통일(현재 `+`에서 뒤집음).
- `IsGreen`: 마찬가지로 `time − offset`. (부호 통일 — 단일 신호 판정도 같은 방향.)
- 기존 뷰 테스트(오프셋 0·6)는 부호 불변점이라 그대로 통과.

### 3.3 엔진 효율 재구현 (시그니처 불변)

`GreenWaveEfficiency(from, to, travelSlots, floor)` — **인수 그대로**, 내부를 `GreenWindowFor`에서 파생:
```
cycle = from.CycleSlots·SlotSeconds
(openFrom, _)      = GreenWindowFor(from, true)   // 축 무관(아래 증명) → true 대표
(openTo, greenLen) = GreenWindowFor(to, true)
arrive = mod(openFrom + travelSlots·SlotSeconds, cycle)   // 상류 초록 선두에 출발 → 이동
δ      = mod(arrive − openTo, cycle)
if δ < greenLen: return 1                          // 하류 초록 안착 = 완전 통과(플래토)
gap    = min(δ − greenLen, cycle − δ)              // 초록창까지 원형 최단거리
maxGap = (cycle − greenLen) / 2
return 1 − clamp01(gap / maxGap) · (1 − floor)     // 완벽 1 → 반대편 한복판 floor
```

**축 상쇄 증명:** `open = axisStart + 0.5·offset`. δ의 `axisStart`는 openFrom·openTo에 똑같이 들어가 (openFrom − openTo)에서 소거 → `δ = mod(0.5·(offsetFrom − offsetTo) + 0.5·travelSlots, cycle)`, **축 무관.** 그래서 직선 경로 인접쌍은 축을 안 넘겨도 정확하고, 뷰(그 축 초록등)와 일치. (회전 경로만 축이 살아남음 → 2차.)

### 3.4 소비자 무변경

- `FlowSolver.SignalFactor`: `GreenWaveEfficiency` 시그니처 그대로라 **코드 변경 없음** (값만 새 모델로 달라짐).
- `SimEngine`, `SimTileRenderer`: 변경 없음.
- **재화(`ArrivalEmitter`):** 변경 없음. `delivered`↓(신호 나쁨) → 도착↓ → 코인↓. A(재화)·B(뷰)가 같은 타이밍 하나에서 흐름 → 병합.

## 4. 데이터 흐름 (통합 후)

```
유저 오프셋 조율 → Signal.OffsetSlots
   ├─(엔진) SignalFactor → GreenWaveEfficiency ─┐
   └─(뷰)   GetSignalPhase → PhaseForAxis ───────┤─ 둘 다 GreenWindowFor 하나에서 파생
                                                 └→ 보는 것 = 버는 것
```

## 5. 결정론

`SignalMap.Tiles` flat 순회 유지, Dictionary 순회 의존 없음, per-car 랜덤 없음(해석적). 기존 결정론 테스트가 회귀 방지.

## 6. 범위 밖 (2차)

처리량 리밸런싱 · `GreenRatio`(초록 길이) 레버 · 교차로 회전 정밀 위상 · 다른 주기 신호쌍 · 거리/가치 코인 배수 · `ISignalControl` Contracts 정식화.

## 7. 테스트 (TDD, `SignalMathTests`·`SignalFlowTests`)

값은 `CycleSlots=12` (cycle=6.0s, greenLen=1.95s, floor=0.5) 기준. **끝점(1.0/≈floor)·플래토·단조·일치**로 견고하게(중간 소수 과고정 회피).

| # | 테스트 | 검증 |
|---|---|---|
| 1 | `GreenWindowFor` 가로/세로 | open이 반주기(3.0s) 벌어지고 greenLen 동일 |
| 2 | `PhaseForAxis`/`IsGreen` 리팩터 회귀 | 기존 오프셋 0·6 케이스 전부 동일 |
| 3 | 정렬(offsetTo−offsetFrom=travel) | 효율 1.0 (기존 peak 테스트 유지) |
| 4 | 플래토 | 초록창 안 착지 오프셋 여럿 다 1.0 |
| 5 | 단조 falloff | 초록창 밖으로 갈수록 효율 감소, 반대편 한복판 ≈floor |
| 6 | 신호 0~1개 | SignalFactor=1 |
| 7 | 오프셋 조작 e2e | `TrySetSignalOffsetSlots`로 정렬 → delivered 1.0 회복 |
| 8 | **일치(anti-drift) — 핵심** | 같은 from/to/travel/offset에서 `GreenWaveEfficiency==1` ⟺ `PhaseForAxis(to, openFrom+travel)==Green`. 정렬·어긋남 두 오프셋으로. 두 모델 재분리 시 **빨개짐.** |
| 9 | 결정론 | 신호 도시 동일입력=동일해시(기존) |

**기존 값 갱신:** `GreenWave_HalfCycleOff_HitsFloor`·`MisalignedSignals_ReduceThroughput`은 새 플래토 모델로 값 재계산(관계 단언 위주). 정렬 케이스(offset 4, travel 4)는 그대로 1.0.

## 8. 변경 파일

- **`SignalMath.cs`**: `GreenWindowFor` 추가(public), `PhaseForAxis`·`IsGreen` 부호 통일+창 경유, `GreenWaveEfficiency` 내부 재구현(시그니처 불변).
- **테스트**: `SignalMathTests`(창·플래토·단조·일치·부호), `SignalFlowTests`(어긋남 값 갱신).
- **무변경**: `FlowSolver.cs`, `SimEngine.cs`, `SimTileRenderer.cs`, `ArrivalEmitter.cs`.

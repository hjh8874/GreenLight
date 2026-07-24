# Sim–View 권한 통일 설계 — 위치·간격·점유의 진실은 Sim, View는 보간만

- 작성: 2026-07-24 · 기준 브랜치: `feat-queueslot-target-distance-hwan` (Phase A 진행 중)
- 코드 기준: 본 체크아웃 `/Users/hwan/Gamemaker/GreenLight` 현재 워킹트리. 파일:라인은 전부 실측.
- 전제(재검증 불요): Phase A = QueueSlot→View 목표 거리 배선(slot×`vehicleMinHeadway` 0.55), 후진 금지,
  로터리 인접 슬롯 목표 제외, corridor 선행 폐지. EditMode 기준선 **323**.

---

## 0. 원칙 (사용자 방침 고정)

**위치·간격·점유의 진실은 Sim이다. View는 Sim이 준 목표를 프레임 보간할 뿐, 스스로 교통 판단을 만들지 않는다.**
서로 다르게 돌아가는 이중 개념은 "Sim이 주(主), View는 미세 오차 방어"로 강등하거나 삭제한다.

현재 이중 권한 지도 (무엇이 위치를 결정하는가):

| 상황 | Sim 쪽 진실 | View 쪽 자체 판단(정리 대상) |
|---|---|---|
| 일반 도로 간격 | `RoadQueueNetwork` 큐 순번 → `CarSnapshot.QueueSlot` (`CarSim.cs:15`) | `ResolveLaneLeaders`(화면 위치로 앞차 선출, `CarMotion.cs:147-243`) + `TryGetLaneHeadway`(`:273`) + `VehicleSpacingMath.LimitAdvance`(`:1084`) |
| 타일 진입 가능 | `CanAcceptNormally` = `_counts[queue] < _capacity` (`RoadQueueNetwork.cs:1291-1303`) | 없음 (Sim 단독) — 단 View 차가 시각적으로 늦게 비켜 "Sim은 비었는데 화면은 차 있음" 발생 |
| 교차로 통과 | `IntersectionMicroGrid` 4비트 셀 + `IntersectionStage` Entry→Conflict→Exit (`IntersectionMicroGrid.cs`) | `intersectionMotionStates` 페이싱(`CarMotion.cs:878-906`) + `TryGetIntersectionEntryLimitDistance`(`SignalVehicleControl.cs:47`) |
| 신호 정지 | `ISignalGate.IsServiceOpen`가 **다음 타일 진입**을 게이트 (`RoadQueueNetwork.cs:600`) | `TryGetSignalEntryStopDistance`(`SignalVehicleControl.cs:11`) — 정지선 위치만 계산(교통 판단 아님, 유지) |
| 로터리 | `RoundaboutTrafficState` 4링셀 + `Progress01` (`RoundaboutTrafficState.cs:182-190`) | `GetRoundaboutAuthorizedDistance`가 `DistanceAtPhase`로 원호 변환 (`RoundaboutVehicleControl.cs:45-54`) — 변환일 뿐 판단 아님, 유지 |
| 재베이크 후 위치 | `CarSim.Rebuild`의 `ResumeTile` (`CarSim.cs:81-92`) | 타일 동일 시 `car.Distance` 보존, 다르면 페이드 소멸 (`SyncCommuteVehicleBindings`, `CarMotion.cs:592-599`) |

---

## 1. 정리 대상별 설계 결정

### 1.1 View 앞차 추종의 보조 강등 — 무엇이 남고 무엇이 사라지나

Phase A 이후 corridor는 `vehicle.TargetDistance`(=Sim 슬롯 목표) 그대로다(`CarMotion.cs:966`,
`vehicleCorridorTiles` 참조처 0 — `MainCityView.cs:64`는 이미 사문). 남은 View 자체 판단은 앞차 추종뿐이다.

**최종 상태:**

| 심볼 | 거취 | 근거 |
|---|---|---|
| `VehicleSpacingMath.LimitAdvance` (`VehicleSpacingMath.cs:27`) | **남는다** — 프레임 단위 하드 클램프 | Sim 목표는 틱(0.1s) 해상도. 틱 사이 프레임에서 두 차의 보간 위상이 어긋나는 미세 오차는 View만 막을 수 있다 |
| `TryGetLaneHeadway` (`CarMotion.cs:273`) | **남는다(축소)** — 간격 회복 감속(`follow`, `recover` 로직 `:1063-1072`) 포함 | 동일. 단 `minHeadway`는 Sim 슬롯 간격과 **같은 값 하나**만 쓴다(§1.2 노브 단일화) |
| `ResolveLaneLeaders`의 화면 위치 휴리스틱 (`:191-242`, `isAhead = routeOffset>0 \|\| ahead>0f \|\| laneSlot[j]<laneSlot[i]`) | **사라진다** — 앞차 선출을 Sim 큐 순서로 대체 | 연속 대기열(§1.2) 이후 "내 앞차"는 Sim이 이미 안다: 같은 큐 slot−1, slot 0이면 하류 타일 큐의 꼬리. 화면 위치 선출은 View가 위치의 주인이던 시절의 산물이며 지연 역전(−0.378타일 계측)의 원인 회피용 땜질이었다 |
| `TryGetForwardLaneOffset` (`:245-270`) | 사라진다 (위와 동반) | 〃 |
| `VehicleSpacingMath.CalculateLookaheadTiles` (`:7`) | 사라진다 (호출처가 위 선출뿐, `CarMotion.cs:203`) | 〃. EditMode `VehicleSpacingMathTests`의 해당 케이스는 함께 삭제 |
| `laneTile/laneDelta/lanePolyline/...` 배열 8종 (`:125-132`) | `laneLeader` 산출용 축소판만 유지 | Sim 유도 앞차 인덱스만 있으면 됨 |
| catch-up 부스트 (`vehicleCatchUpStart/Ramp/Range`, `:1032-1037`) | 남는다 | 목표 추종 속도 문제이지 권한 문제가 아님 |
| `TargetAdvancing`→`ceilingSpeed` (`:938-946`, `:1043`) | 남는다 — **신호 사슬이므로 접촉 금지**(§4) | Sim이 잡고 있는지의 판정으로 이미 Sim이 주다 |

Sim 유도 앞차 선출(대체 구현 스케치, `CarMotion` 내):
스냅샷의 (TileIndex, QueueSlot)로 "같은 폴리라인 축의 (타일,진입방향)이 같고 slot이 하나 작은 차" 또는
"내 경로 다음 타일을 점유한 큐의 꼬리 차"를 carSimMirrors에서 찾는다. 차선 키 유도는 지금과 동일
(`route[p]-route[p-1]` ↔ `TileAt(p)-TileAt(p-1)`, `CarMotion.cs:122-124` 주석의 계약 그대로).

### 1.2 연속 대기열 — `count < capacity`를 하류 공간 파생으로

**현행:** 방향별 FIFO가 타일당 `QueueCapacityPerTile`(=4) 토큰을 담는다. 차에는 길이가 없어서 4대가
한 타일에 "쌓이고", View는 slot×0.55를 그리려니 slot 2부터 타일 시작을 넘는다 —
`RoutePolyline.DistanceAtQueueSlot`이 현재 `[0,Length]` 클램프로 얼버무리는 지점(`RoutePolyline.cs:254-256` 주석).

**채택 모델 — 물리 공간 파생 유효 용량:**
타일 하나(1타일 길이)에 headway 간격으로 실제로 서 있을 수 있는 수만 받는다.

```
effectiveCapacity(tile) = min(QueueCapacityPerTile, ceil(1 / QueueHeadwayTiles))   // 0.55 → 2
```

- `CanAcceptNormally`(`RoadQueueNetwork.cs:1291`)의 `_counts[queue] < _capacity`를
  `_counts[queue] < _effectiveCapacity`로 교체. 로터리 arm 분기(`RoundaboutArmCapacity = 1`, `:46`)는 불변.
- 넘치는 차는 자동으로 상류 타일 큐에 남는다 = **대기열이 인접 타일로 이어진다.** 스필백은 이미
  큐 모델의 자연 동작이다(현행 테스트 `RoadQueueNetworkTests.cs:189-208`이 capacity 2로 이미 검증하는 형태).
- `_cars`/`_nextNodes` 풀 크기 산정(`maxCars = queueCount * _capacity`, `:142`)은
  `QueueCapacityPerTile` 그대로 둔다 — 메모리 상한 용도로 존치.

**QueueSlot 의미 재정의:** "타일 내 순번 0..3" → "타일 내 물리 슬롯 0..1". 전역 위치는
(TileIndex, QueueSlot)의 합성으로 표현된다 — slot이 타일을 넘지 않으므로 View의
`DistanceAtQueueSlot(tileIndex, slot, gap, headInset)` 매핑이 **클램프 없이** 항상 폴리라인 위 유효
거리다. 별도 "전역 slot" 필드는 만들지 않는다(CarSnapshot 무변경).

알려진 근사 오차(수용): 타일 경계에서 slot1(중심−0.55)과 상류 타일 head(중심−1.0) 사이 간격이
0.45타일로 균일치(0.55)보다 좁다. 관통은 아니며(차 길이 0.38×LengthScale ≤ 0.55 계약),
완전 균일 간격은 §1.4 전면 모델로만 가능 — 지금 하지 않는다.

**`QueueCapacityPerTile`의 거취:**
- 의미가 "물리 용량"에서 "풀 상한 + 유효 용량의 캡"으로 바뀐다. 필드 삭제/기본값 변경은 하지 않는다.
- `QueueHeadwayTiles`는 **SimConfig 신규 필드가 정답**이지만 `SimConfig.Default()`(L123-173)는 편집 금지 —
  **요청 경로:** SimConfig 담당(팀 합의 문서상 Default() 단독 정리 담당)에게
  `public float QueueHeadwayTiles` 추가 + `Default()`에 `0.55f` 기입을 요청한다.
  **요청 전 임시:** `RoadQueueNetwork` 내 `private const float QueueHeadwayTiles = 0.55f` + 주석으로
  `MainCityView.vehicleMinHeadway`(L65, 0.55)와 값 계약 명시. 필드 승인 후 상수 제거.
- 방향(권한 통일의 일부): 승인 후에는 **Sim이 headway의 주인**이고 View는
  `SimEngine`에 노출 프로퍼티(예: `CarSimQueueHeadwayTiles`, `CarSimQueueCapacity` `SimEngine.cs:398` 옆)를 읽는다.
  `vehicleMinHeadway` SerializeField는 폴백으로 강등(제거는 씬 5개 직렬화 이슈라 하지 않음).

**파급 — `MaxOccupancy01`(`RoadQueueNetwork.cs:479`):** `approach = maxCount / _capacity`의 분모를
`_effectiveCapacity`로 바꿔야 혼잡 판정(`SimEngine.ScanCarCongestion` `:222`, `CongestionForOccupancy` `:244`,
`QueueSlowRatio 0.5 / QueueJamRatio 0.99`)의 의미가 유지된다(2대=Jam). 바꾸지 않으면 Jam이 절대 안 뜬다.
분모 교체는 밸런스 체감(Slow가 1대에 점등)을 바꾸므로 라이브 확인 항목에 포함.

### 1.3 재베이크 재투영 — "타일 동일 시 보존/변경 시 폐기"를 논리 위치 재매핑으로

**현행:** 건설 리빌드 시 Sim은 `ResumeTile`로 생존(순간이동 방지, `CarSim.cs:81-92`, `TryEnqueueDepartures :249-253`)
하는데, View는 `SyncCommuteVehicleBindings`에서 `SamePolylineTiles`가 아니면 렌더러를 끄고 주차 수렴시킨다
(`CarMotion.cs:592-599`). Sim은 이어 달리는데 화면에서 차가 사라지는 비대칭.

**변경:** 경로 타일이 바뀐 이동 중 차도, Sim 논리 위치로 새 폴리라인에 재투영한다.

```
ReprojectDistance(snapshot, newPoly):
  tileIndex = clamp(snapshot.TileIndex, 0, newPoly.TileCount-1)
  if snapshot.RoundaboutProgress01 >= 0: return GetRoundaboutAuthorizedDistance(newPoly, tileIndex, progress)
  if snapshot.IntersectionProgress01 >= 0: return clamp(DistanceAtTile(tileIndex) + (p-0.5)*tileSize, 0, Length)
  if snapshot.LinkProgress01 > 0:       return lerp(DistanceAtTile(i), DistanceAtTile(i+1), linkP)
  else:                                  return DistanceAtQueueSlot(tileIndex, snapshot.QueueSlot, gap, headInset)
```

- `MoveCarSimVehicle`의 목표 계산(`:857-925`)과 같은 식이므로 함수로 추출해 공용화한다.
- 페이드 경로는 "새 경로에 `ResumeTile`이 없어 Sim이 route[0] 재출발한 경우"(=`snapshot.TileIndex`가 0으로
  리셋되고 위치가 크게 점프)만 남긴다. 판별: 재투영 거리와 현 `car.Distance` 차가 임계(예: 2타일) 초과 시 페이드.
- `RebakeCommuteGeometry`(노브 튜닝 경로, `:402`)는 타일 불변이므로 현행 sticky 유지 — 접촉하지 않는다.

### 1.4 차량 점유 범위 — 길이 도입 여부와 시점

2026-07-23 분석의 `VehicleSimState(DirectedLaneSegmentId, LocalDistance, HalfLength, SafetyGap,
OccupiedConflictZones)` 전면 도입 vs 슬롯 근사 유지:

| | 전면 도입 (연속 위치 Sim) | 슬롯 근사 유지 (본 설계) |
|---|---|---|
| 간격 정확도 | 완전 균일, 차종별 길이 반영 | headway 양자(0.55) 균일, 길이는 양자 이하로 강제 |
| 코드 비용 | `RoadQueueNetwork`(1454줄) 사실상 재작성 + 교차로/로터리/하이웨이 재정식화, EditMode 60+ 파일 영향 | `CanAcceptNormally` 1곳 + 분모 1곳 + View 배선 |
| 결정론/테스트 | float 누적 → 틱 재현성 관리 필요 | 정수 토큰 유지, 기존 테스트 골격 존속 |
| 필요 시점 | **가변 길이 차량이 Sim에 영향을 줄 때** — 구체적으로 `SchoolBusService`류 버스가 도로 큐에 실릴 때 | 지금 |

**결정: 슬롯 근사를 유지한다.** headway 양자(0.55)가 곧 암묵적 차량 점유 길이다. 이 선택의 계약:
`CarStyle.LengthScale` × `BaseVehicleLengthTiles`(0.38, `SignalVehicleControl.cs:8`)가 0.55를 넘지 않아야
한다(현 최대 0.437 — `MainCityView.cs:65` 주석). 이 계약은 CLAUDE.md 결합 표의 기존 항목이며 본 설계가
의존한다고 명문화한다. `VehicleSimState`는 버스가 큐에 들어오는 시점의 별도 설계로 **보류**하되,
§1.5의 "후미 이탈 시 해제"를 슬롯 어휘로 먼저 구현해 두면 전환 시 개념이 그대로 승계된다.

### 1.5 교차·합류 Conflict Zone — 진입 시 점유, 후미 이탈 시 해제

**현행 문제:** `RebuildIntersectionOccupancy`(`RoadQueueNetwork.cs:792`)는 점유를 **매 틱 큐 소속에서
재구성**한다. 차 토큰이 교차로 타일을 떠나 다음 타일 큐로 옮겨진 순간(Exit 스테이지에서 `Move` 성공)
점유가 사라진다. 그러나 차에는 길이가 없고 View 차는 목표를 뒤에서 따라가므로, Sim이 셀을 해제한 뒤에도
화면의 차체는 교차로 안에 있다 → 다음 차가 진입 허가를 받아 **관통**한다.

**변경(시간표 예약 없음 — 점유/해제만):** "후미가 이탈할 때까지 exit 경로 셀을 유지"를 슬롯 어휘로 구현.

- 규칙: 교차로에서 나간 차가 **다음 타일의 경계 인접 슬롯**(연속 대기열에서 slot == effectiveCapacity−1,
  즉 front가 타일 중심−0.55 = 후미가 교차로 쪽 경계 너머)에 있는 동안, 그 차의
  `OccupancyMask(entry, movementExit, Exit)`(= `MovementMask`, `IntersectionMicroGrid.cs:41-47`)를
  해당 교차로 점유에 계속 OR한다.
- 구현 지점: `_intersectionStages[node]`는 `Move` 실행 시 이미 `Exit`로 남고(`ExecuteIntent` `:1085-1089`),
  `ClearStagesOutsideIntersections`(`:434`)가 교차로 밖에서 지운다. 여기에 상태 하나를 추가:
  `Exit` 스테이지 + "직전 타일이 UsesSharedBudget 교차로"인 노드는 `RebuildIntersectionOccupancy`에서
  그 교차로 인덱스로 마스크를 유지하고, 슬롯이 얕아지는 틱(큐 전진으로 slot 감소) 또는 다음 타일을
  떠나는 시점에 `IntersectionStage.None`으로 청산한다. 직전 교차로 인덱스는 노드별 int 배열 1개
  (`_clearingIntersection[maxCars]`)로 기억한다.
- 이로써 "Sim은 해제했는데 View 차가 남아 있는" 창이 닫힌다: View는 Phase A 이후 corridor 상한 때문에
  Sim 목표보다 앞설 수 없고, 뒤처짐은 최대 ~1슬롯(경계 인접 슬롯 유지 구간)이 커버한다.
- 로터리 합류는 이미 이 규칙이다: `TryReserveRingEntry`가 진입+상류 셀을 함께 예약하고
  (`ReserveMergeCells`, `RoundaboutTrafficState.cs:228-233`), 이탈 대기 차가 있으면 링 전체 정지
  (`ServiceRoundaboutRings`의 `heldMask`→`BlockEntries`, `RoadQueueNetwork.cs:1136-1158`). 추가 작업 없음.
- 부작용(의도된 것): 교차로 배출 직후 후속 진입이 1틱가량 늦어진다 → 교차로 처리량 소폭 감소.
  라이브 체크 항목에 그린웨이브 통과량 확인 포함.

### 1.6 로터리 — RingCell 이산 모델 유지

`RoundaboutTrafficState`의 4링셀 + `AdvanceCounterClockwise` + `Progress01(entry, exit, cell)` →
`CarSim.CalculateRoundaboutProgress`(`CarSim.cs:305-322`) → View `GetRoundaboutAuthorizedDistance`의
`DistanceAtPhase(lerp(ci−span, ci+span, progress))` 변환(`RoundaboutVehicleControl.cs:45-54`)은 **유지 가능하고
유지한다.** 이유:

- 링셀은 이미 §1.5가 요구하는 "진입 점유/이탈 해제" conflict zone의 완성형이다(예약·양보·전원 감속까지).
- View 변환은 판단이 아니라 좌표계 변환(이산 progress → 원호 거리)이라 권한 원칙과 충돌하지 않는다.
- 연속 모델로 갈 때 바뀌는 것(참고, 지금 안 함): 링셀 4개 → 원호 각도 연속화, `Progress01`의
  `(steps+1)/(total+1)` 양자화 제거, `RoundaboutArmCapacity=1`의 공간 파생화. 전부 §1.4 전면 도입과 동시에만.
- 본 설계에서 로터리에 닿는 유일한 변경: 연속 대기열(§1.2)에서 **로터리 arm 타일은 제외**
  (`IsRoundaboutArm` 분기 존치 — arm은 "타일 전체가 물리 셀 1칸" 계약, `CanAcceptNormally` `:1295-1302`).
  Phase A의 "로터리 인접 슬롯 목표 제외"(`CarMotion.cs:854-856`의 `targetQueueSlot = 0`)와 정합.

---

## 2. 단계 분해 — 각 단계 = PR 1개, develop 직분기, 스쿼시 머지

기준선: 머지 시점마다 EditMode `CityFlow.Sim.Tests` **323 + 그 단계 신규분 전부 green**. 절차는 CLAUDE.md
그대로(`refresh_unity` compile → `read_console` error 0 → `run_tests` EditMode).

### P0 — Phase A 마감 (진행 중, `feat-queueslot-target-distance-hwan`)
범위: 이미 워킹트리에 있는 것(슬롯 목표, `queueSlotOnlyRegression` 후진 금지 `CarMotion.cs:1012-1015`,
corridor 선행 폐지, 로터리 슬롯 제외). **본 문서의 신규 작업 없음.**
검증: 323 green. 라이브 — ① 관통(교차·동일 차선) 계측 재실행 ② "무근거 정지" 계측(정지의 사유 분류)
③ Sim-View 지연 분포(이전 최대 2.86타일) 재측정해 P1 이후 비교 기준 확보.

### P1 — Sim 연속 대기열 (`RoadQueueNetwork` + 테스트)
- `QueueHeadwayTiles` 상수 도입(0.55, SimConfig 필드 요청 병행 — §4 표), `effectiveCapacity` 계산,
  `CanAcceptNormally` 교체, `MaxOccupancy01` 분모 교체.
- 로터리 arm·하이웨이(`HighwayState.Capacity`는 거리 기반 별도 모델, `:225`)·강제 밸브(`Force`) 불변.
- 테스트: §3 예상 파손 수정 + 신규 — "capacity 4 설정에도 타일당 2대 초과 불가", "3대 대기 시
  1대가 상류 타일에 남고 QueueSlot ≤ 1", "2대 점유 시 Jam 판정".
- 검증: 323(수정 반영) + 신규 green. 라이브 — 대기열이 시각적으로 타일을 이어 늘어서는지,
  혼잡 색(Slow/Jam) 점등 체감, 처리량(도착 이벤트/일) 전후 비교.

### P2 — View 배선 정리 (`CarMotion` + `RoutePolyline`)
- `DistanceAtQueueSlot`의 "Phase A에서는 폴리라인 시작에 조용히 모으고" 주석(L254-256) 갱신 —
  P1 이후 slot ≤ 1이라 클램프는 경로 양끝 보호로만 남는다(코드 변화 최소, 의미 변화 문서화).
- `queueSlotOnlyRegression` 가드 단순화 검토: P1 이후 슬롯 목표가 타일 시작을 넘는 경우가 사라지므로
  가드 발동 조건이 재베이크 케이스로 좁혀진다 — P3와 함께 정리해도 됨(판단 여지).
- `vehicleCorridorTiles`(사문 노브) 제거는 SerializeField 직렬화 이슈 — **필드는 두고 `[Obsolete]` 주석만**.
- 검증: 323 green. 라이브 — 정지 대기열 간격이 균일 0.55(경계 0.45)로 보이는지, 후진 0건.

### P3 — 재베이크 재투영 (`CarMotion`)
- `ReprojectDistance` 추출(§1.3), `SyncCommuteVehicleBindings`의 페이드 조건을 "재투영 불가(대점프)"로 축소.
- 검증: 323 green. 라이브 — 주행 중 도로 건설/철거 반복: 순간이동 0, 페이드는 경로 소실 차만,
  건설 직후 관통 스파이크 없음(재투영 직후 앞차 관계 재검).

### P4 — 교차로 후미 해제 (`RoadQueueNetwork` + `IntersectionMicroGrid` 소비부)
- §1.5 구현: `_clearingIntersection[]`, `RebuildIntersectionOccupancy` 확장, 청산 조건.
- **주의: `CollectIntents` 주변 수정 = 신호 인과 사슬 접촉**(§4 표) — 신호 게이트(L600) 자체는 불변이지만
  교차로 그룹 해석이 바뀌므로 준희 사전 공유 + PR 승인 필수.
- 테스트: 신규 — "배출 직후 경계 인접 슬롯 점유 중 교차 경로 진입 불허", "슬롯 전진 후 진입 허용",
  기존 `RoadQueueDeviceTests`/`RoadQueueNetworkTests` 교차로 시나리오 타이밍 1틱 지연 반영.
- 검증: 323(조정 반영) green. 라이브 — 교차로 관통 계측 0 수렴, 그린웨이브 통과량 회귀 확인.

### P5 — 앞차 추종 강등 확정 (`CarMotion`)
- `ResolveLaneLeaders` 화면 선출 → Sim 큐 순서 유도(§1.1). `TryGetForwardLaneOffset`/
  `CalculateLookaheadTiles` 삭제. `LimitAdvance`+`TryGetLaneHeadway` 존치.
- headway 값 단일화: `SimEngine.CarSimQueueHeadwayTiles` 신설(요청 승인 후) → View가 이를 읽고
  `vehicleMinHeadway`는 폴백.
- 검증: 323 green + `VehicleSpacingMathTests` 정리. 라이브 — SAME-DIR 겹침 계측(기존 385/359/769
  수치와 비교), 데드락 0(dev-log-17 시나리오), 앞차 역전 관계 0.

### P6 (보류, 트리거 조건부) — `VehicleSimState` 전면 도입
버스 등 가변 길이 차량이 도로 큐에 실리는 결정이 나면 별도 설계 문서부터. 본 계획의 범위 밖.

의존성: P1 → P2 → P3(P2와 순서 교환 가능) → P4 → P5. P4는 P1의 슬롯 물리화에 의존한다.

---

## 3. 테스트 영향 예측 (큐 count 불변식 기반)

P1(유효 용량 4→2)에서 깨질 것으로 예측되는 기존 테스트 — 전부 "한 타일에 3~4대"를 전제:

| 테스트 | 위치 | 깨지는 단정 | 조치 |
|---|---|---|---|
| FIFO 4대 적재 | `RoadQueueNetworkTests.cs:104-105` | `AreEqual(4, QueueCount(V(2,2), E))` | 타일당 2 + 상류 스필로 재기술 |
| 만석 상류 대기(capacity 4) | `:164-182` | `AreEqual(3, QueueCount(V(1,0), E))` 등 count 3 전제 | 유효 용량 2 기준으로 수치 수정 |
| 스필백(capacity 2) | `:189-208` | **통과 예상**(min(2,2)=2) | 무변경 |
| 결정론(capacity 2) | `:215-` | 통과 예상 | 무변경 |
| capacity 1 계열 | `:264,308,329` | 통과 예상(min(1,2)=1) | 무변경 |
| 적색 정지선 누적 5대 | `RoadQueueDeviceTests.cs:185-198` | `AreEqual(id+1, QueueCount(approach, E))` — 한 접근 타일에 5대 | 2대+상류 3대 분산으로 재기술, "초록: 틱당 1대 진입"(L198) 카운트다운도 타일별로 분해 |
| `QueueSlot` 0 확인 | `CarSimTests.cs:251` | 통과 예상(head는 여전히 slot 0) | 무변경 |
| 엔진 QueueCount 위임 | `CarSimEngineTests.cs:47-68` | 통과 예상(위임 동일성만 검사) | 무변경 |
| 혼잡 판정 | `CongestionForOccupancy` 소비 테스트(존재 시) | 분모 변경으로 임계 이동 | Jam=2대 기준 확인 |

P4(후미 해제)에서: `RoadQueueNetworkTests`의 교차로 처리 순서 테스트(L375-559 구간의 `CarAtHead` 틱별
단정)들이 배출 직후 1틱 지연으로 어긋날 수 있다 — 틱 인덱스 재계산 필요. 정확 목록은 구현 시 확정.

원칙: 테스트 수정은 "불변식이 바뀐 것"(용량·타이밍)만 수치 갱신하고, 실패를 덮는 완화는 금지.
기준선은 각 PR 머지 시점에 "전부 green"으로 갱신 — 부분 실패 허용 없음(2026-07-22 합의).

---

## 4. 소유권 경계 — 어느 단계가 어디에 닿는가

| 경계 (규칙) | 닿는 단계 | 필요한 절차 |
|---|---|---|
| `SimConfig.Default()`(L123-173) 편집 금지 / 필드 추가는 요청만 | P1(`QueueHeadwayTiles` 필드), P5(노출 프로퍼티는 SimEngine이라 무관) | 담당자에게 필드 추가+Default 기입 요청. 승인 전엔 `RoadQueueNetwork` 내부 const로 대행(0 또는 미존재 시 레거시 동작 불가하므로 const가 안전) |
| `MainCityView`에 `[SerializeField]` 추가 금지(2026-07-22 완화: 자기 구역 내 허용) | P2(노브 제거 안 함), P5(신규 노브 없음 — Sim 값 소비로 해결) | 신규 SerializeField **불필요하게 설계함**. `vehicleCorridorTiles` 등 제거도 안 함(주석 강등만) |
| 신호 사슬 `CollectIntents` → `TargetAdvancing`(CarMotion L938-946) → `ceilingSpeed`(L1043) — 준희 계층 | **P4**(CollectIntents/ResolveIntents 인접 수정), P1(CanAcceptNormally는 사슬 밖이지만 CollectIntents가 호출 — 동작 계약 공유 필요) | P4는 PR + 준희(원작성자) 승인 필수. P1은 사전 공유(신호 게이트 L600 로직 무변경 명시) |
| `CarMotion.cs` = 주석 전담, `MoveCarSimVehicle` 포함 | P0·P2·P3·P5 | 수정 전 공유 + PR 리뷰에 주석 지정(CLAUDE.md: 남의 소유 파일은 수정 가능하되 반드시 미리 공유) |
| `RoutePolyline.cs`/`RoadQueueNetwork.cs` 링 서비스 = 환 소유 | P1·P2·P4 | 자기 소유 — 자유. 단 `BakeInput` 필드 추가 없음(경계 계약 무접촉) |
| `roundaboutOrbitRadius` 씬 5개 동시 수정 | 해당 없음(본 설계는 로터리 기하 무접촉) | — |
| 통합 씬 커밋 금지 | 전 단계 | 라이브 검증은 `CityFlowIntegrated_hwan`에서만, 씬 diff 커밋 제외 |

---

## 5. 하지 않을 것 (범위 밖 명시)

- **차선 변경** — 차선 = (타일, 진입방향) 단일 계약 유지. 추월·회피 없음.
- **가감속 물리의 Sim 이관** — 가감속(`vehicleDriveAccel/BrakeAccel`, catch-up)은 View 연출로 남는다.
  Sim은 틱-토큰 전진만. (View 물리는 위치 권한이 아니라 보간 품질이다.)
- **시간표 예약(conflict zone 시간 슬롯 예약)** — §1.5는 점유/해제만. 도착 예정 시각 기반 예약 없음.
- **운행 차량 세이브** — `SimSaveData`는 집계 통계 유지(`SimSaveData.cs:17-20`). 로드 점프는 현행
  "전 차 주차 수렴"(`CarSim.cs:149-157`) 그대로.
- **`VehicleSimState` 전면 도입** — P6 트리거(버스 큐 탑승) 전까지 착수 금지.
- **하이웨이 링크 모델 변경** — `LinkCar.ExitTick` 시간 기반 모델은 대기열이 아니므로 무접촉.

## 6. 남는 리스크 (설계 시점 인지)

1. 유효 용량 4→2는 도시 전체 저장 용량을 절반으로 줄인다 — 스필백이 빨라져 gridlock 밸브
   (`GridlockValveTicks` 8) 발동 빈도가 오를 수 있다. P1 라이브에서 `ValveActivations` 추이 확인.
2. `TryLocateCar`의 전 큐 선형 탐색(`RoadQueueNetwork.cs:365`)은 차수 불변이나, P4의 노드별 배열 추가로
   틱 비용이 소폭 는다 — MaxSimCars 96 규모에서는 무시 가능.
3. View 지연이 1슬롯 홀드(P4)보다 커지는 프레임 드랍 상황에서는 여전히 이론상 관통 창이 있다 —
   P0 지연 분포 계측으로 상한을 확인하고, 초과 시 홀드를 "슬롯 2단계"로 연장하는 후속 노브만 열어 둔다.

---

## 부록 A — 차량 길이: 다중 슬롯 점유

- 작성: 2026-07-24 · 배경 결정(사용자): 버스 등 긴 차량을 추가하므로 차량 길이를 Sim에 도입한다.
- 위치: §1.4가 보류한 P6 전면 모델과 현행 슬롯 근사의 **중간 지대** — 길이를 슬롯 정수 배수로
  양자화한다. 승용차 1슬롯, 버스/트럭 2슬롯. 정수 토큰·결정론·기존 큐 골격을 유지하는 것이 목적.
- 전제: P1(연속 대기열, effectiveCapacity=2/타일) 선행. P4는 준희 승인 완료 상태.
- 실측 기준: develop 팁 `33746e4`(= P1 작업 브랜치 `feat-continuous-queue-hwan` 워킹트리, 코드 diff 없음).
  Phase A 쪽 인용은 `feat-queueslot-target-distance-hwan`(`40dfd27`)에서 별도 실측해 병기한다.

### A.1 큐 표현 — 노드 slotSize 필드 채택 (유령 노드·외부 가중 기각)

현행 골격: 고정 노드 풀 위의 침습 연결 리스트. 노드 1개=차 1대(`_cars[node]`/`_nextNodes[node]`,
`RoadQueueNetwork.cs:83-84`), 큐별 `_heads/_tails/_counts`(`:87-89`), 전진은 head만
(`CollectIntents`가 `_heads[queue]`만 순회 `:526-529`, `ExecuteIntent`가
`_heads[intent.FromQueue] != intent.Node`면 무효 `:1019`).

| 후보 | 방식 | 판정 근거 |
|---|---|---|
| **① 노드 slotSize 필드 (채택)** | `_slotSizes[maxCars]` 배열 추가, `_counts`를 슬롯 가중으로 재정의 | `AppendNode`/`DetachHead`의 `_counts[queue]++/--`(`:1416`, `:1424`)를 `+= / -= _slotSizes[node]`로 — 2곳. `CanAcceptNormally`(`:1291-1303`)는 P1의 `_counts[queue] < effectiveCapacity` **형태 그대로**(비교값이 자동으로 슬롯 단위가 됨). `TryLocateCar`의 슬롯 카운팅(`:368` `queueSlot=0`, `:386` `queueSlot++`)만 `queueSlot += _slotSizes[node]`로 — 반환 슬롯이 "앞차들 크기 합 = 내 front 슬롯"이 된다. FIFO 전진(`MoveHead` `:1430-1436`)은 노드 단위 원자 이동이라 **무변경** |
| ② 유령 노드 N−1개 | 실차 뒤에 더미 노드 삽입 | head=이동 가능한 차라는 전제가 전면에 깔려 있다: 유령이 head가 되는 순간 `CollectIntents`가 유령에 인텐트를 만들고(`:534` `_cars[node]` 참조), `ExecuteIntent` head 가드(`:1019`)·`Arrival`의 단일 노드 해제(`:1022-1025`)·`RebuildIntersectionOccupancy`의 노드 순회(`:802-826`)가 전부 특례를 요구한다. 타일 경계에 걸치면 유령이 **다른 큐**에 있어야 해 리스트 소속 단일성도 깨진다. 기각 |
| ③ count 가중치만 (크기를 CarSim 쪽에만 보관) | 네트워크는 carId만 알므로 `ICarRouteProvider`류 콜백으로 크기 조회 | `CanAcceptNormally`·`TryLocateCar`가 핫 루프에서 콜백 조회를 반복하게 되고, 결국 크기가 네트워크 판단에 필요하다는 사실은 ①과 같다. ①은 enqueue 시 1회 캐시로 같은 정보를 노드에 싣는 것 — ③은 ①의 열화판. 기각 |

①의 인터페이스 변화: `TryEnqueue(tile, entry, carId)`(`:269-278`)에 `slotSize` 파라미터 추가(기본 1).
풀 크기 `maxCars = queueCount * _capacity`(`:142`)는 상한 용도 그대로(큰 차=더 적은 대수이므로 안전).
`QueueCount()`(`:280-281`)의 의미가 "대수"→"점유 슬롯 수"로 바뀐다 — §3 표의 기존 수치 재기술과
같은 부류의 변화이고, 0 비교인 `HasClearIntersectionExit`(`:1265-1266`)는 영향 없다.

**파급(의도된 것):** `MaxOccupancy01`(`:479-496`)의 분자가 자동 가중된다 — 버스 1대(2슬롯)로
2/2 = Jam. 타일이 실제로 꽉 찬 것이므로 혼잡 의미가 오히려 정확해진다.

**가드 필수:** `slotSize > effectiveCapacity`인 차는 `CanAcceptNormally`가 영원히 false → 출발
큐잉(`CarSim.TryEnqueueDepartures`의 `net.TryEnqueue`, `CarSim.cs:255`)이 영구 실패해 차가 조용히
증발한다. `TryEnqueue`에서 slotSize를 effectiveCapacity로 클램프 + 디버그 단정. P1 기준 **상한 2슬롯**(A.7).

### A.2 타일 경계 — 원자 점유, 상류 용량 소비 없음

**규칙: 차는 정확히 한 타일 큐 소속이고(연결 리스트 소속 단일성 그대로), 그 타일에서만
slotSize만큼 소비한다. 상류 타일 용량은 소비하지 않는다.**

- 유도되는 불변식: effectiveCapacity=2에서 **2슬롯 차는 항상 자기 타일의 단독 점유자, front 슬롯 0**.
  진입에 빈 슬롯 2가 필요하므로 빈 타일에만 들어가고, 들어온 뒤엔 1슬롯 차도 못 따라 들어온다.
  View가 버스 목표를 "타일 머리"로만 다루면 되는 단순화가 공짜로 나온다.
- 물리 겹침은 View 공간 문제로 남는다: 버스 front가 타일 중심일 때 차체 후미는 상류 타일에
  걸칠 수 있다. 뒤차의 최악 front-to-front 간격은 상류 타일 head까지 1.0타일이므로 **2슬롯 차체
  계약: ≤ 1.0 − 여유**. §1.4 계약(0.38×`LengthScale`≤0.55, `SignalVehicleControl.cs:8`,
  `CarStyle.cs:51`의 상한 1.15)을 확장해 **버스 기본 길이 0.76(=0.38×2), 최대 0.76×1.15=0.874**를
  권장 — 여유 0.126으로, 기존 1슬롯 경계 케이스의 여유 0.013(0.45−0.437)보다 오히려 넉넉하다.
- 예외(기존 동작 유지): gridlock 밸브 `Force`(`:1076`)는 지금도 용량을 초과 적재한다. 버스 타일에
  강제 진입하면 슬롯 합 3이 되지만, 가중 `TryLocateCar`가 slot=2를 주고 P2의 폴리라인 클램프가
  받는다 — 밸브 초과는 현행에도 있는 수용된 근사.

**용량-1 구역은 차량 원자(slot 산술 제외):**

| 구역 | 규칙 | 근거 |
|---|---|---|
| 로터리 arm (`RoundaboutArmCapacity=1`, `:46`) | 버스도 1대로 센다 — `CanAcceptNormally`의 arm 분기(`:1296-1302`, 방향 합산)를 **대수 기준으로 유지**(가중 합이면 2>1로 버스가 영원히 못 들어감) | arm 계약이 애초에 "타일 전체가 물리 셀 1칸"(§1.6) — 셀 하나에 차 한 대, 길이는 무관 |
| 링 셀 (`RoundaboutTrafficState.NodeAt`, 셀당 노드 1) | 버스=셀 1칸. 무변경 | 링은 이미 진입 점유/이탈 해제 완성형(§1.5). 이탈 대기 시 링 전체 정지(`:1136-1158`)가 긴 후미도 커버 |
| 교차로 타일 (UsesSharedBudget) | 일반 큐이므로 가중 산술 그대로 — 버스가 교차로 타일을 단독 점유(2/2)하는 것은 바람직 | 마이크로그리드 마스크는 노드 단위(`:802-826`)라 무변경 |
| 하이웨이 링크 (`Capacity = distance`, `:225`) | 버스=1토큰 유지 | 시간 기반 링크 모델(§5)이라 공간 점유 개념이 없음. 수용된 근사(A.7) |

잔여 리스크(수용): 버스가 arm→링으로 옮겨진 직후 차체 후미가 arm에 시각적으로 걸치는 창.
arm 용량 1이라 Sim 관통은 없고, 다음 차의 arm 진입 목표는 arm 중심이므로 겹침 폭은 링 셀-arm
간격이 흡수한다. P4 라이브 계측 항목에 로터리 mouth 겹침 확인을 추가.

### A.3 스냅샷/뷰 배선 — `CarSnapshot.LengthSlots` 필드가 필요하다

"CarStyle→슬롯 수 매핑을 View가 아는 것으로 충분한가" — **불충분.** 세 가지 이유:

1. slotSize는 `CanAcceptNormally`를 바꾸므로 **Sim의 판단 입력**이다. `CarStyle`은 View 전용
   연출 프로파일(`CarStyle.cs:5-6` 주석 "판단 없음", 소비처 `MainCityView.cs:1940`)이고 어셈블리
   방향상 Sim이 ViewKit을 볼 수도 없다. View가 독자 유도하면 Sim과 어긋날 수 있는 이중 권한 —
   본 설계 §0 원칙 위반.
2. View는 **앞차의** 길이가 필요하다: 버스 뒤차의 최소 간격은 `앞차.LengthSlots × vehicleMinHeadway`
   (front-to-front)여야 한다. `TryGetLaneHeadway`(`CarMotion.cs:273`)의 `minHeadway`(`:1040`)와
   `LimitAdvance` 하드 클램프(`:1064-1069`)에 같은 값을 공급 — P5의 headway 단일화(§1.1)에 항 하나가
   추가되는 것.
3. 렌더 스케일(버스 차체)도 같은 값에서 파생돼야 View-Sim 길이가 일치한다.

**결정:** `CarSnapshot`(`CarSim.cs:8-21`)에 `public int LengthSlots`(기본 1) 추가. 값의 주인은
CarSim(차종 배정, 결정론 해시 또는 수요 기반 — A.5의 활성화 단계에서 확정), View는 스냅샷만 읽는다.
`CarStyle.LengthScale`은 **종내 개성**(같은 차종 안의 ±15%)으로 의미가 좁아진다 — 주석 명문화.

**`DistanceAtQueueSlot` 무변경:** A.1의 가중 `QueueSlot`("앞차들 크기 합")이 Phase A 배선
(`40dfd27` `CarMotion.cs:857-861`의 `DistanceAtQueueSlot(tileIndex, targetQueueSlot, slotGap, ...)`,
`slotGap = vehicleMinHeadway×tileSize` `:839`)에 그대로 들어가면 "앞차가 2슬롯이면 내 목표는
−0.55×2"가 **산식 변경 없이** 나온다. 시그니처(`RoutePolyline.cs:245-255`)도 그대로. 단
effectiveCapacity=2에서는 버스와의 타일 공유 자체가 없으므로(A.2 불변식) 이 경로가 실제로 작동하는
건 교차-타일 추종이고, 그쪽은 위 2의 leader-aware minHeadway가 담당한다.

### A.4 P4(교차로 후미 해제)와의 상호작용 — 해제 조건의 일반화

§1.5의 홀드 조건 "slot == effectiveCapacity−1"은 1슬롯 전제다. 일반형:

```
후미가 교차로에 걸쳐 있다  ⇔  frontSlot + LengthSlots > effectiveCapacity − 1
```

- 1슬롯 차: `slot+1 > 1` ⇔ slot==1에서 홀드, slot==0에서 해제 — §1.5와 동치(회귀 없음).
- 2슬롯 버스: `0+2 > 1` — front 슬롯 0(유일한 상태)에서도 항상 참 → **다음 타일 체류 전 구간
  홀드**, 청산은 §1.5가 이미 가진 "다음 타일을 떠나는 시점" 분기가 담당. 기하적으로 맞다:
  버스 front가 타일 중심일 때 후미(≤0.874)는 최대 0.374타일 교차로에 남아 있고, 이는 1슬롯
  차가 slot1에 있을 때의 침범(0.487)과 같은 급 — 타일 안 어느 슬롯에서도 후미가 빠지지 않는다.
- 구현 델타: P4의 청산 조건식 한 줄(±5줄). `_clearingIntersection[]`·`RebuildIntersectionOccupancy`
  확장 구조는 §1.5 그대로.
- **절차:** P4는 준희 승인 완료 상태이나 승인 범위는 §1.5 원문이다. 이 일반화(조건식 변화 +
  버스 홀드가 교차로 처리량에 주는 추가 지연)를 **P4 구현 PR 설명에 부록 A.4 링크로 명시**해
  승인 델타를 소급 공유한다 — 별도 재승인 사이클을 만들 규모는 아니지만 몰래 넣지 않는다.

### A.5 단계 배치 — P1.5(인프라, 동작 불변) + P5.5(활성화) 2분할

| 방안 | 내용 | 장점 | 단점 | PR 규모 |
|---|---|---|---|---|
| **채택: P1.5 + P5.5 분할** | P1 직후 **P1.5**: `_slotSizes[]`·가중 카운트·가중 TryLocateCar·`TryEnqueue` 파라미터·`CarSnapshot.LengthSlots`·클램프 가드. **전 차 slotSize=1 → 동작 완전 불변.** P4·P5는 처음부터 A.4 조건식·leader-aware minHeadway로 작성. **P5.5**: 차종 배정(CarSim)·SimConfig 비율 필드(요청)·View 렌더 스케일·라이브 밸런스 | P4/P5를 한 번만 쓴다(재접촉 없음). P1.5는 기존 테스트 전량 green이 곧 검증이라 회귀 리스크 최소. 활성화(밸런스 영향)는 시퀀스 끝으로 격리 | 죽은 코드(미사용 필드)가 P5.5까지 존재 | P1.5 소형(~150줄: RoadQueueNetwork +40, CarSim +20, 테스트 +6건). P5.5 중형(~200줄 + 라이브) |
| 대안 1: P2 앞에 전부 (P1.5 일괄) | 인프라+활성화 동시 | 단계 수 최소 | 버스 콘텐츠(차종·비주얼)가 아직 없는데 밸런스 변수(교차로 홀드 연장·용량 소비 2배)가 P2~P5 라이브 계측 전부에 섞인다 — 각 단계의 회귀 판정이 흐려짐 | 중형 1건이지만 이후 모든 단계의 계측 비용 증가 |
| 대안 2: P5 뒤에 전부 | 시퀀스 무변경, 나중에 도입 | 현 계획 무접촉 | P4 청산 조건·P5 minHeadway를 **소급 수정** — P4 재접촉은 신호 사슬 재접촉(§4)이라 준희 재승인 왕복이 한 번 더 생긴다. PR 2건 추가 | 소급 PR 2건 + 재승인 비용 |

의존성 갱신: P1 → **P1.5** → P2 → P3 → P4(A.4 조건식 포함) → P5(leader minHeadway 포함) → **P5.5**.
P6 트리거(§1.4 "버스가 도로 큐에 실릴 때")는 이 부록이 흡수한다 — P6의 남은 트리거는 A.7로 재정의.

### A.6 테스트 영향 + 소유권 경계 (추가분만, §3·§4 형식)

테스트 (P1.5 — 전 차 slotSize=1이므로 기존 테스트는 전량 무변경 통과가 곧 합격 기준):

| 테스트 | 위치 | 깨지는 단정 / 신규 내용 | 조치 |
|---|---|---|---|
| 기존 전체 (용량·FIFO·스필백·결정론) | `RoadQueueNetworkTests.cs` 등 | slotSize=1 경로는 산술 동일 | **무변경 통과 = P1.5 합격선** |
| 2슬롯 수용/거부 | 신규 | 빈 타일(유효 2)에 2슬롯 수용, 1슬롯 선점 타일엔 거부 | 신규 작성 |
| 단독 점유 불변식 | 신규 | 2슬롯 진입 후 1슬롯 후속 진입 불가 | 신규 |
| 가중 QueueSlot | 신규 | capacity 4 설정에서 2슬롯 뒤차 slot==2 | 신규(양자화 산술 자체 검증용) |
| slotSize 상한 가드 | 신규 | slotSize>유효용량 → TryEnqueue false(무한 대기 아님) | 신규 |
| arm 차량 원자 | 신규 | 2슬롯이 arm(용량 1) 진입 가능, 2대째 불가 | 신규 |
| 혼합 결정론 | 신규 | 1·2슬롯 혼합 동일 입력 2회 동일 상태 | 신규(§3 결정론 테스트 `:215-` 형식 재사용) |
| Jam=버스 1대 | 신규 | 2슬롯 1대로 `MaxOccupancy01`=1.0 | 신규(P1의 "2대 점유 시 Jam"과 짝) |
| 후미 일반화 | P4 신규에 케이스 추가 | 2슬롯 배출 후 다음 타일 체류 전 구간 교차 진입 불허, 타일 이탈 후 허용 | P4 테스트에 2케이스 추가 |

소유권 경계 (추가분):

| 경계 (규칙) | 닿는 단계 | 필요한 절차 |
|---|---|---|
| `SimConfig.Default()` 편집 금지 | P5.5(차종 비율 필드, 예: `TwoSlotVehicleRatio`) | §4 표의 `QueueHeadwayTiles`와 같은 요청 경로. 승인 전엔 CarSim 내 const 0(비활성) — P1.5가 SimConfig 무접촉인 이유 |
| 신호 사슬(`CollectIntents` 인접) — 준희 계층 | P4 일반화(조건식 ±5줄) | 기승인 범위의 델타 — PR 설명에 A.4 명시(재승인 왕복 없이 소급 공유) |
| `MainCityView` SerializeField 추가 금지 | P5.5 | 신규 노브 불필요 설계: 차체 길이는 `LengthSlots`×기존 `BaseVehicleLengthTiles` 스케일로, 프리팹·노브 추가 없이. 전용 버스 메시가 필요해지면 그때 담당자 요청 |
| `CarSim.cs`(스냅샷 계약) | P1.5 | 필드 추가는 additive지만 스냅샷 소비자(뷰 모션 담당) 사전 공유 |
| `CarStyle.cs`(ViewKit) 의미 축소 | P5.5 | 코드 무변경, 주석만("길이 주권은 snapshot.LengthSlots") — 담당 구역이면 공유 후 주석 PR |

### A.7 이 양자화 모델의 한계 — 무엇이 오면 P6인가

§1.4의 P6 트리거("버스가 큐에 실릴 때")는 본 부록이 해소했다. 남는 P6 트리거를 재정의한다:

1. **3슬롯 이상(트램·굴절버스).** A.1 가드 때문에 slotSize ≤ effectiveCapacity(=2)가 경계다.
   3슬롯은 headway 재양자화(0.55 → ~0.33, 유효 용량 3)로 전 차 간격·View 튜닝을 갈아엎거나 P6.
2. **슬롯 배수로 표현 안 되는 연속 길이가 게임플레이 수치에 직접 쓰일 때.** 예: "도로 점유
   길이 비례 유지비", 주차 슬롯 길이 차등, 차종별 정밀 처리량 밸런스. 양자화는 0.55 단위
   계단이라 이 요구가 오면 `VehicleSimState`(연속 `LocalDistance`+`HalfLength`)로.
3. **차종별 순항 속도 차이(버스가 느린 Sim).** 길이와 별개 축 — 틱당 1타일 균사가 깨지므로
   슬롯 모델로는 불가. P6의 연속 위치 전제.
4. **길이 비례 교차로/링 점유 시간의 정밀 모델.** A.4는 "타일 체류 전 구간 홀드"라는 슬롯
   해상도 근사다. 통과 시간 자체를 길이 함수로 요구하면 P6.
5. **차체 계약 초과.** 2슬롯 차체 ≤ 1.0(권장 0.874) 계약을 넘는 비주얼 요구(예: 실물 비율
   굴절버스)는 경계 겹침이 View로 못 막는 수준이 되므로 P6.

하이웨이 버스=1토큰(A.2 표)은 P6에서도 링크가 시간 모델인 한 그대로 두는 수용 근사다.

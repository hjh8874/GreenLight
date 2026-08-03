# 교차로 출발 허용 — 진입로가 교차로뿐인 건물의 영구 스톨 해소

- 작성: 2026-08-03 (환)
- 결정: 환. 워커·구현자가 §3 결정사항을 바꾸지 말 것
- 선행: 2026-08-03 진입로 교차로 회피(`fix-access-road-intersection-hwan`, PR 대기).
  본 작업은 그 픽스가 **남긴 구멍**을 막는다. 두 브랜치는 서로 독립이며 스택하지 않는다
  (팀 규칙: develop 직분기, 스택 금지)

## 1. 문제

건물의 **선택 가능한 프론티지가 전부 교차로**면 그 집 통근차는 영원히 출발하지 못한다.
차는 도로 위에 얼어붙은 채 렌더링되고, 도로 점유를 쥐지 않으므로 **다른 차들이 그대로 통과**한다.

2026-08-03 리허설 라이브 관찰(플레이 모드 리플렉션 실측):

```
id | state    | enq   | tIdx | slot | blkTicks | tile    | routeLen
 8 | Outbound | False |  0   |  -1  |    7     | (2, 10) |    10
 9 | Outbound | False |  0   |  -1  |    7     | (2, 10) |    10
RescueReroute=8  RescueRestart=8      (차 16대 중 2대 = 12.5%)
```

선행 픽스는 "비교차로 프론티지를 우선"이므로 **전부 교차로인 경우는 여전히 오늘과 같다** —
폴백이 첫 교차로를 돌려주고, 그 차는 큐에 못 들어간다.

## 2. 왜 못 들어가나 (기존 설계의 이유)

`RoadQueueNetwork.IsSafeResumeTile`(`RoadQueueNetwork.cs:416-426`)이 교차로·로터리를 배제한다.
주석이 이유를 명시한다:

> Rebuild enqueue has no intersection stage or roundabout ring reservation.
> Resume only on an ordinary queue tile so those state machines admit the vehicle
> through their normal entry paths.

교차로는 2x2 사분면(`IntersectionCell` NW/NE/SW/SE)으로 관리되고, 차는
`IntersectionStage` Entry → Conflict → Exit를 밟으며 셀을 예약한다
(`IntersectionMicroGrid.cs`, 전이는 `RoadQueueNetwork.cs:1406,1451-1452`).

**스테이지와 셀 예약이 없는 차를 판에 앉히면 충돌 판정이 깨진다.** 그래서 기존 설계는
안전하게 "앉히지 않는다"를 택했고, 그 대가가 영구 스톨이다.

즉 **원리적 불가가 아니라 미구현**이다. 스테이지를 제대로 부여하면 된다.

## 3. 결정사항 (환이 정함 — 구현자가 바꾸지 말 것)

### D1. 스테이지를 부여해 정식 진입시킨다 (A안)

Entry 스테이지로 스폰하고 이후 기존 중재가 Entry → Conflict → Exit로 굴린다.
"한 칸 앞 일반 타일에서 출발" 같은 우회는 **금지** — 교차로 중재를 건너뛰는 새치기다.

### D2. 진입 방향은 2단 규칙으로 산출한다 (지어내지 않는다)

`IntersectionMicroGrid`의 `Dir`는 **진행 방향**이다(`EntryCell(N)=SouthEast` = 북진 차량이
우측통행으로 동쪽 절반 남단을 점유). 따라서 진입 방향이 필요하다.

```
1) 건물 풋프린트 셀 중 그 진입로 타일과 직교(4방) 인접한 셀이 있으면
   → entry = 건물 → 도로 방향 (실제 차고 진출 방향)
2) 직교 인접이 없으면 (대각 프론티지)
   → entry = exit   (이미 자기 차선에 있는 것으로 모델링)
```

`exit`은 `route[0] → route[1]`로 산출한다.

2)가 필요한 이유: `RoadNetwork`의 프론티지 스캔은 **8방**이다(`RoadNetwork.cs:13-14`,
대각 4개 포함, "코너컷 허용" 주석). 진입로가 건물과 모서리로만 닿을 수 있다.

**경계 조건 2개 (구현자가 고민하지 말 것):**

- **직교 인접 셀은 최대 1개다.** 풋프린트가 직사각형(`TileFootprint.GetRotatedSize`)이고
  진입로 타일은 풋프린트 밖이므로, 그 타일의 4방 이웃 중 풋프린트 셀은 최대 하나다
  (둘이면 그 사이 칸까지 풋프린트가 덮어야 해 모순). 다중 후보 처리 로직 불필요
- **`route.Count == 1`이면 스폰하지 않는다.** exit 방향을 산출할 수 없다(출발지가 곧
  도착지). 오늘 동작(오프네트워크)을 그대로 유지한다

### D3. 중재 규칙을 새로 만들지 않는다

스폰도 기존 `IntersectionMicroGrid.Conflicts(occupied, requested)`를 그대로 통과해야 한다.
겹치면 **스폰 실패 → 다음 틱 재시도**. 신호가 빨강이면 Entry에서 대기(기존 흐름).
**새 우선권·새 예외 0개.**

이것이 수렴을 보장한다: 셀이 비면 반드시 들어가므로 영구 스톨이 구조적으로 사라진다.

### D4. 출발만 연다. 재개는 오늘 그대로 둔다

| 상황 | 처리 |
|---|---|
| **출발** — `hasResume == false`, `route[0]`이 교차로 | **이번에 연다** |
| **재개** — 리빌드 후 중간 복귀(`hasResume == true`) | **오늘 그대로** |

재개는 차가 경로상 어디쯤이었는지가 모호해 위험하다. 파급을 막기 위해 분리한다.

**`IsSafeResumeTile` 자체를 고치지 말 것.** 그건 재개 규칙이다. 출발 경로에 별도 진입점을
만든다.

### D5. 로터리는 이번 범위가 아니다

`IsSafeResumeTile`은 교차로와 함께 로터리·로터리 팔도 배제한다. 로터리 링 진입은 예약
모델이 다르므로(`ServiceRoundaboutRings`) **이번에 건드리지 않는다.** 프론티지가 전부
로터리인 건물은 오늘과 동일하게 남는다 — 별건으로 추적한다.

### D6. `SimConfig` 편집 금지

`SimConfig.Default()`(`Sim/SimConfig.cs` L123-173) 편집·필드 추가 금지. 이 변경에 설정값은
필요 없다. 임계값이 필요하면 `private const`로 둔다.

## 4. 변경 지점

| 파일 | 변경 |
|---|---|
| `Sim/RoadQueueNetwork.cs` | 교차로 타일에 스테이지·셀 예약과 함께 스폰하는 **내부 진입점 신설**. 기존 `TryEnqueue`·`IsSafeResumeTile` 불변 |
| `Sim/CarSim.cs` | `TryEnqueueRouteStart`에서 신규 출발이고 `route[0]`이 교차로일 때만 새 진입점 사용 |
| `Sim/RoadNetwork.cs` | 진입 방향 산출에 필요한 "건물 → 진입로 직교 인접" 조회가 없으면 추가 |

소유권: 세 파일 모두 `docs/2026-07-21-parallel-work-ownership.md` 표에 없다(표는 뷰 모션·
로터리·신호·건물 4영역만 지정). 단 `RoadQueueNetwork`의 **링 서비스**는 환 소유이므로
D5에 따라 그쪽은 건드리지 않는다.

## 5. 뒤집히는 기존 테스트 — 의도적 설계 변경

`Assets/Tests/EditMode/CarSimTests.cs`의
`Departure_SpecialRouteOrigin_StaysOffNetwork`가 다음을 단정한다:

> "신규 출발도 특수 route[0]에 stage 없는 차를 앉히지 않고 오프네트워크 대기한다"

**이번 변경이 정확히 이 규칙을 바꾼다.** 낡은 전제가 아니라 의도한 설계 변경이므로:

- 테스트를 "교차로 원점에서도 **유한 틱 안에 진입한다**"로 다시 쓴다
- 커밋 메시지와 PR 본문에 **왜 바꿨는지** 명시한다. 안 적으면 나중에 "테스트를 왜 고쳤나"가 된다
- **`RebuildResume_*` 계열은 건드리지 않는다.** 그대로 통과해야 한다 — D4의 분리가
  지켜졌다는 증거다. 이게 깨지면 재개 경로까지 번진 것이므로 멈추고 보고할 것

## 6. 테스트 계획

RED를 먼저 증명한다. 각 단정에 "이 픽스가 없으면 실패하나?"를 자문할 것.

**지금 실패해야 하는 것:**

- **T1** 프론티지가 전부 교차로인 건물의 통근차가 유한 틱 안에 `_enqueued`가 되어 출발한다
  (오늘 영구 `enq=False`). 라이브 증상의 직접 재현
- **T2** 좌회전 스폰이 `MovementMask`의 대각 셀을 예약해, 그 셀을 쓰는 횡단 차량과
  동시에 진입하지 않는다

**지금도 통과해야 하는 것(회귀 방지):**

- **T3** 교차로 셀이 이미 점유돼 있으면 스폰이 **지연**되고, 비는 즉시 성공한다 (D3 수렴)
- **T4** 대각 프론티지 건물은 D2-2 폴백(`entry = exit`)으로 진입한다
- **T5** `RebuildResume_*` 재개 단정 불변 (D4)
- **T6** 프론티지가 전부 로터리면 오늘과 동일하게 오프네트워크 (D5 — 범위 밖임을 고정)

## 7. 검증 게이트 (감독이 직접 실행 — 워커 금지)

본 체크아웃이 하나뿐이라 워커가 Unity를 돌리면 충돌한다.

1. `refresh_unity(compile="request")`
2. `read_console(types=["error"])` → **`error CS` 0건**. 초록은 컴파일 증거가 아니다
3. `run_tests` EditMode `CityFlow.Sim.Tests` → 기준선 대조

**기준선은 착수 시점에 감독이 실측한다.** 문서에 적힌 숫자(340·454 등)는 전부 낡았다.
2026-08-03 develop 실측은 532건 중 실패 3건이었으나, 그 사이 PR 머지로 바뀔 수 있다.

## 8. 제약

- `.unity` 씬·`ProjectSettings/` 커밋 금지. 작업 트리에 이미 수정된 것이 있으니
  `git add`는 **명시 목록으로만**
- 새 `.cs`는 `.cs.meta` 동반 커밋
- push·PR은 감독이 한다. 워커는 커밋까지
- 워커가 이 문서의 파일:라인이나 판단을 반박하면 **그대로 보고할 것.** 2026-08-03 세션에서
  워커가 감독 계획서의 결함 2건을 잡았고 둘 다 워커가 옳았다

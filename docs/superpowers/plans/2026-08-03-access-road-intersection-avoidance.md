# 진입로 교차로 회피 — 통근차 영구 스톨 수정

- 작성: 2026-08-03 (환)
- 브랜치: `fix-access-road-intersection-hwan` (origin/develop `ae89edf` 직분기)
- 기준선 실측: **EditMode `CityFlow.Sim.Tests` 528건 중 실패 3건** (아래 §4)

## 1. 증상과 라이브 증거

리허설 중 통근차 2대가 도로 위에 멈춘 채 움직이지 않고, **다른 차들이 그대로 통과**했다.

플레이 모드에서 리플렉션으로 직접 조회한 값:

```
id | state    | enq   | tIdx | slot | blkTicks | stage | tile    | routeLen
 8 | Outbound | False |  0   |  -1  |    7     |   0   | (2, 10) |    10
 9 | Outbound | False |  0   |  -1  |    7     |   0   | (2, 10) |    10
RescueReroute=8  RescueRestart=8       (차 16대 중 2대 = 12.5% 영구 사망)
```

`enq=False` / `slot=-1` = **도로 큐에 없음** → 점유를 안 쥐므로 다른 차가 통과한다.
워치독은 죽지 않았다. 8번 돌고도 못 고쳤다.

## 2. 근본 원인 (확정)

집 `(2,8)`의 프론티지 수집 결과:

```
[0] (2,10)  safeResume=False   ← TryGetAccessRoad가 고른 것 (IsIntersection=True)
[1] (3,10)  safeResume=True    ← 멀쩡한 대안
[2] (1,10)  safeResume=True    ← 멀쩡한 대안
[3] (4,10)  safeResume=False
```

> **정정 2026-08-03 (Task A 워커 지적 → 감독 실코드 확인 완료).**
> 최초 작성본은 통근 경로 원점을 `TryGetAccessRoad`가 정한다고 썼는데 **틀렸다.**
> 통근 배정은 `DemandMap.cs:537`이 `CollectAccessRoads`로 프론티지를 전수 수집한 뒤,
> `DemandMap.TryFirstRegionMatch`(`DemandMap.cs:696-708`)가 *"집 프론티지 **순서** 우선 →
> 같은 Region인 **첫** 쌍을 채택(결정론)"* 한다. 즉 원점을 정하는 건 **`CollectAccessRoads`의
> 순서**다. `TryGetAccessRoad`는 다른 호출자들의 경로다.
> 결과적으로 D4(순서만 바꾸고 원소 보존)가 정확한 지렛대였으나, 아래 인과 사슬의
> 함수 지목은 이 정정을 기준으로 읽을 것.

프론티지 수집·선택이 인접 도로 중 **먼저 발견된 것을 그대로 채택**한다. 그 타일이
큐 스폰이 가능한 타일인지 보지 않는다.

인과 사슬:

1. 경로 원점 `route[0]`이 교차로 → `CarSim.TryEnqueueRouteStart`(`CarSim.cs:1282`)가
   `!net.IsSafeResumeTile(route[start])`로 **false 반환**
2. 큐 진입 실패 → `_enqueued=false` → 도로 점유 없음 → 다른 차가 통과
3. 라이브니스 워치독(`CarSim.cs:1047`)이 재시작하지만 **매번 같은 원점**으로 재시도 → 영구 루프
4. 재경로(`TryApplyRescueRoute`)도 `RouteOrigin`에서 replan → 원점 불변 → 역시 실패

**큐 계층은 정상이다.** 설계대로 교차로 스폰을 거부한 것이고, 잘못된 입력을 준
진입로 선택이 범인이다.

## 3. 수정 방침 — 결정 사항 (환이 정함, 워커가 바꾸지 말 것)

**D1. 고치는 곳은 `RoadNetwork`의 프론티지 선택 하나다.**
`TryGetAccessRoad`·`CollectAccessRoads`를 쓰는 모든 하위(DemandMap·RoutePlanner·CarSim)가
함께 낫는다. 호출자마다 가드를 넣지 않는다.

**D2. 큐 의미론은 건드리지 않는다.**
기존 테스트 `CarSimTests.Departure_SpecialRouteOrigin_StaysOffNetwork`가
*"신규 출발도 특수 route[0]에 stage 없는 차를 앉히지 않고 오프네트워크 대기한다"*를
단정한다. **이 테스트는 그대로 통과해야 한다.** 경로를 앞으로 밀어 큐에 넣는 방식은
교차로 중재를 건너뛰는 새치기라 금지다.

**D3. 우선순위만 바꾼다 — 폴백은 유지한다.**
```
현재: 발견 순서대로 첫 번째 채택
수정: 교차로가 아닌 프론티지를 우선. 전부 교차로면 기존대로 첫 번째 채택
```

> **정정 2026-08-03 (Task A 워커 지적, 감독 승인).**
> 최초 작성본의 "인접 도로가 교차로 **하나뿐**"인 케이스는 **정상 배치 API로 만들 수 없다.**
> 모든 건물은 2x2(`Contracts/CityFlowTypes.cs:30-49`)이고 교차로 성립에는 직교 Road 이웃이
> 3개 필요한데(`Sim/CityGrid.cs:246-280`), 건물 쪽 한 팔은 2x2 점유로 막힌다. 따라서 앞면
> 한 타일이 교차로면 **같은 앞면의 다른 타일도 반드시 Road**다.
> 실제 폴백 조건은 **"선택 가능한 프론티지가 전부 교차로인 경우"** 이며, A-2는 그렇게 작성됐다.
> D3의 의도(폴백에서 접근 불가로 만들지 않기)는 그대로 유효하다.

**D4. `CollectAccessRoads`는 순서만 바꾸고 원소는 보존한다.**
`RoadNetwork.cs:124` 주석의 "감사 픽스 2"가 여러 Region 프론티지를 전수 수집하는
이유를 설명한다. **원소를 빼면 도달성이 깨진다.** 정렬/우선순위만 조정할 것.

**D5. 교차로 판정은 `CityGrid.IsIntersection`을 쓴다.**
`IsSafeResumeTile`은 `RoadQueueNetwork`에 있어 `RoadNetwork`가 참조하면 계층이 역전된다.
`RoadNetwork`는 이미 `CityGrid`를 들고 있다. **`CityGrid.IsIntersection`을 수정하지 말 것** —
읽기만 한다(`SimEngine.RebuildSignals()` 컬링이 여기 걸려 있다).

## 4. 기준선 (이 3건은 내 회귀가 아니다)

`CityFlow.Sim.Tests` EditMode 528건 중:

1. `CarSimEngineTests.SignalCycleProgress_ReturnsExpectedBoundaries_AndHandlesOverride`
   — #177이 함께 넣은 신규 테스트가 실패 중 (별건)
2. `ContentFeatureLogicTests.BusStopInfrastructure_BlocksOverlappingPlacementAndLastAccessRemoval`
   — 기왕 실패 (#195 대기)
3. `ContentFeatureLogicTests.PrototypeAssets_AreReadyForSceneIntegration`
   — `AmbulanceContent.prefab` 부재 (#192 미머지)

**이 3건 외에 하나라도 새로 실패하면 회귀다.** 숫자가 다르면 감독에게 보고할 것 —
내 숫자가 틀렸을 가능성이 워커가 틀렸을 가능성보다 높다.

## 5. 작업 순서

### Task A — RED 테스트만 (구현 금지)

`Assets/Tests/EditMode/` 에 테스트를 추가한다. **구현은 절대 건드리지 않는다.**

필수 단정:

- **A-1 (핵심)**: 건물의 인접 도로가 `[교차로, 일반]` 둘 다일 때 `TryGetAccessRoad`가
  **일반 타일**을 반환한다. 라이브 재현 형상을 그대로 쓸 것 —
  교차로는 인접 도로가 3방향 이상이어야 성립하므로 십자/T자를 실제로 깔아야 한다.
- **A-2 (폴백 보존)**: 인접 도로가 **교차로 하나뿐**이면 그 교차로를 그대로 반환한다
  (`false`를 반환하지 않는다 — 접근 불가로 만들면 도달성 회귀다).
- **A-3 (원소 보존)**: `CollectAccessRoads`의 **결과 집합이 수정 전후로 동일**하다.
  순서만 바뀐다. D4를 지키는지 검사한다.
- **A-4 (통합)**: 교차로 진입로를 가진 집의 통근차가 유한 틱 안에 `_enqueued`가 되어
  실제로 출발한다. 라이브 증상(`enq=False` 영구)을 직접 재현하는 단정.

**RED 증명 필수.** 각 단정에 "이 픽스가 없으면 이게 실패하나?"를 자문할 것.
A-2·A-3은 픽스 없이도 통과할 수 있다(현재 동작을 단정하므로) — 이건 **의도된 회귀 방지
테스트**이므로 공허해도 된다. 단 **A-1과 A-4는 반드시 지금 실패해야 한다.**
실패하지 않으면 테스트가 틀린 것이니 멈추고 보고할 것.

기존 테스트 관례는 `Assets/Tests/EditMode/CarSimTests.cs`를 참고
(`Cfg()`, `V(x,y)`, `CityGrid` 직접 배치 패턴).

산출: 새 테스트 파일 + `.cs.meta`. 보고에 **실패한 테스트 이름과 실패 메시지 원문**을 포함할 것.

### Task B — 구현 (Task A의 RED 확인 후에만)

§3의 D1~D5대로 `RoadNetwork`를 수정한다. Task A의 테스트를 GREEN으로 만든다.

## 6. 제약

- **`SimConfig.Default()`(`Sim/SimConfig.cs` L123-173) 편집 금지.** 노브 추가 금지 —
  이 수정에 설정값은 필요 없다.
- **씬(`.unity`) 커밋 금지.** 작업 트리에 이미 수정된 디버그 씬이 있으니 `git add`는
  **명시 목록으로만** 할 것.
- **`ProjectSettings/` 커밋 금지** (패키지 잡음이 끼어 있다).
- 새 `.cs`는 `.cs.meta`를 함께 커밋.
- 커밋만 하고 **push·PR은 하지 말 것** — 감독이 게이트 후 처리한다.

## 7. 감독이 직접 하는 것 (워커가 하지 말 것)

- Unity 컴파일·테스트 게이트 (`refresh_unity` → `read_console` → `run_tests`).
  본 체크아웃은 하나뿐이라 워커가 Unity를 직접 돌리면 충돌한다.
- push·PR 생성.
</content>
</invoke>

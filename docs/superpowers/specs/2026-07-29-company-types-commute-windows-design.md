# 회사 종류 3종과 유형별 출퇴근 시간대 — 설계

- 작성: 2026-07-29 · 기준 커밋: `develop 90fdfad` (#166 머지 직후)
- 파일:라인은 전부 위 커밋 실측
- 선행 기획: `~/LLM_WIKI/wiki/dev/idle-city-company-types-plan.md` (2026-07-24)
  — 그 문서의 갈래 B 권고를 채택하되, 코드가 바뀐 부분 2건을 정정한다(§1.3)

---

## 0. 목표와 확정 요구사항

**목표**: 회사를 3종으로 나누고 **종류마다 출퇴근 시간대를 다르게** 해서, 같은 도로에 시간대별로 다른 교통 패턴이 나타나게 한다. 그래야 "출퇴근"이라는 개념에 의미가 생긴다.

| 항목 | 결정 |
|---|---|
| 3종 | **사무실 / 공장 / 물류창고** |
| 무엇이 다른가 | **출퇴근 시간대** + **정원** |
| 자정 넘는 근무 | **지원한다** (공장 야간 등). 나중 확장에 유리하다는 판단 |
| 하루 길이 | **24시간 = 12분**으로 치환 — **준희님 담당, 본 설계 범위 밖.** 우리 작업의 선행 의존성이다 |
| 가게·상점 | **범위 밖.** 특수건물 8종이 상점 카테고리로 가고, 본 설계의 3종은 "큰 회사"다 |
| 건설 패널 카테고리 재편 | **범위 밖 — 다른 담당자** |
| `TileType` 확장 | **하지 않는다.** `Office` 하나에 인스턴스 데이터로 구분(선행 기획 갈래 B) |

### 0.1 설계 결정 (2026-07-29 검토 확정)

| | 항목 | 결정 |
|---|---|---|
| ① | 차가 유형을 기억하나 | **`CommuteCar`에 필드 추가.** 콜백은 생성 시점의 출처, 필드는 차 생애 동안의 캐시 — 역할이 겹치지 않는다 |
| ② | `Rebuild`의 시각 인자 4개 | **제거하고 콜백으로 대체.** 창의 출처를 하나로 (이중 권한 회피) |
| ③ | 판정 기준 | 전역 필드 → **차 개별 값**. 유형별 창에서는 전역으로 판정 불가 (구조상 필연) |
| ④ | 공사 중 유형 보존 | **`ConstructionSite`에 `CompanyTypeId` 적재** (안 하면 완성 시 전부 사무실) |
| ⑤ | 시간대 값 확정 시점 | **지금 정할 수 있다.** 시각은 게임시간 단위라 하루 길이와 무관 (§2.4) |
| ⑥ | 유형 미지정 배치 | **거부한다.** `Place`가 `false` 반환 — 조용한 폴백을 만들지 않는다 |

---

## 1. 조사 — 지금 코드가 어떤 상태인가

### 1.1 발견: 하루 길이의 출처가 **둘**이고 값이 5배 어긋나 있다

```
GameCalendarService.realSecondsPerGameHour = 1     (통합 씬 4개 전부 실측)
   → 하루 24시간 = 실시간 24초
   → CityBootstrap.cs:92 가 simEngine.SetGameHour(GameCalendar.Hour) 로 주입
   → 출퇴근 판정(CommuteScheduler.UpdateDepartures), SnapCar 가 이 시각을 쓴다

SimConfig.DayLengthSeconds = 120                   (.asset 3개 전부)
   → 하루 = 120초
   → DemandPulse(러시아워 맥동) · 채용 램프 · 건설시간이 이 값을 쓴다
```

같은 "하루"인데 한쪽은 24초, 한쪽은 120초다. 그래서 지금 게임에서는:

- 차는 **24초마다** 출퇴근을 한 바퀴 돌고
- 수요 맥동은 **120초 주기**로 부풀었다 가라앉는다
- → **출퇴근 파도와 러시아워가 5배 어긋나 따로 논다**

`SimConfig.cs:206` 주석은 `DemandPulse`가 "하루에 두 번" 부푼다고 적었으나, 실제로는 게임 시각 기준 **5일에 두 번**이다. 이는 이 프로젝트가 최근 제거해 온 **이중 권한**과 같은 형태다.

### 1.2 발견: 자정을 넘는 근무가 구조적으로 불가능하다

```csharp
// CommuteScheduler.cs:171-176
if (car.State == CarState.ParkedHome
    && hour >= car.DepartHomeHour && hour < _eveningEnd)      // 출근
else if (car.State == CarState.ParkedWork
    && (hour >= car.DepartWorkHour || ...))                   // 퇴근

// CommuteScheduler.cs:219
bool inEveningWindow = hour >= _eveningStart && hour < _eveningEnd;
```

전부 `[0,24)` 선형 비교다. 순환이 없다. 20시 출근 / 05시 퇴근인 차를 만들면 출근한 20시에 이미 `20 >= 5`가 참이라 **출근 즉시 퇴근**한다. 또 `hour < _eveningEnd`(21) 게이트 때문에 **21시 이후에는 누구도 출근할 수 없다.**

### 1.3 선행 기획(07-24) 대비 달라진 것

| 그 문서 주장 | 현재(`90fdfad`) |
|---|---|
| `BuildingDefinitionSO`는 소비자 0건인 죽은 코드 | **살아 있다.** `AnalysisCardController.cs:513`, `PlacementController.cs:56`, `FacilityInfluenceSelectionController.cs:16`, `PlacementVisualManager.cs:34` 가 쓰고 특수건물 8종도 이것으로 정의된다 |
| `CommuteCar`에 sink 타입 필드를 추가해야 한다 | **유효하다.** 콜백만으로도 되지만(§0.1 ①) 차가 자기 유형을 들고 있는 편이 디버깅·추적에 낫다고 판단해 원안을 채택했다 |

### 1.4 이미 있어서 재사용할 것

| 자산 | 위치 | 쓰임 |
|---|---|---|
| 차별 개별 출퇴근 시각 | `CommuteCar.DepartHomeHour` / `DepartWorkHour` | **이미 차마다 개별 필드다.** 값 계산만 유형별로 바꾸면 된다 |
| 콜백 주입 패턴 | `Rebuild(..., Func<Vector2Int,int> workCapacityFor, ...)` | 같은 형태로 창 조회 콜백을 추가 |
| 결정론적 분산 | `StaggerHour(home, start, end)` (`:231`) | 집 좌표 해시로 창 안에 흩뿌림. **그대로 쓴다** |
| 회사 인스턴스 상태 | `DemandMap.CompanyCapacityState` (`:50`) | `Type`·정원·건설시각을 이미 보관. 여기에 유형 id를 얹는다 |
| 한 TileType 아래 여러 종류 | `SpecialBuildingService.TryPlace(anchor, buildingId, ...)` | **선례가 이미 있다.** 갈래 B가 코드에 구현된 형태 |

---

## 2. 설계

작업이 **3단계로 갈리고 순서가 강제된다.**

```
① 시간 축 통일 (12분 하루)     ← 준희님 담당. 본 설계 범위 밖 = 우리의 선행 의존성
② 자정 넘김 지원               ← ①이 있어야 "자정이 언제인지"가 정의된다   ┐ 우리 작업
③ 회사 3종 + 유형별 창         ← ②가 있어야 공장 야간이 성립한다          ┘ PR 각각
```

> **착수 조건**: ②는 ① 머지 후에 시작한다. ① 없이 ②를 하면 하루 24초 기준으로 순환 판정을
> 짜게 되고, ①이 들어오는 순간 그 전제가 통째로 바뀐다. §2.1은 **우리가 만들 것이 아니라
> 준희님께 전달할 조사 결과**다 — 특히 §1.1의 이중 권한 발견이 그렇다.

### 2.1 ① 시간 축 통일 — **준희님 담당.** 아래는 우리가 조사하며 찾은 것을 넘기는 내용이다

**문제**: §1.1의 이중 권한. **목표**: 하루 길이를 한 곳에서만 정의하고 12분(720초)으로 맞춘다.

> 이 절은 **설계 지시가 아니라 인계 자료**다. 우리가 회사 3종을 조사하다 발견한 것이라
> 담당자가 판단에 쓰라고 남긴다. 방식은 담당자가 정한다.

**한 가지 방향(참고):** `SimConfig.DayLengthSeconds`를 단일 출처로 삼고, `GameCalendarService`가 그 값에서 시간당 실초를 유도한다.

```
DayLengthSeconds = 720                     (.asset 3개)
realSecondsPerGameHour = 720 / 24 = 30     ← 하드코딩이 아니라 유도값
```

`GameCalendarService.realSecondsPerGameHour`는 현재 `[SerializeField]`이고 **씬 4개에 `1`로 직렬화**돼 있다. 씬 값이 코드 기본값을 덮으므로, 필드를 지우고 `SimConfig`에서 읽도록 바꾸지 않으면 통일이 되지 않는다.

> ⚠️ 이 변경은 **통합 씬 4개의 직렬화 값**과 얽힌다. 씬 커밋 금지 규칙이 있으므로,
> "필드를 제거하고 런타임에 유도" 방식이어야 씬을 건드리지 않는다. 필드를 남긴 채
> 값만 바꾸면 씬 4개를 각 담당자가 저장해야 한다.

**파급(값이 6배가 되는 것들)** — 전부 `DayLengthSeconds` 기반이라 자동으로 늘어난다. 재튜닝 대상이다.

| 대상 | 지금 | 12분 하루에서 |
|---|---|---|
| 건설시간 집 2게임시간 | 10초 | **60초** |
| 채용 램프 만석까지(회사) | 15초 | **90초** |
| 러시아워 맥동 주기 | 120초 | 720초 |

건설시간은 현재 `.asset` 값이 전부 0(기능 OFF)이므로 이 단계에서는 영향이 없다. 켤 때 재튜닝한다.

**검증**: 게임 시각 1시간 경과에 실시간 30초가 걸리는가. `DemandPulse`의 피크가 출퇴근 파도와 같은 주기로 오는가(현재는 5배 어긋남).

### 2.2 ② 자정 넘김 지원

**문제**: §1.2. **목표**: `출근시각 > 퇴근시각`인 근무(야간)를 표현할 수 있게 한다.

**방향**: 시각 비교를 **순환 구간 포함 판정**으로 바꾼다. 새 개념을 만들지 않고 판정 함수 하나를 도입한다.

```csharp
// 순수 함수 — 테스트하기 쉽고 결정론적이다.
// start <= end 면 통상 구간, start > end 면 자정을 넘는 구간으로 해석한다.
static bool InWindow(float hour, float start, float end) =>
    start <= end ? (hour >= start && hour < end)
                 : (hour >= start || hour < end);
```

바꿔야 하는 세 지점:

| 위치 | 지금 | 바뀌는 것 |
|---|---|---|
| `UpdateDepartures` 출근 (`:172`) | `hour >= DepartHomeHour && hour < _eveningEnd` | 전역 `_eveningEnd` 게이트를 **차별 근무 구간 판정**으로 교체 |
| `UpdateDepartures` 퇴근 (`:175`) | `hour >= DepartWorkHour` | 근무 구간을 벗어났는가로 교체 |
| `SnapCar` (`:219`) | `hour >= _eveningStart && hour < _eveningEnd` | 차별 근무 구간 안이면 `ParkedWork` |

> **핵심 변화**: 지금 `_morningEnd`·`_eveningStart`·`_eveningEnd`는 **스케줄러의 전역 필드**다
> (`:53`에서 `Rebuild`가 저장). 유형별 창이 생기면 전역값으로는 판정할 수 없다.
> 판정 기준을 **차 자신의 `DepartHomeHour`/`DepartWorkHour`**로 옮긴다.
> 그러면 ③에서 유형별 창을 넣어도 이 로직은 그대로 성립한다.

**회귀 위험이 가장 큰 단계다.** 통근 상태 전이 전반이 걸리고, 기존 EditMode 통근 테스트가 전역 창 전제로 쓰였을 수 있다.

**검증**: 기존 통근 테스트 전량 green 유지 + 신규 — 자정을 넘는 근무(예: 출근 20 / 퇴근 5)가 20시에 출근하고 5시에 퇴근하는가, 그 사이 22시·2시에 `ParkedWork`인가, 로드 시각이 23시·3시일 때 `SnapCar`가 `ParkedWork`로 수렴하는가.

### 2.3 ③ 회사 3종 + 유형별 창

**데이터** — 새 작은 SO를 만든다. `BuildingDefinitionSO`는 특수건물이 쓰는 그릇이고 안 쓰는 필드가 딸려 있어 재사용하지 않는다.

```csharp
// Configs/Buildings/CompanyTypeSO.cs (신규)
public sealed class CompanyTypeSO : ScriptableObject
{
    public string companyTypeId;     // "office" | "factory" | "warehouse"
    public string displayName;
    public int    capacity;          // 유형별 정원 (지금은 SimConfig.OfficeCapacity 공통)
    public float  workStartHour;     // 출근 창 시작
    public float  workStartWindow;   // 창 길이(시간). StaggerHour가 이 안에 흩뿌린다
    public float  workEndHour;       // 퇴근 창 시작
    public float  workEndWindow;
}
```

```csharp
// Configs/Buildings/CompanyTypeCatalogSO.cs (신규)
// BuildingCatalogSO 와 같은 형태 — id로 조회, 중복·누락 경고
```

유형 추가가 **에셋 편집만으로** 끝나야 한다(코드 변경 0). 4번째 회사를 넣을 때 필드를 파지 않는다.

**보관** — `DemandMap.CompanyCapacityState`(`:50`)에 `CompanyTypeId`를 추가한다. 이미 `Type`·`TotalCapacity`·`BuiltAtSimSeconds`를 들고 있는 자리이고, 정원이 유형별로 갈리므로 같은 곳에 있는 것이 맞다.

**배치 경로** — `SimEngine.Place`에 **선택 인자**를 더한다. 기존 호출자는 영향받지 않는다.

```csharp
public bool Place(Vector2Int tile, TileType type,
                  PlacementDirection direction = PlacementDirection.North,
                  string companyTypeId = null)
```

**유형 미지정은 거부한다(§0.1 ⑥).** `type`이 `Office`인데 `companyTypeId`가 null이거나
카탈로그에 없는 값이면 **`false`를 반환하고 아무것도 놓지 않는다.**

조용히 사무실로 폴백하지 않는 이유: 건설 패널(타 담당자)이 유형을 안 넘기는 실수를 해도
에러가 안 나고 전부 사무실이 되어버린다. 이 저장소가 결합표까지 만들어 경계하는
"에러 없이 조용히 깨지는" 형태다. 거부하면 실수가 즉시 드러난다.

`Office`가 아닌 타입(House·School·Hospital 등)에는 `companyTypeId`가 무의미하므로 무시한다 —
기존 호출자는 영향받지 않는다.

`SimEngine.cs:536`의 `_demand.RegisterCompany(tile, type, _simTime)`에 id를 함께 넘긴다.
건설시간 완성 경로(`:576`)도 같은 값을 넘겨야 하므로 **`ConstructionSite`에 `CompanyTypeId`를 실어야 한다** — 공사 중에 유형 정보가 유실되면 완성 시 사무실로 되돌아간다.

**창 전달** — `Rebuild`에 콜백 하나를 추가한다. 기존 `workCapacityFor`와 같은 형태다.

```csharp
public void Rebuild(IReadOnlyList<Vector2Int> sources, IReadOnlyList<Vector2Int> sinks,
    Func<Vector2Int, int> workCapacityFor,
    Func<Vector2Int, CommuteWindow> windowFor,      // 신규
    int homeSlots, int maxCars, bool deferNewAssignments = false)
```

`morningStart`/`morningEnd`/`eveningStart`/`eveningEnd` 4개 인자는 **콜백으로 대체되어 사라진다**. 호출부(`CarSim.cs:358`)가 `_cfg.MorningStartHour` 등을 넘기던 자리다.

```csharp
public readonly struct CommuteWindow      // Contracts 또는 Sim 내부
{
    public readonly float StartHour, StartWindow, EndHour, EndWindow;
}
```

차 생성 시점(`CommuteScheduler.cs:123-124`)은 이렇게 바뀐다.

```csharp
CommuteWindow w = windowFor(sinks[i]);
var fresh = new CommuteCar {
    ...
    CompanyTypeId  = w.CompanyTypeId,                                            // 신규(§0.1 ①)
    DepartHomeHour = StaggerHour(sources[i], w.StartHour, w.StartHour + w.StartWindow),
    DepartWorkHour = StaggerHour(sources[i], w.EndHour,   w.EndHour   + w.EndWindow),
};
```

`CommuteWindow`가 `CompanyTypeId`를 함께 실어 오므로 조회가 한 번으로 끝난다.
필드는 **캐시**이고 출처는 콜백이다 — 둘의 역할이 겹치지 않는다.

`StaggerHour`는 그대로다. **집 좌표 해시로 분산**하는 성질이 유지되므로 같은 회사에 다니는 이웃들이 조금씩 다른 시각에 나선다.

**`SimConfig`의 기존 창 4필드** — `MorningStartHour`/`MorningEndHour`/`EveningStartHour`/`EveningEndHour`는 카탈로그가 비어 있을 때의 **폴백 기본값**으로 강등한다. 삭제하지 않는다(`.asset` 3개와 기존 테스트가 쓴다).

**정원** — `SimConfig.OfficeCapacity`(현재 6)도 같은 방식으로 폴백이 된다. `CompanyTypeSO.capacity`가 있으면 그것을 쓴다.

### 2.4 시간대 값 — **지금 정할 수 있다** (①을 기다리지 않는다)

초안에서 "① 확정 후 채운다"고 적었으나 **정정한다.** 시각 값은 하루 길이와 무관하다.

```
DepartHomeHour / DepartWorkHour            게임시간 [0,24) 단위
StaggerHour(home, windowStart, windowEnd)  게임시간 단위
```

하루가 2분이든 12분이든 **"06시 출근"은 06시**다. ①이 바꾸는 것은 "게임 1시간이 실시간 몇 초냐"
뿐이고 "몇 시에 나가냐"가 아니다. 따라서 ③은 ①을 기다리지 않고 값까지 확정할 수 있다.

다만 **체감**은 하루 길이에 달렸다 — 지금(하루 24초)은 창 4시간이 4초라 세 유형이 갈리는 게
눈에 안 보이고, 12분 하루에서 2분이 되어야 보인다. 값은 지금 정하되 **라이브 확인은 ① 이후**다.

| 유형 | 성격 | 창이 겹치는 정도 |
|---|---|---|
| 사무실 | 오전 출근 | 기준 |
| 물류창고 | 새벽~중간 출근 | 사무실보다 이르게, 살짝 겹치게 |
| 공장 | 야간 출근 (자정 넘김) | 앞 둘과 어긋나되 완전 분리는 아니게 |

> 선행 기획 §5-③의 권고를 따른다 — **"겹치되 어긋나게."** 완전히 분리하면 세 유형이
> 도로를 공유할 이유가 없어져 조정 플레이가 죽는다.

---

## 3. 테스트

### ① 시간 축
- 게임 시각 1시간 경과에 실시간 30초가 걸린다
- `DemandPulse`의 피크 주기와 출퇴근 주기가 일치한다(현재는 5배 어긋남)

### ② 자정 넘김 — `InWindow` 순수 함수 + 통합
- `InWindow(hour, 6, 15)` 통상 구간: 6·10·14 참 / 5·15·20 거짓
- `InWindow(hour, 20, 5)` 순환 구간: 20·23·0·4 참 / 5·10·19 거짓
- 출근 20 / 퇴근 5인 차: 20시 `Outbound`, 22시·2시 `ParkedWork`, 5시 `Inbound`
- 로드 23시·3시 → `SnapCar`가 `ParkedWork`
- **기존 통근 테스트 전량 green 유지**

### ③ 회사 3종
- 유형별 정원이 카탈로그 값을 따른다(폴백은 `SimConfig.OfficeCapacity`)
- 사무실 옆 집의 차와 공장 옆 집의 차가 **서로 다른 시각**에 출발한다
- 같은 유형·다른 집의 차는 창 안에서 분산된다(`StaggerHour` 성질 보존)
- 공사 중 저장 → 로드 → 완성 시 **유형이 보존된다**(`ConstructionSite.CompanyTypeId`)
- 카탈로그가 비면 전 회사가 폴백 창·폴백 정원으로 동작한다(구세이브·미설정 방어)

---

## 4. 소유권 경계

| 파일 | 성격 | 절차 |
|---|---|---|
| `Sim/CommuteScheduler.cs` | Sim 코어 — 통근 상태기 | ②가 여기를 크게 고친다. 사전 공유 |
| `Sim/CarSim.cs`, `Sim/DemandMap.cs`, `Sim/SimEngine.cs` | Sim 코어 | PR |
| `Gameplay/Progression/GameCalendarService.cs` | ① 소관 — **준희님** | 우리는 안 건드린다 |
| `Configs/Buildings/CompanyTypeSO.cs` 등 | 신규 | — |
| `SimConfig` 폴백 강등 | 필드 삭제 없음 | 07-22 규칙(`.asset` 3개 동시) |
| 통합 씬 4개 | ①이 `realSecondsPerGameHour` 직렬화 값과 얽힘 | **씬을 안 건드리는 방식으로 설계**(§2.1) |

> 선행 기획 §4는 "Sim/라우팅 원저작 = 이진우, 환 단독 선행 금지"를 인용하나,
> `AGENTS.md:34`는 그 선행 금지 조항이 **2026-07-20에 삭제**됐다고 적는다.
> 현행 규칙(`docs/2026-07-21-parallel-work-ownership.md`)은 "타인 소유 파일은 수정 가능하되
> 반드시 미리 공유"다. **사전 공유는 하되 착수를 막지는 않는다.**

---

## 5. 하지 않을 것

| 항목 | 사유 |
|---|---|
| `TileType` 확장 | 갈래 B로 회피. `Office` 하나에 인스턴스 데이터 |
| 가게·상점 | 특수건물 8종이 상점 카테고리로 간다. 본 설계는 "큰 회사" 3종 |
| 건설 패널 카테고리 재편 | 다른 담당자 |
| 교대근무(개인 단위 시뮬) | 공장은 "야간에 출퇴근하는 근무지"까지. 개인별 교대 로테이션 없음 |
| 화물차·방문객 트래픽 | 특수차/멀티스톱 기능군. 범위 밖 |
| 진짜 수요 곡선(정규분포 피크) | 지금은 균등분포 창. 창만 유형별로 나눠도 목표는 달성된다 |
| 유형별 건물 비주얼 | 리스킨 작업과 병렬. 본 설계는 Sim까지 |

---

## 6. 리스크

1. **②가 통근 상태기 전반을 건드린다.** 기존 EditMode 통근 테스트가 전역 창(`_eveningEnd` 등)을 전제로 쓰였다면 함께 고쳐야 한다. 수치 갱신은 하되 **실패를 덮는 완화는 금지.**
2. **①이 씬 직렬화와 얽힌다.** `realSecondsPerGameHour`를 필드로 남긴 채 값만 바꾸면 씬 4개를 각 담당자가 저장해야 한다. 필드 제거 + 런타임 유도 방식이어야 씬 무접촉이 된다.
3. **①로 건설시간·채용 램프가 6배 느려진다.** 건설시간은 현재 값이 0이라 무영향이지만, 채용 램프는 즉시 체감된다. **① 담당자(준희님)에게 이 파급을 반드시 알려야 한다** — `DayLengthSeconds` 하나를 바꾸면 자기 작업 밖의 두 시스템이 함께 6배가 된다.

6. **우리 작업이 ①에 막혀 있다.** ①이 늦어지면 ②③도 늦어진다. 다만 ②의 `InWindow` 순수 함수와 ③의 `CompanyTypeSO`·카탈로그는 하루 길이와 무관하므로, ① 대기 중에도 **구조 작업은 선행 가능**하다. 시각 상수만 ① 이후에 채운다.
4. **공사 중 유형 유실.** `ConstructionSite`에 `CompanyTypeId`를 싣지 않으면 공사 중 저장→로드→완성에서 전부 사무실이 된다. ③의 필수 항목이다.
5. **선행 기획 문서가 낡았다.** §1.3의 2건 외에도 07-24 이후 develop이 크게 움직였다(#159·#163·#167·#169). 그 문서를 규칙으로 인용하지 말고 코드를 먼저 확인할 것.

# 건물 건설시간 설계 — 타입 승격 방식

- 작성: 2026-07-29 · 기준 커밋: `develop b4690be` (#169 머지 직후)
- 파일:라인은 전부 위 커밋 실측
- 관련 기존 장치: 채용 램프(`CompanyCapacityCalculator`), 건물 변경 이연(#156)

---

## 0. 요구사항 (확정)

| 항목 | 결정 |
|---|---|
| 역할 | **페이싱 + 연출** — 공사 중 기능 정지, 비주얼도 공사 중 |
| 범위 | **건물만.** 도로·신호등·회전교차로·입체교차는 즉시 완성 |
| 시간 기준 | **게임 시간(`_simTime`), 오프라인 정지** |
| 길이 | **건물 종류별 고정값** |
| 세이브 | **진행도 유지** |
| 튜닝값 위치 | **`SimConfig`** (+ `.asset` 3개 동시 기입) |

## 1. 설계의 난점과 채택 근거

"공사 중엔 기능 정지"를 강제하는 것이 설계의 전부다. 건물 타입을 보는 코드는
**15개 파일, 100곳 이상**이고 소유자가 4명(환·주석·준희·진우)에 걸쳐 있다.

```
MainCityView 34 · BuildingStoryDataFactory 10 · DemandMap 10 · EmergencyIncidentSystem 10
FacilityInfluenceSelectionController 8 · AnalysisCardController 5 · SimEngine 5
PopulationSystem 4 · HospitalSystem 4 · BuildPanelController 3 · CityQuestSystem 3 ...
```

소비자를 전부 고쳐서 게이트를 거는 설계(대안 B)는 **하나만 빠뜨려도 "공사 중인 병원이
진료를 한다"가 에러 없이 발생**하고, 앞으로 소비자가 늘 때마다 지켜야 할 규칙이 하나
늘어난다. 컴파일러가 안 잡아주는 규칙이다.

**채택: 타입 승격.** 공사 중엔 `CityGrid`에 실제 타입 대신 `UnderConstruction`을 넣고,
완성 시 실제 타입으로 갈아끼운다. 기능 정지가 **코드가 아니라 데이터로** 강제되므로
소비자가 몇이든 자동으로 지켜진다.

기각한 대안:

| 대안 | 기각 사유 |
|---|---|
| B. 별도 서비스 + 소비자 게이트 | 소비자 100곳 수정, 소유자 4명 전원 PR. 누락이 조용히 깨짐 — #169의 월드 크기 소비자 누락과 같은 함정 |
| C. 배치 자체를 지연(큐잉) | 공사 중 타일이 점유되지 않아 같은 자리 중복 배치 가능. 겹침 검증이 `CityGrid`와 서비스로 갈라져 **이중 권한**을 새로 만든다 — 이 팀이 07-24에 없앤 바로 그 문제 |

### 1.1 실측으로 해소된 위험 2건

```
① CityGrid.cs:197  IsIntersection(): GetTile(t) != TileType.Road → false
   새 TileType이 신호·로터리 컬링(SimEngine.RebuildSignals)을 건드릴 수 없다.
   CLAUDE.md 결합표의 "새 TileType" 경고 항목은 본 설계에 해당하지 않는다.

② switch (TileType) / case TileType. 을 쓰는 파일 = 5개뿐
   BusStopRegistry · SimDebugOverlay · SimTileRenderer · CityQuestSystem · AnalysisCardController
   나머지 소비자는 `== TileType.House` 식 등가 비교라 새 타입에 자동 false
   = 기능 정지가 그대로 달성된다.
```

`TileFootprint.IsBuilding()`(`CityFlowTypes.cs:32`)이 `!= Empty && != Road`이므로
`UnderConstruction`도 building으로 분류된다 — **2x2 풋프린트 예약이 그대로 유지되어
겹침 방지가 공짜**다. 이는 의도된 동작이다.

## 2. 상태 모델

```csharp
// Contracts/CityFlowTypes.cs — 맨 뒤에 추가(기존 값 번호 불변 = 세이브 하위호환)
public enum TileType {
    Empty, Road, House, Office, School, Hospital, SpecialBuilding,
    UnderConstruction
}
```

```csharp
// Sim 내부 (internal)
internal readonly struct ConstructionSite {
    public Vector2Int Anchor;
    public TileType TargetType;
    public PlacementDirection Direction;
    public double CompleteAtSimSeconds;
}
```

```csharp
// Sim/CityGrid.cs — 신규 internal 메서드
// Place()는 CanPlace()로 점유 타일을 거부하므로 승격 전용 경로가 필요하다.
// 풋프린트 타일들의 타입만 교체하고 anchor/direction은 보존한다.
internal bool Promote(Vector2Int anchor, TileType targetType)
```

## 3. 진행과 완성 — 기존 인과사슬 재사용

```csharp
// SimEngine.Place (현재 L503-520) — 건물이면 공사로 시작
if (TileFootprint.IsBuilding(type)) {
    if (!_grid.Place(tile, TileType.UnderConstruction, direction)) return false;
    _construction.Register(tile, type, direction, _simTime + DurationSeconds(type));
    _events.QueuePlaced(new PlacedEvent(tile, TileType.UnderConstruction, false, direction));
    return true;
}
// 도로 등 비건물은 현행 경로 그대로(즉시 완성)
```

```csharp
// SimEngine.Step 첫머리 — 채용 램프 바로 옆(현재 L179)
_demand.AdvanceCompanyCapacities(_simTime);
AdvanceConstruction(_simTime);          // 신규

// 완성 시 = 현재 Place의 후처리를 그대로 실행
_grid.Promote(anchor, targetType);
if (targetType == TileType.Office) _demand.RegisterCompany(anchor, targetType, _simTime);
if (targetType == TileType.Office || targetType == TileType.School) _demandRebalancePending = true;
_buildingAssignmentChangePending = true;
_events.QueuePlaced(new PlacedEvent(anchor, targetType, false, direction));
```

**핵심 성질**: 완성 시점이 곧 현재의 배치 시점이 된다. 새 인과관계를 만들지 않고
**기존 인과사슬의 발화 시점만 뒤로 미는 것**이다. 채용 램프도 완성 시각(`_simTime`)부터
시작하므로 "공사 끝 → 채용 시작"이 자연스럽게 직렬로 이어진다.

`_simTime`은 `Step()`에서만 증가하므로 **오프라인 정지가 자동으로 성립**한다.
오프라인 정산(`GameCalendarService`) 배선은 접촉하지 않는다.

### 3.1 철거

`SimEngine.Remove`(`SimEngine.cs:540`) 경로에서 `_construction.Cancel(anchor)`를 호출해
사이트를 제거한다.

**환불은 Sim이 관여하지 않는다.** 실측 결과 환불은 UI 층
(`InfrastructurePlacementCoordinator.cs:396,442,502`)이 롤백·인프라 철거에서 처리하며
Sim은 경제를 모른다. 공사 중 철거의 환불 여부는 **기존 건물 철거와 동일한 경로**를 타고,
공사 중이라고 특별 대우하지 않는다. Sim이 경제에 개입하면 계층 경계가 깨진다.

### 3.2 `_construction` 위치

`SimEngine`의 private 필드로 둔다(`ConstructionSites` internal 클래스).
`DemandMap`에 넣지 않는 이유: `DemandMap`의 책임은 수요 배정이고, 집·학교·병원은
그 관심사가 아니다. 채용 램프와 나란히 `Step()` 첫머리에서 구동되지만 소유는 분리한다.

```csharp
double DurationSeconds(TileType type)   // SimEngine private
    => HoursFor(type) * _config.DayLengthSeconds / 24d;
```

## 4. 세이브

```csharp
// Contracts/Save/SimSaveData.cs
public ConstructionSaveData[] Constructions;   // 구세이브 = null(공사 0건) — 마이그레이션 공짜

[Serializable] public sealed class ConstructionSaveData {
    public int X, Y;
    public TileType TargetType;
    public PlacementDirection Direction;
    public float RemainingSimSeconds;   // 절대시각이 아닌 상대 잔여값
}
```

- 기존 `Roundabouts`/`Oneways`/`TurnSigns` 배열과 동일한 "구세이브 = null" 패턴 → 하위호환 공짜
- **잔여시간(상대값)으로 저장한다.** 절대 완료시각으로 저장하면 로드 후 `_simTime` 기준이
  달라져 즉시 완성되거나 영원히 안 끝난다
- `PlacedTiles`에는 `UnderConstruction` 타입으로 저장된다(별도 처리 불필요)
- `RestoreSnapshot`에서 사이트를 `_simTime + RemainingSimSeconds`로 재등록

## 5. 튜닝값 — SimConfig

```csharp
// Sim/SimConfig.cs 선언부 — 채용 램프 필드 근처
// ── 건물 건설시간 (게임시간) ─────────────────────
public float ConstructionHoursHouse;      // 🔓
public float ConstructionHoursOffice;     // 🔓
public float ConstructionHoursSchool;     // 🔓
public float ConstructionHoursHospital;   // 🔓
public float ConstructionHoursSpecial;    // 🔓
```

```csharp
// Default()는 전부 0 = 즉시 완성.
// 근거: Default()는 "테스트/디버그용"으로 명시돼 있고(SimConfig.cs:120),
// 0이 아닌 값을 넣으면 BuildStraightCity류 헬퍼를 쓰는 기존 EditMode 테스트가
// "집·회사를 놓으면 바로 통근이 돈다"는 전제 때문에 대량으로 깨진다.
// 부수 효과로 기능이 config 단위 opt-in이 되어 롤아웃도 안전하다.
ConstructionHoursHouse    = 0f,
ConstructionHoursOffice   = 0f,
ConstructionHoursSchool   = 0f,
ConstructionHoursHospital = 0f,
ConstructionHoursSpecial  = 0f,
```

**실제 게임 값은 `.asset` 3개에만 기입한다** (아래 제안값, 라이브 튜닝 전제).

**필수 절차 (2026-07-22 팀 규칙)**: `.asset` **3개를 반드시 함께 채운다.**
누락 시 조용히 0이 들어가 공사가 즉시 끝난다 — 순서가 아니라 **누락**이 위험이다.

```
Assets/05_ScriptableObjects/SimConfig.asset
Assets/05_ScriptableObjects/SimConfig_Integrated.asset
Assets/05_ScriptableObjects/SimConfig_Sandbox.asset
```

**환산식** — 채용 램프(`CompanyCapacityCalculator.cs:26-29`)의 역산:

```
durationSimSeconds = hours × DayLengthSeconds / 24
```

`SimConfig.DayLengthSeconds = 120f`(`SimConfig.cs:166`)이므로 **1 게임시간 = 5 시뮬초**다.
`.asset` 3개에 넣을 제안값과 실제 길이:

| 종류 | 게임시간 | 시뮬초 |
|---|---|---|
| House | 2 | 10s |
| Office | 4 | 20s |
| School | 6 | 30s |
| Hospital | 8 | 40s |
| Special | 6 | 30s |

참고: Office는 완성(20s) 후 채용 램프가 이어진다. `CompanyHiringSlotsPerGameHour = 2`,
`OfficeCapacity = 6`이므로 만석까지 3 게임시간(15s) 추가 — 배치부터 만원까지 총 35s다.
이 총합이 체감상 긴지는 라이브 튜닝에서 판단한다.

**0 이하 방어**: 값이 0 이하면 즉시 완성으로 처리한다(구 config·미기입 자산 방어).

## 6. 뷰 계약과 프리팹

```csharp
// Contracts/IReadOnlyTileData.cs — 진행도 조회 (View 전용, 판단 없음)
bool TryGetConstructionProgress01(Vector2Int tile, out float progress01);
```

- `MainCityView`의 타일 비주얼이 `UnderConstruction`에 공사 비주얼(골조/펜스)을 그린다
  — **이진우 소유 구역**(`docs/2026-07-21-parallel-work-ownership.md`)
- `BuildingConstructionSystem.prefab` — 진행도 HUD·완성 연출 전담
- **프리팹은 표시 전담이다.** 진행·완성 판정은 `SimEngine`이 돌므로 프리팹이 씬에 없어도
  게임은 정상 작동하고 표시만 없다. 배선 실수에 안전한 구조다
  (#169의 `WorldGridSystem.prefab`처럼 배선 여부가 시뮬 동작을 가르지 않는다)

## 7. 테스트 (EditMode, `CityFlow.Sim.Tests`)

| 테스트 | 단정 |
|---|---|
| 배치 직후 기능 정지 | House 배치 후 즉시 → 통근 배정 0, `GetTile` == `UnderConstruction` |
| 완성 후 기능 시작 | 공사시간 경과 후 → `GetTile` == `House`, 배정 발생 |
| 회사 램프 직렬 | Office 완성 시각부터 채용 램프 시작(완성 전 `TryGetCompanyStaffing` 미응답) |
| 겹침 방지 | 공사 중 타일 위 배치 거부 |
| 오프라인 정지 | `Tick` 미호출 구간에서 진행 없음 |
| 세이브 왕복 | 잔여시간 유지, 로드 후 이어서 완성 |
| 구세이브 호환 | `Constructions == null` → 예외 없이 공사 0건 |
| 0 이하 방어 | `ConstructionHours* = 0` → 즉시 완성 |
| 철거 | 공사 중 철거 시 사이트 제거, 잔여 사이트 없음 |

기준선: **현재 develop `b4690be` = EditMode 394/394 green** (2026-07-29 실측).
CLAUDE.md의 340은 낡은 값이다.

## 8. 소유권 경계

| 파일 | 소유/성격 | 절차 |
|---|---|---|
| `Contracts/CityFlowTypes.cs` (`TileType`) | **루트 계약, 전 어셈블리 영향** | 팀 사전 공유 필수 |
| `Contracts/Save/SimSaveData.cs` | 세이브 계약 | 공유 |
| `Contracts/IReadOnlyTileData.cs` | 계약 | 공유 |
| `Sim/SimConfig.cs` + `.asset` 3개 | 필드 추가 — 07-22 완화 규칙 적용 | `.asset` 3개 동시 기입 |
| `Sim/SimEngine.cs`, `Sim/CityGrid.cs` | Sim 코어 | PR |
| `View/MainCityView.cs` 타일 비주얼 | **이진우** | PR + 원작성자 승인 |
| `switch (TileType)` 5개 파일 | 각 소유자 | 전수 검토 후 PR |

## 9. 하지 않을 것 (범위 밖)

- 도로·신호등·회전교차로·입체교차의 건설시간 — 즉시 완성 유지
- 건설 취소 UI·일시정지·가속(러시 결제) — 후속
- 오프라인 공사 진행 — 명시적으로 정지
- 건설비 비례 시간 — 종류별 고정값만
- 공사 인부·차량 등 별도 시뮬 엔티티

## 10. 남는 리스크

1. `TileType`은 `Contracts`의 루트라 enum 추가가 전 어셈블리에 노출된다. 실측으로
   `(int)TileType` 캐스팅·enum 배열 인덱싱 사용처는 **0건**이라 그 경로의 위험은 없다.
   남는 것은 `switch` 5개 파일의 `default` 누락뿐이며 전수 검토로 커버한다.
2. 세이브에 `UnderConstruction` 타일이 들어가므로, 이 값을 모르는 **구버전 클라이언트가
   신버전 세이브를 읽으면** 알 수 없는 타입이 된다. 팀이 구버전 호환을 요구하지 않으면 무시.
3. 공사 중 건물이 `IsBuilding() == true`라 도로 예산·통계 집계에 건물로 잡힐 수 있다.
   집계 소비자(`StatsPanelController`, `CityQuestSystem`)에서 의도와 맞는지 확인 필요.

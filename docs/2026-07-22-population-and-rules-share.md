# 공유 — 인구 시스템 + 소유권 규칙 완화 (2026-07-22, 환)

오늘 올린 PR 3개와, **전원이 알아야 할 사항 1건**을 정리합니다.

| PR | 내용 | 관련 |
|---|---|---|
| [#123](https://github.com/hjh8874/GreenLight/pull/123) | 교차로 방향별 대기 차량 수 API | **김건** |
| [#127](https://github.com/hjh8874/GreenLight/pull/127) | 집마다 인구 만들기 | **진우** |
| [#128](https://github.com/hjh8874/GreenLight/pull/128) | 소유권 규칙 3건 완화 | **전원** |
| [#130](https://github.com/hjh8874/GreenLight/pull/130) | 회사별 정원 + 구인 진행 | **전원 · 주석 · 진우** |

> #130은 **스택 PR**입니다 (base가 `develop`이 아니라 `feat-population-hwan`).
> #127을 먼저 봐주세요.

---

## 🔴 전원 — develop에 실패하는 테스트가 7건 있습니다

이번 작업들과 **무관하게 원래 빨간 상태**입니다. 회귀 판정 기준선이 0이 아니니
"테스트가 실패한다"만으로 자기 작업을 의심하지 마세요.

```
RoadQueueNetworkTests.Step_CrossingAxes_OnlyOnePasses             Expected 1, was 2
RoadQueueNetworkTests.Step_OpposingUTurns_DoNotPassTogether       Expected 1, was 2
RoadQueueNetworkTests.Step_OpposingStraights_BothCrossInSameTick  Expected 0, was 1
RoadQueueDeviceTests.IntersectionEntry_ConflictingOccupant_ClearsBeforeNextEntry
RoadQueueDeviceTests.IntersectionEntry_ConflictingStraightPaths_YieldBeforeEntering
RoadQueueDeviceTests.IntersectionSharedBudget_PrioritizesStraightThenAllowsCompatibleTurns
RoadQueueDeviceTests.IntersectionSharedBudget_PriorityAxisWinsAndOtherQueueRemains
```

"교차하는 축은 한 틱에 한 대만 빠져나가야 한다 — Expected 1, but was 2"
→ **차가 겹쳐 통과하고 있습니다.** 「차 겹침 해결」의 회귀 테스트가 빨간 채로 남아 있는
상태로 보입니다. 담당자 확인이 필요합니다.

**회귀 판정 방법**: 변경 전후로 같은 테스트를 돌려 **실패 목록을 비교**하세요.
위 7건과 정확히 같으면 회귀 0입니다.

---

## 🔔 전원 — `SimConfig`가 바뀌어 씬 동작이 달라집니다 (PR #130)

`.asset` 3개는 규칙대로 전부 채웠지만, 아래는 알고 계셔야 합니다.

### 1. `OfficeParkingSlots` 필드가 **삭제**됐습니다

참조하시던 코드가 있으면 `OfficeCapacity`로 바꿔주세요.

**왜**: 정원이 두 개로 갈려 어긋나 있었습니다. `OfficeCapacity`(20)는 회사에 배정되는
**집** 수, `OfficeParkingSlots`(6)는 주차 가능한 **차** 수였습니다. 집 20채가 배정되는데
6대만 주차할 수 있어 **나머지 14채가 영원히 출근 못 하는 유령 집**이 됐습니다.
이미 배정됐으니 자리 남는 다른 회사로도 못 갑니다. 집이 6채 이하면 안 드러납니다.

→ 하나로 통일했습니다. 배정과 주차가 같은 정원을 봅니다.

### 2. `OfficeCapacity`가 **20 → 6**입니다

회사 하나가 받는 집이 3분의 1로 줄어 **"일자리 부족"이 훨씬 빨리 드러납니다.**
경제 밸런스가 실제로 움직이니 「차 늘린 뒤 안전 검사 + 돈 밸런스」에서 확인이 필요합니다.

### 3. 🚗 씬에 미리 놓인 회사도 t=0부터 구인 램프를 탑니다

**디버그 씬을 켜면 처음 ~3게임시간 동안 통근 차가 없습니다.**
버그가 아니라 의도된 설계입니다. "차가 왜 안 나오지?" 하고 시간 쓰지 마세요.

건설 직후 자리가 0이고 시간당 2자리씩 열려 정원까지 찹니다
(`CompanyHiringSlotsPerGameHour`, 조정 가능).

### 4. 주석님 — `CarSimOfficeParkingSlots`의 값 출처가 바뀌었습니다

`SimEngine.CarSimOfficeParkingSlots`가 이제 `OfficeCapacity`를 읽습니다.
`CarMotion.cs`는 **건드리지 않았습니다.** 다만 이름이 실제 의미(정원)와 어긋나게 됐는데,
소유 파일을 안 건드리려고 유지했습니다. 정리하고 싶으시면 말씀해주세요.

---

## 📋 전원 — 소유권 규칙 3건이 완화됐습니다 (PR #128)

기다리지 않아도 되는 것이 늘었습니다.

### `SimConfig` 필드 추가 — 이제 누구나 합니다

기존 규칙("담당자 1명만 편집")의 근거였던 **"필드 순서가 바뀌면 남의 값을 읽는다"는
사실이 아니었습니다.** `.asset` YAML을 확인한 결과 Unity는 필드를 **이름으로** 직렬화합니다.

**진짜 위험은 누락입니다.** 새 필드는 기존 `.asset` 3개에 키가 없어 **0으로 조용히
들어갑니다.** 에러가 안 납니다.

→ 필드 추가 시 `Default()` **그리고 `.asset` 3개**(`SimConfig` · `SimConfig_Integrated` ·
`SimConfig_Sandbox`)를 **반드시 함께** 채워주세요. `Default()` 충돌은 append-only라
양쪽 줄을 다 살리면 됩니다.

### `MainCityView` SerializeField — 자기 구역 안에서 자유롭게

위험은 "추가하는 것"이 아니라 **남의 필드 옆에 끼워 넣는 것**이었습니다.

→ 구역을 주석 헤더(`// ── [환] 로터리·시뮬 ──`)로 나누고 **사이에 빈 줄 3줄 이상**을
두세요. git 헝크가 3줄 컨텍스트를 잡으므로 이 여백만으로 동시 추가가 병합됩니다.
추가는 **자기 구역 끝에만** 합니다.

### 충돌 해결

- 남의 소유 파일 충돌 → 고치지 말고 넘긴다 → **수정 가능, 단 미리 공유**

### 안 바뀐 것
**통합 씬 커밋 금지(금지 1번)는 그대로입니다.** `.unity` YAML은 실제로 병합이 안 됩니다.

---

## 진우 — `Contents/` 수정했습니다 (PR #127)

`Contents/`가 진우님 소유라 사전 공유드립니다.

### 무엇

지금까지 **모든 집이 무조건 5명**이었습니다. 이제 **학교 커버 여부에 따라 집마다 달라집니다.**

```
집 인구 = 기본 2 + 학교 커버 +2   (커버 반경 3칸, 맨해튼 거리)
```

전에는 `총인구 = 집 개수 × 5`라서 인구가 집 개수의 별칭이었습니다. 도시 단계 기준으로
쓸 수 없었고 "학교 근처만 혜택"이 들어갈 자리도 없었습니다.

### 변경 파일

| 파일 | 내용 |
|---|---|
| `Contents/Logic/PopulationCalculator.cs` | **신규** — 계산·커버 판정을 MonoBehaviour 밖 순수 함수로 |
| `Contents/Logic/CityFlow.Content.asmdef` | **신규** — 위 함수를 EditMode에서 테스트하려면 필요 |
| `Contents/PopulationSystem.cs` | 타일별 계산 + **학교 배치/철거 시 반경 안 집 재계산** |
| `Contents/PopulationConfigSO.cs` | 커버 반경·가산 필드 (116줄 → 60줄로 감소) |
| `PopulationConfig.asset` | House 인구 `5` → `2` |

**핵심은 재계산 경로입니다.** 기존 `OnPlaced`는 **놓인 타일 하나만** 갱신해서, 학교를
지어도 주변 집 인구가 변하지 않았습니다. 배치·철거 양쪽 모두 반경 재계산으로 확장했습니다.

### asmdef를 새로 만든 이유

`Contents/`는 asmdef가 없어 Assembly-CSharp에 있었고, **테스트 어셈블리는 Assembly-CSharp을
참조할 수 없습니다.** `Contents/Logic/` 아래에만 걸었고 `PopulationSystem.cs`는
Assembly-CSharp에 그대로 있습니다. 참조는 `CityFlow.Content → CityFlow.Contracts` 단방향뿐입니다.

### 조절 가능한 값 (인스펙터)
집 기본 인구 `2` · 학교 커버 가산 `+2` · 커버 반경 `3`

### 아직 안 되는 것 (의도적)
- **화면에 안 나옵니다** — HUD 배선은 다음 작업
- **차가 안 늘어납니다** — `CarsPerHouse`는 1 그대로. 주석님의 「건물공간+주차」가 끝나기
  전까지 스위치는 꺼둔 채 로직만 준비하는 것이 카드 지시입니다
- **병원 없습니다** — `TileType`에 Hospital이 없습니다

### 진우 — 「인구·채용 게이지」용 읽기 API 준비됐습니다 (PR #130)

```csharp
bool TryGetCompanyStaffing(Vector2Int tile, out CompanyStaffing staffing);
// staffing = (Filled, Capacity) — 예: 4/6
```

**UI는 만들지 않았습니다.** 읽는 계약만 뚫었으니 표시는 진우님 쪽에서 하시면 됩니다.
`Filled == Capacity`면 "구인 완료", 아니면 "구인중"입니다.

> 알려진 한계: 구인 진행 중에 저장했다 불러오면 **즉시 만석**으로 복원됩니다.
> 램프 진행도가 세이브 대상이 아닙니다 — 세이브 스키마 카드에서 다룰 사안입니다.

---

## 김건 — 대기 차량 API (PR #123)

「신호등 조작 창」에 필요한 데이터가 준비됐습니다.

```csharp
int GetQueueCount(Vector2Int tile, Dir entryDir);   // IReadOnlyTileData

// 사용 예
int horizontal = tileData.GetQueueCount(tile, Dir.E) + tileData.GetQueueCount(tile, Dir.W);
```

**알아두실 것**

1. `Dir`이 `CityFlow.Sim` → `CityFlow.Contracts`로 이동했습니다 (asmdef 경계 때문).
   UI 쪽은 대부분 이미 `using CityFlow.Contracts;`가 있어 그대로 될 겁니다
2. 그리드 밖 좌표는 예외 없이 **0**을 반환합니다. 화면 밖 방어는 불필요합니다
3. `FakeFlowReader`도 구현돼 있어 **Sim 없이 UI만 먼저** 붙여볼 수 있습니다.
   단 페이크의 최대 큐 길이가 `10` 상수라 실제 용량과 다릅니다 — **게이지 최댓값을
   이 숫자에 맞추지 마세요**

**아직 없는 것**: 대기 **시간**(초), 정시성·지각, 그린웨이브 달성 여부.
셋 다 Sim에 데이터 자체가 없습니다. 조작 창 1차에는 넣지 마세요.

> ⚠️ 「그린웨이브 보너스 연출」은 카드 설명보다 훨씬 큽니다. "이미 계산되는 값을 꺼내는
> 작업"이라고 적혀 있지만 **사실이 아닙니다.** `SignalMath.GreenWaveEfficiency`는
> 테스트에서만 호출되고 엔진에 호출이 0건입니다. 엔진 배선부터 하는 신규 작업입니다.

---

## 다음 작업 — 협의가 필요한 것

**「회사 종류 나누기」가 순수 Sim 작업이 아닙니다.**
`TileType`에 가게를 추가하면 파급이 이렇게 갑니다:

| 파일 | 수정 지점 | 소유 |
|---|---|---|
| `View/MainCityView.cs` | **12곳** (색·크기·프리팹·스케일·오프셋) | **진우** |
| `UI/Panels/BuildPanelController.cs` | 2곳 (빌드 버튼) | **진우** |
| `Sim/DemandMap.cs` | 3곳 | — |
| `Sim/SimEngine.cs` | 2곳 | — |
| Debug 계열 | 여러 곳 | — |

**절반이 진우님 영역입니다.** 그리고 새 `TileType` 추가는
`SimEngine.RebuildSignals()`의 컬링과도 얽혀 있어(준희·환 영역) 사전 협의가 필요합니다.

진행 방식(제가 다 하고 공유 / 뷰·UI는 진우님이 / 카드를 쪼갬)을 정하고 싶습니다.

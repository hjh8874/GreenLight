# 4인 동시 작업 파일 소유권 계약 (2026-07-21)

이 문서는 4명이 같은 코드베이스에서 **동시에** 작업하기 위한 팀 합의입니다.
사람이 읽는 문서이면서, 각자 사용하는 AI에게 그대로 전달할 수 있는 작업 지침으로도 사용합니다.

## 왜 이 문서가 필요한가

원래 분업안(주차+충돌 / 로터리 / 신호 / 건물)을 코드로 검증한 결과, **4명 중 3명이 같은 메서드를 고쳐야 했습니다.**

```text
CarMotion.cs → MoveCarSimVehicle()  (243줄)
  주차   → L677-752  정착 분기
  충돌   → L851-872  헤드웨이/추종
  로터리 → L756-782, L823-849  코리도 클램프
  신호   → L784-807  TargetAdvancing (= 뷰의 신호 대기 감지기)
```

기능 이름으로 자른 분업선이 코드의 이음매와 어긋나기 때문입니다.
그래서 **기능이 아니라 계층으로** 자릅니다.

## 결론 — 소유권

```text
소유자가 아닌 파일은 편집하지 않는다. 필요하면 소유자에게 요청한다.
```

| 담당 | 영역 | 소유 파일 |
|---|---|---|
| **주석** | 뷰 모션 전체 | `View/CarMotion.cs` **전담**<br>`View/MainCityView.cs`의 주차 비주얼 (`RebuildParkingVisuals`, `AddParkingMark`, `DestroyParkingVisuals`, `ApplyCarStyle`)<br>`ViewKit/CarStyle.cs` |
| **환** | 로터리 기하·시뮬 | `ViewKit/RoutePolyline.cs`<br>`ViewKit/PolylineMath.cs`<br>`ViewKit/RingLane.cs`<br>`Sim/RoadQueueNetwork.cs`의 링 서비스 (`ServiceRoundaboutRings`, `ExecuteIntent`의 `RingEntry`)<br>`Sim/SimEngine.cs`의 로터리 배치 (`CanPlaceRoundabout`, `TryPlaceRoundabout`, `TryRemoveRoundabout`, `IsInRoundaboutFootprint`) |
| **준희** | 신호 시스템 | `Sim/SignalMath.cs`<br>`Sim/SignalMap.cs`<br>`Sim/SimEngine.cs`의 신호 구역 (L388-441, `IsSignalGreen`, `GetSignalPhase`, `TryOverrideSignal`)<br>`Contracts/ISignalControl.cs`<br>`View/MainCityView.cs`의 신호 비주얼·입력 (`RefreshSignals`, `CreateSignalVisual`, `ApplySignalState`, `HandleSignalInput`, 신호 public API) |
| **진우** | 건물 시스템 | `Contents/` 전체 (`BuildingDefinitionSO`, `PlacedBuildingRegistry`, `PopulationSystem`, `DeliveredProgressSystem`)<br>`UI/Panels/BuildPanelController.cs`<br>`UI/Controllers/BuildSlotController.cs`<br>`View/MainCityView.cs`의 타일 비주얼 (`RefreshTile`, `CreateTileVisual`, `ApplyTileColor`, `GetTileScale`, `AddFallbackBuildingDetails`, `GetPrefab`, `OnPlaced`) |

**로터리는 환이 담당하되 뷰 렌더링은 주석에게 맡깁니다.** 둘은 아래 계약으로만 대화합니다.

## 경계 계약 — 환 ↔ 주석

로터리 기하는 환이 정하고, 그것을 차가 어떻게 달리는지는 주석이 정합니다.
두 사람이 만나는 유일한 지점은 **`RoutePolyline.BakeInput`** 입니다.

```text
환   → BakeInput의 로터리 필드(OrbitRadius, EntryExitOffsetRad, TransitionLength, IsRoundabout)와
       그 결과로 나오는 폴리라인의 형상·길이를 소유한다.
주석 → 그 폴리라인 위에서 차를 움직이는 것(CarMotion.cs 전체)을 소유한다.
```

환이 `BakeInput`에 필드를 추가하면 주석에게 알립니다. 반대로 주석이 로터리 구간에서 차가
이상하게 달린다고 판단하면, `CarMotion.cs`를 고치기 전에 **환에게 기하 문제인지 먼저 확인합니다.**

## 금지 사항 3가지

이 셋은 어기면 다른 사람 작업이 조용히 깨집니다.

### 1. 통합 씬은 아무도 커밋하지 않는다

`MainCityView`가 붙은 씬이 7개이고, Unity의 `.unity` YAML은 **병합이 되지 않습니다.**
4명이 각자 `[SerializeField]`를 추가하면 씬 7개 × 4명 = 라운드당 최대 28곳 충돌입니다.

각자 자기 씬에서만 튜닝합니다.

| 씬 | 담당 |
|---|---|
| `Assets/00_Scenes/CityFlowIntegrated_cmt.unity` | 주석 |
| `Assets/00_Scenes/Debug/CityFlowIntegrated_hwan.unity` | 환 |
| `Assets/00_Scenes/CityFlowIntegrated_Geon.unity` / `_Geon2.unity` | 김건 |
| `Assets/00_Scenes/CityFlowIntegrated_han.unity` | ⚠️ 담당 확인 필요 |
| `Assets/00_Scenes/CityFlowIntegrated_cmt_Debug.unity` | ⚠️ 담당 확인 필요 |
| `Assets/00_Scenes/EngineSandbox_hwan.unity` | 환 |

> **주의**: 씬별 튜닝값이 이미 어긋나 있습니다. `roundaboutOrbitRadius`가
> `_cmt`=0.68, `Debug/_hwan`=0.5, `_Geon`/`_Geon2`/`_han`/`_cmt_Debug`/`EngineSandbox`=0.3.
> `cornerTurnRadius`도 씬마다 0.6과 0.75가 섞여 있습니다.
> 한 씬에서 맞춘 값이 다른 씬에서 깨져 보이는 건 버그가 아니라 이 상태 때문입니다.

### 2. `MainCityView`에 `[SerializeField]`를 추가하지 않는다

`CarMotion.cs`는 `partial`이라 **자기 직렬화 필드가 하나도 없습니다.** 전부 `MainCityView.cs`
L19-110에 몰려 있고, 주석의 그룹(L50-63)과 환의 그룹(L68-75)은 **5줄 간격**입니다.
파일이 아니라 헝크 단위로 부딪힙니다.

튜닝 노브가 필요하면 이 문서 담당자에게 요청합니다. 한 사람이 몰아서 추가합니다.

### 3. `SimConfig.Default()` 는 담당자 1명만 편집한다

`Sim/SimConfig.cs` L123-173의 객체 초기화자를 4명이 동시에 append하면 반드시 충돌합니다.
게다가 `SimConfig`는 **버전 관리가 없는 struct**이고 `.asset` 3개가 이걸 직렬화합니다.
필드 순서가 바뀌면 한 사람의 필드가 다른 사람의 값을 읽습니다 (L23-24의 기존 경고 참조).

필드 추가는 요청만 하고, 담당자가 `Default()`와 `.asset` 3개를 함께 맞춥니다.

## git이 못 잡는 조용한 파괴

아래는 **충돌도 안 나고 컴파일도 되는데 남의 작업이 깨지는** 경로입니다.
자기 작업이 이유 없이 이상해지면 여기부터 의심하세요.

### 신호 → 충돌 (준희 → 주석)

```text
준희: SignalGateAdapter.IsServiceOpen  (SimEngine)
  → RoadQueueNetwork.CollectIntents    (틱에 목표가 전진하는가)
  → CarMotion: TargetAdvancing         (L801/806)
  → ceilingSpeed (L847) → desired (L848)
  → 주석: Mathf.Min(desired, follow)   (L871)
```

파일 3개를 건너뛰는 인과 사슬인데 **공유 심볼이 없어 git이 전혀 못 봅니다.**
`CarMotion.cs` L805의 주석 "틱이 지났는데 목표가 그대로 = Sim이 나를 잡고 있다(신호·정원)"가
이 연결의 유일한 기록입니다.

### 건물 → 주차 (진우 → 주석)

`CarMotion.GetParkingAnchor()`가 `transform.Find("ParkingSlot_{n}")`으로 **진우의 프리팹 자식을
직접 찾습니다.** 진우가 자식 이름을 바꾸거나 `CreateTileVisual`을 재구성하면, 주차 앵커가 조용히
절차적 폴백으로 떨어져 차가 엉뚱한 곳에 섭니다. 에러 없음.

### 기하 ↔ 차체 크기 (환 ↔ 주석)

`RoutePolyline.cs:316,392`의 하한 `0.66`은 **"섬 스침 방지 실측값"** 입니다.
주석이 `CarStyle.LengthScale`로 차를 늘리거나 환이 `roundaboutOrbitRadius`를 올리면
이 상수가 조용히 무효가 되어 차가 섬을 파고듭니다.
같은 식으로 `vehicleMinHeadway`의 기본값 0.55는 "최대 차 길이 0.437 + 여유"라는 **주석에만**
근거가 있습니다.

### 베이크 해시 게이트 (주석 자신)

`SyncCommutePopulation()`이 두 해시로 재베이크 경로를 가릅니다.

```text
commuteRoutesHash 변경 → RebuildCommute        (전체, RebuildParkingVisuals 포함)
commuteTuningHash 변경 → RebakeCommuteGeometry (기하만, 주차 재생성 없음)
```

주차 노브를 **어느 해시에도 안 넣으면** 노브가 죽은 것처럼 보이고,
**튜닝 해시에 넣으면** 주차 비주얼이 갱신되지 않습니다. 둘 다 에러가 안 납니다.

### 실행 순서 (전원)

`MainCityView.Update()`에서 `RefreshSignals()` → `RefreshRoundabouts()` → ... → `RefreshVehicles()`
순으로 돕니다. 차는 **같은 프레임에 앞서 만들어진** 신호·로터리 상태를 읽습니다.
이 순서 앞에 작업을 끼워 넣으면 주석이 보는 값이 달라집니다.

또한 `CreateRoundaboutVisual()`이 `signalRoot`에 붙습니다(오버패스·일방통행·우선도로도 마찬가지).
준희가 `ClearChildren(signalRoot)`를 부르면 **환의 로터리 비주얼이 함께 사라집니다.**

### 타일 의미 변경 (진우 → 환·준희)

`CityGrid.IsIntersection()`이 바뀌면 `SimEngine.RebuildSignals()`의 컬링이 **로터리와 신호를 함께
지웁니다.** 진우가 새 `TileType`을 추가해 도로 인접 판정이 달라지면 여기에 걸립니다.

## 충돌이 나면

```text
1. 남의 소유 파일에서 충돌 → 고치지 말고 소유자에게 넘긴다.
2. 씬 파일 충돌 → 자기 씬이 아니면 무조건 상대 것을 취한다(--theirs).
3. SimConfig.Default() 충돌 → 담당자가 단독으로 정리한다.
```

## 이 분업의 천장

`CarMotion.cs`는 `partial class`라 **결합도를 줄이지 않았습니다.** 파일 소유권만 갈랐을 뿐,
`MoveCarSimVehicle`이 적분 중간에 Transform·렌더러·브레이크등·코인팝을 직접 건드리는 구조는
그대로입니다.

모션을 심 없이 테스트하려면 `RouteVehicle`(모션 상태와 `GameObject` 핸들이 한 클래스에 섞여 있음)
분해부터 시작하는 별개 작업이 필요합니다. 이번 분업의 목적은 거기까지가 아니라
**오후에 4명이 동시에 커밋할 수 있게 하는 것**입니다.

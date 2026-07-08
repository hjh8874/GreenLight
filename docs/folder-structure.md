# GreenLight Folder Structure Guide

이 문서는 GreenLight 팀의 폴더 사용 규칙입니다.
목표는 새 작업을 만들 때 팀원이 같은 위치와 같은 기준으로 파일을 배치하도록 맞추는 것입니다.

중요한 방향은 하나입니다.

```text
새 프로젝트 루트를 따로 만들지 않는다.
현재 Assets 구조 안에서 역할별 규칙을 맞춘다.
```

`Assets/01_Scripts`는 계속 코드의 중심 폴더로 사용합니다.
기존 파일을 한 번에 옮기지 않고, 앞으로 생기는 기능과 정리 작업부터 이 규칙을 적용합니다.

## 이름 규칙

폴더명에는 사람 이름을 쓰지 않습니다.
팀원 이름, 담당자 이름, 임시 작업자 이름은 폴더 기준이 되면 안 됩니다.

사용하지 않는 예:

```text
MemberName
MemberName_UI
Feature_OwnerName
```

사용하는 예:

```text
UI
Gameplay
Simulation
Presentation
Configs
Debug
Prototype
```

기존에 이미 있는 사람 이름 기반 폴더는 당장 옮기지 않습니다.
대신 새 파일부터 역할 기반 폴더에 만들고, 기존 폴더는 기능 단위로 천천히 정리합니다.

## 현재 기준 구조

```text
Assets
  00_Scenes
  01_Scripts
  Geon_UI
  Settings
  Tests
  TextMesh Pro
```

### `Assets/00_Scenes`

Unity 씬을 둡니다.
씬은 목적별로 분리해서 관리합니다.

권장 하위 구조:

```text
Assets/00_Scenes
  Main
  Debug
  Prototype
```

- `Main`: 실제 빌드에 들어갈 메인 씬
- `Debug`: 시뮬레이션, UI, 배치 등 기능 확인용 씬
- `Prototype`: 실험용 씬

현재 있는 씬은 당장 옮기지 않습니다.
새 씬을 만들 때부터 위 기준을 적용합니다.

### `Assets/01_Scripts`

게임 런타임 코드의 중심 폴더입니다.
코어 시뮬레이션, 게임 규칙, 프레젠테이션, 서비스 연결 코드를 여기에 둡니다.

현재 있는 `CityFlow` 구조는 유지합니다.
앞으로는 `CityFlow` 안에서 역할을 더 명확히 나눕니다.

권장 구조:

```text
Assets/01_Scripts/CityFlow
  Contracts
  Sim
  Bootstrap
  Gameplay
  UI
  View
  Configs
  Debug
  Fakes
```

현재 이미 있는 폴더:

```text
Assets/01_Scripts/CityFlow/Contracts
Assets/01_Scripts/CityFlow/Sim
Assets/01_Scripts/CityFlow/Bootstrap
Assets/01_Scripts/CityFlow/View
Assets/01_Scripts/CityFlow/Debug
Assets/01_Scripts/CityFlow/Fakes
```

앞으로 추가할 폴더:

```text
Assets/01_Scripts/CityFlow/Gameplay
Assets/01_Scripts/CityFlow/UI
Assets/01_Scripts/CityFlow/Configs
```

## `CityFlow` 하위 폴더 규칙

### `Contracts`

시스템 간 약속을 둡니다.
다른 모듈이 함께 참조해야 하는 enum, interface, event, 작은 데이터 구조가 들어갑니다.

예시:

```text
TileType
CongestionLevel
IPlacementService
IReadOnlyTileData
SimEventHub
```

규칙:

- 구현 로직을 넣지 않습니다.
- UI나 특정 씬에만 필요한 코드를 넣지 않습니다.
- 여러 시스템이 공유해야 하는 타입만 둡니다.

### `Sim`

순수 시뮬레이션 로직을 둡니다.
교통 흐름, 도로망, 수요, 신호, 정체, 정산처럼 게임의 계산 중심 코드가 여기에 들어갑니다.

예시:

```text
CityGrid
RoadNetwork
DemandMap
FlowSolver
SignalMath
SimEngine
SimConfig
```

규칙:

- UI를 직접 참조하지 않습니다.
- 씬 오브젝트에 강하게 묶이지 않도록 유지합니다.
- 테스트 가능한 계산 로직을 우선합니다.

### `Bootstrap`

씬 시작 시 시스템을 조립하는 코드를 둡니다.
서비스를 만들고, 필요한 MonoBehaviour에 주입하는 역할입니다.

예시:

```text
CityBootstrap
CityFlowServices
ICityFlowServiceConsumer
```

규칙:

- 시스템 연결만 담당합니다.
- 실제 게임 규칙이나 UI 로직을 길게 넣지 않습니다.

### `Gameplay`

플레이어가 직접 체감하는 게임 규칙을 둡니다.
현재는 폴더가 없지만 앞으로 추가하는 것을 권장합니다.

권장 하위 구조:

```text
Assets/01_Scripts/CityFlow/Gameplay
  Placement
  Economy
  Research
  Progression
  Save
```

- `Placement`: 건설, 철거, 배치 규칙
- `Economy`: 코인, 비용, 보상, 정산 규칙
- `Research`: 연구, 업그레이드, 해금
- `Progression`: 목표, 스테이지, 튜토리얼 진행
- `Save`: 저장, 불러오기, 오프라인 시간 처리

규칙:

- `Sim` 계산 결과를 게임 규칙으로 해석하는 코드는 여기에 둡니다.
- UI 버튼 클릭 처리 자체는 UI 쪽에 두고, 실제 규칙은 Gameplay로 분리합니다.

### `UI`

새 UI 스크립트를 둡니다.
기존 UI 작업 폴더는 현재 작업물로 유지하되, 새 UI 코드는 점차 `Assets/01_Scripts/CityFlow/UI`를 기준으로 만듭니다.

권장 하위 구조:

```text
Assets/01_Scripts/CityFlow/UI
  HUD
  Panels
  Controllers
```

- `HUD`: 상단바, 코인, 시간, 효율, 정체도 표시
- `Panels`: 빌드, 분석, 연구, 설정, 통계 패널
- `Controllers`: 탭 전환, 도크 열기/닫기, UI 입력 흐름

규칙:

- TextMeshPro를 사용합니다.
- UI 텍스트 참조는 가능하면 인스펙터에서 관리합니다.
- UI는 `Sim` 내부 클래스에 직접 의존하지 않고, 서비스나 이벤트를 통해 연결합니다.

### `View`

시뮬레이션이나 게임 상태를 월드에 보여주는 코드를 둡니다.
타일 색, 정체도, 밀도, 신호, 이펙트 표시가 여기에 들어갑니다.

예시:

```text
VehicleDensityView
RoadCongestionView
FlowBurstView
```

규칙:

- 계산 자체보다 표시와 반영을 담당합니다.
- UI 패널 코드는 여기에 두지 않습니다.

### `Configs`

ScriptableObject 설정 타입과 밸런스 관련 코드를 둡니다.
현재는 폴더가 없지만 앞으로 추가하는 것을 권장합니다.

권장 하위 구조:

```text
Assets/01_Scripts/CityFlow/Configs
  Simulation
  Economy
  Buildings
  Research
```

- `Simulation`: 틱 간격, 도로 용량, 신호 주기, 정체 기준
- `Economy`: 보상, 비용, 오프라인 정산 수치
- `Buildings`: 도로, 집, 회사, 학교 같은 건물 데이터
- `Research`: 연구 비용, 효과, 선행 조건

규칙:

- 팀원이 인스펙터에서 조정해야 하는 값은 점차 Configs로 옮깁니다.
- 상수로 고정할 값과 밸런스 값을 구분합니다.

### `Debug`

개발 중 확인용 코드를 둡니다.
디버그 오버레이, 시뮬레이션 튜너, 테스트 맵 시더가 여기에 들어갑니다.

규칙:

- 최종 게임 기능과 섞지 않습니다.
- 디버그 전용이면 이름이나 주석에서 명확히 표시합니다.

### `Fakes`

가짜 서비스나 임시 데이터를 둡니다.
UI나 프레젠테이션을 코어 없이 확인할 때 사용합니다.

규칙:

- 실제 게임 로직과 혼동되지 않도록 `Fake` 이름을 붙입니다.
- 최종 기능으로 승격할 때는 Gameplay, Sim, Services로 옮깁니다.

## 기존 UI 작업 폴더

현재 존재하는 사람 이름 기반 UI 작업 폴더입니다.
이름에 사람 이름이 들어가 있으므로 새 규칙의 기준 폴더로 삼지 않습니다.
당장 이동하면 씬과 프리팹 참조가 깨질 수 있으므로 레거시 폴더로 유지합니다.

현재 구조:

```text
Assets/Geon_UI
  Scripts
    Panels
    Editor
  UI_Scene
```

새 UI 스크립트 위치:

```text
Assets/01_Scripts/CityFlow/UI
```

기존 `Assets/Geon_UI/Scripts`의 스크립트는 나중에 기능 단위로 `Assets/01_Scripts/CityFlow/UI`로 옮깁니다.
기존 `Assets/Geon_UI/UI_Scene`의 씬은 나중에 `Assets/00_Scenes/Prototype` 또는 `Assets/00_Scenes/Debug`로 옮깁니다.

## `Assets/Tests`

테스트 코드를 둡니다.

권장 구조:

```text
Assets/Tests
  EditMode
  PlayMode
  Tilemap_test
```

- `EditMode`: 순수 로직, 시뮬레이션, 서비스 테스트
- `PlayMode`: 씬 실행, MonoBehaviour, 통합 테스트
- `Tilemap_test`: 현재 타일맵 프로토타입 테스트

규칙:

- 실제 런타임 기능 코드는 `Tests`에 새로 만들지 않습니다.
- 프로토타입 코드가 실제 기능으로 승격되면 `01_Scripts`로 옮깁니다.

## 리소스 폴더 규칙

현재 명확한 리소스 루트가 부족하므로, 앞으로 필요할 때 아래 폴더를 추가합니다.

```text
Assets/02_Prefabs
Assets/03_Art
Assets/04_Audio
Assets/05_ScriptableObjects
```

### `Assets/02_Prefabs`

프리팹을 둡니다.

권장 하위 구조:

```text
Assets/02_Prefabs
  UI
  Tiles
  Vehicles
  Effects
```

### `Assets/03_Art`

아트 리소스를 둡니다.

권장 하위 구조:

```text
Assets/03_Art
  Materials
  Sprites
  Models
  VFX
```

### `Assets/04_Audio`

오디오 리소스를 둡니다.

권장 하위 구조:

```text
Assets/04_Audio
  Music
  SFX
```

### `Assets/05_ScriptableObjects`

ScriptableObject 에셋 인스턴스를 둡니다.
스크립트 타입은 `Assets/01_Scripts/CityFlow/Configs`에 두고, 실제 `.asset` 파일은 여기에 둡니다.

권장 하위 구조:

```text
Assets/05_ScriptableObjects
  Simulation
  Economy
  Buildings
  Research
```

## 새 파일 위치 빠른 기준

- 새 enum/interface/event: `Assets/01_Scripts/CityFlow/Contracts`
- 새 시뮬레이션 계산 코드: `Assets/01_Scripts/CityFlow/Sim`
- 새 서비스 조립 코드: `Assets/01_Scripts/CityFlow/Bootstrap`
- 새 건설/철거 규칙: `Assets/01_Scripts/CityFlow/Gameplay/Placement`
- 새 코인/보상 규칙: `Assets/01_Scripts/CityFlow/Gameplay/Economy`
- 새 연구/업그레이드 규칙: `Assets/01_Scripts/CityFlow/Gameplay/Research`
- 새 저장 코드: `Assets/01_Scripts/CityFlow/Gameplay/Save`
- 새 월드 표시 코드: `Assets/01_Scripts/CityFlow/View`
- 새 디버그 도구: `Assets/01_Scripts/CityFlow/Debug`
- 새 가짜 서비스: `Assets/01_Scripts/CityFlow/Fakes`
- 새 HUD/UI 컨트롤러: `Assets/01_Scripts/CityFlow/UI/HUD` 또는 `Assets/01_Scripts/CityFlow/UI/Controllers`
- 새 UI 패널: `Assets/01_Scripts/CityFlow/UI/Panels`
- 새 EditMode 테스트: `Assets/Tests/EditMode`
- 새 PlayMode 테스트: `Assets/Tests/PlayMode`
- 새 프리팹: `Assets/02_Prefabs`
- 새 ScriptableObject 에셋: `Assets/05_ScriptableObjects`

## 의존 방향

기본 의존 방향은 아래처럼 잡습니다.

```text
Contracts
<- Sim
<- Gameplay
<- View
<- UI
```

규칙:

- `Contracts`는 가장 아래 계층입니다.
- `Sim`은 UI를 직접 모릅니다.
- `Gameplay`은 Sim 결과를 게임 규칙으로 해석합니다.
- `View`는 상태를 월드에 보여줍니다.
- `UI`는 서비스, 이벤트, public API를 통해 연결합니다.

## 기존 파일 이동 규칙

기존 파일은 지금 당장 옮기지 않습니다.
Unity 참조가 깨질 수 있기 때문입니다.

옮길 때는 다음 순서로 진행합니다.

1. 이동 대상 기능을 정합니다.
2. 해당 기능이 씬, 프리팹, SerializeField에 연결되어 있는지 확인합니다.
3. Unity Editor에서 이동하거나 `.meta` 파일을 함께 보존합니다.
4. 컴파일 오류를 확인합니다.
5. 관련 씬과 테스트를 확인합니다.
6. 작은 단위로 커밋합니다.

## 팀 합의 규칙

1. `Assets/GreenLight` 같은 새 프로젝트 루트는 만들지 않습니다.
2. 새 코드는 현재 구조 안에서 역할별 폴더에 둡니다.
3. `Assets/01_Scripts/CityFlow`는 코어와 게임 로직 중심으로 유지합니다.
4. 사람 이름 기반 폴더를 새로 만들지 않습니다.
5. `Assets/Geon_UI`는 레거시 폴더로 유지하고, 새 UI 코드는 `Assets/01_Scripts/CityFlow/UI`에 만듭니다.
6. 프로토타입이 실제 기능이 되면 `Tests` 밖으로 옮깁니다.
7. ScriptableObject 타입과 에셋 위치를 구분합니다.
8. 큰 폴더 이동은 기능 단위 브랜치에서 따로 진행합니다.

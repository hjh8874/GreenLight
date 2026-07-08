# GreenLight Folder Structure Guide

이 문서는 GreenLight 프로젝트의 장기 폴더 구조 기준입니다.
목표는 팀원이 새 스크립트, 씬, 프리팹, ScriptableObject, 테스트를 만들 때 위치를 고민하지 않도록 하는 것입니다.

현재 브랜치에서는 기존 파일을 옮기지 않고, 앞으로 사용할 기준 폴더만 먼저 만들었습니다.
Unity 씬과 프리팹 참조가 깨질 수 있으므로 실제 이동은 기능 단위로 천천히 진행합니다.

## 기본 원칙

- 프로젝트 전용 파일은 `Assets/GreenLight` 아래에 둡니다.
- 외부 패키지, Unity 기본 리소스, 플러그인은 이 폴더 안으로 옮기지 않습니다.
- 새 기능은 가능하면 이 구조 안에 생성합니다.
- 기존 파일 이동은 `.meta` 파일을 보존하면서 Unity Editor 안에서 진행하는 것을 우선합니다.
- 사람 이름이나 임시 작업자 이름을 폴더명으로 쓰지 않습니다.
- 폴더명은 역할 기준으로 정합니다.

## 최상위 구조

```text
Assets/GreenLight
  Runtime
  Editor
  Tests
  Scenes
  Prefabs
  Art
  Audio
  STRUCTURE.md
```

### `Runtime`

게임 실행 중 사용하는 코드와 설정을 둡니다.
일반 MonoBehaviour, 시뮬레이션 로직, UI 컨트롤러, ScriptableObject 타입 등이 여기에 들어갑니다.

### `Editor`

Unity Editor에서만 사용하는 도구를 둡니다.
예를 들어 자동 배치 생성기, 커스텀 인스펙터, 에디터 메뉴가 여기에 들어갑니다.

### `Tests`

EditMode, PlayMode 테스트를 둡니다.
실제 게임 런타임용 스크립트는 이 폴더에 두지 않습니다.

### `Scenes`

메인 씬, 디버그 씬, 프로토타입 씬을 용도별로 나눕니다.

### `Prefabs`

UI, 타일, 차량, 이펙트 등 재사용 프리팹을 둡니다.

### `Art`

머티리얼, 스프라이트, 모델, VFX 리소스를 둡니다.

### `Audio`

BGM과 효과음을 둡니다.

## Runtime 구조

```text
Runtime
  Core
  Gameplay
  Presentation
  UI
  Configs
  DebugTools
  Fakes
```

### `Runtime/Core`

게임의 가장 낮은 수준의 핵심 코드를 둡니다.
다른 기능들이 의존해도 되는 안정적인 계층입니다.

```text
Core
  Contracts
  Simulation
  Services
  Bootstrap
```

- `Contracts`: 공용 enum, interface, event, 데이터 구조
- `Simulation`: 교통 흐름, 도로망, 수요, 신호, 정산 등 순수 시뮬레이션
- `Services`: 시스템 간 연결에 쓰는 서비스 컨테이너나 서비스 인터페이스
- `Bootstrap`: 씬 시작 시 서비스와 시스템을 조립하는 코드

추천 의존 방향:

```text
Contracts <- Simulation <- Gameplay <- Presentation <- UI
```

UI가 시뮬레이션 내부 클래스에 직접 접근하지 않도록 합니다.
가능하면 `Contracts`, `Services`, 이벤트를 통해 연결합니다.

### `Runtime/Gameplay`

플레이어가 직접 체감하는 게임 규칙을 둡니다.
시뮬레이션 결과를 게임 규칙으로 해석하거나, 플레이어 입력을 게임 명령으로 바꾸는 영역입니다.

```text
Gameplay
  Placement
  Economy
  Research
  Progression
  Save
```

- `Placement`: 건설, 철거, 배치 검증, 타일 선택 명령
- `Economy`: 코인, 비용, 보상, 정산 공식
- `Research`: 연구, 업그레이드, 해금 조건
- `Progression`: 스테이지 진행, 목표, 튜토리얼 상태
- `Save`: 저장/불러오기, 오프라인 시간 계산

### `Runtime/Presentation`

게임 상태를 월드에 보여주는 시각화 계층입니다.
시뮬레이션이나 게임플레이 데이터를 받아서 타일 색, 차량, 이펙트, 카메라 표현으로 바꿉니다.

```text
Presentation
  Views
  Effects
  Camera
```

- `Views`: 타일 밀도, 정체도, 신호등, 차량 흐름 표시
- `Effects`: Flow Burst, 건설 완료, 보상 연출
- `Camera`: 카메라 이동, 줌, 추적, 시야 제어

### `Runtime/UI`

Canvas 기반 UI와 UI 컨트롤러를 둡니다.
TextMeshPro를 사용하고, 텍스트 참조는 가능하면 인스펙터에서 연결합니다.

```text
UI
  HUD
  Panels
  Controllers
```

- `HUD`: 상단바, 코인, 시간, 효율, 정체도 표시
- `Panels`: 빌드, 분석, 연구, 설정, 통계 패널
- `Controllers`: UI 입력 흐름, 탭 전환, 도크 열기/닫기

### `Runtime/Configs`

ScriptableObject 설정값과 밸런스 데이터를 둡니다.
상수로 박기보다 팀원이 인스펙터에서 조정해야 하는 값은 이쪽으로 보냅니다.

```text
Configs
  Simulation
  Economy
  Buildings
  Research
```

- `Simulation`: 틱 간격, 도로 용량, 신호 주기, 정체 기준
- `Economy`: 코인 보상, 비용, 오프라인 정산
- `Buildings`: 도로, 집, 회사, 학교 등 건물 데이터
- `Research`: 연구 비용, 효과, 선행 조건

### `Runtime/DebugTools`

개발 중 확인을 위한 도구를 둡니다.
최종 게임 기능과 섞이지 않게 별도 폴더로 유지합니다.

```text
DebugTools
  Runtime
  Editor
```

- `Runtime`: 게임 실행 중 보이는 디버그 오버레이, 튜너, 시더
- `Editor`: 디버그 씬 생성, 테스트 맵 생성, 보정 도구

### `Runtime/Fakes`

UI나 프레젠테이션을 코어 없이 테스트하기 위한 가짜 서비스를 둡니다.
실제 게임 로직과 혼동하지 않도록 이름에 `Fake`를 붙입니다.

## Scenes 구조

```text
Scenes
  Main
  Debug
  Prototype
```

- `Main`: 실제 빌드에 들어갈 메인 씬
- `Debug`: 시뮬레이션, UI, 배치 등 기능 검증용 씬
- `Prototype`: 버려질 수 있는 실험용 씬

빌드 세팅에는 기본적으로 `Scenes/Main`의 씬만 넣는 것을 권장합니다.

## Prefabs 구조

```text
Prefabs
  UI
  Tiles
  Vehicles
  Effects
```

- `UI`: 패널, 버튼 묶음, HUD 프리팹
- `Tiles`: 타일, 도로, 건물 프리팹
- `Vehicles`: 차량 프리팹
- `Effects`: 이펙트 프리팹

## Art 구조

```text
Art
  Materials
  Sprites
  Models
  VFX
```

아트 리소스는 타입별로 분리합니다.
특정 기능에 강하게 묶인 리소스가 많아지면 하위 폴더를 추가할 수 있습니다.

## Audio 구조

```text
Audio
  Music
  SFX
```

- `Music`: 배경음
- `SFX`: 버튼, 건설, 보상, 차량, 경고음

## 현재 폴더에서 옮길 때의 계획

현재 프로젝트는 이미 다음 구조를 가지고 있습니다.

```text
Assets/01_Scripts/CityFlow
Assets/Geon_UI
Assets/Tests
```

바로 모두 옮기지 말고, 기능이 안정된 순서대로 옮깁니다.

```text
Assets/01_Scripts/CityFlow/Contracts
-> Assets/GreenLight/Runtime/Core/Contracts
```

```text
Assets/01_Scripts/CityFlow/Sim
-> Assets/GreenLight/Runtime/Core/Simulation
```

```text
Assets/01_Scripts/CityFlow/Bootstrap
-> Assets/GreenLight/Runtime/Core/Bootstrap
```

```text
Assets/01_Scripts/CityFlow/View
-> Assets/GreenLight/Runtime/Presentation/Views
```

```text
Assets/01_Scripts/CityFlow/Debug
-> Assets/GreenLight/Runtime/DebugTools/Runtime
```

```text
Assets/01_Scripts/CityFlow/Fakes
-> Assets/GreenLight/Runtime/Fakes
```

```text
Assets/Geon_UI/Scripts
-> Assets/GreenLight/Runtime/UI
```

```text
Assets/Tests/EditMode
-> Assets/GreenLight/Tests/EditMode
```

```text
Assets/Tests/Tilemap_test
-> Assets/GreenLight/Scenes/Prototype 또는 Assets/GreenLight/Runtime/Prototype
```

## 새 파일을 만들 때의 빠른 기준

- 새 enum/interface/event: `Runtime/Core/Contracts`
- 새 시뮬레이션 계산 코드: `Runtime/Core/Simulation`
- 새 배치/건설 규칙: `Runtime/Gameplay/Placement`
- 새 코인/보상 규칙: `Runtime/Gameplay/Economy`
- 새 연구/업그레이드 규칙: `Runtime/Gameplay/Research`
- 새 저장 코드: `Runtime/Gameplay/Save`
- 새 타일/차량 표시 코드: `Runtime/Presentation/Views`
- 새 이펙트 코드: `Runtime/Presentation/Effects`
- 새 카메라 코드: `Runtime/Presentation/Camera`
- 새 HUD 코드: `Runtime/UI/HUD`
- 새 패널 코드: `Runtime/UI/Panels`
- 새 ScriptableObject 설정: `Runtime/Configs`
- 새 에디터 도구: `Editor` 또는 `Runtime/DebugTools/Editor`
- 새 테스트: `Tests/EditMode` 또는 `Tests/PlayMode`

## 주의 사항

- 씬에 연결된 MonoBehaviour 파일을 파일 탐색기로 직접 이동하지 않습니다.
- 이동할 때는 Unity Editor에서 이동하거나 `.meta` 파일을 함께 보존합니다.
- SerializeField 필드명 변경은 신중하게 합니다.
- public API, 인스펙터 참조, 프리팹 참조를 깨지 않도록 기능 단위로 이동합니다.
- 대규모 이동 전에는 반드시 팀원에게 공유하고 브랜치에서 테스트합니다.

## 당장 적용할 팀 규칙

1. 새 작업은 가능하면 `Assets/GreenLight` 아래에 생성합니다.
2. 기존 코드는 참조가 안정된 뒤 기능 단위로 이동합니다.
3. UI는 코어 내부 구현에 직접 의존하지 않습니다.
4. 밸런스 값은 점차 ScriptableObject로 옮깁니다.
5. 디버그/프로토타입 코드는 실제 런타임 코드와 섞지 않습니다.

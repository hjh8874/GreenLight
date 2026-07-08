# Save & Load Naming Contract

이 문서는 GreenLight 세이브&로드 시스템을 구현하기 전에 팀이 맞춰야 할 이름, 책임, 참조 방향 규약입니다.
사람이 읽는 합의 문서이면서, 각자 사용하는 AI에게 그대로 전달할 수 있는 작업 지침으로도 사용합니다.

## 목적

세이브&로드 시스템은 여러 시스템의 상태를 모으는 기능입니다.
이름과 책임을 먼저 맞추지 않으면 구현자가 달라질 때 DTO, 서비스, API가 서로 어긋납니다.

따라서 이 문서의 목적은 아래 세 가지입니다.

```text
1. 저장 데이터 타입 이름을 미리 통일한다.
2. 각 시스템이 제공해야 할 snapshot API 이름을 통일한다.
3. SaveService가 참조해도 되는 것과 참조하면 안 되는 것을 분리한다.
```

## 핵심 합의 요약

```text
전체 저장 DTO는 GameSaveData.
시뮬레이션 저장 DTO는 SimSaveData.
타일 저장 단위는 TileSaveData.
신호 저장 단위는 SignalSaveData.
세이브 흐름은 SaveService.
파일 입출력은 JsonSaveRepository.
시간 제공은 ISaveClock.
코인은 EconomyService가 소유한다.
UI는 저장 상태의 주인이 아니다.
SimEngine은 CreateSnapshot/RestoreSnapshot으로 저장 데이터를 주고받는다.
```

## 폴더 위치

세이브&로드 관련 코드는 아래 위치를 기준으로 합니다.

```text
Assets/01_Scripts/CityFlow/Gameplay/Save
```

권장 하위 구조:

```text
Assets/01_Scripts/CityFlow/Gameplay/Save
  Data
  Repositories
  Clocks
```

파일 배치 예시:

```text
Assets/01_Scripts/CityFlow/Gameplay/Save/Data/GameSaveData.cs
Assets/01_Scripts/CityFlow/Gameplay/Save/Data/SimSaveData.cs
Assets/01_Scripts/CityFlow/Gameplay/Save/Data/TileSaveData.cs
Assets/01_Scripts/CityFlow/Gameplay/Save/Data/SignalSaveData.cs
Assets/01_Scripts/CityFlow/Gameplay/Save/Data/EconomySaveData.cs
Assets/01_Scripts/CityFlow/Gameplay/Save/Data/ResearchSaveData.cs
Assets/01_Scripts/CityFlow/Gameplay/Save/Data/ProgressionSaveData.cs
Assets/01_Scripts/CityFlow/Gameplay/Save/SaveService.cs
Assets/01_Scripts/CityFlow/Gameplay/Save/SaveConstants.cs
Assets/01_Scripts/CityFlow/Gameplay/Save/Repositories/JsonSaveRepository.cs
Assets/01_Scripts/CityFlow/Gameplay/Save/Clocks/ISaveClock.cs
Assets/01_Scripts/CityFlow/Gameplay/Save/Clocks/SystemSaveClock.cs
```

테스트 위치:

```text
Assets/Tests/EditMode
```

## 명명 규칙

DTO는 저장 파일에 들어가는 순수 데이터 타입입니다.
Unity `JsonUtility` 사용 가능성을 고려해 DTO는 `public field` 기반으로 작성합니다.

DTO 필드명은 `PascalCase`를 사용합니다.

예:

```csharp
[Serializable]
public sealed class GameSaveData
{
    public int SaveVersion;
    public long SavedAtUtcTicks;
    public int GridWidth;
    public int GridHeight;
}
```

규칙:

```text
DTO에는 UnityEngine.Object 참조를 넣지 않는다.
DTO에는 GameObject, Component, ScriptableObject 직접 참조를 넣지 않는다.
Vector2Int는 직접 저장하지 않고 X, Y로 풀어서 저장한다.
계산 결과보다 원인 데이터를 저장한다.
```

## 전체 저장 DTO

이름:

```csharp
GameSaveData
```

역할:

```text
세이브 파일 하나 전체를 대표하는 루트 DTO.
SaveService와 JsonSaveRepository가 주로 다룬다.
```

필수 필드:

```csharp
public int SaveVersion;
public long SavedAtUtcTicks;
public int GridWidth;
public int GridHeight;
public SimSaveData Simulation;
public EconomySaveData Economy;
public ResearchSaveData Research;
public ProgressionSaveData Progression;
```

초기 구현에서 아직 없는 시스템은 `null` 또는 기본값을 허용합니다.
단, 필드 이름은 미리 고정합니다.

## 시뮬레이션 저장 DTO

이름:

```csharp
SimSaveData
```

역할:

```text
SimEngine이 소유한 저장 가능 상태를 담는다.
```

필드:

```csharp
public TileSaveData[] PlacedTiles;
public SignalSaveData[] SignalOffsets;
```

규칙:

```text
FlowSolver 내부 캐시를 저장하지 않는다.
정체도, 밀도, 계산된 경로를 저장하지 않는다.
로드 후 시뮬레이션이 다시 계산하게 둔다.
```

## 타일 저장 DTO

이름:

```csharp
TileSaveData
```

필드:

```csharp
public int X;
public int Y;
public TileType Type;
```

규칙:

```text
Empty 타일은 저장하지 않는다.
배치된 타일만 저장한다.
로드 시 기존 맵을 비우고 PlacedTiles를 다시 배치한다.
```

## 신호 저장 DTO

이름:

```csharp
SignalSaveData
```

필드:

```csharp
public int X;
public int Y;
public int OffsetSlots;
```

규칙:

```text
신호 객체 전체를 저장하지 않는다.
플레이어나 시스템이 조정한 OffsetSlots만 저장한다.
신호 타일 목록은 로드 후 맵 배치 기반으로 다시 생성한다.
```

## 경제 저장 DTO

이름:

```csharp
EconomySaveData
```

필드:

```csharp
public long Coins;
```

규칙:

```text
코인의 주인은 EconomyService다.
HUDDashboard의 코인 값은 표시용 캐시로만 취급한다.
UI는 저장 상태의 주인이 아니다.
```

현재 프로젝트에서 코인은 UI에 누적되는 형태가 있으므로, 완성형 저장 전에 `EconomyService`를 만들거나 같은 책임을 가진 시스템을 정해야 합니다.

## 연구 저장 DTO

이름:

```csharp
ResearchSaveData
```

필드 예시:

```csharp
public string[] UnlockedResearchIds;
public string[] PurchasedUpgradeIds;
```

규칙:

```text
연구 저장 키는 string id 기반을 우선 검토한다.
ScriptableObject id와 저장 id를 일치시킨다.
UI 버튼 이름이나 표시 텍스트를 저장 키로 쓰지 않는다.
```

## 진행 저장 DTO

이름:

```csharp
ProgressionSaveData
```

필드 예시:

```csharp
public int CurrentStage;
public string[] CompletedObjectiveIds;
public bool TutorialCompleted;
```

규칙:

```text
튜토리얼, 목표, 스테이지 같은 진행 상태만 저장한다.
UI 탭 열림 상태처럼 일시적인 화면 상태는 저장하지 않는다.
```

## 서비스 이름

세이브 흐름 담당:

```csharp
SaveService
```

역할:

```text
각 시스템에서 snapshot을 모은다.
GameSaveData를 만든다.
JsonSaveRepository에 저장을 요청한다.
로드한 GameSaveData를 각 시스템에 복원한다.
오프라인 정산 흐름을 호출한다.
```

파일 입출력 담당:

```csharp
JsonSaveRepository
```

역할:

```text
Application.persistentDataPath에서 JSON 파일을 읽고 쓴다.
세이브 데이터의 의미를 해석하지 않는다.
SimEngine, EconomyService, UI를 참조하지 않는다.
```

시간 제공:

```csharp
ISaveClock
SystemSaveClock
FakeSaveClock
```

역할:

```text
ISaveClock은 현재 UTC 시간을 제공한다.
SystemSaveClock은 실제 DateTime.UtcNow를 사용한다.
FakeSaveClock은 테스트에서 원하는 시간을 반환한다.
```

## 상수 이름

이름:

```csharp
SaveConstants
```

필드:

```csharp
public const int CurrentSaveVersion = 1;
public const string SaveFileName = "save_v1.json";
public const string BackupSaveFileName = "save_v1_backup.json";
```

규칙:

```text
파일명 문자열을 여러 클래스에 흩뿌리지 않는다.
세이브 버전은 GameSaveData.SaveVersion에 기록한다.
```

## 저장 참여 인터페이스

기본 제안:

```csharp
public interface ISaveParticipant<TSnapshot>
{
    TSnapshot CreateSnapshot();
    void RestoreSnapshot(TSnapshot snapshot);
}
```

제네릭이 부담되면 시스템별 명시 인터페이스를 사용할 수 있습니다.

예:

```csharp
IMapSaveSource
IEconomySaveSource
IResearchSaveSource
IProgressionSaveSource
```

규칙:

```text
SaveService가 내부 필드를 직접 읽지 않는다.
각 시스템이 자기 저장 데이터를 직접 내보낸다.
각 시스템이 자기 저장 데이터를 직접 복원한다.
```

## SimEngine API 계약

권장 API:

```csharp
public SimSaveData CreateSnapshot();
public void RestoreSnapshot(SimSaveData snapshot);
```

대안 API:

```csharp
public IReadOnlyList<TileSaveData> CreateTileSnapshot();
public void RestoreTileSnapshot(IEnumerable<TileSaveData> tiles);
public IReadOnlyList<SignalSaveData> CreateSignalSnapshot();
public void RestoreSignalSnapshot(IEnumerable<SignalSaveData> signals);
```

추천은 `SimSaveData`로 묶는 방식입니다.

SimEngine 복원 규칙:

```text
RestoreSnapshot은 기존 배치 상태를 비운 뒤 PlacedTiles를 복원한다.
타일 복원 후 RoadNetwork, DemandMap, SignalMap이 재계산되도록 한다.
SignalOffsets는 신호 맵 재생성 후 적용한다.
로드 후 정체도와 밀도는 저장값이 아니라 재계산 결과를 사용한다.
```

## SaveService API 계약

1차 권장 API:

```csharp
public bool HasSave();
public bool TryLoad(out GameSaveData data);
public void Save(GameSaveData data);
public void DeleteSave();
```

게임 흐름까지 담당하는 상위 API를 만들 경우:

```csharp
public void SaveNow();
public bool LoadOrCreateNew();
```

규칙:

```text
SaveNow는 현재 시스템 상태로 GameSaveData를 생성하고 저장한다.
LoadOrCreateNew는 저장 파일이 없으면 새 게임 상태를 유지한다.
TryLoad는 실패 이유를 로그로 남긴다.
```

## 이벤트 이름

저장 완료:

```csharp
SaveCompleted
```

로드 완료:

```csharp
LoadCompleted
```

로드 실패:

```csharp
LoadFailed
```

오프라인 정산 완료는 기존 이벤트 사용:

```csharp
SettlementComputed
```

## 참조 방향

허용:

```text
SaveService -> SimEngine snapshot API
SaveService -> EconomyService snapshot API
SaveService -> ResearchService snapshot API
SaveService -> ProgressionService snapshot API
SaveService -> JsonSaveRepository
SaveService -> ISaveClock
JsonSaveRepository -> GameSaveData
```

금지:

```text
SaveService -> HUDDashboard
SaveService -> FlowSolver 내부 배열
SaveService -> CityGrid 내부 배열 직접 접근
JsonSaveRepository -> SimEngine
JsonSaveRepository -> EconomyService
JsonSaveRepository -> UI
UI -> save_v1.json 직접 읽기
UI -> JsonSaveRepository 직접 호출
```

규칙:

```text
UI는 SaveService에 명령만 요청할 수 있다.
파일 입출력 계층은 게임 시스템을 모른다.
저장 시스템은 계산 결과보다 복원 가능한 원인 데이터를 다룬다.
```

## 시간 계약

저장 시간 필드:

```csharp
public long SavedAtUtcTicks;
```

저장 시:

```text
SavedAtUtcTicks = ISaveClock.UtcNow.Ticks
```

로드 시:

```text
elapsedSeconds = (ISaveClock.UtcNow.Ticks - SavedAtUtcTicks) / TimeSpan.TicksPerSecond
```

규칙:

```text
음수 시간은 0으로 처리한다.
오프라인 정산 상한은 SimConfig.OfflineCapHours를 따른다.
오프라인 정산은 SimEngine.SettleOffline(elapsedSeconds)를 통해 수행한다.
```

## 파일 위치 계약

저장 파일은 Unity의 영구 데이터 경로에 둡니다.

```text
Application.persistentDataPath/save_v1.json
Application.persistentDataPath/save_v1_backup.json
```

규칙:

```text
Assets 폴더 안에 런타임 세이브 파일을 쓰지 않는다.
저장 전 기존 save_v1.json을 backup으로 복사하거나 이동한다.
로드 실패 시 backup 로드를 시도할 수 있다.
```

## 저장 대상과 비저장 대상

저장 대상:

```text
GameSaveData.SaveVersion
GameSaveData.SavedAtUtcTicks
GameSaveData.GridWidth
GameSaveData.GridHeight
SimSaveData.PlacedTiles
SimSaveData.SignalOffsets
EconomySaveData.Coins
ResearchSaveData
ProgressionSaveData
```

비저장 대상:

```text
CongestionLevel 계산 결과
Density 계산 결과
ActiveRoutes
FlowSolver 내부 캐시
RoadNetwork 내부 캐시
DemandMap 내부 배정 캐시
차량 임시 위치
UI 표시용 mock 값
디버그 오버레이 상태
현재 열려 있는 UI 탭
```

## 로드 순서 계약

권장 로드 순서:

```text
1. JsonSaveRepository가 GameSaveData를 읽는다.
2. SaveVersion을 확인한다.
3. Config와 서비스가 생성되어 있는지 확인한다.
4. GridWidth, GridHeight를 기준으로 SimEngine을 준비한다.
5. SimEngine.RestoreSnapshot으로 타일 배치와 신호 오프셋을 복원한다.
6. EconomyService가 EconomySaveData를 복원한다.
7. ResearchService가 ResearchSaveData를 복원한다.
8. ProgressionService가 ProgressionSaveData를 복원한다.
9. SavedAtUtcTicks 기준 경과 시간을 계산한다.
10. SimEngine.SettleOffline(elapsedSeconds)를 호출한다.
11. UI 갱신 이벤트를 발행하거나 기존 이벤트 흐름을 통해 표시를 갱신한다.
```

## 1차 구현 범위

프로젝트 현재 상태 기준으로 1차 구현 범위는 아래로 제한합니다.

```text
맵 크기 저장
타일 배치 저장
신호 오프셋 저장
마지막 저장 UTC 시간 저장
로드 후 오프라인 정산 호출
```

1차에서 제외:

```text
연구 저장
진행도 저장
차량 개별 위치 저장
UI 상태 저장
복잡한 버전 마이그레이션
클라우드 저장
암호화
압축
```

## AI에게 전달할 지침

세이브&로드 관련 코드를 생성하거나 수정하는 AI는 이 규칙을 따라야 합니다.

```text
1. 새 저장 DTO 이름은 이 문서의 이름을 사용한다.
2. SaveService가 UI를 직접 참조하지 않게 한다.
3. 파일 입출력은 JsonSaveRepository로 분리한다.
4. 시간은 ISaveClock을 통해 받는다.
5. SimEngine에는 CreateSnapshot/RestoreSnapshot 형태의 API를 우선 제안한다.
6. Vector2Int는 저장 DTO에 직접 넣지 않고 X, Y로 분리한다.
7. 저장 파일은 Application.persistentDataPath 아래에 둔다.
8. 계산 결과를 저장하지 않고 로드 후 재계산하도록 설계한다.
9. 기존 SerializeField, 씬 참조, public API를 임의로 깨지 않는다.
10. 1차 구현 범위를 넘는 기능은 별도 제안으로 분리한다.
```

## 팀 결정 필요 항목

아래 항목은 구현 전에 팀 합의가 필요합니다.

```text
1. EconomyService를 언제 만들지
2. SimEngine snapshot API를 어느 형태로 만들지
3. JsonUtility를 쓸지 Newtonsoft.Json을 쓸지
4. SaveService를 MonoBehaviour로 둘지 순수 C# 서비스로 둘지
5. 저장 트리거를 어디까지 1차에 넣을지
6. backup 파일을 1차부터 쓸지
```

권장 기본값:

```text
EconomyService는 세이브 2차 전에 만든다.
SimEngine API는 SimSaveData CreateSnapshot/RestoreSnapshot으로 시작한다.
직렬화는 1차에서 JsonUtility로 시작한다.
SaveService는 순수 C# 서비스로 시작하고, Unity 생명주기 연결은 MonoBehaviour 어댑터가 담당한다.
저장 트리거는 OnApplicationPause(true), OnApplicationQuit, 배치/철거 성공 후로 시작한다.
backup 파일은 1차부터 둔다.
```

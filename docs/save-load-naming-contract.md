# Save & Load Typed Interface Contract

이 문서는 GreenLight 세이브&로드 시스템을 **타입별 인터페이스 방식**으로 설계하기 위한 팀 합의 제안서입니다.
사람이 읽는 문서이면서, 각자 사용하는 AI에게 그대로 전달할 수 있는 구현 지침으로도 사용합니다.

## 결론

세이브 시스템은 각 기능의 내부 구조를 직접 알지 않습니다.
대신 각 기능은 자기 저장 데이터를 만들고 복원하는 인터페이스를 제공합니다.

```text
각 기능 담당자 = 자기 기능의 저장 DTO와 저장 인터페이스 구현
SaveService = 각 인터페이스를 통해 snapshot을 모아 파일로 저장
JsonSaveRepository = 파일 읽기/쓰기만 담당
UI = 저장 상태의 주인이 아님
```

## 이 방식을 선택하는 이유

현재 프로젝트는 시뮬레이션 코어는 있지만, 경제, 연구, 진행도 같은 게임 시스템은 아직 확정되지 않았습니다.
따라서 하나의 공통 `object` 기반 저장 인터페이스보다, 시스템별로 명확한 타입을 가진 인터페이스가 더 안전합니다.

장점:

```text
컴파일 단계에서 타입 오류를 잡을 수 있다.
각 기능 담당자가 자기 저장 책임을 명확히 가진다.
SaveService가 각 시스템 내부 필드를 직접 알 필요가 없다.
Unity JsonUtility와 DTO 구조를 맞추기 쉽다.
세이브 파일 구조를 사람이 읽고 디버깅하기 쉽다.
```

## 기본 구조

```text
GameSaveData
  SaveVersion
  SavedAtUtcTicks
  GridWidth
  GridHeight
  Simulation
  Economy
  Research
  Progression
```

각 필드는 시스템별 저장 DTO를 가집니다.

```text
Simulation  -> SimSaveData
Economy     -> EconomySaveData
Research    -> ResearchSaveData
Progression -> ProgressionSaveData
```

SaveService는 구체 클래스가 아니라 타입별 저장 인터페이스를 참조합니다.

```text
ISimSaveSource
IEconomySaveSource
IResearchSaveSource
IProgressionSaveSource
```

## 권장 폴더 위치

세이브 서비스와 DTO:

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

공용 인터페이스:

```text
Assets/01_Scripts/CityFlow/Contracts/Save
```

이유:

```text
저장 인터페이스는 여러 시스템이 함께 참조한다.
Contracts는 시스템 간 약속을 두는 위치다.
구현체는 각 시스템 또는 Gameplay/Save 쪽에 둔다.
```

테스트:

```text
Assets/Tests/EditMode
```

## 전체 저장 DTO

이름:

```csharp
GameSaveData
```

역할:

```text
세이브 파일 하나 전체를 대표하는 루트 DTO.
SaveService가 생성하고 JsonSaveRepository가 파일로 저장한다.
```

필드:

```csharp
[Serializable]
public sealed class GameSaveData
{
    public int SaveVersion;
    public long SavedAtUtcTicks;
    public int GridWidth;
    public int GridHeight;
    public SimSaveData Simulation;
    public EconomySaveData Economy;
    public ResearchSaveData Research;
    public ProgressionSaveData Progression;
}
```

규칙:

```text
DTO는 public field 기반으로 둔다.
UnityEngine.Object 참조를 넣지 않는다.
GameObject, Component, ScriptableObject 직접 참조를 넣지 않는다.
아직 없는 시스템의 DTO는 null 또는 기본값을 허용한다.
```

## 시뮬레이션 저장 DTO

이름:

```csharp
SimSaveData
```

필드:

```csharp
[Serializable]
public sealed class SimSaveData
{
    public TileSaveData[] PlacedTiles;
    public SignalSaveData[] SignalOffsets;
}
```

타일 단위:

```csharp
[Serializable]
public sealed class TileSaveData
{
    public int X;
    public int Y;
    public TileType Type;
}
```

신호 단위:

```csharp
[Serializable]
public sealed class SignalSaveData
{
    public int X;
    public int Y;
    public int OffsetSlots;
}
```

규칙:

```text
Empty 타일은 저장하지 않는다.
배치된 타일만 저장한다.
Vector2Int는 저장 DTO에 직접 넣지 않고 X, Y로 풀어 저장한다.
신호 객체 전체를 저장하지 않고 OffsetSlots만 저장한다.
정체도, 밀도, 경로, FlowSolver 캐시는 저장하지 않는다.
로드 후 다시 계산한다.
```

## 경제 저장 DTO

이름:

```csharp
EconomySaveData
```

필드:

```csharp
[Serializable]
public sealed class EconomySaveData
{
    public long Coins;
}
```

규칙:

```text
코인의 주인은 EconomyService다.
HUDDashboard의 코인 값은 표시용 캐시로만 취급한다.
UI는 저장 상태의 주인이 아니다.
```

현재 프로젝트에서는 코인이 UI에 누적되는 형태가 있으므로, 완성형 저장 전에는 `EconomyService` 또는 같은 책임을 가진 시스템을 정해야 합니다.

## 연구 저장 DTO

이름:

```csharp
ResearchSaveData
```

필드 예시:

```csharp
[Serializable]
public sealed class ResearchSaveData
{
    public string[] UnlockedResearchIds;
    public string[] PurchasedUpgradeIds;
}
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
[Serializable]
public sealed class ProgressionSaveData
{
    public int CurrentStage;
    public string[] CompletedObjectiveIds;
    public bool TutorialCompleted;
}
```

규칙:

```text
튜토리얼, 목표, 스테이지 같은 진행 상태만 저장한다.
현재 열려 있는 UI 탭 같은 일시적인 화면 상태는 저장하지 않는다.
```

## 타입별 저장 인터페이스

### 시뮬레이션

```csharp
public interface ISimSaveSource
{
    SimSaveData CreateSnapshot();
    void RestoreSnapshot(SimSaveData snapshot);
}
```

구현 후보:

```text
SimEngine
```

역할:

```text
타일 배치와 신호 오프셋을 snapshot으로 제공한다.
로드 시 타일 배치와 신호 오프셋을 복원한다.
```

### 경제

```csharp
public interface IEconomySaveSource
{
    EconomySaveData CreateSnapshot();
    void RestoreSnapshot(EconomySaveData snapshot);
}
```

구현 후보:

```text
EconomyService
```

역할:

```text
보유 코인과 경제 진행 상태를 snapshot으로 제공한다.
로드 시 경제 상태를 복원한다.
```

### 연구

```csharp
public interface IResearchSaveSource
{
    ResearchSaveData CreateSnapshot();
    void RestoreSnapshot(ResearchSaveData snapshot);
}
```

구현 후보:

```text
ResearchService
```

역할:

```text
해금된 연구와 구매한 업그레이드를 snapshot으로 제공한다.
로드 시 연구 상태를 복원한다.
```

### 진행도

```csharp
public interface IProgressionSaveSource
{
    ProgressionSaveData CreateSnapshot();
    void RestoreSnapshot(ProgressionSaveData snapshot);
}
```

구현 후보:

```text
ProgressionService
```

역할:

```text
스테이지, 목표, 튜토리얼 상태를 snapshot으로 제공한다.
로드 시 진행 상태를 복원한다.
```

## SaveService 역할

이름:

```csharp
SaveService
```

역할:

```text
저장 가능한 시스템들의 인터페이스를 받는다.
각 시스템에서 snapshot을 수집한다.
GameSaveData를 만든다.
JsonSaveRepository에 파일 저장을 요청한다.
로드한 GameSaveData를 각 시스템에 복원한다.
오프라인 정산 흐름을 호출한다.
```

예시 구조:

```csharp
public sealed class SaveService
{
    private readonly ISimSaveSource simSaveSource;
    private readonly IEconomySaveSource economySaveSource;
    private readonly IResearchSaveSource researchSaveSource;
    private readonly IProgressionSaveSource progressionSaveSource;
    private readonly JsonSaveRepository repository;
    private readonly ISaveClock clock;

    public GameSaveData CreateSaveData()
    {
        return new GameSaveData
        {
            SaveVersion = SaveConstants.CurrentSaveVersion,
            SavedAtUtcTicks = clock.UtcNow.Ticks,
            Simulation = simSaveSource.CreateSnapshot(),
            Economy = economySaveSource?.CreateSnapshot(),
            Research = researchSaveSource?.CreateSnapshot(),
            Progression = progressionSaveSource?.CreateSnapshot()
        };
    }
}
```

규칙:

```text
SaveService는 SimEngine, EconomyService 등의 구체 클래스가 아니라 인터페이스를 받는다.
아직 없는 시스템은 null 허용 또는 Null Object 구현을 사용할 수 있다.
SaveService가 HUDDashboard를 참조하지 않는다.
SaveService가 FlowSolver, CityGrid 내부 필드에 직접 접근하지 않는다.
```

## 파일 저장 담당

이름:

```csharp
JsonSaveRepository
```

역할:

```text
GameSaveData를 JSON으로 저장한다.
JSON 파일을 읽어 GameSaveData로 복원한다.
저장 파일 경로를 관리한다.
백업 파일을 관리한다.
게임 시스템의 의미를 해석하지 않는다.
```

권장 API:

```csharp
public bool HasSave();
public bool TryLoad(out GameSaveData data);
public void Save(GameSaveData data);
public void DeleteSave();
```

금지:

```text
JsonSaveRepository가 SimEngine을 참조하지 않는다.
JsonSaveRepository가 EconomyService를 참조하지 않는다.
JsonSaveRepository가 UI를 참조하지 않는다.
```

## 시간 담당

인터페이스:

```csharp
public interface ISaveClock
{
    DateTime UtcNow { get; }
}
```

구현:

```csharp
public sealed class SystemSaveClock : ISaveClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
```

테스트용:

```csharp
public sealed class FakeSaveClock : ISaveClock
{
    public DateTime UtcNow { get; set; }
}
```

규칙:

```text
SaveService 내부에서 DateTime.UtcNow를 직접 호출하지 않는다.
항상 ISaveClock을 통해 시간을 받는다.
오프라인 정산 테스트를 쉽게 만들기 위해 시간 제공자를 분리한다.
```

## 상수

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
세이브 버전과 파일명을 여러 클래스에 흩뿌리지 않는다.
GameSaveData.SaveVersion에는 SaveConstants.CurrentSaveVersion을 기록한다.
```

## 저장 파일 위치

저장 파일은 Unity 런타임 저장 경로에 둡니다.

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

## 참조 방향

허용:

```text
SaveService -> ISimSaveSource
SaveService -> IEconomySaveSource
SaveService -> IResearchSaveSource
SaveService -> IProgressionSaveSource
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

핵심 규칙:

```text
UI는 SaveService에 저장/로드 명령만 요청할 수 있다.
파일 입출력 계층은 게임 시스템을 모른다.
각 시스템은 자기 snapshot 생성과 복원만 책임진다.
```

## 로드 순서

권장 순서:

```text
1. JsonSaveRepository가 GameSaveData를 읽는다.
2. SaveVersion을 확인한다.
3. Config와 서비스가 생성되어 있는지 확인한다.
4. GridWidth, GridHeight 기준으로 SimEngine을 준비한다.
5. ISimSaveSource.RestoreSnapshot(data.Simulation)을 호출한다.
6. IEconomySaveSource.RestoreSnapshot(data.Economy)을 호출한다.
7. IResearchSaveSource.RestoreSnapshot(data.Research)을 호출한다.
8. IProgressionSaveSource.RestoreSnapshot(data.Progression)을 호출한다.
9. SavedAtUtcTicks 기준 경과 시간을 계산한다.
10. SimEngine.SettleOffline(elapsedSeconds)를 호출한다.
11. UI 갱신 이벤트를 발행하거나 기존 이벤트 흐름으로 표시를 갱신한다.
```

주의:

```text
시뮬레이션 복원은 경제/연구/진행도보다 먼저 한다.
오프라인 정산은 기본 상태 복원이 끝난 뒤 수행한다.
UI 갱신은 마지막에 한다.
```

## 1차 구현 범위

현재 프로젝트 기준 1차 범위:

```text
GameSaveData
SimSaveData
TileSaveData
SignalSaveData
ISimSaveSource
SaveService 기본 구조
JsonSaveRepository 기본 구조
ISaveClock
맵 크기 저장
타일 배치 저장
신호 오프셋 저장
SavedAtUtcTicks 저장
로드 후 오프라인 정산 호출
```

1차 제외:

```text
EconomyService 완성
ResearchService 완성
ProgressionService 완성
차량 개별 위치 저장
UI 상태 저장
복잡한 버전 마이그레이션
클라우드 저장
암호화
압축
```

경제, 연구, 진행도는 인터페이스와 DTO 이름만 먼저 예약하고, 실제 구현은 해당 시스템이 생긴 뒤 연결합니다.

## 팀원별 작업 방식

각 기능 담당자는 자기 기능에 맞는 SaveSource를 구현합니다.

예:

```text
시뮬레이션 담당자:
  ISimSaveSource 구현
  SimSaveData 생성/복원 책임

경제 담당자:
  IEconomySaveSource 구현
  EconomySaveData 생성/복원 책임

연구 담당자:
  IResearchSaveSource 구현
  ResearchSaveData 생성/복원 책임

진행 담당자:
  IProgressionSaveSource 구현
  ProgressionSaveData 생성/복원 책임
```

Save 담당자는 각 담당자의 내부 구현을 직접 만지지 않습니다.
인터페이스만 받아서 저장 데이터를 모읍니다.

## AI에게 전달할 지침

세이브&로드 관련 코드를 생성하거나 수정하는 AI는 아래 규칙을 따라야 합니다.

```text
1. 공통 object 기반 저장 인터페이스보다 타입별 인터페이스 방식을 우선한다.
2. ISimSaveSource, IEconomySaveSource, IResearchSaveSource, IProgressionSaveSource 이름을 사용한다.
3. SaveService는 구체 클래스가 아니라 저장 인터페이스를 받는다.
4. JsonSaveRepository는 파일 입출력만 담당한다.
5. DTO에는 UnityEngine.Object 참조를 넣지 않는다.
6. Vector2Int는 X, Y로 풀어서 저장한다.
7. 계산 결과는 저장하지 않고 로드 후 재계산한다.
8. UI는 저장 상태의 주인이 아니다.
9. 기존 SerializeField, 씬 참조, public API를 임의로 깨지 않는다.
10. 1차 구현 범위를 넘는 기능은 별도 제안으로 분리한다.
```

## 팀 결정 필요 항목

구현 전에 팀에서 확인할 항목:

```text
1. 저장 인터페이스를 Contracts/Save에 둘지 Gameplay/Save에 둘지
2. SimEngine이 ISimSaveSource를 직접 구현할지, 별도 Adapter를 둘지
3. EconomyService를 언제 만들지
4. JsonUtility를 쓸지 Newtonsoft.Json을 쓸지
5. SaveService를 순수 C# 서비스로 둘지 MonoBehaviour로 둘지
6. 백업 파일을 1차부터 쓸지
```

권장 기본값:

```text
저장 인터페이스는 Contracts/Save에 둔다.
SimEngine이 ISimSaveSource를 직접 구현한다.
EconomyService는 세이브 2차 전에 만든다.
직렬화는 1차에서 JsonUtility로 시작한다.
SaveService는 순수 C# 서비스로 시작한다.
Unity 생명주기 연결은 별도 MonoBehaviour 어댑터가 담당한다.
백업 파일은 1차부터 둔다.
```

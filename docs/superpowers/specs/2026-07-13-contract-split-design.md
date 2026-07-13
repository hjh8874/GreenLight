# 설계 — 배치물 계약 분리 (ISignalControl → 3 인터페이스)

> 작성: 2026-07-13 (팀장 리뷰 #47/#54/#55 요청 + 김건 상점 UI 오늘 착수 → 지금 분리 확정). 브랜치 `feat-contract-split-hwan`(스택 최상단, turn-restrict 위).
> 배경: `ISignalControl`이 신호 조율 + 배치물 5종(신호·로터리·입체·일방·턴제한)까지 29개 멤버로 비대. 이름이 목적을 거짓말하고, 김건 상점 UI가 오늘 이 계약에 붙는다 → 잘못된 통짜 위에 코드 쌓이기 전 분리.

## 목표 / 스코프

한 계약을 셋으로 분할. **SimEngine은 이미 전 메서드를 구현 → 클래스 선언에 인터페이스 2개 추가뿐, 메서드 본문 무변경.** 소비자 4곳은 필요한 인터페이스로 재캐스팅. 순수 재편 리팩터 — 로직·동작·테스트 결과 무변경(203 그린 유지가 정확성 증명).

## 분리 (팀장 제안 확정)

**`ISignalControl`** (조율 전용 — 공짜 실시간 조작) — 8:
- `SignalTiles`
- `GetSignalOffsetSlots` / `TrySetSignalOffsetSlots`
- `GetSignalGreenSlots` / `TrySetSignalGreenSlots`
- `TryOverrideSignal` / `GetOverrideSecondsLeft` / `GetOverrideCooldownLeft`

**`IIntersectionFacilityService`** (교차로 시설 배치 — 돈 내고 짓는 인프라. **김건 상점**) — 11:
- 신호: `CanPlaceSignal` / `TryPlaceSignal` / `TryRemoveSignal`
- 로터리: `RoundaboutTiles` / `CanPlaceRoundabout` / `TryPlaceRoundabout` / `TryRemoveRoundabout`
- 입체: `OverpassTiles` / `CanPlaceOverpass` / `TryPlaceOverpass` / `TryRemoveOverpass`

**`ITrafficRuleService`** (도로 규칙 배치 — 방향 표지판. **김건 상점**) — 10:
- 일방통행: `OnewayTiles` / `CanPlaceOneway` / `TryPlaceOneway` / `TryRemoveOneway` / `GetOnewayDir`
- 턴제한: `TurnSignTiles` / `CanPlaceTurnSign` / `TryPlaceTurnSign` / `TryRemoveTurnSign` / `GetTurnMode`

**근거**: ①조율=공짜 실시간, 배치=상점·경제 통과 → 트러스트 경계 분리(팀장 #46/#47 결제-원자성). ②시설(물리 인프라: 신호함·로터리섬·입체데크) vs 규칙(방향 제약 표지판)은 성격이 다름 — 일방·턴제한이 "규칙". ③김건 상점 UI = Facility+Rule 두 계약에만 의존, 신호 탭 UI = ISignalControl에만.

## 구현

1. **파일**: `Contracts/IIntersectionFacilityService.cs`·`Contracts/ITrafficRuleService.cs` 신설. `Contracts/ISignalControl.cs` 슬림화(조율 8개만, 주석도 "조율 전용"으로 정리 — 배치물 주석은 새 파일로 이동).
2. **SimEngine.cs**: 클래스 선언 `... ISignalControl, IIntersectionFacilityService, ITrafficRuleService, ...` 추가. **메서드 본문 손대지 말 것**(이미 다 구현됨).
3. **소비자 재캐스팅**(필요한 것만):
   - `MainCityView.cs`: 현재 `signalControl = services.Placement as ISignalControl`. 튜닝(오프셋·초록·오버라이드·SignalTiles)=ISignalControl 유지 + 마커용 `RoundaboutTiles`/`OverpassTiles`=IIntersectionFacilityService, `OnewayTiles`/`GetOnewayDir`/`TurnSignTiles`/`GetTurnMode`=ITrafficRuleService 필드 2개 추가 캐스팅. 사용처 각 인터페이스로.
   - `SandboxPlacementControls.cs`: `TryPlaceSignal/Roundabout/Overpass`=Facility, `TryPlaceOneway/TurnSign`=Rule, 튜닝 없음. 캐스팅 갱신.
   - `DebugSignalTuner.cs`: 튜닝만 씀 → ISignalControl 그대로(무변경 예상, 확인).
4. **테스트**: 대부분 SimEngine 구체 타입 사용이라 무영향. `as ISignalControl`로 **배치** 호출하는 테스트가 있으면 새 인터페이스로(있는지 grep 확인).

## 검증

- refresh(force)→CS 0→전체 EditMode **203 그린**(리팩터라 신규 테스트 없음 — 그린 유지가 정확성 증명). Play 스모크 불요(로직 무변경).
- 소비자 캐스팅 누락 시 컴파일 에러로 즉시 드러남(런타임 널 아님 — 컴파일 게이트가 안전망).

## 비목표

메서드 시그니처·동작 변경, SimEngine 로직, 새 기능. 순수 인터페이스 재편.

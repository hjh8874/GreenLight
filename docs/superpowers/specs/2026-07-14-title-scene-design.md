# 설계 — 타이틀 화면 (그린웨이브 / GREEN WAVE)

> 작성: 2026-07-14 (환). 브랜치 `feat-title-scene-hwan` (develop 기반). 밀린 UI 보조.
> 배경: 게임에 타이틀/메인메뉴 씬이 없었음. 데스크탑 플로팅 아이들 톤([[desktop-floating-games-reference]])을 참고해 "떠있는 위젯" 감성의 첫 화면.
> **팀 노트:** UI는 김건 영역 — 실제 배선 단계라 조율 권장(환이 밀린 UI 보조).

## 목표 / 스코프

Steam PC(16:9 가로) **타이틀 씬** 신규. 떠있는 카드형 메뉴 + 신호등 색 테마. 버튼 배선(시작/이어하기/설정/종료), 세이브 유무로 이어하기 활성. UGUI(TMP). 엔진·경제·세이브 로직 무변경(읽기만: 세이브 파일 존재 체크).

## 핵심 결정

| 결정 | 내용 | 근거 |
|---|---|---|
| 신규 씬 `Assets/00_Scenes/TitleScene.unity` | 에디터 베이커로 생성(팀 관례 `UIGenerator` 패턴) | 재현 가능·수동 배선 노이즈 회피 |
| 떠있는 카드 메뉴 | 화면 중앙 둥근(가능 시) 카드 안에 타이틀+버튼 세로 스택 | FloatingWindowService 정체성·레퍼런스 코지 톤 |
| 신호등 3색 액센트 | 뉴트럴 베이스 + 초록(주)·앰버·레드 | 그린웨이브 테마 직결 |
| 이어하기 = 세이브 파일 존재 | `SaveFilePathProvider.GetDefaultSavePath()`(`save_v1.json`) File.Exists | 세이브 시스템 결합 최소(읽기만) |
| 시작하기 = 씬 로드 | 인스펙터 `gameSceneName`(Build Settings 등록 필요), 없으면 경고 로그 | 통합 씬이 사람별이라 로드 대상 설정형 |
| 배경 v1 = 정적 | 부드러운 색 + 서브틀 모티프. 라이브 sim 배경은 스트레치 | YAGNI — 룩 먼저, 배선 나중 |

## 컴포넌트
- `TitleSceneController.cs` (런타임, CityFlow.UI) — OnStartNewGame/OnContinue/OnSettings/OnQuit, 세이브 유무 체크, 씬 로드.
- `TitleSceneBaker.cs` (Editor) — [MenuItem]로 씬+UI 생성·버튼 onClick 배선·씬 저장.

## 검증
컴파일 그린 → 씬 열어 Play → 스크린샷 육안(레이아웃·톤) → 버튼 동작(이어하기 비활성 확인). 반복 다듬기.

## 비목표
설정 실내용(볼륨 등 최소 플레이스홀더만), 로고 아트, 라이브 sim 배경, 실제 게임 씬 Build Settings 대량 등록(로드 대상 1개만).

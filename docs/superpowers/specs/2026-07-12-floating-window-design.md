# 설계 — 데스크톱 플로팅 창 (UniWindowController 글루)

> 작성: 2026-07-12 (환 자율 위임 — 로드맵 후속, 환 "고" 승인: 외부 패키지 추가 포함). 짝 계획: 2026-07-12-floating-window.md.
> 선행 스택: 엔진 감사(`feat-engine-audit-hwan`, 159/159) 위. 브랜치 `feat-floating-window-hwan`.
> 배경: 게임 포지셔닝 그 자체("모니터 구석에 사는 도시"). dev-log-12 판단대로 직접 개발 없음 — MIT 라이브러리 통합 글루만.

## 목표 / 스코프

**스탠드얼론 빌드에서** 투명·보더리스·최상위 플로팅 창 + 크기 프리셋 + 클릭 통과 + 저부하 모드. 씬 배선 0(런타임 생성). 에디터에선 안전한 no-op. UI 레이아웃(프리셋별 HUD 레벨)은 김건 영역 — 비목표.

## 핵심 결정 (근거 포함)

| 결정 | 내용 | 근거 |
|---|---|---|
| **패키지 = UniWindowController** | `com.kirurobo.uniwinc`(실제 package.json name — 리포 이름과 다름 주의) — UPM git URL(`https://github.com/kirurobo/UniWindowController.git#upm`)로 manifest.json에 추가(락파일 동반 커밋 — 팀 공유 의존성, PR에서 합의) | MIT, 투명창·클릭통과·최상위·DPI를 전부 검증된 구현으로 제공. 직접 개발은 플랫폼 지뢰밭 |
| 씬 배선 0 | `FloatingWindowService`(신규 MonoBehaviour)를 MainCityView.Initialize가 AddComponent — UniWindowController 컴포넌트도 서비스가 런타임 부착 | 드라이브 뷰와 동일 규약. 씬 오너 손 안 빌림 |
| **기본 = 일반 창, F1 토글** | 플로팅 모드는 F1로 진입/해제, 마지막 모드·프리셋은 PlayerPrefs 저장/복원 | 첫 실행에 화면이 투명해져 있으면 사고처럼 보임 — 옵트인이 안전. 복원은 플로팅 앱의 예의 |
| 프리셋 3단 F2/F3/F4 | S=480×270 / M=960×540 / L=1440×810 (16:9) | 방치형 유저는 "구석에 작게↔볼 때 크게" 두 모드만 씀. 자유 리사이즈는 보더리스에서 직접 구현 비용 큼(YAGNI). 기존 키맵(Tab , . [ ] r g v d)과 비충돌 |
| 클릭 통과 = 픽셀 알파 자동 | UniWinC의 히트테스트(Opacity 방식) 사용 | 도시 밖 투명 영역 클릭이 뒤 창으로 통과해야 "데스크톱에 산다"는 감각 완성 — 기능 내장 |
| 저부하 | 플로팅+S 프리셋이면 `Application.targetFrameRate=30`, 그 외 60 | 상주 앱은 팬 소리로 삭제당한다. 엔진은 고정 틱 누산기라 프레임레이트 무관(결과 동일 — 공짜) |
| 투명 카메라 | 플로팅 진입 시 메인 카메라 clearFlags=SolidColor·배경 알파 0으로 전환, 해제 시 원복. 포스트프로세싱은 건드리지 않음(현재 미사용) | 투명창의 전제. 원복 보관으로 일반 모드 무손상 |
| **에디터 = no-op** | `Application.isEditor`면 창 제어 전부 스킵(키 입력·프리셋 로직은 살아 있되 창 API 미호출) | UniWinC는 빌드 전용 — 에디터에서 호출해도 무해하지만 명시 가드가 의도를 문서화 |
| 카메라 보드 핏 | 해상도(창) 변화 감지 시 `orthoSize = max(boardH/2, boardW/(2·aspect)) + margin` | 띠 비율 창에서 도시가 안 잘리게. Screen.width/height 폴링 비교(프레임당 int 비교 2회 — 공짜) |

## §1. FloatingWindowService (신규, 뷰 전용)

- `Init(float boardW, float boardH)` — MainCityView가 보드 크기 전달.
- Update: F1 토글(플로팅 진입/해제 + 카메라 투명 전환/원복 + PlayerPrefs 저장), F2/F3/F4 프리셋(창 크기 + 저부하 갱신 + 저장), 해상도 변화 감지 → 보드 핏.
- UniWinC 부착·설정(isTransparent/isTopmost/히트테스트)은 실제 패키지 API를 설치 후 읽고 맞춘다 — 버전 간 API 드리프트 방지.
- OnDestroy: targetFrameRate 원복.

## §2. 검증

- 컴파일 + 전체 EditMode 159 그린(회귀 게이트).
- 에디터 Play 스모크: 서비스 초기화 예외 0, F1/F2 키가 에디터에서 예외 없이 no-op/프리셋 상태만 갱신, 카메라 핏이 해상도에 반응(Game 뷰 크기 변경 대신 수식 직접 검증 가능).
- **진짜 창 동작은 스탠드얼론 빌드 필요** — 빌드 검증은 환 수동(데모 조립 때). 보고서에 명시.

## 비목표

프리셋별 HUD 레벨(김건), 자유 리사이즈, 트레이 아이콘, 멀티모니터 스냅, 맥 노타리제이션.

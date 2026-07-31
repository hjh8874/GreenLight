# 밸런스 노브 인계 — 진우 님 (에디터·밸런스 담당 확정 2026-07-30)

작성: 환 (2026-07-30). 튜닝 패널·밸런스 조절이 진우 님 관할로 확정되어, 이번 주에 추가된
노브 전체와 런타임 재주입 seam, 조용히 깨지는 함정을 한 장으로 정리합니다.

## 1. 노브 목록 (위치 · 현재값 · 근거)

### SimConfig (`Assets/05_ScriptableObjects/SimConfig*.asset` ×3)

| 노브 | 현재값 | 의미 · 근거 |
|---|---|---|
| `DayLengthSeconds` | **720** | 하루 = 12분 (#180). `GameTimeSettingsSO`와 이중 권한 — 아래 §3-③ 참조 |
| `CompanyHiringSlotsPerGameHour` | **2** | 채용 램프. 게임시간 기준이라 하루 길이와 무관 — 사무실 만석 3게임시간(실시간 1.5분), 공장 5게임시간(2.5분) |
| `ConstructionHours{House·Office·School·Hospital·Special}` | **전부 0 = 기능 OFF** | #171이 ship-dark로 넣은 건설시간. 값을 채우는 순간 켜집니다. 참고 스케일: 2게임시간 = 실시간 1분 |
| `OfficeCapacity` | 6 | 유형 미지정 회사·학교 주차 폴백 정원 |
| `MorningStart/End`·`EveningStart/End` | 6/10·17/21 | **폴백 창** — 유형 없는 회사·School 통근용. 유형이 있으면 아래 CompanyType이 이김 |

### 회사 3종 (`Assets/05_ScriptableObjects/Companies/CompanyType_*.asset`, PR #182)

| 유형 | 정원 | 출근창 | 퇴근창 | 의도 |
|---|---|---|---|---|
| 사무실 | 6 | [6,10) | [15,19) | 기준 |
| 물류창고 | 4 | [4,8) | [13,17) | 새벽조 — 06~08 사무실과 겹침 |
| 공장 | 5 | [20,24) | [5,9) | 야간조(자정 넘김) — 05~08 새벽조 출근과 교차 |

겹침이 의도입니다 — 완전 분리하면 세 유형이 도로를 공유하지 않아 조정 플레이가 죽습니다.
시각은 게임시간 `[0,24)` — 하루 길이를 바꿔도 창 값은 그대로 둡니다.

⚠️ **정원 상한 6** — 뷰 주차 앵커가 전역 `OfficeCapacity`(6) 슬롯으로 그려져, 6을 넘는 정원은
초과 차량이 마지막 칸에 겹쳐 주차됩니다(리뷰 P2로 공장 10→5 조정). 가드 테스트
`CatalogAsset_CapacitiesFitViewParkingContract`가 6 초과를 막고 있으니, 6 초과가 밸런스에
필요하면 목적지별 정원의 뷰 노출(뷰 소유자 작업)을 먼저 요청해 주세요.

### 연구 사다리 (`Assets/05_ScriptableObjects/Resources/CityFlow/ResearchCatalog.asset`, PR #183)

임계값 8개: 커피숍 인구20 · 비디오 인구40 · 약국 학교1 · 주유소 도착60 · 정비소 도착100 ·
영화관 인구80 · 경찰서 병원1 · 큰상점 도착150. 스케일 근거: 하루 도착 천장 = 차량 상한 96 × 2 = 192.
**사다리 변경 = 에셋 편집만** (코드 0줄, 항목 추가·조건 교체 전부).

### 특수건물 8종 (`Assets/05_ScriptableObjects/Buildings/Building_*.asset`, PR #183)

`buildCost` 200~1,200 (사다리 순) · `coinPerVisit` 10 (통근 `CoinPerTrip`과 동일 — 방문이 통근보다
값지면 안 된다는 원칙) · `visitCadence` 1회/7일 (인구 40이면 하루 ~5.7방문 — 동시 방문 상한
`MaxConcurrentSpecialTrips` 8과 정합). 검산: 커피숍 회수 3.5일. 전부 라이브 튜닝 대상입니다.

### 회사 3종 선택 타일 (`Assets/05_ScriptableObjects/CityFlow/TileData/`, PR #182)

`OfficeData`·`FactoryData`·`WarehouseData`에 `companyTypeId` 기입 완료.
**⚠️ 남은 한 수**: 씬의 BuildPanel 슬롯 배열에 Factory/Warehouse 에셋 연결(씬 직렬화라 저희가 커밋 불가).

## 2. 에디터(튜닝 패널)를 만들 때 쓸 seam

- **`SimEngine.ApplyConfig(in SimConfig)`** — 런타임 재주입용으로 설계된 공식 경로(스펙 2026-07-12).
  퇴화 config(TickInterval≤0 등)는 스스로 거부하고 false 반환. 정원·용량은 재적용 시
  `CapacityCeilingFor` 상한 규칙을 따릅니다(유형 정원이 조용히 깎이지 않음, #182)
- **실전 선례**: `CityBootstrap.SyncSimDayLengthToCalendar()` — Start에서 씬 캘린더 값을 읽어
  `ApplyConfig`로 밀어넣는 코드. 패널의 "적용" 버튼이 할 일과 동일 패턴입니다
- SO 에셋 값은 에디터 밖(빌드)에서 못 바꿉니다 — 런타임 튜닝은 "SimConfig 복사본 수정 → ApplyConfig",
  마음에 든 값을 에셋에 손으로 백라이트하는 흐름을 권합니다

## 3. 조용히 깨지는 함정 3개

1. **`SimConfig` 필드는 `.asset` 3개 전부** (`SimConfig`·`_Integrated`·`_Sandbox`) — 하나 빠지면
   그 씬에서 조용히 0이 들어갑니다 (2026-07-22 팀 규칙)
2. **`SimConfig.Default()`(L123~) 편집은 금지 3종** — 값 튜닝은 `.asset`으로만, Default 수정은 요청 경로로
3. **하루 길이는 이중 권한** — `GameTimeSettingsSO`(표시 시계)와 `SimConfig.DayLengthSeconds`(Sim).
   `CityBootstrap`이 런타임에 캘린더 쪽으로 동기화하므로(#180) **씬의 GameTimeSettings가 이깁니다.**
   하루 길이를 튜닝하려면 GameTimeSettings 에셋을 바꾸는 게 정답입니다

## 4. 검증 습관 (이 레포 특유)

- `refresh_unity` 후 **`read_console`에서 `error CS` 먼저** — 컴파일 실패 시 직전 DLL로 테스트가 돌아
  초록이 컴파일 증거가 아닙니다
- EditMode는 `assembly_names=["CityFlow.Sim.Tests"]`로만 완주 가능, 에디터 어셈블리 테스트는
  `group_names=[".*클래스명.*"]` 이름 필터
- 밸런스 체감은 `Time.timeScale` 가속 + 필요 시 `IGameCalendarSaveSource.RestoreSnapshot`으로
  시각 점프 (단, 점프는 차량 주차 수렴을 유발하니 자연 진행 관찰과 구분할 것)

# 오프라인 정산 제거 · 수확 UI 개선 · 플로팅 HUD 레벨 (2026-08-04)

- 작성: 2026-08-04 (환)
- 기준: `develop` `3214bac` (#216 머지 후)
- 브랜치: 태스크별 **develop 직분기** (스택 금지 — squash 정책)
- 데모까지 **D-9** (8/13)

> ⚠️ **이 계획서는 리뷰 대상이다.** 07-29·07-30 이틀 연속으로 리뷰가 잡은 결함 4건이
> 전부 구현이 아니라 **계획서**에서 나왔다. 착수 전에 이 문서를 먼저 검토한다.
> (`~/LLM_WIKI/wiki/playbook/failure-patterns.md` A1)

---

## 0. 착수 전 확인한 것 (실측)

| 확인 | 결과 |
|---|---|
| develop 상태 | 클린, 열린 PR **0건** |
| 미커밋 | `docs/superpowers/plans/2026-08-03-access-road-intersection-avoidance.md` 1건 — #216의 계획서, **먼저 커밋할 것** |
| 소유권 표 | `WeeklyEconomyLoopService`·`OfflineSettlementPopup`·`CoinHarvestButton`·`HUDDashboard`·`SaveService`·`FloatingWindow*` **전부 표에 없음** → 환이 만질 수 있다 |
| ⚠️ 예외 | `MainCityView.cs`는 3명 분할 소유. 이번에 건드릴 2줄은 **`Update()`** 안이며 표에 없는 구역 — 그래도 **공유 지점이므로 PR에 명시** |
| ✅ 해소 | 플로팅 HUD 레벨 — **김건과 협의 완료, 환이 S/M/L 전부 구현** (§3 C1) |
| ⚠️ 예외 | `SimEngine.cs`에 읽기 전용 프로퍼티 1개 추가 (§3 C3) — **추가만, 기존 로직 무변경**. PR에 명시 |

---

## 1. Task A — 오프라인 정산 제거

### 목표
앱이 꺼진 동안의 수입 계산·리포트를 없앤다. **주간 적립과 수확 버튼은 건드리지 않는다.**

### 비목표
- 주간 정산(`WeeklySettlement`) 제거 — 팝업도 남긴다
- `PendingCoins` 적립 구조 변경 (그건 Task B의 영역이고, 이번엔 UI만)
- 세이브 포맷 파괴

### 제거 대상 (실측)

| 계층 | 대상 |
|---|---|
| 계약 | `Contracts/IOfflineSettlementSource.cs` · `Contracts/IOfflineCalendarProgressionSource.cs` · `Contracts/OfflineSettlementMath.cs` |
| 서비스 | `WeeklyEconomyLoopService`의 `SettleOffline()` · `MaximumOfflineSeconds` · `AverageOnlineIncomePerSecond` · `observedOnlineIncomeCoins`/`observedOnlineSeconds` 추적 · `IOfflineSettlementSource` 구현 선언 |
| 서비스 | `GameCalendarService`의 `IOfflineCalendarProgressionSource` 구현 선언 |
| UI | `UI/HUD/OfflineSettlementPopup.cs` · `Editor/OfflineSettlementPopupBaker.cs` |
| 배선 | `SaveService.cs` L31·32·90·125 (두 소스 프로퍼티와 캐스팅) |
| 참조 | `UIRaycastBlocker.cs` L17 · `TileSelectionController.cs` L72 · `MainCityView.cs` L736·L759 · `TitleSceneController.cs` L58 주석 |
| 설정 | `EconomyConfigSO`의 `OfflineIncomePercent` 등 오프라인 전용 필드 |

### ⚠️ 함정 (계획 단계에서 미리 박아둠)

1. ~~**`AddPendingCoinsInternal(trackAsOnlineIncome:)` 플래그**~~ — ✅ **전수 확인 완료 (08-04)**.
   호출자는 정확히 2곳: `L169`(`true`, 공개 `AddPendingCoins`) · `L188`(`false`, `SettleOffline` 뿐).
   `SettleOffline`이 사라지면 `false` 호출자가 0이 되므로 **플래그와 `L224-231` 블록을 같이 제거**한다.
   `observedOnlineIncomeCoins` 누적처도 여기 하나뿐이다.
2. **`IsInteractionBlocked` 4곳** — 팝업이 사라지면 이 가드도 사라진다. 그런데 **주간 정산 팝업도 같은 종류의 입력 차단이 필요할 수 있다.** 지우기 전에 `WeeklySettlementPopup`이 자기 차단을 갖고 있는지 확인 — 없으면 **입력 차단 공백이 생긴다**(조용히 깨지는 종류).
3. **세이브 호환** — 오프라인 필드가 `GameSaveData`/`WeeklySettlementSaveData`에 있으면 **필드는 남기고 읽기만 무시**한다. 구세이브가 로드 실패하면 안 된다. `TileType` 때와 같은 원칙(값은 맨 뒤, 제거는 무시로).
4. **`RestoreCompleted` 경로** — `WeeklyEconomyLoopService` L312-366에 레거시 캘린더 베이스라인 캡처가 있다. 오프라인 계산과 얽혀 있는지 확인 후 분리.

### TDD
- **RED**: `SaveServiceTests`·`GameCalendarServiceTests`에서 오프라인 관련 단정이 컴파일 실패(`CS0246`)하는 것을 먼저 확인
- **GREEN**: 제거 후 `CityFlow.Sim.Tests` 전체 그린
- **회귀 핀**: ① 구세이브 로드 성공 ② 주간 정산 팝업 정상 동작 ③ 수확 버튼 정상 동작 ④ **입력 차단 공백 없음**(주간 팝업 떠 있을 때 타일 클릭 안 먹는지 라이브 확인)

### 커밋
`refactor(economy): remove offline settlement and report`

---

## 2. Task B — 돈 수확 UI 개선

### 목표
**적립 로직은 그대로 두고 표시만 손본다.** `CoinHarvestButton`(106줄)과 `HUDDashboard`의 표시 계층만.

### 비목표
- `PendingCoins` 적립 규칙·주기 변경
- `TryHarvestPendingCoins()` 시그니처 변경
- 세이브 영향

### 현재 흐름 (실측)
```
차량 도착 → OnArrival → AddPendingCoins → PendingCoins 적립
                                              ↓ PendingCoinsChanged 이벤트
                              CoinHarvestButton.Refresh(pending) → pendingText 갱신
플레이어 클릭 → Harvest() → TryHarvestPendingCoins() → 지갑
```

### ✅ 결정 확정 (2026-08-04, 환)

| # | 항목 | 확정 내용 |
|---|---|---|
| B1 | **수확 가능 신호** | 적립 > 0일 때 버튼 펄스·색 강조. 0이면 무채색 |
| B2 | **버튼 위치** | 맵 **가운데 위**로 이동 (현재 위치에서 옮긴다) |
| B3 | **영수증** | 클릭 시 "어떻게 벌었는지" 내역을 영수증 형태로 표시 |
| B4 | **카운트업 연출** | 수확 시 금액이 따르륵 올라가는 도파민 연출 |

> ❌ **계획서 초안의 오류 정정 (08-04 실측)**: §2에 "동전 분수는 이미 있음"이라 적었으나 **없다.**
> `FlowBurstJuice.cs`는 차량 도착 시 **카메라 셰이크 + 사운드**지 코인 연출이 아니다.
> `fountain`·`CoinBurst` 식별자는 코드 전체 0건. **B4는 새로 짓는다** — 단, 파티클 없이
> 숫자 카운트업만으로 충분하다(환의 요구 자체가 "따르륵 올라가는"). 파티클 시스템 도입 금지.

### 이미 되어 있는 것 (중복 구현 금지)
`CoinHarvestButton.cs:100` — `harvestButton.interactable = DisplayedPendingCoins > 0L;`
**0일 때 비활성은 이미 동작한다.** B1은 그 위에 펄스·색만 얹는다.

### 🔑 B3 영수증 — 데이터는 이미 있다 (실측)

비목표의 "적립 로직 그대로"를 **깨지 않고** 된다. `reason` 문자열이 이미 끝까지 흐른다:

```
IWeeklyEconomyService.AddPendingCoins(long amount, string reason = "weekly income")
  → WeeklyEconomyLoopService.AddPendingCoinsInternal(amount, reason, ...)   L196
    → economySystem.AddWeeklyIncome(chunk, reason)                          L220
      → BasicEconomySystem.cs L95  Debug.Log(...)  ← 여기서 버려진다
```

현재 호출자 = **2곳뿐**:
- `WeeklyEconomyLoopService.cs:266` → `"vehicle arrival"`
- `DistanceRewardService.cs:83` → (거리 보상)

**즉 필요한 건 `reason`을 버리지 않고 모으는 것뿐이다.**
`WeeklyEconomyLoopService`에 `Dictionary<string, long>` 하나를 `PendingCoins` 옆에 두고,
`AddPendingCoinsInternal`에서 누적 · `TryHarvestPendingCoins()`에서 클리어.
새 시스템·새 이벤트·세이브 변경 **없음**. `BasicEconomySystem`은 건드리지 않는다.

> ⚠️ **워커 주의**: `AddWeeklyIncome`은 `int`라 큰 금액이 **청크로 쪼개져 여러 번** 불린다(L217-222).
> 영수증 누적은 반드시 **청크 루프 밖**(`acceptedAmount` 기준)에서 1회만 한다. 안 그러면 내역이 중복된다.

### 비목표 (추가)
- 영수증에 **시각/타임스탬프별 라인** — 사유별 합계까지만. 라인 아이템 로그는 YAGNI
- `reason` 문자열의 **현지화/enum화** — 문자열 그대로 쓴다. 사유가 2개뿐이다

### 커밋
`feat(ui): improve coin harvest presentation`

---

## 3. Task C — 플로팅 HUD 레벨 ⚠️ 착수 전 확인 필요

### ⚠️ 이건 "수정"이 아니라 신규 구현이다

코드를 확인한 결과 **"플로팅일 때 화면 내용을 바꾼다"는 개념이 아예 없다.**
`FloatingWindowService`(920줄)는 **창만** 다룬다 — 크기·프리셋·항상 위·최소화·최대화·드래그.
`OnFloatingStateChanged` 구독자는 `FloatingPanelController`와 `GameSaveLifecycleService` 둘뿐이고,
**HUD 구성을 바꾸는 코드는 없다.** `미니모드`라는 식별자는 코드 전체에 0건.

### 있는 것 — 기획서와 훅

캐논: `~/LLM_WIKI/output/designs/traffic-spirit/minimode-hud-table-2026-07-12.md`

| 프리셋 | 창 크기 | HUD 구성 |
|---|---|---|
| **S 미니모드** | 480×270 | 코인 숫자 + 안정도 색점 + **[+] 확장 버튼 하나** |
| **M 관찰** | 960×540 | S + 호버 타일 정보 + 신호 레버 + 드라이브 뷰 PiP |
| **L 작업** | 1440×810 | 풀 HUD (상점·정책·통계) |

훅은 이미 있다: `PresetIndex` 프로퍼티 · `OnFloatingStateChanged` 이벤트.
기획서 규칙 1이 요구하는 **"프리셋 변경 시 이벤트 노출(환 1줄)"** 만 추가하면 UI가 구독할 수 있다.

### ✅ 막혔던 3개 — 전부 해소 (2026-08-04, 환)

| # | 막혔던 것 | 확정 내용 |
|---|---|---|
| C1 | **소유권** (기획서: HUD 레벨=김건) | **김건과 협의 완료 — 환이 S/M/L 전부 구현.** 기획서 표의 담당자 표기는 무효 |
| C2 | S 클릭스루 | **끄기.** 창이 클릭을 정상 수신. 오조작 위험 제거 + 구현도 없음 |
| C3 | 안정도 색점 (07-18 폐기 개념) | **도시 혼잡도로 재정의.** 초록/노랑/빨강 |

### 🔄 확정으로 기획서 표를 덮어쓰는 규칙 — 자동 숨김

환의 추가 결정: **"플로팅 모드에서는 메뉴는 안 보이고 맵만 보이게. 유저가 한 번 클릭했을 때 메뉴를 보여줌."**

이건 기획서의 S/M/L 표와 **직교하는 별개 축**이다. 합치면 이렇게 된다:

```
플로팅 진입      → HUD 크롬 전부 숨김 (빌드 패널·상단 메뉴바 등). 맵만.
                   상시 노출은 최소 오버레이뿐 — 코인 숫자 · 혼잡 색점 · [+] 버튼
맵 클릭 1회      → 현재 프리셋의 HUD 레벨을 표시
다시 클릭        → 도로 숨김 (토글)
```

즉 **S/M/L 표는 "드러났을 때 무엇이 보이는가"를 정의**하고, 자동 숨김은 그 위에 얹힌다.

> ⚠️ **이건 내 해석이다.** 환의 문장은 자동 숨김만 말했고 S/M/L과의 결합 방식은 말하지 않았다.
> 위 3줄이 의도와 다르면 **구현 전에 말해달라.** 워커는 이 해석대로 짓는다.

### 🔑 C3 혼잡도 — 값은 이미 매 틱 계산 중 (실측)

```
SimEngine.cs:332   float jamRatio = ScanCarCongestion();   ← 도시 전체 정체율 0~1
SimEngine.cs:351   private float ScanCarCongestion()       ← private, 반환값은 _stats로만 흘러감
```

**노출 경로가 없을 뿐 계산은 이미 있다.** 필요한 건 L332의 값을 필드에 캐시하고
읽기 전용 프로퍼티(`CityJamRatio01`) 하나를 여는 것 — 필드 1 + 프로퍼티 1 + 대입 1줄.
새 스캔·새 이벤트·주기 계산 **금지**(YAGNI, 그리고 매 틱 도는 코드다).

> ⚠️ `SimEngine`은 공유 지점이다. **추가만 하고 기존 로직은 한 줄도 바꾸지 않는다.** PR 본문에 명시.

### 범위

| 갈래 | 내용 |
|---|---|
| **C-1** | `FloatingWindowService` 프리셋 변경 이벤트 노출 (기획서 규칙 1의 "환 1줄") |
| **C-2** | 자동 숨김 + 클릭 토글 + S/M/L HUD 레벨 전환 |
| **C-3** | 혼잡 색점 (`SimEngine.CityJamRatio01` 노출 + S 오버레이 표시) |

> **D-9 데모 범위 미확정 주의**: 위키는 플로팅을 "솔로 사이드 아이디어로 보류"라 적어뒀고 코드엔 살아 있다.
> C는 **데모 필수 경로가 아니다** — A·B가 밀리면 C를 먼저 잘라낸다.

### 커밋
```
feat(view): expose floating preset change event          (C-1)
feat(ui): auto-hide HUD in floating mode with click reveal  (C-2)
feat(ui): show city congestion dot in mini mode            (C-3)
```

---

## 4. 실행 순서

```
0. 미커밋 계획서 2건 커밋                    ✅
1. Task A  오프라인 정산 제거                ✅ 게이트 통과
2. Task B  수확 UI (B1~B4)                   ✅ 게이트 통과
3. Task C  플로팅 HUD (C-1~C-3)              ✅ 게이트 통과 (배선 미완 — 아래 참조)
```

**직렬로 돈다.** Unity 체크아웃이 하나뿐이라 워커를 병렬로 띄우면 컴파일·테스트 게이트가 서로를 덮어쓴다
(→ `unity-work-in-main-checkout`). 태스크당 워커 1명, 앞 태스크 커밋 후 다음 워커.

**파일 충돌면**: A는 `MainCityView`·`HUDDashboard`를 스치고, B는 `HUDDashboard`를 쓰고,
C는 HUD 표시 전환을 건드린다 → **B·C가 `HUDDashboard`에서 겹친다.** 직렬이 안전 판정의 근거.

**PR은 15:00~16:00 창구.** 각 태스크 = 별도 브랜치(develop 직분기) = PR 1건. 스택 금지.

### 게이트 (태스크마다 동일, 순서 고정)
1. `refresh_unity(compile="request")`
2. `read_console(types=["error"])` → **`error CS` 0건**. 초록은 컴파일 증거가 아니다
3. `run_tests` EditMode `CityFlow.Sim.Tests` → **실패가 §6의 2건을 넘지 않을 것**
4. `git status`로 `.unity` 혼입 확인 · 새 `.cs`는 `.cs.meta` 동반

---

## 5. 중단 / 에스컬레이션 조건

임의로 범위를 넓히지 않고 결정을 다시 받는다.

- 오프라인 제거로 **입력 차단 공백**이 생기는데 주간 팝업이 자기 차단을 갖고 있지 않다
- 세이브 필드를 지우지 않고는 컴파일이 안 되는 구조가 나온다 (= 구세이브 파손 위험)
- `AddPendingCoinsInternal`의 `trackAsOnlineIncome`을 쓰는 곳이 `SettleOffline` 말고 더 있다
- `MainCityView.Update()` 수정이 다른 소유자 구역과 겹친다
- **B3 영수증이 `reason` 누적만으로 안 되는 구조가 나온다** (= 적립 로직 변경 필요 → 비목표 위반)
- **C-2 자동 숨김 해석(§3)이 환의 의도와 다르다** — 워커가 짓기 전에 확인
- `SimEngine`에 프로퍼티 추가만으로 혼잡도를 못 읽는다 (= 기존 로직 수정 필요)
- **고속도로 때와 같은 신호**: 전체 EditMode에서 기존 그린이 깨지고 원인이 우리 변경이 아니다
- 실패가 §6의 2건을 **넘는다**

---

## 6. 회귀 기준선

**2026-08-04 11:35 실측 완료** (develop `3214bac`, `refresh_unity` 후 `error CS` 0건 확인):

```
EditMode CityFlow.Sim.Tests : 548 / 550  (실패 2)
```

기왕 실패 2건 — **둘 다 `ContentFeatureLogicTests`, 경제·UI와 무관**:

| 테스트 | 실패 사유 |
|---|---|
| `BusStopInfrastructure_BlocksOverlappingPlacementAndLastAccessRemoval` | 정류장 설치 중 진입로가 남아야 하는데 제거됨 (`Expected: False / But was: True`) |
| `PrototypeAssets_AreReadyForSceneIntegration` | `Assets/02_Prefabs/Vehicles/AmbulanceContent.prefab` 없음 |

> **판정 규칙**: 이 2건 외에 실패가 하나라도 늘면 **우리 변경 탓**이다. 대조 불필요.
> 이 2건을 고치는 것은 이번 작업 범위가 **아니다**.

---

## 7. 실행 결과 (2026-08-04 마감)

오케스트레이션(Orca run `run_02f21c1c21e7`) + codex 워커로 구현, 감독이 Unity 게이트를 돌렸다.

### 브랜치 3개 — 전부 develop 직분기, 스택 없음

| 태스크 | 브랜치 | 커밋 | 게이트 |
|---|---|---|---|
| A | `feat/2026-08-04-remove-offline-settlement` | `38b5708` (1개) | `error CS` 0 · **542/542**, 실패 2 |
| B | `feat/2026-08-04-harvest-ui` | `8babbc8` (1개) | `error CS` 0 · **550/550**, 실패 2 |
| C | `feat/2026-08-04-floating-hud-level` | `19102c4`·`b155215`·`666ff96` (3개) | `error CS` 0 · **551/551**, 실패 2 |

실패 2건은 **전부 §6의 기왕 실패**(`BusStopInfrastructure…` · `PrototypeAssets…`)다. 신규 실패 0.

- **A가 550→542**: 오프라인 테스트 8개가 대상과 함께 제거됐다. 정상이다
- **B가 550**: 새 테스트 1개 추가, `SaveServiceTests`에서 옮겨왔으므로 순증 0 (ViewEditMode로 이동)
- **C가 551**: `CityJamRatioTests` 1개 순증

전 브랜치 `.unity` 혼입 0건, 새 `.cs`의 `.cs.meta` 누락 0건.

### 계획서가 틀렸던 것 3개 (A1 재발 — 이번엔 착수 중에 잡힘)

1. **"동전 분수는 이미 있음"(§2)** — 없다. `FlowBurstJuice`는 카메라 셰이크다. B4를 새로 지었다
2. **"테스트는 `Assets/Tests/EditMode/`(asmdef `CityFlow.Sim.Tests`)"** — B·C 스펙 양쪽에 이렇게 썼는데
   **틀렸다.** 그 asmdef는 `overrideReferences: true`이고 `Assembly-CSharp`(= `CityFlow.Gameplay`가
   사는 곳)를 **구조적으로 참조할 수 없다.** 경제 테스트는 `Assets/Tests/ViewEditMode/Editor/`로 가야 한다
3. **"C-1은 1줄"** — 맞았지만 C-2는 신규 컴포넌트 166줄이었다. "1줄"은 C-1에만 해당

### 감독이 틀렸던 절차 1개

B의 첫 커밋을 **`read_console` 없이 `run_tests`만 돌려** 통과로 봤다. 실제로는 테스트 어셈블리가
컴파일 실패였고 **스테일 DLL로 542가 나왔다.** [[unity-green-tests-are-not-compile-proof]] 그대로 재현.
→ 게이트 순서(§4)는 이유가 있다. `refresh → read_console → run_tests`를 **한 번도 건너뛰지 않는다.**

### Unity 러너가 40분 물렸다 — 복구법

전체 EditMode(820개)를 필터 없이 돌렸더니 `MergedFeatureIntegrationPlayModeTests`에서
`blocked_reason: editor_unfocused`로 멈췄다. `last_update_unix_ms`가 동결돼 러너 스레드가 죽은 상태.

- 창 포커스(AppleScript)로는 **안 풀렸다**
- **`manage_editor play` → `stop` 토글로 풀렸다.** 이걸 먼저 시도할 것
- 애초에 **전체 EditMode를 게이트로 쓰지 마라.** 완주가 안 된다

### 남은 일 (PR 전에 알아야 할 것)

- **C는 씬 배선이 안 돼 있다.** `FloatingHudLevelController`의 `chromeRoot`·`minimalOverlay`·
  `levelDeltas` 3개가 미배선이라 **현재 어느 씬에서도 동작하지 않는다.** 클래스 상단 주석에
  무엇을 꽂아야 하는지 적어뒀다. 베이커는 일부러 안 만들었다 — 씬마다 대상이 달라 남의 씬 구조를
  가정하게 된다
- **B의 버튼 위치**는 베이커 코드만 바뀌었다. 기존 씬 인스턴스는 **재베이크 전까지 옛 위치**에 남는다
  (Tools > GreenLight > UI > Bake Manual Coin Harvest UI)
- **라이브 확인 미실시** — §1 회귀 핀 ①구세이브 로드 ②주간 팝업 동작 ③수확 버튼 ④입력 차단 공백은
  EditMode로 덮이지 않는다. **PR 본문에 미검증으로 명시할 것**
- `SimEngine`에 읽기 전용 프로퍼티 1개(`CityJamRatio01`)와 `ICongestionHistory`에 계약 1줄이
  늘었다. 공유 지점이므로 **PR에 명시**

---

## 8. 라이브 검증 (2026-08-04 오후)

§7 시점엔 EditMode만 돌았다. 그 뒤 **씬을 열고 플레이 모드로 실제 확인**했다.
결과: **EditMode·컴파일 전부 초록이었는데도 치명적 결함 4건이 살아 있었다.**

### 통합 머지 검증 — A+B+C를 합쳐도 안 깨진다

`tmp/integration-check-2026-08-04` (로컬 전용, 푸시 금지)에서 셋을 머지했다.

- **A↔B 충돌 1건**: `WeeklyEconomyLoopService.cs` — A가 오프라인 추적을 지운 자리에 B가 영수증을 넣었다.
  해소법은 기계적이다: **B의 `pendingBreakdown` 관련 줄만 남기고 A가 지운 오프라인 줄은 버린다.**
  `trackAsOnlineIncome` 블록도 함께 버린다(A가 파라미터를 없앴다)
- **C는 깨끗이 머지**된다
- 통합 상태 게이트: `error CS` 0 · EditMode **543/543**, 실패 2(기왕 실패)

> ⚠️ 충돌 해소를 스크립트로 하다가 `pendingBreakdown` 선언과 `RestoreSnapshot`의 `Clear()`를
> **조용히 날렸다.** 오프라인 줄과 같은 덩어리에 있었기 때문이다. 손으로 확인해서 되살렸다.
> **머지 충돌은 스크립트로 풀지 말 것.**

### 라이브에서만 잡힌 결함 4건

| # | 결함 | 증상 | 왜 EditMode가 못 잡나 |
|---|---|---|---|
| 1 | **베이커 early-return** | 기존 씬의 버튼이 **옛 위치 그대로**, `receiptText=NULL` → **영수증이 절대 안 뜬다**. 재베이크해도 안 바뀜 | 베이커는 에디터 메뉴 코드다. 테스트가 없다 |
| 2 | **`??`에 Unity 가짜 null** | `GetComponent<CanvasGroup>() ?? AddComponent<...>()` 에서 **`AddComponent`가 영영 실행 안 됨** → C-2 전체 무동작 | 컴파일 통과, 테스트 없음. 실측: `ReferenceEquals(x,null)=False` 인데 `x==null`=True |
| 3 | **`OnEnable` 단발 바인딩** | `FloatingWindowService`를 못 잡고 영영 포기 → 이벤트 미구독 | 씬을 열기 전엔 알 수 없다 |
| 4 | **서비스가 런타임 생성** | `MainCityView.cs:504`가 `AddComponent<FloatingWindowService>()`. 씬에 직렬화 0건 → `Start()`로도 늦다 | 위와 같음 |

**2번이 이번 세션 최대 교훈이다.** `UnityEngine.Object`에 `??`·`?.`를 쓰면 안 된다.
Unity의 `==`는 오버로드돼 있어 C# 널 병합 연산자와 **결과가 갈린다.**

### 회귀 핀 4개 — 전부 통과

| 핀 | 방법 | 결과 |
|---|---|---|
| ① 구세이브 로드 | 실제 `save_v1.json`에 `ObservedOnlineIncomeCoins=98765`·`ObservedOnlineSeconds=4321.5`·`PendingCoins=777` 주입 후 `TryLoadAndRestore()` | **True**, `PendingCoins=777` 복원 ✅ (백업 후 원복 완료) |
| ② 주간 정산 팝업 | 수확 → `SettlementCompleted` → 팝업 표시 | 정상 ✅ |
| ③ 수확 버튼 | `vehicle arrival` 150 + `distance reward` 45 적립 후 수확 | 영수증 `RECEIPT +195 / vehicle arrival +150 / distance reward +45` ✅<br>0.8초 여운 뒤 영수증 숨김 + 라벨 `HARVEST 0` 복원 ✅ |
| ④ 입력 차단 공백 | `WeeklySettlementPopup.IsInteractionBlocked` 관찰 | 팝업 뜰 때 `True` → 닫히면 `False` ✅ 소비자 5곳 전부 이관됨 |

### C-2 S/M/L 전환 — 실측 매트릭스

환의 디버그 씬에 배선하고 플레이 모드에서 상태를 강제 주입해 확인했다.

```
float+hidden S : HUD_TopBar=1  AnalysisCard=0  Build_Panel=0
float+reveal S : HUD_TopBar=1  AnalysisCard=0  Build_Panel=0
float+reveal M : HUD_TopBar=1  AnalysisCard=1  Build_Panel=0
float+reveal L : HUD_TopBar=1  AnalysisCard=1  Build_Panel=1
NON-floating   : HUD_TopBar=1  AnalysisCard=1  Build_Panel=1
```
**스펙과 정확히 일치.**

### 실제 HUD 구조와 배선값 (환의 디버그 씬 기준)

계획서 §3의 "chromeRoot 하나로 크롬을 덮는다"는 **틀렸다.** 크롬이 3개로 흩어져 있고
코인·수확버튼은 `HUD_TopBar` 안에 있어 같이 숨어버린다. 컴포넌트를 씬에 맞춰 다시 짰다.

```
UI_MainCanvas
├ Build_Panel            → lLevelObjects[0]
├ HUD_TopBar             → minimalOverlay   (코인·수확버튼·색점이 사는 곳)
├ Dock_Right             → lLevelObjects[1]
├ AnalysisCard_BottomLeft→ mLevelObjects[0]
├ SubPanels_Right        → mLevelObjects[1]
└ CoinHarvestResultPopup   (주간 정산 팝업 — 건드리지 않음)
```

**이 배선은 커밋되지 않았다.** 검증 후 씬을 디스크에서 되읽어 전부 버렸다(씬 커밋 금지).
각자 자기 씬에서 위 표대로 꽂으면 된다.

### 남은 미검증

- **혼잡 색점(C-3)** — `FloatingCongestionDot`을 씬에 배선하지 않았다. `SimEngine.CityJamRatio01`
  노출과 EditMode 테스트는 통과했지만 **색이 실제로 바뀌는 건 못 봤다**
- **마우스 클릭 토글** — `Apply()`를 직접 호출해 매트릭스를 확인했다. `Mouse.current` 경로로
  실제 클릭해본 것은 아니다
- **다른 사람 씬** — 환의 디버그 씬 하나에서만 확인했다

### 8-1. 미검증 2건 후속 확인 (2026-08-04 14:00)

§8에서 미검증으로 남긴 2건을 씬 배선 + 플레이 모드로 마저 확인했다.

#### ✅ 혼잡 색점(C-3) — 임계값 3구간 정확

`HUD_TopBar` 밑에 `CongestionDot`(Image + `FloatingCongestionDot`)을 만들어 배선했다.
서비스 바인딩은 `ICityFlowServiceConsumer.Initialize` 경로로 **한 번에 잡혔다**
(`_history bound=True`, `services.Placement`=`SimEngine`, `is ICongestionHistory`=True).

`SimEngine._cityJamRatio01`을 주입하며 `Refresh()`를 호출해 측정:

```
0.00 GREEN   0.10 GREEN   0.24 GREEN     ← slow 임계 0.25 미만
0.25 YELLOW  0.45 YELLOW  0.59 YELLOW    ← jam 임계 0.60 미만
0.60 RED     0.85 RED     1.00 RED
```
경계값(0.24/0.25, 0.59/0.60)까지 스펙대로다.

#### ✅ 클릭 토글 — 실제 Input System 이벤트로 양방향 확인

`Apply()` 직접 호출이 아니라 `InputSystem.QueueStateEvent`로 진짜 마우스 입력을 넣었다.
플로팅 + 프리셋 L 상태에서:

```
초기            _isRevealed=False  TopBar=1 Analysis=0 Build=0
1번째 좌클릭 →  _isRevealed=True   TopBar=1 Analysis=1 Build=1
2번째 좌클릭 →  _isRevealed=False  TopBar=1 Analysis=0 Build=0
```
`Mouse.current` 경로와 `wasPressedThisFrame`이 실제로 동작한다.

#### ⚠️ 여전히 미검증 — "드러난 상태에서 HUD 컨트롤 클릭은 숨기지 않는다"

`Update()`의 `EventSystem.current.IsPointerOverGameObject()` 가드는 확인하지 못했다.
플로팅이 게임뷰를 **292×1294**로 줄여놓아, 화면 어느 지점에서도 UI 레이캐스트가 안 걸린다
(`RaycastAll` 결과 전 좌표 `uiHits=0`). 캔버스가 더 넓은 기준 해상도로 잡혀 있어서다.

**게임뷰 지오메트리 문제지 코드 결함의 증거가 아니다.** 다만 확인한 적이 없으므로
정상 창 크기에서 한 번 눌러볼 것. 실패 시 증상은 "HUD 버튼을 누르면 HUD가 같이 숨는다"이다.

#### 정리
검증에 쓴 `CongestionDot` 생성·컨트롤러 배선·재베이크는 **전부 버렸다**(씬을 디스크에서 되읽음).
`CongestionDot`은 베이커가 없으므로 각자 수동으로 만들어야 한다 — 위 위치값 참고:
`HUD_TopBar` 자식, anchor/pivot (0,1), anchoredPosition (12,-12), size 16×16.

# 통근 배정 ↔ 게임 경제 연결 — 진단 & 제안 (프로브, 2026-07-18)

> 코드 무변경 조사 + 특성화 테스트. 경제는 준희 오너 영역 — 이건 **제안**이지 확정 변경 아님.
> 브랜치 `feat-commute-economy-probe-hwan` (car-sim HEAD 위). 테스트만 커밋, 로직 변경 0.

## 현재 구조 (코드 사실)
| 레이어 | 파일 | 하는 일 |
|---|---|---|
| 배정 | `DemandMap.AssignType` | 각 집 → **같은 도로섬 최근접 사무실**(맨해튼), `DemandChoicePool`(3) K-최근접 해시 택1, sticky, 사무실당 `OfficeCapacity`(20) 캡 |
| 실현 | `CommuteScheduler.Rebuild` | 배정 짝 중 `OfficeParkingSlots`(6)만 WorkSlot 얻어 실제 통근 |
| 코인 | `CarSim.Step` → `ArrivalEvent` → `WeeklyEconomyLoopService.OnArrival` | 회사 도착 1회 = flat `CoinPerTrip`(10) → 주간 pending → 수동 수확 |
| (+보너스) | `DistanceRewardService` | 도착 목적지 평균 경로거리 비례 보너스 코인. **이미 배선·배포씬 라이브.** |

## 진단 — 구조적 긴장 3가지
1. **최근접 배정 = 거리 최소화 = 정체 최소화.** 코어 루프가 "교통 최적화로 돈"인데, 배정이 통근거리를 구조적으로 짧게 만들어 **최적화할 정체가 잘 안 생긴다** → 플레이어 실력이 개입할 여지 약화.
2. **코인이 flat.** 긴/어려운 통근을 성사시켜도 보상 동일 → 도로·신호 투자 유인이 약함. `DistanceRewardService`가 바로 이 공백을 메우려 존재.
3. **캡 불일치 (테스트로 확인).** `OfficeCapacity`(배정 20) vs `OfficeParkingSlots`(통근 6)이 서로 다른 레이어에 살고 대화하지 않음.
   - `CommuteEconomyProbeTests` (2/2 PASS): 30집→1사무실 ⇒ **통근 6대·하루 최대 60코인**, 배정 용량 20은 코인에 **무영향(inert)**. 코인 실질 레버 = **사무실 주차슬롯 총합**.

## 제안 (택1/조합 — 준희 판정)
- **A. 거리보상 정식 채택**: `DistanceRewardService`를 "차=돈 1:1 위반"이 아니라 **코어 경제 다리**로 승격. 긴 통근 성사 = 더 많은 코인 → 도로 최적화가 곧 수입. (이미 구현돼 있어 최소 변경.)
- **B. 중력(gravity) 배정**: 최근접 대신 `attractiveness / distance^p`. 고가치 사무실이 먼 통근을 끌어 정체 유발 → 최적화 여지 + 사무실 배치가 공간·경제 퍼즐. (더 큰 기획 변경.)
- **C. 캡 통일**: `OfficeCapacity` ↔ `OfficeParkingSlots`를 하나로, 또는 슬롯을 도로 처리량에 연동해 **사무실 수입 = 실제 도착 처리량**. 레버를 명확히.

## 추천
경제 다리(DistanceReward)는 이미 있음. **최소 변경 = A(거리보상 유지·정식화) + C(캡 통일)** 로 "정체를 뚫을수록 돈"을 성립시키고, B(중력 배정)는 W-단위 그레이박스 검증 후 도입 판단.

## 미결 (환/준희)
- DistanceReward 존치 여부 (앞선 ① PR #110 에스컬레이션과 동일 항목) — A안이면 존치가 답.
- 캡 통일 시 밸런스 재튜닝 (사무실 1개 하루 수입 상한 = 슬롯 × CoinPerTrip).

## 프로브 산출물
- `Assets/Tests/EditMode/CommuteEconomyProbeTests.cs` — 캡 결합 수치 고정, EditMode 2/2 PASS (Unity 6000.5.2f1).

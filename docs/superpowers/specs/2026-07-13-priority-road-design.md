# 우선도로(도로 우선권) 설계 — 무신호 λ 비대칭 확장 (2026-07-13)

> 배치물 6번째. 교차로 시설 가족(신호·로터리·입체)에 "극단 편중" 빈자리를 채운다.
> 근거: [[output/designs/traffic-spirit/cities-skylines-benchmark-2026-07-12]] §4 · [[output/designs/traffic-spirit/balance-sheet-v0-2026-07-12]] §1(3분할 전략)

## 1. 목적 — 왜 필요한가

교차로 시설의 밸런스는 교통량 편중도로 3분할된다(밸런스 시트 §1):

| 편중도 | 최적 시설 |
|---|---|
| 균형 (양축 비슷) | 회전교차로 |
| 편중 | 신호 |
| **극단 편중** (한 축이 압도) | **← 비어 있었음** |

극단 편중에서 신호는 메인축도 시분할로 절반만 초록이라 손해, 로터리는 전원 감속, 입체는 과잉(비쌈). **우선도로 = 메인축 100% 무정차 + 곁길 양보**로 이 자리를 채운다. 편중이 심할수록 이득이 커지는 게 수학적으로 떨어진다.

AI 가변 신호등을 영구 기각한 자리를 **정적 위계**(유저가 우선축 지정)로 메운다 — 자동화가 아니라 마이크로 피로 해소(신호 오프셋을 일일이 안 맞춰도 됨).

## 2. 현실 앵커 — 양보표지/스톱사인

통행우선권(right-of-way)의 주도로/부도로(major/minor) 위계. 곁길이 큰길에 양보하는, 운전면허 상식. 현실에서도 교통량 적은 교차로는 신호 대신 스톱/양보 사인으로 처리한다(신호 설치 기준 = signal warrant) — 게임의 "편중 교차 특화"와 동일 원리.

**게임 카피**: "이 방향을 큰길로 지정합니다. 큰길 차는 멈추지 않고, 교차하는 길이 양보합니다. 한쪽 교통이 훨씬 많을 때 효과적."

## 3. 엔진 — 비대칭 λ

기존 무신호 교차로 간섭은 **대칭**이다(`FlowSolver.cs` else 블록, 스펙 2026-07-11 신호 피벗 1단계):
```
_ratioH = AxisRatio(_flowH + λ·_flowV, cap)   // 대칭: 양축이 똑같이 λ만큼 서로 방해
_ratioV = AxisRatio(_flowV + λ·_flowH, cap)
```
우선도로 = 이 λ를 **비대칭**으로:
```
else if (priority.TryGet(t, out var mainAxis))
{
    bool hMain = mainAxis == Axis.Horizontal;
    _ratioH = AxisRatio(_flowH + (hMain ? λ_main : λ_yield)·_flowV, cap)
    _ratioV = AxisRatio(_flowV + (hMain ? λ_yield : λ_main)·_flowH, cap)
}
```
- **메인축**: 곁길로부터 거의 방해 안 받음 (λ_main ≈ 0)
- **곁길**: 메인축으로부터 크게 양보 (λ_yield 높음)
- 입체교차(양축 무간섭)의 "한 축만" 버전. `else if` 분기 하나 + 방향값 저장 = 로터리 동급 크기.

`SimConfig` 추가(값은 잠정 — 진우 밸런스 튜닝):
```
PriorityMainInterference  = 0.1f   // λ_main
PriorityYieldInterference = 2.5f   // λ_yield (기존 무신호 1.5보다 높게 — 양보 부담)
```

**결정론 유지**: 같은 입력 → 같은 해시. 순회 순서·계수 결정론적.

## 4. 배치 규칙 — 교차로 타일 + 4자 배타

- **교차로 타일에만** 배치 (무신호 교차로의 간섭을 비대칭화하는 것이므로)
- **신호·로터리·입체와 4자 배타** (한 교차로 한 장치). FlowSolver의 `else if` 체인이 이미 신호/입체/로터리를 앞단에서 걸러 자연 배타.
- **우선축 지정**: 배치 시 H/V 선택(일방통행의 방향 지정 패턴)
- 교차로 해제 시 자동 소멸 (신호/로터리 규약 동일)
- 배치/철거 시 **TopologyDirty 아님** — 경로는 그대로, 간섭 계수만 바뀜(신호처럼 재계획 불요)

## 5. 계약 — `IIntersectionFacilityService` 확장

신호·로터리·입체와 동류(교차로 시설, 돈 내고 짓는 인프라). #56 배치물 계약 분리 구조를 따른다.
```csharp
IReadOnlyList<Vector2Int> PriorityRoadTiles { get; }
Axis GetPriorityAxis(Vector2Int tile);
bool CanPlacePriorityRoad(Vector2Int tile);
bool TryPlacePriorityRoad(Vector2Int tile, Axis mainAxis);
bool TryRemovePriorityRoad(Vector2Int tile);
```
`Axis` enum(Horizontal/Vertical)은 신규 — 일방통행의 방향 벡터와 달리 축만 필요(양방향 통행, 축만 우선).

## 6. 세이브 — `PriorityRoadSaveData`

일방통행 세이브 패턴(좌표 + 방향 필드). `{ tile, axis }` 리스트. 구세이브 마이그레이션: 우선도로 없음(빈 리스트)이 기본 — 공짜.

## 7. 뷰 — 양보 표지 마커

곁길 진입부에 양보 삼각형(▽) 마커 + 메인축 굵은 선(또는 화살표). 임시 프리미티브(에셋 스왑 전). `// ponytail: 표지판 3D 에셋은 아트 단계`.
- 마커 z는 신호와 분리(공존 없지만 시각 겹침 회피 — 턴표지판 패턴).

## 8. 테스트 핀 (EditMode)

- 우선도로 배치 → 메인축 `_ratio`↓(무정차 근사) + 곁길 `_ratio`↑
- **핵심 이득**: 편중 교차(fMain ≫ fYield)에서 우선도로가 대칭 무신호보다 메인축 처리량(delivered)↑
- 4자 배타: 신호/로터리/입체 있는 타일에 배치 거부, 우선도로 있는 타일에 그것들 거부
- 교차로 아닌 타일 거부
- 세이브 왕복(축 보존) · 결정론(해시 동일)
- 극단 편중이 아닐 때 놓으면 곁길만 손해(로터리형 "잘못 놓으면 낭비" — 전략 긴장 검증)

## 9. 엣지 · 비고

- 메인축에 트래픽 없으면 이득 0 (무해)
- **라이브 미노출**: `AutoDetectSignals=true`(현행)면 모든 교차로에 신호라 무신호 분기 자체가 스킵 → 우선도로 미발동. **신호 배치 모드(구매 피벗) 라이브 전환일에 무신호 간섭 λ와 함께 활성** (기존 λ와 동일 게이트).
- 상점 UI(김건)가 배치 버튼 연결, 가격(진우)은 balance-sheet 곡선(편중 특화라 신호와 로터리 사이 가격대 후보).

## 10. 착수

- **브랜치**: `feat-priority-road-hwan` (develop 기준)
- **선행 없음**: 엔진·계약·세이브 전부 기존 패턴 확장, 독립 착수 가능
- **공통화 리팩터 대기**: 우선도로가 6번째 좌표-전용 배치물 — 로터리·입체·일방·턴·우선도로 공통화는 별도 티켓(이번 스코프 밖)

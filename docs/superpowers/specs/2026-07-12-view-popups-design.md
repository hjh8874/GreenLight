# 설계 — 도파민 뷰 팩 (마이크로 팝업)

> 작성: 2026-07-12 (환 자율 위임 — 로드맵 2026-07-12 "1차 빌드 확장 ①"). 짝 계획: 2026-07-12-view-popups.md.
> 선행 스택: 입체교차(`feat-overpass-hwan`, 151/151) 위. 브랜치 `feat-view-popups-hwan`.
> 배경: "가짜 디테일" — 엔진은 rate만 알지만 화면은 사람이 사는 것처럼. 이벤트(CongestionChanged·FlowBurst)와 차량·혼잡 폴링이 이미 있어 뷰만 얹는다.

## 목표 / 스코프

**뷰 전용 2연출** (MainCityView, 엔진·계약·세이브 무변경):
1. **Jam 분노 팝업**: Jam 타일 위 차량 머리에 빨간 `!` + 뒤꽁무니 회색 매연 퍼프.
2. **Burst 동전 분수**: FlowBurst 시 기존 구체 이펙트에 더해 동전(노란 소구체) 분수 + `♪` 음표 상승.

이모지(💢/🎵)는 기본 폰트 미보장 → **TextMesh `!`/`♪` + 프리미티브로 선구현**(에셋 확정 후 스프라이트 스왑 — 로드맵 규약). 사운드는 SoundCatalog 에셋 대기(비목표).

## 핵심 결정 (근거 포함)

| 결정 | 내용 | 근거 |
|---|---|---|
| Jam 판정 = 차량 위치 폴링 | `MoveVehicle`에서 `tileData.GetCongestion(currentTile) == Jam`이면 마커 켬 | 이벤트(CongestionChanged)는 타일 단위 전이 — "차량 위" 연출은 차량이 매 프레임 아는 자기 타일 상태가 자연스러운 소스. 이미 같은 곳에서 `GetDensity01` 폴링 중(추가 비용 0) |
| 마커 = 차량의 지연 생성 자식 | RouteVehicle에 마커 GO 필드, 첫 Jam에서 생성 후 SetActive 토글 | 96대 상한 — 매 프레임 생성/파괴 대신 토글. 풀링 프레임워크는 과함(YAGNI) |
| `!`·`♪` = TextMesh | 기본 폰트 보장 글리프. 이모지는 tofu 위험 | 임시 프리미티브 규약. 에셋 스왑 지점을 마커 생성 함수 1곳으로 수렴 |
| 동전 = 뷰 내 간이 포물선 | 소구체 6개, 초기 상향+좌우 랜덤 속도, 중력 상수로 낙하, 0.9s 후 소멸 | 파티클 시스템/리지드바디는 과함 — Update 한 루프면 충분. 뷰 전용이라 Random 사용 무방(결정론 무관) |
| 매연 = 반투명 회색 소구체 1개 | 차량 뒤(진행 반대) 오프셋, Jam 동안 느린 펄스 | "매연 파티클 스트림"은 에셋 단계 — 지금은 존재 암시만 |
| 새 파일 없음 | 전부 MainCityView 안(BurstVisual·RouteVehicle 확장) | 버스트 연출이 이미 이 파일 소관. 파일이 커지는 건 사실 — UI/연출 분리는 에셋 단계 리팩터 후보로 기록 |

## §1. Jam 분노 팝업

- RouteVehicle에 `AngryMark`(TextMesh `!` 빨강, 차량 위 +0.35타일)·`SmokePuff`(회색 반투명 구체 0.1타일, 진행 반대 0.25타일) 필드.
- `MoveVehicle` 말미: `bool jammed = tileData.GetCongestion(currentTile) == CongestionLevel.Jam;` → 마커 lazy 생성 + SetActive(jammed). 켜진 동안 AngryMark는 sin 펄스 스케일, SmokePuff는 느린 상하 부유.
- 차량 비활성 시 마커도 함께 꺼짐(자식이라 자동).

## §2. Burst 동전 분수 + 음표

- `OnFlowBurst`(기존 핸들러) 확장: 기존 구체 + **동전 6개**(노란 소구체, 위치=타일 중심, 속도=위 2.2±좌우 1.2 랜덤) + **`♪` TextMesh 1개**(위로 0.8타일/s 상승, 알파 페이드).
- `UpdateBursts`(기존 루프) 옆에 `UpdateCoins()`: 중력 -6/s², 수명 0.9s 지나면 Destroy. 리스트 역순 정리(기존 bursts 패턴).
- 코인 90도 회전 연출 등은 생략 — 에셋 단계.

## §3. 검증 (Play 프로그래매틱 스모크)

- 컴파일 + 전체 EditMode 151 그린(뷰 전용 — 회귀 게이트).
- Play: ①고수요 직선 도시(무교차 기하 + DemandPerHouse 크게)로 Jam 유발 → 활성 차량 중 AngryMark 활성 ≥ 1 확인 ②`SimEventHub.Publish(new FlowBurstEvent(tile, reward))` 직접 발행(공개 API 확인 완료) → 동전·음표 GO 생성 확인. 비포커스 규약(isPaused+Step 펌핑), config 원복.

## 비목표

사운드(SoundCatalog 에셋 대기), 이모지 스프라이트(에셋 확정 후 스왑), 매연 파티클 스트림, 엔진 변경 일체.

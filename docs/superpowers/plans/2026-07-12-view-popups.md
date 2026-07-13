# 도파민 뷰 팩 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Jam 차량 분노 팝업(`!`+매연) + FlowBurst 동전 분수(`♪`) — 뷰 전용 도파민 연출 2종.

**Architecture:** 전부 MainCityView 안. Jam은 차량별 폴링 토글(마커는 vehicleRoot 소속 — 차량은 비균등 스케일이라 자식 금지), Burst는 기존 OnFlowBurst 핸들러 확장 + 간이 포물선 루프. 엔진·계약·세이브 무변경.

**Tech Stack:** Unity(TextMesh + 프리미티브 — 에셋 스왑 전 임시), Unity MCP. 스펙: `docs/superpowers/specs/2026-07-12-view-popups-design.md`.

## Global Constraints

- 브랜치 `feat-view-popups-hwan`(스택: feat-overpass-hwan 위). 커밋 접두 `[Feat]`. 뷰 전용 — `MainCityView.cs`만.
- **마커를 차량 GO의 자식으로 만들지 말 것** — 차량 localScale (0.34t, 0.16t, 0.12) 비균등이라 텍스트가 찌그러진다. vehicleRoot 소속 + 매 프레임 월드 위치 지정.
- TextMesh 폰트 = `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` + `font.material`(투명 지원). 이모지 금지(`!`/`♪`만 — tofu 위험).
- 기존 스위트 151개 그린 유지(컴파일 게이트). Unity MCP: refresh(force)→콘솔 CS 확인→run_tests job 폴링. Thread.Sleep 금지.
- Play 스모크의 config(`AutoDetectSignals` 등) 변경은 기록→원복 필수. 워킹트리에 에셋 부수 diff 남기지 말 것(checkout 정리).

---

### Task 1: Jam 분노 팝업 (`!` + 매연)

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/View/MainCityView.cs` (RouteVehicle 클래스 ~104행, MoveVehicle 말미 ~575행, RefreshVehicles 비활성 분기 ~470행, 헬퍼는 CreateSignalBar 근처)

**Interfaces:**
- Consumes: `tileData.GetCongestion(tile)`(IReadOnlyTileData, 이미 MoveVehicle에서 currentTile 계산 중), `vehicleRoot`, `tileSize`.
- Produces: `CreateTextMark(parent, text, color, size)` 헬퍼(Task 2가 `♪`에 재사용), RouteVehicle.AngryMark/SmokePuff 패턴.

- [ ] **Step 1: 구현** — RouteVehicle 클래스에 필드 2개 추가:

```csharp
        private sealed class RouteVehicle
        {
            public GameObject Object;
            public Renderer Renderer;
            public float Phase;
            public Vector3 Pos;   // 지난 프레임 위치·진행 방향 — 차간 유지 판정용(1프레임 지연 근사)
            public Vector3 Dir;
            public GameObject AngryMark;   // Jam 팝업(!) — vehicleRoot 소속(차량 자식 금지: 비균등 스케일)
            public GameObject SmokePuff;   // Jam 매연 퍼프 — 동일 소속
        }
```

`CreateSignalBar` 아래에 헬퍼 2개:

```csharp
        // 임시 텍스트 마커(에셋 스왑 전): 기본 폰트 TextMesh. 이모지는 tofu 위험 — 글리프 보장 문자만.
        private GameObject CreateTextMark(Transform parent, string text, Color color, float size)
        {
            GameObject go = new GameObject($"TextMark_{text}");
            go.transform.SetParent(parent, false);
            TextMesh tm = go.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            tm.text = text;
            tm.color = color;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.characterSize = size;
            tm.fontSize = 48;
            return go;
        }

        private GameObject CreateSmokePuff()
        {
            GameObject puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            puff.name = "SmokePuff";
            puff.transform.SetParent(vehicleRoot, false);
            puff.transform.localScale = Vector3.one * (tileSize * 0.12f);
            ApplyRendererColor(PrepareRenderer(puff.GetComponent<Renderer>()), new Color(0.45f, 0.45f, 0.45f));
            return puff;
        }
```

`MoveVehicle` 말미(`vehicle.Pos = ...; vehicle.Dir = travelDir;` 바로 앞)에 Jam 토글 블록:

```csharp
            // Jam 분노 팝업(스펙 2026-07-12 §1): 내가 서 있는 타일이 Jam이면 ! + 매연 — 가짜 디테일.
            bool jammed = tileData.GetCongestion(currentTile) == CongestionLevel.Jam;
            if (jammed && vehicle.AngryMark == null)
            {
                vehicle.AngryMark = CreateTextMark(vehicleRoot, "!", Color.red, tileSize * 0.14f);
                vehicle.SmokePuff = CreateSmokePuff();
            }
            if (vehicle.AngryMark != null)
            {
                vehicle.AngryMark.SetActive(jammed);
                vehicle.SmokePuff.SetActive(jammed);
                if (jammed)
                {
                    Vector3 basePos = vehicle.Object.transform.localPosition;
                    float pulse = 1f + 0.2f * Mathf.Abs(Mathf.Sin(Time.time * 6f));
                    vehicle.AngryMark.transform.localPosition = basePos + new Vector3(0f, tileSize * 0.32f, -0.1f);
                    vehicle.AngryMark.transform.localScale = Vector3.one * pulse;
                    vehicle.SmokePuff.transform.localPosition = basePos - travelDir * (tileSize * 0.28f)
                        + new Vector3(0f, tileSize * 0.06f * Mathf.Sin(Time.time * 2f), 0f);
                }
            }
```

`RefreshVehicles`의 비활성 분기(`if (!active) { continue; }`)를 마커도 끄도록 확장:

```csharp
                if (!active)
                {
                    if (vehicles[i].AngryMark != null)
                    {
                        vehicles[i].AngryMark.SetActive(false);
                        vehicles[i].SmokePuff.SetActive(false);
                    }
                    continue;
                }
```

- [ ] **Step 2: 컴파일 확인** — `refresh_unity`(force) → `read_console` CS 에러 0.

- [ ] **Step 3: 전체 회귀** — `run_tests`(EditMode) 151/151.

- [ ] **Step 4: 커밋**

```bash
cd ~/Gamemaker/GreenLight
git add Assets/01_Scripts/CityFlow/View/MainCityView.cs
git commit -m "[Feat] Jam 분노 팝업 — 차량 위 ! 펄스 + 매연 퍼프 (임시 프리미티브)"
```

---

### Task 2: Burst 동전 분수 + `♪`

**Files:**
- Modify: `Assets/01_Scripts/CityFlow/View/MainCityView.cs` (OnFlowBurst ~기존 핸들러, UpdateBursts 옆, Update 배선, BurstVisual 클래스 근처)

**Interfaces:**
- Consumes: Task 1의 `CreateTextMark`, 기존 `OnFlowBurst`/`UpdateBursts`/`effectRoot`/`GridToLocal`.
- Produces: 뷰 전용 — 후속 소비자 없음.

- [ ] **Step 1: 구현** — 필드/클래스(BurstVisual 클래스 아래):

```csharp
        private sealed class CoinVisual
        {
            public GameObject Object;
            public Vector3 Velocity;
            public float DieAt;
        }

        private sealed class NoteVisual
        {
            public TextMesh Text;
            public float DieAt;
        }

        private readonly List<CoinVisual> coins = new();
        private readonly List<NoteVisual> notes = new();
        [SerializeField] private Color coinColor = new Color(1f, 0.84f, 0.2f);
```

`OnFlowBurst` 말미에 추가:

```csharp
            // 동전 분수 + 음표(스펙 2026-07-12 §2): 길이 뚫리는 순간의 도파민 — 뷰 전용, Random 무방.
            Vector3 origin = GridToLocal(e.Tile, -0.5f);
            for (int i = 0; i < 6; i++)
            {
                GameObject coin = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                coin.name = "Coin";
                coin.transform.SetParent(effectRoot, false);
                coin.transform.localPosition = origin;
                coin.transform.localScale = Vector3.one * (tileSize * 0.1f);
                ApplyRendererColor(PrepareRenderer(coin.GetComponent<Renderer>()), coinColor);
                coins.Add(new CoinVisual
                {
                    Object = coin,
                    Velocity = new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(1.6f, 2.4f), 0f) * tileSize,
                    DieAt = Time.time + 0.9f,
                });
            }
            GameObject note = CreateTextMark(effectRoot, "♪", coinColor, tileSize * 0.16f);
            note.transform.localPosition = origin + new Vector3(0f, tileSize * 0.2f, 0f);
            notes.Add(new NoteVisual { Text = note.GetComponent<TextMesh>(), DieAt = Time.time + 1.1f });
```

`UpdateBursts` 아래에 루프 2개, `Update()`의 `UpdateBursts();` 뒤에 `UpdateCoins(); UpdateNotes();` 배선:

```csharp
        private void UpdateCoins()
        {
            for (int i = coins.Count - 1; i >= 0; i--)
            {
                CoinVisual coin = coins[i];
                if (coin.Object == null || Time.time >= coin.DieAt)
                {
                    if (coin.Object != null)
                    {
                        Destroy(coin.Object);
                    }
                    coins.RemoveAt(i);
                    continue;
                }
                coin.Velocity += Vector3.down * (6f * tileSize * Time.deltaTime);   // 간이 중력
                coin.Object.transform.localPosition += coin.Velocity * Time.deltaTime;
            }
        }

        private void UpdateNotes()
        {
            for (int i = notes.Count - 1; i >= 0; i--)
            {
                NoteVisual note = notes[i];
                if (note.Text == null || Time.time >= note.DieAt)
                {
                    if (note.Text != null)
                    {
                        Destroy(note.Text.gameObject);
                    }
                    notes.RemoveAt(i);
                    continue;
                }
                note.Text.transform.localPosition += Vector3.up * (0.8f * tileSize * Time.deltaTime);
                Color c = note.Text.color;
                c.a = Mathf.Clamp01((note.DieAt - Time.time) / 1.1f);
                note.Text.color = c;   // 폰트 머티리얼은 투명 지원
            }
        }
```

- [ ] **Step 2: 컴파일 확인** — `refresh_unity`(force) → `read_console` CS 에러 0.

- [ ] **Step 3: Play 프로그래매틱 스모크** — 비포커스 규약(isPaused+Step 펌핑), config 기록→원복:
  1. 고수요 직선 도시 구성(교차로 불필요 — Jam만 필요): 씬 Play 진입 후 엔진에 도로 일자 + 집·회사 배치, `DemandPerHouse`를 크게 못 바꾸면(SO) 집 여러 채로 Jam 유발. 펌핑 후 활성 차량 중 `AngryMark` activeSelf == true ≥ 1 확인.
  2. Bootstrap의 `SimEventHub` 취득(리플렉션 허용) → `hub.Publish(new FlowBurstEvent(tile, 10))` 직접 발행 → 펌핑 → `GameObject.Find("Coin")` != null 및 `TextMark_♪` 존재 확인 → 0.9s+ 펌핑 후 소멸(정리 루프 작동) 확인.
  3. Play 종료, 워킹트리 클린 확인. 환경 문제로 막히면 컴파일+151 그린으로 DONE_WITH_CONCERNS(막힌 지점 명시).

- [ ] **Step 4: 전체 회귀** — EditMode 151/151.

- [ ] **Step 5: 커밋**

```bash
cd ~/Gamemaker/GreenLight
git add Assets/01_Scripts/CityFlow/View/MainCityView.cs
git commit -m "[Feat] Burst 동전 분수 + 음표 — 간이 포물선, 페이드 상승 (임시 프리미티브)"
```

---

## 완료 기준

- EditMode 151/151(신규 테스트 없음 — 뷰 전용, 컴파일 게이트).
- Play 스모크: Jam 마커 활성 ≥1, 합성 FlowBurst로 동전·음표 생성→소멸 확인, config 원복·워킹트리 클린.

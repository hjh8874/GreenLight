using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class SimEngineTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // TickInterval을 0.25f로: float로 정확히 표현되는 1/4이라
        // 0.1f의 반올림 잡음 없이 누산기 '로직'만 순수하게 검증한다.
        static SimConfig Cfg(float tick, int cap = 5)
        {
            var c = SimConfig.Default();
            c.TickInterval = tick;
            c.MaxStepsPerFrame = cap;
            return c;
        }

        static SimEngine Engine(SimConfig c) => new SimEngine(c, new SimEventHub());

        [Test]
        public void Accumulator_FourEighthTicks_ProduceTwoSteps()
        {
            var e = Engine(Cfg(0.25f));
            for (int i = 0; i < 4; i++) e.Tick(0.125f); // 0.125 ×4 = 0.5 = 2 틱
            Assert.AreEqual(2, e.StepCount);
        }

        [Test]
        public void Accumulator_CarriesRemainderAcrossCalls()
        {
            var e = Engine(Cfg(0.25f));
            e.Tick(0.875f);                  // 0.875 / 0.25 = 3.5 → 3 스텝, 잔여 0.125
            Assert.AreEqual(3, e.StepCount);
            e.Tick(0.125f);                  // 잔여 0.125 + 0.125 = 0.25 → +1 스텝
            Assert.AreEqual(4, e.StepCount);
        }

        [Test]
        public void Accumulator_CapsStepsPerFrame()
        {
            var e = Engine(Cfg(0.25f, cap: 5));
            e.Tick(100f);                    // 원래 400 스텝이지만 캡에 걸려 5
            Assert.AreEqual(5, e.StepCount);
        }

        [Test]
        public void EndToEnd_PlacedCity_FlowsAfterOneTick()
        {
            // 파사드 관통: Place(IPlacementService)로 도시 배치 → Tick 1스텝 →
            // IReadOnlyTileData로 조회. 수요 1/용량 10 → ratio 0.1 → Free, density 0.1.
            var c = Cfg(0.25f);
            c.GridWidth = 5; c.GridHeight = 2;
            c.DemandPerHouse = 1f; c.RoadCapacity = 10f;
            var e = Engine(c);

            for (int x = 0; x <= 4; x++) Assert.IsTrue(e.Place(V(x, 0), TileType.Road));
            Assert.IsTrue(e.Place(V(0, 1), TileType.House));
            Assert.IsTrue(e.Place(V(4, 1), TileType.Office));

            e.Tick(0.25f); // 정확히 1스텝

            Assert.AreEqual(TileType.House, e.GetTileType(V(0, 1)));
            Assert.AreEqual(CongestionLevel.Free, e.GetCongestion(V(2, 0)));
            Assert.AreEqual(0.1f, e.GetDensity01(V(2, 0)), 1e-4f);
        }

        [Test]
        public void EndToEnd_ArrivalPublishedThroughHub()
        {
            // 파이프라인 완주: rate 0.5/s × 8틱(0.25s) = 2초 시뮬 → 도착 정확히 1건이 hub로.
            var c = Cfg(0.25f);
            c.GridWidth = 5; c.GridHeight = 2;
            c.DemandPerHouse = 0.5f; c.RoadCapacity = 10f; c.CoinBase = 1f;

            var hub = new SimEventHub();
            int arrivals = 0, coins = 0;
            hub.Arrival += ev => { arrivals++; coins += ev.Coins; };
            var e = new SimEngine(c, hub);

            for (int x = 0; x <= 4; x++) e.Place(V(x, 0), TileType.Road);
            e.Place(V(0, 1), TileType.House);
            e.Place(V(4, 1), TileType.Office);

            for (int i = 0; i < 8; i++) e.Tick(0.25f);

            Assert.AreEqual(1, arrivals);
            Assert.AreEqual(1, coins);
        }

        [Test]
        public void EndToEnd_JamCity_StabilityDrops()
        {
            // 계획 8을 파사드로: 수요 15/용량 10 → E 0.6 → Stability01 = 9/15 = 0.6
            var c = Cfg(0.25f);
            c.GridWidth = 5; c.GridHeight = 2;
            c.DemandPerHouse = 15f; c.RoadCapacity = 10f;
            var e = new SimEngine(c, new SimEventHub());

            for (int x = 0; x <= 4; x++) e.Place(V(x, 0), TileType.Road);
            e.Place(V(0, 1), TileType.House);
            e.Place(V(4, 1), TileType.Office);

            e.Tick(0.25f);

            Assert.AreEqual(0.6f, e.Stability01, 1e-3f);
        }

        [Test]
        public void PlaceAndRemove_PublishPlacedEvents_OnTickDrain()
        {
            var c = Cfg(0.25f);
            c.GridWidth = 5; c.GridHeight = 2;
            var hub = new SimEventHub();
            var placed = new System.Collections.Generic.List<PlacedEvent>();
            hub.Placed += ev => placed.Add(ev);
            var e = new SimEngine(c, hub);

            e.Place(V(1, 0), TileType.Road);
            e.Place(V(1, 0), TileType.Road);       // 중복 배치 실패 → 이벤트 없어야 함
            Assert.AreEqual(0, placed.Count);      // 즉시 발행 금지 — 틱 끝 Drain에서만

            e.Tick(0.25f);
            Assert.AreEqual(1, placed.Count);
            Assert.AreEqual(V(1, 0), placed[0].Tile);
            Assert.AreEqual(TileType.Road, placed[0].Type);
            Assert.IsFalse(placed[0].IsRemove);

            e.Remove(V(1, 0));
            e.Tick(0.25f);
            Assert.AreEqual(2, placed.Count);
            Assert.AreEqual(TileType.Road, placed[1].Type);   // 뭘 지웠는지도 담아서
            Assert.IsTrue(placed[1].IsRemove);
        }

        // ── StabilityChanged: 바뀐 틱에 이번 틱의 진짜 값을, 안 바뀌면 침묵 ──

        // 정체 도시(수요 15/용량 10 → 0.6, JamCity 테스트와 동일 레시피)를 짓고 hub 구독을 연결.
        static (SimEngine engine, System.Collections.Generic.List<float> got) JamCityWithStabilityLog()
        {
            var c = Cfg(0.25f);
            c.GridWidth = 5; c.GridHeight = 2;
            c.DemandPerHouse = 15f; c.RoadCapacity = 10f;

            var hub = new SimEventHub();
            var got = new System.Collections.Generic.List<float>();
            hub.StabilityChanged += ev => got.Add(ev.Stability01);
            var e = new SimEngine(c, hub);

            for (int x = 0; x <= 4; x++) e.Place(V(x, 0), TileType.Road);
            e.Place(V(0, 1), TileType.House);
            e.Place(V(4, 1), TileType.Office);
            return (e, got);
        }

        [Test]
        public void StabilityChanged_FirstTick_PublishesCurrentStability()
        {
            // 첫 틱 이벤트는 초기값(1.0)이 아니라 이번 틱 계산값(0.6)을 실어야 한다.
            // 세이브 로드 복원 직후에도 구독자(해금 시스템)가 첫 틱부터 진짜 값을 받는 계약.
            var (e, got) = JamCityWithStabilityLog();

            e.Tick(0.25f);

            Assert.AreEqual(1, got.Count);
            Assert.AreEqual(0.6f, got[0], 1e-3f);
        }

        [Test]
        public void StabilityChanged_UnchangedStability_NoRepublish()
        {
            // 안정도가 그대로면 이후 틱은 침묵(스팸 방지) — 첫 발행 1회로 끝.
            var (e, got) = JamCityWithStabilityLog();

            for (int i = 0; i < 10; i++) e.Tick(0.25f);

            Assert.AreEqual(1, got.Count);
        }

        [Test]
        public void StabilityChanged_RepublishesOnChange_SameTick()
        {
            // 자유 흐름(1.0) → 도로 절단(수요 미배달 → 0.0). 바뀐 '그' 틱에 새 값이 나가야 한다.
            var c = Cfg(0.25f);
            c.GridWidth = 5; c.GridHeight = 2;
            c.DemandPerHouse = 1f; c.RoadCapacity = 10f;

            var hub = new SimEventHub();
            var got = new System.Collections.Generic.List<float>();
            hub.StabilityChanged += ev => got.Add(ev.Stability01);
            var e = new SimEngine(c, hub);

            for (int x = 0; x <= 4; x++) e.Place(V(x, 0), TileType.Road);
            e.Place(V(0, 1), TileType.House);
            e.Place(V(4, 1), TileType.Office);

            e.Tick(0.25f);                       // 자유 흐름 → 1.0 발행
            Assert.AreEqual(1, got.Count);
            Assert.AreEqual(1f, got[0], 1e-3f);

            e.Remove(V(2, 0));                   // 유일 경로 절단 → delivered 0
            e.Tick(0.25f);                       // 바뀐 틱에 즉시 재발행
            Assert.AreEqual(2, got.Count);
            Assert.AreEqual(0f, got[1], 1e-3f);

            for (int i = 0; i < 5; i++) e.Tick(0.25f);   // 이후 변화 없음 → 침묵
            Assert.AreEqual(2, got.Count);
        }

        // ── ISimSaveSource(한준희 세이브 계약): 스냅샷 → 복원 왕복 ──

        [Test]
        public void SaveSource_ExposesGridSize()
        {
            // 소프트캐스트가 아니라 컴파일 타임 보장: SimEngine이 인터페이스를 직접 구현.
            var c = Cfg(0.25f);
            c.GridWidth = 9; c.GridHeight = 2;
            CityFlow.Contracts.Save.ISimSaveSource src = Engine(c);
            Assert.AreEqual(9, src.GridWidth);
            Assert.AreEqual(2, src.GridHeight);
        }

        [Test]
        public void CreateSnapshot_StoresOnlyPlacedTiles()
        {
            // 계약: Empty 타일은 저장하지 않는다(naming-contract §SimSaveData).
            var c = Cfg(0.25f);
            c.GridWidth = 5; c.GridHeight = 2;
            var e = Engine(c);
            e.Place(V(0, 0), TileType.Road);
            e.Place(V(4, 1), TileType.School);

            var snap = ((CityFlow.Contracts.Save.ISimSaveSource)e).CreateSnapshot();

            Assert.AreEqual(2, snap.PlacedTiles.Length);   // 나머지 8칸(Empty) 미저장
        }

        [Test]
        public void SaveRoundtrip_RestoresTiles_Signals_AndThroughput()
        {
            // 진짜 계약은 "도시가 똑같이 동작한다": 조율된 두 신호 도시를 스냅샷 →
            // 다른 도시가 그려진 엔진에 복원 → 타일·오프셋·처리량까지 원본과 동일.
            var c = Cfg(0.25f);
            c.GridWidth = 9; c.GridHeight = 2;
            c.DemandPerHouse = 1f; c.RoadCapacity = 10f;

            var a = new SimEngine(c, new SimEventHub());
            for (int x = 0; x <= 8; x++) a.Place(V(x, 0), TileType.Road);
            a.Place(V(2, 1), TileType.Road);
            a.Place(V(6, 1), TileType.Road);
            a.Place(V(0, 1), TileType.House);
            a.Place(V(8, 1), TileType.Office);
            a.Tick(0.25f);                                          // 교차로 2개 감지
            Assert.IsTrue(a.TrySetSignalOffsetSlots(V(6, 0), 4));   // 그린웨이브 조율(오프셋 레버)
            Assert.IsTrue(a.TrySetSignalGreenSlots(V(2, 0), 4));    // 초록 길이 조율(듀티 레버)
            a.Tick(0.25f);
            float originalDelivered = a.DeliveredTotal;

            var snapshot = ((CityFlow.Contracts.Save.ISimSaveSource)a).CreateSnapshot();

            var b = new SimEngine(c, new SimEventHub());
            b.Place(V(1, 1), TileType.House);                        // 스냅샷과 무관한 잔여 도시
            b.Place(V(3, 0), TileType.Road);
            b.Tick(0.25f);

            ((CityFlow.Contracts.Save.ISimSaveSource)b).RestoreSnapshot(snapshot);
            b.Tick(0.25f);                                           // 복원 후 첫 틱: 재구축+계산

            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 9; x++)
                    Assert.AreEqual(a.GetTileType(V(x, y)), b.GetTileType(V(x, y)), $"타일 불일치 ({x},{y})");
            Assert.AreEqual(2, b.SignalTiles.Count);
            Assert.AreEqual(4, b.GetSignalOffsetSlots(V(6, 0)));     // 오프셋 레버 복원
            Assert.AreEqual(4, b.GetSignalGreenSlots(V(2, 0)));      // 초록 길이 레버 복원
            Assert.AreEqual(originalDelivered, b.DeliveredTotal, 1e-3f);   // 두 레버 효과까지 동일
        }

        [Test]
        public void RestoreSnapshot_LegacySaveWithoutGreenSlots_KeepsDefaultGreen()
        {
            // 구세이브 호환: GreenSlots 필드가 없던 세이브는 역직렬화 시 0으로 옴.
            // 0을 "미저장"으로 보고 기본 초록을 덮지 않아야 한다(0=상시적색으로 오해 금지).
            var c = Cfg(0.25f);
            c.GridWidth = 9; c.GridHeight = 2;
            var e = new SimEngine(c, new SimEventHub());
            for (int x = 0; x <= 8; x++) e.Place(V(x, 0), TileType.Road);
            e.Place(V(4, 1), TileType.Road);
            e.Tick(0.25f);
            int defaultGreen = e.GetSignalGreenSlots(V(4, 0));       // 기본 초록(주기 절반)
            Assert.Greater(defaultGreen, 0);

            var tiles = new System.Collections.Generic.List<CityFlow.Contracts.Save.TileSaveData>
            {
                new CityFlow.Contracts.Save.TileSaveData { X = 4, Y = 1, Type = TileType.Road },
            };
            for (int x = 0; x <= 8; x++)
                tiles.Add(new CityFlow.Contracts.Save.TileSaveData { X = x, Y = 0, Type = TileType.Road });

            var legacy = new CityFlow.Contracts.Save.SimSaveData
            {
                PlacedTiles = tiles.ToArray(),
                SignalOffsets = new[]
                {
                    // GreenSlots 미지정 = 0 (구세이브 모사)
                    new CityFlow.Contracts.Save.SignalSaveData { X = 4, Y = 0, OffsetSlots = 2 },
                },
            };

            ((CityFlow.Contracts.Save.ISimSaveSource)e).RestoreSnapshot(legacy);
            e.Tick(0.25f);

            Assert.AreEqual(2, e.GetSignalOffsetSlots(V(4, 0)));     // 오프셋은 복원
            Assert.AreEqual(defaultGreen, e.GetSignalGreenSlots(V(4, 0)));  // 초록은 기본 유지(0으로 안 덮음)
        }

        [Test]
        public void RestoreSnapshot_NullAndOutOfBounds_AreSafe()
        {
            var c = Cfg(0.25f);
            c.GridWidth = 5; c.GridHeight = 2;
            var e = Engine(c);
            e.Place(V(1, 0), TileType.Road);

            ((CityFlow.Contracts.Save.ISimSaveSource)e).RestoreSnapshot(null);   // null → 무시
            Assert.AreEqual(TileType.Road, e.GetTileType(V(1, 0)));              // 기존 상태 유지

            var bad = new CityFlow.Contracts.Save.SimSaveData
            {
                PlacedTiles = new[]
                {
                    new CityFlow.Contracts.Save.TileSaveData { X = 99, Y = 99, Type = TileType.Road },  // OOB
                    new CityFlow.Contracts.Save.TileSaveData { X = 1, Y = 1, Type = TileType.House },
                },
                SignalOffsets = new[]
                {
                    new CityFlow.Contracts.Save.SignalSaveData { X = 50, Y = 50, OffsetSlots = 3 },     // OOB
                },
            };
            ((CityFlow.Contracts.Save.ISimSaveSource)e).RestoreSnapshot(bad);    // OOB 조용히 스킵
            e.Tick(0.25f);

            Assert.AreEqual(TileType.House, e.GetTileType(V(1, 1)));   // 유효분 반영
            Assert.AreEqual(TileType.Empty, e.GetTileType(V(1, 0)));   // 복원 = 전체 교체(잔존 없음)
        }

        // ── ISignalControl(신호 조작 계약): 오프셋에 이어 초록 길이도 엔진 밖에서 조작 ──

        [Test]
        public void SignalControl_GreenLever_ChangesThroughput()
        {
            // 초록을 늘리면 그 교차로 유효 용량↑ → 막혔던 처리량 회복. #11 레버의 손잡이(setter).
            var c = Cfg(0.25f);
            c.GridWidth = 9; c.GridHeight = 2;
            c.DemandPerHouse = 6f; c.RoadCapacity = 10f;   // 신호 타일 유효용량 5(듀티 0.5) → Jam
            var e = new SimEngine(c, new SimEventHub());
            for (int x = 0; x <= 8; x++) e.Place(V(x, 0), TileType.Road);
            e.Place(V(4, 1), TileType.Road);               // (4,0)이 교차로
            e.Place(V(0, 1), TileType.House);
            e.Place(V(8, 1), TileType.Office);
            e.Tick(0.25f);

            ISignalControl ctl = e;                        // 계약으로만 조작
            Assert.AreEqual(1, ctl.SignalTiles.Count);
            float choked = e.DeliveredTotal;

            Assert.IsTrue(ctl.TrySetSignalGreenSlots(V(4, 0), 999));   // 초록 최대(주기-1로 클램프) → 듀티 15/16
            e.Tick(0.25f);

            Assert.Greater(e.DeliveredTotal, choked);        // 레버가 살아있음
            Assert.AreEqual(6f, e.DeliveredTotal, 1e-3f);    // 완전 회복(ratio 0.64 ≤ Jam)
        }

        [Test]
        public void TrySetSignalGreenSlots_NonSignalTile_ReturnsFalse()
        {
            var c = Cfg(0.25f);
            c.GridWidth = 5; c.GridHeight = 2;
            var e = Engine(c);
            e.Place(V(1, 0), TileType.Road);
            e.Tick(0.25f);
            Assert.IsFalse(((ISignalControl)e).TrySetSignalGreenSlots(V(1, 0), 4));   // 직선 도로 = 신호 없음
        }

        [Test]
        public void TrySetSignalGreenSlots_ClampsToCycleRange()
        {
            var c = Cfg(0.25f);
            c.GridWidth = 9; c.GridHeight = 2;
            var e = new SimEngine(c, new SimEventHub());
            for (int x = 0; x <= 8; x++) e.Place(V(x, 0), TileType.Road);
            e.Place(V(4, 1), TileType.Road);
            e.Tick(0.25f);

            ISignalControl ctl = e;
            ctl.TrySetSignalGreenSlots(V(4, 0), -5);
            Assert.AreEqual(1, ctl.GetSignalGreenSlots(V(4, 0)));    // 음수 → 최소 초록 1슬롯(신호 데드락 방지)
            ctl.TrySetSignalGreenSlots(V(4, 0), 999);
            Assert.AreEqual(15, ctl.GetSignalGreenSlots(V(4, 0)));   // 주기 초과 → 주기-1(반대 축 최소 보장)
        }

        [Test]
        public void OverrideSignal_ForcesAxisGreen_ThenCooldownAndExpiry()
        {
            // 오버라이드 스킬: 지정 축 강제 초록 + 반대 축 적색, 쿨다운 중 재사용 거절, 만료 후 복귀.
            var c = Cfg(0.25f);
            c.GridWidth = 9; c.GridHeight = 2;
            c.OverrideDurationSeconds = 0.5f;   // 테스트용 짧게
            c.OverrideCooldownSeconds = 1f;
            var e = new SimEngine(c, new SimEventHub());
            for (int x = 0; x <= 8; x++) e.Place(V(x, 0), TileType.Road);
            e.Place(V(4, 1), TileType.Road);
            e.Tick(0.25f);                       // 교차로 감지

            Assert.IsFalse(e.TryOverrideSignal(V(1, 0), true));                    // 신호 없는 타일 거절
            Assert.IsTrue(e.TryOverrideSignal(V(4, 0), horizontal: false));
            Assert.AreEqual(SignalPhase.Green, e.GetSignalPhase(V(4, 0), false));  // 지정 축 초록
            Assert.AreEqual(SignalPhase.Red, e.GetSignalPhase(V(4, 0), true));     // 반대 축 적색(충돌 방지)
            Assert.IsFalse(e.TryOverrideSignal(V(4, 0), false));                   // 지속+쿨다운 중 거절
            Assert.Greater(e.GetOverrideSecondsLeft(V(4, 0)), 0f);

            for (int i = 0; i < 8; i++) e.Tick(0.25f);                             // 2s 경과 = 만료+쿨다운 해제
            Assert.AreEqual(0f, e.GetOverrideSecondsLeft(V(4, 0)));
            Assert.IsTrue(e.TryOverrideSignal(V(4, 0), true));                     // 재사용 가능
        }

        [Test]
        public void Remove_OutOfBounds_ReturnsFalse_NoCrash_NoEvent()
        {
            var c = Cfg(0.25f);
            c.GridWidth = 5; c.GridHeight = 2;
            var hub = new SimEventHub();
            int placed = 0;
            hub.Placed += _ => placed++;
            var e = new SimEngine(c, hub);

            Assert.IsFalse(e.Remove(V(-1, 0)));
            Assert.IsFalse(e.Remove(V(0, 99)));
            e.Tick(0.25f);
            Assert.AreEqual(0, placed);
        }
    }
}

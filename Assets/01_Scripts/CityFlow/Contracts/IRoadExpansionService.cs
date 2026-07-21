namespace CityFlow.Contracts
{
    // 도로 확장권(스펙 2026-07-17 §2단계, 기획 결정 환): "+10칸"을 코인으로 구매.
    // UI는 services.Placement as IRoadExpansionService 다운캐스트로 접근(IIntersectionFacilityService 선례).
    // 소유권 경계: 코인 차감은 항상 경제 레이어(IEconomyService.TrySpend) 안에서 일어나고,
    // Sim은 성공 통지를 받아 캡만 올린다 — Sim이 잔고를 직접 만지지 않는다.
    public interface IRoadExpansionService
    {
        // 누적 구매횟수(세이브 영속). 유효 캡 = SimConfig.MaxRoadTiles + 구매횟수 × 10.
        int RoadCapacityPurchases { get; }

        // 다음 확장권 가격 = RoadExpandBaseCost × RoadExpandCostGrowth^구매횟수 (반올림 정수).
        long NextRoadExpandCost { get; }

        // 구매 한 번: economy.TrySpend(NextRoadExpandCost) 성공 시에만 AddRoadCapacity. 실패 시 무변화.
        bool TryPurchaseRoadExpansion(IEconomyService economy);

        // 캡 +10(코인 경로 밖 순수 mutator — 세이브 복원·치트·테스트용).
        void AddRoadCapacity();
    }
}

using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content
{
    /// <summary>
    /// 1차 코어에서 사용하는 배치물 가격을 관리합니다.
    ///
    /// 팀원 UI 및 배치 코드를 직접 수정하지 않고,
    /// 경제 담당 코드에서 가격 기준을 한곳에 보관하기 위한 설정입니다.
    /// </summary>
    [CreateAssetMenu(
        fileName = "PlacementPriceConfig",
        menuName = "CityFlow/Content/Placement Price Config"
    )]
    public sealed class PlacementPriceConfigSO :
        ScriptableObject
    {
        [Header("배치물 종류")]

        [Tooltip(
            "신호에 해당하는 TileType입니다. " +
            "프로젝트의 실제 enum 값을 Inspector에서 선택합니다."
        )]
        [SerializeField]
        private TileType signalType;

        [Tooltip(
            "로터리에 해당하는 TileType입니다."
        )]
        [SerializeField]
        private TileType roundaboutType;

        [Tooltip(
            "입체 교차로에 해당하는 TileType입니다."
        )]
        [SerializeField]
        private TileType overpassType;

        [Header("1차 코어 가격")]

        [Tooltip("신호 1개 설치 비용입니다.")]
        [Min(0)]
        [SerializeField]
        private int signalBaseCost = 50;

        [Tooltip(
            "로터리 가격 배율입니다. " +
            "180은 신호 가격의 1.8배입니다."
        )]
        [Min(0)]
        [SerializeField]
        private int roundaboutCostPercent = 180;

        [Tooltip("입체 교차로 1개 설치 비용입니다.")]
        [Min(0)]
        [SerializeField]
        private int overpassBaseCost = 2500;

        public TileType SignalType =>
            signalType;

        public TileType RoundaboutType =>
            roundaboutType;

        public TileType OverpassType =>
            overpassType;

        public int SignalBaseCost =>
            signalBaseCost;

        public int RoundaboutCostPercent =>
            roundaboutCostPercent;

        public int OverpassBaseCost =>
            overpassBaseCost;

        /// <summary>
        /// 신호 가격을 기준으로 로터리 가격을 계산합니다.
        ///
        /// 기본 설정:
        /// 50 × 180% = 90코인
        /// </summary>
        public int GetRoundaboutCost()
        {
            if (signalBaseCost <= 0 ||
                roundaboutCostPercent <= 0)
            {
                return 0;
            }

            long calculatedCost =
                (long)signalBaseCost *
                roundaboutCostPercent /
                100L;

            return calculatedCost >
                   int.MaxValue
                ? int.MaxValue
                : (int)calculatedCost;
        }

        /// <summary>
        /// TileType에 해당하는 배치 비용을 반환합니다.
        ///
        /// 현재 가격 설정에 등록되지 않은 타입은
        /// false와 비용 0을 반환합니다.
        /// </summary>
        public bool TryGetPlacementCost(
            TileType tileType,
            out int cost
        )
        {
            if (tileType == signalType)
            {
                cost = signalBaseCost;
                return true;
            }

            if (tileType == roundaboutType)
            {
                cost = GetRoundaboutCost();
                return true;
            }

            if (tileType == overpassType)
            {
                cost = overpassBaseCost;
                return true;
            }

            cost = 0;
            return false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            signalBaseCost =
                Mathf.Max(
                    0,
                    signalBaseCost
                );

            roundaboutCostPercent =
                Mathf.Max(
                    0,
                    roundaboutCostPercent
                );

            overpassBaseCost =
                Mathf.Max(
                    0,
                    overpassBaseCost
                );

            if (signalType == roundaboutType ||
                signalType == overpassType ||
                roundaboutType == overpassType)
            {
                Debug.LogWarning(
                    "[PlacementPriceConfigSO] " +
                    "신호, 로터리, 입체 교차로의 " +
                    "TileType 설정이 서로 중복되어 있습니다.",
                    this
                );
            }
        }
#endif
    }
}
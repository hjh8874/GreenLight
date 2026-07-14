using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityFlow.UI
{
    /// <summary>
    /// 정식 상점 UI가 연결될 때까지 교통 시설 구매와 배치를 검증하기 위한 임시 컨트롤러입니다.
    /// </summary>
    [DefaultExecutionOrder(1100)]
    public sealed class TemporaryTrafficFacilityControls : MonoBehaviour, ICityFlowServiceConsumer
    {
        private const float TileSize = GridUtil.TileSize;

        private static readonly Vector2Int[] OnewayRotationOrder =
        {
            new Vector2Int(1, 0),
            new Vector2Int(0, -1),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
        };

        [Header("Temporary Prices")]
        [Min(0)] [SerializeField] private long signalCost = 100;
        [Min(0)] [SerializeField] private long roundaboutCost = 250;
        [Min(0)] [SerializeField] private long overpassCost = 400;
        [Min(0)] [SerializeField] private long onewayCost = 50;
        [Min(0)] [SerializeField] private long turnSignCost = 75;

        [Header("Signal")]
        [Min(1)] [SerializeField] private int signalGreenSlots = 8;

        private CityFlowServices services;
        private IReadOnlyTileData tileData;
        private IIntersectionFacilityService facility;
        private ITrafficRuleService trafficRule;
        private IEconomyService economy;
        private bool ready;
        private string lastResult = "대기 중";

        public void Initialize(CityFlowServices cityFlowServices)
        {
            if (!isActiveAndEnabled || !Debug.isDebugBuild)
            {
                return;
            }

            services = cityFlowServices;
            tileData = services?.TileData;
            facility = services?.Placement as IIntersectionFacilityService;
            trafficRule = services?.Placement as ITrafficRuleService;
            economy = services?.Economy;

            if (services != null)
            {
                services.EconomyRegistered += OnEconomyRegistered;
            }

            ready = tileData != null && facility != null && trafficRule != null;

            if (!ready)
            {
                Debug.LogWarning(
                    "[TemporaryTrafficFacilityControls] 교통 시설 배치 서비스 또는 타일 데이터를 찾지 못했습니다."
                );
            }
        }

        private void OnDestroy()
        {
            if (services != null)
            {
                services.EconomyRegistered -= OnEconomyRegistered;
            }
        }

        private void OnEconomyRegistered(IEconomyService registeredEconomy)
        {
            economy = registeredEconomy;
        }

        private void Update()
        {
            if (!ready || Keyboard.current == null || !TryGetHoverTile(out Vector2Int hover))
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;

            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                lastResult = TryPurchaseAndPlace(
                    "신호등",
                    hover,
                    signalCost,
                    () => facility.CanPlaceSignal(hover),
                    () => facility.TryPlaceSignal(hover, signalGreenSlots)
                );
            }
            else if (keyboard.digit2Key.wasPressedThisFrame)
            {
                lastResult = TryPurchaseAndPlace(
                    "로터리",
                    hover,
                    roundaboutCost,
                    () => facility.CanPlaceRoundabout(hover),
                    () => facility.TryPlaceRoundabout(hover)
                );
            }
            else if (keyboard.digit3Key.wasPressedThisFrame)
            {
                lastResult = TryPurchaseAndPlace(
                    "입체교차",
                    hover,
                    overpassCost,
                    () => facility.CanPlaceOverpass(hover),
                    () => facility.TryPlaceOverpass(hover)
                );
            }
            else if (keyboard.digit4Key.wasPressedThisFrame)
            {
                lastResult = TryPlaceOrRotateOneway(hover);
            }
            else if (keyboard.digit5Key.wasPressedThisFrame)
            {
                lastResult = TryPlaceOrRotateTurnSign(hover);
            }
            else if (keyboard.digit0Key.wasPressedThisFrame)
            {
                lastResult = TryRemoveAny(hover);
            }
        }

        private string TryPurchaseAndPlace(
            string label,
            Vector2Int tile,
            long cost,
            Func<bool> canPlace,
            Func<bool> place)
        {
            if (!canPlace())
            {
                return $"{label} 배치 불가 {tile}";
            }

            if (!TryPay(cost, label))
            {
                return economy == null
                    ? $"{label} 구매 불가: 경제 서비스 없음"
                    : $"{label} 구매 불가: 코인 부족 ({Math.Max(0L, cost)} 필요)";
            }

            if (place())
            {
                return $"{label} 배치 성공 {tile} (-{Math.Max(0L, cost)} 코인)";
            }

            Refund(cost, label);
            return $"{label} 배치 실패 {tile}: 결제 취소";
        }

        private bool TryPay(long cost, string label)
        {
            long safeCost = Math.Max(0L, cost);
            if (safeCost == 0L)
            {
                return true;
            }

            if (economy == null)
            {
                return false;
            }

            bool paid = economy.TrySpend(safeCost);
            if (!paid)
            {
                Debug.Log($"[TemporaryTrafficFacilityControls] {label} 구매 실패: 코인 부족");
            }

            return paid;
        }

        private void Refund(long cost, string label)
        {
            long safeCost = Math.Max(0L, cost);
            if (safeCost > 0L && economy != null)
            {
                economy.AddCoins(safeCost, $"temporary {label} placement rollback");
            }
        }

        private string TryPlaceOrRotateOneway(Vector2Int tile)
        {
            Vector2Int existing = trafficRule.GetOnewayDir(tile);
            if (existing != Vector2Int.zero)
            {
                Vector2Int next = NextOnewayDir(existing);
                trafficRule.TryRemoveOneway(tile);

                if (trafficRule.TryPlaceOneway(tile, next))
                {
                    return $"일방통행 방향 변경 {tile} → {DirectionLabel(next)}";
                }

                trafficRule.TryPlaceOneway(tile, existing);
                return $"일방통행 방향 변경 실패 {tile}";
            }

            Vector2Int first = OnewayRotationOrder[0];
            return TryPurchaseAndPlace(
                "일방통행",
                tile,
                onewayCost,
                () => trafficRule.CanPlaceOneway(tile),
                () => trafficRule.TryPlaceOneway(tile, first)
            );
        }

        private string TryPlaceOrRotateTurnSign(Vector2Int tile)
        {
            TurnMode? existing = trafficRule.GetTurnMode(tile);
            if (existing.HasValue)
            {
                TurnMode next = existing.Value == TurnMode.LeftOnly
                    ? TurnMode.RightOnly
                    : TurnMode.LeftOnly;

                trafficRule.TryRemoveTurnSign(tile);

                if (trafficRule.TryPlaceTurnSign(tile, next))
                {
                    return $"회전 표지 방향 변경 {tile} → {TurnLabel(next)}";
                }

                trafficRule.TryPlaceTurnSign(tile, existing.Value);
                return $"회전 표지 방향 변경 실패 {tile}";
            }

            return TryPurchaseAndPlace(
                "회전 표지",
                tile,
                turnSignCost,
                () => trafficRule.CanPlaceTurnSign(tile),
                () => trafficRule.TryPlaceTurnSign(tile, TurnMode.LeftOnly)
            );
        }

        private string TryRemoveAny(Vector2Int tile)
        {
            if (facility.TryRemoveSignal(tile))
            {
                return $"신호등 제거 성공 {tile} (환불 없음)";
            }

            if (facility.TryRemoveRoundabout(tile))
            {
                return $"로터리 제거 성공 {tile} (환불 없음)";
            }

            if (facility.TryRemoveOverpass(tile))
            {
                return $"입체교차 제거 성공 {tile} (환불 없음)";
            }

            if (trafficRule.TryRemoveOneway(tile))
            {
                return $"일방통행 제거 성공 {tile} (환불 없음)";
            }

            if (trafficRule.TryRemoveTurnSign(tile))
            {
                return $"회전 표지 제거 성공 {tile} (환불 없음)";
            }

            return $"제거할 교통 시설 없음 {tile}";
        }

        private static Vector2Int NextOnewayDir(Vector2Int current)
        {
            int index = Array.IndexOf(OnewayRotationOrder, current);
            return OnewayRotationOrder[(index + 1) % OnewayRotationOrder.Length];
        }

        private static string DirectionLabel(Vector2Int direction)
        {
            if (direction == Vector2Int.right) return "동";
            if (direction == Vector2Int.left) return "서";
            if (direction == Vector2Int.up) return "북";
            if (direction == Vector2Int.down) return "남";
            return "없음";
        }

        private static string TurnLabel(TurnMode mode)
        {
            return mode == TurnMode.LeftOnly ? "좌회전" : "우회전";
        }

        private bool TryGetHoverTile(out Vector2Int tile)
        {
            tile = default;
            if (Mouse.current == null || Camera.main == null)
            {
                return false;
            }

            Vector3 world = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            tile = new Vector2Int(
                Mathf.FloorToInt(world.x / TileSize),
                Mathf.FloorToInt(world.y / TileSize)
            );
            return true;
        }

        private string GetDeviceLabel(Vector2Int tile)
        {
            string label = Contains(facility.SignalTiles, tile) ? "신호등"
                : Contains(facility.RoundaboutTiles, tile) ? "로터리"
                : Contains(facility.OverpassTiles, tile) ? "입체교차"
                : trafficRule.GetOnewayDir(tile) != Vector2Int.zero
                    ? $"일방통행({DirectionLabel(trafficRule.GetOnewayDir(tile))})"
                    : "없음";

            TurnMode? turnMode = trafficRule.GetTurnMode(tile);
            if (turnMode.HasValue)
            {
                label = label == "없음"
                    ? $"회전 표지({TurnLabel(turnMode.Value)})"
                    : $"{label} + 회전 표지({TurnLabel(turnMode.Value)})";
            }

            return label;
        }

        private static bool Contains(IReadOnlyList<Vector2Int> tiles, Vector2Int tile)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                if (tiles[i] == tile)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnGUI()
        {
            if (!ready)
            {
                return;
            }

            GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            style.normal.textColor = Color.white;

            GUI.Box(new Rect(8, 92, 970, 116), GUIContent.none);
            GUI.Label(
                new Rect(16, 96, 950, 26),
                $"임시 교통 상점 | 1 신호등({signalCost})  2 로터리({roundaboutCost})  " +
                $"3 입체교차({overpassCost})  4 일방통행({onewayCost})  5 회전 표지({turnSignCost})  0 제거",
                style
            );

            if (TryGetHoverTile(out Vector2Int hover))
            {
                GUI.Label(
                    new Rect(16, 124, 950, 26),
                    $"선택 {hover} | 타일 {tileData.GetTileType(hover)} | 시설 {GetDeviceLabel(hover)}",
                    style
                );
            }

            string coins = economy == null ? "연결 안 됨" : economy.Coins.ToString();
            GUI.Label(new Rect(16, 152, 950, 26), $"코인 {coins} | 결과: {lastResult}", style);
            GUI.Label(new Rect(16, 180, 950, 26), "같은 4/5 키를 다시 누르면 방향 변경 | 제거 시 임시 환불 없음", style);
        }
    }
}

using CityFlow.Contracts;
using CityFlow.UI.Data;
using UnityEngine;

namespace CityFlow.UI.Controllers
{
    public interface IUndoCommand
    {
        void ExecuteUndo(IEconomyService economy, IIntersectionFacilityService facility, ITrafficRuleService rule);
    }

    public class InfrastructureUndoCommand : IUndoCommand
    {
        private readonly InfrastructureKind _kind;
        private readonly Vector2Int _coord;
        private readonly long _cost;

        public InfrastructureUndoCommand(InfrastructureKind kind, Vector2Int coord, long cost)
        {
            _kind = kind;
            _coord = coord;
            _cost = cost;
        }

        public void ExecuteUndo(IEconomyService economy, IIntersectionFacilityService facility, ITrafficRuleService rule)
        {
            if (facility == null || rule == null) return;

            bool removed = false;
            switch (_kind)
            {
                case InfrastructureKind.Signal:
                    removed = facility.TryRemoveSignal(_coord);
                    break;
                case InfrastructureKind.Roundabout:
                    removed = facility.TryRemoveRoundabout(_coord);
                    break;
                case InfrastructureKind.Overpass:
                    removed = facility.TryRemoveOverpass(_coord);
                    break;
                case InfrastructureKind.Oneway:
                    removed = rule.TryRemoveOneway(_coord);
                    break;
                case InfrastructureKind.TurnRestriction:
                    removed = rule.TryRemoveTurnSign(_coord);
                    break;
            }

            if (removed && economy != null && _cost > 0)
            {
                // 100% Full Refund for Undo
                economy.AddCoins(_cost, "Undo Infrastructure 100% Refund");
                Debug.Log($"[InfrastructureUndo] Undid {_kind} at {_coord}. Refunded {_cost} coins.");
            }
        }
    }
}

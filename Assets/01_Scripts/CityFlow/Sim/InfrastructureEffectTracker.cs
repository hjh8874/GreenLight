using System.Collections.Generic;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim
{
    /// <summary>
    /// Holds infrastructure installations until two completed day boundaries
    /// have supplied a full post-installation comparison day.
    /// </summary>
    public sealed class InfrastructureEffectTracker
    {
        private const int Radius = 3;
        private const int Capacity = 16;

        private sealed class Pending
        {
            public Vector2Int Tile;
            public float BeforeRatio01;
            public int Boundaries;
        }

        private readonly List<Pending> _pending = new List<Pending>(Capacity);
        private readonly CongestionLedger _ledger;

        public InfrastructureEffectTracker(CongestionLedger ledger = null)
        {
            _ledger = ledger;
        }

        public void OnPlaced(Vector2Int tile, CongestionLedger ledger = null)
        {
            Pending existing = null;
            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].Tile == tile)
                {
                    existing = _pending[i];
                    break;
                }
            }

            float baseline = ledger == null
                ? 0f
                : ledger.AverageLastDayJamRatio01(tile, Radius);
            if (existing != null)
            {
                existing.BeforeRatio01 = baseline;
                existing.Boundaries = 0;
                return;
            }

            if (_pending.Count >= Capacity)
            {
                _pending.RemoveAt(0);
            }
            _pending.Add(new Pending
            {
                Tile = tile,
                BeforeRatio01 = baseline,
                Boundaries = 0
            });
        }

        public void OnPlaced(Vector2Int tile)
        {
            OnPlaced(tile, _ledger);
        }

        public void OnRemoved(Vector2Int tile)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].Tile == tile)
                {
                    _pending.RemoveAt(i);
                }
            }
        }

        public void ClearPending()
        {
            _pending.Clear();
        }

        public List<InfrastructureEffectEvent> EvaluateOnDayWrap(CongestionLedger ledger)
        {
            var effects = new List<InfrastructureEffectEvent>();
            if (ledger == null) return effects;

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending pending = _pending[i];
                pending.Boundaries++;
                if (pending.Boundaries < 2) continue;

                effects.Add(new InfrastructureEffectEvent(
                    pending.Tile,
                    pending.BeforeRatio01,
                    ledger.AverageLastDayJamRatio01(pending.Tile, Radius)));
                _pending.RemoveAt(i);
            }

            effects.Reverse();
            return effects;
        }
    }
}

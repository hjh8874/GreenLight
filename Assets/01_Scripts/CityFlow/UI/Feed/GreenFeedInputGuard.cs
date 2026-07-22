using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.UI.Feed
{
    public static class GreenFeedInputGuard
    {
        private static readonly HashSet<EntityId> PointerOwners = new HashSet<EntityId>();

        public static bool IsPointerCaptured => PointerOwners.Count > 0;

        public static void SetPointerCaptured(Object owner, bool isCaptured)
        {
            if (owner == null)
            {
                return;
            }

            if (isCaptured)
            {
                PointerOwners.Add(owner.GetEntityId());
                return;
            }

            PointerOwners.Remove(owner.GetEntityId());
        }

        public static void Release(Object owner)
        {
            SetPointerCaptured(owner, false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            PointerOwners.Clear();
        }

        // Unity setup: GreenFeedHoverRelay updates this guard automatically for baked feed UI.
    }
}

using UnityEngine;

namespace CityFlow.View
{
    internal enum VehicleViewRecoveryReason
    {
        None = 0,
        StalledBehindAuthority = 1,
        StopPresentationTimeout = 2
    }

    internal sealed class VehicleViewRecoveryMonitor
    {
        private bool hasSample;
        private int routeVersion;
        private float lastDistance;
        private float stalledSeconds;
        private float stopPresentationSeconds;
        private float cooldownSeconds;

        public VehicleViewRecoveryReason Observe(
            float currentDistance,
            float authoritativeDistance,
            float tileSize,
            float unscaledDeltaTime,
            int currentRouteVersion,
            bool eligible,
            bool stopPresentationPending,
            VehicleViewRecoveryProfileSO profile)
        {
            float safeDeltaTime = Mathf.Max(0f, unscaledDeltaTime);
            cooldownSeconds = Mathf.Max(
                0f,
                cooldownSeconds - safeDeltaTime);

            if (!hasSample || routeVersion != currentRouteVersion)
            {
                BeginRoute(currentDistance, currentRouteVersion);
                return VehicleViewRecoveryReason.None;
            }

            float safeTileSize = Mathf.Max(0.0001f, tileSize);
            float minimumProgress = ResolveMinimumProgressTiles(profile) *
                                    safeTileSize;
            bool progressed = currentDistance >=
                              lastDistance + minimumProgress;
            float debt = authoritativeDistance - currentDistance;
            bool hasMeaningfulDebt = debt >=
                                     ResolveMinimumDebtTiles(profile) *
                                     safeTileSize;

            if (!eligible || !hasMeaningfulDebt)
            {
                stalledSeconds = 0f;
                lastDistance = currentDistance;
            }
            else if (progressed)
            {
                stalledSeconds = 0f;
                lastDistance = currentDistance;
            }
            else
            {
                stalledSeconds += safeDeltaTime;
            }

            if (eligible && stopPresentationPending)
            {
                stopPresentationSeconds += safeDeltaTime;
            }
            else
            {
                stopPresentationSeconds = 0f;
            }

            if (cooldownSeconds > 0f)
            {
                return VehicleViewRecoveryReason.None;
            }

            if (stopPresentationSeconds >=
                ResolveStopPresentationTimeoutSeconds(profile))
            {
                return Trigger(
                    VehicleViewRecoveryReason.StopPresentationTimeout,
                    profile);
            }

            if (stalledSeconds >= ResolveRecoveryDelaySeconds(profile))
            {
                return Trigger(
                    VehicleViewRecoveryReason.StalledBehindAuthority,
                    profile);
            }

            return VehicleViewRecoveryReason.None;
        }

        public void Synchronize(float distance, int currentRouteVersion)
        {
            hasSample = true;
            routeVersion = currentRouteVersion;
            lastDistance = distance;
            stalledSeconds = 0f;
            stopPresentationSeconds = 0f;
        }

        public void Reset()
        {
            hasSample = false;
            routeVersion = 0;
            lastDistance = 0f;
            stalledSeconds = 0f;
            stopPresentationSeconds = 0f;
            cooldownSeconds = 0f;
        }

        private void BeginRoute(float distance, int currentRouteVersion)
        {
            hasSample = true;
            routeVersion = currentRouteVersion;
            lastDistance = distance;
            stalledSeconds = 0f;
            stopPresentationSeconds = 0f;
        }

        private VehicleViewRecoveryReason Trigger(
            VehicleViewRecoveryReason reason,
            VehicleViewRecoveryProfileSO profile)
        {
            stalledSeconds = 0f;
            stopPresentationSeconds = 0f;
            cooldownSeconds = ResolveRecoveryCooldownSeconds(profile);
            return reason;
        }

        private static float ResolveMinimumDebtTiles(
            VehicleViewRecoveryProfileSO profile) =>
            profile != null
                ? profile.MinimumDebtTiles
                : VehicleViewRecoveryProfileSO.DefaultMinimumDebtTiles;

        private static float ResolveMinimumProgressTiles(
            VehicleViewRecoveryProfileSO profile) =>
            profile != null
                ? profile.MinimumProgressTiles
                : VehicleViewRecoveryProfileSO.DefaultMinimumProgressTiles;

        private static float ResolveRecoveryDelaySeconds(
            VehicleViewRecoveryProfileSO profile) =>
            profile != null
                ? profile.RecoveryDelaySeconds
                : VehicleViewRecoveryProfileSO.DefaultRecoveryDelaySeconds;

        private static float ResolveStopPresentationTimeoutSeconds(
            VehicleViewRecoveryProfileSO profile) =>
            profile != null
                ? profile.StopPresentationTimeoutSeconds
                : VehicleViewRecoveryProfileSO
                    .DefaultStopPresentationTimeoutSeconds;

        private static float ResolveRecoveryCooldownSeconds(
            VehicleViewRecoveryProfileSO profile) =>
            profile != null
                ? profile.RecoveryCooldownSeconds
                : VehicleViewRecoveryProfileSO
                    .DefaultRecoveryCooldownSeconds;

        // Unity setup: this is a pure runtime helper owned by each vehicle view.
    }
}

using UnityEngine;

namespace CityFlow.View
{
    [CreateAssetMenu(
        fileName = "VehicleViewRecoveryProfile",
        menuName = "CityFlow/Traffic/Vehicle View Recovery Profile")]
    public sealed class VehicleViewRecoveryProfileSO : ScriptableObject
    {
        public const float DefaultMinimumDebtTiles = 0.75f;
        public const float DefaultMinimumProgressTiles = 0.02f;
        public const float DefaultRecoveryDelaySeconds = 2f;
        public const float DefaultStopPresentationTimeoutSeconds = 1.5f;
        public const float DefaultRecoveryCooldownSeconds = 2f;

        [SerializeField, Min(0.1f)]
        private float minimumDebtTiles = DefaultMinimumDebtTiles;

        [SerializeField, Min(0.001f)]
        private float minimumProgressTiles = DefaultMinimumProgressTiles;

        [SerializeField, Min(0.1f)]
        private float recoveryDelaySeconds = DefaultRecoveryDelaySeconds;

        [SerializeField, Min(0.1f)]
        private float stopPresentationTimeoutSeconds =
            DefaultStopPresentationTimeoutSeconds;

        [SerializeField, Min(0f)]
        private float recoveryCooldownSeconds =
            DefaultRecoveryCooldownSeconds;

        public float MinimumDebtTiles =>
            Mathf.Max(0.1f, minimumDebtTiles);
        public float MinimumProgressTiles =>
            Mathf.Max(0.001f, minimumProgressTiles);
        public float RecoveryDelaySeconds =>
            Mathf.Max(0.1f, recoveryDelaySeconds);
        public float StopPresentationTimeoutSeconds =>
            Mathf.Max(0.1f, stopPresentationTimeoutSeconds);
        public float RecoveryCooldownSeconds =>
            Mathf.Max(0f, recoveryCooldownSeconds);

#if UNITY_EDITOR
        private void OnValidate()
        {
            minimumDebtTiles = Mathf.Max(0.1f, minimumDebtTiles);
            minimumProgressTiles = Mathf.Max(
                0.001f,
                minimumProgressTiles);
            recoveryDelaySeconds = Mathf.Max(
                0.1f,
                recoveryDelaySeconds);
            stopPresentationTimeoutSeconds = Mathf.Max(
                0.1f,
                stopPresentationTimeoutSeconds);
            recoveryCooldownSeconds = Mathf.Max(
                0f,
                recoveryCooldownSeconds);
        }
#endif

        // Unity setup: keep the configured asset at
        // Resources/CityFlow/VehicleViewRecoveryProfile.
    }
}

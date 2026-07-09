using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace GreenLight.Radio.Runtime
{
    [RequireComponent(typeof(RadioService))]
    public sealed class RadioUnlockService : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("Flow Burst Unlock")]
        [SerializeField] private bool unlockOnFirstFlowBurst = true;
        [SerializeField] private int flowBurstUnlockSlotIndex;

        private CityFlowServices services;
        private RadioService radioService;
        private bool hasUnlockedFlowBurstSlot;

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            this.services = services;
            radioService = GetComponent<RadioService>();

            if (radioService == null)
            {
                Debug.LogWarning("[RadioUnlockService] RadioService is missing.");
                return;
            }

            services.Events.FlowBurst += OnFlowBurst;
            Debug.Log("[RadioUnlockService] Radio unlock service initialized.");
        }

        private void OnDestroy()
        {
            if (services?.Events != null)
            {
                services.Events.FlowBurst -= OnFlowBurst;
            }
        }

        private void OnFlowBurst(FlowBurstEvent e)
        {
            if (!unlockOnFirstFlowBurst || hasUnlockedFlowBurstSlot)
            {
                return;
            }

            hasUnlockedFlowBurstSlot = radioService.TryUnlockSlot(flowBurstUnlockSlotIndex);

            if (hasUnlockedFlowBurstSlot)
            {
                Debug.Log($"[RadioUnlockService] Flow Burst unlocked radio slot {flowBurstUnlockSlotIndex}.");
            }
        }

        // Unity setup: Attach this with RadioService. Slot index and unlock rule can be tuned in the Inspector.
    }
}

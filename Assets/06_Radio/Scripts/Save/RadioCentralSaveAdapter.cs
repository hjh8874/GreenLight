using System;
using GreenLight.Radio.Runtime;
using CentralRadioSaveData = CityFlow.Contracts.Save.RadioSaveData;
using CentralRadioSlotSaveData = CityFlow.Contracts.Save.RadioSlotSaveData;

namespace GreenLight.Radio.Save
{
    public sealed class RadioCentralSaveAdapter :
        CityFlow.Contracts.Save.IRadioSaveSource
    {
        private readonly RadioService radioService;

        public RadioCentralSaveAdapter(RadioService radioService)
        {
            this.radioService = radioService;
        }

        public CentralRadioSaveData CreateSnapshot()
        {
            RadioSaveData radioSnapshot = radioService.CreateSnapshot();
            RadioSlotSaveData[] sourceSlots =
                radioSnapshot?.Slots ?? Array.Empty<RadioSlotSaveData>();
            var slots = new CentralRadioSlotSaveData[sourceSlots.Length];

            for (int i = 0; i < sourceSlots.Length; i++)
            {
                RadioSlotSaveData source = sourceSlots[i];

                if (source == null)
                {
                    continue;
                }

                slots[i] = new CentralRadioSlotSaveData
                {
                    SlotIndex = source.SlotIndex,
                    IsUnlocked = source.IsUnlocked,
                    SourceType = source.SourceType,
                    YoutubeVideoId = source.YoutubeVideoId,
                    DisplayName = source.DisplayName,
                    ThemeId = source.ThemeId
                };
            }

            return new CentralRadioSaveData
            {
                Slots = slots,
                CurrentSlotIndex = radioService.CurrentSlotIndex
            };
        }

        public void RestoreSnapshot(CentralRadioSaveData snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            CentralRadioSlotSaveData[] sourceSlots =
                snapshot.Slots ?? Array.Empty<CentralRadioSlotSaveData>();
            var slots = new RadioSlotSaveData[sourceSlots.Length];

            for (int i = 0; i < sourceSlots.Length; i++)
            {
                CentralRadioSlotSaveData source = sourceSlots[i];

                if (source == null)
                {
                    continue;
                }

                slots[i] = new RadioSlotSaveData
                {
                    SlotIndex = source.SlotIndex,
                    IsUnlocked = source.IsUnlocked,
                    SourceType = source.SourceType,
                    YoutubeVideoId = source.YoutubeVideoId,
                    DisplayName = source.DisplayName,
                    ThemeId = source.ThemeId
                };
            }

            radioService.RestoreSnapshotSilently(new RadioSaveData
            {
                Slots = slots
            });

            if (snapshot.CurrentSlotIndex >= 0)
            {
                radioService.TrySelectSlot(snapshot.CurrentSlotIndex);
            }
        }
    }
}

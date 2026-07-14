using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using GreenLight.Radio.Data;
using GreenLight.Radio.Save;
using UnityEngine;

namespace GreenLight.Radio.Runtime
{
    public sealed class RadioService :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        IRadioSaveSource
    {
        [SerializeField] private int initialSlotCount = 3;
        [SerializeField] private List<RadioSlotData> slots = new();

        private RadioCentralSaveAdapter centralSaveAdapter;

        public IReadOnlyList<RadioSlotData> Slots => slots;
        public int CurrentSlotIndex { get; private set; } = -1;
        public RadioSlotData CurrentSlot { get; private set; }

        public event Action<RadioSlotData> SlotUnlocked;
        public event Action<RadioSlotData> SlotRegistered;
        public event Action<RadioSlotData> CurrentSlotChanged;
        public event Action<RadioPlaybackRequest> PlayRequested;
        public event Action PauseRequested;

        private void Awake()
        {
            EnsureSlotCount(initialSlotCount);
        }

        public void Initialize(CityFlowServices services)
        {
            if (services == null)
            {
                return;
            }

            EnsureSlotCount(initialSlotCount);
            centralSaveAdapter ??= new RadioCentralSaveAdapter(this);
            services.RegisterRadioSaveSource(centralSaveAdapter);
            Debug.Log("[RadioService] Radio save source registered.");
        }

        public bool TryUnlockSlot(int slotIndex)
        {
            if (!TryGetSlot(slotIndex, out RadioSlotData slot))
            {
                return false;
            }

            if (slot.IsUnlocked)
            {
                return true;
            }

            slot.Unlock();
            SlotUnlocked?.Invoke(slot);
            Debug.Log($"[RadioService] Radio slot {slotIndex} unlocked.");
            return true;
        }

        public bool TryRegisterYouTubeLink(int slotIndex, string youtubeUrlOrVideoId, string displayName, string themeId = "")
        {
            if (!TryGetSlot(slotIndex, out RadioSlotData slot))
            {
                return false;
            }

            if (!slot.TrySetYouTubeVideo(youtubeUrlOrVideoId, displayName, themeId))
            {
                return false;
            }

            SlotRegistered?.Invoke(slot);
            Debug.Log($"[RadioService] Registered YouTube video '{slot.YoutubeVideoId}' to radio slot {slotIndex}.");
            return true;
        }

        public bool TrySelectSlot(int slotIndex)
        {
            if (!TryGetSlot(slotIndex, out RadioSlotData slot))
            {
                return false;
            }

            if (!slot.IsUnlocked)
            {
                Debug.LogWarning($"[RadioService] Cannot select locked radio slot {slotIndex}.");
                return false;
            }

            CurrentSlotIndex = slotIndex;
            CurrentSlot = slot;
            CurrentSlotChanged?.Invoke(slot);
            return true;
        }

        public bool RequestPlaySelected()
        {
            if (CurrentSlot == null)
            {
                Debug.LogWarning("[RadioService] Play request skipped because no radio slot is selected.");
                return false;
            }

            return RequestPlay(CurrentSlot);
        }

        public bool RequestPlaySlot(int slotIndex)
        {
            if (!TrySelectSlot(slotIndex))
            {
                return false;
            }

            return RequestPlaySelected();
        }

        public void RequestPause()
        {
            PauseRequested?.Invoke();
        }

        public RadioSaveData CreateSnapshot()
        {
            RadioSlotSaveData[] slotSnapshots = new RadioSlotSaveData[slots.Count];

            for (int i = 0; i < slots.Count; i++)
            {
                RadioSlotData slot = slots[i];

                slotSnapshots[i] = new RadioSlotSaveData
                {
                    SlotIndex = slot?.SlotIndex ?? i,
                    IsUnlocked = slot?.IsUnlocked ?? false,
                    SourceType = slot?.SourceType.ToString() ?? RadioSourceType.None.ToString(),
                    YoutubeVideoId = slot?.YoutubeVideoId ?? string.Empty,
                    DisplayName = slot?.DisplayName ?? string.Empty,
                    ThemeId = slot?.ThemeId ?? string.Empty
                };
            }

            return new RadioSaveData
            {
                Slots = slotSnapshots
            };
        }

        public void RestoreSnapshot(RadioSaveData snapshot)
        {
            RestoreSnapshot(snapshot, true);
        }

        public void RestoreSnapshotSilently(RadioSaveData snapshot)
        {
            RestoreSnapshot(snapshot, false);
        }

        private void RestoreSnapshot(
            RadioSaveData snapshot,
            bool notifyListeners)
        {
            if (snapshot?.Slots == null)
            {
                return;
            }

            EnsureSlotCount(snapshot.Slots.Length);
            ResetSlotState();

            for (int i = 0; i < snapshot.Slots.Length; i++)
            {
                RadioSlotSaveData savedSlot = snapshot.Slots[i];

                if (savedSlot == null)
                {
                    continue;
                }

                if (!TryGetSlot(savedSlot.SlotIndex, out RadioSlotData slot))
                {
                    continue;
                }

                slot.Restore(
                    savedSlot.IsUnlocked,
                    ParseSourceType(savedSlot.SourceType),
                    savedSlot.YoutubeVideoId,
                    savedSlot.DisplayName,
                    savedSlot.ThemeId);

                if (notifyListeners && slot.IsUnlocked)
                {
                    SlotUnlocked?.Invoke(slot);
                }

                if (notifyListeners && slot.SourceType != RadioSourceType.None)
                {
                    SlotRegistered?.Invoke(slot);
                }
            }

            Debug.Log("[RadioService] Radio slots restored.");
        }

        public bool TryGetSlot(int slotIndex, out RadioSlotData slot)
        {
            slot = null;

            if (slotIndex < 0 || slotIndex >= slots.Count)
            {
                return false;
            }

            slot = slots[slotIndex];
            return slot != null;
        }

        private bool RequestPlay(RadioSlotData slot)
        {
            if (!CanPlay(slot))
            {
                return false;
            }

            PlayRequested?.Invoke(new RadioPlaybackRequest(slot));
            return true;
        }

        private static bool CanPlay(RadioSlotData slot)
        {
            if (slot == null || !slot.IsUnlocked)
            {
                return false;
            }

            if (slot.SourceType != RadioSourceType.YouTube)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(slot.YoutubeVideoId);
        }

        private void EnsureSlotCount(int count)
        {
            int safeCount = Mathf.Max(0, count);

            for (int i = slots.Count; i < safeCount; i++)
            {
                slots.Add(new RadioSlotData(i));
            }

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] == null)
                {
                    slots[i] = new RadioSlotData(i);
                }
            }
        }

        private void ResetSlotState()
        {
            for (int i = 0; i < slots.Count; i++)
            {
                slots[i]?.Restore(
                    false,
                    RadioSourceType.None,
                    string.Empty,
                    string.Empty,
                    string.Empty);
            }

            CurrentSlotIndex = -1;
            CurrentSlot = null;
        }

        private static RadioSourceType ParseSourceType(string sourceType)
        {
            if (Enum.TryParse(sourceType, out RadioSourceType parsed))
            {
                return parsed;
            }

            return RadioSourceType.None;
        }

        // Unity setup: Attach RadioService to a radio scene/service object. WebView players should subscribe to PlayRequested/PauseRequested.
    }
}

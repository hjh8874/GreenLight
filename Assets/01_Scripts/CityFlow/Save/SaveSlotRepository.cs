using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Save
{
    public sealed class SaveSlotRepository
    {
        public const int MaximumTotalSlots = 30;
        public const int MaximumAutomaticSlots = 5;
        public const int MaximumManualSlots = MaximumTotalSlots - MaximumAutomaticSlots;

        private const int MaximumDisplayNameLength = 40;

        private readonly List<SaveSlotMetadata> slots = new List<SaveSlotMetadata>();

        public IReadOnlyList<SaveSlotMetadata> Slots => slots;
        public int ManualSlotCount => slots.Count(slot => !slot.IsAutomatic);
        public int AutomaticSlotCount => slots.Count(slot => slot.IsAutomatic);

        public SaveSlotRepository()
        {
            LoadIndex();
        }

        public bool TryCreateManual(
            GameSaveData snapshot,
            string requestedName,
            byte[] previewPng,
            out SaveSlotMetadata metadata)
        {
            metadata = null;

            if (ManualSlotCount >= MaximumManualSlots)
            {
                Debug.LogWarning(
                    $"[SaveSlotRepository] Manual save limit reached ({MaximumManualSlots}).");
                return false;
            }

            DateTime nowUtc = ResolveSavedAtUtc(snapshot);
            string fallbackName = nowUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            string displayName = NormalizeDisplayName(requestedName, fallbackName);
            return TryCreate(snapshot, displayName, nowUtc.Ticks, false, previewPng, out metadata);
        }

        public bool TryCreateAutomatic(
            GameSaveData snapshot,
            out SaveSlotMetadata metadata)
        {
            metadata = null;
            DateTime nowUtc = ResolveSavedAtUtc(snapshot);
            string displayName = $"Auto Save {nowUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";

            if (!TryCreate(snapshot, displayName, nowUtc.Ticks, true, null, out metadata))
            {
                return false;
            }

            TrimAutomaticSlots();
            SaveIndex();
            return true;
        }

        public bool TryLoad(string slotId, out GameSaveData snapshot)
        {
            snapshot = null;
            SaveSlotMetadata metadata = FindSlot(slotId);

            if (metadata == null)
            {
                Debug.LogWarning($"[SaveSlotRepository] Save slot was not found: {slotId}");
                return false;
            }

            JsonSaveRepository repository = CreateRepository(metadata.SlotId);
            return repository.TryLoad(out snapshot);
        }

        public bool TryDelete(string slotId)
        {
            SaveSlotMetadata metadata = FindSlot(slotId);

            if (metadata == null)
            {
                return false;
            }

            DeleteSlotFiles(metadata.SlotId);
            slots.Remove(metadata);
            SaveIndex();
            Debug.Log($"[SaveSlotRepository] Save slot deleted: {metadata.DisplayName}");
            return true;
        }

        public bool TryWritePreview(string slotId, byte[] previewPng)
        {
            SaveSlotMetadata metadata = FindSlot(slotId);

            if (metadata == null || previewPng == null || previewPng.Length == 0)
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(SaveFilePathProvider.GetSaveSlotsDirectoryPath());
                File.WriteAllBytes(
                    SaveFilePathProvider.GetSaveSlotPreviewPath(metadata.SlotId),
                    previewPng);
                metadata.MarkPreviewAvailable();
                SaveIndex();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[SaveSlotRepository] Preview could not be written for {slotId}.\n" +
                    exception.Message);
                return false;
            }
        }

        public bool TryReadPreview(string slotId, out byte[] previewPng)
        {
            previewPng = null;
            SaveSlotMetadata metadata = FindSlot(slotId);

            if (metadata == null || !metadata.HasPreview)
            {
                return false;
            }

            string previewPath = SaveFilePathProvider.GetSaveSlotPreviewPath(metadata.SlotId);

            try
            {
                if (!File.Exists(previewPath))
                {
                    return false;
                }

                previewPng = File.ReadAllBytes(previewPath);
                return previewPng.Length > 0;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[SaveSlotRepository] Preview could not be read for {slotId}.\n" +
                    exception.Message);
                return false;
            }
        }

        private bool TryCreate(
            GameSaveData snapshot,
            string displayName,
            long savedAtUtcTicks,
            bool automatic,
            byte[] previewPng,
            out SaveSlotMetadata metadata)
        {
            metadata = null;

            if (snapshot == null)
            {
                return false;
            }

            string slotId = Guid.NewGuid().ToString("N");
            JsonSaveRepository repository = CreateRepository(slotId);

            if (!repository.TrySave(snapshot))
            {
                return false;
            }

            metadata = new SaveSlotMetadata(
                slotId,
                displayName,
                savedAtUtcTicks,
                automatic,
                false);
            slots.Add(metadata);
            SortSlots();

            if (previewPng != null && previewPng.Length > 0)
            {
                TryWritePreview(slotId, previewPng);
            }

            SaveIndex();
            Debug.Log(
                automatic
                    ? $"[SaveSlotRepository] Automatic save slot created: {displayName}"
                    : $"[SaveSlotRepository] Manual save slot created: {displayName}");
            return true;
        }

        private void LoadIndex()
        {
            slots.Clear();
            string indexPath = SaveFilePathProvider.GetSaveSlotIndexPath();

            if (!File.Exists(indexPath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(indexPath, Encoding.UTF8);
                SaveSlotIndex index = JsonUtility.FromJson<SaveSlotIndex>(json);

                if (index?.Items != null)
                {
                    slots.AddRange(index.Items.Where(IsValidSlot));
                    SortSlots();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[SaveSlotRepository] Save slot index could not be loaded.\n" +
                    exception.Message);
            }
        }

        private void SaveIndex()
        {
            try
            {
                string directoryPath = SaveFilePathProvider.GetSaveSlotsDirectoryPath();
                string indexPath = SaveFilePathProvider.GetSaveSlotIndexPath();
                string temporaryPath = indexPath + ".tmp";
                Directory.CreateDirectory(directoryPath);

                string json = JsonUtility.ToJson(new SaveSlotIndex(slots), true);
                File.WriteAllText(temporaryPath, json, Encoding.UTF8);

                if (File.Exists(indexPath))
                {
                    File.Delete(indexPath);
                }

                File.Move(temporaryPath, indexPath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[SaveSlotRepository] Save slot index could not be written.\n" +
                    exception.Message);
            }
        }

        private void TrimAutomaticSlots()
        {
            List<SaveSlotMetadata> automaticSlots = slots
                .Where(slot => slot.IsAutomatic)
                .OrderByDescending(slot => slot.SavedAtUtcTicks)
                .ToList();

            for (int index = MaximumAutomaticSlots; index < automaticSlots.Count; index++)
            {
                SaveSlotMetadata expired = automaticSlots[index];
                DeleteSlotFiles(expired.SlotId);
                slots.Remove(expired);
            }
        }

        private SaveSlotMetadata FindSlot(string slotId)
        {
            if (string.IsNullOrWhiteSpace(slotId))
            {
                return null;
            }

            return slots.FirstOrDefault(slot => slot.SlotId == slotId);
        }

        private static JsonSaveRepository CreateRepository(string slotId)
        {
            return new JsonSaveRepository(
                SaveFilePathProvider.GetSaveSlotPath(slotId),
                SaveFilePathProvider.GetSaveSlotBackupPath(slotId));
        }

        private static bool IsValidSlot(SaveSlotMetadata metadata)
        {
            if (metadata == null || string.IsNullOrWhiteSpace(metadata.SlotId))
            {
                return false;
            }

            return File.Exists(SaveFilePathProvider.GetSaveSlotPath(metadata.SlotId))
                || File.Exists(SaveFilePathProvider.GetSaveSlotBackupPath(metadata.SlotId));
        }

        private static string NormalizeDisplayName(string requestedName, string fallbackName)
        {
            string normalized = string.IsNullOrWhiteSpace(requestedName)
                ? fallbackName
                : requestedName.Trim();

            return normalized.Length <= MaximumDisplayNameLength
                ? normalized
                : normalized.Substring(0, MaximumDisplayNameLength);
        }

        private static DateTime ResolveSavedAtUtc(GameSaveData snapshot)
        {
            if (snapshot?.SavedAtUtcTicks > 0L)
            {
                try
                {
                    return new DateTime(snapshot.SavedAtUtcTicks, DateTimeKind.Utc);
                }
                catch (ArgumentOutOfRangeException)
                {
                    Debug.LogWarning(
                        "[SaveSlotRepository] Snapshot save time was invalid. Current UTC time is used.");
                }
            }

            return DateTime.UtcNow;
        }

        private void SortSlots()
        {
            slots.Sort((left, right) => right.SavedAtUtcTicks.CompareTo(left.SavedAtUtcTicks));
        }

        private static void DeleteSlotFiles(string slotId)
        {
            DeleteFile(SaveFilePathProvider.GetSaveSlotPath(slotId));
            DeleteFile(SaveFilePathProvider.GetSaveSlotBackupPath(slotId));
            DeleteFile(SaveFilePathProvider.GetSaveSlotPreviewPath(slotId));
        }

        private static void DeleteFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        [Serializable]
        private sealed class SaveSlotIndex
        {
            [SerializeField] private List<SaveSlotMetadata> items;

            public List<SaveSlotMetadata> Items => items;

            public SaveSlotIndex(List<SaveSlotMetadata> items)
            {
                this.items = new List<SaveSlotMetadata>(items);
            }
        }
    }
}

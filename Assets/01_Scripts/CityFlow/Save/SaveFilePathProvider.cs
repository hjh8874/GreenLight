using System.IO;
using UnityEngine;

namespace CityFlow.Save
{
    public static class SaveFilePathProvider
    {
        private const string SaveSlotsDirectoryName = "SaveSlots";
        private const string SaveSlotIndexFileName = "slots.json";

        public static string GetDefaultSavePath()
        {
            return Path.Combine(Application.persistentDataPath, SaveConstants.SaveFileName);
        }

        public static string GetDefaultBackupSavePath()
        {
            return Path.Combine(Application.persistentDataPath, SaveConstants.BackupSaveFileName);
        }

        public static string GetSaveSlotsDirectoryPath()
        {
            return Path.Combine(Application.persistentDataPath, SaveSlotsDirectoryName);
        }

        public static string GetSaveSlotIndexPath()
        {
            return Path.Combine(GetSaveSlotsDirectoryPath(), SaveSlotIndexFileName);
        }

        public static string GetSaveSlotPath(string slotId)
        {
            return Path.Combine(GetSaveSlotsDirectoryPath(), $"{slotId}.json");
        }

        public static string GetSaveSlotBackupPath(string slotId)
        {
            return Path.Combine(GetSaveSlotsDirectoryPath(), $"{slotId}.backup.json");
        }

        public static string GetSaveSlotPreviewPath(string slotId)
        {
            return Path.Combine(GetSaveSlotsDirectoryPath(), $"{slotId}.png");
        }
    }
}

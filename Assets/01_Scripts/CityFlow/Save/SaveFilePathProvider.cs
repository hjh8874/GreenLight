using System.IO;
using UnityEngine;

namespace CityFlow.Save
{
    public static class SaveFilePathProvider
    {
        public static string GetDefaultSavePath()
        {
            return Path.Combine(Application.persistentDataPath, SaveConstants.SaveFileName);
        }

        public static string GetDefaultBackupSavePath()
        {
            return Path.Combine(Application.persistentDataPath, SaveConstants.BackupSaveFileName);
        }
    }
}

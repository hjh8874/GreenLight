using System.IO;
using System.Text;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Save
{
    public sealed class JsonSaveRepository
    {
        public string FilePath { get; private set; }
        public string BackupFilePath { get; private set; }

        public JsonSaveRepository(string filePath = null, string backupFilePath = null)
        {
            FilePath = string.IsNullOrEmpty(filePath)
                ? SaveFilePathProvider.GetDefaultSavePath()
                : filePath;

            BackupFilePath = string.IsNullOrEmpty(backupFilePath)
                ? SaveFilePathProvider.GetDefaultBackupSavePath()
                : backupFilePath;
        }

        public bool HasSave()
        {
            return File.Exists(FilePath);
        }

        public bool TrySave(GameSaveData data)
        {
            if (data == null)
            {
                Debug.LogWarning("Save skipped because save data is null.");
                return false;
            }

            try
            {
                string directoryPath = Path.GetDirectoryName(FilePath);

                if (!string.IsNullOrEmpty(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                if (File.Exists(FilePath))
                {
                    File.Copy(FilePath, BackupFilePath, true);
                }

                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(FilePath, json, Encoding.UTF8);
                Debug.Log($"Game saved to {FilePath}");
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"Save file could not be written: {FilePath}\n{exception.Message}");
                return false;
            }
        }

        public bool TryLoad(out GameSaveData data)
        {
            if (TryLoadFromPath(FilePath, out data))
            {
                return true;
            }

            if (TryLoadFromPath(BackupFilePath, out data))
            {
                Debug.LogWarning($"Primary save failed. Backup save loaded from {BackupFilePath}");
                return true;
            }

            return false;
        }

        public void DeleteSave()
        {
            DeleteFile(FilePath);
            DeleteFile(BackupFilePath);
        }

        private static bool TryLoadFromPath(string filePath, out GameSaveData data)
        {
            data = null;

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(filePath, Encoding.UTF8);
                data = JsonUtility.FromJson<GameSaveData>(json);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"Save file could not be loaded: {filePath}\n{exception.Message}");
                return false;
            }

            if (data == null)
            {
                Debug.LogWarning($"Save file could not be parsed: {filePath}");
                return false;
            }

            return true;
        }

        private static void DeleteFile(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}

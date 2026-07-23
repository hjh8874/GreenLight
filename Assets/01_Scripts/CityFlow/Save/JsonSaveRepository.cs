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
            return File.Exists(FilePath) || File.Exists(BackupFilePath);
        }

        public bool TrySave(GameSaveData data)
        {
            if (data == null)
            {
                Debug.LogWarning("Save skipped because save data is null.");
                return false;
            }

            string temporaryFilePath = $"{FilePath}.tmp";

            try
            {
                EnsureParentDirectory(FilePath);
                EnsureParentDirectory(BackupFilePath);
                DeleteFile(temporaryFilePath);

                string json = JsonUtility.ToJson(data, true);
                WriteDurably(temporaryFilePath, json);

                if (!TryLoadFromPath(temporaryFilePath, out _))
                {
                    throw new InvalidDataException(
                        "The temporary save file could not be validated.");
                }

                ReplacePrimaryAtomically(temporaryFilePath);
                Debug.Log($"Game saved to {FilePath}");
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"Save file could not be written: {FilePath}\n{exception.Message}");
                return false;
            }
            finally
            {
                TryDeleteTemporaryFile(temporaryFilePath);
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

            if (data.SaveVersion != SaveConstants.CurrentSaveVersion)
            {
                Debug.LogWarning(
                    $"Save file version {data.SaveVersion} is not supported: {filePath}");
                data = null;
                return false;
            }

            return true;
        }

        private void ReplacePrimaryAtomically(string temporaryFilePath)
        {
            if (!File.Exists(FilePath))
            {
                File.Move(temporaryFilePath, FilePath);
                return;
            }

            string backupPath =
                TryLoadFromPath(FilePath, out _)
                    ? BackupFilePath
                    : null;

            File.Replace(
                temporaryFilePath,
                FilePath,
                backupPath);
        }

        private static void WriteDurably(
            string filePath,
            string contents)
        {
            byte[] bytes =
                new UTF8Encoding(false).GetBytes(contents);

            using (FileStream stream = new FileStream(
                       filePath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static void EnsureParentDirectory(string filePath)
        {
            string directoryPath = Path.GetDirectoryName(filePath);

            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }

        private static void TryDeleteTemporaryFile(string filePath)
        {
            try
            {
                DeleteFile(filePath);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning(
                    $"Temporary save file could not be deleted: {filePath}\n" +
                    exception.Message);
            }
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

namespace CityFlow.Save
{
    public static class SaveConstants
    {
        public const int MinimumSupportedSaveVersion = 1;
        public const int CurrentSaveVersion = 1;
        public const string SaveFileName = "save_v1.json";
        public const string BackupSaveFileName = "save_v1_backup.json";

        public static bool IsSupportedSaveVersion(int saveVersion)
        {
            return saveVersion >= MinimumSupportedSaveVersion
                   && saveVersion <= CurrentSaveVersion;
        }
    }
}

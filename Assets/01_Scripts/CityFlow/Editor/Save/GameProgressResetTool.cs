#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using CityFlow.Save;
using UnityEditor;
using UnityEngine;

namespace CityFlow.EditorTools.Save
{
    public static class GameProgressResetTool
    {
        private const string MenuPath =
            "CityFlow/Save/게임 진행 데이터만 초기화";

        [MenuItem(MenuPath, false, 2000)]
        internal static void ConfirmAndReset()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "게임 진행 데이터 초기화",
                    "플레이 모드를 종료한 뒤 다시 시도해 주세요. " +
                    "플레이 중에는 자동 저장이 초기화된 기록을 다시 만들 수 있습니다.",
                    "확인");
                return;
            }

            string savePath = SaveFilePathProvider.GetDefaultSavePath();
            string backupPath =
                SaveFilePathProvider.GetDefaultBackupSavePath();
            string slotsDirectory =
                SaveFilePathProvider.GetSaveSlotsDirectoryPath();

            bool confirmed = EditorUtility.DisplayDialog(
                "게임 진행 데이터만 초기화할까요?",
                "삭제: 현재 게임 진행, 백업, 자동·수동 저장 슬롯\n" +
                "유지: 밸런스 수치, 연구·개척 설정, ScriptableObject, Scene, Prefab, " +
                "에디터 설정과 화면·창 설정\n\n" +
                "게임 진행 데이터 삭제는 되돌릴 수 없습니다.\n\n" +
                $"저장 위치: {Application.persistentDataPath}",
                "게임 진행만 초기화",
                "취소");
            if (!confirmed)
            {
                return;
            }

            if (!TryResetProgress(
                    savePath,
                    backupPath,
                    slotsDirectory,
                    out int deletedFileCount,
                    out string error))
            {
                EditorUtility.DisplayDialog(
                    "초기화 실패",
                    "게임 진행 데이터를 모두 삭제하지 못했습니다.\n\n" + error,
                    "확인");
                Debug.LogError(
                    $"[GameProgressReset] Reset failed. {error}");
                return;
            }

            string resultMessage = deletedFileCount > 0
                ? $"게임 진행 데이터 {deletedFileCount}개를 삭제했습니다. " +
                  "다음 실행은 처음부터 시작됩니다."
                : "삭제할 게임 진행 데이터가 없습니다. 다음 실행은 처음부터 시작됩니다.";
            EditorUtility.DisplayDialog(
                "초기화 완료",
                resultMessage,
                "확인");
            Debug.Log($"[GameProgressReset] {resultMessage}");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanResetFromMenu()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode &&
                   !EditorApplication.isCompiling &&
                   !EditorApplication.isUpdating;
        }

        internal static bool TryResetProgress(
            string savePath,
            string backupPath,
            string slotsDirectory,
            out int deletedFileCount,
            out string error)
        {
            deletedFileCount = 0;
            error = string.Empty;

            try
            {
                string normalizedSavePath = NormalizeFilePath(savePath);
                string normalizedBackupPath = NormalizeFilePath(backupPath);
                string normalizedSlotsDirectory =
                    NormalizeDirectoryPath(slotsDirectory);
                ValidateTargets(
                    normalizedSavePath,
                    normalizedBackupPath,
                    normalizedSlotsDirectory);

                deletedFileCount += DeleteFileIfPresent(
                    normalizedSavePath);
                deletedFileCount += DeleteFileIfPresent(
                    normalizedSavePath + ".tmp");
                deletedFileCount += DeleteFileIfPresent(
                    normalizedBackupPath);

                if (Directory.Exists(normalizedSlotsDirectory))
                {
                    deletedFileCount += Directory
                        .EnumerateFiles(
                            normalizedSlotsDirectory,
                            "*",
                            SearchOption.AllDirectories)
                        .Count();
                    Directory.Delete(
                        normalizedSlotsDirectory,
                        recursive: true);
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static string NormalizeFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("저장 파일 경로가 비어 있습니다.");
            }

            return Path.GetFullPath(path);
        }

        private static string NormalizeDirectoryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("저장 슬롯 폴더 경로가 비어 있습니다.");
            }

            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void ValidateTargets(
            string savePath,
            string backupPath,
            string slotsDirectory)
        {
            string saveRoot = Path.GetDirectoryName(savePath);
            string backupRoot = Path.GetDirectoryName(backupPath);
            string slotsRoot = Path.GetDirectoryName(slotsDirectory);
            string expectedSlotsDirectoryName = Path.GetFileName(
                SaveFilePathProvider.GetSaveSlotsDirectoryPath());
            if (string.IsNullOrEmpty(saveRoot) ||
                !PathsEqual(saveRoot, backupRoot) ||
                !PathsEqual(saveRoot, slotsRoot) ||
                PathsEqual(saveRoot, slotsDirectory) ||
                !string.Equals(
                    Path.GetFileName(savePath),
                    SaveConstants.SaveFileName,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    Path.GetFileName(backupPath),
                    SaveConstants.BackupSaveFileName,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    Path.GetFileName(slotsDirectory),
                    expectedSlotsDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "저장 경로가 예상 범위를 벗어나 초기화를 중단했습니다.");
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left ?? string.Empty)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right ?? string.Empty)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private static int DeleteFileIfPresent(string path)
        {
            if (!File.Exists(path))
            {
                return 0;
            }

            File.Delete(path);
            return 1;
        }
    }
}
#endif

using System;
using System.IO;
using CityFlow.EditorTools.Save;
using NUnit.Framework;

namespace CityFlow.Sim.Tests
{
    public sealed class GameProgressResetToolTests
    {
        private string testRoot;

        [SetUp]
        public void SetUp()
        {
            testRoot = Path.Combine(
                Path.GetTempPath(),
                "GreenLight_GameProgressReset_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }

        [Test]
        public void TryResetProgress_RemovesProgressButPreservesOtherSettings()
        {
            string savePath = Path.Combine(testRoot, "save_v1.json");
            string backupPath =
                Path.Combine(testRoot, "save_v1_backup.json");
            string temporaryPath = savePath + ".tmp";
            string slotsDirectory = Path.Combine(testRoot, "SaveSlots");
            string settingsPath = Path.Combine(testRoot, "settings.json");

            File.WriteAllText(savePath, "save");
            File.WriteAllText(backupPath, "backup");
            File.WriteAllText(temporaryPath, "temporary");
            Directory.CreateDirectory(slotsDirectory);
            File.WriteAllText(
                Path.Combine(slotsDirectory, "slots.json"),
                "index");
            File.WriteAllText(
                Path.Combine(slotsDirectory, "manual.json"),
                "slot");
            File.WriteAllText(settingsPath, "settings");

            bool reset = GameProgressResetTool.TryResetProgress(
                savePath,
                backupPath,
                slotsDirectory,
                out int deletedFileCount,
                out string error);

            Assert.That(reset, Is.True, error);
            Assert.That(deletedFileCount, Is.EqualTo(5));
            Assert.That(File.Exists(savePath), Is.False);
            Assert.That(File.Exists(backupPath), Is.False);
            Assert.That(File.Exists(temporaryPath), Is.False);
            Assert.That(Directory.Exists(slotsDirectory), Is.False);
            Assert.That(File.Exists(settingsPath), Is.True);
        }

        [Test]
        public void TryResetProgress_WhenRepeated_RemainsSuccessful()
        {
            string savePath = Path.Combine(testRoot, "save_v1.json");
            string backupPath =
                Path.Combine(testRoot, "save_v1_backup.json");
            string slotsDirectory = Path.Combine(testRoot, "SaveSlots");

            bool reset = GameProgressResetTool.TryResetProgress(
                savePath,
                backupPath,
                slotsDirectory,
                out int deletedFileCount,
                out string error);

            Assert.That(reset, Is.True, error);
            Assert.That(deletedFileCount, Is.Zero);
        }

        [Test]
        public void TryResetProgress_RejectsDirectoryOutsideSaveRoot()
        {
            string savePath = Path.Combine(testRoot, "save_v1.json");
            string backupPath =
                Path.Combine(testRoot, "save_v1_backup.json");
            string outsideDirectory = Path.Combine(
                Path.GetTempPath(),
                "GreenLight_Outside_SaveSlots");

            bool reset = GameProgressResetTool.TryResetProgress(
                savePath,
                backupPath,
                outsideDirectory,
                out _,
                out string error);

            Assert.That(reset, Is.False);
            Assert.That(error, Does.Contain("예상 범위"));
        }
    }
}

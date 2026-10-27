using System;
using System.Collections.Generic;
using System.IO;
using AwesomeAssertions;
using FileForge.IO;
using Xunit;

namespace FileForge.Tests;

public class SyncCatalogRepositoryTests
{
    [Fact]
    public void LoadCatalog_WhenFileDoesNotExist_ReturnsEmptyDictionary()
    {
        var result = SyncCatalogRepository.LoadCatalog(@"C:\non\existent\catalog.dat");

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public void SaveCatalog_WhenLoadedAfterSaving_PersistsAndReloadsEntriesCorrectly()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"catalog_test_{Guid.NewGuid():N}.dat");

        try
        {
            var originalCatalog = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["file1.txt"] = "123456_100",
                [@"sub\file2.doc"] = "789012_2048"
            };

            SyncCatalogRepository.SaveCatalog(originalCatalog, tempFile);
            var reloaded = SyncCatalogRepository.LoadCatalog(tempFile);

            reloaded.Should().HaveCount(2);
            reloaded["file1.txt"].Should().Be("123456_100");
            reloaded[@"sub\file2.doc"].Should().Be("789012_2048");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void MigrateLegacyBackupNames_WhenLegacyFoldersExist_RenamesMatchingFolders()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"migrate_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var folder1 = Path.Combine(tempDir, "sI1");
            var folder2 = Path.Combine(tempDir, "sI2");
            var normalFolder = Path.Combine(tempDir, "other_folder");

            Directory.CreateDirectory(folder1);
            Directory.CreateDirectory(folder2);
            Directory.CreateDirectory(normalFolder);

            SyncCatalogRepository.MigrateLegacyBackupNames(tempDir);

            Directory.Exists(folder1).Should().BeFalse();
            Directory.Exists(folder2).Should().BeFalse();
            Directory.Exists(normalFolder).Should().BeTrue();

            var directories = Directory.GetDirectories(tempDir);
            directories.Should().Contain(d => Path.GetFileName(d).StartsWith("Backup_", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void BootstrapInitialCatalog_WhenValidBackupsExist_CreatesIndexFile()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"bootstrap_test_{Guid.NewGuid():N}");
        var srcDir = Path.Combine(tempRoot, "Source");
        var dstDir = Path.Combine(tempRoot, "Dest");
        var backupDir = Path.Combine(dstDir, "Backup_20261016_1200");

        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(backupDir);

        try
        {
            File.WriteAllText(Path.Combine(srcDir, "file.txt"), "A");
            File.WriteAllText(Path.Combine(backupDir, "file.txt"), "A");

            SyncCatalogRepository.BootstrapInitialCatalog(srcDir, dstDir);

            var catalogFile = Path.Combine(dstDir, "backup_catalog.dat");
            File.Exists(catalogFile).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public void BootstrapInitialCatalog_WhenCatalogAlreadyExists_ReturnsEarlyWithoutModifying()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"bootstrap_exist_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            string catFile = Path.Combine(tempRoot, "backup_catalog.dat");
            File.WriteAllText(catFile, "existing");

            SyncCatalogRepository.BootstrapInitialCatalog(tempRoot, tempRoot);

            File.ReadAllText(catFile).Should().Be("existing");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public void BootstrapInitialCatalog_WhenNoLegacyDirectoriesExist_ReturnsEarly()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"bootstrap_nodirs_{Guid.NewGuid():N}");
        var srcDir = Path.Combine(tempRoot, "Source");
        var dstDir = Path.Combine(tempRoot, "Dest");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(dstDir);

        try
        {
            SyncCatalogRepository.BootstrapInitialCatalog(srcDir, dstDir);

            File.Exists(Path.Combine(dstDir, "backup_catalog.dat")).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public void BootstrapInitialCatalog_WhenSourceFileDoesNotExist_IndexesBackupFile()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"bootstrap_nosrc_{Guid.NewGuid():N}");
        var srcDir = Path.Combine(tempRoot, "Source");
        var dstDir = Path.Combine(tempRoot, "Dest");
        var backupDir = Path.Combine(dstDir, "Backup_20261016_1200");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(backupDir);

        try
        {
            File.WriteAllText(Path.Combine(backupDir, "orphan.txt"), "orphan content");

            SyncCatalogRepository.BootstrapInitialCatalog(srcDir, dstDir);

            var catalog = SyncCatalogRepository.LoadCatalog(Path.Combine(dstDir, "backup_catalog.dat"));
            catalog.Should().ContainKey("orphan.txt");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public void MigrateLegacyBackupNames_WhenDirectoryDoesNotExist_ReturnsSilently()
    {
        var action = () => SyncCatalogRepository.MigrateLegacyBackupNames(@"C:\NonExistent_Migrate_Dir_12345");
        action.Should().NotThrow();
    }

    [Fact]
    public void LoadCatalog_WhenDataIsCorrupt_ReturnsEmptyDictionary()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"corrupt_{Guid.NewGuid():N}.dat");
        try
        {
            File.WriteAllBytes(tempFile, [10, 0, 0, 0]); // Count = 10 but no following string data

            var catalog = SyncCatalogRepository.LoadCatalog(tempFile);
            catalog.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using FileForge.Sync;
using FluentAssertions;
using Xunit;

namespace FileForge.Tests;

public class DirectorySynchronizerTests
{
    [Fact]
    public void ParallelEnumerateFiles_ShouldDiscoverAllFilesAndDirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"enum_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var subDir = Path.Combine(tempDir, "Sub");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(tempDir, "file1.txt"), "A");
            File.WriteAllText(Path.Combine(subDir, "file2.txt"), "B");

            var items = DirectorySynchronizer.ParallelEnumerateFiles(tempDir, maxParallelism: 2);

            items.Should().HaveCount(3);
            items.OfType<FileInfo>().Should().HaveCount(2);
            items.OfType<DirectoryInfo>().Should().HaveCount(1);
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
    public void CloneDirectory_ShouldMirrorSourceToDestination()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"clone_test_{Guid.NewGuid():N}");
        var srcDir = Path.Combine(tempRoot, "Source");
        var dstDir = Path.Combine(tempRoot, "Dest");

        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(dstDir);

        try
        {
            File.WriteAllText(Path.Combine(srcDir, "doc.txt"), "Contenu source");
            File.WriteAllText(Path.Combine(dstDir, "obsolete.txt"), "À supprimer");
            var excludedSub = Path.Combine(dstDir, "ExcludedDir");
            Directory.CreateDirectory(excludedSub);
            File.WriteAllText(Path.Combine(excludedSub, "sub.txt"), "exclu");

            var syncOptions = new DirectorySyncOptions
            {
                SourceListParallelism = 1,
                DestinationListParallelism = 1,
                CopyParallelism = 1,
                CloneOptions = new DirectoryCloneOptions
                {
                    ExcludedPaths = ["ExcludedDir"]
                }
            };

            DirectorySynchronizer.CloneDirectory(srcDir, dstDir, syncOptions);

            File.Exists(Path.Combine(dstDir, "doc.txt")).Should().BeTrue();
            File.ReadAllText(Path.Combine(dstDir, "doc.txt")).Should().Be("Contenu source");
            File.Exists(Path.Combine(dstDir, "obsolete.txt")).Should().BeFalse();
            Directory.Exists(excludedSub).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                DirectorySynchronizer.DeleteDirectoryRobust(tempRoot);
            }
        }
    }

    [Fact]
    public void IncrementalBackup_ShouldCreateDatedBackupFolder()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"inc_test_{Guid.NewGuid():N}");
        var srcDir = Path.Combine(tempRoot, "Source");
        var dstDir = Path.Combine(tempRoot, "Dest");

        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(dstDir);

        try
        {
            File.WriteAllText(Path.Combine(srcDir, "data.txt"), "Première version");

            var syncOptions = new DirectorySyncOptions
            {
                SourceListParallelism = 1,
                DestinationListParallelism = 1,
                CopyParallelism = 1
            };

            var staleTempPath = Path.Combine(dstDir, "Backup_In_Progress");
            Directory.CreateDirectory(staleTempPath);
            File.SetAttributes(staleTempPath, FileAttributes.ReadOnly | FileAttributes.Directory);

            DirectorySynchronizer.IncrementalBackup(srcDir, dstDir, syncOptions);

            var backupDirs = Directory.GetDirectories(dstDir);
            backupDirs.Should().ContainSingle(d => Path.GetFileName(d).StartsWith("Backup_", StringComparison.OrdinalIgnoreCase));

            var catalogPath = Path.Combine(dstDir, "backup_catalog.dat");
            File.Exists(catalogPath).Should().BeTrue();
            Directory.Exists(staleTempPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                DirectorySynchronizer.DeleteDirectoryRobust(tempRoot);
            }
        }
    }

    [Fact]
    public void DeleteDirectoryRobust_WhenDirectoryHasReadOnlySubdirectoryAndFiles_ShouldDeleteSuccessfully()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"robust_del_test_{Guid.NewGuid():N}");
        var subDir = Path.Combine(tempRoot, "SubDir_ReadOnly");
        var filePath = Path.Combine(subDir, "file_readonly.txt");

        Directory.CreateDirectory(subDir);
        File.WriteAllText(filePath, "Contenu protégé");

        File.SetAttributes(filePath, FileAttributes.ReadOnly);
        File.SetAttributes(subDir, FileAttributes.ReadOnly | FileAttributes.Directory);
        File.SetAttributes(tempRoot, FileAttributes.ReadOnly | FileAttributes.Directory);

        DirectorySynchronizer.DeleteDirectoryRobust(tempRoot);

        Directory.Exists(tempRoot).Should().BeFalse();
    }

    [Fact]
    public void DeleteDirectoryRobust_WhenDirectoryDoesNotExist_ShouldReturnSilently()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"non_existent_{Guid.NewGuid():N}");

        var action = () => DirectorySynchronizer.DeleteDirectoryRobust(nonExistentPath);

        action.Should().NotThrow();
    }

    [Fact]
    public void DeleteDirectoryRobust_WhenPathIsNullOrWhiteSpace_ShouldThrowArgumentException()
    {
        var action = () => DirectorySynchronizer.DeleteDirectoryRobust("   ");

        action.Should().Throw<ArgumentException>();
    }
}

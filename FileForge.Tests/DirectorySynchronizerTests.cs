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
    public void ParallelEnumerateFiles_WhenCalled_DiscoversAllFilesAndDirectories()
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
    public void CloneDirectory_WhenExecuted_MirrorsSourceToDestination()
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
            var obsoleteEmpty = Path.Combine(dstDir, "ObsoleteEmptyDir");
            Directory.CreateDirectory(obsoleteEmpty);

            var srcSubDir = Path.Combine(srcDir, "NewSubDir");
            Directory.CreateDirectory(srcSubDir);
            File.WriteAllText(Path.Combine(srcSubDir, "subdoc.txt"), "Contenu sous-dossier");

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
            Directory.Exists(obsoleteEmpty).Should().BeFalse();
            Directory.Exists(Path.Combine(dstDir, "NewSubDir")).Should().BeTrue();
            File.Exists(Path.Combine(dstDir, "NewSubDir", "subdoc.txt")).Should().BeTrue();
            Directory.Exists(excludedSub).Should().BeFalse();

            // Deuxième passage sans changement
            DirectorySynchronizer.CloneDirectory(srcDir, dstDir, syncOptions);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                DirectorySynchronizer.ForceDeleteDirectory(tempRoot);
            }
        }
    }

    [Fact]
    public void IncrementalBackup_WhenExecuted_CreatesDatedBackupFolderAndCatalog()
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

            // Deuxième passage incrémental sans changement
            DirectorySynchronizer.IncrementalBackup(srcDir, dstDir, syncOptions);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                DirectorySynchronizer.ForceDeleteDirectory(tempRoot);
            }
        }
    }

    [Fact]
    public void ForceDeleteDirectory_WhenDirectoryHasReadOnlySubdirectoryAndFiles_DeletesSuccessfully()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"force_del_test_{Guid.NewGuid():N}");
        var subDir = Path.Combine(tempRoot, "SubDir_ReadOnly");
        var filePath = Path.Combine(subDir, "file_readonly.txt");

        Directory.CreateDirectory(subDir);
        File.WriteAllText(filePath, "Contenu protégé");

        File.SetAttributes(filePath, FileAttributes.ReadOnly);
        File.SetAttributes(subDir, FileAttributes.ReadOnly | FileAttributes.Directory);
        File.SetAttributes(tempRoot, FileAttributes.ReadOnly | FileAttributes.Directory);

        DirectorySynchronizer.ForceDeleteDirectory(tempRoot);

        Directory.Exists(tempRoot).Should().BeFalse();
    }

    [Fact]
    public void ForceDeleteDirectory_WhenDirectoryDoesNotExist_ReturnsSilently()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"non_existent_{Guid.NewGuid():N}");

        var action = () => DirectorySynchronizer.ForceDeleteDirectory(nonExistentPath);

        action.Should().NotThrow();
    }

    [Fact]
    public void ForceDeleteDirectory_WhenPathIsNullOrWhiteSpace_ThrowsArgumentException()
    {
        var action = () => DirectorySynchronizer.ForceDeleteDirectory("   ");

        action.Should().Throw<ArgumentException>();
    }
}

using System;
using System.IO;
using FileForge.IO;
using FluentAssertions;
using Xunit;

namespace FileForge.Tests;

public class FileCopierTests
{
    [Theory]
    [InlineData(500, 64 * 1024)]
    [InlineData(1024 * 1024 + 1, 128 * 1024)]
    [InlineData(2 * 1024 * 1024 + 1, 256 * 1024)]
    [InlineData(4 * 1024 * 1024 + 1, 512 * 1024)]
    [InlineData(8 * 1024 * 1024 + 1, 1024 * 1024)]
    [InlineData(16 * 1024 * 1024 + 1, 2 * 1024 * 1024)]
    [InlineData(32 * 1024 * 1024 + 1, 4 * 1024 * 1024)]
    [InlineData(64 * 1024 * 1024 + 1, 8 * 1024 * 1024)]
    [InlineData(128 * 1024 * 1024 + 1, 16 * 1024 * 1024)]
    [InlineData(600 * 1024 * 1024, 16 * 1024 * 1024)]
    public void CalculateOptimalBufferSize_WhenGivenFileSize_ReturnsExpectedMinimumSize(long fileSize, int expectedMinSize)
    {
        int bufferSize = FileCopier.CalculateOptimalBufferSize(fileSize);

        bufferSize.Should().BeGreaterOrEqualTo(expectedMinSize);
    }

    [Theory]
    [InlineData(500, "500 o")]
    [InlineData(2048, "2 Ko")]
    [InlineData(5 * 1024 * 1024, "5 Mo")]
    public void FormatFileSize_WhenGivenByteCount_ReturnsExpectedString(long bytes, string expected)
    {
        string formatted = FileCopier.FormatFileSize(bytes);

        formatted.Should().Be(expected);
    }

    [Fact]
    public void CopyFile_WhenSourceIsValidAndVerifyHashIsTrue_CopiesAndVerifiesSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"copy_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var srcFile = Path.Combine(tempDir, "source.txt");
        var dstFile = Path.Combine(tempDir, "destination.txt");

        try
        {
            File.WriteAllText(srcFile, "Contenu de test pour la copie sécurisée.");
            File.WriteAllText(dstFile, "Ancien contenu à écraser");

            bool success = FileCopier.CopyFile(srcFile, dstFile, verifyHash: true);

            success.Should().BeTrue();
            File.Exists(dstFile).Should().BeTrue();
            File.ReadAllText(dstFile).Should().Be("Contenu de test pour la copie sécurisée.");
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
    public void CompareFileHashes_WhenFilesAreIdentical_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hash_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var file1 = Path.Combine(tempDir, "file1.txt");
        var file2 = Path.Combine(tempDir, "file2.txt");

        try
        {
            File.WriteAllText(file1, "Même contenu");
            File.WriteAllText(file2, "Même contenu");

            bool matches = FileCopier.CompareFileHashes(file1, file2);

            matches.Should().BeTrue();
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
    public void CopyFile_WhenDisplayProgressIsTrue_CopiesSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"copy_prog_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var srcFile = Path.Combine(tempDir, "source_prog.txt");
        var dstFile = Path.Combine(tempDir, "destination_prog.txt");

        try
        {
            File.WriteAllText(srcFile, new string('A', 10000));

            bool success = FileCopier.CopyFile(srcFile, dstFile, bufferSize: 1024, displayProgress: true);

            success.Should().BeTrue();
            File.Exists(dstFile).Should().BeTrue();
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
    public void CopyFile_WhenSourceDoesNotExist_ReturnsFalse()
    {
        bool success = FileCopier.CopyFile(@"C:\NonExistent_File_12345.txt", @"C:\NonExistent_Dest_12345.txt");
        success.Should().BeFalse();
    }

    [Fact]
    public void CopyMetadata_WhenSourceIsDirectory_CopiesDatesAndAttributes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"meta_dir_test_{Guid.NewGuid():N}");
        var srcDir = Path.Combine(tempDir, "Src");
        var dstDir = Path.Combine(tempDir, "Dst");

        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(dstDir);

        try
        {
            var pastDate = new DateTime(2020, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            Directory.SetLastWriteTimeUtc(srcDir, pastDate);

            FileCopier.CopyMetadata(srcDir, dstDir, includeAcl: true);

            Directory.GetLastWriteTimeUtc(dstDir).Should().Be(pastDate);
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
    public void CopyAcl_WhenSourceDoesNotExist_ThrowsFileNotFoundException()
    {
        var action = () => FileCopier.CopyAcl(@"C:\NonExistent_Path_Acl", @"C:\NonExistent_Path_Dst");
        action.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void CopyMetadata_WhenSourceIsFileWithAcl_CopiesMetadataSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"meta_file_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var srcFile = Path.Combine(tempDir, "file_src.txt");
        var dstFile = Path.Combine(tempDir, "file_dst.txt");

        try
        {
            File.WriteAllText(srcFile, "data");
            File.WriteAllText(dstFile, "data");

            var pastDate = new DateTime(2021, 5, 10, 8, 30, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(srcFile, pastDate);

            FileCopier.CopyMetadata(srcFile, dstFile, includeAcl: true);

            File.GetLastWriteTimeUtc(dstFile).Should().Be(pastDate);
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
    public void CopyAcl_WhenSourceIsFile_CopiesAclSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"acl_file_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var srcFile = Path.Combine(tempDir, "file_src.txt");
        var dstFile = Path.Combine(tempDir, "file_dst.txt");

        try
        {
            File.WriteAllText(srcFile, "data");
            File.WriteAllText(dstFile, "data");

            var action = () => FileCopier.CopyAcl(srcFile, dstFile);
            action.Should().NotThrow();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}

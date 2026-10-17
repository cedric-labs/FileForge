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

    [Fact]
    public void ComputeFileHash_WhenFileExists_ReturnsExpectedSha256Hex()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"hash_test_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tempFile, "FileForgeHashTest");
            var hash = FileCopier.ComputeFileHash(tempFile);

            hash.Should().NotBeNullOrWhiteSpace();
            hash.Length.Should().Be(64); // SHA-256 produit 64 caractères hexadécimaux
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ComputeFileHashAsync_WhenFileExists_ReturnsSameAsSync()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"hash_async_test_{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(tempFile, "FileForgeHashAsyncTest");
            var syncHash = FileCopier.ComputeFileHash(tempFile);
            var asyncHash = await FileCopier.ComputeFileHashAsync(tempFile);

            asyncHash.Should().Be(syncHash);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void VerifyFileHash_WhenHashMatches_ReturnsTrue()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"verify_hash_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tempFile, "VerifyMe");
            var actualHash = FileCopier.ComputeFileHash(tempFile);

            bool matches = FileCopier.VerifyFileHash(tempFile, actualHash);
            matches.Should().BeTrue();

            bool matchesUpper = FileCopier.VerifyFileHash(tempFile, actualHash.ToUpperInvariant());
            matchesUpper.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void VerifyFileHash_WhenHashDiffers_ReturnsFalse()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"verify_diff_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tempFile, "VerifyMismatch");
            bool matches = FileCopier.VerifyFileHash(tempFile, "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            matches.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReadFileHeader_WhenFileExists_ReturnsRequestedBytes()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"header_test_{Guid.NewGuid():N}.dat");
        try
        {
            byte[] rawBytes = [0x53, 0x51, 0x4C, 0x69, 0x74, 0x65, 0x20, 0x66, 0x6F, 0x72, 0x6D, 0x61, 0x74, 0x20, 0x33, 0x00];
            File.WriteAllBytes(tempFile, rawBytes);

            var header = FileCopier.ReadFileHeader(tempFile, 16);
            header.Should().Equal(rawBytes);

            var shortHeader = FileCopier.ReadFileHeader(tempFile, 6);
            shortHeader.Should().Equal(rawBytes[..6]);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReadFileHeaderAsync_WhenFileExists_ReturnsExactBytes()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"header_async_{Guid.NewGuid():N}.dat");
        try
        {
            byte[] rawBytes = [0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02];
            await File.WriteAllBytesAsync(tempFile, rawBytes);

            var header = await FileCopier.ReadFileHeaderAsync(tempFile, 4);
            header.Should().Equal(rawBytes[..4]);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void MatchesFileHeader_WhenHeaderMatches_ReturnsTrue()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"match_hdr_{Guid.NewGuid():N}.dat");
        try
        {
            byte[] rawBytes = [0x53, 0x51, 0x4C, 0x69, 0x74, 0x65];
            File.WriteAllBytes(tempFile, rawBytes);

            FileCopier.MatchesFileHeader(tempFile, [0x53, 0x51, 0x4C]).Should().BeTrue();
            FileCopier.MatchesFileHeader(tempFile, [0x00, 0x00]).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}


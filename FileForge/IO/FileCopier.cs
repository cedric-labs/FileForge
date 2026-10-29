using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using FileForge.Diagnostics;

namespace FileForge.IO;

/// <summary>
/// Copies single files with their metadata and ACLs, and checks their integrity by hashing.
/// </summary>
public static class FileCopier
{
    private const string TempExtension = ".fileforge_tmp";

    public static event EventHandler<LogMessageEventArgs>? CopierLog;

    private static void RaiseLog(string message)
    {
        CopierLog?.Invoke(null, new LogMessageEventArgs(message));
    }

    /// <summary>
    /// Copies a file through a temporary file (.fileforge_tmp), so that the destination is never left half-written.
    /// </summary>
    /// <param name="sourcePath">Source file path.</param>
    /// <param name="destPath">Destination file path.</param>
    /// <param name="bufferSize">Copy buffer size in bytes.</param>
    /// <param name="displayProgress">Whether to show live progress on the console.</param>
    /// <param name="verifyHash">Whether to compare the SHA-256 hashes of both files after the copy.</param>
    /// <param name="progressCallback">Optional callback receiving the bytes copied so far and the total size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the copy and the optional check succeeded; otherwise false.</returns>
    public static bool CopyFile(
        string sourcePath,
        string destPath,
        int bufferSize = 64 * 1024,
        bool displayProgress = false,
        bool verifyHash = false,
        Action<long, long>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destPath);

        cancellationToken.ThrowIfCancellationRequested();
        var tempPath = destPath + TempExtension;

        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            long fileSize = new FileInfo(sourcePath).Length;
            long totalBytesRead = 0;

            using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var destStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                try
                {
                    int bytesRead;
                    int lastReportedProgress = 0;

                    while ((bytesRead = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        destStream.Write(buffer, 0, bytesRead);
                        totalBytesRead += bytesRead;

                        progressCallback?.Invoke(totalBytesRead, fileSize);

                        int currentProgress = fileSize > 0 ? (int)((totalBytesRead * 100) / fileSize) : 100;

                        if (currentProgress != lastReportedProgress)
                        {
                            lastReportedProgress = currentProgress;
                            if (displayProgress)
                            {
                                TryWriteProgress(currentProgress, totalBytesRead);
                            }
                        }
                    }

                    if (displayProgress)
                    {
                        TryWriteLine();
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(destPath))
            {
                File.Delete(destPath);
            }

            File.Move(tempPath, destPath);
            CopyMetadata(sourcePath, destPath, false);

            if (verifyHash && !CompareFileHashes(sourcePath, destPath))
            {
                RaiseLog($"[WARN] SHA-256 mismatch after copy: {sourcePath}");
                File.Delete(destPath);
                return false;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            CleanupTempFile(tempPath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RaiseLog($"[ERROR] File copy failed ({sourcePath} -> {destPath}): {ex.Message}");
            CleanupTempFile(tempPath);
            return false;
        }
    }

    private static void CleanupTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
        {
            RaiseLog($"[WARN] Could not delete the temporary file ({tempPath}): {cleanupEx.Message}");
        }
    }

    /// <summary>
    /// Copies the metadata (timestamps, attributes and optionally ACLs) of one item to another.
    /// </summary>
    public static void CopyMetadata(string sourcePath, string destPath, bool includeAcl = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destPath);

        if (File.Exists(sourcePath))
        {
            var srcInfo = new FileInfo(sourcePath);
            File.SetCreationTimeUtc(destPath, srcInfo.CreationTimeUtc);
            File.SetLastAccessTimeUtc(destPath, srcInfo.LastAccessTimeUtc);
            File.SetLastWriteTimeUtc(destPath, srcInfo.LastWriteTimeUtc);
            File.SetAttributes(destPath, srcInfo.Attributes);

            if (includeAcl)
            {
                CopyAcl(sourcePath, destPath);
            }
        }
        else if (Directory.Exists(sourcePath))
        {
            var srcInfo = new DirectoryInfo(sourcePath);
            Directory.SetCreationTimeUtc(destPath, srcInfo.CreationTimeUtc);
            Directory.SetLastAccessTimeUtc(destPath, srcInfo.LastAccessTimeUtc);
            Directory.SetLastWriteTimeUtc(destPath, srcInfo.LastWriteTimeUtc);

            var dirAttributes = srcInfo.Attributes & ~(FileAttributes.ReadOnly | FileAttributes.System);
            File.SetAttributes(destPath, dirAttributes);

            if (includeAcl)
            {
                CopyAcl(sourcePath, destPath);
            }
        }
    }

    /// <summary>
    /// Copies the access control lists (ACLs).
    /// </summary>
    public static void CopyAcl(string sourcePath, string destPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destPath);

        if (File.Exists(sourcePath))
        {
            var srcInfo = new FileInfo(sourcePath);
            var dstInfo = new FileInfo(destPath);
            FileSystemAclExtensions.SetAccessControl(dstInfo, FileSystemAclExtensions.GetAccessControl(srcInfo));
        }
        else if (Directory.Exists(sourcePath))
        {
            var srcInfo = new DirectoryInfo(sourcePath);
            var dstInfo = new DirectoryInfo(destPath);
            FileSystemAclExtensions.SetAccessControl(dstInfo, FileSystemAclExtensions.GetAccessControl(srcInfo));
        }
        else
        {
            throw new FileNotFoundException($"The source path does not exist: {sourcePath}");
        }
    }

    /// <summary>
    /// Compares the SHA-256 hashes of two files to check that they are byte-for-byte identical.
    /// </summary>
    /// <param name="path1">First file path.</param>
    /// <param name="path2">Second file path.</param>
    /// <returns>True when both hashes are identical; otherwise false.</returns>
    public static bool CompareFileHashes(string path1, string path2)
    {
        using var sha = SHA256.Create();
        using var stream1 = new FileStream(path1, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var stream2 = new FileStream(path2, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var hash1 = sha.ComputeHash(stream1);
        var hash2 = sha.ComputeHash(stream2);
        return hash1.SequenceEqual(hash2);
    }

    /// <summary>
    /// Computes the hexadecimal hash of a file.
    /// </summary>
    /// <param name="filePath">File path.</param>
    /// <param name="algorithm">Hash algorithm (SHA-256 by default).</param>
    /// <returns>The lowercase hexadecimal hash.</returns>
    public static string ComputeFileHash(string filePath, HashAlgorithmName? algorithm = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return ComputeHash(stream, algorithm);
    }

    /// <summary>
    /// Asynchronously computes the hexadecimal hash of a file.
    /// </summary>
    /// <param name="filePath">File path.</param>
    /// <param name="algorithm">Hash algorithm (SHA-256 by default).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lowercase hexadecimal hash.</returns>
    public static async Task<string> ComputeFileHashAsync(
        string filePath,
        HashAlgorithmName? algorithm = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return await ComputeHashAsync(stream, algorithm, cancellationToken);
    }

    /// <summary>
    /// Computes the hexadecimal hash of a stream.
    /// </summary>
    /// <param name="stream">Stream to hash.</param>
    /// <param name="algorithm">Hash algorithm (SHA-256 by default).</param>
    /// <returns>The lowercase hexadecimal hash.</returns>
    public static string ComputeHash(Stream stream, HashAlgorithmName? algorithm = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var hasher = CreateHashAlgorithm(algorithm ?? HashAlgorithmName.SHA256);
        var hashBytes = hasher.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Asynchronously computes the hexadecimal hash of a stream.
    /// </summary>
    /// <param name="stream">Stream to hash.</param>
    /// <param name="algorithm">Hash algorithm (SHA-256 by default).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lowercase hexadecimal hash.</returns>
    public static async Task<string> ComputeHashAsync(
        Stream stream,
        HashAlgorithmName? algorithm = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var hasher = CreateHashAlgorithm(algorithm ?? HashAlgorithmName.SHA256);
        var hashBytes = await hasher.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Checks whether the hash of a file matches the expected hexadecimal value.
    /// </summary>
    /// <param name="filePath">Path of the file to check.</param>
    /// <param name="expectedHashHex">Expected hexadecimal hash (case-insensitive).</param>
    /// <param name="algorithm">Hash algorithm (SHA-256 by default).</param>
    /// <returns>True when the hashes match; otherwise false.</returns>
    public static bool VerifyFileHash(string filePath, string expectedHashHex, HashAlgorithmName? algorithm = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHashHex);

        var actualHash = ComputeFileHash(filePath, algorithm);
        return string.Equals(actualHash, expectedHashHex.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads up to the given number of bytes from the start of a file, with shared read/write access.
    /// </summary>
    /// <param name="filePath">File path.</param>
    /// <param name="byteCount">Number of bytes to read.</param>
    /// <returns>The bytes read (at most byteCount).</returns>
    public static byte[] ReadFileHeader(string filePath, int byteCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteCount);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[Math.Min(stream.Length, byteCount)];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);
        if (bytesRead < buffer.Length)
        {
            Array.Resize(ref buffer, bytesRead);
        }
        return buffer;
    }

    /// <summary>
    /// Asynchronously reads up to the given number of bytes from the start of a file, with shared read/write access.
    /// </summary>
    /// <param name="filePath">File path.</param>
    /// <param name="byteCount">Number of bytes to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bytes read (at most byteCount).</returns>
    public static async Task<byte[]> ReadFileHeaderAsync(string filePath, int byteCount, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteCount);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[Math.Min(stream.Length, byteCount)];
        int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        if (bytesRead < buffer.Length)
        {
            Array.Resize(ref buffer, bytesRead);
        }
        return buffer;
    }

    /// <summary>
    /// Checks whether a file starts with the given byte sequence.
    /// </summary>
    /// <param name="filePath">File path.</param>
    /// <param name="expectedHeader">Expected leading bytes.</param>
    /// <returns>True when the file exists and starts with the expected bytes; otherwise false.</returns>
    public static bool MatchesFileHeader(string filePath, byte[] expectedHeader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(expectedHeader);

        if (expectedHeader.Length == 0)
        {
            return true;
        }

        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            var actual = ReadFileHeader(filePath, expectedHeader.Length);
            return actual.SequenceEqual(expectedHeader);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static HashAlgorithm CreateHashAlgorithm(HashAlgorithmName algorithm)
    {
        if (algorithm == HashAlgorithmName.SHA256) return SHA256.Create();
        if (algorithm == HashAlgorithmName.SHA512) return SHA512.Create();
        if (algorithm == HashAlgorithmName.SHA384) return SHA384.Create();
        throw new NotSupportedException($"Hash algorithm '{algorithm.Name}' is not supported.");
    }

    /// <summary>
    /// Picks the copy buffer size from the size of the source file.
    /// </summary>
    /// <param name="fileSize">File size in bytes.</param>
    /// <returns>The buffer size, between 64 KB and 16 MB.</returns>
    public static int CalculateOptimalBufferSize(long fileSize)
    {
        const int smallFileThreshold = 1 * 1024 * 1024;
        const int largeFileThreshold = 500 * 1024 * 1024;

        const int buffer64K = 64 * 1024;
        const int buffer128K = 128 * 1024;
        const int buffer256K = 256 * 1024;
        const int buffer512K = 512 * 1024;
        const int buffer1M = 1024 * 1024;
        const int buffer2M = 2 * 1024 * 1024;
        const int buffer4M = 4 * 1024 * 1024;
        const int buffer8M = 8 * 1024 * 1024;
        const int buffer16M = 16 * 1024 * 1024;

        if (fileSize < smallFileThreshold) return buffer64K;
        if (fileSize > largeFileThreshold) return buffer16M;

        long proposed = fileSize / 8;
        if (proposed <= buffer64K) return buffer64K;
        if (proposed <= buffer128K) return buffer128K;
        if (proposed <= buffer256K) return buffer256K;
        if (proposed <= buffer512K) return buffer512K;
        if (proposed <= buffer1M) return buffer1M;
        if (proposed <= buffer2M) return buffer2M;
        if (proposed <= buffer4M) return buffer4M;
        if (proposed <= buffer8M) return buffer8M;

        return buffer16M;
    }

    /// <summary>
    /// Formats a byte count with its unit (B, KB, MB, GB, TB, PB).
    /// </summary>
    /// <param name="bytes">Byte count.</param>
    /// <returns>The formatted value with its unit.</returns>
    public static string FormatFileSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB", "PB"];
        int i;
        double dblBytes = bytes;
        for (i = 0; i < suffixes.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblBytes = bytes / 1024.0;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", dblBytes, suffixes[i]);
    }

    private static void TryWriteProgress(int currentProgress, long totalBytesRead)
    {
        int width = GetConsoleWidthSafe();
        string progressText = $"\rProgression : {currentProgress}% [{FormatFileSize(totalBytesRead)}]";
        Console.Write(progressText.PadRight(Math.Max(1, width - 1)));
    }

    private static void TryWriteLine()
    {
        Console.WriteLine();
    }

    private static int GetConsoleWidthSafe()
    {
        return 80;
    }
}

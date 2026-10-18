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
/// Gère la copie sécurisée de fichiers individuels, de leurs métadonnées, ACL et vérification d'intégrité par hachage.
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
    /// Copie un fichier vers sa destination via un fichier temporaire (.fileforge_tmp) pour assurer la cohérence transactionnelle.
    /// </summary>
    /// <param name="sourcePath">Chemin du fichier source.</param>
    /// <param name="destPath">Chemin du fichier de destination.</param>
    /// <param name="bufferSize">Taille du tampon de copie en octets.</param>
    /// <param name="displayProgress">Indique s'il faut afficher la progression sur la console en direct.</param>
    /// <param name="verifyHash">Indique si l'intégrité du fichier doit être vérifiée par empreinte SHA256 après copie.</param>
    /// <param name="progressCallback">Délégation optionnelle notifiée du nombre d'octets transférés et de la taille totale.</param>
    /// <param name="cancellationToken">Jeton d'annulation coopérative.</param>
    /// <returns>True si la copie et la vérification ont réussi, sinon false.</returns>
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
                RaiseLog($"[WARN] Incohérence de hachage SHA256 après copie : {sourcePath}");
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
            RaiseLog($"[ERROR] Erreur lors de la copie du fichier ({sourcePath} -> {destPath}) : {ex.Message}");
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
            RaiseLog($"[WARN] Échec du nettoyage du fichier temporaire ({tempPath}) : {cleanupEx.Message}");
        }
    }

    /// <summary>
    /// Copie les métadonnées (dates, attributs et optionnellement les ACL) d'un élément vers un autre.
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
    /// Copie les listes de contrôle d'accès (ACL).
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
            throw new FileNotFoundException($"Le chemin source n'existe pas : {sourcePath}");
        }
    }

    /// <summary>
    /// Compare les empreintes de hachage SHA256 de deux fichiers pour s'assurer de leur stricte identité binaire.
    /// </summary>
    /// <param name="path1">Chemin du premier fichier.</param>
    /// <param name="path2">Chemin du second fichier.</param>
    /// <returns>True si les deux empreintes sont rigoureusement identiques, sinon false.</returns>
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
    /// Calcule l'empreinte de hachage hexadécimale d'un fichier physique.
    /// </summary>
    /// <param name="filePath">Chemin d'accès au fichier.</param>
    /// <param name="algorithm">Algorithme de hachage cryptographique souhaité (SHA256 par défaut).</param>
    /// <returns>Empreinte hexadécimale en minuscules.</returns>
    public static string ComputeFileHash(string filePath, HashAlgorithmName? algorithm = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return ComputeHash(stream, algorithm);
    }

    /// <summary>
    /// Calcule de manière asynchrone l'empreinte de hachage hexadécimale d'un fichier physique.
    /// </summary>
    /// <param name="filePath">Chemin d'accès au fichier.</param>
    /// <param name="algorithm">Algorithme de hachage cryptographique souhaité (SHA256 par défaut).</param>
    /// <param name="cancellationToken">Jeton d'annulation coopérative.</param>
    /// <returns>Empreinte hexadécimale en minuscules.</returns>
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
    /// Calcule l'empreinte de hachage hexadécimale à partir d'un flux de données.
    /// </summary>
    /// <param name="stream">Flux source à hacher.</param>
    /// <param name="algorithm">Algorithme de hachage cryptographique souhaité (SHA256 par défaut).</param>
    /// <returns>Empreinte hexadécimale en minuscules.</returns>
    public static string ComputeHash(Stream stream, HashAlgorithmName? algorithm = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var hasher = CreateHashAlgorithm(algorithm ?? HashAlgorithmName.SHA256);
        var hashBytes = hasher.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Calcule de manière asynchrone l'empreinte de hachage hexadécimale à partir d'un flux de données.
    /// </summary>
    /// <param name="stream">Flux source à hacher.</param>
    /// <param name="algorithm">Algorithme de hachage cryptographique souhaité (SHA256 par défaut).</param>
    /// <param name="cancellationToken">Jeton d'annulation coopérative.</param>
    /// <returns>Empreinte hexadécimale en minuscules.</returns>
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
    /// Vérifie si l'empreinte de hachage d'un fichier correspond à la valeur hexadécimale attendue.
    /// </summary>
    /// <param name="filePath">Chemin du fichier à vérifier.</param>
    /// <param name="expectedHashHex">Empreinte hexadécimale attendue (insensible à la casse).</param>
    /// <param name="algorithm">Algorithme de hachage cryptographique souhaité (SHA256 par défaut).</param>
    /// <returns>True si les empreintes correspondent rigoureusement, sinon false.</returns>
    public static bool VerifyFileHash(string filePath, string expectedHashHex, HashAlgorithmName? algorithm = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHashHex);

        var actualHash = ComputeFileHash(filePath, algorithm);
        return string.Equals(actualHash, expectedHashHex.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Lit de manière non bloquante jusqu'à un nombre donné d'octets au tout début d'un fichier physique.
    /// </summary>
    /// <param name="filePath">Chemin d'accès au fichier.</param>
    /// <param name="byteCount">Nombre d'octets à lire.</param>
    /// <returns>Tableau d'octets lus (de longueur inférieure ou égale à byteCount).</returns>
    public static byte[] ReadFileHeader(string filePath, int byteCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteCount);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Le fichier spécifié est introuvable : {filePath}", filePath);
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
    /// Lit de manière asynchrone et non bloquante jusqu'à un nombre donné d'octets au tout début d'un fichier physique.
    /// </summary>
    /// <param name="filePath">Chemin d'accès au fichier.</param>
    /// <param name="byteCount">Nombre d'octets à lire.</param>
    /// <param name="cancellationToken">Jeton d'annulation coopérative.</param>
    /// <returns>Tableau d'octets lus (de longueur inférieure ou égale à byteCount).</returns>
    public static async Task<byte[]> ReadFileHeaderAsync(string filePath, int byteCount, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteCount);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Le fichier spécifié est introuvable : {filePath}", filePath);
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
    /// Vérifie si un fichier physique débute par la séquence d'octets spécifiée.
    /// </summary>
    /// <param name="filePath">Chemin d'accès au fichier.</param>
    /// <param name="expectedHeader">En-tête attendu sous forme d'octets.</param>
    /// <returns>True si le fichier existe et commence par les octets attendus, sinon false.</returns>
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
        throw new NotSupportedException($"L'algorithme de hachage '{algorithm.Name}' n'est pas pris en charge.");
    }

    /// <summary>
    /// Calcule la taille optimale de tampon de copie en fonction de la taille globale du fichier source.
    /// </summary>
    /// <param name="fileSize">Taille du fichier en octets.</param>
    /// <returns>Taille de tampon recommandée (entre 64 Ko et 16 Mo).</returns>
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
    /// Formate un nombre d'octets en chaîne lisible avec unité adéquate (o, Ko, Mo, Go, To, Po).
    /// </summary>
    /// <param name="bytes">Nombre d'octets.</param>
    /// <returns>Chaîne formatée avec unité.</returns>
    public static string FormatFileSize(long bytes)
    {
        string[] suffixes = ["o", "Ko", "Mo", "Go", "To", "Po"];
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

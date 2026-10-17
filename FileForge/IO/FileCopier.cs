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

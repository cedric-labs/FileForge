using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FileForge.Diagnostics;
using FileForge.IO;
using FileForge.Paths;
using FileForge.Terminal;

namespace FileForge.Sync;

/// <summary>
/// Runs mirror (clone) and incremental directory synchronization.
/// </summary>
public static class DirectorySynchronizer
{
    private static readonly object DirectoryLock = new();

    public static ConsoleDashboard? Dashboard { get; set; }

    public static event EventHandler<LogMessageEventArgs>? SynchronizerLog;

    private static void RaiseLog(string message)
    {
        SynchronizerLog?.Invoke(null, new LogMessageEventArgs(message));
    }

    /// <summary>
    /// Mirrors a source directory to a destination, according to the given options.
    /// </summary>
    /// <param name="sourceRoot">Source root directory.</param>
    /// <param name="destRoot">Destination root directory.</param>
    /// <param name="options">Synchronization and filtering options.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    public static void CloneDirectory(
        string sourceRoot,
        string destRoot,
        DirectorySyncOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destRoot);
        ArgumentNullException.ThrowIfNull(options);

        cancellationToken.ThrowIfCancellationRequested();
        using var dashboard = new ConsoleDashboard(options.TaskTitle ?? "Clone (Miroir)", sourceRoot, destRoot);
        Dashboard = dashboard;
        try
        {
            RaiseLog("[DEBUG] Listing the source items...");
            var srcItems = ParallelEnumerateFiles(
                sourceRoot,
                options.SourceListParallelism,
                options.IncludedFilters,
                options.ExcludedFilters,
                options.CloneOptions,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            RaiseLog("[DEBUG] Listing the target items...");
            var dstItems = ParallelEnumerateFiles(
                destRoot,
                options.DestinationListParallelism,
                options.IncludedFilters,
                options.ExcludedFilters,
                options.CloneOptions,
                cancellationToken);

            var srcFiles = srcItems.OfType<FileInfo>().ToList();
            var dstFiles = dstItems.OfType<FileInfo>().ToList();
            var srcDirs = srcItems.OfType<DirectoryInfo>().ToList();
            var dstDirs = dstItems.OfType<DirectoryInfo>().ToList();

            var srcDirsMap = srcDirs.ToDictionary(d => Path.GetRelativePath(sourceRoot, PathNormalizer.RemoveLongPathPrefix(d.FullName)));
            var dstDirsMap = dstDirs.ToDictionary(d => Path.GetRelativePath(destRoot, PathNormalizer.RemoveLongPathPrefix(d.FullName)));
            var srcFilesMap = srcFiles.ToDictionary(f => Path.GetRelativePath(sourceRoot, PathNormalizer.RemoveLongPathPrefix(f.FullName)));
            var dstFilesMap = dstFiles.ToDictionary(f => Path.GetRelativePath(destRoot, PathNormalizer.RemoveLongPathPrefix(f.FullName)));

            if (options.CloneOptions is not null)
            {
                srcFilesMap = srcFilesMap
                    .Where(kvp => !options.CloneOptions.IsExcluded(PathNormalizer.RemoveLongPathPrefix(kvp.Value.FullName)))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                srcDirsMap = srcDirsMap
                    .Where(kvp => !options.CloneOptions.IsExcluded(PathNormalizer.RemoveLongPathPrefix(kvp.Value.FullName)))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }

            // Create the missing folders
            foreach (var dir in srcDirsMap.Keys.Except(dstDirsMap.Keys))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fullDestDir = Path.Combine(destRoot, dir);
                Directory.CreateDirectory(fullDestDir);
            }

            // Copy the new or changed files
            var filesToCopy = srcFilesMap.Where(kvp =>
            {
                var relPath = kvp.Key;
                var srcFile = kvp.Value;
                return !dstFilesMap.TryGetValue(relPath, out var dstFile) || dstFile.LastWriteTime < srcFile.LastWriteTime;
            }).ToList();

            if (filesToCopy.Count > 0)
            {
                long totalBytes = filesToCopy.Sum(kvp => kvp.Value.Length);
                dashboard.SetTransferPlan(filesToCopy.Count, totalBytes);

                var copiedFiles = ParallelCopyFiles(filesToCopy, destRoot, options.CopyParallelism, options.VerifyHash, cancellationToken);
                int failedCount = filesToCopy.Count - copiedFiles.Count;
                if (failedCount > 0)
                {
                    RaiseLog($"[WARN] {failedCount} file(s) could not be copied to the destination.");
                }

                dashboard.Complete(failedCount > 0
                    ? $"Clone completed with {failedCount} failed file(s)."
                    : "Mirror clone completed.");
            }
            else
            {
                dashboard.SetUpToDate("Mirror folder already synchronized.");
                RaiseLog("[DEBUG] No change detected. No file written.");
            }

            // Delete the obsolete files
            foreach (var path in dstFilesMap.Keys.Except(srcFilesMap.Keys))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fullPath = Path.Combine(destRoot, path);
                try
                {
                    File.Delete(fullPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    RaiseLog($"[WARN] Cannot delete the file ({fullPath}): {ex.Message}");
                }
            }

            // Delete the obsolete folders
            foreach (var path in dstDirsMap.Keys.Except(srcDirsMap.Keys).OrderByDescending(p => p.Count(c => c == Path.DirectorySeparatorChar)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fullPath = PathNormalizer.NormalizeLongPath(Path.Combine(destRoot, path));

                if (options.CloneOptions is not null && options.CloneOptions.ExcludedPaths.Contains(path))
                {
                    if (Directory.Exists(fullPath))
                    {
                        try
                        {
                            ForceDeleteDirectory(fullPath);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            RaiseLog($"[WARN] Cannot delete the excluded folder ({fullPath}): {ex.Message}");
                        }
                    }
                    continue;
                }

                if (Directory.Exists(fullPath) && !Directory.EnumerateFileSystemEntries(fullPath).Any())
                {
                    try
                    {
                        Directory.Delete(fullPath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        RaiseLog($"[WARN] Cannot delete the folder ({fullPath}): {ex.Message}");
                    }
                }
            }
        }
        finally
        {
            Dashboard = null;
        }
    }

    /// <summary>
    /// Runs a strict incremental backup into a new dated folder, according to the given options.
    /// </summary>
    /// <param name="sourceRoot">Source root directory.</param>
    /// <param name="destBaseDir">Base directory that receives the incremental backups.</param>
    /// <param name="options">Synchronization and filtering options.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    public static void IncrementalBackup(
        string sourceRoot,
        string destBaseDir,
        DirectorySyncOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destBaseDir);
        ArgumentNullException.ThrowIfNull(options);

        cancellationToken.ThrowIfCancellationRequested();
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm", System.Globalization.CultureInfo.InvariantCulture);
        string finalBackupPath = Path.Combine(destBaseDir, $"Backup_{timestamp}");
        string tempBackupPath = Path.Combine(destBaseDir, "Backup_In_Progress");
        string indexFilePath = Path.Combine(destBaseDir, "backup_catalog.dat");

        using var dashboard = new ConsoleDashboard(options.TaskTitle ?? "Incremental", sourceRoot, destBaseDir);
        Dashboard = dashboard;
        try
        {
            RaiseLog("[DEBUG] Preparing the incremental backup in the temporary folder.");

            if (Directory.Exists(tempBackupPath))
            {
                RaiseLog("[WARN] An existing temporary folder was found. Cleaning it up...");
                ForceDeleteDirectory(tempBackupPath);
            }

            var srcItems = ParallelEnumerateFiles(
                sourceRoot,
                options.SourceListParallelism,
                options.IncludedFilters,
                options.ExcludedFilters,
                options.CloneOptions,
                cancellationToken);

            var srcFiles = srcItems.OfType<FileInfo>().ToList();
            var srcDirs = srcItems.OfType<DirectoryInfo>().ToList();

            var srcFilesMap = srcFiles.ToDictionary(f => Path.GetRelativePath(sourceRoot, PathNormalizer.RemoveLongPathPrefix(f.FullName)));
            var srcDirsMap = srcDirs.ToDictionary(d => Path.GetRelativePath(sourceRoot, PathNormalizer.RemoveLongPathPrefix(d.FullName)));

            if (options.CloneOptions is not null)
            {
                srcFilesMap = srcFilesMap.Where(kvp => !options.CloneOptions.IsExcluded(PathNormalizer.RemoveLongPathPrefix(kvp.Value.FullName))).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                srcDirsMap = srcDirsMap.Where(kvp => !options.CloneOptions.IsExcluded(PathNormalizer.RemoveLongPathPrefix(kvp.Value.FullName))).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }

            var previousCatalog = SyncCatalogRepository.LoadCatalog(indexFilePath);
            var currentCatalog = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pendingFingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var filesToCopy = new List<KeyValuePair<string, FileInfo>>();
            var dirsToCreate = new HashSet<string>();

            foreach (var kvp in srcFilesMap)
            {
                string relPath = kvp.Key;
                var srcFile = kvp.Value;
                string fileFingerprint = $"{srcFile.LastWriteTimeUtc.Ticks}_{srcFile.Length}";

                if (!previousCatalog.TryGetValue(relPath, out var prevFingerprint) || prevFingerprint != fileFingerprint)
                {
                    filesToCopy.Add(kvp);
                    pendingFingerprints[relPath] = fileFingerprint;

                    string? parentDir = Path.GetDirectoryName(relPath);
                    while (!string.IsNullOrEmpty(parentDir))
                    {
                        if (srcDirsMap.ContainsKey(parentDir))
                        {
                            dirsToCreate.Add(parentDir);
                        }
                        parentDir = Path.GetDirectoryName(parentDir);
                    }
                }
                else
                {
                    currentCatalog[relPath] = fileFingerprint;
                }
            }

            if (filesToCopy.Count > 0)
            {
                Directory.CreateDirectory(tempBackupPath);

                foreach (var dir in dirsToCreate.OrderBy(p => p.Length))
                {
                    string fullDestDir = Path.Combine(tempBackupPath, dir);
                    if (!Directory.Exists(fullDestDir))
                    {
                        Directory.CreateDirectory(fullDestDir);
                        FileCopier.CopyMetadata(Path.Combine(sourceRoot, dir), fullDestDir, false);
                    }
                }

                RaiseLog($"[DEBUG] Copying {filesToCopy.Count} file(s) to the temporary storage...");
                long totalBytes = filesToCopy.Sum(kvp => kvp.Value.Length);
                dashboard.SetTransferPlan(filesToCopy.Count, totalBytes);

                var copiedFiles = ParallelCopyFiles(filesToCopy, tempBackupPath, options.CopyParallelism, options.VerifyHash, cancellationToken);

                int failedCount = filesToCopy.Count - copiedFiles.Count;
                if (failedCount > 0)
                {
                    RaiseLog($"[WARN] {failedCount} file(s) could not be copied and will be retried on the next run.");
                }

                foreach (var relPath in copiedFiles)
                {
                    currentCatalog[relPath] = pendingFingerprints[relPath];
                }

                RaiseLog($"[DEBUG] Committing the backup. Renaming to {finalBackupPath}...");
                if (Directory.Exists(finalBackupPath))
                {
                    finalBackupPath += "_" + DateTime.Now.ToString("ss", System.Globalization.CultureInfo.InvariantCulture);
                }

                Directory.Move(tempBackupPath, finalBackupPath);
                SyncCatalogRepository.SaveCatalog(currentCatalog, indexFilePath);
                dashboard.Complete(failedCount > 0
                    ? $"Backup committed with {failedCount} failed file(s)."
                    : "Incremental backup committed and saved.");
                RaiseLog("[DEBUG] Incremental backup committed and saved.");
            }
            else
            {
                dashboard.SetUpToDate("Disk already up to date, no change detected.");
                RaiseLog("[DEBUG] No change detected. No file written.");
            }
        }
        finally
        {
            Dashboard = null;
        }
    }

    /// <summary>
    /// Walks a directory recursively and in parallel to list the files and subfolders that match the filters.
    /// </summary>
    /// <param name="rootPath">Root path to walk.</param>
    /// <param name="maxParallelism">Maximum degree of parallelism.</param>
    /// <param name="includeFilters">Semicolon-separated inclusion filters.</param>
    /// <param name="excludeFilters">Semicolon-separated exclusion filters.</param>
    /// <param name="options">Global exclusion options.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>The file system items found.</returns>
    public static List<FileSystemInfo> ParallelEnumerateFiles(
        string rootPath,
        int maxParallelism,
        string includeFilters = "*",
        string excludeFilters = "",
        DirectoryCloneOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int activeCount = 0;
        var files = new ConcurrentBag<FileSystemInfo>();
        var dirsToScan = new ConcurrentQueue<string>();
        string normalizedRoot = PathNormalizer.NormalizeLongPath(rootPath);
        dirsToScan.Enqueue(normalizedRoot);
        var tasks = new List<Task>();

        int totalItemsFound = 0;
        int processedDirs = 0;

        var includePatterns = includeFilters.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (includePatterns.Length == 0) includePatterns = ["*"];

        var excludePatterns = excludeFilters.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var includeRegexes = includePatterns.Select(ConvertWildcardToRegex).ToArray();
        var excludeRegexes = excludePatterns.Select(ConvertWildcardToRegex).ToArray();

        int effectiveParallelism = Math.Max(1, maxParallelism);
        for (int i = 0; i < effectiveParallelism; i++)
        {
            if (i == 1)
            {
                Task.Delay(100, cancellationToken).Wait(100, cancellationToken);
            }

            tasks.Add(Task.Run(() =>
            {
                bool process = true;

                while (process)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!dirsToScan.TryDequeue(out var currentDir))
                    {
                        if (dirsToScan.IsEmpty && Interlocked.CompareExchange(ref activeCount, 0, 0) == 0)
                        {
                            process = false;
                        }
                    }
                    else
                    {
                        Interlocked.Increment(ref activeCount);
                        Interlocked.Increment(ref processedDirs);
                        Dashboard?.UpdateScanning(currentDir, totalItemsFound);

                        try
                        {
                            foreach (var subdir in Directory.EnumerateDirectories(currentDir))
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var normalizedSubdir = PathNormalizer.NormalizeLongPath(subdir);
                                string absoluteSubdir = PathNormalizer.RemoveLongPathPrefix(normalizedSubdir);

                                if (options is not null && options.IsExcluded(absoluteSubdir))
                                {
                                    continue;
                                }

                                files.Add(new DirectoryInfo(normalizedSubdir));
                                dirsToScan.Enqueue(normalizedSubdir);
                                Interlocked.Increment(ref totalItemsFound);
                            }

                            foreach (var file in Directory.EnumerateFiles(currentDir))
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                string fileName = Path.GetFileName(file);

                                if (MatchesFilter(fileName, includeRegexes, excludeRegexes))
                                {
                                    var normalizedFile = PathNormalizer.NormalizeLongPath(file);
                                    string absoluteFile = PathNormalizer.RemoveLongPathPrefix(normalizedFile);

                                    if (options is not null && options.IsExcluded(absoluteFile))
                                    {
                                        continue;
                                    }

                                    files.Add(new FileInfo(normalizedFile));
                                    Interlocked.Increment(ref totalItemsFound);
                                }
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            RaiseLog($"[WARN] Cannot scan {currentDir}: {ex.Message}");
                        }
                        finally
                        {
                            Interlocked.Decrement(ref activeCount);
                        }
                    }
                }
            }, cancellationToken));
        }

        Task.WaitAll([.. tasks], cancellationToken);
        return [.. files];
    }

    private static HashSet<string> ParallelCopyFiles(
        List<KeyValuePair<string, FileInfo>> filesToCopy,
        string destRoot,
        int parallelism,
        bool verifyHash,
        CancellationToken cancellationToken = default)
    {
        var succeeded = new ConcurrentBag<string>();
        if (filesToCopy.Count == 0)
        {
            return [.. succeeded];
        }

        var sortedFiles = filesToCopy
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var filesQueue = new ConcurrentQueue<KeyValuePair<string, FileInfo>>(sortedFiles);
        var tasks = new List<Task>();
        int activeWorkers = 0;
        int effectiveParallelism = Math.Max(1, parallelism);

        for (int i = 0; i < effectiveParallelism; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                Interlocked.Increment(ref activeWorkers);
                try
                {
                    while (filesQueue.TryDequeue(out var kvp))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var relPath = kvp.Key;
                        var srcFile = kvp.Value;
                        var destPath = Path.Combine(destRoot, relPath);

                        var destDir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        {
                            lock (DirectoryLock)
                            {
                                if (!Directory.Exists(destDir))
                                {
                                    Directory.CreateDirectory(destDir);
                                }
                            }
                        }

                        if (srcFile.Exists)
                        {
                            int bufferSize = FileCopier.CalculateOptimalBufferSize(srcFile.Length);
                            Dashboard?.UpdateCurrentFile(relPath, 0, srcFile.Length);

                            bool copyOk = FileCopier.CopyFile(
                                srcFile.FullName,
                                PathNormalizer.NormalizeLongPath(destPath),
                                bufferSize,
                                displayProgress: parallelism <= 1 && Dashboard is null,
                                verifyHash: verifyHash,
                                progressCallback: (bytesCopied, totalBytes) =>
                                {
                                    Dashboard?.UpdateCurrentFile(relPath, bytesCopied, totalBytes);
                                },
                                cancellationToken: cancellationToken);

                            if (copyOk)
                            {
                                succeeded.Add(relPath);
                                Dashboard?.CompleteFile(srcFile.Length);
                            }
                            else
                            {
                                Dashboard?.FailFile(srcFile.Length);
                            }
                        }
                        else
                        {
                            RaiseLog($"[WARN] Source file not found: {srcFile.FullName}");
                            Dashboard?.FailFile(srcFile.Length);
                        }
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref activeWorkers);
                }
            }, cancellationToken));
        }

        Task.WaitAll([.. tasks], cancellationToken);
        return [.. succeeded];
    }

    private static Regex ConvertWildcardToRegex(string pattern)
    {
        string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return new Regex(regexPattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
    }

    private static bool MatchesFilter(string fileName, Regex[] includeRegexes, Regex[] excludeRegexes)
    {
        bool included = includeRegexes.Length == 0 || includeRegexes.Any(r => r.IsMatch(fileName));
        bool excluded = excludeRegexes.Length > 0 && excludeRegexes.Any(r => r.IsMatch(fileName));
        return included && !excluded;
    }

    /// <summary>
    /// Deletes a directory recursively, clearing the ReadOnly and System attributes first.
    /// </summary>
    /// <param name="directoryPath">Path of the directory to delete.</param>
    public static void ForceDeleteDirectory(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        string normalizedPath = PathNormalizer.NormalizeLongPath(directoryPath);
        if (!Directory.Exists(normalizedPath))
        {
            return;
        }

        StripReadOnlyAndSystemAttributes(normalizedPath);
        Directory.Delete(normalizedPath, recursive: true);
    }

    private static void StripReadOnlyAndSystemAttributes(string directoryPath)
    {
        var dirInfo = new DirectoryInfo(directoryPath);
        if ((dirInfo.Attributes & (FileAttributes.ReadOnly | FileAttributes.System)) != FileAttributes.None)
        {
            File.SetAttributes(directoryPath, FileAttributes.Directory);
        }

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            var fileAttr = File.GetAttributes(file);
            if ((fileAttr & (FileAttributes.ReadOnly | FileAttributes.System)) != FileAttributes.None)
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
        }

        foreach (var subDir in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.AllDirectories))
        {
            var subDirAttr = File.GetAttributes(subDir);
            if ((subDirAttr & (FileAttributes.ReadOnly | FileAttributes.System)) != FileAttributes.None)
            {
                File.SetAttributes(subDir, FileAttributes.Directory);
            }
        }
    }
}

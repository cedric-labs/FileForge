using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FileForge.Diagnostics;

namespace FileForge.IO;

/// <summary>
/// Saves, loads and rebuilds the index catalogs used by incremental backups.
/// </summary>
public static partial class SyncCatalogRepository
{
    public static event EventHandler<LogMessageEventArgs>? CatalogLog;

    private static void RaiseLog(string message)
    {
        CatalogLog?.Invoke(null, new LogMessageEventArgs(message));
    }

    [GeneratedRegex(@"^sI\d+$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex LegacyFolderRegex();

    [GeneratedRegex(@"^Backup_\d{8}_\d{4}(?:_\d+)?$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex BackupFolderRegex();

    /// <summary>
    /// Loads a binary index catalog.
    /// </summary>
    /// <param name="catalogPath">Path of the binary catalog file (.dat).</param>
    /// <returns>A dictionary mapping each relative path to the file's timestamped fingerprint.</returns>
    public static Dictionary<string, string> LoadCatalog(string catalogPath)
    {
        var catalog = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(catalogPath))
        {
            return catalog;
        }

        try
        {
            using var reader = new BinaryReader(File.OpenRead(catalogPath), Encoding.UTF8);
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                string key = reader.ReadString();
                string val = reader.ReadString();
                catalog[key] = val;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            RaiseLog($"[WARN] Index catalog corrupted or unreadable ({catalogPath}): {ex.Message}. Starting from an empty catalog.");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return catalog;
    }

    /// <summary>
    /// Saves the index catalog to a compact binary file.
    /// </summary>
    /// <param name="catalog">Items and fingerprints to save.</param>
    /// <param name="catalogPath">Path of the catalog file (.dat).</param>
    public static void SaveCatalog(Dictionary<string, string> catalog, string catalogPath)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);

        try
        {
            using var writer = new BinaryWriter(File.Open(catalogPath, FileMode.Create, FileAccess.Write), Encoding.UTF8);
            writer.Write(catalog.Count);
            foreach (var kvp in catalog)
            {
                writer.Write(kvp.Key);
                writer.Write(kvp.Value);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RaiseLog($"[WARN] Could not save the index catalog ({catalogPath}): {ex.Message}");
        }
    }

    /// <summary>
    /// Renames legacy increment folders ("sI1", "sI2"...) to the "Backup_YYYYMMDD_HHmm" format.
    /// </summary>
    /// <param name="destBaseDir">Root folder holding the backup folders.</param>
    public static void MigrateLegacyBackupNames(string destBaseDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destBaseDir);

        if (!Directory.Exists(destBaseDir))
        {
            return;
        }

        RaiseLog("[MIGRATION] Renaming legacy folders...");

        var directories = Directory.GetDirectories(destBaseDir)
            .Select(d => new DirectoryInfo(d))
            .Where(d => LegacyFolderRegex().IsMatch(d.Name))
            .OrderBy(d => d.CreationTimeUtc)
            .ToList();

        foreach (var dir in directories)
        {
            string timestamp = dir.CreationTime.ToString("yyyyMMdd_HHmm", System.Globalization.CultureInfo.InvariantCulture);
            string newName = $"Backup_{timestamp}";
            string newFullPath = Path.Combine(destBaseDir, newName);

            int counter = 1;
            while (Directory.Exists(newFullPath))
            {
                newName = $"Backup_{timestamp}_{counter}";
                newFullPath = Path.Combine(destBaseDir, newName);
                counter++;
            }

            RaiseLog($"[MIGRATION] Renaming {dir.Name} -> {newName}");
            try
            {
                Directory.Move(dir.FullName, newFullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                RaiseLog($"[ERROR] Could not rename {dir.Name}: {ex.Message}");
            }
        }

        RaiseLog("[MIGRATION] Renaming complete.");
    }

    /// <summary>
    /// Rebuilds an initial index catalog from the increment folders already on the target disk.
    /// </summary>
    /// <param name="sourceRoot">Source root folder.</param>
    /// <param name="destBaseDir">Target root folder holding the existing increments.</param>
    public static void BootstrapInitialCatalog(string sourceRoot, string destBaseDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destBaseDir);

        string indexFilePath = Path.Combine(destBaseDir, "backup_catalog.dat");

        if (File.Exists(indexFilePath))
        {
            RaiseLog("[BOOTSTRAP] A catalog already exists. Rebuild skipped.");
            return;
        }

        RaiseLog("[BOOTSTRAP] Rebuilding the catalog from the increment stack...");

        var legacyDirs = Directory.GetDirectories(destBaseDir)
            .Select(d => new DirectoryInfo(d))
            .Where(d => BackupFolderRegex().IsMatch(d.Name))
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .ToList();

        if (legacyDirs.Count == 0)
        {
            RaiseLog("[BOOTSTRAP] No increment folder found. The catalog will be created empty.");
            return;
        }

        var currentCatalog = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var backupDir in legacyDirs)
        {
            RaiseLog($"[BOOTSTRAP] Processing increment: {backupDir.Name}");

            var files = Directory.EnumerateFiles(backupDir.FullName, "*", SearchOption.AllDirectories);
            foreach (string filePath in files)
            {
                string relPath = Path.GetRelativePath(backupDir.FullName, filePath);
                string expectedSourcePath = Path.Combine(sourceRoot, relPath);

                if (File.Exists(expectedSourcePath))
                {
                    var srcFileInfo = new FileInfo(expectedSourcePath);
                    currentCatalog[relPath] = $"{srcFileInfo.LastWriteTimeUtc.Ticks}_{srcFileInfo.Length}";
                }
                else
                {
                    var fileInfo = new FileInfo(filePath);
                    currentCatalog[relPath] = $"{fileInfo.LastWriteTimeUtc.Ticks}_{fileInfo.Length}";
                }
            }
        }

        SaveCatalog(currentCatalog, indexFilePath);
        RaiseLog($"[BOOTSTRAP] Catalog rebuilt: {currentCatalog.Count} unique files.");
    }
}

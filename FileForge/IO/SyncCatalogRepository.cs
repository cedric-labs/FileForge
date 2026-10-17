using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FileForge.Diagnostics;

namespace FileForge.IO;

/// <summary>
/// Gère la persistance, le chargement et la reconstruction des catalogues d'index pour les sauvegardes incrémentales.
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
            RaiseLog($"[WARN] Catalogue d'index corrompu ou illisible ({catalogPath}) : {ex.Message}. Réinitialisation d'un catalogue vierge.");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return catalog;
    }

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
            RaiseLog($"[WARN] Impossible de sauvegarder le catalogue d'index ({catalogPath}) : {ex.Message}");
        }
    }

    public static void MigrateLegacyBackupNames(string destBaseDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destBaseDir);

        if (!Directory.Exists(destBaseDir))
        {
            return;
        }

        RaiseLog("[MIGRATION] Démarrage du renommage des anciens dossiers...");

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

            RaiseLog($"[MIGRATION] Renommage de {dir.Name} -> {newName}");
            try
            {
                Directory.Move(dir.FullName, newFullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                RaiseLog($"[ERROR] Échec lors du renommage de {dir.Name} : {ex.Message}");
            }
        }

        RaiseLog("[MIGRATION] Renommage terminé.");
    }

    public static void BootstrapInitialCatalog(string sourceRoot, string destBaseDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destBaseDir);

        string indexFilePath = Path.Combine(destBaseDir, "backup_catalog.dat");

        if (File.Exists(indexFilePath))
        {
            RaiseLog("[BOOTSTRAP] Un catalogue existe déjà. Initialisation avortée.");
            return;
        }

        RaiseLog("[BOOTSTRAP] Reconstitution du catalogue à partir de la pile d'incréments...");

        var legacyDirs = Directory.GetDirectories(destBaseDir)
            .Select(d => new DirectoryInfo(d))
            .Where(d => BackupFolderRegex().IsMatch(d.Name))
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .ToList();

        if (legacyDirs.Count == 0)
        {
            RaiseLog("[BOOTSTRAP] Aucun dossier incrémental détecté. Le catalogue sera créé vide.");
            return;
        }

        var currentCatalog = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var backupDir in legacyDirs)
        {
            RaiseLog($"[BOOTSTRAP] Traitement de l'incrément : {backupDir.Name}");

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
        RaiseLog($"[BOOTSTRAP] Catalogue initialisé avec succès. {currentCatalog.Count} fichiers uniques référencés.");
    }
}

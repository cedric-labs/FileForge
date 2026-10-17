using System;
using System.IO;
using System.Linq;
using System.Management;
using FileForge.Diagnostics;

namespace FileForge.Storage;

/// <summary>
/// Détermine les capacités matérielles (SSD, RAM, cœurs CPU) pour optimiser le degré de parallélisme.
/// </summary>
public static class HardwarePerformanceAdvisor
{
    public static event EventHandler<LogMessageEventArgs>? AdvisorLog;

    private static void RaiseLog(string message)
    {
        AdvisorLog?.Invoke(null, new LogMessageEventArgs(message));
    }

    /// <summary>
    /// Calcule le degré de parallélisme optimal en fonction des cœurs logiques, du type de disque et de la RAM.
    /// </summary>
    public static int CalculateOptimalParallelism(string sourcePath, string destPath)
    {
        int processorCount = Environment.ProcessorCount;
        bool isSourceSsd = IsSsdDrive(sourcePath);
        bool isDestSsd = IsSsdDrive(destPath);
        double availableRamGb = GetAvailableMemoryInGB();

        int baseParallelism;

        if (isSourceSsd && isDestSsd)
        {
            baseParallelism = Math.Min(processorCount, 16);
        }
        else
        {
            baseParallelism = 1;
        }

        if (availableRamGb < 2)
        {
            baseParallelism = Math.Min(2, baseParallelism);
        }

        return baseParallelism;
    }

    /// <summary>
    /// Détermine si un chemin est localisé sur un disque SSD via WMI.
    /// </summary>
    public static bool IsSsdDrive(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string? driveLetter = Path.GetPathRoot(Path.GetFullPath(path))?.TrimEnd('\\');
            if (string.IsNullOrEmpty(driveLetter))
            {
                return false;
            }

            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
            foreach (var disk in searcher.Get())
            {
                string model = disk["Model"]?.ToString() ?? string.Empty;
                string mediaType = disk["MediaType"]?.ToString() ?? string.Empty;

                using var partitionSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{disk["DeviceID"]}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition");

                foreach (var partition in partitionSearcher.Get())
                {
                    using var logicalDiskSearcher = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass = Win32_LogicalDiskToPartition");

                    foreach (var logicalDisk in logicalDiskSearcher.Get())
                    {
                        if (string.Equals(logicalDisk["Name"]?.ToString(), driveLetter, StringComparison.OrdinalIgnoreCase))
                        {
                            return model.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                                   mediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                                   model.Contains("NVMe", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
            }

            return false;
        }
        catch (ManagementException ex)
        {
            RaiseLog($"[WARN] Détection SSD impossible via WMI ({path}) : {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            RaiseLog($"[WARN] Accès refusé pour la détection du disque ({path}) : {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Récupère la mémoire vive physique disponible en Gigaoctets via WMI.
    /// </summary>
    public static double GetAvailableMemoryInGB()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT FreePhysicalMemory FROM Win32_OperatingSystem");
            using var results = searcher.Get();
            var osObject = results.OfType<ManagementBaseObject>().FirstOrDefault();
            if (osObject is not null)
            {
                var freeKb = Convert.ToDouble(osObject["FreePhysicalMemory"], System.Globalization.CultureInfo.InvariantCulture);
                return freeKb / (1024.0 * 1024.0);
            }

            return 4.0;
        }
        catch (ManagementException ex)
        {
            RaiseLog($"[WARN] Récupération de la mémoire libre impossible ({ex.Message}). Utilisation valeur par défaut (4 Go).");
            return 4.0;
        }
    }
}

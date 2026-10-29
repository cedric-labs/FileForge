using System;
using System.IO;
using System.Linq;
using System.Management;
using FileForge.Diagnostics;

namespace FileForge.Storage;

/// <summary>
/// Reads the hardware capabilities (SSD, RAM, CPU cores) to choose the degree of parallelism.
/// </summary>
public static class HardwarePerformanceAdvisor
{
    public static event EventHandler<LogMessageEventArgs>? AdvisorLog;

    private static void RaiseLog(string message)
    {
        AdvisorLog?.Invoke(null, new LogMessageEventArgs(message));
    }

    /// <summary>
    /// Computes the degree of parallelism from the logical cores, the disk type and the free RAM.
    /// </summary>
    /// <param name="sourcePath">Source path.</param>
    /// <param name="destPath">Destination path.</param>
    /// <returns>The recommended degree of parallelism, from 1 to 16.</returns>
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
    /// Uses WMI to tell whether a path is on an SSD.
    /// </summary>
    /// <param name="path">Path or drive letter.</param>
    /// <returns>True when the underlying drive is an SSD or NVMe disk; otherwise false.</returns>
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
            RaiseLog($"[WARN] Could not detect an SSD through WMI ({path}): {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            RaiseLog($"[WARN] Access denied while detecting the disk ({path}): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Uses WMI to read the free physical memory in gigabytes.
    /// </summary>
    /// <returns>The free memory in GB (4.0 GB when it cannot be read).</returns>
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
            RaiseLog($"[WARN] Could not read the free memory ({ex.Message}). Using the default value (4 GB).");
            return 4.0;
        }
    }
}

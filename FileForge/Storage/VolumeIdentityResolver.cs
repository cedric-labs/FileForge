using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FileForge.Storage;

/// <summary>
/// Resolves a stable identifier for a local volume or a UNC network share.
/// </summary>
public static class VolumeIdentityResolver
{
    [DllImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(
        string lpRootPathName,
        StringBuilder lpVolumeNameBuffer,
        uint nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        StringBuilder lpFileSystemNameBuffer,
        uint nFileSystemNameSize);

    /// <summary>
    /// Returns a stable identifier for a local path (volume serial number) or a UNC path (server\share).
    /// </summary>
    /// <param name="path">Local or UNC path.</param>
    /// <returns>An identifier such as "VOL:XXXXXXXX" or "UNC:\\server\share", or null when it cannot be resolved.</returns>
    public static string? GetDiskId(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        // Cas UNC : \\serveur\partage\...
        if (path.StartsWith('\\'))
        {
            string? uncRoot = GetUncRoot(path);
            return uncRoot is not null ? $"UNC:{uncRoot}" : null;
        }

        // Cas local : D:\...
        return GetVolumeIdFromPath(path);
    }

    /// <summary>
    /// Reads the serial number of a local volume through the Win32 GetVolumeInformation API.
    /// </summary>
    /// <param name="path">Local path.</param>
    /// <returns>A "VOL:XXXXXXXX" identifier, or null when it cannot be read.</returns>
    public static string? GetVolumeIdFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string? root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            return null;
        }

        var volumeName = new StringBuilder(261);
        var fileSystemName = new StringBuilder(261);

        bool success = GetVolumeInformationW(
            root,
            volumeName,
            (uint)volumeName.Capacity,
            out uint serialNumber,
            out _,
            out _,
            fileSystemName,
            (uint)fileSystemName.Capacity);

        if (!success)
        {
            return null;
        }

        return $"VOL:{serialNumber:X8}";
    }

    /// <summary>
    /// Extracts the root (\\server\share) of a UNC path.
    /// </summary>
    /// <param name="uncPath">UNC network path.</param>
    /// <returns>The \\server\share root, or the original path when it is not a valid UNC path.</returns>
    public static string? GetUncRoot(string? uncPath)
    {
        if (string.IsNullOrWhiteSpace(uncPath))
        {
            return null;
        }

        var trimmed = uncPath.TrimStart('\\');
        var parts = trimmed.Split('\\', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2)
        {
            return $"\\\\{parts[0]}\\{parts[1]}";
        }

        return uncPath;
    }

    /// <summary>
    /// Compares two disk or network share identifiers, ignoring spelling variants
    /// (e.g. "UNC:\server\share" and "UNC:\\server\share").
    /// </summary>
    /// <param name="actual">Resolved identifier.</param>
    /// <param name="expected">Identifier expected by the configuration.</param>
    /// <returns>True when both identifiers match; otherwise false.</returns>
    public static bool AreMatchingDiskIds(string? actual, string? expected)
    {
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (actual is null || expected is null)
        {
            return false;
        }

        return string.Equals(NormalizeDiskId(actual), NormalizeDiskId(expected), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes a disk identifier to its canonical form.
    /// For UNC network shares, rewrites the prefix to the "UNC:\\server\share" form.
    /// </summary>
    /// <param name="diskId">Disk identifier to normalize.</param>
    /// <returns>The normalized identifier.</returns>
    public static string NormalizeDiskId(string diskId)
    {
        if (string.IsNullOrWhiteSpace(diskId))
        {
            return string.Empty;
        }

        if (diskId.StartsWith("UNC:", StringComparison.OrdinalIgnoreCase))
        {
            string uncBody = diskId["UNC:".Length..]
                .Replace('/', '\\')
                .TrimStart('\\');
            return $"UNC:\\\\{uncBody}";
        }

        return diskId;
    }
}

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FileForge.Storage;

/// <summary>
/// Détermine et extrait un identifiant stable pour un volume local ou un partage réseau UNC.
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
    /// Retourne un identifiant stable pour un chemin local (numéro de série du volume) ou UNC (serveur\partage).
    /// </summary>
    /// <param name="path">Chemin local ou UNC à identifier.</param>
    /// <returns>Identifiant stable sous forme "VOL:XXXXXXXX" ou "UNC:\\serveur\partage", ou null si non résolu.</returns>
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
    /// Utilise l'API Win32 GetVolumeInformation pour obtenir le numéro de série d'un volume local.
    /// </summary>
    /// <param name="path">Chemin local à analyser.</param>
    /// <returns>Identifiant "VOL:XXXXXXXX" ou null en cas d'erreur de lecture.</returns>
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
    /// Extrait la racine d'un chemin UNC (\\serveur\partage) de manière déterministe.
    /// </summary>
    /// <param name="uncPath">Chemin réseau UNC.</param>
    /// <returns>Racine \\serveur\partage ou le chemin original si invalide.</returns>
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
    /// Compare deux identifiants de disque ou de partage réseau en tolérant les variations d'écriture
    /// (ex: "UNC:\serveur\partage" et "UNC:\\serveur\partage").
    /// </summary>
    /// <param name="actual">Identifiant résolu réel.</param>
    /// <param name="expected">Identifiant attendu dans la configuration.</param>
    /// <returns>True si les deux identifiants correspondent, sinon false.</returns>
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
    /// Normalise la forme canonique d'un identifiant de disque.
    /// Pour les partages réseau UNC, harmonise le préfixe vers la syntaxe "UNC:\\serveur\partage".
    /// </summary>
    /// <param name="diskId">Identifiant de disque à normaliser.</param>
    /// <returns>Identifiant normalisé.</returns>
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

using System;
using System.IO;

namespace FileForge.Paths;

/// <summary>
/// Normalise et convertit les chemins d'accès pour supporter les chemins longs Windows (>260 caractères) et UNC.
/// </summary>
public static class PathNormalizer
{
    /// <summary>
    /// Normalise un chemin pour supporter les chemins longs sous Windows avec le préfixe étendu.
    /// </summary>
    public static string NormalizeLongPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = Path.GetFullPath(path).Replace('/', '\\');

        if (full.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return full;
        }

        if (full.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return string.Concat(@"\\?\UNC\", full.AsSpan(2));
        }

        return @"\\?\" + full;
    }

    /// <summary>
    /// Supprime les préfixes de chemin long (\\?\ ou \\?\UNC\) pour un affichage ou un traitement standard.
    /// </summary>
    public static string RemoveLongPathPrefix(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(@"\\", path.AsSpan(8));
        }

        if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            return path[4..];
        }

        return path;
    }
}

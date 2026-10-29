using System;
using System.IO;

namespace FileForge.Paths;

/// <summary>
/// Converts paths so that Windows long paths (over 260 characters) and UNC paths can be used.
/// </summary>
public static class PathNormalizer
{
    /// <summary>
    /// Adds the extended-length prefix to a path so that Windows accepts it beyond 260 characters.
    /// </summary>
    /// <param name="path">Path to normalize.</param>
    /// <returns>The path with the \\?\ or \\?\UNC\ prefix.</returns>
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
    /// Removes the long path prefixes (\\?\ or \\?\UNC\) for display or for APIs that do not accept them.
    /// </summary>
    /// <param name="path">Path, possibly prefixed.</param>
    /// <returns>The path without the extended-length prefix.</returns>
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

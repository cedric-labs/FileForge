using System;
using System.Collections.Generic;
using System.IO;

namespace FileForge.Sync;

/// <summary>
/// Configuration and filtering options for directory clone and exclusion operations.
/// </summary>
public sealed class DirectoryCloneOptions
{
    /// <summary>
    /// Absolute paths explicitly excluded from synchronization or cloning.
    /// </summary>
    public HashSet<string> ExcludedPaths { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks whether an absolute path is excluded (exact match or subfolder of an excluded path).
    /// </summary>
    /// <param name="path">Path to test.</param>
    /// <returns>True when the path or one of its parents is in the exclusion list; otherwise false.</returns>
    public bool IsExcluded(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var excluded in ExcludedPaths)
        {
            if (string.Equals(path, excluded, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (path.StartsWith(excluded + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

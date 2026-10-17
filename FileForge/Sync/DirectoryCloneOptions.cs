using System;
using System.Collections.Generic;
using System.IO;

namespace FileForge.Sync;

/// <summary>
/// Options de configuration et de filtrage pour les opérations de clonage et d'exclusion de répertoires.
/// </summary>
public sealed class DirectoryCloneOptions
{
    /// <summary>
    /// Ensemble des chemins absolus explicitement exclus de la synchronisation ou du clonage.
    /// </summary>
    public HashSet<string> ExcludedPaths { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Vérifie si un chemin absolu est exclu (soit par correspondance exacte, soit en tant que sous-dossier).
    /// </summary>
    /// <param name="path">Chemin d'accès à tester.</param>
    /// <returns>True si le chemin ou l'un de ses parents figure dans la liste d'exclusions, sinon false.</returns>
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

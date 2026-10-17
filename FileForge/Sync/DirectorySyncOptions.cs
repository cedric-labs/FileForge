namespace FileForge.Sync;

/// <summary>
/// Options complètes paramétrant l'exécution d'une synchronisation ou d'une sauvegarde de répertoires.
/// </summary>
public sealed class DirectorySyncOptions
{
    /// <summary>
    /// Degré de parallélisme appliqué pour l'inventaire des fichiers sources.
    /// </summary>
    public int SourceListParallelism { get; init; } = 1;

    /// <summary>
    /// Degré de parallélisme appliqué pour l'inventaire des fichiers cibles.
    /// </summary>
    public int DestinationListParallelism { get; init; } = 1;

    /// <summary>
    /// Degré de parallélisme appliqué pour le transfert et la copie des fichiers.
    /// </summary>
    public int CopyParallelism { get; init; } = 1;

    /// <summary>
    /// Indique s'il faut comparer les empreintes de hachage SHA256 après chaque transfert.
    /// </summary>
    public bool VerifyHash { get; init; }

    /// <summary>
    /// Filtres d'inclusion sous forme de motifs séparés par des points-virgules (ex: "*.jpg;*.png").
    /// </summary>
    public string IncludedFilters { get; init; } = "*";

    /// <summary>
    /// Filtres d'exclusion sous forme de motifs séparés par des points-virgules (ex: "*.tmp;*.log").
    /// </summary>
    public string ExcludedFilters { get; init; } = string.Empty;

    /// <summary>
    /// Options avancées d'exclusion par chemins absolus ou relatifs.
    /// </summary>
    public DirectoryCloneOptions? CloneOptions { get; init; }

    /// <summary>
    /// Intitulé textuel affiché dans l'en-tête du tableau de bord console.
    /// </summary>
    public string? TaskTitle { get; init; }
}

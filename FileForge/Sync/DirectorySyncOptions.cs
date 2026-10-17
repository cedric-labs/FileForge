namespace FileForge.Sync;

/// <summary>
/// Options complètes paramétrant l'exécution d'une synchronisation ou d'une sauvegarde de répertoires.
/// </summary>
public sealed class DirectorySyncOptions
{
    public int SourceListParallelism { get; init; } = 1;

    public int DestinationListParallelism { get; init; } = 1;

    public int CopyParallelism { get; init; } = 1;

    public bool VerifyHash { get; init; }

    public string IncludedFilters { get; init; } = "*";

    public string ExcludedFilters { get; init; } = string.Empty;

    public DirectoryCloneOptions? CloneOptions { get; init; }

    public string? TaskTitle { get; init; }
}

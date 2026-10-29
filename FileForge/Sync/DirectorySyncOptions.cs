namespace FileForge.Sync;

/// <summary>
/// Options that drive a directory synchronization or backup run.
/// </summary>
public sealed class DirectorySyncOptions
{
    /// <summary>
    /// Degree of parallelism used to list the source files.
    /// </summary>
    public int SourceListParallelism { get; init; } = 1;

    /// <summary>
    /// Degree of parallelism used to list the target files.
    /// </summary>
    public int DestinationListParallelism { get; init; } = 1;

    /// <summary>
    /// Degree of parallelism used to transfer and copy the files.
    /// </summary>
    public int CopyParallelism { get; init; } = 1;

    /// <summary>
    /// Whether SHA256 hashes are compared after each transfer.
    /// </summary>
    public bool VerifyHash { get; init; }

    /// <summary>
    /// Inclusion filters, as semicolon-separated patterns (for example "*.jpg;*.png").
    /// </summary>
    public string IncludedFilters { get; init; } = "*";

    /// <summary>
    /// Exclusion filters, as semicolon-separated patterns (for example "*.tmp;*.log").
    /// </summary>
    public string ExcludedFilters { get; init; } = string.Empty;

    /// <summary>
    /// Exclusion by absolute or relative paths.
    /// </summary>
    public DirectoryCloneOptions? CloneOptions { get; init; }

    /// <summary>
    /// Title shown in the header of the console dashboard.
    /// </summary>
    public string? TaskTitle { get; init; }
}

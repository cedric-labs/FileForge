namespace FileForge.Terminal;

/// <summary>
/// Life cycle phases of the console dashboard.
/// </summary>
public enum DashboardPhase
{
    /// <summary>
    /// Scanning, listing and comparing the directories.
    /// </summary>
    Scanning,

    /// <summary>
    /// Transferring and copying the files.
    /// </summary>
    Copying,

    /// <summary>
    /// No file needs a transfer (disk up to date).
    /// </summary>
    UpToDate,

    /// <summary>
    /// Final summary once the operations are over.
    /// </summary>
    Completed
}

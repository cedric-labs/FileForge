namespace FileForge.IO;

/// <summary>
/// Kind of file operation.
/// </summary>
public enum FileOperationType
{
    /// <summary>
    /// Copy.
    /// </summary>
    Copy,

    /// <summary>
    /// Deletion.
    /// </summary>
    Delete,

    /// <summary>
    /// Move.
    /// </summary>
    Move,

    /// <summary>
    /// Scan or inventory.
    /// </summary>
    Scan,

    /// <summary>
    /// Comparison of files or hashes.
    /// </summary>
    Compare,

    /// <summary>
    /// No operation in progress.
    /// </summary>
    Inactive
}

/// <summary>
/// Progress of a file operation.
/// </summary>
public sealed class FileOperationProgress(
    string sourcePath,
    string destinationPath,
    long totalBytes,
    long processedBytes,
    FileOperationType operationType,
    bool isCompleted = false)
{
    /// <summary>
    /// Source file path.
    /// </summary>
    public string SourcePath { get; } = sourcePath;

    /// <summary>
    /// Destination file path.
    /// </summary>
    public string DestinationPath { get; } = destinationPath;

    /// <summary>
    /// Total size in bytes of the item being processed.
    /// </summary>
    public long TotalBytes { get; } = totalBytes;

    /// <summary>
    /// Bytes already processed or transferred.
    /// </summary>
    public long ProcessedBytes { get; } = processedBytes;

    /// <summary>
    /// Completion percentage, from 0 to 100.
    /// </summary>
    public double PercentComplete => TotalBytes > 0 ? (double)ProcessedBytes / TotalBytes * 100 : 0;

    /// <summary>
    /// Kind of the current operation.
    /// </summary>
    public FileOperationType OperationType { get; } = operationType;

    /// <summary>
    /// Whether this item is fully processed.
    /// </summary>
    public bool IsCompleted { get; } = isCompleted;
}

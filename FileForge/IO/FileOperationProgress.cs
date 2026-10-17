namespace FileForge.IO;

/// <summary>
/// Type d'opération sur les fichiers.
/// </summary>
public enum FileOperationType
{
    Copy,
    Delete,
    Move,
    Scan,
    Compare,
    Inactive
}

/// <summary>
/// Informations sur la progression d'une opération de fichier.
/// </summary>
public sealed class FileOperationProgress(
    string sourcePath,
    string destinationPath,
    long totalBytes,
    long processedBytes,
    FileOperationType operationType,
    bool isCompleted = false)
{
    public string SourcePath { get; } = sourcePath;
    public string DestinationPath { get; } = destinationPath;
    public long TotalBytes { get; } = totalBytes;
    public long ProcessedBytes { get; } = processedBytes;
    public double PercentComplete => TotalBytes > 0 ? (double)ProcessedBytes / TotalBytes * 100 : 0;
    public FileOperationType OperationType { get; } = operationType;
    public bool IsCompleted { get; } = isCompleted;
}

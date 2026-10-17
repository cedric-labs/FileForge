namespace FileForge.IO;

/// <summary>
/// Type d'opération sur les fichiers.
/// </summary>
public enum FileOperationType
{
    /// <summary>
    /// Opération de copie.
    /// </summary>
    Copy,

    /// <summary>
    /// Opération de suppression.
    /// </summary>
    Delete,

    /// <summary>
    /// Opération de déplacement.
    /// </summary>
    Move,

    /// <summary>
    /// Opération d'analyse ou d'inventaire.
    /// </summary>
    Scan,

    /// <summary>
    /// Opération de comparaison de fichiers ou d'empreintes.
    /// </summary>
    Compare,

    /// <summary>
    /// Opération inactive ou en attente.
    /// </summary>
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
    /// <summary>
    /// Chemin du fichier source concerné.
    /// </summary>
    public string SourcePath { get; } = sourcePath;

    /// <summary>
    /// Chemin du fichier de destination concerné.
    /// </summary>
    public string DestinationPath { get; } = destinationPath;

    /// <summary>
    /// Taille totale en octets de l'élément en cours de traitement.
    /// </summary>
    public long TotalBytes { get; } = totalBytes;

    /// <summary>
    /// Nombre d'octets déjà traités ou transférés.
    /// </summary>
    public long ProcessedBytes { get; } = processedBytes;

    /// <summary>
    /// Pourcentage de complétion calculé entre 0 et 100.
    /// </summary>
    public double PercentComplete => TotalBytes > 0 ? (double)ProcessedBytes / TotalBytes * 100 : 0;

    /// <summary>
    /// Type de l'opération en cours.
    /// </summary>
    public FileOperationType OperationType { get; } = operationType;

    /// <summary>
    /// Indique si l'opération individuelle est intégralement achevée.
    /// </summary>
    public bool IsCompleted { get; } = isCompleted;
}

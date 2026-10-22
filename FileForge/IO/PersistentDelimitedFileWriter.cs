using System.Text;

namespace FileForge.IO;

/// <summary>
/// Écrivain persistant de fichiers délimités (CSV, TSV) thread-safe avec ouverture partagée non bloquante (FileShare.ReadWrite).
/// Permet l'ajout continu de lignes au fil de l'eau et le rechargement asynchrone des lignes préexistantes au démarrage.
/// </summary>
public sealed class PersistentDelimitedFileWriter : IAsyncDisposable
{
    private readonly string _filePath;
    private readonly string _headerLine;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private FileStream? _fileStream;
    private StreamWriter? _streamWriter;

    /// <summary>
    /// Initialise une nouvelle instance de l'écrivain délimité.
    /// </summary>
    /// <param name="filePath">Chemin d'accès au fichier de destination.</param>
    /// <param name="headerLine">Ligne d'en-tête à écrire si le fichier est nouvellement créé.</param>
    public PersistentDelimitedFileWriter(string filePath, string headerLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(headerLine);

        _filePath = filePath;
        _headerLine = headerLine;
    }

    /// <summary>
    /// Initialise le flux sous-jacent avec partage en lecture/écriture, écrit l'en-tête si nécessaire ou se place en fin de fichier.
    /// </summary>
    /// <param name="cancellationToken">Jeton d'annulation coopérative.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var fileExisted = File.Exists(_filePath) && new FileInfo(_filePath).Length > 0;

            _fileStream = new FileStream(
                _filePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite,
                4096,
                useAsync: true);

            _streamWriter = new StreamWriter(_fileStream, Encoding.UTF8, bufferSize: 4096, leaveOpen: true)
            {
                AutoFlush = true
            };

            if (!fileExisted && !string.IsNullOrWhiteSpace(_headerLine))
            {
                await _streamWriter.WriteLineAsync(_headerLine.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _fileStream.Seek(0, SeekOrigin.End);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Lit de manière asynchrone l'ensemble des lignes existantes du fichier (hors en-tête) pour hydrater les caches de dédoublonnage.
    /// </summary>
    /// <param name="cancellationToken">Jeton d'annulation coopérative.</param>
    /// <returns>Collection des lignes existantes trouvées.</returns>
    public async Task<IReadOnlyList<string>> ReadExistingLinesAsync(CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();

        if (!File.Exists(_filePath))
        {
            return lines;
        }

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var readStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(readStream, Encoding.UTF8);

            var isHeader = true;
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (isHeader)
                {
                    isHeader = false;
                    continue;
                }

                lines.Add(line.Trim());
            }
        }
        finally
        {
            _semaphore.Release();
        }

        return lines;
    }

    /// <summary>
    /// Ajoute une ligne de manière thread-safe et asynchrone au fichier.
    /// </summary>
    /// <param name="line">Ligne à ajouter.</param>
    /// <param name="cancellationToken">Jeton d'annulation coopérative.</param>
    public async Task AppendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (_streamWriter is null)
        {
            throw new InvalidOperationException("L'écrivain de fichier délimité doit être initialisé avant d'écrire.");
        }

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _streamWriter.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Échappe une valeur textuelle selon les règles standards RFC 4180 pour inclusion dans un champ délimité.
    /// </summary>
    /// <param name="value">Valeur textuelle brute.</param>
    /// <param name="delimiter">Caractère séparateur utilisé.</param>
    /// <returns>Valeur échappée prête pour l'export.</returns>
    public static string EscapeField(string value, char delimiter = ',')
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var mustQuote = value.Contains(delimiter) || value.Contains('"') ||
                        value.Contains('\n') || value.Contains('\r');

        if (!mustQuote)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_streamWriter is not null)
        {
            await _streamWriter.FlushAsync().ConfigureAwait(false);
            await _streamWriter.DisposeAsync().ConfigureAwait(false);
            _streamWriter = null;
        }

        if (_fileStream is not null)
        {
            await _fileStream.DisposeAsync().ConfigureAwait(false);
            _fileStream = null;
        }

        _semaphore.Dispose();
    }
}

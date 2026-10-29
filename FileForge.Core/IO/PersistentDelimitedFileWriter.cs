using System.Text;

namespace FileForge.IO;

/// <summary>
/// Thread-safe persistent writer for delimited files (CSV, TSV), opened with non-blocking shared access (FileShare.ReadWrite).
/// Appends lines as they arrive and reloads the existing lines asynchronously at startup.
/// </summary>
public sealed class PersistentDelimitedFileWriter : IAsyncDisposable
{
    private readonly string _filePath;
    private readonly string _headerLine;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private FileStream? _fileStream;
    private StreamWriter? _streamWriter;

    /// <summary>
    /// Initializes a new delimited file writer.
    /// </summary>
    /// <param name="filePath">Path of the target file.</param>
    /// <param name="headerLine">Header line written when the file is created.</param>
    public PersistentDelimitedFileWriter(string filePath, string headerLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(headerLine);

        _filePath = filePath;
        _headerLine = headerLine;
    }

    /// <summary>
    /// Opens the underlying stream with read/write sharing, then writes the header for a new file or seeks to the end of an existing one.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
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
    /// Asynchronously reads every existing line of the file (header excluded), so that callers can rebuild their deduplication caches.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The existing lines.</returns>
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
    /// Appends a line to the file asynchronously; concurrent calls are serialized.
    /// </summary>
    /// <param name="line">Line to append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task AppendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (_streamWriter is null)
        {
            throw new InvalidOperationException("The delimited file writer must be initialized before writing.");
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
    /// Escapes a text value according to RFC 4180 so that it can be stored in a delimited field.
    /// </summary>
    /// <param name="value">Raw text value.</param>
    /// <param name="delimiter">Field separator.</param>
    /// <returns>The escaped value.</returns>
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

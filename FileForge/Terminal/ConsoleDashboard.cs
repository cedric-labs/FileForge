using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace FileForge.Terminal;

/// <summary>
/// Retro console dashboard in the style of Norton Commander that shows, in real time,
/// every stage of a backup or synchronization (scan, copy, up to date, completed).
/// </summary>
public sealed class ConsoleDashboard : IDisposable
{
    private static readonly object ConsoleLock = new();

    private readonly string _taskName;
    private readonly string _sourceRoot;
    private readonly string _destRoot;
    private readonly Stopwatch _stopwatch;
    private readonly bool _isInteractive;

    private DashboardPhase _phase;
    private int _totalFiles;
    private long _totalBytes;
    private int _completedFiles;
    private long _completedBytes;
    private int _failedFiles;
    private long _failedBytes;
    private int _scannedDirs;
    private int _scannedItems;
    private string _currentScanningPath = string.Empty;
    private string _statusMessage = string.Empty;
    private string _currentFileName = string.Empty;
    private long _currentFileBytes;
    private long _currentFileTotalBytes;
    private double _currentFilePercent;
    private long _lastRenderTicks;
    private bool _hasCleared;
    private int _lastWindowWidth;
    private int _lastWindowHeight;

    /// <summary>
    /// Current phase of the dashboard.
    /// </summary>
    public DashboardPhase Phase => _phase;

    /// <summary>
    /// Creates a dashboard in the scan phase.
    /// </summary>
    /// <param name="taskName">Task name shown in the header.</param>
    /// <param name="sourceRoot">Source directory.</param>
    /// <param name="destRoot">Target directory.</param>
    public ConsoleDashboard(string taskName, string sourceRoot, string destRoot)
        : this(taskName, sourceRoot, destRoot, 0, 0, DashboardPhase.Scanning)
    {
    }

    /// <summary>
    /// Creates a dashboard directly in the transfer phase.
    /// </summary>
    /// <param name="taskName">Task name shown in the header.</param>
    /// <param name="sourceRoot">Source directory.</param>
    /// <param name="destRoot">Target directory.</param>
    /// <param name="totalFiles">Total number of files to copy.</param>
    /// <param name="totalBytes">Total number of bytes to transfer.</param>
    public ConsoleDashboard(
        string taskName,
        string sourceRoot,
        string destRoot,
        int totalFiles,
        long totalBytes)
        : this(taskName, sourceRoot, destRoot, totalFiles, totalBytes, DashboardPhase.Copying)
    {
    }

    private ConsoleDashboard(
        string taskName,
        string sourceRoot,
        string destRoot,
        int totalFiles,
        long totalBytes,
        DashboardPhase initialPhase)
    {
        _taskName = taskName;
        _sourceRoot = sourceRoot;
        _destRoot = destRoot;
        _totalFiles = totalFiles;
        _totalBytes = totalBytes;
        _phase = initialPhase;
        _stopwatch = Stopwatch.StartNew();
        _isInteractive = !Console.IsOutputRedirected && Console.WindowWidth > 0;

        if (_isInteractive)
        {
            try
            {
                Console.CursorVisible = false;
            }
            catch (IOException)
            {
                // Some terminals reject cursor changes; the dashboard still renders without them
            }
            Render();
        }
    }

    /// <summary>
    /// Updates the display during the scan phase.
    /// </summary>
    /// <param name="currentDirectory">Folder being scanned.</param>
    /// <param name="itemsFound">Total number of items found.</param>
    public void UpdateScanning(string currentDirectory, int itemsFound)
    {
        _phase = DashboardPhase.Scanning;
        _currentScanningPath = currentDirectory;
        _scannedItems = itemsFound;
        System.Threading.Interlocked.Increment(ref _scannedDirs);

        RenderIfThrottled();
    }

    /// <summary>
    /// Sets the transfer plan and switches the dashboard to the copy phase.
    /// </summary>
    /// <param name="totalFiles">Total number of files to copy.</param>
    /// <param name="totalBytes">Total number of bytes to transfer.</param>
    public void SetTransferPlan(int totalFiles, long totalBytes)
    {
        _phase = DashboardPhase.Copying;
        _totalFiles = totalFiles;
        _totalBytes = totalBytes;

        RenderIfThrottled(force: true);
    }

    /// <summary>
    /// Switches the dashboard to the "up to date" phase when no file needs a transfer.
    /// </summary>
    /// <param name="message">Status message.</param>
    public void SetUpToDate(string message = "Disk up to date (no change detected)")
    {
        _phase = DashboardPhase.UpToDate;
        _statusMessage = message;

        RenderIfThrottled(force: true);
    }

    /// <summary>
    /// Switches the dashboard to the completed phase.
    /// </summary>
    /// <param name="message">Final summary message.</param>
    public void Complete(string message = "Backup committed")
    {
        _phase = DashboardPhase.Completed;
        _statusMessage = message;

        RenderIfThrottled(force: true);
    }

    /// <summary>
    /// Updates the progress of the file being copied.
    /// </summary>
    /// <param name="fileName">File name or relative path.</param>
    /// <param name="bytesCopied">Bytes already transferred for this file.</param>
    /// <param name="fileTotalBytes">Total size of this file in bytes.</param>
    public void UpdateCurrentFile(string fileName, long bytesCopied, long fileTotalBytes)
    {
        _currentFileName = fileName;
        _currentFileBytes = bytesCopied;
        _currentFileTotalBytes = fileTotalBytes;
        _currentFilePercent = fileTotalBytes > 0 ? (double)bytesCopied / fileTotalBytes * 100.0 : 100.0;

        RenderIfThrottled();
    }

    /// <summary>
    /// Marks the current file as copied and updates the counters.
    /// </summary>
    /// <param name="fileSize">Size of the copied file.</param>
    public void CompleteFile(long fileSize)
    {
        System.Threading.Interlocked.Increment(ref _completedFiles);
        System.Threading.Interlocked.Add(ref _completedBytes, fileSize);
        _currentFileBytes = 0;

        RenderIfThrottled(force: true);
    }

    /// <summary>
    /// Marks the current file as failed and updates the failure counters.
    /// </summary>
    /// <param name="fileSize">Size of the failed file.</param>
    public void FailFile(long fileSize)
    {
        System.Threading.Interlocked.Increment(ref _failedFiles);
        System.Threading.Interlocked.Add(ref _failedBytes, fileSize);
        _currentFileBytes = 0;

        RenderIfThrottled(force: true);
    }

    private void RenderIfThrottled(bool force = false)
    {
        long currentTicks = Stopwatch.GetTimestamp();
        double elapsedMs = (double)(currentTicks - _lastRenderTicks) * 1000.0 / Stopwatch.Frequency;

        if (!force && elapsedMs < 60)
        {
            return;
        }

        _lastRenderTicks = currentTicks;
        Render();
    }

    /// <summary>
    /// Renders the dashboard on the console now, when the output is interactive.
    /// </summary>
    public void Render()
    {
        if (!_isInteractive)
        {
            return;
        }

        lock (ConsoleLock)
        {
            try
            {
                int windowWidth = Console.WindowWidth;
                int windowHeight = Console.WindowHeight;

                if (!_hasCleared || windowWidth != _lastWindowWidth || windowHeight != _lastWindowHeight)
                {
                    Console.ResetColor();
                    Console.Clear();
                    _hasCleared = true;
                    _lastWindowWidth = windowWidth;
                    _lastWindowHeight = windowHeight;
                }

                int boxWidth = Math.Clamp(windowWidth - 4, 60, 96);
                int leftMargin = Math.Max(0, (windowWidth - boxWidth) / 2);

                Console.SetCursorPosition(0, 1);
                DrawNortonBox(leftMargin, boxWidth, windowWidth);
            }
            catch (IOException)
            {
                // Resizing the window while drawing raises I/O errors; the next refresh redraws the frame
            }
        }
    }

    internal void DrawNortonBox(int leftMargin, int boxWidth, int windowWidth)
    {
        int contentWidth = Math.Max(20, boxWidth - 4);
        int frameBorderWidth = contentWidth + 2;

        DrawFrameBorder(leftMargin, frameBorderWidth, '╔', '═', '╗', windowWidth);
        WriteContentLine(leftMargin, contentWidth, windowWidth, (CenterText(_taskName, contentWidth), ConsoleColor.White));
        DrawFrameBorder(leftMargin, frameBorderWidth, '╠', '═', '╣', windowWidth);

        string srcPath = ShortenPath(_sourceRoot, contentWidth - 9);
        WriteContentLine(leftMargin, contentWidth, windowWidth, ("Source : ", ConsoleColor.Cyan), (srcPath, ConsoleColor.White));

        string dstPath = ShortenPath(_destRoot, contentWidth - 9);
        WriteContentLine(leftMargin, contentWidth, windowWidth, ("Cible  : ", ConsoleColor.Cyan), (dstPath, ConsoleColor.White));

        DrawFrameBorder(leftMargin, frameBorderWidth, '╠', '═', '╣', windowWidth);

        switch (_phase)
        {
            case DashboardPhase.Scanning:
                DrawScanningContent(leftMargin, contentWidth, windowWidth, frameBorderWidth);
                break;

            case DashboardPhase.UpToDate:
                DrawUpToDateContent(leftMargin, contentWidth, windowWidth, frameBorderWidth);
                break;

            case DashboardPhase.Completed:
                DrawCompletedContent(leftMargin, contentWidth, windowWidth, frameBorderWidth);
                break;

            case DashboardPhase.Copying:
            default:
                DrawCopyingContent(leftMargin, contentWidth, windowWidth, frameBorderWidth);
                break;
        }

        DrawFrameBorder(leftMargin, frameBorderWidth, '╚', '═', '╝', windowWidth);
        Console.ResetColor();
    }

    private void DrawScanningContent(int leftMargin, int contentWidth, int windowWidth, int frameBorderWidth)
    {
        WriteContentLine(leftMargin, contentWidth, windowWidth,
            ("Statut : ", ConsoleColor.Cyan),
            ("Scanning the source...", ConsoleColor.Yellow));

        WriteContentLine(leftMargin, contentWidth, windowWidth,
            ("Found  : ", ConsoleColor.Cyan),
            ($"{_scannedItems,6} item(s) ", ConsoleColor.White),
            ($"({_scannedDirs} folders scanned)", ConsoleColor.DarkCyan));

        string scanDirLabel = string.IsNullOrEmpty(_currentScanningPath) ? "(starting)" : ShortenPath(_currentScanningPath, contentWidth);
        WriteContentLine(leftMargin, contentWidth, windowWidth, (scanDirLabel, ConsoleColor.White));

        DrawFrameBorder(leftMargin, frameBorderWidth, '╠', '═', '╣', windowWidth);

        TimeSpan scanElapsed = _stopwatch.Elapsed;
        WriteContentLine(leftMargin, contentWidth, windowWidth,
            ("Elapsed: ", ConsoleColor.Cyan),
            (FormatTimeSpan(scanElapsed), ConsoleColor.White),
            (" | Indexing the folder tree...", ConsoleColor.DarkCyan));
    }

    private void DrawUpToDateContent(int leftMargin, int contentWidth, int windowWidth, int frameBorderWidth)
    {
        string status = string.IsNullOrEmpty(_statusMessage) ? "Disk already up to date (no change)" : _statusMessage;
        WriteContentLine(leftMargin, contentWidth, windowWidth,
            ("Statut : ", ConsoleColor.Cyan),
            (status, ConsoleColor.Green));

        WriteContentLine(leftMargin, contentWidth, windowWidth,
            ("File   : ", ConsoleColor.Cyan),
            ("No file to transfer (0 / 0)", ConsoleColor.White));

        int barWidth = Math.Max(5, contentWidth - 2);
        string fullBar = BuildProgressBar(100.0, barWidth);
        WriteContentLine(leftMargin, contentWidth, windowWidth, (fullBar, ConsoleColor.Green));

        DrawFrameBorder(leftMargin, frameBorderWidth, '╠', '═', '╣', windowWidth);

        WriteContentLine(leftMargin, contentWidth, windowWidth,
            ("Elapsed: ", ConsoleColor.Cyan),
            (FormatTimeSpan(_stopwatch.Elapsed), ConsoleColor.White),
            (" | No transfer needed", ConsoleColor.Green));
    }

    private void DrawCompletedContent(int leftMargin, int contentWidth, int windowWidth, int frameBorderWidth)
    {
        string status = string.IsNullOrEmpty(_statusMessage) ? "Backup completed" : _statusMessage;
        WriteContentLine(leftMargin, contentWidth, windowWidth,
            ("Statut : ", ConsoleColor.Cyan),
            (status, ConsoleColor.Green));

        string compCounter = $"[ {_completedFiles} / {_totalFiles} ] ";
        if (_failedFiles > 0)
        {
            WriteContentLine(leftMargin, contentWidth, windowWidth,
                ("File   : ", ConsoleColor.Cyan),
                (compCounter, ConsoleColor.White),
                ($"{_completedFiles} copied, ", ConsoleColor.Green),
                ($"{_failedFiles} failed", ConsoleColor.Red));
        }
        else
        {
            WriteContentLine(leftMargin, contentWidth, windowWidth,
                ("File   : ", ConsoleColor.Cyan),
                (compCounter, ConsoleColor.White),
                ("All files copied", ConsoleColor.Green));
        }

        int barWidth = Math.Max(5, contentWidth - 2);
        double finalPercent = _totalFiles > 0 ? (double)_completedFiles / _totalFiles * 100.0 : 100.0;
        string fullBar = BuildProgressBar(finalPercent, barWidth);
        WriteContentLine(leftMargin, contentWidth, windowWidth, (fullBar, _failedFiles > 0 ? ConsoleColor.Yellow : ConsoleColor.Green));

        DrawFrameBorder(leftMargin, frameBorderWidth, '╠', '═', '╣', windowWidth);

        TimeSpan finalElapsed = _stopwatch.Elapsed;
        double finalSec = Math.Max(0.001, finalElapsed.TotalSeconds);
        double avgSpeed = _completedBytes / finalSec;
        WriteContentLine(leftMargin, contentWidth, windowWidth,
            ("Speed  : ", ConsoleColor.Cyan),
            ($"{FormatBytes((long)avgSpeed)}/s", ConsoleColor.White),
            (" | Total time : ", ConsoleColor.Cyan),
            (FormatTimeSpan(finalElapsed), ConsoleColor.White));
    }

    private void DrawCopyingContent(int leftMargin, int contentWidth, int windowWidth, int frameBorderWidth)
    {
        int processedFiles = _completedFiles + _failedFiles;
        int remainingFiles = Math.Max(0, _totalFiles - processedFiles);
        int currentFileNum = Math.Min(_totalFiles, processedFiles + 1);
        string fileCounter = $"[ {currentFileNum} / {_totalFiles} ] ";

        if (_failedFiles > 0)
        {
            WriteContentLine(leftMargin, contentWidth, windowWidth,
                ("File   : ", ConsoleColor.Cyan),
                (fileCounter, ConsoleColor.White),
                ($"({remainingFiles} restants, ", ConsoleColor.Yellow),
                ($"{_failedFiles} failed)", ConsoleColor.Red));
        }
        else
        {
            WriteContentLine(leftMargin, contentWidth, windowWidth,
                ("File   : ", ConsoleColor.Cyan),
                (fileCounter, ConsoleColor.White),
                ($"({remainingFiles} restants)", ConsoleColor.Yellow));
        }

        string currentFileLabel = string.IsNullOrEmpty(_currentFileName) ? "(waiting)" : ShortenPath(_currentFileName, contentWidth);
        WriteContentLine(leftMargin, contentWidth, windowWidth, (currentFileLabel, ConsoleColor.White));

        const int metricsWidth = 32;
        int fileBarWidth = Math.Max(5, contentWidth - metricsWidth - 1);
        int fileInnerChars = Math.Max(3, fileBarWidth - 2);
        int fileFilled = (int)Math.Round(Math.Clamp(_currentFilePercent, 0.0, 100.0) / 100.0 * fileInnerChars);
        fileFilled = Math.Clamp(fileFilled, 0, fileInnerChars);
        int fileEmpty = fileInnerChars - fileFilled;
        string fileMetrics = $" {_currentFilePercent,5:F1}% ({FormatBytes(_currentFileBytes)} / {FormatBytes(_currentFileTotalBytes)})";

        WriteContentLine(leftMargin, contentWidth, windowWidth,
            ("[", ConsoleColor.Cyan),
            (new string('█', fileFilled), ConsoleColor.Yellow),
            (new string('░', fileEmpty), ConsoleColor.DarkCyan),
            ("]", ConsoleColor.Cyan),
            (PadOrTruncate(fileMetrics, metricsWidth), ConsoleColor.White));

        DrawFrameBorder(leftMargin, frameBorderWidth, '╠', '═', '╣', windowWidth);

        long liveBytes = System.Threading.Interlocked.Read(ref _completedBytes) + _currentFileBytes;
        long effectiveCopiedBytes = Math.Min(_totalBytes, liveBytes);

        double overallPercent;
        if (_totalBytes > 0)
        {
            overallPercent = (double)effectiveCopiedBytes / _totalBytes * 100.0;
        }
        else if (_totalFiles > 0)
        {
            overallPercent = (double)processedFiles / _totalFiles * 100.0;
        }
        else
        {
            overallPercent = 100.0;
        }
        overallPercent = Math.Clamp(overallPercent, 0.0, 100.0);

        int totalBarWidth = Math.Max(5, contentWidth - 8 - metricsWidth - 1);
        int totalInnerChars = Math.Max(3, totalBarWidth - 2);
        int totalFilled = (int)Math.Round(Math.Clamp(overallPercent, 0.0, 100.0) / 100.0 * totalInnerChars);
        totalFilled = Math.Clamp(totalFilled, 0, totalInnerChars);
        int totalEmpty = totalInnerChars - totalFilled;
        string overallMetrics = $" {overallPercent,5:F1}% ({FormatBytes(effectiveCopiedBytes)} / {FormatBytes(_totalBytes)})";

        WriteContentLine(leftMargin, contentWidth, windowWidth,
            ("Total  : ", ConsoleColor.Cyan),
            ("[", ConsoleColor.Cyan),
            (new string('█', totalFilled), ConsoleColor.Green),
            (new string('░', totalEmpty), ConsoleColor.DarkCyan),
            ("]", ConsoleColor.Cyan),
            (PadOrTruncate(overallMetrics, metricsWidth), ConsoleColor.White));

        TimeSpan elapsed = _stopwatch.Elapsed;
        double elapsedSec = Math.Max(0.001, elapsed.TotalSeconds);
        double bytesPerSec = effectiveCopiedBytes / elapsedSec;
        string speed = $"{FormatBytes((long)bytesPerSec)}/s";

        TimeSpan remainingTime = TimeSpan.Zero;
        if (bytesPerSec > 0 && _totalBytes > effectiveCopiedBytes && remainingFiles > 0)
        {
            double remainingSec = (_totalBytes - effectiveCopiedBytes) / bytesPerSec;
            if (remainingSec < 86400 * 7)
            {
                remainingTime = TimeSpan.FromSeconds(remainingSec);
            }
        }

        WriteContentLine(leftMargin, contentWidth, windowWidth,
            ("Speed  : ", ConsoleColor.Cyan),
            ($"{speed,-10}", ConsoleColor.White),
            (" | Elapsed: ", ConsoleColor.Cyan),
            (FormatTimeSpan(elapsed), ConsoleColor.White),
            (" | Restant : ", ConsoleColor.Cyan),
            (FormatTimeSpan(remainingTime), remainingTime > TimeSpan.Zero ? ConsoleColor.Yellow : ConsoleColor.White));
    }

    private static void DrawFrameBorder(int leftMargin, int frameBorderWidth, char left, char fill, char right, int windowWidth)
    {
        WriteMargin(leftMargin);
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write(left);
        Console.Write(new string(fill, frameBorderWidth));
        Console.Write(right);

        Console.ResetColor();
        int remainingRight = Math.Max(0, windowWidth - (leftMargin + frameBorderWidth + 2));
        if (remainingRight > 0)
        {
            Console.Write(new string(' ', remainingRight));
        }
        Console.WriteLine();
    }

    private static void WriteContentLine(int leftMargin, int contentWidth, int windowWidth, params (string text, ConsoleColor color)[] segments)
    {
        WriteMargin(leftMargin);
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("║ ");

        int printedLength = 0;
        foreach (var (text, color) in segments)
        {
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            int remainingSpace = contentWidth - printedLength;
            if (remainingSpace <= 0)
            {
                break;
            }

            string toPrint = text.Length > remainingSpace ? text[..remainingSpace] : text;
            Console.ForegroundColor = color;
            Console.Write(toPrint);
            printedLength += toPrint.Length;
        }

        if (printedLength < contentWidth)
        {
            Console.Write(new string(' ', contentWidth - printedLength));
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write(" ║");

        Console.ResetColor();
        int remainingRight = Math.Max(0, windowWidth - (leftMargin + contentWidth + 4));
        if (remainingRight > 0)
        {
            Console.Write(new string(' ', remainingRight));
        }
        Console.WriteLine();
    }

    private static void WriteMargin(int leftMargin)
    {
        Console.ResetColor();
        if (leftMargin > 0)
        {
            Console.Write(new string(' ', leftMargin));
        }
    }

    /// <summary>
    /// Renders the dashboard as plain text for a given width.
    /// </summary>
    /// <param name="width">Total display width.</param>
    /// <returns>The retro dashboard as text.</returns>
    public string BuildDashboardString(int width) => BuildDashboardString(width, width);

    /// <summary>
    /// Renders the dashboard as plain text, centred from the box width and the total width.
    /// </summary>
    /// <param name="totalWidth">Total width of the container.</param>
    /// <param name="boxWidth">Inner width of the Norton box.</param>
    /// <returns>The retro dashboard as text.</returns>
    public string BuildDashboardString(int totalWidth, int boxWidth)
    {
        int effectiveBoxWidth = Math.Clamp(boxWidth, 50, Math.Max(50, totalWidth));
        int leftMargin = Math.Max(0, (totalWidth - effectiveBoxWidth) / 2);
        string indent = new string(' ', leftMargin);
        int contentWidth = Math.Max(20, effectiveBoxWidth - 4);
        int frameBorderWidth = contentWidth + 2;
        var sb = new System.Text.StringBuilder();

        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}╔{new string('═', frameBorderWidth)}╗");
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {CenterText(_taskName, contentWidth)} ║");
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}╠{new string('═', frameBorderWidth)}╣");

        string srcText = $"Source : {ShortenPath(_sourceRoot, contentWidth - 9)}";
        string dstText = $"Cible  : {ShortenPath(_destRoot, contentWidth - 9)}";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(srcText, contentWidth)} ║");
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(dstText, contentWidth)} ║");
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}╠{new string('═', frameBorderWidth)}╣");

        switch (_phase)
        {
            case DashboardPhase.Scanning:
                AppendScanningString(sb, indent, contentWidth, frameBorderWidth);
                break;

            case DashboardPhase.UpToDate:
                AppendUpToDateString(sb, indent, contentWidth, frameBorderWidth);
                break;

            case DashboardPhase.Completed:
                AppendCompletedString(sb, indent, contentWidth, frameBorderWidth);
                break;

            case DashboardPhase.Copying:
            default:
                AppendCopyingString(sb, indent, contentWidth, frameBorderWidth);
                break;
        }

        sb.Append(CultureInfo.InvariantCulture, $"{indent}╚{new string('═', frameBorderWidth)}╝");
        return sb.ToString();
    }

    private void AppendScanningString(System.Text.StringBuilder sb, string indent, int contentWidth, int frameBorderWidth)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate("Status : Scanning the source...", contentWidth)} ║");
        string itemsText = $"Found  : {_scannedItems} items ({_scannedDirs} folders scanned)";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(itemsText, contentWidth)} ║");
        string scanDir = string.IsNullOrEmpty(_currentScanningPath) ? "(starting)" : ShortenPath(_currentScanningPath, contentWidth);
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(scanDir, contentWidth)} ║");
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}╠{new string('═', frameBorderWidth)}╣");
        string stats = $"Elapsed: {FormatTimeSpan(_stopwatch.Elapsed)} | Indexing the folder tree...";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(stats, contentWidth)} ║");
    }

    private void AppendUpToDateString(System.Text.StringBuilder sb, string indent, int contentWidth, int frameBorderWidth)
    {
        string status = string.IsNullOrEmpty(_statusMessage) ? "Status : Disk already up to date" : $"Status : {_statusMessage}";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(status, contentWidth)} ║");
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate("File   : No file to transfer (0 / 0)", contentWidth)} ║");
        int barWidth = Math.Max(5, contentWidth - 2);
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(BuildProgressBar(100.0, barWidth), contentWidth)} ║");
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}╠{new string('═', frameBorderWidth)}╣");
        string stats = $"Elapsed: {FormatTimeSpan(_stopwatch.Elapsed)} | No transfer needed";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(stats, contentWidth)} ║");
    }

    private void AppendCompletedString(System.Text.StringBuilder sb, string indent, int contentWidth, int frameBorderWidth)
    {
        string status = string.IsNullOrEmpty(_statusMessage) ? "Status : Backup completed" : $"Status : {_statusMessage}";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(status, contentWidth)} ║");
        string countText = _failedFiles > 0
            ? $"File   : [ {_completedFiles} / {_totalFiles} ] ({_failedFiles} failed)"
            : $"File   : [ {_completedFiles} / {_totalFiles} ] (all copied)";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(countText, contentWidth)} ║");
        int barWidth = Math.Max(5, contentWidth - 2);
        double finalPercent = _totalFiles > 0 ? (double)_completedFiles / _totalFiles * 100.0 : 100.0;
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(BuildProgressBar(finalPercent, barWidth), contentWidth)} ║");
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}╠{new string('═', frameBorderWidth)}╣");

        TimeSpan elapsed = _stopwatch.Elapsed;
        double elapsedSec = Math.Max(0.001, elapsed.TotalSeconds);
        double avgSpeed = _completedBytes / elapsedSec;
        string stats = $"Speed  : {FormatBytes((long)avgSpeed)}/s | Total time : {FormatTimeSpan(elapsed)}";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(stats, contentWidth)} ║");
    }

    private void AppendCopyingString(System.Text.StringBuilder sb, string indent, int contentWidth, int frameBorderWidth)
    {
        int processedFiles = _completedFiles + _failedFiles;
        int remainingFiles = Math.Max(0, _totalFiles - processedFiles);
        int currentFileNum = Math.Min(_totalFiles, processedFiles + 1);
        string remainingText = _failedFiles > 0
            ? $"({remainingFiles} remaining, {_failedFiles} failed)"
            : $"({remainingFiles} restants)";
        string currentFileIndex = $"File   : [ {currentFileNum} / {_totalFiles} ] {remainingText}";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(currentFileIndex, contentWidth)} ║");

        string currentFileLabel = string.IsNullOrEmpty(_currentFileName) ? "(waiting)" : ShortenPath(_currentFileName, contentWidth);
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(currentFileLabel, contentWidth)} ║");

        const int metricsWidth = 32;
        int fileBarWidth = Math.Max(5, contentWidth - metricsWidth - 1);
        string fileBar = BuildProgressBar(_currentFilePercent, Math.Max(3, fileBarWidth - 2));
        string fileMetrics = $"{_currentFilePercent,5:F1}% ({FormatBytes(_currentFileBytes)} / {FormatBytes(_currentFileTotalBytes)})";
        string fileLine = $"{fileBar} {PadOrTruncate(fileMetrics, metricsWidth)}";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(fileLine, contentWidth)} ║");
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}╠{new string('═', frameBorderWidth)}╣");

        long liveBytes = System.Threading.Interlocked.Read(ref _completedBytes) + _currentFileBytes;
        long effectiveCopiedBytes = Math.Min(_totalBytes, liveBytes);

        double overallPercent;
        if (_totalBytes > 0)
        {
            overallPercent = (double)effectiveCopiedBytes / _totalBytes * 100.0;
        }
        else if (_totalFiles > 0)
        {
            overallPercent = (double)processedFiles / _totalFiles * 100.0;
        }
        else
        {
            overallPercent = 100.0;
        }
        overallPercent = Math.Clamp(overallPercent, 0.0, 100.0);

        int totalBarWidth = Math.Max(5, contentWidth - 8 - metricsWidth - 1);
        string overallBar = BuildProgressBar(overallPercent, Math.Max(3, totalBarWidth - 2));
        string overallMetrics = $"{overallPercent,5:F1}% ({FormatBytes(effectiveCopiedBytes)} / {FormatBytes(_totalBytes)})";
        string overallLine = $"Total  : {overallBar} {PadOrTruncate(overallMetrics, metricsWidth)}";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(overallLine, contentWidth)} ║");

        TimeSpan elapsed = _stopwatch.Elapsed;
        double elapsedSec = Math.Max(0.001, elapsed.TotalSeconds);
        double bytesPerSec = effectiveCopiedBytes / elapsedSec;
        string speed = $"{FormatBytes((long)bytesPerSec)}/s";

        TimeSpan remainingTime = TimeSpan.Zero;
        if (bytesPerSec > 0 && _totalBytes > effectiveCopiedBytes && remainingFiles > 0)
        {
            double remainingSec = (_totalBytes - effectiveCopiedBytes) / bytesPerSec;
            if (remainingSec < 86400 * 7)
            {
                remainingTime = TimeSpan.FromSeconds(remainingSec);
            }
        }

        string statsLine = $"Speed  : {speed,-10} | Elapsed: {FormatTimeSpan(elapsed)} | Remaining: {FormatTimeSpan(remainingTime)}";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(statsLine, contentWidth)} ║");
    }

    /// <summary>
    /// Centres a text in a fixed-width area by padding it with spaces.
    /// </summary>
    /// <param name="text">Text to centre.</param>
    /// <param name="width">Largeur totale cible.</param>
    /// <returns>The centred text, padded with spaces.</returns>
    public static string CenterText(string text, int width)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new string(' ', Math.Max(0, width));
        }

        if (text.Length >= width)
        {
            return text[..width];
        }

        int leftPadding = (width - text.Length) / 2;
        int rightPadding = width - text.Length - leftPadding;
        return new string(' ', leftPadding) + text + new string(' ', rightPadding);
    }

    /// <summary>
    /// Builds a retro progress bar from full and shaded block characters.
    /// </summary>
    /// <param name="percent">Percentage between 0 and 100.</param>
    /// <param name="barInnerWidth">Number of characters inside the bar.</param>
    /// <returns>The progress bar as text, between brackets.</returns>
    public static string BuildProgressBar(double percent, int barInnerWidth)
    {
        int effectiveWidth = Math.Clamp(barInnerWidth, 5, 80);
        int filled = (int)Math.Round(Math.Clamp(percent, 0.0, 100.0) / 100.0 * effectiveWidth);
        filled = Math.Clamp(filled, 0, effectiveWidth);
        int empty = effectiveWidth - filled;

        return $"[{new string('█', filled)}{new string('░', empty)}]";
    }

    /// <summary>
    /// Formats a byte count with its unit (B, KB, MB, GB, TB, PB).
    /// </summary>
    /// <param name="bytes">Byte count.</param>
    /// <returns>The formatted value with its unit.</returns>
    public static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB", "PB"];
        int i = 0;
        double dblBytes = bytes;

        while (i < suffixes.Length - 1 && dblBytes >= 1024.0)
        {
            dblBytes /= 1024.0;
            i++;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", dblBytes, suffixes[i]);
    }

    /// <summary>
    /// Formats a duration as HH:MM:SS or MM:SS.
    /// </summary>
    /// <param name="ts">Duration to format.</param>
    /// <returns>The formatted duration.</returns>
    public static string FormatTimeSpan(TimeSpan ts)
    {
        return ts.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:D2}", (int)ts.TotalHours, ts.Minutes, ts.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}", ts.Minutes, ts.Seconds);
    }

    /// <summary>
    /// Shortens a path with a middle ellipsis when it is longer than the allowed length.
    /// </summary>
    /// <param name="path">Path to shorten.</param>
    /// <param name="maxLength">Maximum allowed length.</param>
    /// <returns>The shortened path.</returns>
    public static string ShortenPath(string path, int maxLength)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        if (path.Length <= maxLength)
        {
            return path;
        }

        if (maxLength <= 5)
        {
            return $"...{path[^Math.Max(1, maxLength - 3)..]}";
        }

        int halfLength = (maxLength - 3) / 2;
        return $"{path[..halfLength]}...{path[^halfLength..]}";
    }

    /// <summary>
    /// Fits a string to a fixed width by truncating it or padding it with spaces on the right.
    /// </summary>
    /// <param name="text">Input text.</param>
    /// <param name="width">Target fixed width.</param>
    /// <returns>A string whose length is exactly width.</returns>
    public static string PadOrTruncate(string text, int width)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new string(' ', Math.Max(0, width));
        }

        if (text.Length > width)
        {
            return text[..width];
        }

        return text.PadRight(width);
    }

    /// <summary>
    /// Stops the stopwatch and restores the console terminal state.
    /// </summary>
    public void Dispose()
    {
        _stopwatch.Stop();
        if (_isInteractive)
        {
            try
            {
                Render();
                Console.ResetColor();
                Console.CursorVisible = true;
                Console.SetCursorPosition(0, 15);
                Console.WriteLine();
            }
            catch (IOException)
            {
                // The console may already be closed; restoring the cursor is then pointless
            }
        }
    }
}

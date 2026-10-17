using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace FileForge.Terminal;

/// <summary>
/// Tableau de bord console rétro type Norton Commander affichant la progression en temps réel
/// d'une opération de synchronisation ou copie (fichier courant et progression globale),
/// centré sur l'écran, avec détection des échecs et progression fluide en continu.
/// </summary>
public sealed class ConsoleDashboard : IDisposable
{
    private static readonly object ConsoleLock = new();

    private readonly string _taskName;
    private readonly string _sourceRoot;
    private readonly string _destRoot;
    private readonly int _totalFiles;
    private readonly long _totalBytes;
    private readonly Stopwatch _stopwatch;
    private readonly bool _isInteractive;

    private int _completedFiles;
    private long _completedBytes;
    private int _failedFiles;
    private long _failedBytes;
    private string _currentFileName = string.Empty;
    private long _currentFileBytes;
    private long _currentFileTotalBytes;
    private double _currentFilePercent;
    private long _lastRenderTicks;
    private bool _hasCleared;
    private int _lastWindowWidth;
    private int _lastWindowHeight;

    public ConsoleDashboard(
        string taskName,
        string sourceRoot,
        string destRoot,
        int totalFiles,
        long totalBytes)
    {
        _taskName = taskName;
        _sourceRoot = sourceRoot;
        _destRoot = destRoot;
        _totalFiles = totalFiles;
        _totalBytes = totalBytes;
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
                // Toléré selon l'environnement de terminal
            }
        }
    }

    public void UpdateCurrentFile(string fileName, long bytesCopied, long fileTotalBytes)
    {
        _currentFileName = fileName;
        _currentFileBytes = bytesCopied;
        _currentFileTotalBytes = fileTotalBytes;
        _currentFilePercent = fileTotalBytes > 0 ? (double)bytesCopied / fileTotalBytes * 100.0 : 100.0;

        RenderIfThrottled();
    }

    public void CompleteFile(long fileSize)
    {
        System.Threading.Interlocked.Increment(ref _completedFiles);
        System.Threading.Interlocked.Add(ref _completedBytes, fileSize);
        _currentFileBytes = 0;

        RenderIfThrottled(force: true);
    }

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
                // Protection contre les erreurs E/S lors de redimensionnements intempestifs
            }
        }
    }

    internal void DrawNortonBox(int leftMargin, int boxWidth, int windowWidth)
    {
        int contentWidth = Math.Max(20, boxWidth - 4);
        int frameBorderWidth = contentWidth + 2;

        // 1. Haut du cadre
        DrawFrameBorder(leftMargin, frameBorderWidth, '╔', '═', '╗', windowWidth);

        // 2. Titre centré
        string title = _taskName;
        WriteContentLine(leftMargin, contentWidth, windowWidth, (CenterText(title, contentWidth), ConsoleColor.White));

        // 3. Séparateur
        DrawFrameBorder(leftMargin, frameBorderWidth, '╠', '═', '╣', windowWidth);

        // 4. Source
        string srcPath = ShortenPath(_sourceRoot, contentWidth - 9);
        WriteContentLine(leftMargin, contentWidth, windowWidth, ("Source : ", ConsoleColor.Cyan), (srcPath, ConsoleColor.White));

        // 5. Cible
        string dstPath = ShortenPath(_destRoot, contentWidth - 9);
        WriteContentLine(leftMargin, contentWidth, windowWidth, ("Cible  : ", ConsoleColor.Cyan), (dstPath, ConsoleColor.White));

        // 6. Séparateur
        DrawFrameBorder(leftMargin, frameBorderWidth, '╠', '═', '╣', windowWidth);

        // 7. Fichier : [ X / Y ] (Z restants [, W ignorés])
        int processedFiles = _completedFiles + _failedFiles;
        int remainingFiles = Math.Max(0, _totalFiles - processedFiles);
        int currentFileNum = Math.Min(_totalFiles, processedFiles + 1);
        string fileCounter = $"[ {currentFileNum} / {_totalFiles} ] ";

        if (_failedFiles > 0)
        {
            WriteContentLine(leftMargin, contentWidth, windowWidth,
                ("Fichier : ", ConsoleColor.Cyan),
                (fileCounter, ConsoleColor.White),
                ($"({remainingFiles} restants, ", ConsoleColor.Yellow),
                ($"{_failedFiles} en échec)", ConsoleColor.Red));
        }
        else
        {
            WriteContentLine(leftMargin, contentWidth, windowWidth,
                ("Fichier : ", ConsoleColor.Cyan),
                (fileCounter, ConsoleColor.White),
                ($"({remainingFiles} restants)", ConsoleColor.Yellow));
        }

        // 8. Nom du fichier courant
        string currentFileLabel = string.IsNullOrEmpty(_currentFileName) ? "(en attente)" : ShortenPath(_currentFileName, contentWidth);
        WriteContentLine(leftMargin, contentWidth, windowWidth, (currentFileLabel, ConsoleColor.White));

        // 9. Barre fichier
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

        // 10. Séparateur
        DrawFrameBorder(leftMargin, frameBorderWidth, '╠', '═', '╣', windowWidth);

        // 11. Barre globale (prend en compte en temps réel les octets du fichier en cours)
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
            ("Total : ", ConsoleColor.Cyan),
            ("[", ConsoleColor.Cyan),
            (new string('█', totalFilled), ConsoleColor.Green),
            (new string('░', totalEmpty), ConsoleColor.DarkCyan),
            ("]", ConsoleColor.Cyan),
            (PadOrTruncate(overallMetrics, metricsWidth), ConsoleColor.White));

        // 12. Statistiques (Débit, Temps écoulé, Restant)
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
            ("Débit : ", ConsoleColor.Cyan),
            ($"{speed,-10}", ConsoleColor.White),
            (" | Écoulé : ", ConsoleColor.Cyan),
            (FormatTimeSpan(elapsed), ConsoleColor.White),
            (" | Restant : ", ConsoleColor.Cyan),
            (FormatTimeSpan(remainingTime), remainingTime > TimeSpan.Zero ? ConsoleColor.Yellow : ConsoleColor.White));

        // 13. Bas du cadre
        DrawFrameBorder(leftMargin, frameBorderWidth, '╚', '═', '╝', windowWidth);

        Console.ResetColor();
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

    public string BuildDashboardString(int width) => BuildDashboardString(width, width);

    public string BuildDashboardString(int totalWidth, int boxWidth)
    {
        int effectiveBoxWidth = Math.Clamp(boxWidth, 50, Math.Max(50, totalWidth));
        int leftMargin = Math.Max(0, (totalWidth - effectiveBoxWidth) / 2);
        string indent = new string(' ', leftMargin);
        int contentWidth = Math.Max(20, effectiveBoxWidth - 4);
        int frameBorderWidth = contentWidth + 2;
        var sb = new System.Text.StringBuilder();

        // 1. En-tête
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}╔{new string('═', frameBorderWidth)}╗");
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {CenterText(_taskName, contentWidth)} ║");
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}╠{new string('═', frameBorderWidth)}╣");

        // 2. Chemins
        string srcText = $"Source : {ShortenPath(_sourceRoot, contentWidth - 9)}";
        string dstText = $"Cible  : {ShortenPath(_destRoot, contentWidth - 9)}";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(srcText, contentWidth)} ║");
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(dstText, contentWidth)} ║");
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}╠{new string('═', frameBorderWidth)}╣");

        // 3. Fichier courant
        int processedFiles = _completedFiles + _failedFiles;
        int remainingFiles = Math.Max(0, _totalFiles - processedFiles);
        int currentFileNum = Math.Min(_totalFiles, processedFiles + 1);
        string remainingText = _failedFiles > 0
            ? $"({remainingFiles} restants, {_failedFiles} en échec)"
            : $"({remainingFiles} restants)";
        string currentFileIndex = $"Fichier : [ {currentFileNum} / {_totalFiles} ] {remainingText}";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(currentFileIndex, contentWidth)} ║");

        string currentFileLabel = string.IsNullOrEmpty(_currentFileName) ? "(en attente)" : ShortenPath(_currentFileName, contentWidth);
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(currentFileLabel, contentWidth)} ║");

        const int metricsWidth = 32;
        int fileBarWidth = Math.Max(5, contentWidth - metricsWidth - 1);
        string fileBar = BuildProgressBar(_currentFilePercent, Math.Max(3, fileBarWidth - 2));
        string fileMetrics = $"{_currentFilePercent,5:F1}% ({FormatBytes(_currentFileBytes)} / {FormatBytes(_currentFileTotalBytes)})";
        string fileLine = $"{fileBar} {PadOrTruncate(fileMetrics, metricsWidth)}";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(fileLine, contentWidth)} ║");
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}╠{new string('═', frameBorderWidth)}╣");

        // 4. Progression globale
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
        string overallLine = $"Total : {overallBar} {PadOrTruncate(overallMetrics, metricsWidth)}";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(overallLine, contentWidth)} ║");

        // 5. Statistiques (Vitesse, temps écoulé, estimation restante)
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

        string statsLine = $"Débit : {speed,-10} | Écoulé : {FormatTimeSpan(elapsed)} | Restant : {FormatTimeSpan(remainingTime)}";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(statsLine, contentWidth)} ║");
        sb.Append(CultureInfo.InvariantCulture, $"{indent}╚{new string('═', frameBorderWidth)}╝");

        return sb.ToString();
    }

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

    public static string BuildProgressBar(double percent, int barInnerWidth)
    {
        int effectiveWidth = Math.Clamp(barInnerWidth, 5, 80);
        int filled = (int)Math.Round(Math.Clamp(percent, 0.0, 100.0) / 100.0 * effectiveWidth);
        filled = Math.Clamp(filled, 0, effectiveWidth);
        int empty = effectiveWidth - filled;

        return $"[{new string('█', filled)}{new string('░', empty)}]";
    }

    public static string FormatBytes(long bytes)
    {
        string[] suffixes = ["o", "Ko", "Mo", "Go", "To", "Po"];
        int i = 0;
        double dblBytes = bytes;

        while (i < suffixes.Length - 1 && dblBytes >= 1024.0)
        {
            dblBytes /= 1024.0;
            i++;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", dblBytes, suffixes[i]);
    }

    public static string FormatTimeSpan(TimeSpan ts)
    {
        return ts.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:D2}", (int)ts.TotalHours, ts.Minutes, ts.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}", ts.Minutes, ts.Seconds);
    }

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
                // Toléré si la console a été fermée
            }
        }
    }
}

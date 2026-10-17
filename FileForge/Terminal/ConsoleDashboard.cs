using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace FileForge.Terminal;

/// <summary>
/// Tableau de bord console rétro type Norton Commander affichant en temps réel
/// toutes les étapes d'une sauvegarde ou synchronisation (analyse, copie, à jour, terminé).
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
    /// Phase active dans laquelle se trouve le tableau de bord.
    /// </summary>
    public DashboardPhase Phase => _phase;

    /// <summary>
    /// Initialise un nouveau tableau de bord en phase d'analyse/inventaire.
    /// </summary>
    /// <param name="taskName">Nom de la tâche affiché dans l'en-tête.</param>
    /// <param name="sourceRoot">Répertoire source.</param>
    /// <param name="destRoot">Répertoire cible.</param>
    public ConsoleDashboard(string taskName, string sourceRoot, string destRoot)
        : this(taskName, sourceRoot, destRoot, 0, 0, DashboardPhase.Scanning)
    {
    }

    /// <summary>
    /// Initialise un nouveau tableau de bord en phase de transfert direct.
    /// </summary>
    /// <param name="taskName">Nom de la tâche affiché dans l'en-tête.</param>
    /// <param name="sourceRoot">Répertoire source.</param>
    /// <param name="destRoot">Répertoire cible.</param>
    /// <param name="totalFiles">Nombre total de fichiers à copier.</param>
    /// <param name="totalBytes">Volume total en octets à transférer.</param>
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
                // Toléré selon l'environnement de terminal
            }
            Render();
        }
    }

    /// <summary>
    /// Met à jour l'affichage lors de la phase d'inventaire.
    /// </summary>
    /// <param name="currentDirectory">Dossier actuellement exploré.</param>
    /// <param name="itemsFound">Nombre total d'éléments découverts.</param>
    public void UpdateScanning(string currentDirectory, int itemsFound)
    {
        _phase = DashboardPhase.Scanning;
        _currentScanningPath = currentDirectory;
        _scannedItems = itemsFound;
        System.Threading.Interlocked.Increment(ref _scannedDirs);

        RenderIfThrottled();
    }

    /// <summary>
    /// Initialise le plan de transfert et bascule le tableau de bord en phase de copie active.
    /// </summary>
    /// <param name="totalFiles">Nombre total de fichiers à copier.</param>
    /// <param name="totalBytes">Volume total d'octets à transférer.</param>
    public void SetTransferPlan(int totalFiles, long totalBytes)
    {
        _phase = DashboardPhase.Copying;
        _totalFiles = totalFiles;
        _totalBytes = totalBytes;

        RenderIfThrottled(force: true);
    }

    /// <summary>
    /// Bascule le tableau de bord en phase "à jour" si aucun fichier ne nécessite de transfert.
    /// </summary>
    /// <param name="message">Message d'état informatif.</param>
    public void SetUpToDate(string message = "Disque à jour (aucun changement détecté)")
    {
        _phase = DashboardPhase.UpToDate;
        _statusMessage = message;

        RenderIfThrottled(force: true);
    }

    /// <summary>
    /// Bascule le tableau de bord en phase de complétion finale.
    /// </summary>
    /// <param name="message">Message de synthèse final.</param>
    public void Complete(string message = "Sauvegarde validée avec succès")
    {
        _phase = DashboardPhase.Completed;
        _statusMessage = message;

        RenderIfThrottled(force: true);
    }

    /// <summary>
    /// Met à jour la progression du fichier individuel en cours de copie.
    /// </summary>
    /// <param name="fileName">Nom ou chemin relatif du fichier.</param>
    /// <param name="bytesCopied">Octets déjà transférés pour ce fichier.</param>
    /// <param name="fileTotalBytes">Taille totale de ce fichier en octets.</param>
    public void UpdateCurrentFile(string fileName, long bytesCopied, long fileTotalBytes)
    {
        _currentFileName = fileName;
        _currentFileBytes = bytesCopied;
        _currentFileTotalBytes = fileTotalBytes;
        _currentFilePercent = fileTotalBytes > 0 ? (double)bytesCopied / fileTotalBytes * 100.0 : 100.0;

        RenderIfThrottled();
    }

    /// <summary>
    /// Marque le fichier courant comme copié avec succès et incrémente les compteurs.
    /// </summary>
    /// <param name="fileSize">Taille du fichier terminé.</param>
    public void CompleteFile(long fileSize)
    {
        System.Threading.Interlocked.Increment(ref _completedFiles);
        System.Threading.Interlocked.Add(ref _completedBytes, fileSize);
        _currentFileBytes = 0;

        RenderIfThrottled(force: true);
    }

    /// <summary>
    /// Marque le fichier courant comme ayant échoué et incrémente les compteurs d'échec.
    /// </summary>
    /// <param name="fileSize">Taille du fichier en échec.</param>
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
    /// Force le rendu graphique immédiat du tableau de bord sur la console si la sortie est interactive.
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
                // Protection contre les erreurs E/S lors de redimensionnements intempestifs
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
            ("Inventaire / Analyse de la source en cours...", ConsoleColor.Yellow));

        WriteContentLine(leftMargin, contentWidth, windowWidth,
            ("Trouvés: ", ConsoleColor.Cyan),
            ($"{_scannedItems,6} élément(s) ", ConsoleColor.White),
            ($"({_scannedDirs} dossiers scannés)", ConsoleColor.DarkCyan));

        string scanDirLabel = string.IsNullOrEmpty(_currentScanningPath) ? "(initialisation)" : ShortenPath(_currentScanningPath, contentWidth);
        WriteContentLine(leftMargin, contentWidth, windowWidth, (scanDirLabel, ConsoleColor.White));

        DrawFrameBorder(leftMargin, frameBorderWidth, '╠', '═', '╣', windowWidth);

        TimeSpan scanElapsed = _stopwatch.Elapsed;
        WriteContentLine(leftMargin, contentWidth, windowWidth,
            ("Écoulé : ", ConsoleColor.Cyan),
            (FormatTimeSpan(scanElapsed), ConsoleColor.White),
            (" | Indexation de l'arborescence...", ConsoleColor.DarkCyan));
    }

    private void DrawUpToDateContent(int leftMargin, int contentWidth, int windowWidth, int frameBorderWidth)
    {
        string status = string.IsNullOrEmpty(_statusMessage) ? "Disque déjà à jour (aucun changement)" : _statusMessage;
        WriteContentLine(leftMargin, contentWidth, windowWidth,
            ("Statut : ", ConsoleColor.Cyan),
            (status, ConsoleColor.Green));

        WriteContentLine(leftMargin, contentWidth, windowWidth,
            ("Fichier: ", ConsoleColor.Cyan),
            ("Aucun fichier à transférer (0 / 0)", ConsoleColor.White));

        int barWidth = Math.Max(5, contentWidth - 2);
        string fullBar = BuildProgressBar(100.0, barWidth);
        WriteContentLine(leftMargin, contentWidth, windowWidth, (fullBar, ConsoleColor.Green));

        DrawFrameBorder(leftMargin, frameBorderWidth, '╠', '═', '╣', windowWidth);

        WriteContentLine(leftMargin, contentWidth, windowWidth,
            ("Écoulé : ", ConsoleColor.Cyan),
            (FormatTimeSpan(_stopwatch.Elapsed), ConsoleColor.White),
            (" | Aucun transfert nécessaire", ConsoleColor.Green));
    }

    private void DrawCompletedContent(int leftMargin, int contentWidth, int windowWidth, int frameBorderWidth)
    {
        string status = string.IsNullOrEmpty(_statusMessage) ? "Sauvegarde terminée avec succès" : _statusMessage;
        WriteContentLine(leftMargin, contentWidth, windowWidth,
            ("Statut : ", ConsoleColor.Cyan),
            (status, ConsoleColor.Green));

        string compCounter = $"[ {_completedFiles} / {_totalFiles} ] ";
        if (_failedFiles > 0)
        {
            WriteContentLine(leftMargin, contentWidth, windowWidth,
                ("Fichier: ", ConsoleColor.Cyan),
                (compCounter, ConsoleColor.White),
                ($"{_completedFiles} copiés, ", ConsoleColor.Green),
                ($"{_failedFiles} en échec", ConsoleColor.Red));
        }
        else
        {
            WriteContentLine(leftMargin, contentWidth, windowWidth,
                ("Fichier: ", ConsoleColor.Cyan),
                (compCounter, ConsoleColor.White),
                ("Tous les fichiers ont été copiés avec succès", ConsoleColor.Green));
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
            ("Débit  : ", ConsoleColor.Cyan),
            ($"{FormatBytes((long)avgSpeed)}/s", ConsoleColor.White),
            (" | Durée totale : ", ConsoleColor.Cyan),
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
                ("Fichier: ", ConsoleColor.Cyan),
                (fileCounter, ConsoleColor.White),
                ($"({remainingFiles} restants, ", ConsoleColor.Yellow),
                ($"{_failedFiles} en échec)", ConsoleColor.Red));
        }
        else
        {
            WriteContentLine(leftMargin, contentWidth, windowWidth,
                ("Fichier: ", ConsoleColor.Cyan),
                (fileCounter, ConsoleColor.White),
                ($"({remainingFiles} restants)", ConsoleColor.Yellow));
        }

        string currentFileLabel = string.IsNullOrEmpty(_currentFileName) ? "(en attente)" : ShortenPath(_currentFileName, contentWidth);
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
            ("Débit  : ", ConsoleColor.Cyan),
            ($"{speed,-10}", ConsoleColor.White),
            (" | Écoulé : ", ConsoleColor.Cyan),
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
    /// Génère une représentation textuelle brute du tableau de bord pour une largeur donnée.
    /// </summary>
    /// <param name="width">Largeur totale de l'affichage.</param>
    /// <returns>Chaîne formatée représentant le tableau de bord rétro.</returns>
    public string BuildDashboardString(int width) => BuildDashboardString(width, width);

    /// <summary>
    /// Génère une représentation textuelle brute du tableau de bord avec centrage selon la largeur de la boîte et la largeur totale.
    /// </summary>
    /// <param name="totalWidth">Largeur totale du conteneur.</param>
    /// <param name="boxWidth">Largeur intérieure de la boîte Norton.</param>
    /// <returns>Chaîne formatée représentant le tableau de bord rétro.</returns>
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
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate("Statut : Inventaire / Analyse de la source en cours...", contentWidth)} ║");
        string itemsText = $"Trouvés: {_scannedItems} éléments ({_scannedDirs} dossiers scannés)";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(itemsText, contentWidth)} ║");
        string scanDir = string.IsNullOrEmpty(_currentScanningPath) ? "(initialisation)" : ShortenPath(_currentScanningPath, contentWidth);
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(scanDir, contentWidth)} ║");
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}╠{new string('═', frameBorderWidth)}╣");
        string stats = $"Écoulé : {FormatTimeSpan(_stopwatch.Elapsed)} | Indexation de l'arborescence...";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(stats, contentWidth)} ║");
    }

    private void AppendUpToDateString(System.Text.StringBuilder sb, string indent, int contentWidth, int frameBorderWidth)
    {
        string status = string.IsNullOrEmpty(_statusMessage) ? "Statut : Disque déjà à jour" : $"Statut : {_statusMessage}";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(status, contentWidth)} ║");
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate("Fichier: Aucun fichier à transférer (0 / 0)", contentWidth)} ║");
        int barWidth = Math.Max(5, contentWidth - 2);
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(BuildProgressBar(100.0, barWidth), contentWidth)} ║");
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}╠{new string('═', frameBorderWidth)}╣");
        string stats = $"Écoulé : {FormatTimeSpan(_stopwatch.Elapsed)} | Aucun transfert nécessaire";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(stats, contentWidth)} ║");
    }

    private void AppendCompletedString(System.Text.StringBuilder sb, string indent, int contentWidth, int frameBorderWidth)
    {
        string status = string.IsNullOrEmpty(_statusMessage) ? "Statut : Sauvegarde terminée avec succès" : $"Statut : {_statusMessage}";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(status, contentWidth)} ║");
        string countText = _failedFiles > 0
            ? $"Fichier: [ {_completedFiles} / {_totalFiles} ] ({_failedFiles} en échec)"
            : $"Fichier: [ {_completedFiles} / {_totalFiles} ] (Tous copiés)";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(countText, contentWidth)} ║");
        int barWidth = Math.Max(5, contentWidth - 2);
        double finalPercent = _totalFiles > 0 ? (double)_completedFiles / _totalFiles * 100.0 : 100.0;
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(BuildProgressBar(finalPercent, barWidth), contentWidth)} ║");
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}╠{new string('═', frameBorderWidth)}╣");

        TimeSpan elapsed = _stopwatch.Elapsed;
        double elapsedSec = Math.Max(0.001, elapsed.TotalSeconds);
        double avgSpeed = _completedBytes / elapsedSec;
        string stats = $"Débit  : {FormatBytes((long)avgSpeed)}/s | Durée totale : {FormatTimeSpan(elapsed)}";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(stats, contentWidth)} ║");
    }

    private void AppendCopyingString(System.Text.StringBuilder sb, string indent, int contentWidth, int frameBorderWidth)
    {
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

        string statsLine = $"Débit  : {speed,-10} | Écoulé : {FormatTimeSpan(elapsed)} | Restant : {FormatTimeSpan(remainingTime)}";
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║ {PadOrTruncate(statsLine, contentWidth)} ║");
    }

    /// <summary>
    /// Centre un texte au milieu d'une zone de largeur fixe en ajoutant des espaces compensatoires.
    /// </summary>
    /// <param name="text">Texte à centrer.</param>
    /// <param name="width">Largeur totale cible.</param>
    /// <returns>Texte centré entouré d'espaces.</returns>
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
    /// Construit une barre de progression rétro utilisant des caractères blocs pleins et ombrés.
    /// </summary>
    /// <param name="percent">Pourcentage entre 0 et 100.</param>
    /// <param name="barInnerWidth">Nombre de caractères intérieurs pour la barre.</param>
    /// <returns>Représentation textuelle de la barre de progression entourée de crochets.</returns>
    public static string BuildProgressBar(double percent, int barInnerWidth)
    {
        int effectiveWidth = Math.Clamp(barInnerWidth, 5, 80);
        int filled = (int)Math.Round(Math.Clamp(percent, 0.0, 100.0) / 100.0 * effectiveWidth);
        filled = Math.Clamp(filled, 0, effectiveWidth);
        int empty = effectiveWidth - filled;

        return $"[{new string('█', filled)}{new string('░', empty)}]";
    }

    /// <summary>
    /// Formate un volume d'octets avec l'unité correspondante (o, Ko, Mo, Go, To, Po).
    /// </summary>
    /// <param name="bytes">Nombre d'octets.</param>
    /// <returns>Chaîne formatée avec unité.</returns>
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

    /// <summary>
    /// Formate une durée en format HH:MM:SS ou MM:SS.
    /// </summary>
    /// <param name="ts">Durée à formater.</param>
    /// <returns>Chaîne temporelle formatée.</returns>
    public static string FormatTimeSpan(TimeSpan ts)
    {
        return ts.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:D2}", (int)ts.TotalHours, ts.Minutes, ts.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}", ts.Minutes, ts.Seconds);
    }

    /// <summary>
    /// Raccourcit un chemin d'accès au moyen d'une ellipse centrale s'il dépasse la longueur maximale allouée.
    /// </summary>
    /// <param name="path">Chemin à abréger.</param>
    /// <param name="maxLength">Longueur maximale tolérée.</param>
    /// <returns>Chemin raccourci.</returns>
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
    /// Ajuste une chaîne à une largeur fixe en la tronquant ou en la complétant par des espaces à droite.
    /// </summary>
    /// <param name="text">Texte d'entrée.</param>
    /// <param name="width">Largeur fixe souhaitée.</param>
    /// <returns>Chaîne de longueur exactement égale à width.</returns>
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
    /// Libère les ressources du chronomètre et restaure l'état du terminal de la console.
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
                // Toléré si la console a été fermée
            }
        }
    }
}

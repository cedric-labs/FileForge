using System;

namespace FileForge.Diagnostics;

/// <summary>
/// Événement portant un message textuel de journalisation pour les opérations FileForge.
/// </summary>
public sealed class LogMessageEventArgs(string message) : EventArgs
{
    /// <summary>
    /// Message de journalisation émis.
    /// </summary>
    public string Message { get; } = message;
}

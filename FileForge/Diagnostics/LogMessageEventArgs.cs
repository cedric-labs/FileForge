using System;

namespace FileForge.Diagnostics;

/// <summary>
/// Event data carrying a log message emitted by a FileForge operation.
/// </summary>
public sealed class LogMessageEventArgs(string message) : EventArgs
{
    /// <summary>
    /// The log message.
    /// </summary>
    public string Message { get; } = message;
}

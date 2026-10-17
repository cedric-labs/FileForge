using System;

namespace FileForge.Diagnostics;

public sealed class LogMessageEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

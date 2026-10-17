using FileForge.Diagnostics;
using FluentAssertions;
using Xunit;

namespace FileForge.Tests;

public class LogMessageEventArgsTests
{
    [Fact]
    public void Constructor_ShouldSetMessageProperty()
    {
        var args = new LogMessageEventArgs("Test message");
        args.Message.Should().Be("Test message");
    }
}

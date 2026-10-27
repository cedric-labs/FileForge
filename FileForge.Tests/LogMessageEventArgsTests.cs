using AwesomeAssertions;
using FileForge.Diagnostics;
using Xunit;

namespace FileForge.Tests;

public class LogMessageEventArgsTests
{
    [Fact]
    public void Constructor_WhenMessageProvided_SetsMessageProperty()
    {
        var args = new LogMessageEventArgs("Test message");
        args.Message.Should().Be("Test message");
    }
}

using FileForge.IO;
using FluentAssertions;
using Xunit;

namespace FileForge.Tests;

public class FileOperationProgressTests
{
    [Fact]
    public void PercentComplete_WhenBytesProcessed_CalculatesAccurately()
    {
        var progress = new FileOperationProgress(@"C:\src.txt", @"D:\dst.txt", totalBytes: 1000, processedBytes: 250, FileOperationType.Copy);

        progress.PercentComplete.Should().Be(25.0);
        progress.OperationType.Should().Be(FileOperationType.Copy);
        progress.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void PercentComplete_WhenTotalBytesIsZero_ReturnsZero()
    {
        var progress = new FileOperationProgress(@"C:\src.txt", @"D:\dst.txt", totalBytes: 0, processedBytes: 0, FileOperationType.Inactive, isCompleted: true);

        progress.PercentComplete.Should().Be(0);
        progress.IsCompleted.Should().BeTrue();
    }
}

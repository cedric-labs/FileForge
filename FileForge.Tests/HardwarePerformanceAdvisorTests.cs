using System;
using AwesomeAssertions;
using FileForge.Storage;
using Xunit;

namespace FileForge.Tests;

public class HardwarePerformanceAdvisorTests
{
    [Fact]
    public void CalculateOptimalParallelism_WhenPathsAreValid_ReturnsAtLeastOne()
    {
        int parallelism = HardwarePerformanceAdvisor.CalculateOptimalParallelism(@"C:\Source", @"D:\Destination");

        parallelism.Should().BeGreaterThanOrEqualTo(1);
        parallelism.Should().BeLessThanOrEqualTo(Environment.ProcessorCount);
    }

    [Fact]
    public void IsSsdDrive_WhenPathIsEmptyOrWhitespace_ReturnsFalse()
    {
        HardwarePerformanceAdvisor.IsSsdDrive(string.Empty).Should().BeFalse();
        HardwarePerformanceAdvisor.IsSsdDrive("   ").Should().BeFalse();
    }

    [Fact]
    public void GetAvailableMemoryInGB_WhenQueried_ReturnsPositiveNumber()
    {
        double memory = HardwarePerformanceAdvisor.GetAvailableMemoryInGB();

        memory.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(@"\\server\share\sub")]
    [InlineData(@"Z:\NonExistentDrive\Data")]
    public void IsSsdDrive_WhenUncOrNonExistentDrive_ReturnsFalse(string path)
    {
        bool isSsd = HardwarePerformanceAdvisor.IsSsdDrive(path);

        isSsd.Should().BeFalse();
    }

    [Fact]
    public void CalculateOptimalParallelism_WhenOneSideIsUnc_ReturnsOne()
    {
        int parallelism = HardwarePerformanceAdvisor.CalculateOptimalParallelism(@"\\server\share", @"C:\Local");

        parallelism.Should().Be(1);
    }

    [Fact]
    public void AdvisorLog_WhenEventSubscribed_ReceivesOrIgnoresEventsCorrectly()
    {
        bool logged = false;
        EventHandler<FileForge.Diagnostics.LogMessageEventArgs> handler = (_, _) => logged = true;

        HardwarePerformanceAdvisor.AdvisorLog += handler;
        try
        {
            HardwarePerformanceAdvisor.IsSsdDrive(@"C:\ValidPath");
        }
        finally
        {
            HardwarePerformanceAdvisor.AdvisorLog -= handler;
        }

        logged.Should().BeFalse(); // pas d'erreur sur chemin valide
    }
}

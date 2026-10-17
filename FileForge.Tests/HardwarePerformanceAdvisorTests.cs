using System;
using FileForge.Storage;
using FluentAssertions;
using Xunit;

namespace FileForge.Tests;

public class HardwarePerformanceAdvisorTests
{
    [Fact]
    public void CalculateOptimalParallelism_ValidPaths_ShouldReturnAtLeastOne()
    {
        int parallelism = HardwarePerformanceAdvisor.CalculateOptimalParallelism(@"C:\Source", @"D:\Destination");

        parallelism.Should().BeGreaterOrEqualTo(1);
        parallelism.Should().BeLessOrEqualTo(Environment.ProcessorCount);
    }

    [Fact]
    public void IsSsdDrive_WhenPathIsEmptyOrWhitespace_ShouldReturnFalse()
    {
        HardwarePerformanceAdvisor.IsSsdDrive(string.Empty).Should().BeFalse();
        HardwarePerformanceAdvisor.IsSsdDrive("   ").Should().BeFalse();
    }

    [Fact]
    public void GetAvailableMemoryInGB_ShouldReturnPositiveNumber()
    {
        double memory = HardwarePerformanceAdvisor.GetAvailableMemoryInGB();

        memory.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(@"\\server\share\sub")]
    [InlineData(@"Z:\NonExistentDrive\Data")]
    public void IsSsdDrive_WhenUncOrNonExistentDrive_ShouldReturnFalse(string path)
    {
        bool isSsd = HardwarePerformanceAdvisor.IsSsdDrive(path);

        isSsd.Should().BeFalse();
    }

    [Fact]
    public void CalculateOptimalParallelism_WhenOneSideIsUnc_ShouldReturnOne()
    {
        int parallelism = HardwarePerformanceAdvisor.CalculateOptimalParallelism(@"\\server\share", @"C:\Local");

        parallelism.Should().Be(1);
    }

    [Fact]
    public void AdvisorLog_EventSubscription_ShouldWork()
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

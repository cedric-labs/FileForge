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
}

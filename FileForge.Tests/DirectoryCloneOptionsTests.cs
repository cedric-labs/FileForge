using AwesomeAssertions;
using FileForge.Sync;
using Xunit;

namespace FileForge.Tests;

public class DirectoryCloneOptionsTests
{
    [Fact]
    public void IsExcluded_WhenPathIsEmptyOrNull_ReturnsFalse()
    {
        var options = new DirectoryCloneOptions();

        options.IsExcluded(string.Empty).Should().BeFalse();
        options.IsExcluded(null).Should().BeFalse();
    }

    [Fact]
    public void IsExcluded_WhenPathMatchesExactly_ReturnsTrue()
    {
        var options = new DirectoryCloneOptions
        {
            ExcludedPaths = [@"C:\Data\Temp", @"D:\Logs"]
        };

        options.IsExcluded(@"C:\Data\Temp").Should().BeTrue();
        options.IsExcluded(@"c:\data\temp").Should().BeTrue();
    }

    [Fact]
    public void IsExcluded_WhenPathIsSubfolderOfExcluded_ReturnsTrue()
    {
        var options = new DirectoryCloneOptions
        {
            ExcludedPaths = [@"C:\Data\Temp"]
        };

        options.IsExcluded(@"C:\Data\Temp\sub\file.txt").Should().BeTrue();
    }

    [Fact]
    public void IsExcluded_WhenPathIsNotExcluded_ReturnsFalse()
    {
        var options = new DirectoryCloneOptions
        {
            ExcludedPaths = [@"C:\Data\Temp"]
        };

        options.IsExcluded(@"C:\Data\Projects\App.cs").Should().BeFalse();
    }
}

using System;
using System.IO;
using FileForge.Storage;
using FluentAssertions;
using Xunit;

namespace FileForge.Tests;

public class VolumeIdentityResolverTests
{
    [Fact]
    public void GetDiskId_WhenPathIsEmptyOrWhitespace_ShouldReturnNull()
    {
        VolumeIdentityResolver.GetDiskId(null).Should().BeNull();
        VolumeIdentityResolver.GetDiskId(string.Empty).Should().BeNull();
        VolumeIdentityResolver.GetDiskId("   ").Should().BeNull();
    }

    [Fact]
    public void GetUncRoot_WhenUncPathValid_ShouldExtractServerAndShare()
    {
        string? root = VolumeIdentityResolver.GetUncRoot(@"\\nas01\backup\folder\file.txt");

        root.Should().Be(@"\\nas01\backup");
    }

    [Fact]
    public void GetUncRoot_WhenEmptyOrWhitespace_ShouldReturnNull()
    {
        VolumeIdentityResolver.GetUncRoot(null).Should().BeNull();
        VolumeIdentityResolver.GetUncRoot(string.Empty).Should().BeNull();
    }

    [Fact]
    public void GetUncRoot_WhenSingleServerSegment_ShouldReturnOriginal()
    {
        VolumeIdentityResolver.GetUncRoot(@"\\server").Should().Be(@"\\server");
    }

    [Fact]
    public void GetDiskId_WhenLocalDrive_ShouldReturnVolumeIdentifier()
    {
        var rootDir = Path.GetPathRoot(Environment.CurrentDirectory) ?? @"C:\";
        var diskId = VolumeIdentityResolver.GetDiskId(rootDir);

        diskId.Should().NotBeNull();
        diskId.Should().StartWith("VOL:");
    }

    [Fact]
    public void GetDiskId_WhenUncPath_ShouldReturnUncIdentifier()
    {
        var diskId = VolumeIdentityResolver.GetDiskId(@"\\nas\share\folder");

        diskId.Should().Be(@"UNC:\\nas\share");
    }

    [Fact]
    public void GetVolumeIdFromPath_WhenPathIsRelativeOrEmpty_ShouldReturnNull()
    {
        VolumeIdentityResolver.GetVolumeIdFromPath(string.Empty).Should().BeNull();
        VolumeIdentityResolver.GetVolumeIdFromPath("relative/subfolder").Should().BeNull();
    }
}

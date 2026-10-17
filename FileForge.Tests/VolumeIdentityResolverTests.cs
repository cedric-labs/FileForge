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

    [Theory]
    [InlineData(@"UNC:\karine-pc\Didi", @"UNC:\\karine-pc\Didi", true)]
    [InlineData(@"UNC:\\karine-pc\didi", @"unc:\karine-pc\DIDI", true)]
    [InlineData(@"UNC:/karine-pc/Didi", @"UNC:\karine-pc\Didi", true)]
    [InlineData(@"VOL:12345678", @"vol:12345678", true)]
    [InlineData(@"VOL:12345678", @"VOL:99999999", false)]
    [InlineData(@"UNC:\server\share1", @"UNC:\server\share2", false)]
    [InlineData(null, null, true)]
    [InlineData(null, @"UNC:\server\share", false)]
    [InlineData(@"UNC:\server\share", null, false)]
    public void AreMatchingDiskIds_ShouldCorrectlyMatchVariants(string? actual, string? expected, bool expectedResult)
    {
        bool result = VolumeIdentityResolver.AreMatchingDiskIds(actual, expected);
        result.Should().Be(expectedResult);
    }

    [Theory]
    [InlineData(@"UNC:\karine-pc\Didi", @"UNC:\\karine-pc\Didi")]
    [InlineData(@"UNC:\\karine-pc\Didi", @"UNC:\\karine-pc\Didi")]
    [InlineData(@"UNC:/karine-pc/Didi", @"UNC:\\karine-pc\Didi")]
    [InlineData(@"VOL:ABCDEF12", @"VOL:ABCDEF12")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void NormalizeDiskId_ShouldHarmonizePrefix(string input, string expected)
    {
        string normalized = VolumeIdentityResolver.NormalizeDiskId(input);
        normalized.Should().Be(expected);
    }
}

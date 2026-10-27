using System;
using System.IO;
using AwesomeAssertions;
using FileForge.Storage;
using Xunit;

namespace FileForge.Tests;

public class VolumeIdentityResolverTests
{
    [Fact]
    public void GetDiskId_WhenPathIsEmptyOrWhitespace_ReturnsNull()
    {
        VolumeIdentityResolver.GetDiskId(null).Should().BeNull();
        VolumeIdentityResolver.GetDiskId(string.Empty).Should().BeNull();
        VolumeIdentityResolver.GetDiskId("   ").Should().BeNull();
    }

    [Fact]
    public void GetUncRoot_WhenUncPathIsValid_ExtractsServerAndShare()
    {
        string? root = VolumeIdentityResolver.GetUncRoot(@"\\nas01\backup\folder\file.txt");

        root.Should().Be(@"\\nas01\backup");
    }

    [Fact]
    public void GetUncRoot_WhenPathIsEmptyOrWhitespace_ReturnsNull()
    {
        VolumeIdentityResolver.GetUncRoot(null).Should().BeNull();
        VolumeIdentityResolver.GetUncRoot(string.Empty).Should().BeNull();
    }

    [Fact]
    public void GetUncRoot_WhenPathIsSingleServerSegment_ReturnsOriginal()
    {
        VolumeIdentityResolver.GetUncRoot(@"\\server").Should().Be(@"\\server");
    }

    [Fact]
    public void GetDiskId_WhenPathIsLocalDrive_ReturnsVolumeIdentifier()
    {
        var rootDir = Path.GetPathRoot(Environment.CurrentDirectory) ?? @"C:\";
        var diskId = VolumeIdentityResolver.GetDiskId(rootDir);

        diskId.Should().NotBeNull();
        diskId.Should().StartWith("VOL:");
    }

    [Fact]
    public void GetDiskId_WhenPathIsUnc_ReturnsUncIdentifier()
    {
        var diskId = VolumeIdentityResolver.GetDiskId(@"\\nas\share\folder");

        diskId.Should().Be(@"UNC:\\nas\share");
    }

    [Fact]
    public void GetVolumeIdFromPath_WhenPathIsRelativeOrEmpty_ReturnsNull()
    {
        VolumeIdentityResolver.GetVolumeIdFromPath(string.Empty).Should().BeNull();
        VolumeIdentityResolver.GetVolumeIdFromPath("relative/subfolder").Should().BeNull();
    }

    [Fact]
    public void GetVolumeIdFromPath_WhenDriveIsInvalid_ReturnsNull()
    {
        VolumeIdentityResolver.GetVolumeIdFromPath(@"Z:\NonExistentDrive_12345\").Should().BeNull();
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
    public void AreMatchingDiskIds_WhenComparingVariants_ReturnsExpectedResult(string? actual, string? expected, bool expectedResult)
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
    public void NormalizeDiskId_WhenPrefixProvided_HarmonizesPrefix(string input, string expected)
    {
        string normalized = VolumeIdentityResolver.NormalizeDiskId(input);
        normalized.Should().Be(expected);
    }
}

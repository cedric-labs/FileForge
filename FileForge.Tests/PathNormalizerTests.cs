using System;
using AwesomeAssertions;
using FileForge.Paths;
using Xunit;

namespace FileForge.Tests;

public class PathNormalizerTests
{
    [Theory]
    [InlineData(@"\\?\C:\Temp", @"C:\Temp")]
    [InlineData(@"\\?\UNC\server\share", @"\\server\share")]
    [InlineData(@"D:\Data\File.txt", @"D:\Data\File.txt")]
    public void RemoveLongPathPrefix_WhenPrefixedPathProvided_RemovesPrefixCorrectly(string input, string expected)
    {
        var result = PathNormalizer.RemoveLongPathPrefix(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void NormalizeLongPath_WhenPathIsNullOrEmpty_ThrowsArgumentException()
    {
        var action = () => PathNormalizer.NormalizeLongPath(string.Empty);

        action.Should().Throw<ArgumentException>();
    }
}

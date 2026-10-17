using System;
using FileForge.Terminal;
using FluentAssertions;
using Xunit;

namespace FileForge.Tests;

public class ConsoleDashboardTests
{
    [Theory]
    [InlineData(0, 10, "[░░░░░░░░░░]")]
    [InlineData(50, 10, "[█████░░░░░]")]
    [InlineData(100, 10, "[██████████]")]
    [InlineData(-10, 10, "[░░░░░░░░░░]")]
    [InlineData(150, 10, "[██████████]")]
    public void BuildProgressBar_ReturnsExpectedVisualRepresentation(double percent, int barWidth, string expected)
    {
        string result = ConsoleDashboard.BuildProgressBar(percent, barWidth);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildProgressBar_ClampsWidthCorrectly()
    {
        string minResult = ConsoleDashboard.BuildProgressBar(50, 2);
        Assert.Equal("[██░░░]", minResult);

        string maxResult = ConsoleDashboard.BuildProgressBar(50, 100);
        Assert.Equal(82, maxResult.Length);
    }

    [Theory]
    [InlineData(0, "0 o")]
    [InlineData(512, "512 o")]
    [InlineData(1024, "1 Ko")]
    [InlineData(1536, "1.5 Ko")]
    [InlineData(1048576, "1 Mo")]
    [InlineData(1073741824, "1 Go")]
    public void FormatBytes_FormatsCorrectly(long bytes, string expected)
    {
        string result = ConsoleDashboard.FormatBytes(bytes);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0, 5, 23, "05:23")]
    [InlineData(1, 2, 3, "01:02:03")]
    [InlineData(25, 0, 0, "25:00:00")]
    public void FormatTimeSpan_FormatsHoursAndMinutesCorrectly(int hours, int minutes, int seconds, string expected)
    {
        var ts = new TimeSpan(hours, minutes, seconds);
        string result = ConsoleDashboard.FormatTimeSpan(ts);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ShortenPath_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ConsoleDashboard.ShortenPath(string.Empty, 20));
    }

    [Fact]
    public void ShortenPath_ShortEnough_ReturnsOriginal()
    {
        string path = @"C:\Folder\file.txt";
        Assert.Equal(path, ConsoleDashboard.ShortenPath(path, 50));
    }

    [Fact]
    public void ShortenPath_TooLong_ShortensWithEllipsis()
    {
        string path = @"C:\Users\user\AppData\Local\Temp\VeryLongDirectoryName\SubFolder\TargetFile.txt";
        string result = ConsoleDashboard.ShortenPath(path, 30);

        Assert.True(result.Length <= 30);
        Assert.Contains("...", result);
    }

    [Fact]
    public void PadOrTruncate_BehavesAsExpected()
    {
        Assert.Equal("     ", ConsoleDashboard.PadOrTruncate(string.Empty, 5));
        Assert.Equal("abc  ", ConsoleDashboard.PadOrTruncate("abc", 5));
        Assert.Equal("abcde", ConsoleDashboard.PadOrTruncate("abcdefgh", 5));
    }

    [Fact]
    public void BuildDashboardString_ContainsHeadersPathsAndCounts()
    {
        using var dashboard = new ConsoleDashboard(
            taskName: "Sauvegarde Test",
            sourceRoot: @"D:\Source\Data",
            destRoot: @"G:\Backup\Data",
            totalFiles: 42,
            totalBytes: 1048576);

        string box = dashboard.BuildDashboardString(80);

        Assert.Contains("Sauvegarde Test", box);
        Assert.Contains("Source :", box);
        Assert.Contains("Cible  :", box);
        Assert.Contains("[ 1 / 42 ]", box);
        Assert.Contains("(42 restants)", box);
        Assert.Contains("╔", box);
        Assert.Contains("╚", box);
    }

    [Fact]
    public void UpdateCurrentFile_And_CompleteFile_UpdateCountersAndRenderSafely()
    {
        using var dashboard = new ConsoleDashboard(
            taskName: "Test Run",
            sourceRoot: @"D:\Source",
            destRoot: @"G:\Dest",
            totalFiles: 2,
            totalBytes: 2000);

        dashboard.UpdateCurrentFile("document.pdf", 500, 1000);
        dashboard.CompleteFile(1000);

        string box = dashboard.BuildDashboardString(80);
        Assert.Contains("[ 2 / 2 ]", box);
        Assert.Contains("(1 restants)", box);

        dashboard.CompleteFile(1000);
        string finalBox = dashboard.BuildDashboardString(80);
        Assert.Contains("(0 restants)", finalBox);

        dashboard.Render();
    }

    [Fact]
    public void CenterText_CentersCorrectly()
    {
        Assert.Equal("   test   ", ConsoleDashboard.CenterText("test", 10));
        Assert.Equal("test", ConsoleDashboard.CenterText("test", 4));
        Assert.Equal("te", ConsoleDashboard.CenterText("test", 2));
    }

    [Fact]
    public void BuildDashboardString_CentersWithLeftMargin()
    {
        using var dashboard = new ConsoleDashboard(
            taskName: "Center Test",
            sourceRoot: @"D:\Source",
            destRoot: @"G:\Dest",
            totalFiles: 10,
            totalBytes: 5000);

        string box = dashboard.BuildDashboardString(totalWidth: 100, boxWidth: 60);
        Assert.StartsWith(new string(' ', 20) + "╔", box, StringComparison.Ordinal);
    }

    [Fact]
    public void FailFile_UpdatesCountersAndShowsIgnoredFiles()
    {
        using var dashboard = new ConsoleDashboard(
            taskName: "Fail Test",
            sourceRoot: @"D:\Source",
            destRoot: @"G:\Dest",
            totalFiles: 4,
            totalBytes: 4000);

        dashboard.CompleteFile(1000);
        dashboard.FailFile(1000);

        string box = dashboard.BuildDashboardString(80);
        Assert.Contains("[ 3 / 4 ]", box);
        Assert.Contains("(2 restants, 1 ignorés)", box);
    }

    [Fact]
    public void DrawNortonBox_ShouldRenderDirectlyWithoutThrowing()
    {
        using var dashboard = new ConsoleDashboard(
            taskName: "Render Test",
            sourceRoot: @"D:\Source\Directory",
            destRoot: @"E:\Dest\Directory",
            totalFiles: 3,
            totalBytes: 3000);

        var act1 = () => dashboard.DrawNortonBox(leftMargin: 2, boxWidth: 70, windowWidth: 80);
        act1.Should().NotThrow();

        dashboard.UpdateCurrentFile("active_file.txt", 500, 1000);
        var act2 = () => dashboard.DrawNortonBox(leftMargin: 0, boxWidth: 60, windowWidth: 60);
        act2.Should().NotThrow();

        dashboard.CompleteFile(1000);
        dashboard.FailFile(1000);
        var act3 = () => dashboard.DrawNortonBox(leftMargin: 5, boxWidth: 80, windowWidth: 100);
        act3.Should().NotThrow();
    }

    [Fact]
    public void DrawNortonBox_WithZeroTotals_ShouldRenderGracefully()
    {
        using var dashboard = new ConsoleDashboard(
            taskName: "Zero Test",
            sourceRoot: @"D:\EmptySrc",
            destRoot: @"E:\EmptyDst",
            totalFiles: 0,
            totalBytes: 0);

        var act = () => dashboard.DrawNortonBox(leftMargin: 0, boxWidth: 60, windowWidth: 60);
        act.Should().NotThrow();
    }
}

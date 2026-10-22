using FileForge.IO;
using FluentAssertions;
using Xunit;

namespace FileForge.Tests.IO;

public sealed class PersistentDelimitedFileWriterTests : IAsyncLifetime
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "FileForge_Delimited_" + Guid.NewGuid().ToString("N"));

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_tempDirectory);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task InitializeAsync_WhenFileDoesNotExist_WritesHeader()
    {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "test.csv");
        await using var writer = new PersistentDelimitedFileWriter(testFile, "ColA,ColB");

        // Act
        await writer.InitializeAsync();
        await writer.DisposeAsync();

        // Assert
        var content = await File.ReadAllLinesAsync(testFile);
        content.Should().ContainSingle().Which.Should().Be("ColA,ColB");
    }

    [Fact]
    public async Task AppendLineAsync_WhenLinesAdded_AppendsCorrectly()
    {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "test_append.csv");
        await using var writer = new PersistentDelimitedFileWriter(testFile, "Header");
        await writer.InitializeAsync();

        // Act
        await writer.AppendLineAsync("Line1");
        await writer.AppendLineAsync("Line2");
        await writer.DisposeAsync();

        // Assert
        var content = await File.ReadAllLinesAsync(testFile);
        content.Should().Equal("Header", "Line1", "Line2");
    }

    [Fact]
    public async Task ReadExistingLinesAsync_WhenFileHasData_ReturnsOnlyDataLines()
    {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "test_read.csv");
        await File.WriteAllLinesAsync(testFile, ["Header", "Row1", "Row2"]);

        await using var writer = new PersistentDelimitedFileWriter(testFile, "Header");
        await writer.InitializeAsync();

        // Act
        var lines = await writer.ReadExistingLinesAsync();

        // Assert
        lines.Should().Equal("Row1", "Row2");
    }

    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("valeur,avec,virgule", "\"valeur,avec,virgule\"")]
    [InlineData("valeur \"avec\" guillemets", "\"valeur \"\"avec\"\" guillemets\"")]
    public void EscapeField_WhenCalled_EscapesAsExpected(string input, string expected)
    {
        // Act
        var result = PersistentDelimitedFileWriter.EscapeField(input);

        // Assert
        result.Should().Be(expected);
    }
}

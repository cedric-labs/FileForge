using System.Reflection;
using AwesomeAssertions;
using FileForge.IO;
using Xunit;

namespace FileForge.Tests;

public sealed class AssemblyVersionTests
{
    /// <summary>
    /// Les consommateurs compilés contre une version antérieure du package référencent sa version d'assembly :
    /// si elle retombait à Majeur.0.0.0 (comportement par défaut de MinVer), le runtime refuserait de la charger.
    /// </summary>
    [Fact]
    public void AssemblyVersion_WhenBuiltFromGitTag_MatchesThePackageVersion()
    {
        // Arrange
        var assembly = typeof(PersistentDelimitedFileWriter).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        informationalVersion.Should().NotBeNullOrWhiteSpace();
        var packageVersion = Version.Parse(informationalVersion!.Split('+', '-')[0]);

        // Act
        var assemblyVersion = assembly.GetName().Version;

        // Assert
        assemblyVersion.Should().Be(new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build, 0));
    }
}

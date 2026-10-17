using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace FileForge.Tests;

public class ArchitectureRulesTests
{
    private static readonly Assembly ProductionAssembly = typeof(FileForge.Paths.PathNormalizer).Assembly;

    [Fact]
    public void Architecture_ShouldNotContainClassesWithGenericCatchAllNames()
    {
        // Règle 3 : Interdiction de *Helper*, *Utils*, *Manager*, *Common*
        var types = Types.InAssembly(ProductionAssembly).GetTypes();

        var invalidTypes = types
            .Where(t => t.Name.Contains("Helper", StringComparison.OrdinalIgnoreCase) ||
                        t.Name.Contains("Utils", StringComparison.OrdinalIgnoreCase) ||
                        t.Name.Contains("Manager", StringComparison.OrdinalIgnoreCase) ||
                        t.Name.Contains("Common", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.FullName)
            .ToList();

        invalidTypes.Should().BeEmpty(
            because: "les classes ne doivent pas porter de noms fourre-tout (*Helper*, *Utils*, *Manager*, *Common*)");
    }

    [Fact]
    public void Architecture_ShouldNotContainNamespacesWithGenericCatchAllNames()
    {
        // Règle 3 : Interdiction de namespaces génériques
        var types = Types.InAssembly(ProductionAssembly).GetTypes();

        var invalidNamespaces = types
            .Select(t => t.Namespace ?? string.Empty)
            .Where(ns => ns.Contains("Helper", StringComparison.OrdinalIgnoreCase) ||
                         ns.Contains("Utils", StringComparison.OrdinalIgnoreCase) ||
                         ns.Contains("Manager", StringComparison.OrdinalIgnoreCase) ||
                         ns.Contains("Common", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();

        invalidNamespaces.Should().BeEmpty(
            because: "aucun namespace ne doit s'appeler Helper, Utils, Manager ou Common");
    }

    [Fact]
    public void SourceCode_ShouldNotContainWarningSuppressionDirectives()
    {
        // Règle 1 : Zéro masquage d'avertissement (#pragma warning disable, // NOSONAR, [SuppressMessage])
        var sourceFiles = GetProductionSourceFiles();

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);

            content.Should().NotContain("#pragma warning disable",
                because: $"le fichier {Path.GetFileName(file)} ne doit pas désactiver d'avertissements du compilateur");

            content.Should().NotContain("// NOSONAR",
                because: $"le fichier {Path.GetFileName(file)} ne doit pas contenir de directive // NOSONAR");

            content.Should().NotContain("[SuppressMessage",
                because: $"le fichier {Path.GetFileName(file)} ne doit pas supprimer de messages d'analyse");
        }
    }

    [Fact]
    public void SourceCode_ShouldNotContainSilentCatchBlocks()
    {
        // Règle 2 : Zéro bloc catch muet
        var sourceFiles = GetProductionSourceFiles();

        foreach (var file in sourceFiles)
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith("catch", StringComparison.Ordinal) && trimmed.EndsWith("{}", StringComparison.Ordinal))
                {
                    Assert.Fail($"Bloc catch muet détecté dans {Path.GetFileName(file)} à la ligne {i + 1} : '{trimmed}'");
                }
            }
        }
    }

    private static string[] GetProductionSourceFiles()
    {
        var testDir = Path.GetDirectoryName(typeof(ArchitectureRulesTests).Assembly.Location) ?? AppContext.BaseDirectory;
        var solutionDir = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", ".."));
        var productionDir = Path.Combine(solutionDir, "FileForge");

        if (!Directory.Exists(productionDir))
        {
            return [];
        }

        return Directory.GetFiles(productionDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .ToArray();
    }
}

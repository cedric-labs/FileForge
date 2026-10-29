using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AwesomeAssertions;
using NetArchTest.Rules;
using Xunit;

namespace FileForge.Tests;

public class ArchitectureRulesTests
{
    private static readonly Assembly ProductionAssembly = typeof(FileForge.Paths.PathNormalizer).Assembly;

    [Fact]
    public void ClassNames_WhenScanned_DoNotContainCatchAllSuffixes()
    {
        // Rule 3: no *Helper*, *Utils*, *Manager*, *Common* classes
        var types = Types.InAssembly(ProductionAssembly).GetTypes();

        var invalidTypes = types
            .Where(t => t.Name.Contains("Helper", StringComparison.OrdinalIgnoreCase) ||
                        t.Name.Contains("Utils", StringComparison.OrdinalIgnoreCase) ||
                        t.Name.Contains("Manager", StringComparison.OrdinalIgnoreCase) ||
                        t.Name.Contains("Common", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.FullName)
            .ToList();

        invalidTypes.Should().BeEmpty(
            because: "classes must not have catch-all names (*Helper*, *Utils*, *Manager*, *Common*)");
    }

    [Fact]
    public void Namespaces_WhenScanned_DoNotContainCatchAllSuffixes()
    {
        // Rule 3: no generic namespaces
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
            because: "no namespace may be named Helper, Utils, Manager or Common");
    }

    [Fact]
    public void SourceCode_WhenScanned_ContainsNoWarningSuppressionDirectives()
    {
        // Rule 1: no warning suppression (#pragma warning disable, // NOSONAR, [SuppressMessage])
        var sourceFiles = GetProductionSourceFiles();

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);

            content.Should().NotContain("#pragma warning disable",
                because: $"file {Path.GetFileName(file)} must not disable compiler warnings");

            content.Should().NotContain("// NOSONAR",
                because: $"file {Path.GetFileName(file)} must not contain a // NOSONAR directive");

            content.Should().NotContain("[SuppressMessage",
                because: $"file {Path.GetFileName(file)} must not suppress analyzer messages");
        }
    }

    [Fact]
    public void SourceCode_WhenScanned_ContainsNoSilentCatchBlocks()
    {
        // Rule 2: no silent catch block
        var sourceFiles = GetProductionSourceFiles();

        foreach (var file in sourceFiles)
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith("catch", StringComparison.Ordinal) && trimmed.EndsWith("{}", StringComparison.Ordinal))
                {
                    Assert.Fail($"Silent catch block found in {Path.GetFileName(file)} at line {i + 1}: '{trimmed}'");
                }
            }
        }
    }

    [Fact]
    public void SourceCode_WhenScanned_ContainsNoAiBuzzwords()
    {
        // Rule 6: no AI lexical markers
        string[] aiBuzzwords = ["Robust", "Resilient", "Seamless", "Smart", "Comprehensive", "Ultimate"];
        var types = Types.InAssembly(ProductionAssembly).GetTypes();

        foreach (var type in types)
        {
            foreach (var word in aiBuzzwords)
            {
                type.Name.Should().NotContain(word, because: $"type {type.Name} must not contain the AI lexical marker '{word}'");
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (var word in aiBuzzwords)
                {
                    method.Name.Should().NotContain(word, because: $"method {type.Name}.{method.Name} must not contain the AI lexical marker '{word}'");
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

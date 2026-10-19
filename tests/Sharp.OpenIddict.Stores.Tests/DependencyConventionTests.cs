using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Sharp.OpenIddict.Stores.Tests;

/// <summary>
/// Central package management constraints from design 4.1/4.1.1: versions are maintained only in the central
/// manifest, and product projects do not reference database drivers.
/// </summary>
public sealed class DependencyConventionTests
{
    [Fact]
    public void ProjectFiles_DoNotPinPackageVersions()
    {
        var offenders = new List<string>();

        foreach (var project in EnumerateProjectFiles())
        {
            var document = XDocument.Load(project);
            foreach (var reference in document.Descendants("PackageReference"))
            {
                if (reference.Attribute("Version") is not null || reference.Attribute("VersionOverride") is not null)
                {
                    offenders.Add($"{Relative(project)} -> {reference.Attribute("Include")?.Value}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Package versions must be centralized in Directory.Packages.props:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void ProductProjects_DoNotReferenceDatabaseDrivers()
    {
        var drivers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft.Data.Sqlite",
            "Microsoft.Data.SqlClient",
            "Npgsql",
            "MySqlConnector",
            "Oracle.ManagedDataAccess.Core",
            "DM.DmProvider",
            "Microsoft.EntityFrameworkCore",
        };

        var offenders = new List<string>();

        foreach (var project in EnumerateProjectFiles().Where(IsProductProject))
        {
            var document = XDocument.Load(project);
            foreach (var reference in document.Descendants("PackageReference"))
            {
                var id = reference.Attribute("Include")?.Value;
                if (id is not null && drivers.Contains(id))
                {
                    offenders.Add($"{Relative(project)} -> {id}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Database drivers may only be referenced by tests or samples:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void CentralPackageManagement_IsEnabledWithPinnedVersions()
    {
        var props = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Packages.props"));
        var versions = props.Descendants("PackageVersion").ToList();

        Assert.NotEmpty(versions);
        Assert.All(versions, v => Assert.False(string.IsNullOrWhiteSpace(v.Attribute("Version")?.Value)));

        var build = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));
        Assert.Contains(build.Descendants("ManagePackageVersionsCentrally"),
            element => element.Value.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CentralPackageManagement_UsesOnlyStableVersions()
    {
        var props = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Packages.props"));

        var prereleases = props.Descendants("PackageVersion")
            .Select(v => new { Id = v.Attribute("Include")?.Value, Version = v.Attribute("Version")?.Value })
            .Where(v => v.Version is not null && v.Version.Contains('-'))
            .Select(v => $"{v.Id} {v.Version}")
            .ToList();

        Assert.True(prereleases.Count == 0,
            "Prerelease packages must not be referenced:" + Environment.NewLine + string.Join(Environment.NewLine, prereleases));
    }

    [Fact]
    public void ProductProjects_TargetFiveFrameworks()
    {
        var expected = new[] { "netstandard2.0", "netstandard2.1", "net8.0", "net9.0", "net10.0" };
        var offenders = new List<string>();

        foreach (var project in EnumerateProjectFiles().Where(IsProductProject))
        {
            var document = XDocument.Load(project);
            var frameworks = document.Descendants("TargetFrameworks").FirstOrDefault()?.Value
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (frameworks is null || !frameworks.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
            {
                offenders.Add($"{Relative(project)} -> {(frameworks is null ? "missing TargetFrameworks" : string.Join(';', frameworks))}");
            }
        }

        Assert.True(offenders.Count == 0,
            "7.x product projects must target five frameworks (design 4.1.1):" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<string> EnumerateProjectFiles()
        => Directory.EnumerateFiles(RepositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static bool IsProductProject(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Relative(string path) => Path.GetRelativePath(RepositoryRoot, path);

    private static string RepositoryRoot { get; } = LocateRepositoryRoot();

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sharp.OpenIddict.Stores.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root (Sharp.OpenIddict.Stores.slnx).");
    }
/// <summary>
    /// Design 9.3: Dapper is an internal execution detail confined to the session type. Keeping the reference
    /// surface this small is what makes the dependency removable later as a mechanical change.
    /// </summary>
    [Fact]
    public void DapperIsConfinedToTheQueryExecutionLayer()
    {
        var offenders = new List<string>();
        var root = Path.Combine(RepositoryRoot, "src");

        foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The session is the one allowed place: it owns the command execution wrapper.
            if (Path.GetFileName(file).Equals("OpenIddictStoreSession.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);

            // Direct dependence on Dapper means importing its namespace, naming its types, or building its
            // command options. Calling our own session wrapper (which happens to share method names) is the
            // intended path and must not be flagged.
            // The check runs on line starts because "using Dapper;" also appears as a substring of a
            // longer namespace import, and only a real import or a qualified type name is a violation.
            var importsDapper = text
                .Split('\n')
                .Select(line => line.Trim())
                .Any(line => line.Equals("using Dapper;", StringComparison.Ordinal)
                             || line.StartsWith("using Dapper.", StringComparison.Ordinal)
                             || line.StartsWith("global using Dapper", StringComparison.Ordinal));

            if (importsDapper
                || text.Contains("Dapper.SqlMapper", StringComparison.Ordinal)
                || text.Contains("global::Dapper", StringComparison.Ordinal)
                || text.Contains("new CommandDefinition(", StringComparison.Ordinal))
            {
                offenders.Add(Relative(file));
            }
        }

        Assert.True(offenders.Count == 0,
            "Dapper must stay confined to OpenIddictStoreSession (design 9.3):" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }
}

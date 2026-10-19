using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Sharp.OpenIddict.Stores.IntegrationTests;

/// <summary>
/// Package asset contract (COMPAT-02, design 4.1): every shipped package must carry exactly the five
/// target frameworks, declare a per-TFM dependency group for the Microsoft.Extensions lines, and never
/// depend on a preview version. A package that quietly drops a TFM, or feeds a net10 dependency to a
/// netstandard asset, is a broken artifact even when the build succeeds.
/// </summary>
public sealed class PackageAssetTests
{
    private static readonly string[] TargetFrameworks =
        ["netstandard2.0", "netstandard2.1", "net8.0", "net9.0", "net10.0"];

    private static readonly string[] Products =
    [
        "Sharp.OpenIddict.Stores.Models",
        "Sharp.OpenIddict.Stores",
        "Sharp.OpenIddict.Stores.SqlServer",
        "Sharp.OpenIddict.Stores.PostgreSql",
        "Sharp.OpenIddict.Stores.MySql",
        "Sharp.OpenIddict.Stores.Sqlite",
        "Sharp.OpenIddict.Stores.Oracle",
        "Sharp.OpenIddict.Stores.Dm",
    ];

    [Fact]
    public void AllEightPackagesAreProduced()
    {
        foreach (var product in Products)
        {
            Assert.True(File.Exists(PackagePath(product)), $"The package for '{product}' was not produced.");
        }

        Assert.Equal(8, Directory.GetFiles(ArtifactsDirectory, "*.nupkg").Length);
    }

    [Theory]
    [MemberData(nameof(ProductNames))]
    public void PackageCarriesAllFiveTargetFrameworkAssets(string product)
    {
        using var archive = ZipFile.OpenRead(PackagePath(product));

        var frameworks = archive.Entries
            .Where(entry => entry.FullName.StartsWith("lib/", StringComparison.Ordinal))
            .Select(entry => entry.FullName.Substring(4).Split('/')[0])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var expected = TargetFrameworks.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, frameworks);

        // Each framework must contribute an assembly, not just an empty folder.
        foreach (var framework in TargetFrameworks)
        {
            Assert.Contains(archive.Entries,
                entry => entry.FullName.StartsWith($"lib/{framework}/", StringComparison.Ordinal)
                         && entry.FullName.EndsWith(".dll", StringComparison.Ordinal));
        }
    }

    [Theory]
    [MemberData(nameof(ProductNames))]
    public void PackageDeclaresPerTargetFrameworkDependencyGroups(string product)
    {
        var nuspec = ReadNuspec(product);

        var frameworks = nuspec.Descendants()
            .Where(element => element.Name.LocalName == "group")
            .Select(element => element.Attribute("targetFramework")?.Value)
            .Where(value => value is not null)
            .Select(value => NormalizeFramework(value!))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // The nuspec must expose a group per shipped framework, so a consumer resolves the right line.
        foreach (var framework in TargetFrameworks)
        {
            Assert.Contains(framework, frameworks);
        }
    }

    [Theory]
    [MemberData(nameof(ProductNames))]
    public void LowTargetFrameworksNeverReferenceHighGenerationMicrosoftExtensions(string product)
    {
        var nuspec = ReadNuspec(product);

        // Design 4.1.1: the netstandard/net8 line must resolve the 8.x versions, net9 the 9.x line, and
        // net10 the 10.x line. A net10 package flowing into a netstandard asset is the exact conflict the
        // per-TFM groups exist to prevent.
        var expectedMajorByFramework = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["netstandard2.0"] = 8,
            ["netstandard2.1"] = 8,
            ["net8.0"] = 8,
            ["net9.0"] = 9,
            ["net10.0"] = 10,
        };

        foreach (var group in nuspec.Descendants().Where(element => element.Name.LocalName == "group"))
        {
            var raw = group.Attribute("targetFramework")?.Value;

            if (raw is null)
            {
                continue;
            }

            var framework = NormalizeFramework(raw);

            if (!expectedMajorByFramework.TryGetValue(framework, out var expectedMajor))
            {
                continue;
            }

            foreach (var dependency in group.Elements().Where(element => element.Name.LocalName == "dependency"))
            {
                var id = dependency.Attribute("id")?.Value ?? string.Empty;
                var version = dependency.Attribute("version")?.Value ?? string.Empty;

                if (!id.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
                    && !id.StartsWith("System.Security.Cryptography", StringComparison.Ordinal))
                {
                    continue;
                }

                var major = ExtractMajor(version);

                Assert.True(major is not null,
                    $"{product} declares an unparseable version '{version}' for '{id}' under {framework}.");

                Assert.True(major <= expectedMajor,
                    $"{product} declares '{id} {version}' under {framework}, but that framework must stay on " +
                    $"the {expectedMajor}.x line: a higher generation causes the dependency conflict the " +
                    "per-TFM groups exist to prevent.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(ProductNames))]
    public void NoPreviewOrPrereleaseDependency(string product)
    {
        var nuspec = ReadNuspec(product);

        foreach (var dependency in nuspec.Descendants().Where(element => element.Name.LocalName == "dependency"))
        {
            var id = dependency.Attribute("id")?.Value ?? string.Empty;
            var version = dependency.Attribute("version")?.Value ?? string.Empty;

            Assert.False(version.Contains('-', StringComparison.Ordinal)
                         && !version.StartsWith("[", StringComparison.Ordinal),
                $"{product} depends on the prerelease version '{version}' of '{id}'. Only stable releases are allowed.");

            Assert.DoesNotContain("preview", version, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("rc", id, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [MemberData(nameof(ProductNames))]
    public void PackageHasNoProjectLevelVersionOverride(string product)
    {
        // Central package management is the single version source (design 4.1). A nuspec dependency that
        // disagrees with Directory.Packages.props would mean a project sneaked in its own Version.
        var nuspec = ReadNuspec(product);
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var central = ReadCentralVersions();

        foreach (var dependency in nuspec.Descendants().Where(element => element.Name.LocalName == "dependency"))
        {
            var id = dependency.Attribute("id")?.Value;
            var version = dependency.Attribute("version")?.Value;

            if (id is null || version is null || !central.TryGetValue(id, out var candidates))
            {
                continue;
            }

            // The declared version must be one the centre actually pins; any other number means a project
            // bypassed central package management with its own Version attribute.
            Assert.True(candidates.Any(candidate => version.StartsWith(candidate, StringComparison.Ordinal)),
                $"{product} depends on '{id} {version}', which is not one of the centrally pinned versions " +
                $"({string.Join(", ", candidates)}).");
        }
    }

    [Fact]
    public void EveryPackageUsesTheSameVersion()
    {
        var versions = Directory.GetFiles(ArtifactsDirectory, "*.nupkg")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Select(name => Products
                .Select(product => (Product: product, Prefix: product + "."))
                .Where(candidate => name.StartsWith(candidate.Prefix, StringComparison.Ordinal))
                .OrderByDescending(candidate => candidate.Prefix.Length)
                .Select(candidate => name.Substring(candidate.Prefix.Length))
                .FirstOrDefault())
            .Where(version => version is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Single(versions);
        Assert.Equal("7.0.0", versions[0]);
    }

    [Fact]
    public void SqlAssetsAreCarriedOnlyByTheProvidersThatOwnThem()
    {
        // The core package ships no SQL; the SQLite package ships its own scripts (design 10.1).
        Assert.DoesNotContain(
            ListEntries("Sharp.OpenIddict.Stores"), name => name.StartsWith("sql/", StringComparison.Ordinal));

        var sqlite = ListEntries("Sharp.OpenIddict.Stores.Sqlite");

        Assert.Contains("sql/7/install/v0001.sql.template", sqlite);
        Assert.Contains("sql/manifest.json", sqlite);
        Assert.Contains("sql/README.md", sqlite);
    }

    public static IEnumerable<object[]> ProductNames()
        => Products.Select(product => new object[] { product });

    private static IEnumerable<string> ListEntries(string product)
    {
        using var archive = ZipFile.OpenRead(PackagePath(product));

        return archive.Entries.Select(entry => entry.FullName).ToArray();
    }

    private static XDocument ReadNuspec(string product)
    {
        using var archive = ZipFile.OpenRead(PackagePath(product));
        var entry = archive.Entries.Single(item =>
            item.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));

        using var stream = entry.Open();

        return XDocument.Load(stream);
    }

    private static Dictionary<string, List<string>> ReadCentralVersions()
    {
        var path = Path.Combine(RepositoryRoot, "Directory.Packages.props");
        var document = XDocument.Load(path);

        // A package id can legitimately appear once per TFM line in central package management (design
        // 4.1.1), so every declared version is collected rather than only the last one.
        var versions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in document.Descendants()
                     .Where(item => item.Name.LocalName == "PackageVersion")
                     .Where(item => item.Attribute("Include") is not null))
        {
            var id = element.Attribute("Include")!.Value;
            var version = element.Attribute("Version")?.Value;

            if (version is null)
            {
                continue;
            }

            if (!versions.TryGetValue(id, out var list))
            {
                versions[id] = list = [];
            }

            list.Add(version);
        }

        return versions;
    }

    private static string NormalizeFramework(string targetFramework)
    {
        // nuspec writes identifiers like ".NETStandard2.0" or ".NETCoreApp8.0"; map them back to the TFM.
        var value = targetFramework.Replace(".", string.Empty, StringComparison.Ordinal);

        // The trailing digits are "20" for 2.0 and "100" for 10.0, so the dot goes before the last digit.
        if (value.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
        {
            var version = value.Substring("netstandard".Length);
            return "netstandard" + version.Insert(version.Length - 1, ".");
        }

        if (value.StartsWith("NETCoreApp", StringComparison.OrdinalIgnoreCase))
        {
            var version = value.Substring("NETCoreApp".Length);
            return "net" + version.Insert(version.Length - 1, ".");
        }

        // Some writers use the TFM directly.
        return targetFramework.ToLowerInvariant();
    }

    private static int? ExtractMajor(string version)
    {
        var trimmed = version.TrimStart('[', '(', ' ');

        var digits = new string(trimmed.TakeWhile(character => char.IsDigit(character)).ToArray());

        return digits.Length > 0 && int.TryParse(digits, out var major) ? major : null;
    }

    private static string PackagePath(string product)
        => Path.Combine(ArtifactsDirectory, product + ".7.0.0.nupkg");

    // Declared before its consumers: static field initializers run in textual order.
    private static string RepositoryRoot { get; } = LocateRepositoryRoot();

    private static string ArtifactsDirectory { get; } = Path.Combine(RepositoryRoot, "artifacts");

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sharp.OpenIddict.Stores.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root (Sharp.OpenIddict.Stores.slnx) was not found.");
    }
}

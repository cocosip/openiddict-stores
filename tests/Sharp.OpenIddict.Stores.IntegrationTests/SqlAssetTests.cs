using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Sharp.OpenIddict.Stores.IntegrationTests;

/// <summary>
/// Script asset checks from design 10.1/10.4: files referenced by the manifest exist, digests match, and
/// encoding and line endings follow the convention. These checks target repository sources and do not replace
/// real-database installation verification.
/// </summary>
public sealed class SqlAssetTests
{
    public static TheoryData<string> Providers =>
        ["SqlServer", "PostgreSql", "MySql", "Sqlite", "Oracle", "Dm"];

    [Fact]
    public void Sqlite_IsTheReferenceProviderWithAFullManifest()
    {
        var root = Path.Combine(RepositoryRoot, "src", "Sharp.OpenIddict.Stores.Sqlite", "Sql");

        Assert.True(File.Exists(Path.Combine(root, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(root, "README.md")));
        Assert.True(File.Exists(Path.Combine(root, "1", "install", "v0001.sql.template")));
    }

    /// <summary>
    /// Scripts are shipped as templates: the table prefix and schema are runtime configuration, so a file
    /// with concrete identifiers would be wrong for every non-default deployment (design 10.1).
    /// </summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public void ShippedScripts_AreTemplatesNotRunnableSql(string provider)
    {
        var root = Path.Combine(RepositoryRoot, "src", $"Sharp.OpenIddict.Stores.{provider}", "Sql");

        if (!Directory.Exists(root))
        {
            return;
        }

        // A .sql file would be mistaken for something a deployment can execute directly.
        var rendered = Directory.EnumerateFiles(root, "*.sql", SearchOption.AllDirectories).ToList();

        Assert.True(rendered.Count == 0,
            $"{provider} ships runnable SQL instead of templates: " + string.Join(", ", rendered));
    }

    /// <summary>Every template must declare which placeholders a deployment has to render.</summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public void Manifest_DeclaresTheTokensEachTemplateRenders(string provider)
    {
        var root = Path.Combine(RepositoryRoot, "src", $"Sharp.OpenIddict.Stores.{provider}", "Sql");
        var manifestPath = Path.Combine(root, "manifest.json");

        if (!File.Exists(manifestPath))
        {
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));

        foreach (var entry in document.RootElement.GetProperty("entries").EnumerateArray())
        {
            var tokens = entry.GetProperty("rendersTokens").EnumerateArray()
                .Select(token => token.GetString()!)
                .ToHashSet(StringComparer.Ordinal);

            Assert.Contains("__PREFIX__", tokens);

            var path = Path.Combine(root,
                entry.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            var text = File.ReadAllText(path);

            // The declaration and the file have to agree, otherwise a deployment misses a placeholder.
            Assert.Equal(tokens.OrderBy(token => token, StringComparer.Ordinal),
                TokensIn(text).OrderBy(token => token, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// A template's header is what a DBA reads before running it, so it must state the same series the file
    /// actually installs. Three headers kept the pre-renumbering "7" long after the directory, the seed
    /// record, and the manifest had moved to series 1 — a stale comment on a script is a wrong instruction.
    /// </summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public void TemplateHeader_StatesTheSeriesTheScriptInstalls(string provider)
    {
        var root = Path.Combine(RepositoryRoot, "src", $"Sharp.OpenIddict.Stores.{provider}", "Sql");

        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var file in EnumerateScripts(root))
        {
            var header = File.ReadLines(file).First(line => line.StartsWith("--", StringComparison.Ordinal));

            // Only the product banner carries a series number; MySQL's banner names its servers instead.
            var banner = Regex.Match(header, @"\s(\d+)\s+/\s+v\d{4}\b");

            if (!banner.Success)
            {
                continue;
            }

            Assert.True(banner.Groups[1].Value == OpenIddictStoreSchema.RequiredSeries,
                $"{Path.GetFileName(file)} is headed with series {banner.Groups[1].Value} but installs " +
                $"series {OpenIddictStoreSchema.RequiredSeries}: '{header.Trim()}'.");
        }
    }

    /// <summary>
    /// A header that names a placeholder literally is telling the operator to render it. That claim has to be
    /// true of the body: SQLite's header demanded <c>__SCHEMA__</c> for a provider that has no schemas, and
    /// rendering it was impossible. Headers that describe a placeholder in prose are unaffected.
    /// </summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public void TemplateHeader_DoesNotDemandAPlaceholderTheBodyLacks(string provider)
    {
        var root = Path.Combine(RepositoryRoot, "src", $"Sharp.OpenIddict.Stores.{provider}", "Sql");

        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var file in EnumerateScripts(root))
        {
            var lines = File.ReadAllLines(file);
            var header = string.Join("\n", lines.Where(line => line.TrimStart().StartsWith("--", StringComparison.Ordinal)));
            var body = string.Join("\n", lines.Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));

            foreach (var token in new[] { "__PREFIX__", "__SCHEMA__" })
            {
                if (header.Contains(token, StringComparison.Ordinal))
                {
                    Assert.True(body.Contains(token, StringComparison.Ordinal),
                        $"{Path.GetFileName(file)}: the header names {token} but the body never uses it.");
                }
            }
        }
    }

    private static IEnumerable<string> TokensIn(string text)
    {
        // Comments are documentation, not placeholders that a deployment has to fill in.
        var script = string.Join("\n", text.Split('\n')
            .Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));

        foreach (var token in new[] { "__PREFIX__", "__SCHEMA__" })
        {
            if (script.Contains(token, StringComparison.Ordinal))
            {
                yield return token;
            }
        }
    }

    /// <summary>
    /// The runtime refuses to operate unless the installed version matches, so the constant it compares
    /// against and the version the shipped manifest targets must not drift apart (design 10.2, 17).
    /// </summary>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    [InlineData("Oracle")]
    [InlineData("Dm")]
    public void Manifest_TargetsTheVersionTheRuntimeRequires(string provider)
    {
        var manifestPath = Path.Combine(RepositoryRoot, "src", $"Sharp.OpenIddict.Stores.{provider}", "Sql", "manifest.json");

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));

        Assert.Equal(OpenIddictStoreSchema.RequiredSeries, document.RootElement.GetProperty("series").GetString());

        // One provider can ship several entries: MySQL and MariaDB share a file but are listed and accepted
        // separately (design 10.1), so every install entry has to target the version the runtime requires.
        var installs = document.RootElement.GetProperty("entries").EnumerateArray()
            .Where(entry => entry.GetProperty("type").GetString() == "install")
            .ToList();

        Assert.NotEmpty(installs);

        foreach (var install in installs)
        {
            Assert.Equal(OpenIddictStoreSchema.RequiredSeries, install.GetProperty("targetSeries").GetString());
            Assert.Equal(OpenIddictStoreSchema.RequiredVersion, install.GetProperty("targetVersion").GetInt32());
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void Manifest_EntriesMatchFilesAndDigests(string provider)
    {
        var root = Path.Combine(RepositoryRoot, "src", $"Sharp.OpenIddict.Stores.{provider}", "Sql");
        var manifestPath = Path.Combine(root, "manifest.json");

        // Providers that have not started delivering scripts are skipped; once scripts exist there must be a complete manifest.
        if (!File.Exists(manifestPath))
        {
            Assert.False(Directory.Exists(root) && Directory.EnumerateFiles(root, "*.sql", SearchOption.AllDirectories).Any(),
                $"{provider} already has SQL but no manifest.json.");

            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToList();

        Assert.NotEmpty(entries);

        foreach (var entry in entries)
        {
            var relative = entry.GetProperty("path").GetString()!;
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(path), $"{provider} manifest references a file that does not exist: {relative}");

            var expected = entry.GetProperty("sha256").GetString();
            var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

            Assert.Equal(expected, actual);

            var type = entry.GetProperty("type").GetString();
            var sourceVersion = entry.GetProperty("sourceVersion");

            if (type == "install")
            {
                Assert.Equal(JsonValueKind.Null, sourceVersion.ValueKind);
            }
            else
            {
                Assert.Equal(JsonValueKind.Number, sourceVersion.ValueKind);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void Scripts_AreUtf8WithoutBomAndUseLf(string provider)
    {
        var root = Path.Combine(RepositoryRoot, "src", $"Sharp.OpenIddict.Stores.{provider}", "Sql");

        if (!Directory.Exists(root))
        {
            return;
        }

        var offenders = new List<string>();

        foreach (var file in EnumerateScripts(root))
        {
            var bytes = File.ReadAllBytes(file);

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                offenders.Add($"{Path.GetFileName(file)}: contains a BOM");
            }

            if (System.Text.Encoding.UTF8.GetString(bytes).Contains("\r\n", StringComparison.Ordinal))
            {
                offenders.Add($"{Path.GetFileName(file)}: uses CRLF");
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void EverySqlFile_IsReferencedByTheManifest(string provider)
    {
        var root = Path.Combine(RepositoryRoot, "src", $"Sharp.OpenIddict.Stores.{provider}", "Sql");
        var manifestPath = Path.Combine(root, "manifest.json");

        if (!File.Exists(manifestPath))
        {
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var referenced = document.RootElement.GetProperty("entries").EnumerateArray()
            .Select(entry => entry.GetProperty("path").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        var actual = EnumerateScripts(root)
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .ToList();

        var unreferenced = actual.Where(path => !referenced.Contains(path)).ToList();

        Assert.True(unreferenced.Count == 0,
            $"{provider} has scripts that are not registered in the manifest: " + string.Join(", ", unreferenced));
    }

    private static IEnumerable<string> EnumerateScripts(string root)
        => Directory.EnumerateFiles(root, "*.sql.template", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(root, "*.sql", SearchOption.AllDirectories));

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
}

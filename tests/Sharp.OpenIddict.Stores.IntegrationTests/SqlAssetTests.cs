using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
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
        Assert.True(File.Exists(Path.Combine(root, "7", "install", "v0001.sql")));
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

        foreach (var file in Directory.EnumerateFiles(root, "*.sql", SearchOption.AllDirectories))
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

        var actual = Directory.EnumerateFiles(root, "*.sql", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .ToList();

        var unreferenced = actual.Where(path => !referenced.Contains(path)).ToList();

        Assert.True(unreferenced.Count == 0,
            $"{provider} has scripts that are not registered in the manifest: " + string.Join(", ", unreferenced));
    }

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

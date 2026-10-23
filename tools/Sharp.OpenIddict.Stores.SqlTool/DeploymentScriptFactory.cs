using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Dm;
using Sharp.OpenIddict.Stores.MySql;
using Sharp.OpenIddict.Stores.Oracle;
using Sharp.OpenIddict.Stores.PostgreSql;
using Sharp.OpenIddict.Stores.Sqlite;
using Sharp.OpenIddict.Stores.SqlServer;
using System.Text.Json;

namespace Sharp.OpenIddict.Stores.SqlTool;

/// <summary>
/// Renders the deployment SQL of a provider from the templates the packages ship: it selects the manifest
/// entry (for Dameng, the entry matching the compatibility mode), applies the configured table prefix and
/// schema with the same validation the runtime uses, and returns the executable copy. The rendered output
/// always contains the seed record the runtime's version guard requires (design 10.1, 10.3, 17).
/// </summary>
/// <remarks>
/// This tool never talks to a database. It produces SQL; executing it, in an order that honours the
/// transaction boundaries recorded in the provider's <c>Sql/README.md</c>, stays with the deployment.
/// </remarks>
public static class DeploymentScriptFactory
{
    /// <summary>One renderable manifest entry of a provider.</summary>
    /// <remarks>
    /// <c>Dialect</c> is per entry, not per provider: Dameng ships one dialect per compatibility mode, so a
    /// provider-wide dialect would misreport two of its three entries (design 5, 10.1).
    /// </remarks>
    public sealed record ScriptEntry(
        string Provider,
        string Server,
        string? MinimumVersion,
        string Mode,
        string Path,
        string Sha256,
        IReadOnlyList<string> RendersTokens,
        IOpenIddictStoreDialect Dialect);

    /// <summary>One provider and the entries it ships.</summary>
    public sealed record ProviderInfo(string Name, IReadOnlyList<ScriptEntry> Entries);

    private static readonly (string Name, IOpenIddictStoreDialect Dialect)[] Providers =
    [
        ("SqlServer", OpenIddictStoreSqlServerDialect.Instance),
        ("PostgreSql", OpenIddictStorePostgreSqlDialect.Instance),
        ("MySql", OpenIddictStoreMySqlDialect.Instance),
        ("Sqlite", OpenIddictStoreSqliteDialect.Instance),
        ("Oracle", OpenIddictStoreOracleDialect.Instance),
        ("Dm", OpenIddictStoreDmDialect.Instance),
    ];

    private const string ResourcePrefix = "Sharp.OpenIddict.Stores.SqlTool.Sql.";
    private const string DefaultMode = "native";

    /// <summary>Lists every provider with its manifest entries, read from the embedded assets.</summary>
    public static IReadOnlyList<ProviderInfo> ListProviders()
        => Providers.Select(provider => new ProviderInfo(
            provider.Name,
            ReadManifest(provider.Name).Entries)).ToArray();

    /// <summary>
    /// Renders the deployment script for one provider entry. <paramref name="mode"/> selects the Dameng
    /// compatibility mode (<c>native</c>, <c>oracle-compatible</c>, <c>mysql-compatible</c>; default
    /// <c>native</c>) and must stay null for every other provider. <paramref name="server"/> disambiguates
    /// when a provider ships several entries that render different files; a prefix substring of the
    /// <c>server</c> field is enough. Entries that share one file (MySQL and MariaDB) need no disambiguation.
    /// </summary>
    public static string Render(
        string provider,
        string? mode = null,
        string prefix = OpenIddictStoreOptions.DefaultTablePrefix,
        string? schema = null,
        string? server = null)
    {
        // Validates the provider/mode pair before any manifest work, so an unknown mode fails with its own message.
        DialectFor(provider, mode);

        var manifest = ReadManifest(provider);
        var modeName = provider == "Dm" ? (mode ?? DefaultMode) : null;

        var candidates = manifest.Entries
            .Where(entry => modeName is null || string.Equals(entry.Mode, modeName, StringComparison.Ordinal))
            .ToList();

        if (!string.IsNullOrEmpty(server))
        {
            candidates = candidates
                .Where(entry => entry.Server.Contains(server, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var distinct = candidates
            .GroupBy(entry => (entry.Path, entry.Sha256))
            .ToList();

        if (distinct.Count == 0)
        {
            throw new InvalidOperationException(
                $"Provider '{provider}' has no manifest entry matching the request"
                + (server is null ? string.Empty : $" for server '{server}'") + ". Declared servers: "
                + string.Join(", ", manifest.Entries.Select(entry => entry.Server)) + ".");
        }

        if (distinct.Count > 1)
        {
            throw new InvalidOperationException(
                $"Provider '{provider}' has several candidate scripts; pass --server to choose one. "
                + "Declared servers: " + string.Join(", ", candidates.Select(entry => entry.Server)) + ".");
        }

        var entry = distinct[0].First();

        // The runtime rejects a dedicated schema for these providers (OpenIddictStoreOptionsValidator);
        // the tool must not quietly accept it and produce a script with no schema qualifier.
        if (!string.IsNullOrEmpty(schema) && !entry.RendersTokens.Contains("__SCHEMA__"))
        {
            throw new ArgumentException(
                $"Provider '{provider}' does not support a dedicated schema: its script has no schema placeholder. " +
                "Remove --schema, or configure a provider that supports one.");
        }

        var template = ReadResource(provider, entry.Path);
        var rendered = OpenIddictStoreIdentifiers.RenderDeploymentScript(template, entry.Dialect, prefix, schema);

        AssertNoLeftoverTokens(rendered, provider, entry.Path);

        return rendered;
    }

    private static IOpenIddictStoreDialect DialectFor(string provider, string? mode)
    {
        var known = Providers.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, provider, StringComparison.OrdinalIgnoreCase));

        if (known.Dialect is null)
        {
            throw new ArgumentException(
                $"Unknown provider '{provider}'. Known providers: " +
                string.Join(", ", Providers.Select(candidate => candidate.Name)) + ".");
        }

        if (mode is not null && !string.Equals(provider, "Dm", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Only the Dm provider has compatibility modes; '{provider}' does not accept --mode.");
        }

        return provider.Equals("Dm", StringComparison.OrdinalIgnoreCase) ? mode switch
        {
            null or "native" => OpenIddictStoreDmDialect.Instance,
            "oracle-compatible" => OpenIddictStoreDmOracleCompatibleDialect.Instance,
            "mysql-compatible" => OpenIddictStoreDmMySqlCompatibleDialect.Instance,
            _ => throw new ArgumentException(
                $"Unknown DM8 compatibility mode '{mode}'. Known modes: native, oracle-compatible, mysql-compatible."),
        } : known.Dialect;
    }

    private sealed record ManifestData(IReadOnlyList<ScriptEntry> Entries);

    private static ManifestData ReadManifest(string provider)
    {
        using var document = JsonDocument.Parse(ReadResource(provider, "manifest.json"));
        var entries = new List<ScriptEntry>();

        foreach (var element in document.RootElement.GetProperty("entries").EnumerateArray())
        {
            // This tool renders the install script only. An upgrade entry describes a source-to-target
            // transition and must never be handed to a deployment as the install copy.
            if (element.GetProperty("type").GetString() != "install")
            {
                continue;
            }

            var path = element.GetProperty("path").GetString()!;

            entries.Add(new ScriptEntry(
                provider,
                element.GetProperty("server").GetString() ?? string.Empty,
                element.TryGetProperty("minimumVersion", out var minimum) ? minimum.GetString() : null,
                ModeFromPath(path),
                path,
                element.GetProperty("sha256").GetString() ?? string.Empty,
                element.GetProperty("rendersTokens").EnumerateArray()
                    .Select(token => token.GetString() ?? string.Empty)
                    .ToArray(),
                // Only Dameng has compatibility modes; every other provider's entry renders with its one dialect.
                DialectFor(provider, string.Equals(provider, "Dm", StringComparison.OrdinalIgnoreCase)
                    ? ModeFromPath(path)
                    : null)));
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"Provider '{provider}' ships a manifest without entries.");
        }

        return new ManifestData(entries);
    }

    /// <summary>The compatibility mode lives in the DM layout as the directory under <c>install</c>.</summary>
    private static string ModeFromPath(string path)
    {
        var segments = path.Split('/');

        return segments.Length >= 3 && segments[0] == "1" && segments[1] == "install" && segments.Length == 4
            ? segments[2]
            : DefaultMode;
    }

    private static string ReadResource(string provider, string relativePath)
    {
        // The compiler rewrites embedded resource names into valid identifiers per path segment: "1" becomes
        // "_7", "oracle-compatible" becomes "oracle_compatible". Reconstructing that algorithm would couple
        // the tool to its details, so both sides are compared in a canonical form that keeps only the
        // alphanumeric content, which is stable across those rewrites.
        static string Canonical(string name)
            => new(name.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

        var name = ResourcePrefix + provider + "." + relativePath.Replace('/', '.');
        var assembly = typeof(DeploymentScriptFactory).Assembly;
        var resourceName = assembly.GetManifestResourceNames().SingleOrDefault(candidate =>
            string.Equals(Canonical(candidate), Canonical(name), StringComparison.Ordinal));

        if (resourceName is null)
        {
            throw new InvalidOperationException($"Embedded SQL asset '{name}' was not found; the tool build is incomplete.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded SQL asset '{name}' could not be opened.");

        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    private static void AssertNoLeftoverTokens(string rendered, string provider, string path)
    {
        foreach (var token in new[] { "__PREFIX__", "__SCHEMA__" })
        {
            if (rendered.Contains(token, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Rendering '{provider}'/{path} left the placeholder '{token}' in the output.");
            }
        }
    }
}

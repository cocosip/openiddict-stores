using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// The one place a real-server run is configured. A single <c>databases.json</c> next to this project names
/// the provider to run and carries the connection strings, so switching databases is an edit to one value
/// instead of a different command line or a different environment.
/// </summary>
/// <remarks>
/// The file never enters the repository: <c>databases.json.template</c> is the committed shape and
/// <c>databases.json</c> is ignored, so a connection string cannot be committed by accident. With no file at
/// all every server-backed fact reports itself as skipped with the reason, and SQLite still runs because it
/// needs no server. <c>provider</c> is either <c>all</c> (the default) or one key from
/// <see cref="ConnectionStringKeys"/>.
/// </remarks>
internal static class AcceptanceServers
{
    /// <summary>The configuration file name, read from the project directory so editing it needs no rebuild.</summary>
    public const string FileName = "databases.json";

    /// <summary>Runs every configured server.</summary>
    public const string All = "all";

    /// <summary>SQLite: no server, so no connection string is ever read for it.</summary>
    public const string Sqlite = "Sqlite";

    /// <summary>SQL Server. The string is a master-database connection whose account may create databases.</summary>
    public const string SqlServer = "SqlServer";

    /// <summary>PostgreSQL.</summary>
    public const string PostgreSql = "PostgreSql";

    /// <summary>MySQL 8.0.</summary>
    public const string MySql = "MySql";

    /// <summary>MariaDB 10.6.</summary>
    public const string MariaDb = "MariaDb";

    /// <summary>Oracle (SID targets use the full descriptor).</summary>
    public const string Oracle = "Oracle";

    /// <summary>DM8 native mode (<c>COMPATIBLE_MODE=0</c>).</summary>
    public const string DmNative = "DmNative";

    /// <summary>DM8 Oracle-compatible mode (<c>COMPATIBLE_MODE=2</c>).</summary>
    public const string DmOracle = "DmOracle";

    /// <summary>DM8 MySQL-compatible mode (<c>COMPATIBLE_MODE=4</c>).</summary>
    public const string DmMySql = "DmMySql";

    /// <summary>Every key a connection string can be given for, in the order a report reads best.</summary>
    public static IReadOnlyList<string> ConnectionStringKeys { get; } = new[]
    {
        SqlServer, PostgreSql, MySql, MariaDb, Oracle, DmNative, DmOracle, DmMySql,
    };

    private static readonly Lazy<Configuration> Loaded = new(Load);

    /// <summary>The provider selection: <c>all</c>, or the one key this run is limited to.</summary>
    public static string Provider => Loaded.Value.Provider;

    /// <summary>
    /// Whether the named suite has what it needs: SQLite always does, a server suite needs its connection
    /// string, and any suite also has to be selected by <see cref="Provider"/>.
    /// </summary>
    public static bool IsConfigured(string key) => Unavailable(key) is null;

    /// <summary>The configured connection string, or the empty string when the entry is absent.</summary>
    public static string ConnectionString(string key)
        => Loaded.Value.ConnectionStrings.TryGetValue(key, out var value) ? value : string.Empty;

    /// <summary>The schema the DM suites install into, or the empty string for the connection's default.</summary>
    public static string Schema(string key)
        => Loaded.Value.Schemas.TryGetValue(key, out var value) ? value : string.Empty;

    /// <summary>
    /// Why the named suite cannot run, or null when it can. The three cases are deliberately distinct: "not
    /// selected", "selected but no connection string", and "no configuration file at all" are three different
    /// fixes, and a single "skipped" would hide which one applies.
    /// </summary>
    public static string? Unavailable(string key)
    {
        if (!IsSelected(key))
        {
            return $"is not selected: '{FileName}' sets provider to '{Provider}', not '{key}'.";
        }

        if (key == Sqlite)
        {
            return null;
        }

        if (ConnectionString(key).Length > 0)
        {
            return null;
        }

        return Loaded.Value.FileExists
            ? $"needs the connectionStrings.{key} entry in '{FileName}'."
            : $"needs '{FileName}' (copy '{FileName}.template' and fill in connectionStrings.{key}).";
    }

    /// <summary>Whether the selection covers the named suite.</summary>
    public static bool IsSelected(string key)
        => Provider == All || string.Equals(Provider, key, StringComparison.OrdinalIgnoreCase);

    private static Configuration Load()
    {
        var path = Path.Combine(RepositoryLayout.Root, "tests", "Sharp.OpenIddict.Stores.ServerAcceptanceTests", FileName);

        if (!File.Exists(path))
        {
            return new Configuration(false, All, Empty, Empty);
        }

        // Comments are allowed because the committed shape is a documented template: copying it and filling in
        // the values has to work without first deleting the explanations.
        using var document = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        var root = document.RootElement;

        var provider = root.TryGetProperty("provider", out var providerElement)
            && providerElement.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(providerElement.GetString())
                ? providerElement.GetString()!
                : All;

        return new Configuration(
            true,
            provider,
            ReadStringMap(root, "connectionStrings"),
            ReadStringMap(root, "schemas"));
    }

    private static Dictionary<string, string> ReadStringMap(JsonElement root, string property)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!root.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return values;
        }

        foreach (var entry in element.EnumerateObject())
        {
            var value = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : null;

            if (!string.IsNullOrWhiteSpace(value))
            {
                values[entry.Name] = value!;
            }
        }

        return values;
    }

    private static readonly Dictionary<string, string> Empty = new(StringComparer.OrdinalIgnoreCase);

    private sealed record Configuration(
        bool FileExists,
        string Provider,
        IReadOnlyDictionary<string, string> ConnectionStrings,
        IReadOnlyDictionary<string, string> Schemas);
}

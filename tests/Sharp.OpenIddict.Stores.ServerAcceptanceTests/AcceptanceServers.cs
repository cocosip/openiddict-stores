using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// The connection strings of the real-server acceptance runs, read from the dotnet user-secrets store,
/// which lives in the user profile and never inside the repository
/// (<c>dotnet user-secrets --id OpenIddictStoresServerAcceptance</c>). Deliberately in-box: locating and
/// parsing the secrets file needs nothing beyond the base library, so no secret can end up in a repository
/// file, a CI variable, or a package.
/// </summary>
internal static class AcceptanceServers
{
    /// <summary>The user-secrets id this harness resolves; the same id is pinned in the project file.</summary>
    public const string SecretsId = "OpenIddictStoresServerAcceptance";

    /// <summary>The secret holding the PostgreSQL connection string.</summary>
    public const string PostgreSql = "PostgreSql";

    /// <summary>The secret holding the MySQL 8.0 connection string.</summary>
    public const string MySql = "MySql";

    /// <summary>The secret holding the MariaDB 10.6 connection string.</summary>
    public const string MariaDb = "MariaDb";

    /// <summary>The secret holding the Oracle connection string (SID targets use the full descriptor).</summary>
    public const string Oracle = "Oracle";

    /// <summary>The secret holding the DM8 native-mode connection string.</summary>
    public const string DmNative = "DmNative";

    /// <summary>The secret holding the schema the DM8 native-mode tables are installed into.</summary>
    public const string DmNativeSchema = "DmNativeSchema";

    /// <summary>The secret holding the DM8 Oracle-compatible-mode connection string.</summary>
    public const string DmOracle = "DmOracle";

    /// <summary>The secret holding the schema the DM8 Oracle-compatible tables are installed into.</summary>
    public const string DmOracleSchema = "DmOracleSchema";

    /// <summary>The secret holding the DM8 MySQL-compatible-mode connection string.</summary>
    public const string DmMySql = "DmMySql";

    /// <summary>The secret holding the schema the DM8 MySQL-compatible tables are installed into.</summary>
    public const string DmMySqlSchema = "DmMySqlSchema";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Secrets = new(LoadSecrets);

    /// <summary>Whether the named secret exists and is non-empty; drives the skip decision of the facts.</summary>
    public static bool IsConfigured(string key)
        => Secrets.Value.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value);

    /// <summary>The configured connection string, or the empty string when the secret is absent.</summary>
    public static string ConnectionString(string key)
        => Secrets.Value.TryGetValue(key, out var value) ? value : string.Empty;

    private static IReadOnlyDictionary<string, string> LoadSecrets()
    {
        var path = CandidateSecretsPaths().FirstOrDefault(File.Exists);

        if (path is null)
        {
            return new Dictionary<string, string>();
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(
                property => property.Name,
                property => property.Value.GetString() ?? string.Empty,
                StringComparer.Ordinal);
    }

    /// <summary>
    /// The secrets locations the dotnet CLI uses: <c>%APPDATA%\Microsoft\UserSecrets</c> on Windows and
    /// <c>~/.microsoft/usersecrets</c> elsewhere.
    /// </summary>
    private static IEnumerable<string> CandidateSecretsPaths()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "UserSecrets", SecretsId, "secrets.json");

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".microsoft", "usersecrets", SecretsId, "secrets.json");
    }
}

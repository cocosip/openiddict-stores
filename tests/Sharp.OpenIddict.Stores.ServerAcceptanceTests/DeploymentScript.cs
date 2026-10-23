using System;
using System.IO;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Dm;
using Sharp.OpenIddict.Stores.MySql;
using Sharp.OpenIddict.Stores.Oracle;
using Sharp.OpenIddict.Stores.PostgreSql;
using Sharp.OpenIddict.Stores.SqlServer;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// Loads a shipped install-script template and renders the deployment copy the acceptance run executes:
/// <c>__PREFIX__</c> becomes the run-unique table prefix and <c>__SCHEMA__</c> the quoted schema plus its
/// trailing dot (nothing for the provider default). Rendering uses the runtime dialects, so a copy always
/// matches the identifiers the stores look up (design 10.1).
/// </summary>
internal static class DeploymentScript
{
    public static string Sqlite(string prefix, string? schema = null)
        => OpenIddictStoreIdentifiers.RenderDeploymentScript(
            Template("Sqlite"), OpenIddict.Stores.Sqlite.OpenIddictStoreSqliteDialect.Instance, prefix, schema);

    public static string SqlServer(string prefix, string? schema = null)
        => OpenIddictStoreIdentifiers.RenderDeploymentScript(
            Template("SqlServer"), OpenIddictStoreSqlServerDialect.Instance, prefix, schema);

    public static string PostgreSql(string prefix, string? schema = null)
        => OpenIddictStoreIdentifiers.RenderDeploymentScript(
            Template("PostgreSql"), OpenIddictStorePostgreSqlDialect.Instance, prefix, schema);

    /// <summary>
    /// Renders the MySQL/MariaDB install script. One physical template serves both servers, so the two suites
    /// call this with their own connection; the rendered text is identical, which is exactly why the two runs
    /// have to be recorded separately (design 5, 10.1).
    /// </summary>
    public static string MySql(string prefix)
        => OpenIddictStoreIdentifiers.RenderDeploymentScript(
            Template("MySql"), OpenIddictStoreMySqlDialect.Instance, prefix);

    public static string Oracle(string prefix, string? schema = null)
        => OpenIddictStoreIdentifiers.RenderDeploymentScript(
            Template("Oracle"), OpenIddictStoreOracleDialect.Instance, prefix, schema);

    /// <summary>
    /// Renders the DM8 install script for one compatibility mode. The mode names the template subdirectory
    /// and selects the dialect whose quoting and identifier limit the rendering obeys; the values are the
    /// modes' own <c>CompatibilityMode</c> constants, so an unknown mode fails here instead of rendering the
    /// wrong script.
    /// </summary>
    public static string Dm(string mode, string prefix, string? schema = null)
        => OpenIddictStoreIdentifiers.RenderDeploymentScript(
            Template("Dm", mode), mode switch
            {
                OpenIddictStoreDmDialect.CompatibilityMode => OpenIddictStoreDmDialect.Instance,
                OpenIddictStoreDmOracleCompatibleDialect.CompatibilityMode => OpenIddictStoreDmOracleCompatibleDialect.Instance,
                OpenIddictStoreDmMySqlCompatibleDialect.CompatibilityMode => OpenIddictStoreDmMySqlCompatibleDialect.Instance,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown DM8 compatibility mode."),
            },
            prefix, schema);

    private static string Template(string provider, string? mode = null)
        => File.ReadAllText(Path.Combine(
            RepositoryLayout.Root, "src", $"Sharp.OpenIddict.Stores.{provider}", "Sql", "1", "install",
            mode is null ? "v0001.sql.template" : Path.Combine(mode, "v0001.sql.template")));
}

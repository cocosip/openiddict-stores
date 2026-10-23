using System;
using System.IO;
using Sharp.OpenIddict.Stores;

namespace Sharp.OpenIddict.Stores.IntegrationTests;

/// <summary>
/// Loads a shipped install-script template and renders a deployment copy for the given prefix and schema.
/// The repository files are templates, not runnable scripts: table names are configurable, so a copy has
/// to be rendered before it can be executed (design 10.1).
/// </summary>
internal static class DeploymentScript
{
    public const string DefaultPrefix = OpenIddictStoreOptions.DefaultTablePrefix;

    /// <summary>Reads <c>Sql/1/install/v0001.sql.template</c> from the provider project.</summary>
    /// <remarks>
    /// DM ships one script per compatibility mode in a subdirectory; the other providers keep the single file.
    /// </remarks>
    public static string Template(string provider, string? mode = null)
        => File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", $"Sharp.OpenIddict.Stores.{provider}", "Sql", "1", "install",
            mode is null ? "v0001.sql.template" : Path.Combine(mode, "v0001.sql.template")));

    /// <summary>Renders the SQLite install script with the same quoting the runtime dialect uses.</summary>
    public static string Sqlite(string prefix = DefaultPrefix)
        => OpenIddictStoreIdentifiers.RenderDeploymentScript(
            Template("Sqlite"), SqliteDialect, prefix);

    private static IOpenIddictStoreDialect SqliteDialect => OpenIddict.Stores.Sqlite.OpenIddictStoreSqliteDialect.Instance;

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

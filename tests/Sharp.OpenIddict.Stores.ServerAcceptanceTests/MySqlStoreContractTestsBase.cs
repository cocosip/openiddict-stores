using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Threading.Tasks;
using MySqlConnector;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.MySql;
using Xunit;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// Shared plumbing for the MySQL and MariaDB acceptance suites. One physical template serves both servers
/// (design 5, 10.1), so the rendering, the catalog query, and the version floor are identical; only the server
/// identity and the declared floor differ, and each suite keeps its own fact list so a run on one server never
/// stands in for the other.
/// </summary>
/// <remarks>
/// The suites install into the connecting user's own database under a run-unique table prefix and drop exactly
/// the nine tables they created, so a real deployment in the same database is safe. Unlike PostgreSQL, MySQL
/// has no dedicated-schema concept, which is why <see cref="ServerAcceptanceBase.SchemaName"/> stays null and
/// the options validator rejects <c>SetSchema</c> for this provider (design 14.2).
/// </remarks>
public abstract class MySqlStoreContractTestsBase : ServerAcceptanceBase
{
    /// <summary>The <c>VERSION()</c> prefix the instance must report, e.g. <c>8.0</c> or <c>10.6</c>.</summary>
    protected abstract string MinimumServerVersion { get; }

    /// <summary>The manifest <c>server</c> entry this suite is the acceptance run for.</summary>
    protected abstract string ServerEntry { get; }

    protected override OpenIddictStoreProvider Provider => OpenIddictStoreProvider.MySql;

    protected override IOpenIddictStoreDialect Dialect => OpenIddictStoreMySqlDialect.Instance;

    protected override DbConnection CreateConnection() => new MySqlConnection(ConnectionString);

    protected override string RenderInstallScript(string prefix) => DeploymentScript.MySql(prefix);

    /// <summary>
    /// The catalog query for existing tables whose name starts with the prefix. <c>TABLE_SCHEMA = DATABASE()</c>
    /// keeps the listing inside the database the run is connected to, so a second database on the same server
    /// can never contribute a name that then gets dropped here. MySQL spells these metadata columns in upper
    /// case, unlike PostgreSQL's lowercase information_schema.
    /// </summary>
    protected override Task<List<string>> ListOwnedTableNamesAsync(string prefix)
        => QueryScalarsAsync<string>(
            "SELECT TABLE_NAME FROM information_schema.TABLES " +
            "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME LIKE @pattern",
            ("pattern", prefix + "%"));

    protected override Task DropOwnedTableAsync(string tableName)
        => ExecuteAsync($"DROP TABLE {Dialect.QuoteIdentifier(tableName)}");

    /// <summary>
    /// Verifies the server really is the one this suite claims: <c>VERSION()</c> must be at least the declared
    /// floor. A MariaDB instance reports <c>10.6.16-MariaDB</c> and a MySQL instance <c>8.0.36</c>, so a
    /// numeric comparison is used rather than a prefix match — a prefix would reject MySQL 8.4, which is above
    /// the floor and perfectly valid. The MariaDB/MySQL distinction is asserted by the caller's own suite
    /// because the two share this plumbing but not the declaration.
    /// </summary>
    protected override async Task CheckServerPrerequisitesAsync()
    {
        var version = Assert.Single(await QueryScalarsAsync<string>("SELECT VERSION()"));

        Assert.True(ParseVersion(version) >= ParseVersion(MinimumServerVersion),
            $"The manifest declares {ServerEntry} {MinimumServerVersion}+ for this entry; the server reports '{version}'.");
        // The two entries point at one template, so the only thing that makes them separate acceptance runs is
        // that each suite insists on its own server. A MariaDB run recorded against the MySQL entry would be
        // evidence for a target nothing was executed against.
        var reportedMariaDb = version.Contains("MariaDB", System.StringComparison.Ordinal);
        Assert.True(reportedMariaDb == IsMariaDb,
            $"This suite is the acceptance run for {ServerEntry}, but the server reports '{version}'.");
    }

    /// <summary>Whether this suite targets MariaDB rather than MySQL.</summary>
    protected abstract bool IsMariaDb { get; }

    /// <summary>
    /// Reads the leading <c>major.minor</c> of a server version banner as one comparable integer. Both servers
    /// prefix the number, so the suffix (<c>-MariaDB</c>, <c>-log</c>, distro tags) is ignored. The scale leaves
    /// room for a three-digit minor version, which is how MariaDB numbers its releases.
    /// </summary>
    private static int ParseVersion(string version)
    {
        var parts = version.Split('.', '-', '+');

        return int.Parse(parts[0], CultureInfo.InvariantCulture) * 10_000
             + int.Parse(parts[1], CultureInfo.InvariantCulture);
    }
}

using System;
using System.Linq;
using Sharp.OpenIddict.Stores.SqlTool;
using Xunit;

namespace Sharp.OpenIddict.Stores.Tests;

/// <summary>
/// The SQL generation tool must produce, from the embedded assets, exactly the script a deployment renders
/// by hand: the right manifest entry per provider and Dameng compatibility mode, the configured prefix and
/// schema, and the seed record — with no placeholder left behind.
/// </summary>
public sealed class SqlToolTests
{
    [Fact]
    public void ListProviders_CoversEveryProviderAndItsModes()
    {
        var providers = DeploymentScriptFactory.ListProviders();

        Assert.Equal(
            new[] { "SqlServer", "PostgreSql", "MySql", "Sqlite", "Oracle", "Dm" },
            providers.Select(provider => provider.Name).ToArray());

        var dm = Assert.Single(providers, provider => provider.Name == "Dm");

        Assert.Equal(
            new[] { "native", "oracle-compatible", "mysql-compatible" },
            dm.Entries.Select(entry => entry.Mode).ToArray());

        // Each entry names the dialect it renders with: a provider-wide dialect would misreport two of the
        // three DM entries as native (design 5, 10.1).
        Assert.Equal(
            new[] { "OpenIddictStoreDmDialect", "OpenIddictStoreDmOracleCompatibleDialect", "OpenIddictStoreDmMySqlCompatibleDialect" },
            dm.Entries.Select(entry => entry.Dialect.GetType().Name).ToArray());

        // The manifest is the source the digests are recorded in; the tool must expose the same claims.
        Assert.All(dm.Entries, entry => Assert.Equal(64, entry.Sha256.Length));
    }

    [Fact]
    public void Render_DefaultPrefixAndSchema_ProduceTheSameScriptTheReadmeDocuments()
    {
        var sql = DeploymentScriptFactory.Render("Sqlite");

        Assert.Contains("\"OpenIddictApplications\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("__PREFIX__", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("__SCHEMA__", sql, StringComparison.Ordinal);

        // The version guard refuses to run without the seed record, so every rendered script must carry it.
        Assert.Contains("INSERT INTO", sql, StringComparison.Ordinal);
        Assert.Contains("'1', 1", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_CustomPrefixAndSchema_ReachesTablesAndIndexes()
    {
        var sql = DeploymentScriptFactory.Render("Dm", mode: "native", prefix: "Oidc", schema: "OIDC");

        Assert.Contains("\"OIDC\".\"OidcApplications\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"OIDC\".\"UK_OidcApplications_ClientId\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"OIDC\".\"OidcSchemaVersions\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_DmMode_SelectsTheMatchingManifestEntry()
    {
        var native = DeploymentScriptFactory.Render("Dm", mode: "native", prefix: "Oidc");
        var oracleCompatible = DeploymentScriptFactory.Render("Dm", mode: "oracle-compatible", prefix: "Oidc");
        var mySqlCompatible = DeploymentScriptFactory.Render("Dm", mode: "mysql-compatible", prefix: "Oidc");

        // Each mode's script declares its mode in the header; the bodies are shared by design and a test in
        // the dialect suite enforces that, so here only the declaration has to differ.
        Assert.Contains("(native mode)", native, StringComparison.Ordinal);
        Assert.Contains("(Oracle-compatible mode)", oracleCompatible, StringComparison.Ordinal);
        Assert.Contains("(MySQL-compatible mode)", mySqlCompatible, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_OmittedDmMode_FallsBackToNative()
    {
        Assert.Equal(
            DeploymentScriptFactory.Render("Dm", mode: "native", prefix: "Oidc"),
            DeploymentScriptFactory.Render("Dm", prefix: "Oidc"));
    }

    [Fact]
    public void Render_MySqlWithoutServer_ResolvesTheSharedScript()
    {
        // MySQL and MariaDB are two entries over one file; the tool must not force a --server choice.
        var sql = DeploymentScriptFactory.Render("MySql", prefix: "Oidc");

        Assert.DoesNotContain("__PREFIX__", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Sqlite", "native")]
    [InlineData("Oracle", "native")]
    [InlineData("SqlServer", "mysql-compatible")]
    public void Render_ModeOnANonDmProvider_IsRejected(string provider, string mode)
    {
        Assert.Throws<ArgumentException>(() => DeploymentScriptFactory.Render(provider, mode: mode));
    }

    [Fact]
    public void Render_UnknownProviderOrMode_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => DeploymentScriptFactory.Render("Firestore"));
        Assert.Throws<ArgumentException>(() => DeploymentScriptFactory.Render("Dm", mode: "teradata"));
    }

    /// <summary>
    /// The runtime refuses a dedicated schema where the provider has no such concept, and so must the tool:
    /// rendering a script without the qualifier would install tables the application never looks for.
    /// </summary>
    [Theory]
    [InlineData("MySql")]
    [InlineData("Sqlite")]
    public void Render_SchemaOnAProviderWithoutSchemaSupport_IsRejected(string provider)
    {
        Assert.Throws<ArgumentException>(() => DeploymentScriptFactory.Render(provider, schema: "OIDC"));

        // The same call without a schema keeps working.
        Assert.DoesNotContain("__PREFIX__", DeploymentScriptFactory.Render(provider), StringComparison.Ordinal);
    }

    /// <summary>
    /// A manifest may carry upgrade entries beside the install entry; the tool renders the install script and
    /// must never hand an upgrade transition to a deployment as that script (design 10.1).
    /// </summary>
    [Fact]
    public void ListProviders_ExposesOnlyInstallEntries()
    {
        Assert.All(DeploymentScriptFactory.ListProviders(), provider =>
            Assert.All(provider.Entries, entry =>
            {
                Assert.Contains("/install/", entry.Path, StringComparison.Ordinal);
                Assert.DoesNotContain("/upgrade/", entry.Path, StringComparison.Ordinal);
            }));
    }

    [Theory]
    [InlineData(65, false)]
    [InlineData(66, true)]
    public void Render_SqlServerConstraintNames_CountTheFixedHeadInTheLengthLimit(int prefixLength, bool rejected)
    {
        // The longest SQL Server object name is FK___PREFIX__ApplicationPostLogoutRedirectUris_...: the
        // fixed "FK_" head plus prefix plus suffix. Measuring only prefix + suffix (60 characters here)
        // would accept a prefix that passes rendering and then dies mid-install on that constraint.
        var prefix = new string('a', prefixLength);

        if (rejected)
        {
            Assert.Throws<ArgumentException>(() => DeploymentScriptFactory.Render("SqlServer", prefix: prefix));
        }
        else
        {
            var sql = DeploymentScriptFactory.Render("SqlServer", prefix: prefix);
            Assert.DoesNotContain("__PREFIX__", sql, StringComparison.Ordinal);
        }
    }
}

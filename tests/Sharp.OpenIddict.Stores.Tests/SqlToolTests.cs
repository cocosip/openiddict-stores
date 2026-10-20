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
}

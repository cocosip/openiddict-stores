using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Dm;
using Sharp.OpenIddict.Stores.Oracle;
using Xunit;

namespace Sharp.OpenIddict.Stores.Tests;

/// <summary>
/// Oracle and DM8 dialect checks that do not need a server: quoting, the identifier limit, paging, the
/// NLSSORT-based exact comparison, and — for DM — the compatibility-mode declaration that must not widen.
/// </summary>
public sealed class OracleAndDmDialectTests
{
    private static IOpenIddictStoreDialect Oracle => OpenIddictStoreOracleDialect.Instance;

    private static IOpenIddictStoreDialect Dm => OpenIddictStoreDmDialect.Instance;

    [Fact]
    public void Oracle_UsesColonParameters_AndDoubleQuotes()
    {
        Assert.Equal(OpenIddictStoreProvider.Oracle, Oracle.Provider);
        Assert.Equal(":", Oracle.ParameterPrefix);

        // Unquoted names fold to upper case in Oracle, so the mixed-case runtime names must be quoted.
        Assert.Equal("\"OpenIddictApplications\"", Oracle.QuoteIdentifier("OpenIddictApplications"));
    }

    [Fact]
    public void Oracle_IdentifierLimit_Is128_NotTheHistorical30()
    {
        // 30 was the limit before 12.2; the declared floor is 19c, and the longest table name is already 43.
        Assert.Equal(128, Oracle.MaximumIdentifierLength);

        Assert.Equal("\"" + new string('a', 128) + "\"", Oracle.QuoteIdentifier(new string('a', 128)));
        Assert.Throws<ArgumentException>(() => Oracle.QuoteIdentifier(new string('a', 129)));
    }

    [Fact]
    public void Oracle_ExactComparison_ForcesABinarySort()
    {
        // VARCHAR2 comparison is non-padded, but case and accents follow NLS_COMP/NLS_SORT, so the predicate
        // pins both sides to a byte-wise sort instead of trusting session settings (design 16.1.1).
        Assert.Equal(
            "(NLSSORT(\"ClientId\", 'NLS_SORT=BINARY') = NLSSORT(:identifier, 'NLS_SORT=BINARY'))",
            Oracle.BuildExactStringEquals("\"ClientId\"", "identifier"));

        Assert.Equal(
            "(\"Status\" IS NULL OR NLSSORT(\"Status\", 'NLS_SORT=BINARY') <> NLSSORT(:status, 'NLS_SORT=BINARY'))",
            Oracle.BuildExactStringNotEqualsIncludingNull("\"Status\"", "status"));
    }

    [Fact]
    public void Oracle_Paging_UsesTheRowLimitingClause()
    {
        Assert.Equal(string.Empty, Oracle.BuildPaging(null, null));
        Assert.Equal("FETCH NEXT 10 ROWS ONLY", Oracle.BuildPaging(10, null));
        Assert.Equal("OFFSET 20 ROWS", Oracle.BuildPaging(null, 20));
        Assert.Equal("OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY", Oracle.BuildPaging(10, 20));

        // Unlike SQL Server, a zero count is expressible.
        Assert.Equal("FETCH NEXT 0 ROWS ONLY", Oracle.BuildPaging(0, null));
    }

    [Fact]
    public void Oracle_FormatDate_DropsTheUtcKind()
    {
        var value = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

        var formatted = Assert.IsType<DateTime>(Oracle.FormatDate(value));

        // The driver would shift a Utc value by the session time zone; the wall-clock UTC value is stored as-is.
        Assert.Equal(DateTimeKind.Unspecified, formatted.Kind);
        Assert.Equal(value.Ticks, formatted.Ticks);
    }

    [Fact]
    public void Oracle_And_Dm_DeclareDifferentParameterPrefixesAndDialects()
    {
        // DM is a separate implementation on purpose: "Oracle SQL runs" is not DM compatibility (design 5).
        Assert.NotSame(Oracle, Dm);
        Assert.Equal(OpenIddictStoreProvider.Dm, Dm.Provider);
        Assert.Equal(":", Dm.ParameterPrefix);
    }

    [Fact]
    public void Dm_DeclaresNativeMode_AsPartOfItsIdentity()
    {
        Assert.Equal("native", OpenIddictStoreDmDialect.CompatibilityMode);
    }

    /// <summary>
    /// The manifest is where a compatibility claim becomes binding, so an Oracle-compatible DM8 entry cannot
    /// appear by editing a README: it would have to be added here, with a script and an acceptance run.
    /// </summary>
    [Fact]
    public void DmManifest_ClaimsNativeModeOnly()
    {
        var path = Path.Combine(RepositoryRoot, "src", "Sharp.OpenIddict.Stores.Dm", "Sql", "manifest.json");

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var servers = document.RootElement.GetProperty("entries").EnumerateArray()
            .Select(entry => entry.GetProperty("server").GetString()!)
            .ToList();

        Assert.Single(servers);
        Assert.Contains("native", servers[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(servers, server => server.Contains("Oracle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Oracle_And_Dm_InstallScriptsUseTheirOwnPhysicalTypes()
    {
        var oracle = InstallTemplate("Oracle");
        var dm = InstallTemplate("Dm");

        // Design 15.5 gives each server its own mapping; sharing one script would be wrong for one of them.
        Assert.Contains("nvarchar2(100)", oracle, StringComparison.Ordinal);
        Assert.Contains("nclob", oracle, StringComparison.Ordinal);
        Assert.Contains("raw(32)", oracle, StringComparison.Ordinal);

        Assert.Contains("nvarchar(100)", dm, StringComparison.Ordinal);
        Assert.Contains("clob", dm, StringComparison.Ordinal);
        Assert.Contains("varbinary(32)", dm, StringComparison.Ordinal);

        // Neither may claim all-or-nothing installation: both commit DDL implicitly.
        Assert.DoesNotContain("BEGIN;", oracle, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN;", dm, StringComparison.Ordinal);
        Assert.Contains("commits DDL implicitly", oracle, StringComparison.Ordinal);
        Assert.Contains("commits DDL implicitly", dm, StringComparison.Ordinal);
    }

    [Fact]
    public void OracleAndDm_TemplatesRenderWithinTheIdentifierLimit()
    {
        foreach (var (provider, dialect) in new[] { ("Oracle", Oracle), ("Dm", Dm) })
        {
            var template = InstallTemplate(provider);
            var rendered = OpenIddictStoreIdentifiers.RenderDeploymentScript(template, dialect, "OpenIddict", "OIDC");

            var overlength = Regex.Matches(rendered, "\"[A-Za-z0-9_]+\"")
                .Select(match => match.Value.Trim('"'))
                .Where(name => name.Length > dialect.MaximumIdentifierLength)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            Assert.True(overlength.Count == 0,
                $"{provider} renders identifiers over {dialect.MaximumIdentifierLength}: " + string.Join(", ", overlength));

            // The schema qualifier must reach both the tables and the index definitions.
            Assert.Contains("\"OIDC\".\"OpenIddictApplications\"", rendered, StringComparison.Ordinal);
            Assert.Contains("\"OIDC\".\"UK_OpenIddictApplications_ClientId\"", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("__PREFIX__", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("__SCHEMA__", rendered, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UseOracleAndUseDm_RegisterTheirOwnDialects()
    {
        AssertRegistered(dialect => dialect.UseOracle(), typeof(OpenIddictStoreOracleDialect));
        AssertRegistered(dialect => dialect.UseDm(), typeof(OpenIddictStoreDmDialect));

        static void AssertRegistered(Func<OpenIddictStoreBuilder, OpenIddictStoreBuilder> select, Type expected)
        {
            var services = new ServiceCollection();

            services.AddOpenIddict().AddCore(options => options.UseStores(stores =>
                select(stores).UseConnectionFactory(_ => new FakeConnection())));

            using var provider = ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);

            Assert.IsType(expected, provider.GetService(typeof(IOpenIddictStoreDialect)));
        }
    }

    private static string InstallTemplate(string provider)
        => File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", $"Sharp.OpenIddict.Stores.{provider}", "Sql", "7", "install", "v0001.sql.template"));

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

    private sealed class FakeConnection : System.Data.Common.DbConnection
    {
        private string _connectionString = string.Empty;

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }

        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "1.0";
        public override System.Data.ConnectionState State => System.Data.ConnectionState.Closed;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Close() { }
        public override void Open() => throw new NotSupportedException();
        protected override System.Data.Common.DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel)
            => throw new NotSupportedException();
        protected override System.Data.Common.DbCommand CreateDbCommand()
            => throw new NotSupportedException();
    }
}

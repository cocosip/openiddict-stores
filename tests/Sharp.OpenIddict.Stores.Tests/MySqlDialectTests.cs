using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.MySql;
using Xunit;

namespace Sharp.OpenIddict.Stores.Tests;

/// <summary>
/// MySQL/MariaDB dialect checks that do not need a server: backtick quoting, the identifier limit, LIMIT/OFFSET
/// paging, and the BINARY operator that PAD SPACE collations make mandatory for exact comparison (design 16.1.1).
/// </summary>
public sealed class MySqlDialectTests
{
    private static IOpenIddictStoreDialect Dialect => OpenIddictStoreMySqlDialect.Instance;

    [Fact]
    public void Provider_ParameterPrefixAndLimits_MatchMySql()
    {
        Assert.Equal(OpenIddictStoreProvider.MySql, Dialect.Provider);
        Assert.Equal("@", Dialect.ParameterPrefix);
        Assert.True(Dialect.MaximumParameters <= 65_535);

        // MySQL identifiers are limited to 64 characters and longer names are rejected, not truncated.
        Assert.Equal(64, Dialect.MaximumIdentifierLength);
    }

    [Fact]
    public void QuoteIdentifier_UsesBackticks()
    {
        // Backticks work in every server mode; ANSI double quotes depend on sql_mode.
        Assert.Equal("`OpenIddictApplications`", Dialect.QuoteIdentifier("OpenIddictApplications"));

        // "Position" is a reserved word, which is exactly why every name is quoted.
        Assert.Equal("`Position`", Dialect.QuoteIdentifier("Position"));
    }

    [Fact]
    public void QuoteIdentifier_RejectsIdentifierLongerThanTheLimit()
    {
        var atLimit = new string('a', 64);
        Assert.Equal("`" + atLimit + "`", Dialect.QuoteIdentifier(atLimit));

        var exception = Assert.Throws<ArgumentException>(() => Dialect.QuoteIdentifier(new string('a', 65)));
        Assert.Contains("64-byte identifier limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildExactStringEquals_AddsBinary_SoTrailingSpacesAreSignificant()
    {
        // Without BINARY a PAD SPACE collation treats 'client' and 'client ' as equal and the lookup would
        // return the wrong row; the operator is what makes the comparison exact (design 16.1.1).
        Assert.Equal("(`ClientId` = BINARY @identifier)", Dialect.BuildExactStringEquals("`ClientId`", "identifier"));
    }

    [Fact]
    public void BuildExactStringNotEqualsIncludingNull_KeepsNullRowsAndStaysBinary()
    {
        Assert.Equal(
            "(`Status` IS NULL OR `Status` <> BINARY @status)",
            Dialect.BuildExactStringNotEqualsIncludingNull("`Status`", "status"));
    }

    [Fact]
    public void BuildExactStringColumnEquals_ComparesByteWise()
    {
        Assert.Equal(
            "(BINARY `a`.`ApplicationId` = `b`.`Id`)",
            Dialect.BuildExactStringColumnEquals("`a`.`ApplicationId`", "`b`.`Id`"));
    }

    [Fact]
    public void BuildPaging_AlwaysEmitsLimit_BecauseMySqlRequiresIt()
    {
        Assert.Equal(string.Empty, Dialect.BuildPaging(null, null));
        Assert.Equal("LIMIT 10", Dialect.BuildPaging(10, null));
        Assert.Equal("LIMIT 10 OFFSET 20", Dialect.BuildPaging(10, 20));

        // OFFSET without LIMIT is a syntax error in MySQL; the documented "no limit" value stands in.
        Assert.Equal("LIMIT 18446744073709551615 OFFSET 20", Dialect.BuildPaging(null, 20));

        // LIMIT 0 is legal and returns no rows.
        Assert.Equal("LIMIT 0", Dialect.BuildPaging(0, null));
    }

    [Fact]
    public void FormatDate_BindsTheUtcValueUnchanged_AndKeepsMicroseconds()
    {
        var value = new DateTime(2026, 10, 19, 12, 34, 56, DateTimeKind.Utc).AddTicks(1_239);

        var formatted = Assert.IsType<DateTime>(Dialect.FormatDate(value));

        // Normalized to microseconds, then bound as-is: the driver must not shift it by the server time zone.
        Assert.Equal(value.Ticks - 9, formatted.Ticks);
        Assert.Null(Dialect.FormatDate(null));
    }

    [Fact]
    public void UseMySql_RegistersTheDialect()
    {
        var services = new ServiceCollection();

        services.AddOpenIddict().AddCore(options => options.UseStores(stores => stores
            .UseMySql()
            .UseConnectionFactory(_ => new FakeConnection())));

        using var provider = ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);

        Assert.Same(OpenIddictStoreMySqlDialect.Instance, provider.GetService(typeof(IOpenIddictStoreDialect)));
    }

    [Fact]
    public void InstallTemplate_RendersWithinTheIdentifierLimit_ForTheLongestUsablePrefix()
    {
        var template = InstallTemplate();

        // "ApplicationPostLogoutRedirectUris" is 33 characters, so 31 is the longest prefix that fits 64.
        foreach (var prefix in new[] { OpenIddictStoreOptions.DefaultTablePrefix, new string('P', 31) })
        {
            var rendered = OpenIddictStoreIdentifiers.RenderDeploymentScript(template, Dialect, prefix);

            var overlength = Regex.Matches(rendered, "`[A-Za-z0-9_]+`")
                .Select(match => match.Value.Trim('`'))
                .Where(name => name.Length > 64)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            Assert.True(overlength.Count == 0,
                $"prefix '{prefix}' renders identifiers longer than 64 characters: " + string.Join(", ", overlength));
        }
    }

    [Fact]
    public void InstallTemplate_RefusesAPrefixWhoseTableNamesWouldBeRefused()
    {
        var exception = Assert.Throws<ArgumentException>(() => OpenIddictStoreIdentifiers
            .RenderDeploymentScript(InstallTemplate(), Dialect, new string('P', 40)));

        Assert.Contains("64-byte identifier limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallTemplate_StatementsSplitCleanly()
    {
        var rendered = OpenIddictStoreIdentifiers.RenderDeploymentScript(
            InstallTemplate(), Dialect, "OpenIddict");

        var statements = OpenIddictStoreIdentifiers.SplitStatements(rendered);

        // Nine tables, three unique indexes, twelve secondary indexes, one SchemaVersions row. There is no
        // BEGIN/COMMIT because MySQL commits DDL implicitly (see Sql/README.md).
        Assert.Equal(9 + 3 + 12 + 1, statements.Length);

        Assert.All(statements.Take(statements.Length - 1),
            statement => Assert.StartsWith("CREATE", statement, StringComparison.Ordinal));
        Assert.StartsWith("INSERT INTO", statements[^1], StringComparison.Ordinal);

        Assert.DoesNotContain("__PREFIX__", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("__SCHEMA__", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallTemplate_UsesThePhysicalTypesFromTheDesign()
    {
        var template = InstallTemplate();

        // Design 15.5: varchar(n) for bounded text, longtext for large text and JSON, binary(32) for the
        // digests, datetime(6) for UTC values.
        Assert.Contains("varchar(100)", template, StringComparison.Ordinal);
        Assert.Contains("`Payload` longtext", template, StringComparison.Ordinal);
        Assert.Contains("`UriHash` binary(32)", template, StringComparison.Ordinal);
        Assert.Contains("datetime(6)", template, StringComparison.Ordinal);

        // utf8mb4 is required for full Unicode; the binary collation keeps comparisons case-sensitive.
        Assert.Contains("CHARSET=utf8mb4 COLLATE=utf8mb4_bin", template, StringComparison.Ordinal);

        // The row format is stated so the 3072-byte index key limit does not depend on server defaults.
        Assert.Contains("ROW_FORMAT=DYNAMIC", template, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallTemplate_HasNoTransaction_AndSaysWhyItCannot()
    {
        var template = InstallTemplate();

        // MySQL and MariaDB commit DDL implicitly, so promising all-or-nothing would be a lie.
        Assert.DoesNotContain("BEGIN;", template, StringComparison.Ordinal);
        Assert.DoesNotContain("START TRANSACTION", template, StringComparison.Ordinal);
        Assert.Contains("commit DDL implicitly", template, StringComparison.Ordinal);
    }

    private static string InstallTemplate()
        => File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Sharp.OpenIddict.Stores.MySql", "Sql", "1", "install", "v0001.sql.template"));

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

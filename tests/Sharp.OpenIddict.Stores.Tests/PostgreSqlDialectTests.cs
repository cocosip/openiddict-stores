using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.PostgreSql;
using Xunit;

namespace Sharp.OpenIddict.Stores.Tests;

/// <summary>
/// PostgreSQL dialect checks that do not need a server: identifier quoting, the byte-length limit that a server
/// would otherwise enforce by silently truncating, paging, and the timestamp binding that Npgsql 6+ requires.
/// </summary>
public sealed class PostgreSqlDialectTests
{
    private static IOpenIddictStoreDialect Dialect => OpenIddictStorePostgreSqlDialect.Instance;

    [Fact]
    public void Provider_ParameterPrefixAndLimits_MatchPostgreSql()
    {
        Assert.Equal(OpenIddictStoreProvider.PostgreSql, Dialect.Provider);
        Assert.Equal("@", Dialect.ParameterPrefix);

        // 65535 is the protocol limit; the constant is lower to leave room for the values a statement carries.
        Assert.True(Dialect.MaximumParameters <= 65_535);

        // 63 bytes is where PostgreSQL starts truncating identifiers without an error.
        Assert.Equal(63, Dialect.MaximumIdentifierLength);
    }

    [Fact]
    public void QuoteIdentifier_UsesDoubleQuotes_AndRejectsReservedWordsOnlyByQuoting()
    {
        // Unquoted names fold to lower case, so the mixed-case runtime names only work when quoted.
        Assert.Equal("\"OpenIddictApplications\"", Dialect.QuoteIdentifier("OpenIddictApplications"));
        Assert.Equal("\"user\"", Dialect.QuoteIdentifier("user"));
    }

    [Fact]
    public void QuoteIdentifier_RejectsIdentifierLongerThanTheByteLimit()
    {
        // 63 ASCII characters is exactly the limit.
        var atLimit = new string('a', 63);
        Assert.Equal("\"" + atLimit + "\"", Dialect.QuoteIdentifier(atLimit));

        var exception = Assert.Throws<ArgumentException>(() => Dialect.QuoteIdentifier(new string('a', 64)));
        Assert.Contains("63-byte identifier limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void QuoteIdentifier_CountsBytesNotCharacters()
    {
        // 32 two-byte characters are 32 characters but 64 bytes, which the server would truncate.
        var widened = new string('\u00e9', 32);

        Assert.Equal(32, widened.Length);
        Assert.Equal(64, System.Text.Encoding.UTF8.GetByteCount(widened));
        Assert.Throws<ArgumentException>(() => Dialect.QuoteIdentifier(widened));
    }

    [Fact]
    public void BuildExactStringEquals_IsAPlainEquality_WithoutSqlServerLengthCompensation()
    {
        // C is forced in the predicate so a hand-created table cannot fall back to a case-insensitive default
        // collation; being byte-wise, it needs no DATALENGTH trick the way SQL Server did.
        Assert.Equal(
            "(\"ClientId\" COLLATE \"C\" = @identifier)",
            Dialect.BuildExactStringEquals("\"ClientId\"", "identifier"));
        Assert.DoesNotContain("DATALENGTH", Dialect.BuildExactStringEquals("\"ClientId\"", "identifier"), StringComparison.Ordinal);

        Assert.Equal(
            "(\"a\".\"ApplicationId\" COLLATE \"C\" = \"b\".\"Id\")",
            Dialect.BuildExactStringColumnEquals("\"a\".\"ApplicationId\"", "\"b\".\"Id\""));
    }

    [Fact]
    public void BuildExactStringNotEqualsIncludingNull_KeepsNullRows()
    {
        Assert.Equal(
            "(\"Status\" IS NULL OR \"Status\" COLLATE \"C\" <> @status)",
            Dialect.BuildExactStringNotEqualsIncludingNull("\"Status\"", "status"));
    }

    [Fact]
    public void BuildPaging_UsesLimitAndOffset_AndAllowsOffsetWithoutLimit()
    {
        Assert.Equal(string.Empty, Dialect.BuildPaging(null, null));
        Assert.Equal("LIMIT 10", Dialect.BuildPaging(10, null));
        Assert.Equal("OFFSET 20", Dialect.BuildPaging(null, 20));
        Assert.Equal("LIMIT 10 OFFSET 20", Dialect.BuildPaging(10, 20));

        // Unlike SQL Server, a zero count is expressible and returns no rows.
        Assert.Equal("LIMIT 0", Dialect.BuildPaging(0, null));
    }

    [Fact]
    public void FormatDate_DropsTheUtcKind_ButKeepsTheInstant()
    {
        // Npgsql 6+ rejects a UTC DateTime for timestamp without time zone and requires Unspecified.
        var value = new DateTime(2026, 10, 19, 12, 34, 56, DateTimeKind.Utc).AddTicks(1_230);

        var formatted = Assert.IsType<DateTime>(Dialect.FormatDate(value));

        Assert.Equal(DateTimeKind.Unspecified, formatted.Kind);
        Assert.Equal(value.Ticks, formatted.Ticks);

        Assert.Null(Dialect.FormatDate(null));
    }

    [Fact]
    public void FormatDate_NormalizesLocalInputToUtc()
    {
        var local = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Local);

        var formatted = Assert.IsType<DateTime>(Dialect.FormatDate(local));

        Assert.Equal(DateTimeKind.Unspecified, formatted.Kind);
        Assert.Equal(local.ToUniversalTime().Ticks, formatted.Ticks);
    }

    [Fact]
    public void UsePostgreSql_RegistersTheDialect()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        services.AddOpenIddict().AddCore(options => options.UseStores(stores => stores
            .UsePostgreSql()
            .UseConnectionFactory(_ => new FakeConnection())));

        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions
            .BuildServiceProvider(services);

        Assert.Same(
            OpenIddictStorePostgreSqlDialect.Instance,
            provider.GetService(typeof(IOpenIddictStoreDialect)));
    }

    [Fact]
    public void InstallTemplate_RendersWithinTheIdentifierLimit_ForEveryTablePrefixTheOptionsAllow()
    {
        var template = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Sharp.OpenIddict.Stores.PostgreSql", "Sql", "7", "install", "v0001.sql.template"));

        // The longest fixed table suffix is "ApplicationPostLogoutRedirectUris" (33 bytes), so a 30-character
        // prefix is the longest that keeps every table name within the 63-byte limit.
        foreach (var prefix in new[] { OpenIddictStoreOptions.DefaultTablePrefix, new string('P', 30) })
        {
            var rendered = OpenIddictStoreIdentifiers.RenderDeploymentScript(template, Dialect, prefix);

            var overlength = Regex.Matches(rendered, "\"[A-Za-z0-9_]+\"")
                .Select(match => match.Value.Trim('"'))
                .Where(name => System.Text.Encoding.UTF8.GetByteCount(name) > 63)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            Assert.True(overlength.Count == 0,
                $"prefix '{prefix}' renders identifiers longer than 63 bytes: " + string.Join(", ", overlength));
        }
    }

    [Fact]
    public void InstallTemplate_RefusesAPrefixWhoseTableNamesWouldBeTruncated()
    {
        var template = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Sharp.OpenIddict.Stores.PostgreSql", "Sql", "7", "install", "v0001.sql.template"));

        var exception = Assert.Throws<ArgumentException>(() => OpenIddictStoreIdentifiers
            .RenderDeploymentScript(template, Dialect, new string('P', 40)));

        Assert.Contains("63-byte identifier limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallTemplate_StatementsSplitCleanly_WithoutStrayCommentStatements()
    {
        var template = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Sharp.OpenIddict.Stores.PostgreSql", "Sql", "7", "install", "v0001.sql.template"));
        var rendered = OpenIddictStoreIdentifiers.RenderDeploymentScript(template, Dialect, "OpenIddict");

        var statements = OpenIddictStoreIdentifiers.SplitStatements(rendered);

        // SplitStatements keeps the terminating semicolon, so compare the keyword only.
        Assert.Equal("BEGIN;", statements[0]);
        Assert.Equal("COMMIT;", statements[^1]);

        // BEGIN + COMMIT, nine tables, three unique indexes, twelve secondary indexes, one SchemaVersions row.
        Assert.Equal(2 + 9 + 3 + 12 + 1, statements.Length);

        // A comment line must never be executed on its own, and no placeholder may survive rendering.
        Assert.DoesNotContain(statements, statement => statement.TrimStart().StartsWith("--", StringComparison.Ordinal));
        Assert.DoesNotContain("__PREFIX__", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("__SCHEMA__", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallTemplate_DeclaresNoSchemaWhenTheDefaultOneIsUsed()
    {
        var template = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Sharp.OpenIddict.Stores.PostgreSql", "Sql", "7", "install", "v0001.sql.template"));

        var unqualified = OpenIddictStoreIdentifiers.RenderDeploymentScript(template, Dialect, "OpenIddict");
        var qualified = OpenIddictStoreIdentifiers.RenderDeploymentScript(template, Dialect, "OpenIddict", "auth");

        Assert.Contains("CREATE TABLE \"OpenIddictApplications\"", unqualified, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE \"auth\".\"OpenIddictApplications\"", qualified, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallTemplate_UsesThePhysicalTypesFromTheDesign()
    {
        var template = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Sharp.OpenIddict.Stores.PostgreSql", "Sql", "7", "install", "v0001.sql.template"));

        // Design 15.5: varchar(n) for bounded text, text for large text, bytea for the 32-byte digests,
        // timestamp(6) without time zone for UTC values.
        Assert.Contains("varchar(100)", template, StringComparison.Ordinal);
        Assert.Contains("\"Payload\" text", template, StringComparison.Ordinal);
        Assert.Contains("\"UriHash\" bytea", template, StringComparison.Ordinal);
        Assert.Contains("timestamp(6) without time zone", template, StringComparison.Ordinal);

        // Identifiers and the values compared for equality carry the C collation (design 16.1.1).
        Assert.Contains("\"ClientId\" varchar(100) COLLATE \"C\"", template, StringComparison.Ordinal);
        Assert.Contains("\"ConcurrencyToken\" varchar(50) COLLATE \"C\"", template, StringComparison.Ordinal);
    }

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

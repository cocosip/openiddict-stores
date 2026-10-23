using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Models;
using Sharp.OpenIddict.Stores.Sqlite;
using Sharp.OpenIddict.Stores.Stores;
using Xunit;

namespace Sharp.OpenIddict.Stores.IntegrationTests;

/// <summary>
/// Schema version contract from design 10.3/17: the stores verify Series/Version before the first operation
/// against a database, refuse to work against a mismatch instead of writing to an incompatible structure,
/// never issue DDL, and remember a successful check process-wide for that exact database — failures are
/// never cached, and a different database always runs its own check.
/// </summary>
public sealed class SchemaVersionContractTests : IAsyncLifetime
{
    private string _databasePath = string.Empty;
    private string _connectionString = string.Empty;

    public Task InitializeAsync()
    {
        _databasePath = TestDatabaseName.CreateFilePath("SchemaVersionContractTests");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true,
        }.ToString();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // A leftover temp file must never fail the suite.
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task InstalledSchema_IsAcceptedWithoutAnyError()
    {
        await InstallAsync();

        var store = CreateStore();

        Assert.Equal(0, await store.CountAsync(default));
    }

    [Fact]
    public async Task MissingSchema_IsRejectedBeforeAnyRead()
    {
        // No install script has run: the version table does not exist at all.
        var store = CreateStore();

        var exception = await Assert.ThrowsAsync<OpenIddictStoreSchemaVersionException>(
            () => store.CountAsync(default).AsTask());

        Assert.Contains("SchemaVersions", exception.Message, StringComparison.Ordinal);
        Assert.Contains("never creates or alters tables", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongVersion_IsRejectedAndNamesTheRequiredVersion()
    {
        await InstallAsync();
        await ExecuteAsync("UPDATE OpenIddictSchemaVersions SET Version = 2");

        var store = CreateStore();

        var exception = await Assert.ThrowsAsync<OpenIddictStoreSchemaVersionException>(
            () => store.CountAsync(default).AsTask());

        Assert.Contains("version 2", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"requires version {OpenIddictStoreSchema.RequiredVersion}", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongSeries_IsRejected()
    {
        await InstallAsync();
        await ExecuteAsync("UPDATE OpenIddictSchemaVersions SET Series = '8'");

        var store = CreateStore();

        var exception = await Assert.ThrowsAsync<OpenIddictStoreSchemaVersionException>(
            () => store.CountAsync(default).AsTask());

        Assert.Contains("series '8'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("requires series '1'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForeignSeriesRow_DoesNotMaskTheRequiredSeries()
    {
        await InstallAsync();

        // A version table may carry rows of several series once a second one ships; the guard must
        // read its own series' row instead of whichever row the engine happens to return first.
        await ExecuteAsync(
            "INSERT INTO OpenIddictSchemaVersions (Series, Version, UpdatedUtc) VALUES ('8', 1, '2026-10-20T00:00:00.000Z')");

        var store = CreateStore();

        Assert.Equal(0, await store.CountAsync(default));
    }

    [Fact]
    public async Task ForeignSeriesOnly_IsRejectedWithTheInstalledSeries()
    {
        await InstallAsync();
        await ExecuteAsync("DELETE FROM OpenIddictSchemaVersions WHERE Series = '1'");
        await ExecuteAsync(
            "INSERT INTO OpenIddictSchemaVersions (Series, Version, UpdatedUtc) VALUES ('8', 1, '2026-10-20T00:00:00.000Z')");

        var store = CreateStore();

        var exception = await Assert.ThrowsAsync<OpenIddictStoreSchemaVersionException>(
            () => store.CountAsync(default).AsTask());

        Assert.Contains("series '8'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("requires series '1'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingVersionRow_IsRejected()
    {
        await InstallAsync();
        await ExecuteAsync("DELETE FROM OpenIddictSchemaVersions");

        var store = CreateStore();

        var exception = await Assert.ThrowsAsync<OpenIddictStoreSchemaVersionException>(
            () => store.CountAsync(default).AsTask());

        Assert.Contains("no row for series", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomPrefix_ReadsItsOwnVersionTable()
    {
        await InstallAsync(prefix: "Oidc");

        // The guard must follow the configured prefix, not assume the default table name.
        var store = CreateStore(prefix: "Oidc");

        Assert.Equal(0, await store.CountAsync(default));
    }

    [Fact]
    public async Task RepeatedOperations_ReadTheVersionOnlyOnce()
    {
        await InstallAsync();

        var store = CreateStore();

        await store.CountAsync(default);
        await store.CountAsync(default);
        await store.CountAsync(default);

        // Drop the table after the first successful check: a cached verification must not re-read it.
        await ExecuteAsync("DROP TABLE OpenIddictSchemaVersions");

        Assert.Equal(0, await store.CountAsync(default));
    }

    [Fact]
    public async Task Verification_IsSharedAcrossStoreInstancesForTheSameDatabase()
    {
        await InstallAsync();

        await CreateStore().CountAsync(default);

        // A successful check is cached process-wide for this database (dialect, version table, connection
        // string), so a second store instance never re-reads the version table. A schema replaced under a
        // running process still requires the restart design 17 already mandates.
        await ExecuteAsync("DROP TABLE OpenIddictSchemaVersions");

        Assert.Equal(0, await CreateStore().CountAsync(default));
    }

    [Fact]
    public async Task ADifferentDatabase_VerifiesIndependently()
    {
        await InstallAsync();
        await CreateStore().CountAsync(default);

        // A second, uninstalled database file is a different cache key: the process-level cache for the
        // first database must never mask this one's missing schema.
        var otherPath = TestDatabaseName.CreateFilePath("SchemaVersionContractTestsOther");
        var otherConnection = new SqliteConnectionStringBuilder
        {
            DataSource = otherPath,
            ForeignKeys = true,
        }.ToString();

        try
        {
            var store = CreateStore(connectionString: otherConnection);

            await Assert.ThrowsAsync<OpenIddictStoreSchemaVersionException>(
                () => store.CountAsync(default).AsTask());
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            try
            {
                File.Delete(otherPath);
            }
            catch (IOException)
            {
                // A leftover temp file must never fail the suite.
            }
        }
    }

    [Fact]
    public async Task FailedVerification_IsNotCached()
    {
        await InstallAsync();
        await ExecuteAsync("UPDATE OpenIddictSchemaVersions SET Version = 5");

        var store = CreateStore();

        await Assert.ThrowsAsync<OpenIddictStoreSchemaVersionException>(() => store.CountAsync(default).AsTask());

        // The operator runs the script and retries on the same instance: the failure must not be sticky.
        await ExecuteAsync($"UPDATE OpenIddictSchemaVersions SET Version = {OpenIddictStoreSchema.RequiredVersion}");

        Assert.Equal(0, await store.CountAsync(default));
    }

    [Fact]
    public async Task StoreNeverIssuesDdl()
    {
        // Point the store at an empty file: a version check must fail, not create anything.
        var store = CreateStore();

        await Assert.ThrowsAsync<OpenIddictStoreSchemaVersionException>(() => store.CountAsync(default).AsTask());

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";

        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    /// <summary>
    /// A dialect that cannot force exact comparison into its predicate — DM8, where case sensitivity is fixed at
    /// database-creation time and invisible to the runtime — declares a startup probe. When the probe reports an
    /// unsupported instance the store must refuse to run, because the alternative is silently matching the wrong
    /// row (design 16.1.1).
    /// </summary>
    [Fact]
    public async Task StartupProbe_ThatReportsAnUnsupportedInstance_RefusesTheDatabase()
    {
        await InstallAsync();

        var store = CreateStore(probe: new ProbeDialect(unsupported: true));

        var exception = await Assert.ThrowsAsync<OpenIddictStoreServerConfigurationException>(
            () => store.CountAsync(default).AsTask());

        // The operator must be told which setting to change, and that the store will not degrade instead.
        Assert.Contains("CASE_SENSITIVE", exception.Message, StringComparison.Ordinal);
        Assert.Contains("will not fall back", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartupProbe_ThatReportsASupportedInstance_IsAccepted()
    {
        await InstallAsync();

        var store = CreateStore(probe: new ProbeDialect(unsupported: false));

        Assert.Equal(0, await store.CountAsync(default));
    }

    /// <summary>
    /// The probe is part of the cached verification, not a per-operation check: once it has passed, dropping
    /// even the objects it read must not trigger it again.
    /// </summary>
    [Fact]
    public async Task StartupProbe_RunsOnlyWithTheCachedVerification()
    {
        await InstallAsync();

        var dialect = new ProbeDialect(unsupported: false);
        var store = CreateStore(probe: dialect);

        await store.CountAsync(default);
        await store.CountAsync(default);

        Assert.Equal(1, dialect.ProbeQueriesBuilt);
    }

    /// <summary>
    /// The probe is a separate failure from a wrong schema: the tables are fine, the server is not. Reporting it
    /// as a version mismatch would send the operator looking for a script that cannot fix anything.
    /// </summary>
    [Fact]
    public async Task StartupProbe_IsNotReportedAsASchemaVersionFailure()
    {
        await InstallAsync();

        var store = CreateStore(probe: new ProbeDialect(unsupported: true));

        var exception = await Record.ExceptionAsync(() => store.CountAsync(default).AsTask());

        Assert.NotNull(exception);
        Assert.IsNotType<OpenIddictStoreSchemaVersionException>(exception);
    }

    private OpenIddictStoreApplicationStore<OpenIddictStoreApplication> CreateStore(
        string prefix = "OpenIddict", string? connectionString = null, IOpenIddictStoreStartupProbe? probe = null)
    {
        var options = Options.Create(new OpenIddictStoreOptions { TablePrefix = prefix });
        options.Value.Providers.Add(OpenIddictStoreProvider.Sqlite);

        IOpenIddictStoreDialect dialect = probe is null
            ? OpenIddictStoreSqliteDialect.Instance
            : new ProbingSqliteDialect(probe);

        return new OpenIddictStoreApplicationStore<OpenIddictStoreApplication>(
            options,
            new SqliteConnectionFactory(connectionString ?? _connectionString),
            dialect,
            NullLogger<OpenIddictStoreApplicationStore<OpenIddictStoreApplication>>.Instance);
    }

    private async Task InstallAsync(string prefix = "OpenIddict")
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        foreach (var statement in OpenIddictStoreIdentifiers.SplitStatements(DeploymentScript.Sqlite(prefix)))
        {
            if (statement.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase)
                || statement.StartsWith("COMMIT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// A probe whose verdict is chosen by the test. The SQL runs on SQLite, so the guard's plumbing — ordering
    /// against the version check, caching, and exception type — is exercised for real rather than mocked.
    /// </summary>
    private sealed class ProbeDialect(bool unsupported) : IOpenIddictStoreStartupProbe
    {
        /// <summary>How many times the guard asked for (and therefore executed) the probe.</summary>
        public int ProbeQueriesBuilt { get; private set; }

        public string BuildProbeSql(string versionsTable)
        {
            ProbeQueriesBuilt++;

            return unsupported
                ? $"SELECT 1 FROM {versionsTable}"
                : $"SELECT CASE WHEN 'abc' = 'ABC' THEN 1 ELSE 0 END FROM {versionsTable}";
        }

        public string ProbeFailureMessage =>
            "The instance folds case in string comparisons (CASE_SENSITIVE=0); exact string comparison requires " +
            "CASE_SENSITIVE=1, and this store will not fall back to an inexact comparison.";
    }

    /// <summary>The SQLite dialect plus a probe: a dialect only becomes a probe target by opting in.</summary>
    private sealed class ProbingSqliteDialect(IOpenIddictStoreStartupProbe probe)
        : IOpenIddictStoreDialect, IOpenIddictStoreStartupProbe
    {
        private readonly IOpenIddictStoreDialect _inner = OpenIddictStoreSqliteDialect.Instance;

        public OpenIddictStoreProvider Provider => _inner.Provider;
        public string ParameterPrefix => _inner.ParameterPrefix;
        public int MaximumParameters => _inner.MaximumParameters;
        public int MaximumIdentifierLength => _inner.MaximumIdentifierLength;
        public string QuoteIdentifier(string identifier) => _inner.QuoteIdentifier(identifier);
        public string BuildExactStringEquals(string column, string parameterName)
            => _inner.BuildExactStringEquals(column, parameterName);
        public string BuildPaging(int? count, int? offset) => _inner.BuildPaging(count, offset);
        public object? FormatDate(DateTime? value) => _inner.FormatDate(value);
        public DateTime? ReadDate(System.Data.Common.DbDataReader reader, int ordinal)
            => _inner.ReadDate(reader, ordinal);
        public string BuildExactStringNotEqualsIncludingNull(string column, string parameterName)
            => _inner.BuildExactStringNotEqualsIncludingNull(column, parameterName);
        public string BuildExactStringColumnEquals(string left, string right)
            => _inner.BuildExactStringColumnEquals(left, right);
        public bool EmptyStringBindsAsNull => _inner.EmptyStringBindsAsNull;

        public string BuildProbeSql(string versionsTable) => probe.BuildProbeSql(versionsTable);
        public string ProbeFailureMessage => probe.ProbeFailureMessage;
    }
}

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
/// Schema version contract from design 10.3/17: the stores verify Series/Version before the first operation,
/// refuse to work against a mismatch instead of writing to an incompatible structure, never issue DDL, and
/// remember the successful check only for the lifetime of that store configuration.
/// </summary>
public sealed class SchemaVersionContractTests : IAsyncLifetime
{
    private string _databasePath = string.Empty;
    private string _connectionString = string.Empty;

    public Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"oidc-version-{Guid.NewGuid():N}.db");
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
        Assert.Contains("requires series '7'", exception.Message, StringComparison.Ordinal);
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
    public async Task Verification_IsNotSharedBetweenStoreConfigurations()
    {
        await InstallAsync();

        await CreateStore().CountAsync(default);

        // A separate store instance is a separate configuration and must verify again, so it sees the change.
        await ExecuteAsync("UPDATE OpenIddictSchemaVersions SET Version = 9");

        var exception = await Assert.ThrowsAsync<OpenIddictStoreSchemaVersionException>(
            () => CreateStore().CountAsync(default).AsTask());

        Assert.Contains("version 9", exception.Message, StringComparison.Ordinal);
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

    private OpenIddictStoreApplicationStore<OpenIddictStoreApplication> CreateStore(string prefix = "OpenIddict")
    {
        var options = Options.Create(new OpenIddictStoreOptions { TablePrefix = prefix });
        options.Value.Providers.Add(OpenIddictStoreProvider.Sqlite);

        return new OpenIddictStoreApplicationStore<OpenIddictStoreApplication>(
            options,
            new SqliteConnectionFactory(_connectionString),
            OpenIddictStoreSqliteDialect.Instance,
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
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// Install script acceptance from design 10: render a deployment copy from the shipped template, execute it on
/// a temporary SQLite file, and verify tables, constraints, indexes, and the schema version record without
/// ad-hoc DDL. A non-default prefix is covered because the prefix is runtime configuration.
/// </summary>
public sealed class SqliteInstallScriptTests : IAsyncLifetime
{
    private string _databasePath = string.Empty;

    public Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"oidc-dapper-{Guid.NewGuid():N}.db");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task InstallScript_CreatesFourMainAndFourAuxiliaryTables()
    {
        await using var connection = await OpenAndInstallAsync();

        var tables = await QueryNamesAsync(connection,
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name");

        Assert.Equal(
            [
                "OpenIddictApplicationPostLogoutRedirectUris",
                "OpenIddictApplicationRedirectUris",
                "OpenIddictApplications",
                "OpenIddictAuthorizationScopes",
                "OpenIddictAuthorizations",
                "OpenIddictSchemaVersions",
                "OpenIddictScopeResources",
                "OpenIddictScopes",
                "OpenIddictTokens",
            ],
            tables);
    }

    [Fact]
    public async Task InstallScript_RecordsSeriesAndVersion()
    {
        await using var connection = await OpenAndInstallAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Series, Version FROM OpenIddictSchemaVersions";

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("7", reader.GetString(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task InstallScript_EnforcesForeignKeys()
    {
        await using var connection = await OpenAndInstallAsync();

        // Referencing a nonexistent Application must be rejected (foreign keys are enabled).
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO OpenIddictAuthorizations ("Id", "ConcurrencyToken", "ApplicationId")
            VALUES ('a1', 'c1', 'missing-application')
            """;

        var exception = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
        Assert.Contains("FOREIGN KEY", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstallScript_EnforcesUniqueClientIdAndName()
    {
        await using var connection = await OpenAndInstallAsync();

        await ExecuteAsync(connection,
            "INSERT INTO OpenIddictApplications (\"Id\", \"ConcurrencyToken\", \"ClientId\") VALUES ('1', 'c1', 'client')");

        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO OpenIddictApplications (\"Id\", \"ConcurrencyToken\", \"ClientId\") VALUES ('2', 'c2', 'client')";

        var exception = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
        Assert.Contains("UNIQUE", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstallScript_DistinguishesTrailingSpacesInUniqueIndex()
    {
        await using var connection = await OpenAndInstallAsync();

        await ExecuteAsync(connection,
            "INSERT INTO OpenIddictApplications (\"Id\", \"ConcurrencyToken\", \"ClientId\") VALUES ('1', 'c1', 'client')");

        // SQLite's BINARY collation distinguishes trailing spaces, so 'client ' is a different value.
        await ExecuteAsync(connection,
            "INSERT INTO OpenIddictApplications (\"Id\", \"ConcurrencyToken\", \"ClientId\") VALUES ('2', 'c2', 'client ')");

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM OpenIddictApplications";

        Assert.Equal(2L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task InstallScript_CreatesExpectedIndexes()
    {
        await using var connection = await OpenAndInstallAsync();

        var indexes = await QueryNamesAsync(connection,
            "SELECT name FROM sqlite_master WHERE type = 'index' AND name NOT LIKE 'sqlite_%' ORDER BY name");

        Assert.Contains("IX_OpenIddictTokens_AuthorizationId", indexes);
        Assert.Contains("IX_OpenIddictTokens_Subject", indexes);
        Assert.Contains("IX_OpenIddictAuthorizations_CreationDate", indexes);
        Assert.Contains("IX_OpenIddictScopeResources_ResourceHash_ScopeId", indexes);
        Assert.Contains("IX_OpenIddictApplicationRedirectUris_UriHash_ApplicationId", indexes);
    }

    [Fact]
    public async Task InstallScript_IsNotIdempotent()
    {
        await using var connection = await OpenAndInstallAsync();

        // Reinstalling must fail; IF NOT EXISTS cannot be used to fake idempotency (design 10.3).
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteScriptAsync(connection, LoadScript()));
    }

    [Fact]
    public void InstallScript_UsesOnlyValidatedPlaceholders()
    {
        var template = DeploymentScript.Template("Sqlite");

        Assert.Contains("__PREFIX__", template, StringComparison.Ordinal);

        // A template is not a runnable script: the placeholder cannot survive into a deployment copy.
        Assert.Throws<ArgumentException>(() => OpenIddictStoreIdentifiers.RenderDeploymentScript(
            template, OpenIddictStoreSqliteDialect.Instance, "1bad"));
        Assert.Throws<ArgumentException>(() => OpenIddictStoreIdentifiers.RenderDeploymentScript(
            template, OpenIddictStoreSqliteDialect.Instance, "bad-prefix"));

        var rendered = DeploymentScript.Sqlite("Custom");

        Assert.Contains("CREATE TABLE \"CustomApplications\"", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("__PREFIX__", rendered, StringComparison.Ordinal);
    }

    /// <summary>
    /// The prefix is runtime configuration, so a non-default prefix must produce a complete, working schema.
    /// This is the case that a concrete script hard-coded to OpenIddict could not serve.
    /// </summary>
    [Fact]
    public async Task CustomPrefix_InstallsAndIsUsable()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true,
        }.ToString();

        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await ExecuteScriptAsync(connection, DeploymentScript.Sqlite("Oidc"));

            var tables = await QueryNamesAsync(connection,
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name");

            Assert.Contains("OidcApplications", tables);
            Assert.Contains("OidcSchemaVersions", tables);

            // Nothing may be left under the default prefix: the deployment copy is a complete replacement.
            Assert.DoesNotContain("OpenIddictApplications", tables);
        }

        var options = Options.Create(new OpenIddictStoreOptions { TablePrefix = "Oidc" });
        options.Value.Providers.Add(OpenIddictStoreProvider.Sqlite);

        var store = new OpenIddictStoreApplicationStore<OpenIddictStoreApplication>(
            options,
            new SqliteConnectionFactory(connectionString),
            OpenIddictStoreSqliteDialect.Instance,
            NullLogger<OpenIddictStoreApplicationStore<OpenIddictStoreApplication>>.Instance);

        // The store has to find the tables it was told to use, not the ones the script happened to create.
        var application = new OpenIddictStoreApplication { ClientId = "custom-prefix" };
        await store.CreateAsync(application, default);

        Assert.NotNull(await store.FindByClientIdAsync("custom-prefix", default));
    }

    private async Task<SqliteConnection> OpenAndInstallAsync()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true,
        }.ToString());

        await connection.OpenAsync();
        await ExecuteScriptAsync(connection, LoadScript());

        return connection;
    }

    private static async Task ExecuteScriptAsync(SqliteConnection connection, string script)
    {
        var statements = OpenIddictStoreIdentifiers.SplitStatements(script);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        foreach (var statement in statements)
        {
            // The script contains its own BEGIN/COMMIT; replace those transaction statements with an outer transaction to avoid nesting.
            if (statement.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase)
                || statement.StartsWith("COMMIT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> QueryNamesAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static string LoadScript() => DeploymentScript.Sqlite();
}

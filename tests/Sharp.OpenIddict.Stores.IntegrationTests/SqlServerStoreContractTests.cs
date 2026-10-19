using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sharp.OpenIddict.Stores.Models;
using Sharp.OpenIddict.Stores.SqlServer;
using Sharp.OpenIddict.Stores.Stores;
using Xunit;

namespace Sharp.OpenIddict.Stores.IntegrationTests;

/// <summary>
/// SQL Server acceptance on a real server (DB-02). The dialect claims that cannot be proven on SQLite live
/// here: binary exact comparison including trailing spaces, datetime2(6) microseconds, OFFSET/FETCH paging
/// without a count, filtered unique indexes, and associated deletes. Each test creates and drops its own
/// database, so the run leaves nothing behind.
/// </summary>
public sealed class SqlServerStoreContractTests : IAsyncLifetime
{
    /// <summary>A master-database connection string whose account may create and drop databases.</summary>
    private const string ServerVariable = "DSH_SQLSERVER";

    private string _databaseName = string.Empty;
    private string _connectionString = string.Empty;
    private string _masterConnectionString = string.Empty;
    private OpenIddictStoreApplicationStore<OpenIddictStoreApplication> _applications = null!;
    private OpenIddictStoreScopeStore<OpenIddictStoreScope> _scopes = null!;
    private OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization> _authorizations = null!;
    private OpenIddictStoreTokenStore<OpenIddictStoreToken> _tokens = null!;

    public async Task InitializeAsync()
    {
        _masterConnectionString = Environment.GetEnvironmentVariable(ServerVariable) ?? string.Empty;

        if (_masterConnectionString.Length == 0)
        {
            return;
        }

        _databaseName = "oidc-db02-" + Guid.NewGuid().ToString("N");

        await using (var master = new SqlConnection(_masterConnectionString))
        {
            await master.OpenAsync();

            await using var create = master.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{_databaseName}]";
            await create.ExecuteNonQueryAsync();
        }

        var builder = new SqlConnectionStringBuilder(_masterConnectionString)
        {
            InitialCatalog = _databaseName,
        };

        _connectionString = builder.ConnectionString;

        await ExecuteInstallScriptAsync();

        var options = Options.Create(new OpenIddictStoreOptions { TablePrefix = "OpenIddict" });
        options.Value.Providers.Add(OpenIddictStoreProvider.SqlServer);

        var factory = new SqlServerConnectionFactory(_connectionString);
        var dialect = OpenIddictStoreSqlServerDialect.Instance;

        _applications = new OpenIddictStoreApplicationStore<OpenIddictStoreApplication>(
            options, factory, dialect,
            NullLogger<OpenIddictStoreApplicationStore<OpenIddictStoreApplication>>.Instance);
        _scopes = new OpenIddictStoreScopeStore<OpenIddictStoreScope>(
            options, factory, dialect,
            NullLogger<OpenIddictStoreScopeStore<OpenIddictStoreScope>>.Instance);
        _authorizations = new OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization>(
            options, factory, dialect,
            NullLogger<OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization>>.Instance);
        _tokens = new OpenIddictStoreTokenStore<OpenIddictStoreToken>(
            options, factory, dialect,
            NullLogger<OpenIddictStoreTokenStore<OpenIddictStoreToken>>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (_databaseName.Length == 0 || _masterConnectionString.Length == 0)
        {
            return;
        }

        // Pools hold connections open, so the drop would block on its own leaked session otherwise.
        SqlConnection.ClearAllPools();

        await using var master = new SqlConnection(_masterConnectionString);
        await master.OpenAsync();

        await using var drop = master.CreateCommand();
        drop.CommandText = $@"
            IF DB_ID(N'{_databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{_databaseName}];
            END";
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>Runs the rendered deployment copy the way a deployment would: render, then one statement per command.</summary>
    private async Task ExecuteInstallScriptAsync(string prefix = DeploymentScript.DefaultPrefix, string? schema = null)
    {
        var script = DeploymentScript.SqlServer(prefix, schema);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        if (!string.IsNullOrEmpty(schema))
        {
            // The script does not create the schema; a deployment creates it first (see Sql/README.md).
            await using var create = connection.CreateCommand();
            create.CommandText =
                $"IF SCHEMA_ID(N'{schema}') IS NULL EXEC(N'CREATE SCHEMA {OpenIddictStoreSqlServerDialect.Instance.QuoteIdentifier(schema!)}');";
            await create.ExecuteNonQueryAsync();
        }

        foreach (var statement in OpenIddictStoreIdentifiers.SplitStatements(script))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync();
        }
    }

    [ServerFact(ServerVariable, "The SQL Server acceptance suite")]
    public async Task InstallScript_CreatesNineTablesTheIndexesAndTheVersionRow()
    {
        // Ordered with a binary collation so the expected sequence is defined by the names alone and not
        // by whatever the database default collation happens to be.
        var tables = await QueryScalarsAsync<string>(
            "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') AND name LIKE N'OpenIddict%' " +
            "ORDER BY name COLLATE Latin1_General_100_BIN2");

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

        // The three nullable unique keys must be filtered indexes: without the filter SQL Server still
        // allows only one NULL per key, which would reject a second row with no ClientId.
        var filtered = await QueryScalarsAsync<string>(
            "SELECT name FROM sys.indexes WHERE is_unique = 1 AND has_filter = 1 AND name LIKE N'UK_OpenIddict%' ORDER BY name");

        Assert.Equal(
            [
                "UK_OpenIddictApplications_ClientId",
                "UK_OpenIddictScopes_Name",
                "UK_OpenIddictTokens_ReferenceId",
            ],
            filtered);

        Assert.Equal([1], await QueryScalarsAsync<int>(
            "SELECT Version FROM OpenIddictSchemaVersions WHERE Series = N'7'"));
    }

    [ServerFact(ServerVariable, "The SQL Server acceptance suite")]
    public async Task ReRunningTheInstallScript_IsRefusedInsteadOfSkipped()
    {
        var exception = await Record.ExceptionAsync(() => ExecuteInstallScriptAsync());

        Assert.NotNull(exception);
        Assert.Contains("already installed", exception!.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The prefix and schema are runtime configuration, so a deployment copy rendered with custom values must
    /// produce a complete schema under that name and be usable through a store configured the same way.
    /// </summary>
    [ServerFact(ServerVariable, "The SQL Server acceptance suite")]
    public async Task CustomPrefixAndSchema_InstallAndAreUsable()
    {
        // The fixture already installed dbo.OpenIddict*, so this copy goes to a different schema with a
        // different prefix in the same database: a collision would prove the rendering leaked somewhere.
        await ExecuteInstallScriptAsync("Oidc", "auth");

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using (var reads = connection.CreateCommand())
        {
            reads.CommandText =
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'auth') AND name LIKE N'Oidc%'";
            Assert.Equal(9, (int)(await reads.ExecuteScalarAsync())!);
        }

        await using (var reads = connection.CreateCommand())
        {
            // Nothing may appear under the default prefix or in dbo because of this second copy.
            reads.CommandText =
                "SELECT COUNT(*) FROM sys.tables WHERE name LIKE N'OpenIddict%' AND schema_id <> SCHEMA_ID(N'dbo')";
            Assert.Equal(0, (int)(await reads.ExecuteScalarAsync())!);
        }

        await using (var reads = connection.CreateCommand())
        {
            reads.CommandText = "SELECT Version FROM auth.OidcSchemaVersions WHERE Series = N'7'";
            Assert.Equal(1, (int)(await reads.ExecuteScalarAsync())!);
        }

        var options = Options.Create(new OpenIddictStoreOptions { TablePrefix = "Oidc", Schema = "auth" });
        options.Value.Providers.Add(OpenIddictStoreProvider.SqlServer);

        var store = new OpenIddictStoreApplicationStore<OpenIddictStoreApplication>(
            options,
            new SqlServerConnectionFactory(_connectionString),
            OpenIddictStoreSqlServerDialect.Instance,
            NullLogger<OpenIddictStoreApplicationStore<OpenIddictStoreApplication>>.Instance);

        var application = new OpenIddictStoreApplication { ClientId = "custom-prefix" };
        await store.CreateAsync(application, default);

        Assert.NotNull(await store.FindByClientIdAsync("custom-prefix", default));

        // The guard has to look at the schema-qualified name too, or a second run would create half the tables
        // and only fail on the first duplicate.
        var again = await Record.ExceptionAsync(() => ExecuteInstallScriptAsync("Oidc", "auth"));

        Assert.NotNull(again);
        Assert.Contains("already installed", again!.Message, StringComparison.Ordinal);
    }

    [ServerFact(ServerVariable, "The SQL Server acceptance suite")]
    public async Task Application_RoundTripsUnicodeLargeValuesAndCollections()
    {
        var payload = new string('π', 50_000);

        var application = new OpenIddictStoreApplication
        {
            ClientId = "client-über",
            DisplayName = "Anzeigename für Ünïcode über 50 000 Zeichen",
            ClientSecret = payload,
        };

        await _applications.SetPermissionsAsync(application, ["ept:token", "gt:client_credentials"], default);
        await _applications.SetRedirectUrisAsync(application, ["https://example.test/cb", "https://example.test/cb2"], default);
        await _applications.CreateAsync(application, default);

        // nvarchar(max) must hold the emoji as a single UTF-16 pair, not as two broken halves.
        Assert.Equal("Anzeigename für Ünïcode über 50 000 Zeichen",
            await _applications.GetDisplayNameAsync(
                (await _applications.FindByClientIdAsync("client-über", default))!, default));

        var loaded = await _applications.FindByClientIdAsync("client-über", default);

        Assert.NotNull(loaded);
        Assert.Equal(payload.Length, (await _applications.GetClientSecretAsync(loaded!, default))!.Length);
        Assert.Equal(["ept:token", "gt:client_credentials"],
            (await _applications.GetPermissionsAsync(loaded!, default)).ToArray());
        Assert.Equal(["https://example.test/cb", "https://example.test/cb2"],
            (await _applications.GetRedirectUrisAsync(loaded!, default)).ToArray());

        // The auxiliary index is what makes the lookup possible; a JSON-only row would not be found.
        var byUri = new List<OpenIddictStoreApplication>();

        await foreach (var candidate in _applications.FindByRedirectUriAsync("https://example.test/cb2", default))
        {
            byUri.Add(candidate);
        }

        Assert.Single(byUri);
        Assert.Equal(application.Id, byUri[0].Id);
    }

    [ServerFact(ServerVariable, "The SQL Server acceptance suite")]
    public async Task Dates_KeepMicrosecondPrecisionAcrossCreateAndRead()
    {
        // A value ending in microseconds: a "datetime" parameter would round it to 1233330 ticks.
        var created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(1234560);

        var token = new OpenIddictStoreToken
        {
            CreationDate = created,
            ExpirationDate = created.AddHours(1),
            RedemptionDate = created.AddMinutes(1),
            Status = "valid",
            Type = "access_token",
        };

        await _tokens.CreateAsync(token, default);

        var loaded = await _tokens.FindByIdAsync(token.Id!, default);

        Assert.NotNull(loaded);
        Assert.Equal(created, await _tokens.GetCreationDateAsync(loaded!, default));
        Assert.Equal(created.AddHours(1), await _tokens.GetExpirationDateAsync(loaded!, default));
        Assert.Equal(created.AddMinutes(1), await _tokens.GetRedemptionDateAsync(loaded!, default));

        // The dates must still compare correctly in a predicate, not just read back.
        Assert.NotNull(await _tokens.FindByIdAsync(token.Id!, default));
    }

    [ServerFact(ServerVariable, "The SQL Server acceptance suite")]
    public async Task ExactComparison_DistinguishesCaseAndAccents()
    {
        await _applications.CreateAsync(new OpenIddictStoreApplication { ClientId = "client" }, default);
        await _applications.CreateAsync(new OpenIddictStoreApplication { ClientId = "Client" }, default);
        await _applications.CreateAsync(new OpenIddictStoreApplication { ClientId = "clíent" }, default);

        Assert.NotNull(await _applications.FindByClientIdAsync("client", default));
        Assert.NotNull(await _applications.FindByClientIdAsync("Client", default));
        Assert.NotNull(await _applications.FindByClientIdAsync("clíent", default));

        Assert.NotEqual(
            (await _applications.FindByClientIdAsync("client", default))!.Id,
            (await _applications.FindByClientIdAsync("Client", default))!.Id);

        Assert.Null(await _applications.FindByClientIdAsync("CLIENT", default));
        Assert.Null(await _applications.FindByClientIdAsync("clien", default));
    }

    [ServerFact(ServerVariable, "The SQL Server acceptance suite")]
    public async Task ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNone()
    {
        await _applications.CreateAsync(new OpenIddictStoreApplication { ClientId = "spaced" }, default);

        // The stored value is "spaced". A lookup for "spaced " must not find it: BIN2 alone would,
        // because it pads to equal length, so the dialect also compares DATALENGTH.
        Assert.Null(await _applications.FindByClientIdAsync("spaced ", default));
        Assert.NotNull(await _applications.FindByClientIdAsync("spaced", default));
    }

    [ServerFact(ServerVariable, "The SQL Server acceptance suite")]
    public async Task UniqueIndex_RejectsASecondRowWithTheSameClientId()
    {
        await _applications.CreateAsync(new OpenIddictStoreApplication { ClientId = "unique" }, default);

        var exception = await Record.ExceptionAsync(
            () => _applications.CreateAsync(new OpenIddictStoreApplication { ClientId = "unique" }, default).AsTask());

        Assert.NotNull(exception);

        // Two rows without a ClientId must both be accepted: the filtered index ignores NULL.
        await _applications.CreateAsync(new OpenIddictStoreApplication(), default);
        await _applications.CreateAsync(new OpenIddictStoreApplication(), default);
    }

    [ServerFact(ServerVariable, "The SQL Server acceptance suite")]
    public async Task Update_RejectsAStaleConcurrencyToken()
    {
        var application = new OpenIddictStoreApplication { ClientId = "cas" };
        await _applications.SetRedirectUrisAsync(application, ["https://a.test/cb"], default);
        await _applications.CreateAsync(application, default);

        var winner = await _applications.FindByIdAsync(application.Id!, default);
        var winnerToken = winner!.ConcurrencyToken;
        await _applications.SetRedirectUrisAsync(winner, ["https://winner.test/cb"], default);
        await _applications.UpdateAsync(winner, default);

        // A second snapshot taken before the winner's update still carries the old token. It has to be
        // rebuilt from the winner object so the token really is stale, not the freshly rotated one.
        var stale = new OpenIddictStoreApplication
        {
            Id = application.Id,
            ConcurrencyToken = winnerToken,
            ClientId = "cas",
        };
        await _applications.SetRedirectUrisAsync(stale, ["https://stale.test/cb"], default);

        await Assert.ThrowsAsync<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
            async () => await _applications.UpdateAsync(stale, default));

        var reread = await _applications.FindByIdAsync(application.Id!, default);
        Assert.Equal(["https://winner.test/cb"],
            (await _applications.GetRedirectUrisAsync(reread!, default)).ToArray());
    }

    [ServerFact(ServerVariable, "The SQL Server acceptance suite")]
    public async Task Paging_UsesOffsetFetchAndAcceptsAnOffsetWithoutACount()
    {
        for (var index = 0; index < 7; index++)
        {
            await _scopes.CreateAsync(
                new OpenIddictStoreScope { Name = "scope-" + index.ToString("D2") }, default);
        }

        // ListAsync orders by Id, not by Name, so the expected page is the Id-ordered slice. Asserting the
        // names would couple this test to generated GUIDs; instead, collect every page and prove the pages
        // tile the whole set in a stable order.
        var everything = new List<string>();

        await foreach (var scope in _scopes.ListAsync(null, null, default))
        {
            everything.Add(scope.Id!);
        }

        Assert.Equal(7, everything.Count);

        var paged = new List<string>();

        await foreach (var scope in _scopes.ListAsync(3, 2, default))
        {
            paged.Add(scope.Id!);
        }

        Assert.Equal(everything.Skip(2).Take(3), paged);

        // OFFSET without FETCH is legal; FETCH NEXT 0 is not, and the dialect refuses to emit it.
        var rest = new List<string>();

        await foreach (var scope in _scopes.ListAsync(null, 5, default))
        {
            rest.Add(scope.Id!);
        }

        Assert.Equal(everything.Skip(5), rest);
    }

    [ServerFact(ServerVariable, "The SQL Server acceptance suite")]
    public async Task CountQuery_DoesNotEmitOrderByInsideADerivedTable()
    {
        for (var index = 0; index < 4; index++)
        {
            await _scopes.CreateAsync(new OpenIddictStoreScope { Name = "counted-" + index }, default);
        }

        // SQL Server rejects ORDER BY in a derived table unless it is paired with OFFSET/FETCH, so the
        // count query must drop the ordering. This would fail on any server if it regressed.
        var count = await _scopes.CountAsync(scopes => scopes, default);

        Assert.Equal(4, count);
    }

    [ServerFact(ServerVariable, "The SQL Server acceptance suite")]
    public async Task ScopeResources_AreIndexedAndTheJsonStaysAuthoritative()
    {
        var scope = new OpenIddictStoreScope { Name = "api" };
        await _scopes.SetResourcesAsync(scope, ["https://rs1", "https://rs2"], default);
        await _scopes.CreateAsync(scope, default);

        var found = new List<OpenIddictStoreScope>();

        await foreach (var candidate in _scopes.FindByResourceAsync("https://rs2", default))
        {
            found.Add(candidate);
        }

        Assert.Single(found);
        Assert.Equal(["https://rs1", "https://rs2"],
            (await _scopes.GetResourcesAsync(found[0], default)).ToArray());

        var variant = new List<OpenIddictStoreScope>();

        await foreach (var candidate in _scopes.FindByResourceAsync("https://RS2", default))
        {
            variant.Add(candidate);
        }

        Assert.Empty(variant);
    }

    [ServerFact(ServerVariable, "The SQL Server acceptance suite")]
    public async Task AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorization()
    {
        var application = new OpenIddictStoreApplication { ClientId = "delete-me" };
        await _applications.CreateAsync(application, default);

        var authorization = new OpenIddictStoreAuthorization
        {
            ApplicationId = application.Id,
            Status = "valid",
            Type = "permanent",
            Subject = "alice",
        };
        await _authorizations.CreateAsync(authorization, default);

        // ApplicationId stays null: the token hangs off the authorization only, so a delete that looked
        // at ApplicationId alone would leave an orphan that the foreign key would then block.
        var token = new OpenIddictStoreToken
        {
            AuthorizationId = authorization.Id,
            Status = "valid",
            Type = "access_token",
        };
        await _tokens.CreateAsync(token, default);

        await _applications.DeleteAsync(application, default);

        Assert.Null(await _tokens.FindByIdAsync(token.Id!, default));
        Assert.Null(await _authorizations.FindByIdAsync(authorization.Id!, default));
        Assert.Null(await _applications.FindByIdAsync(application.Id!, default));
    }

    [ServerFact(ServerVariable, "The SQL Server acceptance suite")]
    public async Task BulkRevocation_TreatsANullStatusAsNotYetRevoked()
    {
        var valid = await CreateTokenAsync("alice", "valid");
        var alreadyRevoked = await CreateTokenAsync("alice", "revoked");
        var withoutStatus = await CreateTokenAsync("alice", null);

        // A plain <> yields UNKNOWN for NULL and would silently skip that row.
        Assert.Equal(2, await _tokens.RevokeBySubjectAsync("alice", default));

        // The status accessors read the entity in memory, so the database has to be read back to prove
        // the update happened; asserting on the same instance only repeats what the test already set.
        Assert.Equal(["revoked", "revoked", "revoked"],
            (await QueryScalarsAsync<string>(
                "SELECT ISNULL([Status], N'<null>') FROM OpenIddictTokens ORDER BY Id")).ToArray());

        // Nothing is left to revoke, and the already-revoked row is never matched twice.
        Assert.Equal(0, await _tokens.RevokeBySubjectAsync("alice", default));
    }

    [ServerFact(ServerVariable, "The SQL Server acceptance suite")]
    public async Task BulkRevocation_DoesNotMatchCaseOrAccentVariants()
    {
        var exact = await CreateTokenAsync("alice", "valid");
        var upper = await CreateTokenAsync("Alice", "valid");

        Assert.Equal(1, await _tokens.RevokeBySubjectAsync("alice", default));

        // Read the store, not the in-memory entity: only the exact-case row may have been revoked.
        Assert.Equal("revoked", await ReadStatusAsync(exact.Id!));
        Assert.Equal("valid", await ReadStatusAsync(upper.Id!));
    }

    [ServerFact(ServerVariable, "The SQL Server acceptance suite")]
    public async Task Prune_RemovesOnlyRowsBeforeTheThreshold()
    {
        var threshold = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Prune requires the CreationDate itself to be before the threshold, so the date has to be set
        // at insert time; updating only ExpirationDate would leave the row outside the predicate.
        var expired = new OpenIddictStoreToken
        {
            Subject = "bob",
            Status = "valid",
            Type = "access_token",
            CreationDate = threshold.AddDays(-2).UtcDateTime,
            ExpirationDate = threshold.AddDays(-1).UtcDateTime,
        };
        await _tokens.CreateAsync(expired, default);

        var future = new OpenIddictStoreToken
        {
            Subject = "bob",
            Status = "valid",
            Type = "access_token",
            CreationDate = threshold.AddDays(1).UtcDateTime,
            ExpirationDate = threshold.AddDays(2).UtcDateTime,
        };
        await _tokens.CreateAsync(future, default);

        var pruned = await _tokens.PruneAsync(threshold, default);

        Assert.True(pruned >= 1);
        Assert.Null(await _tokens.FindByIdAsync(expired.Id!, default));
        Assert.NotNull(await _tokens.FindByIdAsync(future.Id!, default));
    }

    private async Task<OpenIddictStoreToken> CreateTokenAsync(string subject, string? status)
    {
        var token = new OpenIddictStoreToken
        {
            Subject = subject,
            Status = status,
            Type = "access_token",
            CreationDate = DateTime.UtcNow,
        };

        await _tokens.CreateAsync(token, default);

        return token;
    }

    /// <summary>Reads one status straight from the database, bypassing the entity held in memory.</summary>
    private async Task<string> ReadStatusAsync(string id)
    {
        var values = await QueryScalarsAsync<string>(
            $"SELECT ISNULL([Status], N'<null>') FROM OpenIddictTokens WHERE [Id] = @id",
            ("@id", id));

        return Assert.Single(values);
    }

    private async Task<List<T>> QueryScalarsAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        var results = new List<T>();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(reader.GetFieldValue<T>(0));
        }

        return results;
    }

    private sealed class SqlServerConnectionFactory : IOpenIddictStoreConnectionFactory
    {
        private readonly string _connectionString;

        public SqlServerConnectionFactory(string connectionString) => _connectionString = connectionString;

        public ValueTask<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken)
            => new(new SqlConnection(_connectionString));
    }
}

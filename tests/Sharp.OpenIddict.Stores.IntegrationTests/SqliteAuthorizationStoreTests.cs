using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Models;
using Sharp.OpenIddict.Stores.Sqlite;
using Sharp.OpenIddict.Stores.Stores;
using Xunit;

namespace Sharp.OpenIddict.Stores.IntegrationTests;

/// <summary>
/// Authorization Store contract tests (real SQLite): create/read/update/delete, optional filtering,
/// RequiredScopes contains-all semantics, Scopes index, and exact comparison.
/// </summary>
public sealed class SqliteAuthorizationStoreTests : IAsyncLifetime
{
    private string _databasePath = string.Empty;
    private string _connectionString = string.Empty;
    private OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization> _store = null!;

    public async Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"oidc-auth-{Guid.NewGuid():N}.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true,
        }.ToString();

        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync();

            var script = DeploymentScript.Sqlite();

            foreach (var statement in OpenIddictStoreIdentifiers.SplitStatements(script))
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

        var options = Options.Create(new OpenIddictStoreOptions { TablePrefix = "OpenIddict" });
        options.Value.Providers.Add(OpenIddictStoreProvider.Sqlite);

        _store = new OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization>(
            options,
            new SqliteConnectionFactory(_connectionString),
            OpenIddictStoreSqliteDialect.Instance,
            NullLogger<OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization>>.Instance);
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
    public async Task CreateAndFind_RoundTripsAllColumns()
    {
        var application = await CreateApplicationAsync("app");

        var authorization = new OpenIddictStoreAuthorization
        {
            ApplicationId = application,
            Status = "valid",
            Subject = "alice",
            Type = "permanent",
            Properties = """{"k":1}""",
        };

        var created = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        await _store.SetCreationDateAsync(authorization, created, default);
        await _store.SetScopesAsync(authorization, ["api", "offline_access"], default);
        await _store.CreateAsync(authorization, default);

        var loaded = await _store.FindByIdAsync(authorization.Id!, default);

        Assert.NotNull(loaded);
        Assert.Equal(application, await _store.GetApplicationIdAsync(loaded!, default));
        Assert.Equal("valid", await _store.GetStatusAsync(loaded!, default));
        Assert.Equal("alice", await _store.GetSubjectAsync(loaded!, default));
        Assert.Equal("permanent", await _store.GetTypeAsync(loaded!, default));
        Assert.Equal(created, await _store.GetCreationDateAsync(loaded!, default));
        Assert.Equal(["api", "offline_access"], (await _store.GetScopesAsync(loaded!, default)).ToArray());
        Assert.Equal(1, (await _store.GetPropertiesAsync(loaded!, default))["k"].GetInt32());
    }

    [Fact]
    public async Task Find_OptionalFiltersAreSkippedWhenNullOrEmpty()
    {
        var application = await CreateApplicationAsync("app");

        await _store.CreateAsync(new OpenIddictStoreAuthorization
        {
            ApplicationId = application,
            Status = "valid",
            Subject = "alice",
            Type = "permanent",
        }, default);

        await _store.CreateAsync(new OpenIddictStoreAuthorization
        {
            ApplicationId = application,
            Status = "revoked",
            Subject = "bob",
            Type = "ad-hoc",
        }, default);

        // All null/empty strings: no filtering.
        Assert.Equal(2, (await ToListAsync(_store.FindAsync(null, null, null, null, null, default))).Count);
        Assert.Equal(2, (await ToListAsync(_store.FindAsync("", "", "", "", null, default))).Count);

        Assert.Single(await ToListAsync(_store.FindAsync("alice", null, null, null, null, default)));
        // Both belong to the same Application, so filtering by client returns both.
        Assert.Equal(2, (await ToListAsync(_store.FindAsync(null, application, null, null, null, default))).Count);
        Assert.Empty(await ToListAsync(_store.FindAsync(null, "missing", null, null, null, default)));
        Assert.Single(await ToListAsync(_store.FindAsync(null, null, "revoked", null, null, default)));
        Assert.Single(await ToListAsync(_store.FindAsync(null, null, null, "ad-hoc", null, default)));

        // Combined conditions intersect.
        Assert.Single(await ToListAsync(
            _store.FindAsync("alice", application, "valid", "permanent", null, default)));
        Assert.Empty(await ToListAsync(
            _store.FindAsync("alice", application, "revoked", null, null, default)));

        // Exact comparison: case and trailing-space variants do not match.
        Assert.Empty(await ToListAsync(_store.FindAsync("Alice", null, null, null, null, default)));
        Assert.Empty(await ToListAsync(_store.FindAsync("alice ", null, null, null, null, default)));
        Assert.Empty(await ToListAsync(_store.FindAsync(null, null, "Valid", null, null, default)));
    }

    [Fact]
    public async Task Find_RequiredScopesMustAllMatch()
    {
        var application = await CreateApplicationAsync("app");

        var both = new OpenIddictStoreAuthorization { ApplicationId = application, Status = "valid" };
        await _store.SetScopesAsync(both, ["api", "offline_access"], default);
        await _store.CreateAsync(both, default);

        var onlyApi = new OpenIddictStoreAuthorization { ApplicationId = application, Status = "valid" };
        await _store.SetScopesAsync(onlyApi, ["api"], default);
        await _store.CreateAsync(onlyApi, default);

        // Requesting two Scopes: only the row that has both is returned, not a row with either one.
        var required = await ToListAsync(_store.FindAsync(
            null, null, null, null, ["api", "offline_access"], default));
        Assert.Single(required);
        Assert.Equal(both.Id, required[0].Id);

        // Duplicate requested values are deduplicated first.
        var duplicated = await ToListAsync(_store.FindAsync(
            null, null, null, null, ["api", "api"], default));
        Assert.Equal(2, duplicated.Count);

        // An empty collection does not constrain Scope.
        Assert.Equal(2, (await ToListAsync(_store.FindAsync(
            null, null, null, null, ImmutableArray<string>.Empty, default))).Count);

        // Exact comparison: case and trailing-space variants do not match.
        Assert.Empty(await ToListAsync(_store.FindAsync(
            null, null, null, null, ["API"], default)));
        Assert.Empty(await ToListAsync(_store.FindAsync(
            null, null, null, null, ["api "], default)));
    }

    [Fact]
    public async Task FindByApplicationIdAndSubject_AreExact()
    {
        var application = await CreateApplicationAsync("app");

        var authorization = new OpenIddictStoreAuthorization
        {
            ApplicationId = application,
            Subject = "alice",
            Status = "valid",
        };
        await _store.CreateAsync(authorization, default);

        Assert.Single(await ToListAsync(_store.FindByApplicationIdAsync(application, default)));
        Assert.Single(await ToListAsync(_store.FindBySubjectAsync("alice", default)));
        Assert.Empty(await ToListAsync(_store.FindByApplicationIdAsync("missing", default)));
        Assert.Empty(await ToListAsync(_store.FindBySubjectAsync("Alice", default)));

        await Assert.ThrowsAsync<ArgumentException>(
            () => ToListAsync(_store.FindByApplicationIdAsync("", default)));
        await Assert.ThrowsAsync<ArgumentException>(
            () => ToListAsync(_store.FindBySubjectAsync(null!, default)));
    }

    [Fact]
    public async Task Update_RotatesTokenRebuildsScopesAndRejectsStale()
    {
        var application = await CreateApplicationAsync("app");

        var authorization = new OpenIddictStoreAuthorization
        {
            ApplicationId = application,
            Status = "valid",
            Subject = "alice",
        };
        await _store.SetScopesAsync(authorization, ["api"], default);
        await _store.CreateAsync(authorization, default);

        var original = authorization.ConcurrencyToken;
        var stale = await _store.FindByIdAsync(authorization.Id!, default);

        await _store.SetStatusAsync(authorization, "revoked", default);
        await _store.SetScopesAsync(authorization, ["offline_access"], default);
        await _store.UpdateAsync(authorization, default);

        Assert.NotEqual(original, authorization.ConcurrencyToken);
        Assert.Equal("revoked", await _store.GetStatusAsync(
            (await _store.FindByIdAsync(authorization.Id!, default))!, default));

        // The collection index has been rebuilt: the old Scope no longer matches.
        Assert.Empty(await ToListAsync(_store.FindAsync(null, null, null, null, ["api"], default)));
        Assert.Single(await ToListAsync(_store.FindAsync(null, null, null, null, ["offline_access"], default)));

        stale!.Subject = "stale";
        await Assert.ThrowsAsync<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
            () => _store.UpdateAsync(stale, default).AsTask());
    }

    [Fact]
    public async Task Delete_RemovesAuthorizationsTokensAndScopeRows()
    {
        var application = await CreateApplicationAsync("app");

        var authorization = new OpenIddictStoreAuthorization
        {
            ApplicationId = application,
            Status = "valid",
        };
        await _store.SetScopesAsync(authorization, ["api"], default);
        await _store.CreateAsync(authorization, default);

        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync();

            foreach (var sql in new[]
            {
                $"INSERT INTO OpenIddictTokens (Id, ConcurrencyToken, ApplicationId, AuthorizationId, Status) VALUES ('t1', 'ct', '{application}', '{authorization.Id}', 'valid')",
                $"INSERT INTO OpenIddictTokens (Id, ConcurrencyToken, AuthorizationId, Status) VALUES ('t2', 'ct', '{authorization.Id}', 'valid')",
            })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync();
            }
        }

        await _store.DeleteAsync(authorization, default);

        Assert.Null(await _store.FindByIdAsync(authorization.Id!, default));

        await using var verify = new SqliteConnection(_connectionString);
        await verify.OpenAsync();

        Assert.Equal(0L, await ScalarAsync(verify, "SELECT COUNT(*) FROM OpenIddictAuthorizations"));
        Assert.Equal(0L, await ScalarAsync(verify, "SELECT COUNT(*) FROM OpenIddictAuthorizationScopes"));
        Assert.Equal(0L, await ScalarAsync(verify, "SELECT COUNT(*) FROM OpenIddictTokens"));

        // The Application is retained.
        Assert.Equal(1L, await ScalarAsync(verify, "SELECT COUNT(*) FROM OpenIddictApplications"));
    }

    [Fact]
    public async Task Delete_RejectsStaleConcurrencyToken()
    {
        var authorization = new OpenIddictStoreAuthorization { Status = "valid" };
        await _store.CreateAsync(authorization, default);

        authorization.ConcurrencyToken = Guid.NewGuid().ToString();

        await Assert.ThrowsAsync<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
            () => _store.DeleteAsync(authorization, default).AsTask());
    }

    [Fact]
    public async Task CountAndList_BehaveAsDocumented()
    {
        var ids = new List<string>();
        for (var index = 0; index < 3; index++)
        {
            var authorization = new OpenIddictStoreAuthorization { Status = "valid" };
            await _store.CreateAsync(authorization, default);
            ids.Add(authorization.Id!);
        }

        ids.Sort(StringComparer.Ordinal);

        Assert.Equal(3L, await _store.CountAsync(default));

        var all = await ToListAsync(_store.ListAsync(null, null, default));
        Assert.Equal(ids, all.Select(item => item.Id));

        var page = await ToListAsync(_store.ListAsync(2, 1, default));
        Assert.Equal(ids.Skip(1).Take(2), page.Select(item => item.Id));

        Assert.Empty(await ToListAsync(_store.ListAsync(0, null, default)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ToListAsync(_store.ListAsync(-1, null, default)));
    }

    [Fact]
    public async Task UnimplementedMembers_AreRejectedExplicitly()
    {
        var authorization = new OpenIddictStoreAuthorization { Status = "valid", Subject = "alice" };
        await _store.CreateAsync(authorization, default);

        // Query callbacks (QUERY-01..03) are not implemented yet and must be rejected explicitly
        // rather than return fake results. Revocation and pruning are implemented and covered by
        // BulkRevocationContractTests and PruneContractTests.
        await Assert.ThrowsAsync<NotSupportedException>(
            () => _store.CountAsync<int>(query => query.Select(item => 1), default).AsTask());

        // Revocation is live: the seeded valid row is matched and counted.
        Assert.Equal(1L, await _store.RevokeBySubjectAsync("alice", default));

        // Pruning is live too: a valid authorization is not prunable, so the call reports zero.
        Assert.Equal(0L, await _store.PruneAsync(DateTimeOffset.UtcNow, default));
    }

    private async Task<string> CreateApplicationAsync(string clientId)
    {
        var application = new OpenIddictStoreApplication { ClientId = clientId };

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO OpenIddictApplications (Id, ConcurrencyToken, ClientId) VALUES (@id, @ct, @clientId)";
        command.Parameters.AddWithValue("@id", application.Id);
        command.Parameters.AddWithValue("@ct", application.ConcurrencyToken);
        command.Parameters.AddWithValue("@clientId", clientId);
        await command.ExecuteNonQueryAsync();

        return application.Id!;
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();

        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return (long)(await command.ExecuteScalarAsync())!;
    }
}

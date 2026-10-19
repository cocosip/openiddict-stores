using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
/// Collection index and CAS contract tests (design 7, design 16.1.1, design 16.2):
/// the digest only narrows candidates and the authoritative JSON decides identity, so a
/// forced digest collision, a value longer than 200 characters, case and trailing-space
/// variants must all behave exactly like the in-memory collection.
/// </summary>
public sealed class CollectionIndexContractTests : IAsyncLifetime
{
    private string _databasePath = string.Empty;
    private string _connectionString = string.Empty;
    private OpenIddictStoreApplicationStore<OpenIddictStoreApplication> _applications = null!;
    private OpenIddictStoreScopeStore<OpenIddictStoreScope> _scopes = null!;
    private OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization> _authorizations = null!;

    public async Task InitializeAsync()
    {
        _databasePath = TestDatabaseName.CreateFilePath("CollectionIndexContractTests");
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

        var factory = new SqliteConnectionFactory(_connectionString);
        var dialect = OpenIddictStoreSqliteDialect.Instance;

        _applications = new OpenIddictStoreApplicationStore<OpenIddictStoreApplication>(
            options, factory, dialect, NullLogger<OpenIddictStoreApplicationStore<OpenIddictStoreApplication>>.Instance);
        _scopes = new OpenIddictStoreScopeStore<OpenIddictStoreScope>(
            options, factory, dialect, NullLogger<OpenIddictStoreScopeStore<OpenIddictStoreScope>>.Instance);
        _authorizations = new OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization>(
            options, factory, dialect,
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
    public async Task ForcedDigestCollision_IsRejectedByOrdinalRecheck()
    {
        // Two different values deliberately share one digest row, simulating a SHA-256 collision.
        // The store must not return the entity whose JSON does not actually contain the value.
        var application = new OpenIddictStoreApplication { ClientId = "collision" };
        await _applications.SetRedirectUrisAsync(application, ["https://real/cb"], default);
        await _applications.CreateAsync(application, default);

        var other = new OpenIddictStoreApplication { ClientId = "other" };
        await _applications.SetRedirectUrisAsync(other, ["https://other/cb"], default);
        await _applications.CreateAsync(other, default);

        // Rewrite the index row for `other` so its digest equals the digest of "https://real/cb"
        // while the authoritative JSON still holds "https://other/cb".
        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE OpenIddictApplicationRedirectUris SET UriHash = @hash WHERE ApplicationId = @id";
            command.Parameters.Add("@hash", SqliteType.Blob).Value = Sha256("https://real/cb");
            command.Parameters.AddWithValue("@id", other.Id);
            await command.ExecuteNonQueryAsync();
        }

        var found = await ToListAsync(_applications.FindByRedirectUriAsync("https://real/cb", default));

        Assert.Single(found);
        Assert.Equal(application.Id, found[0].Id);
    }

    [Fact]
    public async Task ValueLongerThan200Characters_RoundTripsWithoutTruncation()
    {
        var longUri = "https://example.test/" + new string('a', 400);
        var application = new OpenIddictStoreApplication { ClientId = "long" };

        await _applications.SetRedirectUrisAsync(application, [longUri], default);
        await _applications.CreateAsync(application, default);

        var loaded = await _applications.FindByIdAsync(application.Id!, default);
        Assert.Equal([longUri], (await _applications.GetRedirectUrisAsync(loaded!, default)).ToArray());

        var found = await ToListAsync(_applications.FindByRedirectUriAsync(longUri, default));
        Assert.Single(found);
        Assert.Equal(longUri, (await _applications.GetRedirectUrisAsync(found[0], default))[0]);
    }

    [Fact]
    public async Task DuplicateValuesInCollection_AreStoredOnceButJsonKeepsOriginalOrder()
    {
        var application = new OpenIddictStoreApplication { ClientId = "dup" };
        await _applications.SetRedirectUrisAsync(
            application, ["https://b", "https://a", "https://b"], default);
        await _applications.CreateAsync(application, default);

        // The authoritative JSON keeps the caller's order and its duplicates.
        var loaded = await _applications.FindByIdAsync(application.Id!, default);
        Assert.Equal(["https://b", "https://a", "https://b"],
            (await _applications.GetRedirectUrisAsync(loaded!, default)).ToArray());

        // The auxiliary index holds one row per distinct value.
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        Assert.Equal(2L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM OpenIddictApplicationRedirectUris"));
    }

    [Fact]
    public async Task ResourceAndScopeIndexes_SurviveValueUpdates()
    {
        var scope = new OpenIddictStoreScope { Name = "s" };
        await _scopes.SetResourcesAsync(scope, ["https://one", "https://two"], default);
        await _scopes.CreateAsync(scope, default);

        Assert.Single(await ToListAsync(_scopes.FindByResourceAsync("https://one", default)));

        await _scopes.SetResourcesAsync(scope, ["https://three"], default);
        await _scopes.UpdateAsync(scope, default);

        Assert.Empty(await ToListAsync(_scopes.FindByResourceAsync("https://one", default)));
        Assert.Empty(await ToListAsync(_scopes.FindByResourceAsync("https://two", default)));
        Assert.Single(await ToListAsync(_scopes.FindByResourceAsync("https://three", default)));

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM OpenIddictScopeResources"));
    }

    [Fact]
    public async Task RequiredScopes_UseIndexAndIntersectAcrossChunks()
    {
        var application = new OpenIddictStoreApplication { ClientId = "app" };
        await _applications.CreateAsync(application, default);

        // Three scopes that together exceed the SQLite parameter budget when queried one by one.
        var all = Enumerable.Range(0, 1200).Select(index => "scope-" + index).ToList();

        var full = new OpenIddictStoreAuthorization { ApplicationId = application.Id, Status = "valid" };
        await _authorizations.SetScopesAsync(full, [.. all], default);
        await _authorizations.CreateAsync(full, default);

        var partial = new OpenIddictStoreAuthorization { ApplicationId = application.Id, Status = "valid" };
        await _authorizations.SetScopesAsync(partial, [.. all.Take(1199)], default);
        await _authorizations.CreateAsync(partial, default);

        // Requesting the whole set must match only the authorization that has every scope.
        var found = await ToListAsync(_authorizations.FindAsync(
            null, null, null, null, [.. all], default));

        Assert.Single(found);
        Assert.Equal(full.Id, found[0].Id);

        // Dropping the last scope makes the partial authorization eligible as well.
        var relaxed = await ToListAsync(_authorizations.FindAsync(
            null, null, null, null, [.. all.Take(1199)], default));

        Assert.Equal(2, relaxed.Count);
    }

    [Fact]
    public async Task Cas_RejectsCaseAndTrailingSpaceVariantsOfTheToken()
    {
        var scope = new OpenIddictStoreScope { Name = "cas" };
        await _scopes.CreateAsync(scope, default);

        // A concurrency token that differs only by case or a trailing space must not match.
        var original = scope.ConcurrencyToken;

        scope.ConcurrencyToken = original!.ToUpperInvariant();
        if (scope.ConcurrencyToken == original)
        {
            scope.ConcurrencyToken = original.ToLowerInvariant();
        }

        await Assert.ThrowsAsync<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
            () => _scopes.DeleteAsync(scope, default).AsTask());

        scope.ConcurrencyToken = original + " ";
        await Assert.ThrowsAsync<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
            () => _scopes.DeleteAsync(scope, default).AsTask());

        // The real token still works.
        scope.ConcurrencyToken = original;
        await _scopes.DeleteAsync(scope, default);
        Assert.Null(await _scopes.FindByIdAsync(scope.Id!, default));
    }

    [Fact]
    public async Task Cas_FailedUpdateLeavesIndexesAndJsonUntouched()
    {
        var scope = new OpenIddictStoreScope { Name = "rollback" };
        await _scopes.SetResourcesAsync(scope, ["https://keep"], default);
        await _scopes.CreateAsync(scope, default);

        var stale = await _scopes.FindByIdAsync(scope.Id!, default);
        Assert.NotNull(stale);

        // Rotate the real token so the stale snapshot can no longer win.
        await _scopes.SetDisplayNameAsync(scope, "winner", default);
        await _scopes.UpdateAsync(scope, default);

        // The stale snapshot also changes its resources; the whole transaction must roll back.
        await _scopes.SetResourcesAsync(stale!, ["https://lost"], default);

        await Assert.ThrowsAsync<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
            () => _scopes.UpdateAsync(stale!, default).AsTask());

        Assert.Single(await ToListAsync(_scopes.FindByResourceAsync("https://keep", default)));
        Assert.Empty(await ToListAsync(_scopes.FindByResourceAsync("https://lost", default)));
        Assert.Equal("winner", await _scopes.GetDisplayNameAsync(
            (await _scopes.FindByIdAsync(scope.Id!, default))!, default));
    }

    [Fact]
    public async Task CancellationDuringWrite_RollsBackCollectionIndex()
    {
        var scope = new OpenIddictStoreScope { Name = "cancel" };
        await _scopes.SetResourcesAsync(scope, ["https://before"], default);
        await _scopes.CreateAsync(scope, default);

        await _scopes.SetResourcesAsync(scope, ["https://after"], default);

        using var cancellation = new System.Threading.CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _scopes.UpdateAsync(scope, cancellation.Token).AsTask());

        // Nothing changed: the JSON still names the old value and the index still matches it.
        var loaded = await _scopes.FindByIdAsync(scope.Id!, default);
        Assert.Equal(["https://before"], (await _scopes.GetResourcesAsync(loaded!, default)).ToArray());
        Assert.Single(await ToListAsync(_scopes.FindByResourceAsync("https://before", default)));
        Assert.Empty(await ToListAsync(_scopes.FindByResourceAsync("https://after", default)));
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

    private static byte[] Sha256(string value)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();

        return sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value));
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return (long)(await command.ExecuteScalarAsync())!;
    }
}

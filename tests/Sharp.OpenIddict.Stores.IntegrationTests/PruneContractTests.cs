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
/// Dedicated prune contract (design 16.3). Pruning never reuses the ordinary delete path: it selects
/// by creation date, applies the per-object status/type/expiry conditions with NULL compensation,
/// keeps the NOT EXISTS guard inside the final DELETE, and runs in independently committed batches.
/// </summary>
public sealed class PruneContractTests : IAsyncLifetime
{
    private string _databasePath = string.Empty;
    private string _connectionString = string.Empty;
    private OpenIddictStoreTokenStore<OpenIddictStoreToken> _tokens = null!;
    private OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization> _authorizations = null!;

    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Old = Now.AddDays(-30);

    public async Task InitializeAsync()
    {
        _databasePath = TestDatabaseName.CreateFilePath("PruneContractTests");
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

        _tokens = new OpenIddictStoreTokenStore<OpenIddictStoreToken>(
            options, factory, dialect, NullLogger<OpenIddictStoreTokenStore<OpenIddictStoreToken>>.Instance);
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
    public async Task Token_WithNonValidStatusIsPruned()
    {
        var pruned = await CreateTokenAsync("revoked", Old);
        var kept = await CreateTokenAsync("valid", Old);

        Assert.Equal(1L, await _tokens.PruneAsync(Now, default));

        Assert.False(await ExistsAsync("OpenIddictTokens", pruned));
        Assert.True(await ExistsAsync("OpenIddictTokens", kept));
    }

    [Fact]
    public async Task Token_WithNullStatusCountsAsNotValid()
    {
        // A NULL status is neither inactive nor valid, so it qualifies. A plain `<>` would drop it.
        var pruned = await CreateTokenAsync(null, Old);
        var kept = await CreateTokenAsync("valid", Old);

        Assert.Equal(1L, await _tokens.PruneAsync(Now, default));

        Assert.False(await ExistsAsync("OpenIddictTokens", pruned));
        Assert.True(await ExistsAsync("OpenIddictTokens", kept));
    }

    [Fact]
    public async Task Token_WithInactiveStatusIsKept()
    {
        var inactive = await CreateTokenAsync("inactive", Old);

        Assert.Equal(0L, await _tokens.PruneAsync(Now, default));
        Assert.True(await ExistsAsync("OpenIddictTokens", inactive));
    }

    [Fact]
    public async Task Token_StatusComparisonIsExactForCaseAndTrailingSpace()
    {
        // "Valid" and "valid " are different statuses, so they are "not valid" and qualify.
        var upper = await CreateTokenAsync("Valid", Old);
        var padded = await CreateTokenAsync("valid ", Old);
        var realValid = await CreateTokenAsync("valid", Old);

        Assert.Equal(2L, await _tokens.PruneAsync(Now, default));

        Assert.False(await ExistsAsync("OpenIddictTokens", upper));
        Assert.False(await ExistsAsync("OpenIddictTokens", padded));
        Assert.True(await ExistsAsync("OpenIddictTokens", realValid));

        // "inactive " is likewise not the inactive status, so it is prunable.
        var paddedInactive = await CreateTokenAsync("inactive ", Old);
        Assert.Equal(1L, await _tokens.PruneAsync(Now, default));
        Assert.False(await ExistsAsync("OpenIddictTokens", paddedInactive));
    }

    [Fact]
    public async Task Token_IsNeverPrunedBeforeTheThreshold()
    {
        var fresh = await CreateTokenAsync("revoked", Now);
        var boundary = await CreateTokenAsync("revoked", Now.AddTicks(-1));
        var old = await CreateTokenAsync("revoked", Old);

        Assert.Equal(2L, await _tokens.PruneAsync(Now, default));

        Assert.True(await ExistsAsync("OpenIddictTokens", fresh));
        Assert.False(await ExistsAsync("OpenIddictTokens", boundary));
        Assert.False(await ExistsAsync("OpenIddictTokens", old));
    }

    [Fact]
    public async Task Token_WithNullCreationDateIsNeverPruned()
    {
        var undated = await CreateTokenAsync("revoked", null);

        Assert.Equal(0L, await _tokens.PruneAsync(Now, default));
        Assert.True(await ExistsAsync("OpenIddictTokens", undated));
    }

    [Fact]
    public async Task Token_WithNonValidAuthorizationIsPrunedEvenWhenStatusIsValid()
    {
        var authorization = await CreateAuthorizationAsync("revoked", Old);

        var pruned = await CreateTokenAsync("valid", Old, authorizationId: authorization);
        var kept = await CreateTokenAsync("valid", Old);

        Assert.Equal(1L, await _tokens.PruneAsync(Now, default));

        Assert.False(await ExistsAsync("OpenIddictTokens", pruned));
        Assert.True(await ExistsAsync("OpenIddictTokens", kept));
    }

    [Fact]
    public async Task Token_WithValidAuthorizationAndValidStatusIsKept()
    {
        var authorization = await CreateAuthorizationAsync("valid", Old);
        var token = await CreateTokenAsync("valid", Old, authorizationId: authorization);

        Assert.Equal(0L, await _tokens.PruneAsync(Now, default));
        Assert.True(await ExistsAsync("OpenIddictTokens", token));
    }

    [Fact]
    public async Task Token_ThatIsAlreadyExpiredIsPruned()
    {
        var expired = await CreateTokenAsync("valid", Old, expirationDate: Now.AddDays(-1));
        var live = await CreateTokenAsync("valid", Old, expirationDate: Now.AddDays(1));

        Assert.Equal(1L, await _tokens.PruneAsync(Now, default));

        Assert.False(await ExistsAsync("OpenIddictTokens", expired));
        Assert.True(await ExistsAsync("OpenIddictTokens", live));
    }

    [Fact]
    public async Task Token_PruneIsBatchedAtFiveHundredRows()
    {
        for (var index = 0; index < 1200; index++)
        {
            await CreateTokenAsync("revoked", Old);
        }

        // 1,200 candidates are removed in three batches of at most 500, all within one call.
        Assert.Equal(1200L, await _tokens.PruneAsync(Now, default));
        Assert.Equal(0L, await CountAsync("OpenIddictTokens"));
    }

    [Fact]
    public async Task Authorization_WithNonValidStatusAndNoTokensIsPruned()
    {
        var pruned = await CreateAuthorizationAsync("revoked", Old);
        var keptValid = await CreateAuthorizationAsync("valid", Old);

        Assert.Equal(1L, await _authorizations.PruneAsync(Now, default));

        Assert.False(await ExistsAsync("OpenIddictAuthorizations", pruned));
        Assert.True(await ExistsAsync("OpenIddictAuthorizations", keptValid));
    }

    [Fact]
    public async Task Authorization_AdHocTypeIsPrunedEvenWhenStatusIsValid()
    {
        var adHoc = await CreateAuthorizationAsync("valid", Old, type: "ad-hoc");
        var permanent = await CreateAuthorizationAsync("valid", Old, type: "permanent");

        Assert.Equal(1L, await _authorizations.PruneAsync(Now, default));

        Assert.False(await ExistsAsync("OpenIddictAuthorizations", adHoc));
        Assert.True(await ExistsAsync("OpenIddictAuthorizations", permanent));

        // The type comparison is exact: "Ad-Hoc" and "ad-hoc " are different values.
        var upper = await CreateAuthorizationAsync("valid", Old, type: "Ad-Hoc");
        var padded = await CreateAuthorizationAsync("valid", Old, type: "ad-hoc ");

        Assert.Equal(0L, await _authorizations.PruneAsync(Now, default));
        Assert.True(await ExistsAsync("OpenIddictAuthorizations", upper));
        Assert.True(await ExistsAsync("OpenIddictAuthorizations", padded));
    }

    [Fact]
    public async Task Authorization_WithAttachedTokensIsKept()
    {
        // The NOT EXISTS guard protects authorizations that still have tokens, so pruning never
        // cascades into token deletion the way the ordinary delete path does.
        var authorization = await CreateAuthorizationAsync("revoked", Old);
        var token = await CreateTokenAsync("valid", Old, authorizationId: authorization);

        Assert.Equal(0L, await _authorizations.PruneAsync(Now, default));

        Assert.True(await ExistsAsync("OpenIddictAuthorizations", authorization));
        Assert.True(await ExistsAsync("OpenIddictTokens", token));
    }

    [Fact]
    public async Task Authorization_BecomesPrunableOnceItsTokensAreGone()
    {
        var authorization = await CreateAuthorizationAsync("revoked", Old);
        var token = await CreateTokenAsync("valid", Old, authorizationId: authorization);

        Assert.Equal(0L, await _authorizations.PruneAsync(Now, default));

        // Removing the token through the ordinary delete path makes the authorization eligible.
        var loaded = await _tokens.FindByIdAsync(token, default);
        await _tokens.DeleteAsync(loaded!, default);

        Assert.Equal(1L, await _authorizations.PruneAsync(Now, default));
        Assert.False(await ExistsAsync("OpenIddictAuthorizations", authorization));
    }

    [Fact]
    public async Task Authorization_NullStatusCountsAsNotValid()
    {
        var pruned = await CreateAuthorizationAsync(null, Old);
        var kept = await CreateAuthorizationAsync("valid", Old);

        Assert.Equal(1L, await _authorizations.PruneAsync(Now, default));

        Assert.False(await ExistsAsync("OpenIddictAuthorizations", pruned));
        Assert.True(await ExistsAsync("OpenIddictAuthorizations", kept));
    }

    [Fact]
    public async Task PruneIsIdempotentAndReportsZeroWhenNothingQualifies()
    {
        await CreateTokenAsync("valid", Old);
        await CreateAuthorizationAsync("valid", Old);

        Assert.Equal(0L, await _tokens.PruneAsync(Now, default));
        Assert.Equal(0L, await _tokens.PruneAsync(Now, default));
        Assert.Equal(0L, await _authorizations.PruneAsync(Now, default));
        Assert.Equal(0L, await _authorizations.PruneAsync(Now, default));
    }

    [Fact]
    public async Task PrunePropagatesCancellation()
    {
        await CreateTokenAsync("revoked", Old);

        using var cancellation = new System.Threading.CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _tokens.PruneAsync(Now, cancellation.Token).AsTask());

        // The cancelled call must not have deleted anything.
        Assert.Equal(1L, await CountAsync("OpenIddictTokens"));
    }

    [Fact]
    public async Task PruneDoesNotUseTheOrdinaryDeleteAssociationMatrix()
    {
        // A token whose authorization is prunable but still referenced must survive the
        // authorization prune, proving the dedicated path never cascades.
        var authorization = await CreateAuthorizationAsync("revoked", Old);
        await CreateTokenAsync("valid", Old, authorizationId: authorization);

        Assert.Equal(0L, await _authorizations.PruneAsync(Now, default));

        Assert.Equal(1L, await CountAsync("OpenIddictTokens"));
        Assert.Equal(1L, await CountAsync("OpenIddictAuthorizations"));
    }

    private async Task<string> CreateTokenAsync(
        string? status, DateTimeOffset? creationDate,
        string? authorizationId = null, DateTimeOffset? expirationDate = null)
    {
        if (authorizationId is not null && !await ExistsAsync("OpenIddictAuthorizations", authorizationId))
        {
            await InsertAuthorizationRowAsync(authorizationId, "valid", Old, "permanent");
        }

        var token = new OpenIddictStoreToken
        {
            Status = status,
            AuthorizationId = authorizationId,
        };

        await _tokens.SetCreationDateAsync(token, creationDate, default);
        await _tokens.SetExpirationDateAsync(token, expirationDate, default);
        await _tokens.CreateAsync(token, default);

        return token.Id!;
    }

    private async Task<string> CreateAuthorizationAsync(
        string? status, DateTimeOffset? creationDate, string? type = null)
    {
        var authorization = new OpenIddictStoreAuthorization { Status = status, Type = type };

        await _authorizations.SetCreationDateAsync(authorization, creationDate, default);
        await _authorizations.CreateAsync(authorization, default);

        return authorization.Id!;
    }

    private async Task InsertAuthorizationRowAsync(
        string id, string? status, DateTimeOffset? creationDate, string? type)
    {
        var authorization = new OpenIddictStoreAuthorization
        {
            Id = id,
            Status = status,
            Type = type,
        };

        await _authorizations.SetCreationDateAsync(authorization, creationDate, default);
        await _authorizations.CreateAsync(authorization, default);
    }

    private async Task<bool> ExistsAsync(string table, string id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);

        return (long)(await command.ExecuteScalarAsync())! > 0;
    }

    private async Task<long> CountAsync(string table)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";

        return (long)(await command.ExecuteScalarAsync())!;
    }
}

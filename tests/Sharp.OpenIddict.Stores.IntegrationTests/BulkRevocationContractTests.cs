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
/// Bulk revocation contract (design 16.2.2). Each of the seven methods has its own predicate:
/// only the three <c>Token.RevokeBy*</c> methods exclude rows that are already exactly
/// <c>revoked</c>; the generic <c>RevokeAsync</c> and the <c>Authorization.RevokeBy*</c> methods
/// must keep matching such rows and count them again.
/// </summary>
public sealed class BulkRevocationContractTests : IAsyncLifetime
{
    private string _databasePath = string.Empty;
    private string _connectionString = string.Empty;
    private OpenIddictStoreTokenStore<OpenIddictStoreToken> _tokens = null!;
    private OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization> _authorizations = null!;

    public async Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"oidc-revoke-{Guid.NewGuid():N}.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true,
        }.ToString();

        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync();

            var script = OpenIddictStoreIdentifiers.ApplyPrefix(
                await File.ReadAllTextAsync(Path.Combine(RepositoryRoot,
                    "src", "Sharp.OpenIddict.Stores.Sqlite", "Sql", "7", "install", "v0001.sql")),
                "OpenIddict");

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
    public async Task TokenRevokeBySubject_CountsOneThenZero_AndLeavesAlreadyRevokedRows()
    {
        // Required example: one valid and one revoked token under the same subject.
        var valid = await CreateTokenAsync("alice", "valid");
        var revoked = await CreateTokenAsync("alice", "revoked");

        var revokedBefore = await ReadConcurrencyTokenAsync("OpenIddictTokens", revoked);

        Assert.Equal(1L, await _tokens.RevokeBySubjectAsync("alice", default));
        Assert.Equal(0L, await _tokens.RevokeBySubjectAsync("alice", default));

        // The valid row was marked and its token rotated.
        Assert.Equal("revoked", await ReadStatusAsync("OpenIddictTokens", valid));
        Assert.NotEqual(await ReadConcurrencyTokenAsync("OpenIddictTokens", valid), revokedBefore);

        // The already-revoked row was excluded: status unchanged and its token not rotated.
        Assert.Equal("revoked", await ReadStatusAsync("OpenIddictTokens", revoked));
        Assert.Equal(revokedBefore, await ReadConcurrencyTokenAsync("OpenIddictTokens", revoked));
    }

    [Fact]
    public async Task TokenRevokeGeneric_CountsBothRowsTwice()
    {
        // Required example: the generic method has no implicit revoked exclusion, so it returns 2 both times.
        var valid = await CreateTokenAsync("alice", "valid");
        var revoked = await CreateTokenAsync("alice", "revoked");

        Assert.Equal(2L, await _tokens.RevokeAsync("alice", null, null, null, default));

        var validAfterFirst = await ReadConcurrencyTokenAsync("OpenIddictTokens", valid);
        var revokedAfterFirst = await ReadConcurrencyTokenAsync("OpenIddictTokens", revoked);

        Assert.Equal(2L, await _tokens.RevokeAsync("alice", null, null, null, default));

        // Both rows were matched again, so both tokens rotated again.
        Assert.NotEqual(validAfterFirst, await ReadConcurrencyTokenAsync("OpenIddictTokens", valid));
        Assert.NotEqual(revokedAfterFirst, await ReadConcurrencyTokenAsync("OpenIddictTokens", revoked));
    }

    [Fact]
    public async Task TokenRevokeGeneric_WithExplicitStatusValid_CountsOneThenZero()
    {
        await CreateTokenAsync("alice", "valid");
        await CreateTokenAsync("alice", "revoked");

        Assert.Equal(1L, await _tokens.RevokeAsync("alice", null, "valid", null, default));
        Assert.Equal(0L, await _tokens.RevokeAsync("alice", null, "valid", null, default));
    }

    [Fact]
    public async Task AuthorizationRevokeBySubject_CountsTwoBothTimes()
    {
        // Required example: authorization revocation never excludes already revoked rows.
        var valid = await CreateAuthorizationAsync("alice", "valid");
        var revoked = await CreateAuthorizationAsync("alice", "revoked");

        Assert.Equal(2L, await _authorizations.RevokeBySubjectAsync("alice", default));

        var validAfterFirst = await ReadConcurrencyTokenAsync("OpenIddictAuthorizations", valid);
        var revokedAfterFirst = await ReadConcurrencyTokenAsync("OpenIddictAuthorizations", revoked);

        Assert.Equal(2L, await _authorizations.RevokeBySubjectAsync("alice", default));

        Assert.NotEqual(validAfterFirst, await ReadConcurrencyTokenAsync("OpenIddictAuthorizations", valid));
        Assert.NotEqual(revokedAfterFirst, await ReadConcurrencyTokenAsync("OpenIddictAuthorizations", revoked));
    }

    [Fact]
    public async Task NullStatusRows_AreMatchedByGenericAndTokenRevokeBy_ButNotByExplicitStatus()
    {
        var nullStatus = await CreateTokenAsync("alice", null);

        // A NULL status is "not revoked", so the Token.RevokeBy* exclusion keeps it.
        Assert.Equal(1L, await _tokens.RevokeBySubjectAsync("alice", default));
        Assert.Equal("revoked", await ReadStatusAsync("OpenIddictTokens", nullStatus));

        // An explicit status filter only matches that exact value, never NULL.
        var other = await CreateTokenAsync("bob", null);
        Assert.Equal(0L, await _tokens.RevokeAsync("bob", null, "valid", null, default));
        Assert.Null(await ReadStatusAsync("OpenIddictTokens", other));

        // Without a status filter the generic method does match the NULL row.
        Assert.Equal(1L, await _tokens.RevokeAsync("bob", null, null, null, default));
        Assert.Equal("revoked", await ReadStatusAsync("OpenIddictTokens", other));
    }

    [Fact]
    public async Task NullStatusRow_IsNotMatchedWhenTheSpecifiedKeyDiffers()
    {
        // The NULL branch must still be constrained by the other predicates of the method.
        var aliceToken = await CreateTokenAsync("alice", null);
        var bobToken = await CreateTokenAsync("bob", null);

        Assert.Equal(1L, await _tokens.RevokeBySubjectAsync("alice", default));

        Assert.Equal("revoked", await ReadStatusAsync("OpenIddictTokens", aliceToken));
        Assert.Null(await ReadStatusAsync("OpenIddictTokens", bobToken));

        // Same for the generic method with a non-matching subject.
        Assert.Equal(0L, await _tokens.RevokeAsync("carol", null, null, null, default));
        Assert.Null(await ReadStatusAsync("OpenIddictTokens", bobToken));
    }

    [Fact]
    public async Task SubjectComparison_IsExactForCaseAndTrailingSpace()
    {
        var lower = await CreateTokenAsync("alice", "valid");
        var upper = await CreateTokenAsync("Alice", "valid");
        var padded = await CreateTokenAsync("alice ", "valid");

        Assert.Equal(1L, await _tokens.RevokeBySubjectAsync("alice", default));

        Assert.Equal("revoked", await ReadStatusAsync("OpenIddictTokens", lower));
        Assert.Equal("valid", await ReadStatusAsync("OpenIddictTokens", upper));
        Assert.Equal("valid", await ReadStatusAsync("OpenIddictTokens", padded));

        Assert.Equal(1L, await _tokens.RevokeBySubjectAsync("Alice", default));
        Assert.Equal(1L, await _tokens.RevokeBySubjectAsync("alice ", default));
    }

    [Fact]
    public async Task RevokedWithTrailingSpace_IsNotTreatedAsAlreadyRevoked()
    {
        // "revoked " is a different status value: the exclusion must not match it.
        var padded = await CreateTokenAsync("alice", "revoked ");

        Assert.Equal(1L, await _tokens.RevokeBySubjectAsync("alice", default));
        Assert.Equal("revoked", await ReadStatusAsync("OpenIddictTokens", padded));

        // Now it really is revoked, so the exclusion applies.
        Assert.Equal(0L, await _tokens.RevokeBySubjectAsync("alice", default));
    }

    [Fact]
    public async Task StatusAndTypeFilters_AreExactForCaseAndTrailingSpace()
    {
        var valid = await CreateTokenAsync("alice", "valid", "access_token");
        var upper = await CreateTokenAsync("alice", "Valid", "access_token");
        var paddedType = await CreateTokenAsync("alice", "valid", "access_token ");

        Assert.Equal(1L, await _tokens.RevokeAsync("alice", null, "valid", "access_token", default));

        Assert.Equal("revoked", await ReadStatusAsync("OpenIddictTokens", valid));
        Assert.Equal("Valid", await ReadStatusAsync("OpenIddictTokens", upper));
        Assert.Equal("valid", await ReadStatusAsync("OpenIddictTokens", paddedType));
    }

    [Fact]
    public async Task RevokeByApplicationId_MatchesOnlyThatApplication()
    {
        var application = Guid.NewGuid().ToString();
        var otherApplication = Guid.NewGuid().ToString();

        var mine = await CreateTokenAsync("alice", "valid", applicationId: application);
        var theirs = await CreateTokenAsync("alice", "valid", applicationId: otherApplication);
        var unbound = await CreateTokenAsync("alice", "valid");

        Assert.Equal(1L, await _tokens.RevokeByApplicationIdAsync(application, default));

        Assert.Equal("revoked", await ReadStatusAsync("OpenIddictTokens", mine));
        Assert.Equal("valid", await ReadStatusAsync("OpenIddictTokens", theirs));
        Assert.Equal("valid", await ReadStatusAsync("OpenIddictTokens", unbound));
    }

    [Fact]
    public async Task RevokeByAuthorizationId_MatchesOnlyThatAuthorization()
    {
        var authorization = Guid.NewGuid().ToString();
        var otherAuthorization = Guid.NewGuid().ToString();

        var mine = await CreateTokenAsync("alice", "valid", authorizationId: authorization);
        var theirs = await CreateTokenAsync("alice", "valid", authorizationId: otherAuthorization);

        Assert.Equal(1L, await _tokens.RevokeByAuthorizationIdAsync(authorization, default));

        Assert.Equal("revoked", await ReadStatusAsync("OpenIddictTokens", mine));
        Assert.Equal("valid", await ReadStatusAsync("OpenIddictTokens", theirs));
    }

    [Fact]
    public async Task RevokeWithoutMatchingRows_ReturnsZeroAndChangesNothing()
    {
        var token = await CreateTokenAsync("alice", "valid");
        var before = await ReadConcurrencyTokenAsync("OpenIddictTokens", token);

        Assert.Equal(0L, await _tokens.RevokeBySubjectAsync("nobody", default));
        Assert.Equal(0L, await _tokens.RevokeByApplicationIdAsync("missing", default));
        Assert.Equal(0L, await _tokens.RevokeByAuthorizationIdAsync("missing", default));
        Assert.Equal(0L, await _tokens.RevokeAsync("nobody", null, null, null, default));
        Assert.Equal(0L, await _authorizations.RevokeBySubjectAsync("nobody", default));
        Assert.Equal(0L, await _authorizations.RevokeByApplicationIdAsync("missing", default));

        Assert.Equal("valid", await ReadStatusAsync("OpenIddictTokens", token));
        Assert.Equal(before, await ReadConcurrencyTokenAsync("OpenIddictTokens", token));
    }

    [Fact]
    public async Task RevokeBySpecifiedKey_RejectsNullOrEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _tokens.RevokeBySubjectAsync("", default).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(
            () => _tokens.RevokeByApplicationIdAsync(null!, default).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(
            () => _tokens.RevokeByAuthorizationIdAsync("", default).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(
            () => _authorizations.RevokeBySubjectAsync(null!, default).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(
            () => _authorizations.RevokeByApplicationIdAsync("", default).AsTask());
    }

    [Fact]
    public async Task RevokeDoesNotCrossStores()
    {
        // Revoking tokens must not touch authorizations and vice versa (design 16.2.2).
        var token = await CreateTokenAsync("alice", "valid");
        var authorization = await CreateAuthorizationAsync("alice", "valid");

        Assert.Equal(1L, await _tokens.RevokeBySubjectAsync("alice", default));
        Assert.Equal("valid", await ReadStatusAsync("OpenIddictAuthorizations", authorization));

        Assert.Equal(1L, await _authorizations.RevokeBySubjectAsync("alice", default));
        Assert.Equal("revoked", await ReadStatusAsync("OpenIddictAuthorizations", authorization));
        Assert.Equal("revoked", await ReadStatusAsync("OpenIddictTokens", token));
    }

    [Fact]
    public async Task RevokeRotatesTokensOnlyForMatchedRows()
    {
        var matched = await CreateTokenAsync("alice", "valid");
        var unmatched = await CreateTokenAsync("bob", "valid");

        var matchedBefore = await ReadConcurrencyTokenAsync("OpenIddictTokens", matched);
        var unmatchedBefore = await ReadConcurrencyTokenAsync("OpenIddictTokens", unmatched);

        Assert.Equal(1L, await _tokens.RevokeBySubjectAsync("alice", default));

        Assert.NotEqual(matchedBefore, await ReadConcurrencyTokenAsync("OpenIddictTokens", matched));
        Assert.Equal(unmatchedBefore, await ReadConcurrencyTokenAsync("OpenIddictTokens", unmatched));

        // A stale snapshot taken before the revocation can no longer update the row.
        var stale = new OpenIddictStoreToken
        {
            Id = matched,
            ConcurrencyToken = matchedBefore,
            Status = "valid",
        };

        await Assert.ThrowsAsync<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
            () => _tokens.UpdateAsync(stale, default).AsTask());
    }

    private async Task<string> CreateTokenAsync(
        string? subject, string? status, string? type = null,
        string? applicationId = null, string? authorizationId = null)
    {
        // Foreign keys require the referenced rows to exist before the token is inserted.
        if (applicationId is not null)
        {
            await InsertParentAsync("OpenIddictApplications", applicationId, "ClientId", "client-" + applicationId);
        }

        if (authorizationId is not null)
        {
            await InsertParentAsync("OpenIddictAuthorizations", authorizationId, "ApplicationId", applicationId);
        }

        var token = new OpenIddictStoreToken
        {
            Subject = subject,
            Status = status,
            Type = type,
            ApplicationId = applicationId,
            AuthorizationId = authorizationId,
        };

        await _tokens.CreateAsync(token, default);

        return token.Id!;
    }

    private async Task InsertParentAsync(string table, string id, string column, string? value)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {table} (Id, ConcurrencyToken, {column}) VALUES (@id, @ct, @value)";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@ct", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@value", (object?)value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string> CreateAuthorizationAsync(string? subject, string? status)
    {
        var authorization = new OpenIddictStoreAuthorization { Subject = subject, Status = status };

        await _authorizations.CreateAsync(authorization, default);

        return authorization.Id!;
    }

    private async Task<string?> ReadStatusAsync(string table, string id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Status FROM {table} WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);

        var value = await command.ExecuteScalarAsync();

        return value is DBNull ? null : (string?)value;
    }

    private async Task<string?> ReadConcurrencyTokenAsync(string table, string id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT ConcurrencyToken FROM {table} WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);

        var value = await command.ExecuteScalarAsync();

        return value is DBNull ? null : (string?)value;
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
            ?? throw new InvalidOperationException("Repository root (Sharp.OpenIddict.Stores.slnx) was not found.");
    }

    private sealed class SqliteConnectionFactory : IOpenIddictStoreConnectionFactory
    {
        private readonly string _connectionString;

        public SqliteConnectionFactory(string connectionString) => _connectionString = connectionString;

        public ValueTask<System.Data.Common.DbConnection> CreateConnectionAsync(
            System.Threading.CancellationToken cancellationToken)
            => new(new SqliteConnection(_connectionString));
    }
}

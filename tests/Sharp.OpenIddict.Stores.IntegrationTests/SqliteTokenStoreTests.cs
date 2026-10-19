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
/// Token Store contract tests (real SQLite): create/read/update/single delete, optional filtering, round trips
/// across the three date columns, and exact comparison (design 7/15/16.1).
/// </summary>
public sealed class SqliteTokenStoreTests : IAsyncLifetime
{
    private string _databasePath = string.Empty;
    private string _connectionString = string.Empty;
    private OpenIddictStoreTokenStore<OpenIddictStoreToken> _store = null!;

    public async Task InitializeAsync()
    {
        _databasePath = TestDatabaseName.CreateFilePath("SqliteTokenStoreTests");
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

        _store = new OpenIddictStoreTokenStore<OpenIddictStoreToken>(
            options,
            new SqliteConnectionFactory(_connectionString),
            OpenIddictStoreSqliteDialect.Instance,
            NullLogger<OpenIddictStoreTokenStore<OpenIddictStoreToken>>.Instance);
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
    public async Task CreateAndFind_RoundTripsAllColumnsAndDates()
    {
        var application = await CreateApplicationAsync("app");
        var authorization = await CreateAuthorizationAsync(application);

        var token = new OpenIddictStoreToken
        {
            ApplicationId = application,
            AuthorizationId = authorization,
            Payload = "payload",
            Properties = """{"k":2}""",
            ReferenceId = "ref-1",
            Status = "valid",
            Subject = "alice",
            Type = "access_token",
        };

        var created = new DateTimeOffset(2026, 10, 19, 12, 0, 0, 500, TimeSpan.Zero).AddTicks(1230);
        var expires = created.AddHours(1);
        var redeemed = created.AddMinutes(5);

        await _store.SetCreationDateAsync(token, created, default);
        await _store.SetExpirationDateAsync(token, expires, default);
        await _store.SetRedemptionDateAsync(token, redeemed, default);
        await _store.CreateAsync(token, default);

        var loaded = await _store.FindByIdAsync(token.Id!, default);

        Assert.NotNull(loaded);
        Assert.Equal(application, await _store.GetApplicationIdAsync(loaded!, default));
        Assert.Equal(authorization, await _store.GetAuthorizationIdAsync(loaded!, default));
        Assert.Equal("payload", await _store.GetPayloadAsync(loaded!, default));
        Assert.Equal("ref-1", await _store.GetReferenceIdAsync(loaded!, default));
        Assert.Equal("valid", await _store.GetStatusAsync(loaded!, default));
        Assert.Equal("alice", await _store.GetSubjectAsync(loaded!, default));
        Assert.Equal("access_token", await _store.GetTypeAsync(loaded!, default));
        Assert.Equal(2, (await _store.GetPropertiesAsync(loaded!, default))["k"].GetInt32());

        // Date round trips preserve microsecond precision and are interpreted as UTC.
        Assert.Equal(TruncateToMicroseconds(created), await _store.GetCreationDateAsync(loaded!, default));
        Assert.Equal(TruncateToMicroseconds(expires), await _store.GetExpirationDateAsync(loaded!, default));
        Assert.Equal(TruncateToMicroseconds(redeemed), await _store.GetRedemptionDateAsync(loaded!, default));
        Assert.Equal(TimeSpan.Zero, (await _store.GetCreationDateAsync(loaded!, default))!.Value.Offset);
    }

    [Fact]
    public async Task NullableForeignKeysAndDates_StayNull()
    {
        var token = new OpenIddictStoreToken { Status = "valid" };
        await _store.CreateAsync(token, default);

        var loaded = await _store.FindByIdAsync(token.Id!, default);

        Assert.Null(await _store.GetApplicationIdAsync(loaded!, default));
        Assert.Null(await _store.GetAuthorizationIdAsync(loaded!, default));
        Assert.Null(await _store.GetCreationDateAsync(loaded!, default));
        Assert.Null(await _store.GetExpirationDateAsync(loaded!, default));
        Assert.Null(await _store.GetRedemptionDateAsync(loaded!, default));
        Assert.Null(await _store.GetPayloadAsync(loaded!, default));
        Assert.Null(await _store.GetReferenceIdAsync(loaded!, default));
        Assert.Empty(await _store.GetPropertiesAsync(loaded!, default));
    }

    [Fact]
    public async Task Find_OptionalFiltersAndExactComparison()
    {
        var application = await CreateApplicationAsync("app");
        var authorization = await CreateAuthorizationAsync(application);

        await _store.CreateAsync(new OpenIddictStoreToken
        {
            ApplicationId = application,
            AuthorizationId = authorization,
            Status = "valid",
            Subject = "alice",
            Type = "access_token",
        }, default);

        await _store.CreateAsync(new OpenIddictStoreToken
        {
            ApplicationId = application,
            Status = "revoked",
            Subject = "bob",
            Type = "refresh_token",
        }, default);

        Assert.Equal(2, (await ToListAsync(_store.FindAsync(null, null, null, null, default))).Count);
        Assert.Equal(2, (await ToListAsync(_store.FindAsync("", "", "", "", default))).Count);

        Assert.Single(await ToListAsync(_store.FindAsync("alice", null, null, null, default)));
        // Both Tokens belong to the same Application, so filtering by client returns both.
        Assert.Equal(2, (await ToListAsync(_store.FindAsync(null, application, null, null, default))).Count);
        Assert.Empty(await ToListAsync(_store.FindAsync(null, "missing", null, null, default)));
        Assert.Single(await ToListAsync(_store.FindAsync(null, null, "revoked", null, default)));
        Assert.Single(await ToListAsync(_store.FindAsync(null, null, null, "refresh_token", default)));

        // Exact comparison: case and trailing-space variants do not match.
        Assert.Empty(await ToListAsync(_store.FindAsync("Alice", null, null, null, default)));
        Assert.Empty(await ToListAsync(_store.FindAsync("alice ", null, null, null, default)));
        Assert.Empty(await ToListAsync(_store.FindAsync(null, null, "revoked ", null, default)));
        Assert.Empty(await ToListAsync(_store.FindAsync(null, null, null, "ACCESS_TOKEN", default)));
    }

    [Fact]
    public async Task FindByForeignKeysAndReferenceId_AreExact()
    {
        var application = await CreateApplicationAsync("app");
        var authorization = await CreateAuthorizationAsync(application);

        var token = new OpenIddictStoreToken
        {
            ApplicationId = application,
            AuthorizationId = authorization,
            ReferenceId = "ref",
            Subject = "alice",
            Status = "valid",
        };
        await _store.CreateAsync(token, default);

        Assert.Single(await ToListAsync(_store.FindByApplicationIdAsync(application, default)));
        Assert.Single(await ToListAsync(_store.FindByAuthorizationIdAsync(authorization, default)));
        Assert.Single(await ToListAsync(_store.FindBySubjectAsync("alice", default)));
        Assert.NotNull(await _store.FindByReferenceIdAsync("ref", default));

        Assert.Null(await _store.FindByReferenceIdAsync("REF", default));
        Assert.Null(await _store.FindByReferenceIdAsync("ref ", default));
        Assert.Empty(await ToListAsync(_store.FindByApplicationIdAsync("missing", default)));

        await Assert.ThrowsAsync<ArgumentException>(() => _store.FindByReferenceIdAsync("", default).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(
            () => ToListAsync(_store.FindBySubjectAsync("", default)));
    }

    [Fact]
    public async Task Update_RotatesTokenAndRejectsStaleSnapshot()
    {
        var token = new OpenIddictStoreToken { Status = "valid", Subject = "alice" };
        await _store.CreateAsync(token, default);

        var original = token.ConcurrencyToken;
        var stale = await _store.FindByIdAsync(token.Id!, default);

        await _store.SetStatusAsync(token, "revoked", default);
        await _store.UpdateAsync(token, default);

        Assert.NotEqual(original, token.ConcurrencyToken);
        Assert.Equal("revoked", await _store.GetStatusAsync(
            (await _store.FindByIdAsync(token.Id!, default))!, default));

        stale!.Subject = "stale";
        await Assert.ThrowsAsync<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
            () => _store.UpdateAsync(stale, default).AsTask());
    }

    [Fact]
    public async Task Delete_RemovesOnlyThatToken()
    {
        var token = new OpenIddictStoreToken { Status = "valid" };
        var keep = new OpenIddictStoreToken { Status = "valid" };
        await _store.CreateAsync(token, default);
        await _store.CreateAsync(keep, default);

        await _store.DeleteAsync(token, default);

        Assert.Null(await _store.FindByIdAsync(token.Id!, default));
        Assert.NotNull(await _store.FindByIdAsync(keep.Id!, default));
        Assert.Equal(1L, await _store.CountAsync(default));
    }

    [Fact]
    public async Task Delete_RejectsStaleConcurrencyToken()
    {
        var token = new OpenIddictStoreToken { Status = "valid" };
        await _store.CreateAsync(token, default);

        token.ConcurrencyToken = Guid.NewGuid().ToString();

        await Assert.ThrowsAsync<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
            () => _store.DeleteAsync(token, default).AsTask());
    }

    [Fact]
    public async Task CountAndList_BehaveAsDocumented()
    {
        var ids = new List<string>();
        for (var index = 0; index < 3; index++)
        {
            var token = new OpenIddictStoreToken { Status = "valid" };
            await _store.CreateAsync(token, default);
            ids.Add(token.Id!);
        }

        ids.Sort(StringComparer.Ordinal);

        Assert.Equal(3L, await _store.CountAsync(default));

        var all = await ToListAsync(_store.ListAsync(null, null, default));
        Assert.Equal(ids, all.Select(item => item.Id));

        var tail = await ToListAsync(_store.ListAsync(null, 2, default));
        Assert.Equal(ids.Skip(2), tail.Select(item => item.Id));

        Assert.Empty(await ToListAsync(_store.ListAsync(0, null, default)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ToListAsync(_store.ListAsync(-1, null, default)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ToListAsync(_store.ListAsync(null, -1, default)));
    }

    [Fact]
    public async Task UnimplementedMembers_AreRejectedExplicitly()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => _store.CountAsync<int>(query => query.Select(item => 1), default).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(
            () => _store.GetAsync<int, int>((query, _) => query.Select(item => 1), 0, default).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(
            () => ToListAsync(_store.ListAsync<int, int>((query, _) => query.Select(item => 1), 0, default)));

        // Pruning is implemented and covered by PruneContractTests; with no rows it reports zero.
        Assert.Equal(0L, await _store.PruneAsync(DateTimeOffset.UtcNow, default));
    }

    [Fact]
    public async Task Instantiate_CreatesEntityWithGeneratedId()
    {
        var token = await _store.InstantiateAsync(default);

        Assert.NotNull(token);
        Assert.True(Guid.TryParse(token.Id, out _));
        Assert.NotEqual(token.Id, (await _store.InstantiateAsync(default)).Id);
    }

    [Fact]
    public async Task UniqueReferenceId_IsEnforced()
    {
        await _store.CreateAsync(new OpenIddictStoreToken { ReferenceId = "dup", Status = "valid" }, default);

        await Assert.ThrowsAsync<SqliteException>(
            () => _store.CreateAsync(
                new OpenIddictStoreToken { ReferenceId = "dup", Status = "valid" }, default).AsTask());
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
        => new(value.Ticks - (value.Ticks % 10), value.Offset);

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

    private async Task<string> CreateAuthorizationAsync(string applicationId)
    {
        var authorization = new OpenIddictStoreAuthorization
        {
            ApplicationId = applicationId,
            Status = "valid",
        };

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO OpenIddictAuthorizations (Id, ConcurrencyToken, ApplicationId, Status) " +
            "VALUES (@id, @ct, @applicationId, 'valid')";
        command.Parameters.AddWithValue("@id", authorization.Id);
        command.Parameters.AddWithValue("@ct", authorization.ConcurrencyToken);
        command.Parameters.AddWithValue("@applicationId", applicationId);
        await command.ExecuteNonQueryAsync();

        return authorization.Id!;
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
}

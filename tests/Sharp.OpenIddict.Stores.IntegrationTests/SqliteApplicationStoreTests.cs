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
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Models;
using Sharp.OpenIddict.Stores.Sqlite;
using Sharp.OpenIddict.Stores.Stores;
using Xunit;

namespace Sharp.OpenIddict.Stores.IntegrationTests;

/// <summary>
/// Application Store contract tests: run the shipped install script against a real temporary SQLite database,
/// covering create/read/update/delete, uniqueness, collection indexes, and exact string comparison (design
/// 7/15/16.1.1).
/// </summary>
public sealed class SqliteApplicationStoreTests : IAsyncLifetime
{
    private string _databasePath = string.Empty;
    private string _connectionString = string.Empty;
    private OpenIddictStoreApplicationStore<OpenIddictStoreApplication> _store = null!;

    public async Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"oidc-app-{Guid.NewGuid():N}.db");
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

        _store = new OpenIddictStoreApplicationStore<OpenIddictStoreApplication>(
            options, factory, OpenIddictStoreSqliteDialect.Instance, NullLogger<OpenIddictStoreApplicationStore<OpenIddictStoreApplication>>.Instance);
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
    public async Task CreateAndFind_RoundTripsAllScalarColumns()
    {
        var application = new OpenIddictStoreApplication
        {
            ApplicationType = "web",
            ClientId = "client-1",
            ClientSecret = "secret",
            ClientType = "public",
            ConsentType = "explicit",
            DisplayName = "Anzeigename für Ünïcode",
            Properties = """{"a":1}""",
        };

        await _store.CreateAsync(application, default);

        var loaded = await _store.FindByIdAsync(application.Id!, default);

        Assert.NotNull(loaded);
        Assert.Equal("web", await _store.GetApplicationTypeAsync(loaded!, default));
        Assert.Equal("client-1", await _store.GetClientIdAsync(loaded!, default));
        Assert.Equal("secret", await _store.GetClientSecretAsync(loaded!, default));
        Assert.Equal("public", await _store.GetClientTypeAsync(loaded!, default));
        Assert.Equal("explicit", await _store.GetConsentTypeAsync(loaded!, default));
        Assert.Equal("Anzeigename für Ünïcode", await _store.GetDisplayNameAsync(loaded!, default));
        Assert.Equal(1, (await _store.GetPropertiesAsync(loaded!, default))["a"].GetInt32());
    }

    [Fact]
    public async Task CreateAndFind_RoundTripsCollectionsAndJwks()
    {
        var application = new OpenIddictStoreApplication();
        var key = new JsonWebKey { Kty = "oct", Kid = "k1", K = "c2VjcmV0" };

        await _store.SetRedirectUrisAsync(application, ["https://a/cb", "https://a/cb "], default);
        await _store.SetPostLogoutRedirectUrisAsync(application, ["https://a/out"], default);
        await _store.SetPermissionsAsync(application, ["ept:token", "gt:client_credentials"], default);
        await _store.SetRequirementsAsync(application, ["ft:pkce"], default);
        await _store.SetDisplayNamesAsync(application,
            ImmutableDictionary.CreateRange<CultureInfo, string>(
            [
                new(CultureInfo.GetCultureInfo("en"), "Name"),
                new(CultureInfo.GetCultureInfo("zh-Hans"), "Nömé"),
            ]), default);
        await _store.SetSettingsAsync(application,
            ImmutableDictionary.CreateRange<string, string>(StringComparer.Ordinal, [new("K", "v")]), default);
        var jwks = new JsonWebKeySet();
        jwks.Keys.Add(key);
        await _store.SetJsonWebKeySetAsync(application, jwks, default);

        await _store.CreateAsync(application, default);

        var loaded = await _store.FindByIdAsync(application.Id!, default);

        Assert.NotNull(loaded);
        Assert.Equal(["https://a/cb", "https://a/cb "], (await _store.GetRedirectUrisAsync(loaded!, default)).ToArray());
        Assert.Equal(["https://a/out"], (await _store.GetPostLogoutRedirectUrisAsync(loaded!, default)).ToArray());
        Assert.Equal(["ept:token", "gt:client_credentials"], (await _store.GetPermissionsAsync(loaded!, default)).ToArray());
        Assert.Equal(["ft:pkce"], (await _store.GetRequirementsAsync(loaded!, default)).ToArray());

        var names = await _store.GetDisplayNamesAsync(loaded!, default);
        Assert.Equal("Name", names[CultureInfo.GetCultureInfo("en")]);
        Assert.Equal("Nömé", names[CultureInfo.GetCultureInfo("zh-Hans")]);

        Assert.Equal("v", (await _store.GetSettingsAsync(loaded!, default))["K"]);

        var set = await _store.GetJsonWebKeySetAsync(loaded!, default);
        Assert.NotNull(set);
        Assert.Single(set!.Keys);
        Assert.Equal("k1", set.Keys[0].Kid);
        Assert.Equal("oct", set.Keys[0].Kty);
    }

    [Fact]
    public async Task EmptyCollections_ReadBackAsEmpty()
    {
        var application = new OpenIddictStoreApplication();

        await _store.CreateAsync(application, default);

        var loaded = await _store.FindByIdAsync(application.Id!, default);

        Assert.Empty(await _store.GetRedirectUrisAsync(loaded!, default));
        Assert.Empty(await _store.GetPermissionsAsync(loaded!, default));
        Assert.Empty(await _store.GetDisplayNamesAsync(loaded!, default));
        Assert.Empty(await _store.GetPropertiesAsync(loaded!, default));
        Assert.Empty(await _store.GetSettingsAsync(loaded!, default));
        Assert.Null(await _store.GetJsonWebKeySetAsync(loaded!, default));
    }

    [Fact]
    public async Task FindByClientId_IsExactAndRejectsEmptyKey()
    {
        var application = new OpenIddictStoreApplication { ClientId = "client" };
        await _store.CreateAsync(application, default);

        Assert.NotNull(await _store.FindByClientIdAsync("client", default));

        // Design 16.1.1: case-sensitive and trailing-space-sensitive, so database collation must not cause a match.
        Assert.Null(await _store.FindByClientIdAsync("Client", default));
        Assert.Null(await _store.FindByClientIdAsync("client ", default));
        Assert.Null(await _store.FindByClientIdAsync("CLIENT", default));

        await Assert.ThrowsAsync<ArgumentException>(() => _store.FindByClientIdAsync("", default).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => _store.FindByIdAsync("", default).AsTask());
    }

    [Fact]
    public async Task DuplicateClientId_IsRejectedByUniqueIndex()
    {
        await _store.CreateAsync(new OpenIddictStoreApplication { ClientId = "dup" }, default);

        await Assert.ThrowsAsync<SqliteException>(
            () => _store.CreateAsync(new OpenIddictStoreApplication { ClientId = "dup" }, default).AsTask());
    }

    [Fact]
    public async Task FindByRedirectUri_UsesHashCandidatesAndOrdinalVerification()
    {
        var withUri = new OpenIddictStoreApplication { ClientId = "a" };
        await _store.SetRedirectUrisAsync(withUri, ["https://a/cb", "https://a/cb "], default);
        await _store.CreateAsync(withUri, default);

        var other = new OpenIddictStoreApplication { ClientId = "b" };
        await _store.SetRedirectUrisAsync(other, ["https://b/cb"], default);
        await _store.CreateAsync(other, default);

        var exact = await ToListAsync(_store.FindByRedirectUriAsync("https://a/cb", default));
        Assert.Single(exact);
        Assert.Equal("a", await _store.GetClientIdAsync(exact[0], default));

        // A trailing space makes a different collection value: only the row holding that exact value should match.
        var trailing = await ToListAsync(_store.FindByRedirectUriAsync("https://a/cb ", default));
        Assert.Single(trailing);
        Assert.Equal(withUri.Id, await _store.GetIdAsync(trailing[0], default));

        Assert.Empty(await ToListAsync(_store.FindByRedirectUriAsync("https://a/CB", default)));
        Assert.Empty(await ToListAsync(_store.FindByRedirectUriAsync("https://missing", default)));
    }

    [Fact]
    public async Task Update_RotatesConcurrencyTokenAndRejectsStaleSnapshot()
    {
        var application = new OpenIddictStoreApplication { ClientId = "c1", DisplayName = "first" };
        await _store.CreateAsync(application, default);

        var original = application.ConcurrencyToken;

        var stale = await _store.FindByIdAsync(application.Id!, default);
        Assert.NotNull(stale);

        application.DisplayName = "second";
        await _store.UpdateAsync(application, default);

        Assert.NotEqual(original, application.ConcurrencyToken);
        Assert.Equal("second", await _store.GetDisplayNameAsync(
            (await _store.FindByIdAsync(application.Id!, default))!, default));

        // Updating a stale snapshot with the old token must fail and throw the native concurrency exception.
        stale!.DisplayName = "stale";
        await Assert.ThrowsAsync<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
            () => _store.UpdateAsync(stale, default).AsTask());

        // After the failure the in-memory entity keeps the old token and the database value is unchanged.
        Assert.Equal("second", await _store.GetDisplayNameAsync(
            (await _store.FindByIdAsync(application.Id!, default))!, default));
    }

    [Fact]
    public async Task Update_RebuildsCollectionIndexes()
    {
        var application = new OpenIddictStoreApplication { ClientId = "c1" };
        await _store.SetRedirectUrisAsync(application, ["https://old/cb"], default);
        await _store.CreateAsync(application, default);

        await _store.SetRedirectUrisAsync(application, ["https://new/cb"], default);
        await _store.UpdateAsync(application, default);

        Assert.Empty(await ToListAsync(_store.FindByRedirectUriAsync("https://old/cb", default)));
        Assert.Single(await ToListAsync(_store.FindByRedirectUriAsync("https://new/cb", default)));
    }

    [Fact]
    public async Task Delete_RemovesApplicationAuthorizationsAndTokens()
    {
        var application = new OpenIddictStoreApplication { ClientId = "c1" };
        await _store.CreateAsync(application, default);

        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync();

            foreach (var sql in new[]
            {
                $"INSERT INTO OpenIddictAuthorizations (Id, ConcurrencyToken, ApplicationId, Status) VALUES ('a1', 'ct', '{application.Id}', 'valid')",
                $"INSERT INTO OpenIddictAuthorizationScopes (AuthorizationId, ScopeHash, Scope, Position) VALUES ('a1', x'00', 'api', 0)",
                $"INSERT INTO OpenIddictTokens (Id, ConcurrencyToken, ApplicationId, AuthorizationId, Status) VALUES ('t1', 'ct', '{application.Id}', 'a1', 'valid')",
                $"INSERT INTO OpenIddictTokens (Id, ConcurrencyToken, AuthorizationId, Status) VALUES ('t2', 'ct', 'a1', 'valid')",
            })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync();
            }
        }

        await _store.DeleteAsync(application, default);

        Assert.Null(await _store.FindByIdAsync(application.Id!, default));

        await using var verify = new SqliteConnection(_connectionString);
        await verify.OpenAsync();

        Assert.Equal(0L, await ScalarAsync(verify, "SELECT COUNT(*) FROM OpenIddictAuthorizations"));
        Assert.Equal(0L, await ScalarAsync(verify, "SELECT COUNT(*) FROM OpenIddictAuthorizationScopes"));
        Assert.Equal(0L, await ScalarAsync(verify, "SELECT COUNT(*) FROM OpenIddictTokens"));
    }

    [Fact]
    public async Task Delete_RejectsStaleConcurrencyToken()
    {
        var application = new OpenIddictStoreApplication { ClientId = "c1" };
        await _store.CreateAsync(application, default);

        application.ConcurrencyToken = Guid.NewGuid().ToString();

        await Assert.ThrowsAsync<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
            () => _store.DeleteAsync(application, default).AsTask());
    }

    [Fact]
    public async Task Count_ReturnsAllRows()
    {
        await _store.CreateAsync(new OpenIddictStoreApplication { ClientId = "c1" }, default);
        await _store.CreateAsync(new OpenIddictStoreApplication { ClientId = "c2" }, default);

        Assert.Equal(2L, await _store.CountAsync(default));
    }

    [Fact]
    public async Task List_OrdersByIdAndHonorsPaging()
    {
        var ids = new List<string>();
        for (var index = 0; index < 5; index++)
        {
            var application = new OpenIddictStoreApplication { ClientId = $"c{index}" };
            await _store.CreateAsync(application, default);
            ids.Add(application.Id!);
        }

        ids.Sort(StringComparer.Ordinal);

        var all = await ToListAsync(_store.ListAsync(null, null, default));
        Assert.Equal(ids, all.Select(item => item.Id));

        var page = await ToListAsync(_store.ListAsync(2, 1, default));
        Assert.Equal(ids.Skip(1).Take(2), page.Select(item => item.Id));

        var tail = await ToListAsync(_store.ListAsync(null, 3, default));
        Assert.Equal(ids.Skip(3), tail.Select(item => item.Id));

        Assert.Empty(await ToListAsync(_store.ListAsync(0, null, default)));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => ToListAsync(_store.ListAsync(-1, null, default)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => ToListAsync(_store.ListAsync(null, -1, default)));
    }

    [Fact]
    public async Task QueryCallbacks_AreRejectedExplicitly()
    {
        // Query callbacks are not implemented yet: they must be rejected explicitly instead of returning a full table load (design 9).
        await Assert.ThrowsAsync<NotSupportedException>(
            () => _store.CountAsync<int>(query => query.Select(item => 1), default).AsTask());

        await Assert.ThrowsAsync<NotSupportedException>(
            () => _store.GetAsync<int, int>((query, _) => query.Select(item => 1), 0, default).AsTask());

        await Assert.ThrowsAsync<NotSupportedException>(
            () => ToListAsync(_store.ListAsync<int, int>((query, _) => query.Select(item => 1), 0, default)));
    }

    [Fact]
    public async Task Instantiate_CreatesEntityWithGeneratedId()
    {
        var application = await _store.InstantiateAsync(default);

        Assert.NotNull(application);
        Assert.True(Guid.TryParse(application.Id, out _));
        Assert.NotEqual(application.Id, (await _store.InstantiateAsync(default)).Id);
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

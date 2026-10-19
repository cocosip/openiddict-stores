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
/// Scope Store contract tests: cover create/read/update/delete on a real temporary SQLite database, exact Name
/// comparison and uniqueness, the JSON authority of Resources, and the ScopeResources index (design
/// 7/15/16.1.1).
/// </summary>
public sealed class SqliteScopeStoreTests : IAsyncLifetime
{
    private string _databasePath = string.Empty;
    private string _connectionString = string.Empty;
    private OpenIddictStoreScopeStore<OpenIddictStoreScope> _store = null!;

    public async Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"oidc-scope-{Guid.NewGuid():N}.db");
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

        _store = new OpenIddictStoreScopeStore<OpenIddictStoreScope>(
            options,
            new SqliteConnectionFactory(_connectionString),
            OpenIddictStoreSqliteDialect.Instance,
            NullLogger<OpenIddictStoreScopeStore<OpenIddictStoreScope>>.Instance);
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
        var scope = new OpenIddictStoreScope
        {
            Name = "api",
            Description = "Béschrëibung",
            DisplayName = "Anzeige",
            Properties = """{"k":"v"}""",
        };

        await _store.SetDescriptionsAsync(scope,
            ImmutableDictionary.CreateRange<CultureInfo, string>(
                [new(CultureInfo.GetCultureInfo("zh-Hans"), "Beschreibung für Ünïcode")]), default);
        await _store.SetDisplayNamesAsync(scope,
            ImmutableDictionary.CreateRange<CultureInfo, string>(
                [new(CultureInfo.GetCultureInfo("en"), "Display")]), default);
        await _store.SetResourcesAsync(scope, ["https://rs1", "https://rs2"], default);

        await _store.CreateAsync(scope, default);

        var byId = await _store.FindByIdAsync(scope.Id!, default);
        var byName = await _store.FindByNameAsync("api", default);

        Assert.NotNull(byId);
        Assert.NotNull(byName);
        Assert.Equal(scope.Id, byId!.Id);
        Assert.Equal(scope.Id, byName!.Id);

        Assert.Equal("Béschrëibung", await _store.GetDescriptionAsync(byId, default));
        Assert.Equal("Anzeige", await _store.GetDisplayNameAsync(byId, default));
        Assert.Equal("api", await _store.GetNameAsync(byId, default));
        Assert.Equal("Beschreibung für Ünïcode", (await _store.GetDescriptionsAsync(byId, default))[CultureInfo.GetCultureInfo("zh-Hans")]);
        Assert.Equal("Display", (await _store.GetDisplayNamesAsync(byId, default))[CultureInfo.GetCultureInfo("en")]);
        Assert.Equal("v", (await _store.GetPropertiesAsync(byId, default))["k"].GetString());
        Assert.Equal(["https://rs1", "https://rs2"], (await _store.GetResourcesAsync(byId, default)).ToArray());
    }

    [Fact]
    public async Task EmptyCollections_ReadBackAsEmpty()
    {
        var scope = new OpenIddictStoreScope { Name = "empty" };
        await _store.CreateAsync(scope, default);

        var loaded = await _store.FindByNameAsync("empty", default);

        Assert.Empty(await _store.GetResourcesAsync(loaded!, default));
        Assert.Empty(await _store.GetDescriptionsAsync(loaded!, default));
        Assert.Empty(await _store.GetDisplayNamesAsync(loaded!, default));
        Assert.Empty(await _store.GetPropertiesAsync(loaded!, default));
    }

    [Fact]
    public async Task FindByName_IsExactAndRejectsEmptyKey()
    {
        await _store.CreateAsync(new OpenIddictStoreScope { Name = "api" }, default);

        Assert.NotNull(await _store.FindByNameAsync("api", default));
        Assert.Null(await _store.FindByNameAsync("Api", default));
        Assert.Null(await _store.FindByNameAsync("api ", default));

        await Assert.ThrowsAsync<ArgumentException>(() => _store.FindByNameAsync("", default).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => _store.FindByIdAsync(null!, default).AsTask());
    }

    [Fact]
    public async Task DuplicateName_IsRejectedByUniqueIndex()
    {
        await _store.CreateAsync(new OpenIddictStoreScope { Name = "dup" }, default);

        await Assert.ThrowsAsync<SqliteException>(
            () => _store.CreateAsync(new OpenIddictStoreScope { Name = "dup" }, default).AsTask());
    }

    [Fact]
    public async Task FindByResource_UsesIndexAndOrdinalVerification()
    {
        var withResource = new OpenIddictStoreScope { Name = "s1" };
        await _store.SetResourcesAsync(withResource, ["https://rs", "https://rs "], default);
        await _store.CreateAsync(withResource, default);

        var other = new OpenIddictStoreScope { Name = "s2" };
        await _store.SetResourcesAsync(other, ["https://other"], default);
        await _store.CreateAsync(other, default);

        var exact = await ToListAsync(_store.FindByResourceAsync("https://rs", default));
        Assert.Single(exact);
        Assert.Equal("s1", await _store.GetNameAsync(exact[0], default));

        // A trailing space makes a different collection element: only the row holding that exact value matches.
        var trailing = await ToListAsync(_store.FindByResourceAsync("https://rs ", default));
        Assert.Single(trailing);
        Assert.Equal(withResource.Id, await _store.GetIdAsync(trailing[0], default));

        Assert.Empty(await ToListAsync(_store.FindByResourceAsync("https://RS", default)));
        Assert.Empty(await ToListAsync(_store.FindByResourceAsync("https://missing", default)));

        await Assert.ThrowsAsync<ArgumentException>(
            () => ToListAsync(_store.FindByResourceAsync("", default)));
    }

    [Fact]
    public async Task FindByNames_EmptyListReturnsNothingAndFiltersExactly()
    {
        await _store.CreateAsync(new OpenIddictStoreScope { Name = "a" }, default);
        await _store.CreateAsync(new OpenIddictStoreScope { Name = "b" }, default);

        Assert.Empty(await ToListAsync(_store.FindByNamesAsync(ImmutableArray<string>.Empty, default)));
        Assert.Empty(await ToListAsync(_store.FindByNamesAsync(default, default)));

        var found = await ToListAsync(_store.FindByNamesAsync(["a", "b", "missing"], default));
        Assert.Equal(2, found.Count);

        // Case and trailing-space variants do not match.
        Assert.Empty(await ToListAsync(_store.FindByNamesAsync(["A"], default)));
        Assert.Empty(await ToListAsync(_store.FindByNamesAsync(["a "], default)));
    }

    [Fact]
    public async Task Update_RotatesTokenAndRejectsStaleSnapshot()
    {
        var scope = new OpenIddictStoreScope { Name = "s", DisplayName = "first" };
        await _store.SetResourcesAsync(scope, ["https://old"], default);
        await _store.CreateAsync(scope, default);

        var original = scope.ConcurrencyToken;
        var stale = await _store.FindByIdAsync(scope.Id!, default);

        await _store.SetDisplayNameAsync(scope, "second", default);
        await _store.SetResourcesAsync(scope, ["https://new"], default);
        await _store.UpdateAsync(scope, default);

        Assert.NotEqual(original, scope.ConcurrencyToken);
        Assert.Equal("second", await _store.GetDisplayNameAsync(
            (await _store.FindByIdAsync(scope.Id!, default))!, default));

        // The resource index has been rebuilt.
        Assert.Empty(await ToListAsync(_store.FindByResourceAsync("https://old", default)));
        Assert.Single(await ToListAsync(_store.FindByResourceAsync("https://new", default)));

        stale!.DisplayName = "stale";
        await Assert.ThrowsAsync<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
            () => _store.UpdateAsync(stale, default).AsTask());

        Assert.Equal("second", await _store.GetDisplayNameAsync(
            (await _store.FindByIdAsync(scope.Id!, default))!, default));
    }

    [Fact]
    public async Task Delete_RemovesScopeAndItsResourcesOnly()
    {
        var scope = new OpenIddictStoreScope { Name = "s1" };
        await _store.SetResourcesAsync(scope, ["https://rs"], default);
        await _store.CreateAsync(scope, default);

        var keep = new OpenIddictStoreScope { Name = "s2" };
        await _store.SetResourcesAsync(keep, ["https://keep"], default);
        await _store.CreateAsync(keep, default);

        await _store.DeleteAsync(scope, default);

        Assert.Null(await _store.FindByIdAsync(scope.Id!, default));
        Assert.NotNull(await _store.FindByIdAsync(keep.Id!, default));

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM OpenIddictScopeResources"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM OpenIddictScopes"));

        // Deleting a Scope does not affect independent entities outside Scope.Resources (this database has no other entities, so only counts are verified).
        Assert.Single(await ToListAsync(_store.FindByResourceAsync("https://keep", default)));
    }

    [Fact]
    public async Task Delete_RejectsStaleConcurrencyToken()
    {
        var scope = new OpenIddictStoreScope { Name = "s" };
        await _store.CreateAsync(scope, default);

        scope.ConcurrencyToken = Guid.NewGuid().ToString();

        await Assert.ThrowsAsync<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
            () => _store.DeleteAsync(scope, default).AsTask());
    }

    [Fact]
    public async Task CountAndList_BehaveAsDocumented()
    {
        var ids = new List<string>();
        for (var index = 0; index < 3; index++)
        {
            var scope = new OpenIddictStoreScope { Name = $"s{index}" };
            await _store.CreateAsync(scope, default);
            ids.Add(scope.Id!);
        }

        ids.Sort(StringComparer.Ordinal);

        Assert.Equal(3L, await _store.CountAsync(default));

        var all = await ToListAsync(_store.ListAsync(null, null, default));
        Assert.Equal(ids, all.Select(item => item.Id));

        var page = await ToListAsync(_store.ListAsync(1, 1, default));
        Assert.Equal(ids.Skip(1).Take(1), page.Select(item => item.Id));

        Assert.Empty(await ToListAsync(_store.ListAsync(0, null, default)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ToListAsync(_store.ListAsync(-1, null, default)));
    }

    [Fact]
    public async Task QueryCallbacks_AreRejectedExplicitly()
    {
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
        var scope = await _store.InstantiateAsync(default);

        Assert.NotNull(scope);
        Assert.True(Guid.TryParse(scope.Id, out _));
        Assert.NotEqual(scope.Id, (await _store.InstantiateAsync(default)).Id);
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

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Models;
using Sharp.OpenIddict.Stores.Stores;
using Xunit;

namespace Sharp.OpenIddict.Stores.IntegrationTests;

/// <summary>
/// End-to-end registration verification: from <c>UseStores</c> to reading an Application through the native
/// Manager, proving the Store is really wired into the OpenIddict DI pipeline (design 6).
/// </summary>
public sealed class ManagerIntegrationTests : IAsyncLifetime
{
    private string _databasePath = string.Empty;
    private string _connectionString = string.Empty;
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"oidc-manager-{Guid.NewGuid():N}.db");
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

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddOpenIddict().AddCore(options => options.UseStores(stores => stores
            .UseSqlite()
            .UseConnectionFactory(_ => new SqliteConnection(_connectionString))));

        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public async Task ApplicationStore_IsResolvableFromContainer()
    {
        await using var scope = _provider.CreateAsyncScope();

        var store = scope.ServiceProvider
            .GetRequiredService<IOpenIddictApplicationStore<OpenIddictStoreApplication>>();

        Assert.IsType<OpenIddictStoreApplicationStore<OpenIddictStoreApplication>>(store);
    }

    [Fact]
    public async Task ApplicationManager_CreatesAndFindsApplication()
    {
        await using var scope = _provider.CreateAsyncScope();

        var manager = scope.ServiceProvider
            .GetRequiredService<OpenIddictApplicationManager<OpenIddictStoreApplication>>();

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = "manager-client",
            ClientType = "public",
            DisplayName = "Manager créé",
        };
        descriptor.RedirectUris.Add(new Uri("https://client/cb"));

        var application = await manager.CreateAsync(descriptor, CancellationToken.None);

        Assert.NotNull(application);
        Assert.Equal("manager-client", await manager.GetClientIdAsync(application, CancellationToken.None));
        Assert.Equal("Manager créé", await manager.GetDisplayNameAsync(application, CancellationToken.None));
        Assert.Equal(1L, await manager.CountAsync(CancellationToken.None));

        var found = await manager.FindByClientIdAsync("manager-client", CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal(await manager.GetIdAsync(application, CancellationToken.None),
            await manager.GetIdAsync(found!, CancellationToken.None));

        // Exact comparison: neither case variants nor trailing-space variants may match (design 16.1.1).
        Assert.Null(await manager.FindByClientIdAsync("Manager-Client", CancellationToken.None));
        Assert.Null(await manager.FindByClientIdAsync("manager-client ", CancellationToken.None));
    }

    [Fact]
    public async Task ApplicationManager_UpdatesDescriptorAndRotatesToken()
    {
        await using var scope = _provider.CreateAsyncScope();

        var manager = scope.ServiceProvider
            .GetRequiredService<OpenIddictApplicationManager<OpenIddictStoreApplication>>();

        var application = await manager.CreateAsync(
            new OpenIddictApplicationDescriptor { ClientId = "c1", ClientType = "public", DisplayName = "before" }, CancellationToken.None);

        var descriptor = new OpenIddictApplicationDescriptor { ClientId = "c1", ClientType = "public", DisplayName = "after" };
        await manager.UpdateAsync(application, descriptor, CancellationToken.None);

        Assert.Equal("after", await manager.GetDisplayNameAsync(application, CancellationToken.None));
    }

    [Fact]
    public async Task NonGenericManager_WorksAgainstStore()
    {
        await using var scope = _provider.CreateAsyncScope();

        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        var application = await manager.CreateAsync(
            new OpenIddictApplicationDescriptor { ClientId = "nongeneric" }, CancellationToken.None);

        Assert.NotNull(application);
        Assert.Equal("nongeneric", await manager.GetClientIdAsync(application, CancellationToken.None));
        Assert.NotNull(await manager.FindByClientIdAsync("nongeneric", CancellationToken.None));
    }

    [Fact]
    public async Task ScopeManager_CreatesFindsAndUpdatesScope()
    {
        await using var scope = _provider.CreateAsyncScope();

        var manager = scope.ServiceProvider
            .GetRequiredService<OpenIddictScopeManager<OpenIddictStoreScope>>();

        var descriptor = new OpenIddictScopeDescriptor { Name = "api", DisplayName = "API" };
        descriptor.Resources.Add("https://rs");

        var created = await manager.CreateAsync(descriptor, CancellationToken.None);

        Assert.NotNull(created);
        Assert.Equal("api", await manager.GetNameAsync(created, CancellationToken.None));
        Assert.Equal(1L, await manager.CountAsync(CancellationToken.None));

        var found = await manager.FindByNameAsync("api", CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal(created.Id, found!.Id);

        // Lookup by resource name goes through ScopeResources hash candidates plus an ordinal re-check.
        var byResource = new List<OpenIddictStoreScope>();
        await foreach (var item in manager.FindByResourceAsync("https://rs", CancellationToken.None))
        {
            byResource.Add(item);
        }

        Assert.Single(byResource);
        Assert.Equal(created.Id, byResource[0].Id);

        // Exact comparison applies on the Manager path as well.
        Assert.Null(await manager.FindByNameAsync("API", CancellationToken.None));
    }

    [Fact]
    public async Task NonGenericScopeManager_WorksAgainstStore()
    {
        await using var scope = _provider.CreateAsyncScope();

        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        var created = await manager.CreateAsync(
            new OpenIddictScopeDescriptor { Name = "nongeneric" }, CancellationToken.None);

        Assert.NotNull(created);
        Assert.Equal("nongeneric", await manager.GetNameAsync(created, CancellationToken.None));
    }

    [Fact]
    public async Task AllFourStores_AreResolvableFromContainer()
    {
        await using var scope = _provider.CreateAsyncScope();

        Assert.IsType<OpenIddictStoreApplicationStore<OpenIddictStoreApplication>>(
            scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationStore<OpenIddictStoreApplication>>());
        Assert.IsType<OpenIddictStoreScopeStore<OpenIddictStoreScope>>(
            scope.ServiceProvider.GetRequiredService<IOpenIddictScopeStore<OpenIddictStoreScope>>());
        Assert.IsType<OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization>>(
            scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationStore<OpenIddictStoreAuthorization>>());
        Assert.IsType<OpenIddictStoreTokenStore<OpenIddictStoreToken>>(
            scope.ServiceProvider.GetRequiredService<IOpenIddictTokenStore<OpenIddictStoreToken>>());
    }

    [Fact]
    public async Task TokenManager_CreatesAndFindsToken()
    {
        await using var scope = _provider.CreateAsyncScope();

        var applicationManager = scope.ServiceProvider
            .GetRequiredService<OpenIddictApplicationManager<OpenIddictStoreApplication>>();
        var tokenManager = scope.ServiceProvider
            .GetRequiredService<OpenIddictTokenManager<OpenIddictStoreToken>>();

        var application = await applicationManager.CreateAsync(
            new OpenIddictApplicationDescriptor { ClientId = "token-client", ClientType = "public" },
            CancellationToken.None);

        var descriptor = new OpenIddictTokenDescriptor
        {
            ApplicationId = await applicationManager.GetIdAsync(application, CancellationToken.None),
            Status = "valid",
            Subject = "alice",
            Type = "access_token",
            Payload = "payload",
            CreationDate = DateTimeOffset.UtcNow,
            ExpirationDate = DateTimeOffset.UtcNow.AddHours(1),
        };

        var token = await tokenManager.CreateAsync(descriptor, CancellationToken.None);

        Assert.NotNull(token);
        Assert.Equal("alice", await tokenManager.GetSubjectAsync(token, CancellationToken.None));
        // Manager normalizes TokenType into the full URN; assert the value actually stored.
        Assert.Equal(OpenIddictConstants.TokenTypeIdentifiers.AccessToken, await tokenManager.GetTypeAsync(token, CancellationToken.None));
        Assert.Equal(1L, await tokenManager.CountAsync(CancellationToken.None));

        var found = await tokenManager.FindByIdAsync(
            await tokenManager.GetIdAsync(token, CancellationToken.None) ?? string.Empty, CancellationToken.None);
        Assert.NotNull(found);
    }

    [Fact]
    public async Task AuthorizationManager_CreatesAndFindsAuthorization()
    {
        await using var scope = _provider.CreateAsyncScope();

        var applicationManager = scope.ServiceProvider
            .GetRequiredService<OpenIddictApplicationManager<OpenIddictStoreApplication>>();
        var authorizationManager = scope.ServiceProvider
            .GetRequiredService<OpenIddictAuthorizationManager<OpenIddictStoreAuthorization>>();

        var application = await applicationManager.CreateAsync(
            new OpenIddictApplicationDescriptor { ClientId = "authz-client", ClientType = "public" },
            CancellationToken.None);

        var descriptor = new OpenIddictAuthorizationDescriptor
        {
            ApplicationId = await applicationManager.GetIdAsync(application, CancellationToken.None),
            Status = "valid",
            Subject = "alice",
            Type = "permanent",
            CreationDate = DateTimeOffset.UtcNow,
        };
        descriptor.Scopes.Add("api");

        var authorization = await authorizationManager.CreateAsync(descriptor, CancellationToken.None);

        Assert.NotNull(authorization);
        Assert.Equal("alice", await authorizationManager.GetSubjectAsync(authorization, CancellationToken.None));
        Assert.Equal(1L, await authorizationManager.CountAsync(CancellationToken.None));

        var found = await authorizationManager.FindBySubjectAsync("alice", CancellationToken.None)
            .ToListAsync();
        Assert.Single(found);
    }

    [Fact]
    public async Task TokenManager_RevokeBySubjectIsVisibleThroughANewScope()
    {
        var application = await CreateApplicationThroughManagerAsync("revoke-client");
        var applicationId = application.Id;

        await using (var scope = _provider.CreateAsyncScope())
        {
            var tokenManager = scope.ServiceProvider
                .GetRequiredService<OpenIddictTokenManager<OpenIddictStoreToken>>();

            foreach (var status in new[] { "valid", "revoked" })
            {
                await tokenManager.CreateAsync(new OpenIddictTokenDescriptor
                {
                    ApplicationId = applicationId,
                    Status = status,
                    Subject = "alice",
                    Type = OpenIddictConstants.TokenTypeIdentifiers.AccessToken,
                    CreationDate = DateTimeOffset.UtcNow,
                }, CancellationToken.None);
            }

            // Token.RevokeBy* excludes the already revoked row: only the valid one is counted.
            Assert.Equal(1L, await tokenManager.RevokeBySubjectAsync("alice", CancellationToken.None));
            Assert.Equal(0L, await tokenManager.RevokeBySubjectAsync("alice", CancellationToken.None));
        }

        // A brand new scope must observe the committed state, which proves the write really went
        // to the database rather than to a cached entity (design 16.2.2).
        await using var verifyScope = _provider.CreateAsyncScope();
        var verifyManager = verifyScope.ServiceProvider
            .GetRequiredService<OpenIddictTokenManager<OpenIddictStoreToken>>();

        var revoked = new List<OpenIddictStoreToken>();
        await foreach (var token in verifyManager.FindBySubjectAsync("alice", CancellationToken.None))
        {
            revoked.Add(token);
        }

        Assert.Equal(2, revoked.Count);
        Assert.All(revoked, token => Assert.Equal("revoked", token.Status));
    }

    [Fact]
    public async Task AuthorizationManager_RevokeBySubjectCountsAlreadyRevokedRows()
    {
        var application = await CreateApplicationThroughManagerAsync("authz-revoke-client");

        await using var scope = _provider.CreateAsyncScope();

        var manager = scope.ServiceProvider
            .GetRequiredService<OpenIddictAuthorizationManager<OpenIddictStoreAuthorization>>();

        foreach (var status in new[] { "valid", "revoked" })
        {
            await manager.CreateAsync(new OpenIddictAuthorizationDescriptor
            {
                ApplicationId = application.Id,
                Status = status,
                Subject = "alice",
                Type = "permanent",
                CreationDate = DateTimeOffset.UtcNow,
            }, CancellationToken.None);
        }

        // Authorization.RevokeBy* never excludes already revoked rows: 2 both times.
        Assert.Equal(2L, await manager.RevokeBySubjectAsync("alice", CancellationToken.None));
        Assert.Equal(2L, await manager.RevokeBySubjectAsync("alice", CancellationToken.None));
    }

    [Fact]
    public async Task TokenManager_PruneRemovesOnlyQualifyingTokens()
    {
        var application = await CreateApplicationThroughManagerAsync("prune-client");
        var old = DateTimeOffset.UtcNow.AddDays(-30);

        await using (var scope = _provider.CreateAsyncScope())
        {
            var tokenManager = scope.ServiceProvider
                .GetRequiredService<OpenIddictTokenManager<OpenIddictStoreToken>>();

            foreach (var status in new[] { "revoked", "valid" })
            {
                await tokenManager.CreateAsync(new OpenIddictTokenDescriptor
                {
                    ApplicationId = application.Id,
                    Status = status,
                    Subject = "alice",
                    Type = OpenIddictConstants.TokenTypeIdentifiers.AccessToken,
                    CreationDate = old,
                }, CancellationToken.None);
            }

            // Only the non-valid token qualifies; the valid one has no expiry and no revoked authorization.
            Assert.Equal(1L, await tokenManager.PruneAsync(DateTimeOffset.UtcNow, CancellationToken.None));
        }

        await using var verifyScope = _provider.CreateAsyncScope();
        var verifyManager = verifyScope.ServiceProvider
            .GetRequiredService<OpenIddictTokenManager<OpenIddictStoreToken>>();

        var remaining = new List<OpenIddictStoreToken>();
        await foreach (var token in verifyManager.ListAsync(null, null, CancellationToken.None))
        {
            remaining.Add(token);
        }

        Assert.Single(remaining);
        Assert.Equal("valid", remaining[0].Status);
    }

    [Fact]
    public async Task ApplicationManager_DeleteRemovesItsAuthorizationsAndTokens()
    {
        var application = await CreateApplicationThroughManagerAsync("cascade-client");
        string authorizationId;
        string tokenId;

        await using (var scope = _provider.CreateAsyncScope())
        {
            var authorizationManager = scope.ServiceProvider
                .GetRequiredService<OpenIddictAuthorizationManager<OpenIddictStoreAuthorization>>();
            var tokenManager = scope.ServiceProvider
                .GetRequiredService<OpenIddictTokenManager<OpenIddictStoreToken>>();

            var authorization = await authorizationManager.CreateAsync(new OpenIddictAuthorizationDescriptor
            {
                ApplicationId = application.Id,
                Status = "valid",
                Subject = "alice",
                Type = "permanent",
                CreationDate = DateTimeOffset.UtcNow,
            }, CancellationToken.None);

            authorizationId = authorization.Id!;

            // A token bound only through the authorization, with no ApplicationId of its own.
            var token = await tokenManager.CreateAsync(new OpenIddictTokenDescriptor
            {
                AuthorizationId = authorizationId,
                Status = "valid",
                Subject = "alice",
                Type = OpenIddictConstants.TokenTypeIdentifiers.AccessToken,
                CreationDate = DateTimeOffset.UtcNow,
            }, CancellationToken.None);

            tokenId = token.Id!;
        }

        await using (var scope = _provider.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider
                .GetRequiredService<OpenIddictApplicationManager<OpenIddictStoreApplication>>();

            var loaded = await manager.FindByIdAsync(application.Id!, CancellationToken.None);
            Assert.NotNull(loaded);
            await manager.DeleteAsync(loaded!, CancellationToken.None);
        }

        await using var verifyScope = _provider.CreateAsyncScope();

        Assert.Null(await verifyScope.ServiceProvider
            .GetRequiredService<IOpenIddictApplicationManager>()
            .FindByIdAsync(application.Id!, CancellationToken.None));
        Assert.Null(await verifyScope.ServiceProvider
            .GetRequiredService<IOpenIddictAuthorizationManager>()
            .FindByIdAsync(authorizationId, CancellationToken.None));
        Assert.Null(await verifyScope.ServiceProvider
            .GetRequiredService<IOpenIddictTokenManager>()
            .FindByIdAsync(tokenId, CancellationToken.None));
    }

    private async Task<OpenIddictStoreApplication> CreateApplicationThroughManagerAsync(string clientId)
    {
        await using var scope = _provider.CreateAsyncScope();

        var manager = scope.ServiceProvider
            .GetRequiredService<OpenIddictApplicationManager<OpenIddictStoreApplication>>();

        return await manager.CreateAsync(
            new OpenIddictApplicationDescriptor { ClientId = clientId, ClientType = "public" },
            CancellationToken.None);
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
            ?? throw new InvalidOperationException("Could not locate the repository root (Sharp.OpenIddict.Stores.slnx).");
    }
}

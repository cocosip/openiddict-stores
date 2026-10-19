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
/// Ordinary delete association matrix (design 16.2.1) and its race behaviour (LIFE-02).
/// Deleting an Application removes A and T and nothing outside that set; deleting an
/// Authorization removes every token pointing at it and never the authorizations those tokens
/// also reference. A failed root CAS must roll the whole cascade back.
/// </summary>
public sealed class DeleteAssociationContractTests : IAsyncLifetime
{
    private string _databasePath = string.Empty;
    private string _connectionString = string.Empty;
    private OpenIddictStoreApplicationStore<OpenIddictStoreApplication> _applications = null!;
    private OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization> _authorizations = null!;
    private OpenIddictStoreTokenStore<OpenIddictStoreToken> _tokens = null!;

    public async Task InitializeAsync()
    {
        _databasePath = TestDatabaseName.CreateFilePath("DeleteAssociationContractTests");
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
        _authorizations = new OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization>(
            options, factory, dialect,
            NullLogger<OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization>>.Instance);
        _tokens = new OpenIddictStoreTokenStore<OpenIddictStoreToken>(
            options, factory, dialect, NullLogger<OpenIddictStoreTokenStore<OpenIddictStoreToken>>.Instance);
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
    public async Task DeleteApplication_RemovesItsAuthorizationsTokensAndAuxiliaryRows()
    {
        var (application, otherApplication) = await CreateTwoApplicationsAsync();

        var ownAuthorization = await CreateAuthorizationAsync(application, "alice");
        var foreignAuthorization = await CreateAuthorizationAsync(otherApplication, "alice");

        // A token bound to this application through its authorization, with no ApplicationId of its own.
        var indirect = await CreateTokenAsync(applicationId: null, authorizationId: ownAuthorization);
        // A token whose ApplicationId is this application but whose authorization belongs elsewhere.
        var direct = await CreateTokenAsync(applicationId: application, authorizationId: foreignAuthorization);
        // A token that belongs entirely to the other application.
        var untouched = await CreateTokenAsync(applicationId: otherApplication, authorizationId: foreignAuthorization);

        await _applications.SetRedirectUrisAsync(
            (await _applications.FindByIdAsync(application, default))!, ["https://a/cb"], default);
        await _applications.UpdateAsync((await _applications.FindByIdAsync(application, default))!, default);

        var loaded = await _applications.FindByIdAsync(application, default);
        await _applications.DeleteAsync(loaded!, default);

        // A and T are gone.
        Assert.False(await ExistsAsync("OpenIddictApplications", application));
        Assert.False(await ExistsAsync("OpenIddictAuthorizations", ownAuthorization));
        Assert.False(await ExistsAsync("OpenIddictTokens", indirect));
        Assert.False(await ExistsAsync("OpenIddictTokens", direct));

        // Everything outside the set survives, including the authorization the direct token referenced.
        Assert.True(await ExistsAsync("OpenIddictApplications", otherApplication));
        Assert.True(await ExistsAsync("OpenIddictAuthorizations", foreignAuthorization));
        Assert.True(await ExistsAsync("OpenIddictTokens", untouched));

        Assert.Equal(0L, await CountAsync("OpenIddictApplicationRedirectUris"));

        // The other application's authorization keeps its own scope row.
        Assert.Equal(1L, await CountAsync("OpenIddictAuthorizationScopes"));
    }

    [Fact]
    public async Task DeleteApplication_KeepsTheAuthorizationOfATokenBoundToAnotherApplication()
    {
        // The token is removed because its ApplicationId matches, but the authorization it points at
        // belongs to another application and must survive (design 16.2.1).
        var (application, otherApplication) = await CreateTwoApplicationsAsync();

        var foreignAuthorization = await CreateAuthorizationAsync(otherApplication, "alice");
        var token = await CreateTokenAsync(applicationId: application, authorizationId: foreignAuthorization);

        var loaded = await _applications.FindByIdAsync(application, default);
        await _applications.DeleteAsync(loaded!, default);

        Assert.False(await ExistsAsync("OpenIddictTokens", token));
        Assert.True(await ExistsAsync("OpenIddictAuthorizations", foreignAuthorization));
        Assert.True(await ExistsAsync("OpenIddictApplications", otherApplication));
    }

    [Fact]
    public async Task DeleteApplication_WithNullApplicationIdTokenUnderItsAuthorization_RemovesIt()
    {
        // Required protocol scenario: A and T valid, T.AuthorizationId = A, T.ApplicationId = NULL.
        var (application, _) = await CreateTwoApplicationsAsync();
        var authorization = await CreateAuthorizationAsync(application, "alice");
        var token = await CreateTokenAsync(applicationId: null, authorizationId: authorization);

        var loaded = await _applications.FindByIdAsync(application, default);
        await _applications.DeleteAsync(loaded!, default);

        Assert.False(await ExistsAsync("OpenIddictTokens", token));
    }

    [Fact]
    public async Task DeleteApplication_DoesNotDeleteATokenReferencingItsAuthorizationAlongsideAnother()
    {
        // A token that also references another authorization is still in T (its AuthorizationId is in A),
        // so it is removed; the other authorization is untouched. This pins the union semantics.
        var (application, otherApplication) = await CreateTwoApplicationsAsync();
        var ownAuthorization = await CreateAuthorizationAsync(application, "alice");
        var otherAuthorization = await CreateAuthorizationAsync(otherApplication, "bob");

        var token = await CreateTokenAsync(applicationId: otherApplication, authorizationId: ownAuthorization);

        var loaded = await _applications.FindByIdAsync(application, default);
        await _applications.DeleteAsync(loaded!, default);

        Assert.False(await ExistsAsync("OpenIddictTokens", token));
        Assert.True(await ExistsAsync("OpenIddictAuthorizations", otherAuthorization));
    }

    [Fact]
    public async Task DeleteApplication_RejectsStaleTokenAndRollsBackEverything()
    {
        var (application, _) = await CreateTwoApplicationsAsync();
        var authorization = await CreateAuthorizationAsync(application, "alice");
        var token = await CreateTokenAsync(applicationId: null, authorizationId: authorization);

        var loaded = await _applications.FindByIdAsync(application, default);
        Assert.NotNull(loaded);

        // Change the stored token so the snapshot is stale.
        loaded!.ConcurrencyToken = Guid.NewGuid().ToString();

        await Assert.ThrowsAsync<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
            () => _applications.DeleteAsync(loaded, default).AsTask());

        // Nothing was deleted: the cascade must roll back as a whole.
        Assert.True(await ExistsAsync("OpenIddictApplications", application));
        Assert.True(await ExistsAsync("OpenIddictAuthorizations", authorization));
        Assert.True(await ExistsAsync("OpenIddictTokens", token));
    }

    [Fact]
    public async Task DeleteApplication_RejectsCaseAndTrailingSpaceTokenVariants()
    {
        var (application, _) = await CreateTwoApplicationsAsync();
        var authorization = await CreateAuthorizationAsync(application, "alice");

        var loaded = await _applications.FindByIdAsync(application, default);
        var original = loaded!.ConcurrencyToken!;

        foreach (var variant in new[] { original.ToUpperInvariant(), original + " " })
        {
            if (variant == original)
            {
                continue;
            }

            loaded.ConcurrencyToken = variant;

            await Assert.ThrowsAsync<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
                () => _applications.DeleteAsync(loaded, default).AsTask());

            Assert.True(await ExistsAsync("OpenIddictApplications", application));
            Assert.True(await ExistsAsync("OpenIddictAuthorizations", authorization));
        }
    }

    [Fact]
    public async Task DeleteAuthorization_RemovesAllItsTokensRegardlessOfTheirApplication()
    {
        var (application, otherApplication) = await CreateTwoApplicationsAsync();
        var authorization = await CreateAuthorizationAsync(application, "alice");
        var unrelatedAuthorization = await CreateAuthorizationAsync(otherApplication, "bob");

        var noApplication = await CreateTokenAsync(applicationId: null, authorizationId: authorization);
        var otherApplicationToken = await CreateTokenAsync(
            applicationId: otherApplication, authorizationId: authorization);
        var unrelatedToken = await CreateTokenAsync(
            applicationId: otherApplication, authorizationId: unrelatedAuthorization);

        var loaded = await _authorizations.FindByIdAsync(authorization, default);
        await _authorizations.DeleteAsync(loaded!, default);

        Assert.False(await ExistsAsync("OpenIddictAuthorizations", authorization));
        Assert.False(await ExistsAsync("OpenIddictTokens", noApplication));
        Assert.False(await ExistsAsync("OpenIddictTokens", otherApplicationToken));

        // The other authorization and its own token survive; both applications survive.
        Assert.True(await ExistsAsync("OpenIddictAuthorizations", unrelatedAuthorization));
        Assert.True(await ExistsAsync("OpenIddictTokens", unrelatedToken));
        Assert.True(await ExistsAsync("OpenIddictApplications", application));
        Assert.True(await ExistsAsync("OpenIddictApplications", otherApplication));
    }

    [Fact]
    public async Task DeleteAuthorization_DoesNotDeleteOtherAuthorizationsReferencedByItsTokens()
    {
        // The deleted authorization's token also references a second authorization; only the deleted
        // authorization goes away, never the other one (design 16.2.1: no reverse expansion).
        var (application, otherApplication) = await CreateTwoApplicationsAsync();
        var authorization = await CreateAuthorizationAsync(application, "alice");
        var otherAuthorization = await CreateAuthorizationAsync(otherApplication, "bob");

        var token = await CreateTokenAsync(applicationId: otherApplication, authorizationId: authorization);

        var loaded = await _authorizations.FindByIdAsync(authorization, default);
        await _authorizations.DeleteAsync(loaded!, default);

        Assert.False(await ExistsAsync("OpenIddictTokens", token));
        Assert.True(await ExistsAsync("OpenIddictAuthorizations", otherAuthorization));
    }

    [Fact]
    public async Task DeleteAuthorization_RejectsStaleTokenAndRollsBackTokens()
    {
        var (application, _) = await CreateTwoApplicationsAsync();
        var authorization = await CreateAuthorizationAsync(application, "alice");
        var token = await CreateTokenAsync(applicationId: null, authorizationId: authorization);

        var loaded = await _authorizations.FindByIdAsync(authorization, default);
        loaded!.ConcurrencyToken = Guid.NewGuid().ToString();

        await Assert.ThrowsAsync<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
            () => _authorizations.DeleteAsync(loaded, default).AsTask());

        Assert.True(await ExistsAsync("OpenIddictAuthorizations", authorization));
        Assert.True(await ExistsAsync("OpenIddictTokens", token));
    }

    [Fact]
    public async Task StaleSnapshotUpdate_AfterCascade_ThrowsConcurrencyException()
    {
        // The design requires that a snapshot taken before the delete cannot be written back.
        var (application, _) = await CreateTwoApplicationsAsync();
        var authorization = await CreateAuthorizationAsync(application, "alice");

        var staleApplication = await _applications.FindByIdAsync(application, default);
        var staleAuthorization = await _authorizations.FindByIdAsync(authorization, default);

        await _applications.DeleteAsync(staleApplication!, default);

        await Assert.ThrowsAsync<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
            () => _applications.UpdateAsync(staleApplication!, default).AsTask());

        await Assert.ThrowsAsync<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
            () => _authorizations.UpdateAsync(staleAuthorization!, default).AsTask());
    }

    [Fact]
    public async Task DeleteApplication_LeavesTheOtherApplicationsAuxiliaryRowsIntact()
    {
        var (application, otherApplication) = await CreateTwoApplicationsAsync();

        var other = await _applications.FindByIdAsync(otherApplication, default);
        await _applications.SetRedirectUrisAsync(other!, ["https://keep/cb"], default);
        await _applications.UpdateAsync(other!, default);

        var loaded = await _applications.FindByIdAsync(application, default);
        await _applications.DeleteAsync(loaded!, default);

        var kept = await _applications.FindByIdAsync(otherApplication, default);
        Assert.NotNull(kept);
        Assert.Equal(["https://keep/cb"],
            (await _applications.GetRedirectUrisAsync(kept!, default)).ToArray());
        Assert.Single(await ToListAsync(_applications.FindByRedirectUriAsync("https://keep/cb", default)));
    }

    [Fact]
    public async Task DeleteApplication_WithNoRelatedRowsStillSucceeds()
    {
        var (application, _) = await CreateTwoApplicationsAsync();

        var loaded = await _applications.FindByIdAsync(application, default);
        await _applications.DeleteAsync(loaded!, default);

        Assert.False(await ExistsAsync("OpenIddictApplications", application));
    }

    [Fact]
    public async Task DeleteTokenAndScope_DoNotTouchRelatedRows()
    {
        var (application, _) = await CreateTwoApplicationsAsync();
        var authorization = await CreateAuthorizationAsync(application, "alice");
        var token = await CreateTokenAsync(applicationId: application, authorizationId: authorization);

        var loadedToken = await _tokens.FindByIdAsync(token, default);
        await _tokens.DeleteAsync(loadedToken!, default);

        Assert.False(await ExistsAsync("OpenIddictTokens", token));
        Assert.True(await ExistsAsync("OpenIddictAuthorizations", authorization));
        Assert.True(await ExistsAsync("OpenIddictApplications", application));
    }

    private async Task<(string Application, string OtherApplication)> CreateTwoApplicationsAsync()
    {
        var application = new OpenIddictStoreApplication { ClientId = "app-" + Guid.NewGuid().ToString("N") };
        var other = new OpenIddictStoreApplication { ClientId = "app-" + Guid.NewGuid().ToString("N") };

        await _applications.CreateAsync(application, default);
        await _applications.CreateAsync(other, default);

        return (application.Id!, other.Id!);
    }

    private async Task<string> CreateAuthorizationAsync(string? applicationId, string? subject)
    {
        var authorization = new OpenIddictStoreAuthorization
        {
            ApplicationId = applicationId,
            Subject = subject,
            Status = "valid",
            Type = "permanent",
        };

        await _authorizations.SetScopesAsync(authorization, ["api"], default);
        await _authorizations.CreateAsync(authorization, default);

        return authorization.Id!;
    }

    private async Task<string> CreateTokenAsync(string? applicationId, string? authorizationId)
    {
        var token = new OpenIddictStoreToken
        {
            ApplicationId = applicationId,
            AuthorizationId = authorizationId,
            Status = "valid",
            Subject = "alice",
            Type = "access_token",
        };

        await _tokens.CreateAsync(token, default);

        return token.Id!;
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

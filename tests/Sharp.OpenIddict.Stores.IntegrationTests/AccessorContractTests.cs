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
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Models;
using Sharp.OpenIddict.Stores.Sqlite;
using Sharp.OpenIddict.Stores.Stores;
using Xunit;

namespace Sharp.OpenIddict.Stores.IntegrationTests;

/// <summary>
/// Accessor and payload contract (design 15.2, STORE-02): every Get/Set member must round-trip through a
/// real database without truncation, and Unicode, long payloads, and large JSON documents must survive
/// byte for byte. Reference identifiers are stored verbatim, never hashed or truncated.
/// </summary>
public sealed class AccessorContractTests : IAsyncLifetime
{
    private string _databasePath = string.Empty;
    private string _connectionString = string.Empty;
    private OpenIddictStoreTokenStore<OpenIddictStoreToken> _tokens = null!;
    private OpenIddictStoreApplicationStore<OpenIddictStoreApplication> _applications = null!;
    private OpenIddictStoreScopeStore<OpenIddictStoreScope> _scopes = null!;
    private OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization> _authorizations = null!;

    public async Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"oidc-accessor-{Guid.NewGuid():N}.db");
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
    public async Task LongPayload_IsNotTruncated()
    {
        // A real OpenIddict payload can hold an entire token document; 100 KB must survive intact.
        var payload = "{\"data\":\"" + new string('x', 100_000) + "\"}";

        var token = new OpenIddictStoreToken { Payload = payload, Status = "valid" };
        await _tokens.CreateAsync(token, default);

        var loaded = await _tokens.FindByIdAsync(token.Id!, default);

        Assert.Equal(payload.Length, (await _tokens.GetPayloadAsync(loaded!, default))!.Length);
        Assert.Equal(payload, await _tokens.GetPayloadAsync(loaded!, default));
    }

    [Fact]
    public async Task UnicodeContent_RoundTripsWithoutLoss()
    {
        // Mixed scripts, combining marks, surrogate pairs, and right-to-left text.
        var text = "Ünïcode 文字 «quoted» עברית عربى 😀🎉 e\u0301 \u00e9";

        var scope = new OpenIddictStoreScope
        {
            Name = text,
            DisplayName = text,
            Description = text,
        };

        await _scopes.SetDisplayNamesAsync(scope,
            ImmutableDictionary.CreateRange<CultureInfo, string>(
                [new(CultureInfo.GetCultureInfo("zh-Hans"), text)]), default);
        await _scopes.SetResourcesAsync(scope, [text, text + " "], default);
        await _scopes.CreateAsync(scope, default);

        var loaded = await _scopes.FindByIdAsync(scope.Id!, default);

        Assert.Equal(text, await _scopes.GetNameAsync(loaded!, default));
        Assert.Equal(text, await _scopes.GetDisplayNameAsync(loaded!, default));
        Assert.Equal(text, await _scopes.GetDescriptionAsync(loaded!, default));
        Assert.Equal(text, (await _scopes.GetDisplayNamesAsync(loaded!, default))[CultureInfo.GetCultureInfo("zh-Hans")]);
        Assert.Equal([text, text + " "], (await _scopes.GetResourcesAsync(loaded!, default)).ToArray());

        // The exact lookup finds the value and its trailing-space neighbour independently.
        Assert.Single(await ToListAsync(_scopes.FindByResourceAsync(text, default)));
        Assert.Single(await ToListAsync(_scopes.FindByResourceAsync(text + " ", default)));
    }

    [Fact]
    public async Task LargePropertiesAndSettings_RoundTripAsJson()
    {
        var properties = ImmutableDictionary.CreateBuilder<string, JsonElement>(StringComparer.Ordinal);

        for (var index = 0; index < 200; index++)
        {
            properties["key" + index] = JsonDocument.Parse($"{{\"n\":{index},\"t\":\"值{index}\"}}").RootElement;
        }

        var settings = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        settings["Long"] = new string('y', 5_000);

        var application = new OpenIddictStoreApplication { ClientId = "big" };
        await _applications.SetPropertiesAsync(application, properties.ToImmutable(), default);
        await _applications.SetSettingsAsync(application, settings.ToImmutable(), default);
        await _applications.CreateAsync(application, default);

        var loaded = await _applications.FindByIdAsync(application.Id!, default);
        var restored = await _applications.GetPropertiesAsync(loaded!, default);

        Assert.Equal(200, restored.Count);
        Assert.Equal(199, restored["key199"].GetProperty("n").GetInt32());
        Assert.Equal("值199", restored["key199"].GetProperty("t").GetString());
        Assert.Equal(5_000, (await _applications.GetSettingsAsync(loaded!, default))["Long"].Length);
    }

    [Fact]
    public async Task ReferenceId_IsStoredVerbatimNotHashed()
    {
        // The reference identifier must round-trip exactly: a digest would break FindByReferenceIdAsync.
        var referenceId = "ref-" + Guid.NewGuid().ToString("N") + "-Ünïcode";

        var token = new OpenIddictStoreToken { ReferenceId = referenceId, Status = "valid" };
        await _tokens.CreateAsync(token, default);

        var loaded = await _tokens.FindByReferenceIdAsync(referenceId, default);

        Assert.NotNull(loaded);
        Assert.Equal(referenceId, await _tokens.GetReferenceIdAsync(loaded!, default));

        // The stored column holds the literal value, not a hash.
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ReferenceId FROM OpenIddictTokens WHERE Id = @id";
        command.Parameters.AddWithValue("@id", token.Id);

        Assert.Equal(referenceId, (string?)await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ClientSecret_IsStoredVerbatim()
    {
        // A hashed client secret produced upstream must not be altered by the store.
        var secret = "$2a$11$" + new string('z', 53);

        var application = new OpenIddictStoreApplication { ClientId = "confidential", ClientSecret = secret };
        await _applications.CreateAsync(application, default);

        var loaded = await _applications.FindByClientIdAsync("confidential", default);

        Assert.Equal(secret, await _applications.GetClientSecretAsync(loaded!, default));
    }

    [Fact]
    public async Task AllApplicationAccessors_RoundTrip()
    {
        var key = new Microsoft.IdentityModel.Tokens.JsonWebKey { Kty = "oct", Kid = "k", K = "c2VjcmV0" };
        var jwks = new Microsoft.IdentityModel.Tokens.JsonWebKeySet();
        jwks.Keys.Add(key);

        var application = new OpenIddictStoreApplication();
        await _applications.SetApplicationTypeAsync(application, "web", default);
        await _applications.SetClientIdAsync(application, "id", default);
        await _applications.SetClientSecretAsync(application, "secret", default);
        await _applications.SetClientTypeAsync(application, "confidential", default);
        await _applications.SetConsentTypeAsync(application, "explicit", default);
        await _applications.SetDisplayNameAsync(application, "name", default);
        await _applications.SetJsonWebKeySetAsync(application, jwks, default);
        await _applications.SetPermissionsAsync(application, ["p1", "p2"], default);
        await _applications.SetPostLogoutRedirectUrisAsync(application, ["https://out"], default);
        await _applications.SetRedirectUrisAsync(application, ["https://cb"], default);
        await _applications.SetRequirementsAsync(application, ["r1"], default);

        await _applications.CreateAsync(application, default);
        var loaded = await _applications.FindByIdAsync(application.Id!, default);

        Assert.Equal("web", await _applications.GetApplicationTypeAsync(loaded!, default));
        Assert.Equal("id", await _applications.GetClientIdAsync(loaded!, default));
        Assert.Equal("secret", await _applications.GetClientSecretAsync(loaded!, default));
        Assert.Equal("confidential", await _applications.GetClientTypeAsync(loaded!, default));
        Assert.Equal("explicit", await _applications.GetConsentTypeAsync(loaded!, default));
        Assert.Equal("name", await _applications.GetDisplayNameAsync(loaded!, default));
        Assert.NotNull(await _applications.GetJsonWebKeySetAsync(loaded!, default));
        Assert.Equal(["p1", "p2"], (await _applications.GetPermissionsAsync(loaded!, default)).ToArray());
        Assert.Equal(["https://out"], (await _applications.GetPostLogoutRedirectUrisAsync(loaded!, default)).ToArray());
        Assert.Equal(["https://cb"], (await _applications.GetRedirectUrisAsync(loaded!, default)).ToArray());
        Assert.Equal(["r1"], (await _applications.GetRequirementsAsync(loaded!, default)).ToArray());
    }

    [Fact]
    public async Task AllScopeAndAuthorizationAccessors_RoundTrip()
    {
        var scope = new OpenIddictStoreScope();
        await _scopes.SetNameAsync(scope, "n", default);
        await _scopes.SetDisplayNameAsync(scope, "d", default);
        await _scopes.SetDescriptionAsync(scope, "desc", default);
        await _scopes.CreateAsync(scope, default);

        var loadedScope = await _scopes.FindByIdAsync(scope.Id!, default);
        Assert.Equal("n", await _scopes.GetNameAsync(loadedScope!, default));
        Assert.Equal("d", await _scopes.GetDisplayNameAsync(loadedScope!, default));
        Assert.Equal("desc", await _scopes.GetDescriptionAsync(loadedScope!, default));

        // The foreign key requires a real application, so one is created for this case.
        var owner = new OpenIddictStoreApplication { ClientId = "accessor-owner" };
        await _applications.CreateAsync(owner, default);

        var authorization = new OpenIddictStoreAuthorization();
        await _authorizations.SetApplicationIdAsync(authorization, owner.Id, default);
        await _authorizations.SetSubjectAsync(authorization, "sub", default);
        await _authorizations.SetStatusAsync(authorization, "valid", default);
        await _authorizations.SetTypeAsync(authorization, "permanent", default);
        await _authorizations.SetScopesAsync(authorization, ["s1"], default);
        var created = new DateTimeOffset(2026, 5, 5, 5, 5, 5, TimeSpan.Zero);
        await _authorizations.SetCreationDateAsync(authorization, created, default);
        await _authorizations.CreateAsync(authorization, default);

        var loadedAuthorization = await _authorizations.FindByIdAsync(authorization.Id!, default);
        Assert.Equal(owner.Id, await _authorizations.GetApplicationIdAsync(loadedAuthorization!, default));
        Assert.Equal("sub", await _authorizations.GetSubjectAsync(loadedAuthorization!, default));
        Assert.Equal("valid", await _authorizations.GetStatusAsync(loadedAuthorization!, default));
        Assert.Equal("permanent", await _authorizations.GetTypeAsync(loadedAuthorization!, default));
        Assert.Equal(["s1"], (await _authorizations.GetScopesAsync(loadedAuthorization!, default)).ToArray());
        Assert.Equal(created, await _authorizations.GetCreationDateAsync(loadedAuthorization!, default));
    }

    [Fact]
    public async Task TokenDates_AreNormalizedToMicroseconds()
    {
        // A value with sub-microsecond precision must be truncated, matching the stored resolution.
        var precise = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero).AddTicks(1234);

        var token = new OpenIddictStoreToken { Status = "valid" };
        await _tokens.SetCreationDateAsync(token, precise, default);
        await _tokens.SetExpirationDateAsync(token, precise.AddHours(1), default);
        await _tokens.SetRedemptionDateAsync(token, precise.AddMinutes(1), default);
        await _tokens.CreateAsync(token, default);

        var loaded = await _tokens.FindByIdAsync(token.Id!, default);

        var expected = new DateTimeOffset(precise.Ticks - (precise.Ticks % 10), TimeSpan.Zero);
        Assert.Equal(expected, await _tokens.GetCreationDateAsync(loaded!, default));
        Assert.Equal(expected.AddHours(1), await _tokens.GetExpirationDateAsync(loaded!, default));
        Assert.Equal(expected.AddMinutes(1), await _tokens.GetRedemptionDateAsync(loaded!, default));
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

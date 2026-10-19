using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using Sharp.OpenIddict.Stores.Models;
using Sharp.OpenIddict.Stores.Queries;

namespace Sharp.OpenIddict.Stores.Stores;

/// <summary>
/// The store for Application. Collections and dictionaries are persisted as JSON, and collections involved in lookups also have auxiliary index tables.
/// </summary>
/// <typeparam name="TApplication">The entity type, which must be <see cref="OpenIddictStoreApplication"/> or a derived type.</typeparam>
public class OpenIddictStoreApplicationStore<TApplication> : OpenIddictStoreBase,
    IOpenIddictApplicationStore<TApplication>
    where TApplication : class
{
    /// <summary>Creates the store from its dependencies.</summary>
    public OpenIddictStoreApplicationStore(
        IOptions<OpenIddictStoreOptions> options,
        IOpenIddictStoreConnectionFactory connectionFactory,
        IOpenIddictStoreDialect dialect,
        ILogger<OpenIddictStoreApplicationStore<TApplication>> logger)
        : base(options, connectionFactory, dialect, logger)
    {
    }

    /// <inheritdoc />
    public virtual async ValueTask<long> CountAsync(CancellationToken cancellationToken)
    {
        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        return await session.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM {Table(Schema.Applications)}", null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<TApplication>, IQueryable<TResult>> query, CancellationToken cancellationToken)
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        // Query callbacks are translated to one SQL statement (design 9); nothing is loaded in memory.
        return await CountQueryAsync<TApplication, TResult>(
            Schema.Applications, query, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask CreateAsync(TApplication application, CancellationToken cancellationToken)
    {
        if (application is null)
        {
            throw new ArgumentNullException(nameof(application));
        }

        if (application is not OpenIddictStoreApplication entity)
        {
            throw new ArgumentException(
                $"The entity must be {nameof(OpenIddictStoreApplication)} or a derived type.", nameof(application));
        }

        if (string.IsNullOrEmpty(entity.Id))
        {
            entity.Id = Guid.NewGuid().ToString();
        }

        if (string.IsNullOrEmpty(entity.ConcurrencyToken))
        {
            entity.ConcurrencyToken = Guid.NewGuid().ToString();
        }

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        var affected = await session.ExecuteAsync(
            $@"INSERT INTO {Table(Schema.Applications)} (
                {Column("Id")}, {Column("ConcurrencyToken")}, {Column("ApplicationType")}, {Column("ClientId")},
                {Column("ClientSecret")}, {Column("ClientType")}, {Column("ConsentType")}, {Column("DisplayName")},
                {Column("DisplayNames")}, {Column("JsonWebKeySet")}, {Column("Permissions")},
                {Column("PostLogoutRedirectUris")}, {Column("Properties")}, {Column("RedirectUris")},
                {Column("Requirements")}, {Column("Settings")})
            VALUES (
                @Id, @ConcurrencyToken, @ApplicationType, @ClientId, @ClientSecret, @ClientType, @ConsentType,
                @DisplayName, @DisplayNames, @JsonWebKeySet, @Permissions, @PostLogoutRedirectUris, @Properties,
                @RedirectUris, @Requirements, @Settings)",
            entity, cancellationToken).ConfigureAwait(false);

        if (affected != 1)
        {
            throw new InvalidOperationException("Creating the Application wrote no rows.");
        }

        // The collection auxiliary index shares the transaction with the main table (design 7/16.2).
        await using var transaction = await session.BeginAsync(cancellationToken).ConfigureAwait(false);
        await ReplaceCollectionIndexAsync(
            session, transaction.Value, entity.Id!, Schema.ApplicationRedirectUris, "ApplicationId", "UriHash", "Uri",
            OpenIddictStoreJson.DeserializeStrings(entity.RedirectUris), cancellationToken).ConfigureAwait(false);
        await ReplaceCollectionIndexAsync(
            session, transaction.Value, entity.Id!, Schema.ApplicationPostLogoutRedirectUris, "ApplicationId", "UriHash",
            "Uri", OpenIddictStoreJson.DeserializeStrings(entity.PostLogoutRedirectUris), cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        LogWritten(nameof(CreateAsync), affected);
    }

    /// <inheritdoc />
    public virtual async ValueTask DeleteAsync(TApplication application, CancellationToken cancellationToken)
    {
        if (application is not OpenIddictStoreApplication entity)
        {
            throw new ArgumentNullException(nameof(application));
        }

        if (string.IsNullOrEmpty(entity.Id))
        {
            throw new ArgumentException("The entity is missing its identifier.", nameof(application));
        }

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await session.BeginAsync(cancellationToken).ConfigureAwait(false);

        // Cascading delete matrix (design 16.2.1): the Application, its Authorizations, the Tokens linked to it directly or
        // through those Authorizations, and the corresponding auxiliary rows. All of this completes in the same transaction.
        var applications = Table(Schema.Applications);
        var authorizations = Table(Schema.Authorizations);
        var tokens = Table(Schema.Tokens);

        // Step 1: confirm the root record still carries the expected token and take the write lock
        // before the related sets are computed, so a concurrent association cannot slip through the
        // window between reading the set and deleting it (design 16.2.1 step 1, LIFE-02).
        var locked = await session.ExecuteAsync(
            $"UPDATE {applications} SET {Column("ConcurrencyToken")} = {Column("ConcurrencyToken")} " +
            $"WHERE {Column("Id")} = @Id AND {Dialect.BuildExactStringEquals(Column("ConcurrencyToken"), "ConcurrencyToken")}",
            new { Id = entity.Id, ConcurrencyToken = entity.ConcurrencyToken },
            transaction.Value, cancellationToken).ConfigureAwait(false);

        if (locked != 1)
        {
            throw OpenIddictStoreErrors.Concurrency(nameof(OpenIddictStoreApplication));
        }

        // Step 2: delete T — tokens whose ApplicationId is this application, unioned with tokens whose
        // AuthorizationId belongs to A. The ApplicationId of those tokens may be NULL or another
        // application; the authorizations they reference outside A are kept.
        var tokenDelete = $@"
            DELETE FROM {tokens}
            WHERE {Column("ApplicationId")} = @Id
               OR {Column("AuthorizationId")} IN (
                    SELECT {Column("Id")} FROM {authorizations} WHERE {Column("ApplicationId")} = @Id)";

        await session.ExecuteAsync(tokenDelete, new { Id = entity.Id }, transaction.Value, cancellationToken)
            .ConfigureAwait(false);

        // Step 3: delete the auxiliary scope rows of A, then A itself.
        await session.ExecuteAsync(
            $"DELETE FROM {Table(Schema.AuthorizationScopes)} WHERE {Column("AuthorizationId")} IN " +
            $"(SELECT {Column("Id")} FROM {authorizations} WHERE {Column("ApplicationId")} = @Id)",
            new { Id = entity.Id }, transaction.Value, cancellationToken).ConfigureAwait(false);

        await session.ExecuteAsync(
            $"DELETE FROM {authorizations} WHERE {Column("ApplicationId")} = @Id",
            new { Id = entity.Id }, transaction.Value, cancellationToken).ConfigureAwait(false);

        // Step 4: delete the two URI auxiliary tables, then the root record under the previous token.
        await session.ExecuteAsync(
            $"DELETE FROM {Table(Schema.ApplicationRedirectUris)} WHERE {Column("ApplicationId")} = @Id",
            new { Id = entity.Id }, transaction.Value, cancellationToken).ConfigureAwait(false);

        await session.ExecuteAsync(
            $"DELETE FROM {Table(Schema.ApplicationPostLogoutRedirectUris)} WHERE {Column("ApplicationId")} = @Id",
            new { Id = entity.Id }, transaction.Value, cancellationToken).ConfigureAwait(false);

        var affected = await session.ExecuteAsync(
            $"DELETE FROM {applications} WHERE {Column("Id")} = @Id " +
            $"AND {Dialect.BuildExactStringEquals(Column("ConcurrencyToken"), "ConcurrencyToken")}",
            new { Id = entity.Id, ConcurrencyToken = entity.ConcurrencyToken },
            transaction.Value, cancellationToken).ConfigureAwait(false);

        if (affected != 1)
        {
            throw OpenIddictStoreErrors.Concurrency(nameof(OpenIddictStoreApplication));
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        LogWritten(nameof(DeleteAsync), affected);
    }

    /// <inheritdoc />
    public virtual async ValueTask<TApplication?> FindByClientIdAsync(
        string identifier, CancellationToken cancellationToken)
    {
        OpenIddictStoreErrors.ThrowIfKeyIsNullOrEmpty(identifier, nameof(identifier));

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        return await QueryOneAsync(
            session,
            $"WHERE {Dialect.BuildExactStringEquals(Column("ClientId"), "identifier")}",
            new { identifier }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<TApplication?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        OpenIddictStoreErrors.ThrowIfKeyIsNullOrEmpty(identifier, nameof(identifier));

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        return await QueryOneAsync(
            session,
            $"WHERE {Dialect.BuildExactStringEquals(Column("Id"), "identifier")}",
            new { identifier }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual IAsyncEnumerable<TApplication> FindByPostLogoutRedirectUriAsync(
        string uri, CancellationToken cancellationToken)
        => FindByCollectionAsync(
            Schema.ApplicationPostLogoutRedirectUris, "ApplicationId", "UriHash", "Uri", uri, cancellationToken);

    /// <inheritdoc />
    public virtual IAsyncEnumerable<TApplication> FindByRedirectUriAsync(string uri, CancellationToken cancellationToken)
        => FindByCollectionAsync(
            Schema.ApplicationRedirectUris, "ApplicationId", "UriHash", "Uri", uri, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<string?> GetApplicationTypeAsync(
        TApplication application, CancellationToken cancellationToken)
        => GetStringAsync(application, static entity => entity.ApplicationType);

    /// <inheritdoc />
    public virtual async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<TApplication>, TState, IQueryable<TResult>> query, TState state,
        CancellationToken cancellationToken)
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        // Query callbacks are translated to one SQL statement (design 9); nothing is loaded in memory.
        return await GetQueryAsync<TApplication, TState, TResult>(
            Schema.Applications, query, state, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual ValueTask<string?> GetClientIdAsync(TApplication application, CancellationToken cancellationToken)
        => GetStringAsync(application, static entity => entity.ClientId);

    /// <inheritdoc />
    public virtual ValueTask<string?> GetClientSecretAsync(TApplication application, CancellationToken cancellationToken)
        => GetStringAsync(application, static entity => entity.ClientSecret);

    /// <inheritdoc />
    public virtual ValueTask<string?> GetClientTypeAsync(TApplication application, CancellationToken cancellationToken)
        => GetStringAsync(application, static entity => entity.ClientType);

    /// <inheritdoc />
    public virtual ValueTask<string?> GetConsentTypeAsync(TApplication application, CancellationToken cancellationToken)
        => GetStringAsync(application, static entity => entity.ConsentType);

    /// <inheritdoc />
    public virtual ValueTask<string?> GetDisplayNameAsync(TApplication application, CancellationToken cancellationToken)
        => GetStringAsync(application, static entity => entity.DisplayName);

    /// <inheritdoc />
    public virtual ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(
        TApplication application, CancellationToken cancellationToken)
    {
        var entity = Cast(application);

        return new(OpenIddictStoreJson.DeserializeLocalized(entity.DisplayNames));
    }

    /// <inheritdoc />
    public virtual ValueTask<string?> GetIdAsync(TApplication application, CancellationToken cancellationToken)
        => GetStringAsync(application, static entity => entity.Id);

    /// <inheritdoc />
    public virtual ValueTask<JsonWebKeySet?> GetJsonWebKeySetAsync(
        TApplication application, CancellationToken cancellationToken)
    {
        var entity = Cast(application);

        return new(OpenIddictStoreJson.DeserializeJsonWebKeySet(entity.JsonWebKeySet));
    }

    /// <inheritdoc />
    public virtual ValueTask<ImmutableArray<string>> GetPermissionsAsync(
        TApplication application, CancellationToken cancellationToken)
    {
        var entity = Cast(application);

        return new(OpenIddictStoreJson.DeserializeStrings(entity.Permissions));
    }

    /// <inheritdoc />
    public virtual ValueTask<ImmutableArray<string>> GetPostLogoutRedirectUrisAsync(
        TApplication application, CancellationToken cancellationToken)
    {
        var entity = Cast(application);

        return new(OpenIddictStoreJson.DeserializeStrings(entity.PostLogoutRedirectUris));
    }

    /// <inheritdoc />
    public virtual ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        TApplication application, CancellationToken cancellationToken)
    {
        var entity = Cast(application);

        return new(OpenIddictStoreJson.DeserializeProperties(entity.Properties));
    }

    /// <inheritdoc />
    public virtual ValueTask<ImmutableArray<string>> GetRedirectUrisAsync(
        TApplication application, CancellationToken cancellationToken)
    {
        var entity = Cast(application);

        return new(OpenIddictStoreJson.DeserializeStrings(entity.RedirectUris));
    }

    /// <inheritdoc />
    public virtual ValueTask<ImmutableArray<string>> GetRequirementsAsync(
        TApplication application, CancellationToken cancellationToken)
    {
        var entity = Cast(application);

        return new(OpenIddictStoreJson.DeserializeStrings(entity.Requirements));
    }

    /// <inheritdoc />
    public virtual ValueTask<ImmutableDictionary<string, string>> GetSettingsAsync(
        TApplication application, CancellationToken cancellationToken)
    {
        var entity = Cast(application);

        return new(OpenIddictStoreJson.DeserializeSettings(entity.Settings));
    }

    /// <inheritdoc />
    public virtual ValueTask<TApplication> InstantiateAsync(CancellationToken cancellationToken)
        => new(Activator.CreateInstance<TApplication>());

    /// <inheritdoc />
    public virtual async IAsyncEnumerable<TApplication> ListAsync(
        int? count, int? offset, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "count cannot be negative.");
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "offset cannot be negative.");
        }

        // count = 0 returns an empty sequence without executing a query (design 16.1).
        if (count is 0)
        {
            yield break;
        }

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        // Sort stably by Id by default and page in the database.
        var sql = $"SELECT * FROM {Table(Schema.Applications)} ORDER BY {Column("Id")} " +
                  $"{Dialect.BuildPaging(count, offset)}".TrimEnd();

        await foreach (var row in session.StreamAsync(
            sql, null, MapEntity, cancellationToken).ConfigureAwait(false))
        {
            yield return (TApplication)(object)row;
        }
    }

    /// <inheritdoc />
    public virtual IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<TApplication>, TState, IQueryable<TResult>> query, TState state,
        CancellationToken cancellationToken)
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        // Query callbacks are translated to one SQL statement (design 9); nothing is loaded in memory.
        return ListQueryAsync<TApplication, TState, TResult>(
            Schema.Applications, query, state, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask SetApplicationTypeAsync(
        TApplication application, string? type, CancellationToken cancellationToken)
        => SetStringAsync(application, static (entity, value) => entity.ApplicationType = value, type);

    /// <inheritdoc />
    public virtual ValueTask SetClientIdAsync(
        TApplication application, string? identifier, CancellationToken cancellationToken)
        => SetStringAsync(application, static (entity, value) => entity.ClientId = value, identifier);

    /// <inheritdoc />
    public virtual ValueTask SetClientSecretAsync(
        TApplication application, string? secret, CancellationToken cancellationToken)
        => SetStringAsync(application, static (entity, value) => entity.ClientSecret = value, secret);

    /// <inheritdoc />
    public virtual ValueTask SetClientTypeAsync(
        TApplication application, string? type, CancellationToken cancellationToken)
        => SetStringAsync(application, static (entity, value) => entity.ClientType = value, type);

    /// <inheritdoc />
    public virtual ValueTask SetConsentTypeAsync(
        TApplication application, string? type, CancellationToken cancellationToken)
        => SetStringAsync(application, static (entity, value) => entity.ConsentType = value, type);

    /// <inheritdoc />
    public virtual ValueTask SetDisplayNameAsync(
        TApplication application, string? name, CancellationToken cancellationToken)
        => SetStringAsync(application, static (entity, value) => entity.DisplayName = value, name);

    /// <inheritdoc />
    public virtual ValueTask SetDisplayNamesAsync(
        TApplication application, ImmutableDictionary<CultureInfo, string> names, CancellationToken cancellationToken)
    {
        Cast(application).DisplayNames = OpenIddictStoreJson.SerializeLocalized(names);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetJsonWebKeySetAsync(
        TApplication application, JsonWebKeySet? set, CancellationToken cancellationToken)
    {
        Cast(application).JsonWebKeySet = OpenIddictStoreJson.SerializeJsonWebKeySet(set);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetPermissionsAsync(
        TApplication application, ImmutableArray<string> permissions, CancellationToken cancellationToken)
    {
        Cast(application).Permissions = OpenIddictStoreJson.SerializeStrings(permissions);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetPostLogoutRedirectUrisAsync(
        TApplication application, ImmutableArray<string> uris, CancellationToken cancellationToken)
    {
        Cast(application).PostLogoutRedirectUris = OpenIddictStoreJson.SerializeStrings(uris);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetPropertiesAsync(
        TApplication application, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
    {
        Cast(application).Properties = OpenIddictStoreJson.SerializeProperties(properties);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetRedirectUrisAsync(
        TApplication application, ImmutableArray<string> uris, CancellationToken cancellationToken)
    {
        Cast(application).RedirectUris = OpenIddictStoreJson.SerializeStrings(uris);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetRequirementsAsync(
        TApplication application, ImmutableArray<string> requirements, CancellationToken cancellationToken)
    {
        Cast(application).Requirements = OpenIddictStoreJson.SerializeStrings(requirements);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetSettingsAsync(
        TApplication application, ImmutableDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        Cast(application).Settings = OpenIddictStoreJson.SerializeSettings(settings);

        return default;
    }

    /// <inheritdoc />
    public virtual async ValueTask UpdateAsync(TApplication application, CancellationToken cancellationToken)
    {
        if (application is not OpenIddictStoreApplication entity)
        {
            throw new ArgumentNullException(nameof(application));
        }

        if (string.IsNullOrEmpty(entity.Id))
        {
            throw new ArgumentException("The entity is missing its identifier.", nameof(application));
        }

        var previous = entity.ConcurrencyToken;
        var updated = Guid.NewGuid().ToString();

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await session.BeginAsync(cancellationToken).ConfigureAwait(false);

        var affected = await session.ExecuteAsync(
            $@"UPDATE {Table(Schema.Applications)} SET
                {Column("ConcurrencyToken")} = @Updated,
                {Column("ApplicationType")} = @ApplicationType,
                {Column("ClientId")} = @ClientId,
                {Column("ClientSecret")} = @ClientSecret,
                {Column("ClientType")} = @ClientType,
                {Column("ConsentType")} = @ConsentType,
                {Column("DisplayName")} = @DisplayName,
                {Column("DisplayNames")} = @DisplayNames,
                {Column("JsonWebKeySet")} = @JsonWebKeySet,
                {Column("Permissions")} = @Permissions,
                {Column("PostLogoutRedirectUris")} = @PostLogoutRedirectUris,
                {Column("Properties")} = @Properties,
                {Column("RedirectUris")} = @RedirectUris,
                {Column("Requirements")} = @Requirements,
                {Column("Settings")} = @Settings
            WHERE {Column("Id")} = @Id
              AND {Dialect.BuildExactStringEquals(Column("ConcurrencyToken"), "Previous")}",
            new
            {
                Id = entity.Id,
                Previous = previous,
                Updated = updated,
                entity.ApplicationType,
                entity.ClientId,
                entity.ClientSecret,
                entity.ClientType,
                entity.ConsentType,
                entity.DisplayName,
                entity.DisplayNames,
                entity.JsonWebKeySet,
                entity.Permissions,
                entity.PostLogoutRedirectUris,
                entity.Properties,
                entity.RedirectUris,
                entity.Requirements,
                entity.Settings,
            },
            transaction.Value, cancellationToken).ConfigureAwait(false);

        if (affected != 1)
        {
            // The in-memory entity keeps the previous token after the rollback (design 8).
            throw OpenIddictStoreErrors.Concurrency(nameof(OpenIddictStoreApplication));
        }

        await ReplaceCollectionIndexAsync(
            session, transaction.Value, entity.Id!, Schema.ApplicationRedirectUris, "ApplicationId", "UriHash", "Uri",
            OpenIddictStoreJson.DeserializeStrings(entity.RedirectUris), cancellationToken).ConfigureAwait(false);
        await ReplaceCollectionIndexAsync(
            session, transaction.Value, entity.Id!, Schema.ApplicationPostLogoutRedirectUris, "ApplicationId", "UriHash",
            "Uri", OpenIddictStoreJson.DeserializeStrings(entity.PostLogoutRedirectUris), cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        // Update the in-memory token only after the commit succeeds.
        entity.ConcurrencyToken = updated;
        LogWritten(nameof(UpdateAsync), affected);
    }

    private static OpenIddictStoreApplication MapEntity(System.Data.Common.DbDataReader reader)
    {
        var entity = new OpenIddictStoreApplication();

        for (var index = 0; index < reader.FieldCount; index++)
        {
            var name = reader.GetName(index);
            if (reader.IsDBNull(index))
            {
                continue;
            }

            switch (name)
            {
                case "Id": entity.Id = reader.GetString(index); break;
                case "ConcurrencyToken": entity.ConcurrencyToken = reader.GetString(index); break;
                case "ApplicationType": entity.ApplicationType = reader.GetString(index); break;
                case "ClientId": entity.ClientId = reader.GetString(index); break;
                case "ClientSecret": entity.ClientSecret = reader.GetString(index); break;
                case "ClientType": entity.ClientType = reader.GetString(index); break;
                case "ConsentType": entity.ConsentType = reader.GetString(index); break;
                case "DisplayName": entity.DisplayName = reader.GetString(index); break;
                case "DisplayNames": entity.DisplayNames = reader.GetString(index); break;
                case "JsonWebKeySet": entity.JsonWebKeySet = reader.GetString(index); break;
                case "Permissions": entity.Permissions = reader.GetString(index); break;
                case "PostLogoutRedirectUris": entity.PostLogoutRedirectUris = reader.GetString(index); break;
                case "Properties": entity.Properties = reader.GetString(index); break;
                case "RedirectUris": entity.RedirectUris = reader.GetString(index); break;
                case "Requirements": entity.Requirements = reader.GetString(index); break;
                case "Settings": entity.Settings = reader.GetString(index); break;
            }
        }

        return entity;
    }

    private static OpenIddictStoreApplication Cast(TApplication application)
        => application as OpenIddictStoreApplication
           ?? throw new ArgumentException(
               $"The entity must be {nameof(OpenIddictStoreApplication)} or a derived type.", nameof(application));

    private static ValueTask<string?> GetStringAsync(
        TApplication application, Func<OpenIddictStoreApplication, string?> selector)
        => new(selector(Cast(application)));

    private static ValueTask SetStringAsync(
        TApplication application, Action<OpenIddictStoreApplication, string?> setter, string? value)
    {
        setter(Cast(application), value);

        return default;
    }

    private async ValueTask<TApplication?> QueryOneAsync(
        OpenIddictStoreSession session, string where, object parameters, CancellationToken cancellationToken)
        => await session.QuerySingleOrDefaultAsync(
            $"SELECT * FROM {Table(Schema.Applications)} {where}",
            parameters,
            reader => (TApplication)OpenIddictStoreEntityMapper.Map(reader, typeof(TApplication), Dialect),
            cancellationToken).ConfigureAwait(false);

    private async IAsyncEnumerable<TApplication> FindByCollectionAsync(
        string table, string ownerColumn, string hashColumn, string valueColumn, string value,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        OpenIddictStoreErrors.ThrowIfKeyIsNullOrEmpty(value, nameof(value));

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        // The digest only narrows candidates; a final Ordinal re-check against the authoritative JSON value is done, and each entity is returned once.
        var sql = $@"
            SELECT DISTINCT a.* FROM {Table(Schema.Applications)} a
            INNER JOIN {Table(table)} i ON i.{Column(ownerColumn)} = a.{Column("Id")}
            WHERE i.{Column(hashColumn)} = @hash";

        await foreach (var candidate in session.StreamAsync(
            sql,
            new { hash = OpenIddictStoreSession.Hash(value) },
            reader => OpenIddictStoreEntityMapper.Map(reader, typeof(TApplication), Dialect),
            cancellationToken).ConfigureAwait(false))
        {
            var application = (TApplication)candidate;
            var entity = (OpenIddictStoreApplication)(object)application;

            var values = valueColumn == "Uri"
                ? OpenIddictStoreJson.DeserializeStrings(
                    table == Schema.ApplicationRedirectUris ? entity.RedirectUris : entity.PostLogoutRedirectUris)
                : ImmutableArray<string>.Empty;

            if (values.Contains(value, StringComparer.Ordinal))
            {
                yield return application;
            }
        }
    }

    private async ValueTask ReplaceCollectionIndexAsync(
        OpenIddictStoreSession session, System.Data.Common.DbTransaction transaction, string ownerId,
        string table, string ownerColumn, string hashColumn, string valueColumn,
        ImmutableArray<string> values, CancellationToken cancellationToken)
    {
        await session.ExecuteAsync(
            $"DELETE FROM {Table(table)} WHERE {Column(ownerColumn)} = @ownerId",
            new { ownerId }, transaction, cancellationToken).ConfigureAwait(false);

        if (values.IsDefaultOrEmpty)
        {
            return;
        }

        // After Ordinal deduplication assign Position; the auxiliary table order does not override the original order inside the JSON.
        var distinct = values.Distinct(StringComparer.Ordinal).ToList();

        for (var position = 0; position < distinct.Count; position++)
        {
            var value = distinct[position];

            await session.ExecuteAsync(
                $@"INSERT INTO {Table(table)} ({Column(ownerColumn)}, {Column(hashColumn)}, {Column(valueColumn)},
                    {Column("Position")})
                   VALUES (@ownerId, @hash, @value, @position)",
                new
                {
                    ownerId,
                    hash = OpenIddictStoreSession.Hash(value),
                    value,
                    position,
                },
                transaction, cancellationToken).ConfigureAwait(false);
        }
    }
}

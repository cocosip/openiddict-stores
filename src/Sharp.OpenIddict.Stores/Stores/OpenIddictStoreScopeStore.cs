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
using OpenIddict.Abstractions;
using Sharp.OpenIddict.Stores.Models;
using Sharp.OpenIddict.Stores.Queries;

namespace Sharp.OpenIddict.Stores.Stores;

/// <summary>
/// The store for Scope. Resources are authoritative in JSON, and the ScopeResources auxiliary table is only used for lookups by resource name.
/// </summary>
/// <typeparam name="TScope">The entity type, which must be <see cref="OpenIddictStoreScope"/> or a derived type.</typeparam>
public class OpenIddictStoreScopeStore<TScope> : OpenIddictStoreBase, IOpenIddictScopeStore<TScope>
    where TScope : class
{
    /// <summary>Creates the store from its dependencies.</summary>
    public OpenIddictStoreScopeStore(
        IOptions<OpenIddictStoreOptions> options,
        IOpenIddictStoreConnectionFactory connectionFactory,
        IOpenIddictStoreDialect dialect,
        ILogger<OpenIddictStoreScopeStore<TScope>> logger)
        : base(options, connectionFactory, dialect, logger)
    {
    }

    /// <inheritdoc />
    public virtual async ValueTask<long> CountAsync(CancellationToken cancellationToken)
    {
        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        return await session.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM {Table(Schema.Scopes)}", null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<TScope>, IQueryable<TResult>> query, CancellationToken cancellationToken)
        => CountQueryAsync<TScope, TResult>(Schema.Scopes, query, cancellationToken);

    /// <inheritdoc />
    public virtual async ValueTask CreateAsync(TScope scope, CancellationToken cancellationToken)
    {
        if (scope is not OpenIddictStoreScope entity)
        {
            throw new ArgumentNullException(nameof(scope));
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
        await using var transaction = await session.BeginAsync(cancellationToken).ConfigureAwait(false);

        var affected = await session.ExecuteAsync(
            $@"INSERT INTO {Table(Schema.Scopes)} (
                {Column("Id")}, {Column("ConcurrencyToken")}, {Column("Description")}, {Column("Descriptions")},
                {Column("DisplayName")}, {Column("DisplayNames")}, {Column("Name")}, {Column("Properties")},
                {Column("Resources")})
            VALUES (
                {Parameter("Id")}, {Parameter("ConcurrencyToken")}, {Parameter("Description")}, {Parameter("Descriptions")},
                {Parameter("DisplayName")}, {Parameter("DisplayNames")}, {Parameter("Name")}, {Parameter("Properties")},
                {Parameter("Resources")})",
            entity, transaction.Value, cancellationToken).ConfigureAwait(false);

        if (affected != 1)
        {
            throw new InvalidOperationException("Creating the Scope wrote no rows.");
        }

        await ReplaceResourcesAsync(
            session, transaction.Value, entity.Id!,
            OpenIddictStoreJson.DeserializeStrings(entity.Resources), cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        LogWritten(nameof(CreateAsync), affected);
    }

    /// <inheritdoc />
    public virtual async ValueTask DeleteAsync(TScope scope, CancellationToken cancellationToken)
    {
        if (scope is not OpenIddictStoreScope entity)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (string.IsNullOrEmpty(entity.Id))
        {
            throw new ArgumentException("The entity is missing its identifier.", nameof(scope));
        }

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await session.BeginAsync(cancellationToken).ConfigureAwait(false);

        // Deleting a Scope only cascades to its ScopeResources auxiliary rows and affects no other entity (design 16.2.1).
        await session.ExecuteAsync(
            $"DELETE FROM {Table(Schema.ScopeResources)} WHERE {Column("ScopeId")} = {Parameter("Id")}",
            new { Id = entity.Id }, transaction.Value, cancellationToken).ConfigureAwait(false);

        var affected = await session.ExecuteAsync(
            $"DELETE FROM {Table(Schema.Scopes)} WHERE {Column("Id")} = {Parameter("Id")} " +
            $"AND {Dialect.BuildExactStringEquals(Column("ConcurrencyToken"), "ConcurrencyToken")}",
            new { Id = entity.Id, ConcurrencyToken = entity.ConcurrencyToken },
            transaction.Value, cancellationToken).ConfigureAwait(false);

        if (affected == 0)
        {
            throw OpenIddictStoreErrors.Concurrency(nameof(OpenIddictStoreScope));
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        LogWritten(nameof(DeleteAsync), affected);
    }

    /// <inheritdoc />
    public virtual async ValueTask<TScope?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        OpenIddictStoreErrors.ThrowIfKeyIsNullOrEmpty(identifier, nameof(identifier));

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        return await QueryOneAsync(
            session, $"WHERE {Dialect.BuildExactStringEquals(Column("Id"), "identifier")}",
            new { identifier }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<TScope?> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        OpenIddictStoreErrors.ThrowIfKeyIsNullOrEmpty(name, nameof(name));

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        return await QueryOneAsync(
            session, $"WHERE {Dialect.BuildExactStringEquals(Column("Name"), "name")}",
            new { name }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async IAsyncEnumerable<TScope> FindByNamesAsync(
        ImmutableArray<string> names,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // An empty list returns an empty sequence without executing an unfiltered query (design 16.1).
        if (names.IsDefaultOrEmpty)
        {
            yield break;
        }

        if (names.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException("The requested set of names cannot contain null or empty strings.", nameof(names));
        }

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        // Parameterized IN with exact equality matching; the number of names is protected by the dialect parameter limit.
        var distinct = names.Distinct(StringComparer.Ordinal).ToList();
        var parameters = new Dictionary<string, object?>();
        var placeholders = new List<string>(distinct.Count);

        for (var index = 0; index < distinct.Count; index++)
        {
            var parameter = "name" + index.ToString(CultureInfo.InvariantCulture);
            placeholders.Add(Parameter(parameter));
            parameters[parameter] = distinct[index];
        }

        var sql = $"SELECT * FROM {Table(Schema.Scopes)} WHERE {Column("Name")} IN ({string.Join(", ", placeholders)})";

        await foreach (var row in session.StreamAsync(sql, parameters, MapEntity, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return (TScope)(object)row;
        }
    }

    /// <inheritdoc />
    public virtual async IAsyncEnumerable<TScope> FindByResourceAsync(
        string resource, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        OpenIddictStoreErrors.ThrowIfKeyIsNullOrEmpty(resource, nameof(resource));

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        // The digest only narrows candidates; a final Ordinal re-check against the authoritative JSON value
        // is done, and each Scope is returned once. No SELECT DISTINCT: the scope table carries CLOB/NCLOB
        // columns, which Oracle and DM refuse to place under DISTINCT (ORA-00932), so duplicate candidate
        // rows are folded here instead — the Ordinal re-check stays authoritative either way.
        var sql = $@"
            SELECT s.* FROM {Table(Schema.Scopes)} s
            INNER JOIN {Table(Schema.ScopeResources)} i ON i.{Column("ScopeId")} = s.{Column("Id")}
            WHERE i.{Column("ResourceHash")} = {Parameter("hash")}";

        var returned = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var candidate in session.StreamAsync(
            sql,
            new { hash = OpenIddictStoreSession.Hash(resource) },
            reader => OpenIddictStoreEntityMapper.Map(reader, typeof(TScope), Dialect),
            cancellationToken).ConfigureAwait(false))
        {
            var scope = (TScope)candidate;
            var model = (OpenIddictStoreScope)(object)scope;

            if (!returned.Add(model.Id!))
            {
                continue;
            }

            if (OpenIddictStoreJson.DeserializeStrings(model.Resources)
                .Contains(resource, StringComparer.Ordinal))
            {
                yield return scope;
            }
        }
    }

    /// <inheritdoc />
    public virtual ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<TScope>, TState, IQueryable<TResult>> query, TState state,
        CancellationToken cancellationToken)
        => GetQueryAsync<TScope, TState, TResult>(Schema.Scopes, query, state, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<string?> GetDescriptionAsync(TScope scope, CancellationToken cancellationToken)
        => GetStringAsync(scope, static entity => entity.Description);

    /// <inheritdoc />
    public virtual ValueTask<ImmutableDictionary<CultureInfo, string>> GetDescriptionsAsync(
        TScope scope, CancellationToken cancellationToken)
        => new(OpenIddictStoreJson.DeserializeLocalized(Cast(scope).Descriptions));

    /// <inheritdoc />
    public virtual ValueTask<string?> GetDisplayNameAsync(TScope scope, CancellationToken cancellationToken)
        => GetStringAsync(scope, static entity => entity.DisplayName);

    /// <inheritdoc />
    public virtual ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(
        TScope scope, CancellationToken cancellationToken)
        => new(OpenIddictStoreJson.DeserializeLocalized(Cast(scope).DisplayNames));

    /// <inheritdoc />
    public virtual ValueTask<string?> GetIdAsync(TScope scope, CancellationToken cancellationToken)
        => GetStringAsync(scope, static entity => entity.Id);

    /// <inheritdoc />
    public virtual ValueTask<string?> GetNameAsync(TScope scope, CancellationToken cancellationToken)
        => GetStringAsync(scope, static entity => entity.Name);

    /// <inheritdoc />
    public virtual ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        TScope scope, CancellationToken cancellationToken)
        => new(OpenIddictStoreJson.DeserializeProperties(Cast(scope).Properties));

    /// <inheritdoc />
    public virtual ValueTask<ImmutableArray<string>> GetResourcesAsync(
        TScope scope, CancellationToken cancellationToken)
        => new(OpenIddictStoreJson.DeserializeStrings(Cast(scope).Resources));

    /// <inheritdoc />
    public virtual ValueTask<TScope> InstantiateAsync(CancellationToken cancellationToken)
        => new(Activator.CreateInstance<TScope>());

    /// <inheritdoc />
    public virtual async IAsyncEnumerable<TScope> ListAsync(
        int? count, int? offset,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "count cannot be negative.");
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "offset cannot be negative.");
        }

        if (count is 0)
        {
            yield break;
        }

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"SELECT * FROM {Table(Schema.Scopes)} ORDER BY {Column("Id")} " +
                  $"{Dialect.BuildPaging(count, offset)}".TrimEnd();

        await foreach (var row in session.StreamAsync(sql, null, MapEntity, cancellationToken).ConfigureAwait(false))
        {
            yield return (TScope)(object)row;
        }
    }

    /// <inheritdoc />
    public virtual IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<TScope>, TState, IQueryable<TResult>> query, TState state,
        CancellationToken cancellationToken)
        => ListQueryAsync<TScope, TState, TResult>(Schema.Scopes, query, state, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask SetDescriptionAsync(
        TScope scope, string? description, CancellationToken cancellationToken)
        => SetStringAsync(scope, static (entity, value) => entity.Description = value, description);

    /// <inheritdoc />
    public virtual ValueTask SetDescriptionsAsync(
        TScope scope, ImmutableDictionary<CultureInfo, string> descriptions, CancellationToken cancellationToken)
    {
        Cast(scope).Descriptions = OpenIddictStoreJson.SerializeLocalized(descriptions);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetDisplayNameAsync(TScope scope, string? name, CancellationToken cancellationToken)
        => SetStringAsync(scope, static (entity, value) => entity.DisplayName = value, name);

    /// <inheritdoc />
    public virtual ValueTask SetDisplayNamesAsync(
        TScope scope, ImmutableDictionary<CultureInfo, string> names, CancellationToken cancellationToken)
    {
        Cast(scope).DisplayNames = OpenIddictStoreJson.SerializeLocalized(names);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetNameAsync(TScope scope, string? name, CancellationToken cancellationToken)
        => SetStringAsync(scope, static (entity, value) => entity.Name = value, name);

    /// <inheritdoc />
    public virtual ValueTask SetPropertiesAsync(
        TScope scope, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
    {
        Cast(scope).Properties = OpenIddictStoreJson.SerializeProperties(properties);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetResourcesAsync(
        TScope scope, ImmutableArray<string> resources, CancellationToken cancellationToken)
    {
        Cast(scope).Resources = OpenIddictStoreJson.SerializeStrings(resources);

        return default;
    }

    /// <inheritdoc />
    public virtual async ValueTask UpdateAsync(TScope scope, CancellationToken cancellationToken)
    {
        if (scope is not OpenIddictStoreScope entity)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (string.IsNullOrEmpty(entity.Id))
        {
            throw new ArgumentException("The entity is missing its identifier.", nameof(scope));
        }

        var previous = entity.ConcurrencyToken;
        var updated = Guid.NewGuid().ToString();

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await session.BeginAsync(cancellationToken).ConfigureAwait(false);

        var affected = await session.ExecuteAsync(
            $@"UPDATE {Table(Schema.Scopes)} SET
                {Column("ConcurrencyToken")} = {Parameter("Updated")},
                {Column("Description")} = {Parameter("Description")},
                {Column("Descriptions")} = {Parameter("Descriptions")},
                {Column("DisplayName")} = {Parameter("DisplayName")},
                {Column("DisplayNames")} = {Parameter("DisplayNames")},
                {Column("Name")} = {Parameter("Name")},
                {Column("Properties")} = {Parameter("Properties")},
                {Column("Resources")} = {Parameter("Resources")}
            WHERE {Column("Id")} = {Parameter("Id")}
              AND {Dialect.BuildExactStringEquals(Column("ConcurrencyToken"), "Previous")}",
            new
            {
                Id = entity.Id,
                Previous = previous,
                Updated = updated,
                entity.Description,
                entity.Descriptions,
                entity.DisplayName,
                entity.DisplayNames,
                entity.Name,
                entity.Properties,
                entity.Resources,
            },
            transaction.Value, cancellationToken).ConfigureAwait(false);

        if (affected != 1)
        {
            throw OpenIddictStoreErrors.Concurrency(nameof(OpenIddictStoreScope));
        }

        await ReplaceResourcesAsync(
            session, transaction.Value, entity.Id!,
            OpenIddictStoreJson.DeserializeStrings(entity.Resources), cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        entity.ConcurrencyToken = updated;
        LogWritten(nameof(UpdateAsync), affected);
    }

    private static OpenIddictStoreScope Cast(TScope scope)
        => scope as OpenIddictStoreScope
           ?? throw new ArgumentException(
               $"The entity must be {nameof(OpenIddictStoreScope)} or a derived type.", nameof(scope));

    private static ValueTask<string?> GetStringAsync(
        TScope scope, Func<OpenIddictStoreScope, string?> selector)
        => new(selector(Cast(scope)));

    private static ValueTask SetStringAsync(
        TScope scope, Action<OpenIddictStoreScope, string?> setter, string? value)
    {
        setter(Cast(scope), value);

        return default;
    }

    private async ValueTask<TScope?> QueryOneAsync(
        OpenIddictStoreSession session, string where, object parameters, CancellationToken cancellationToken)
        => await session.QuerySingleOrDefaultAsync(
            $"SELECT * FROM {Table(Schema.Scopes)} {where}",
            parameters,
            reader => (TScope)OpenIddictStoreEntityMapper.Map(reader, typeof(TScope), Dialect),
            cancellationToken).ConfigureAwait(false);

    private async ValueTask ReplaceResourcesAsync(
        OpenIddictStoreSession session, System.Data.Common.DbTransaction transaction, string scopeId,
        ImmutableArray<string> resources, CancellationToken cancellationToken)
    {
        await session.ExecuteAsync(
            $"DELETE FROM {Table(Schema.ScopeResources)} WHERE {Column("ScopeId")} = {Parameter("scopeId")}",
            new { scopeId }, transaction, cancellationToken).ConfigureAwait(false);

        if (resources.IsDefaultOrEmpty)
        {
            return;
        }

        var distinct = resources.Distinct(StringComparer.Ordinal).ToList();

        for (var position = 0; position < distinct.Count; position++)
        {
            var value = distinct[position];

            await session.ExecuteAsync(
                $@"INSERT INTO {Table(Schema.ScopeResources)} (
                    {Column("ScopeId")}, {Column("ResourceHash")}, {Column("Resource")}, {Column("Position")})
                   VALUES ({Parameter("scopeId")}, {Parameter("hash")}, {Parameter("value")}, {Parameter("position")})",
                new
                {
                    scopeId,
                    hash = OpenIddictStoreSession.Hash(value),
                    value,
                    position,
                },
                transaction, cancellationToken).ConfigureAwait(false);
        }
    }

    private static OpenIddictStoreScope MapEntity(System.Data.Common.DbDataReader reader)
    {
        var entity = new OpenIddictStoreScope();

        for (var index = 0; index < reader.FieldCount; index++)
        {
            if (reader.IsDBNull(index))
            {
                continue;
            }

            switch (reader.GetName(index))
            {
                case "Id": entity.Id = reader.GetString(index); break;
                case "ConcurrencyToken": entity.ConcurrencyToken = reader.GetString(index); break;
                case "Description": entity.Description = reader.GetString(index); break;
                case "Descriptions": entity.Descriptions = reader.GetString(index); break;
                case "DisplayName": entity.DisplayName = reader.GetString(index); break;
                case "DisplayNames": entity.DisplayNames = reader.GetString(index); break;
                case "Name": entity.Name = reader.GetString(index); break;
                case "Properties": entity.Properties = reader.GetString(index); break;
                case "Resources": entity.Resources = reader.GetString(index); break;
            }
        }

        return entity;
    }
}

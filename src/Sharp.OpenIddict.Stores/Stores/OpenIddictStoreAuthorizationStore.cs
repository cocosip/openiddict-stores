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
/// The store for Authorization. Scopes are authoritative in JSON, and the AuthorizationScopes auxiliary table is only used for collection lookups.
/// </summary>
/// <typeparam name="TAuthorization">The entity type, which must be <see cref="OpenIddictStoreAuthorization"/> or a derived type.</typeparam>
public class OpenIddictStoreAuthorizationStore<TAuthorization> : OpenIddictStoreBase,
    IOpenIddictAuthorizationStore<TAuthorization>
    where TAuthorization : class
{
    /// <summary>Creates the store from its dependencies.</summary>
    public OpenIddictStoreAuthorizationStore(
        IOptions<OpenIddictStoreOptions> options,
        IOpenIddictStoreConnectionFactory connectionFactory,
        IOpenIddictStoreDialect dialect,
        ILogger<OpenIddictStoreAuthorizationStore<TAuthorization>> logger)
        : base(options, connectionFactory, dialect, logger)
    {
    }

    /// <inheritdoc />
    public virtual async ValueTask<long> CountAsync(CancellationToken cancellationToken)
    {
        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        return await session.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM {Table(Schema.Authorizations)}", null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<TAuthorization>, IQueryable<TResult>> query, CancellationToken cancellationToken)
        => CountQueryAsync<TAuthorization, TResult>(Schema.Authorizations, query, cancellationToken);

    /// <inheritdoc />
    public virtual async ValueTask CreateAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        if (authorization is not OpenIddictStoreAuthorization entity)
        {
            throw new ArgumentException(
                $"The entity must be {nameof(OpenIddictStoreAuthorization)} or a derived type.", nameof(authorization));
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
            $@"INSERT INTO {Table(Schema.Authorizations)} (
                {Column("Id")}, {Column("ConcurrencyToken")}, {Column("ApplicationId")}, {Column("CreationDate")},
                {Column("Properties")}, {Column("Scopes")}, {Column("Status")}, {Column("Subject")}, {Column("Type")})
            VALUES (
                {Parameter("Id")}, {Parameter("ConcurrencyToken")}, {Parameter("ApplicationId")}, {Parameter("CreationDate")},
                {Parameter("Properties")}, {Parameter("Scopes")}, {Parameter("Status")}, {Parameter("Subject")},
                {Parameter("Type")})",
            ToParameters(entity), transaction.Value, cancellationToken).ConfigureAwait(false);

        if (affected != 1)
        {
            throw new InvalidOperationException("Creating the Authorization wrote no rows.");
        }

        await ReplaceScopesAsync(
            session, transaction.Value, entity.Id!,
            OpenIddictStoreJson.DeserializeStrings(entity.Scopes), cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        LogWritten(nameof(CreateAsync), affected);
    }

    /// <inheritdoc />
    public virtual async ValueTask DeleteAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        if (authorization is not OpenIddictStoreAuthorization entity)
        {
            throw new ArgumentException(
                $"The entity must be {nameof(OpenIddictStoreAuthorization)} or a derived type.", nameof(authorization));
        }

        if (string.IsNullOrEmpty(entity.Id))
        {
            throw new ArgumentException("The entity is missing its identifier.", nameof(authorization));
        }

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await session.BeginAsync(cancellationToken).ConfigureAwait(false);

        // Take the root write lock before computing the related set, so a token created between the
        // check and the delete cannot survive the cascade (design 16.2.1, LIFE-02).
        var locked = await session.ExecuteAsync(
            $"UPDATE {Table(Schema.Authorizations)} SET {Column("ConcurrencyToken")} = {Column("ConcurrencyToken")} " +
            $"WHERE {Dialect.BuildExactStringEquals(Column("Id"), "Id")} AND {Dialect.BuildExactStringEquals(Column("ConcurrencyToken"), "ConcurrencyToken")}",
            new { Id = entity.Id, ConcurrencyToken = entity.ConcurrencyToken },
            transaction.Value, cancellationToken).ConfigureAwait(false);

        if (locked != 1)
        {
            throw OpenIddictStoreErrors.Concurrency(nameof(OpenIddictStoreAuthorization));
        }

        // Deleting an Authorization deletes every Token whose AuthorizationId points at it, even when
        // that token has no ApplicationId or belongs to another application (design 16.2.1). The
        // authorizations those tokens also reference are kept.
        await session.ExecuteAsync(
            $"DELETE FROM {Table(Schema.Tokens)} WHERE {Column("AuthorizationId")} = {Parameter("Id")}",
            new { Id = entity.Id }, transaction.Value, cancellationToken).ConfigureAwait(false);

        await session.ExecuteAsync(
            $"DELETE FROM {Table(Schema.AuthorizationScopes)} WHERE {Column("AuthorizationId")} = {Parameter("Id")}",
            new { Id = entity.Id }, transaction.Value, cancellationToken).ConfigureAwait(false);

        var affected = await session.ExecuteAsync(
            $"DELETE FROM {Table(Schema.Authorizations)} WHERE {Dialect.BuildExactStringEquals(Column("Id"), "Id")} " +
            $"AND {Dialect.BuildExactStringEquals(Column("ConcurrencyToken"), "ConcurrencyToken")}",
            new { Id = entity.Id, ConcurrencyToken = entity.ConcurrencyToken },
            transaction.Value, cancellationToken).ConfigureAwait(false);

        if (affected != 1)
        {
            throw OpenIddictStoreErrors.Concurrency(nameof(OpenIddictStoreAuthorization));
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        LogWritten(nameof(DeleteAsync), affected);
    }

    /// <inheritdoc />
    public virtual async IAsyncEnumerable<TAuthorization> FindAsync(
        string? subject, string? client, string? status, string? type, ImmutableArray<string>? scopes,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        // Optional filters are not applied when null or empty (design 16.1); client is the Application primary key.
        var conditions = new List<string>();
        var parameters = new Dictionary<string, object?>();

        if (!string.IsNullOrEmpty(subject))
        {
            conditions.Add(Dialect.BuildExactStringEquals(Column("Subject"), "subject"));
            parameters["subject"] = subject;
        }

        if (!string.IsNullOrEmpty(client))
        {
            conditions.Add(Dialect.BuildExactStringEquals(Column("ApplicationId"), "client"));
            parameters["client"] = client;
        }

        if (!string.IsNullOrEmpty(status))
        {
            conditions.Add(Dialect.BuildExactStringEquals(Column("Status"), "status"));
            parameters["status"] = status;
        }

        if (!string.IsNullOrEmpty(type))
        {
            conditions.Add(Dialect.BuildExactStringEquals(Column("Type"), "type"));
            parameters["type"] = type;
        }

        var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
        var sql = $"SELECT * FROM {Table(Schema.Authorizations)} {where}".TrimEnd();

        if (scopes is not { Length: > 0 })
        {
            await foreach (var row in session.StreamAsync(sql, parameters, MapEntity, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return (TAuthorization)(object)row;
            }

            yield break;
        }

        // RequiredScopes: the authorization must carry every distinct requested scope, not just one
        // of them (design 16.1). The auxiliary index narrows candidates; the authoritative JSON is
        // then rechecked with Ordinal comparison so a digest collision can never widen the result.
        var required = scopes.Value.Distinct(StringComparer.Ordinal).ToList();
        var filter = conditions.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", conditions);
        var chunkSize = Math.Max(1, Dialect.MaximumParameters - parameters.Count);

        List<string>? candidates = null;

        foreach (var group in Chunk(required, chunkSize))
        {
            var groupParameters = new Dictionary<string, object?>(parameters, StringComparer.Ordinal);
            var placeholders = new List<string>(group.Count);

            for (var index = 0; index < group.Count; index++)
            {
                var name = "scope" + index.ToString(CultureInfo.InvariantCulture);
                groupParameters[name] = OpenIddictStoreSession.Hash(group[index]);
                placeholders.Add(Parameter(name));
            }

            var idSql = $@"
                SELECT DISTINCT a.{Column("Id")} FROM {Table(Schema.Authorizations)} a
                INNER JOIN {Table(Schema.AuthorizationScopes)} i
                    ON i.{Column("AuthorizationId")} = a.{Column("Id")}
                WHERE i.{Column("ScopeHash")} IN ({string.Join(", ", placeholders)}){filter}";

            var ids = new List<string>();

            await foreach (var id in session
                .StreamAsync(idSql, groupParameters, reader => reader.GetString(0), cancellationToken)
                .ConfigureAwait(false))
            {
                ids.Add(id);
            }

            // Intersect across chunks: every requested scope must be present, so a row has to
            // survive every chunk. Only identifier strings are held in memory, never whole entities.
            candidates = candidates is null
                ? ids.ToList()
                : candidates.Intersect(ids, StringComparer.Ordinal).ToList();

            if (candidates.Count == 0)
            {
                yield break;
            }
        }

        foreach (var group in Chunk(candidates!, chunkSize))
        {
            var groupParameters = new Dictionary<string, object?>(StringComparer.Ordinal);
            var placeholders = new List<string>(group.Count);

            for (var index = 0; index < group.Count; index++)
            {
                var name = "id" + index.ToString(CultureInfo.InvariantCulture);
                groupParameters[name] = group[index];
                placeholders.Add(Parameter(name));
            }

            var pageSql = $"SELECT * FROM {Table(Schema.Authorizations)} " +
                          $"WHERE {Column("Id")} IN ({string.Join(", ", placeholders)}) ORDER BY {Column("Id")}";

            await foreach (var row in session.StreamAsync(pageSql, groupParameters, MapEntity, cancellationToken)
                .ConfigureAwait(false))
            {
                var granted = OpenIddictStoreJson.DeserializeStrings(row.Scopes);

                if (required.All(scope => granted.Contains(scope, StringComparer.Ordinal)))
                {
                    yield return (TAuthorization)(object)row;
                }
            }
        }
    }

    /// <inheritdoc />
    public virtual async IAsyncEnumerable<TAuthorization> FindByApplicationIdAsync(
        string identifier, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        OpenIddictStoreErrors.ThrowIfKeyIsNullOrEmpty(identifier, nameof(identifier));

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"SELECT * FROM {Table(Schema.Authorizations)} " +
                  $"WHERE {Dialect.BuildExactStringEquals(Column("ApplicationId"), "identifier")}";

        await foreach (var row in session.StreamAsync(sql, new { identifier }, MapEntity, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return (TAuthorization)(object)row;
        }
    }

    /// <inheritdoc />
    public virtual async ValueTask<TAuthorization?> FindByIdAsync(
        string identifier, CancellationToken cancellationToken)
    {
        OpenIddictStoreErrors.ThrowIfKeyIsNullOrEmpty(identifier, nameof(identifier));

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        return await session.QuerySingleOrDefaultAsync(
            $"SELECT * FROM {Table(Schema.Authorizations)} " +
            $"WHERE {Dialect.BuildExactStringEquals(Column("Id"), "identifier")}",
            new { identifier },
            reader => (TAuthorization)OpenIddictStoreEntityMapper.Map(reader, typeof(TAuthorization), Dialect),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async IAsyncEnumerable<TAuthorization> FindBySubjectAsync(
        string subject, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        OpenIddictStoreErrors.ThrowIfKeyIsNullOrEmpty(subject, nameof(subject));

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"SELECT * FROM {Table(Schema.Authorizations)} " +
                  $"WHERE {Dialect.BuildExactStringEquals(Column("Subject"), "subject")}";

        await foreach (var row in session.StreamAsync(sql, new { subject }, MapEntity, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return (TAuthorization)(object)row;
        }
    }

    /// <inheritdoc />
    public virtual ValueTask<string?> GetApplicationIdAsync(
        TAuthorization authorization, CancellationToken cancellationToken)
        => GetStringAsync(authorization, static entity => entity.ApplicationId);

    /// <inheritdoc />
    public virtual ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<TAuthorization>, TState, IQueryable<TResult>> query, TState state,
        CancellationToken cancellationToken)
        => GetQueryAsync<TAuthorization, TState, TResult>(Schema.Authorizations, query, state, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<DateTimeOffset?> GetCreationDateAsync(
        TAuthorization authorization, CancellationToken cancellationToken)
        => new(ToOffset(OpenIddictStoreJson.AsUtc(Cast(authorization).CreationDate)));

    /// <inheritdoc />
    public virtual ValueTask<string?> GetIdAsync(
        TAuthorization authorization, CancellationToken cancellationToken)
        => GetStringAsync(authorization, static entity => entity.Id);

    /// <inheritdoc />
    public virtual ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        TAuthorization authorization, CancellationToken cancellationToken)
        => new(OpenIddictStoreJson.DeserializeProperties(Cast(authorization).Properties));

    /// <inheritdoc />
    public virtual ValueTask<ImmutableArray<string>> GetScopesAsync(
        TAuthorization authorization, CancellationToken cancellationToken)
        => new(OpenIddictStoreJson.DeserializeStrings(Cast(authorization).Scopes));

    /// <inheritdoc />
    public virtual ValueTask<string?> GetStatusAsync(
        TAuthorization authorization, CancellationToken cancellationToken)
        => GetStringAsync(authorization, static entity => entity.Status);

    /// <inheritdoc />
    public virtual ValueTask<string?> GetSubjectAsync(
        TAuthorization authorization, CancellationToken cancellationToken)
        => GetStringAsync(authorization, static entity => entity.Subject);

    /// <inheritdoc />
    public virtual ValueTask<string?> GetTypeAsync(
        TAuthorization authorization, CancellationToken cancellationToken)
        => GetStringAsync(authorization, static entity => entity.Type);

    /// <inheritdoc />
    public virtual ValueTask<TAuthorization> InstantiateAsync(CancellationToken cancellationToken)
        => new(Activator.CreateInstance<TAuthorization>());

    /// <inheritdoc />
    public virtual async IAsyncEnumerable<TAuthorization> ListAsync(
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

        var sql = $"SELECT * FROM {Table(Schema.Authorizations)} ORDER BY {Column("Id")} " +
                  $"{Dialect.BuildPaging(count, offset)}".TrimEnd();

        await foreach (var row in session.StreamAsync(sql, null, MapEntity, cancellationToken).ConfigureAwait(false))
        {
            yield return (TAuthorization)(object)row;
        }
    }

    /// <inheritdoc />
    public virtual IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<TAuthorization>, TState, IQueryable<TResult>> query, TState state,
        CancellationToken cancellationToken)
        => ListQueryAsync<TAuthorization, TState, TResult>(Schema.Authorizations, query, state, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Dedicated prune path (design 16.3): an authorization qualifies only when its creation date is
    /// strictly before the threshold, its status is not <c>valid</c> or its type is <c>ad-hoc</c>, and
    /// no token still points at it. The NOT EXISTS guard is part of the final DELETE, so a token that
    /// appears between the check and the delete makes the row ineligible instead of cascading.
    /// </remarks>
    public virtual ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
        => PruneCoreAsync(BuildPruneSql, threshold, cancellationToken);

    /// <summary>Builds one batch DELETE for authorizations, carrying the full predicate (design 16.3).</summary>
    private string BuildPruneSql(Dictionary<string, object?> parameters)
    {
        var authorizations = Table(Schema.Authorizations);
        var tokens = Table(Schema.Tokens);

        parameters["valid"] = OpenIddictStoreStatuses.Valid;
        parameters["adhoc"] = OpenIddictStoreStatuses.AdHoc;

        // NULL counts as "not valid", so an authorization without a status is eligible.
        var notValid = Dialect.BuildExactStringNotEqualsIncludingNull("a." + Column("Status"), "valid");

        // The paged candidate subselect is wrapped in a derived table before it reaches the IN list:
        // MySQL rejects a LIMIT directly inside an IN subquery (error 1235), and the wrapper also
        // materializes the self-reference so the same-table DELETE stays allowed (error 1093).
        return $@"
            DELETE FROM {authorizations}
            WHERE {Column("Id")} IN (
                SELECT {Column("Id")} FROM (
                    SELECT a.{Column("Id")} FROM {authorizations} a
                    WHERE a.{Column("CreationDate")} IS NOT NULL
                      AND a.{Column("CreationDate")} < {Parameter("threshold")}
                      AND ({notValid} OR {Dialect.BuildExactStringEquals("a." + Column("Type"), "adhoc")})
                      AND NOT EXISTS (
                            SELECT 1 FROM {tokens} t
                            WHERE t.{Column("AuthorizationId")} = a.{Column("Id")})
                    ORDER BY a.{Column("Id")}
                    {Dialect.BuildPaging(PruneBatchSize, null)}
                ) batched)";
    }

    /// <inheritdoc />
    /// <remarks>Generic revocation does not add the "not already revoked" exclusion (design 16.2.2).</remarks>
    public virtual ValueTask<long> RevokeAsync(
        string? subject, string? client, string? status, string? type, CancellationToken cancellationToken)
        => RevokeCoreAsync(
            Schema.Authorizations, BuildRevokeConditions(subject, client, status, type),
            BuildRevokeParameters(subject, client, status, type), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Authorization revocation never excludes already revoked rows (design 16.2.2).</remarks>
    public virtual ValueTask<long> RevokeByApplicationIdAsync(
        string identifier, CancellationToken cancellationToken)
    {
        OpenIddictStoreErrors.ThrowIfKeyIsNullOrEmpty(identifier, nameof(identifier));

        return RevokeCoreAsync(
            Schema.Authorizations,
            [Dialect.BuildExactStringEquals(Column("ApplicationId"), "identifier")],
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["identifier"] = identifier },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>Authorization revocation never excludes already revoked rows (design 16.2.2).</remarks>
    public virtual ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken)
    {
        OpenIddictStoreErrors.ThrowIfKeyIsNullOrEmpty(subject, nameof(subject));

        return RevokeCoreAsync(
            Schema.Authorizations,
            [Dialect.BuildExactStringEquals(Column("Subject"), "subject")],
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["subject"] = subject },
            cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask SetApplicationIdAsync(
        TAuthorization authorization, string? identifier, CancellationToken cancellationToken)
        => SetStringAsync(authorization, static (entity, value) => entity.ApplicationId = value, identifier);

    /// <inheritdoc />
    public virtual ValueTask SetCreationDateAsync(
        TAuthorization authorization, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        Cast(authorization).CreationDate = OpenIddictStoreJson.NormalizeUtc(date);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetPropertiesAsync(
        TAuthorization authorization, ImmutableDictionary<string, JsonElement> properties,
        CancellationToken cancellationToken)
    {
        Cast(authorization).Properties = OpenIddictStoreJson.SerializeProperties(properties);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetScopesAsync(
        TAuthorization authorization, ImmutableArray<string> scopes, CancellationToken cancellationToken)
    {
        Cast(authorization).Scopes = OpenIddictStoreJson.SerializeStrings(scopes);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetStatusAsync(
        TAuthorization authorization, string? status, CancellationToken cancellationToken)
        => SetStringAsync(authorization, static (entity, value) => entity.Status = value, status);

    /// <inheritdoc />
    public virtual ValueTask SetSubjectAsync(
        TAuthorization authorization, string? subject, CancellationToken cancellationToken)
        => SetStringAsync(authorization, static (entity, value) => entity.Subject = value, subject);

    /// <inheritdoc />
    public virtual ValueTask SetTypeAsync(
        TAuthorization authorization, string? type, CancellationToken cancellationToken)
        => SetStringAsync(authorization, static (entity, value) => entity.Type = value, type);

    /// <inheritdoc />
    public virtual async ValueTask UpdateAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        if (authorization is not OpenIddictStoreAuthorization entity)
        {
            throw new ArgumentException(
                $"The entity must be {nameof(OpenIddictStoreAuthorization)} or a derived type.", nameof(authorization));
        }

        if (string.IsNullOrEmpty(entity.Id))
        {
            throw new ArgumentException("The entity is missing its identifier.", nameof(authorization));
        }

        var previous = entity.ConcurrencyToken;
        var updated = Guid.NewGuid().ToString();

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await session.BeginAsync(cancellationToken).ConfigureAwait(false);

        var parameters = ToParameters(entity);
        parameters["Previous"] = previous;
        parameters["Updated"] = updated;

        var affected = await session.ExecuteAsync(
            $@"UPDATE {Table(Schema.Authorizations)} SET
                {Column("ConcurrencyToken")} = {Parameter("Updated")},
                {Column("ApplicationId")} = {Parameter("ApplicationId")},
                {Column("CreationDate")} = {Parameter("CreationDate")},
                {Column("Properties")} = {Parameter("Properties")},
                {Column("Scopes")} = {Parameter("Scopes")},
                {Column("Status")} = {Parameter("Status")},
                {Column("Subject")} = {Parameter("Subject")},
                {Column("Type")} = {Parameter("Type")}
            WHERE {Dialect.BuildExactStringEquals(Column("Id"), "Id")}
              AND {Dialect.BuildExactStringEquals(Column("ConcurrencyToken"), "Previous")}",
            parameters, transaction.Value, cancellationToken).ConfigureAwait(false);

        if (affected != 1)
        {
            throw OpenIddictStoreErrors.Concurrency(nameof(OpenIddictStoreAuthorization));
        }

        await ReplaceScopesAsync(
            session, transaction.Value, entity.Id!,
            OpenIddictStoreJson.DeserializeStrings(entity.Scopes), cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        entity.ConcurrencyToken = updated;
        LogWritten(nameof(UpdateAsync), affected);
    }

    /// <summary>Builds the optional-filter predicates of the generic revocation method (design 16.2.2).</summary>
    private List<string> BuildRevokeConditions(string? subject, string? client, string? status, string? type)
    {
        var conditions = new List<string>();

        if (!string.IsNullOrEmpty(subject))
        {
            conditions.Add(Dialect.BuildExactStringEquals(Column("Subject"), "subject"));
        }

        if (!string.IsNullOrEmpty(client))
        {
            conditions.Add(Dialect.BuildExactStringEquals(Column("ApplicationId"), "client"));
        }

        if (!string.IsNullOrEmpty(status))
        {
            conditions.Add(Dialect.BuildExactStringEquals(Column("Status"), "status"));
        }

        if (!string.IsNullOrEmpty(type))
        {
            conditions.Add(Dialect.BuildExactStringEquals(Column("Type"), "type"));
        }

        return conditions;
    }

    private static Dictionary<string, object?> BuildRevokeParameters(
        string? subject, string? client, string? status, string? type)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (!string.IsNullOrEmpty(subject))
        {
            parameters["subject"] = subject;
        }

        if (!string.IsNullOrEmpty(client))
        {
            parameters["client"] = client;
        }

        if (!string.IsNullOrEmpty(status))
        {
            parameters["status"] = status;
        }

        if (!string.IsNullOrEmpty(type))
        {
            parameters["type"] = type;
        }

        return parameters;
    }

    private static OpenIddictStoreAuthorization Cast(TAuthorization authorization)
        => authorization as OpenIddictStoreAuthorization
           ?? throw new ArgumentException(
               $"The entity must be {nameof(OpenIddictStoreAuthorization)} or a derived type.", nameof(authorization));

    private static ValueTask<string?> GetStringAsync(
        TAuthorization authorization, Func<OpenIddictStoreAuthorization, string?> selector)
        => new(selector(Cast(authorization)));

    private static ValueTask SetStringAsync(
        TAuthorization authorization, Action<OpenIddictStoreAuthorization, string?> setter, string? value)
    {
        setter(Cast(authorization), value);

        return default;
    }

    private static DateTimeOffset? ToOffset(DateTime? value)
        => value is null ? null : new DateTimeOffset(value.Value, TimeSpan.Zero);

    private Dictionary<string, object?> ToParameters(OpenIddictStoreAuthorization entity)
        => new(StringComparer.Ordinal)
        {
            ["Id"] = entity.Id,
            ["ConcurrencyToken"] = entity.ConcurrencyToken,
            ["ApplicationId"] = entity.ApplicationId,

            // Dates are bound in the dialect's format and do not rely on the driver's default DateTime conversion.
            ["CreationDate"] = OpenIddictStoreDateParameter.Format(entity.CreationDate, Dialect),
            ["Properties"] = entity.Properties,
            ["Scopes"] = entity.Scopes,
            ["Status"] = entity.Status,
            ["Subject"] = entity.Subject,
            ["Type"] = entity.Type,
        };

    private async ValueTask ReplaceScopesAsync(
        OpenIddictStoreSession session, System.Data.Common.DbTransaction transaction, string authorizationId,
        ImmutableArray<string> scopes, CancellationToken cancellationToken)
    {
        await session.ExecuteAsync(
            $"DELETE FROM {Table(Schema.AuthorizationScopes)} WHERE {Column("AuthorizationId")} = {Parameter("authorizationId")}",
            new { authorizationId }, transaction, cancellationToken).ConfigureAwait(false);

        if (scopes.IsDefaultOrEmpty)
        {
            return;
        }

        var distinct = scopes.Distinct(StringComparer.Ordinal).ToList();

        for (var position = 0; position < distinct.Count; position++)
        {
            var value = distinct[position];

            await session.ExecuteAsync(
                $@"INSERT INTO {Table(Schema.AuthorizationScopes)} (
                    {Column("AuthorizationId")}, {Column("ScopeHash")}, {Column("Scope")}, {Column("Position")})
                   VALUES ({Parameter("authorizationId")}, {Parameter("hash")}, {Parameter("value")}, {Parameter("position")})",
                new
                {
                    authorizationId,
                    hash = OpenIddictStoreSession.Hash(value),
                    value,
                    position,
                },
                transaction, cancellationToken).ConfigureAwait(false);
        }
    }

    private OpenIddictStoreAuthorization MapEntity(System.Data.Common.DbDataReader reader)
    {
        var entity = new OpenIddictStoreAuthorization();

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
                case "ApplicationId": entity.ApplicationId = reader.GetString(index); break;
                case "CreationDate": entity.CreationDate = OpenIddictStoreDateParameter.Parse(reader, index, Dialect); break;
                case "Properties": entity.Properties = reader.GetString(index); break;
                case "Scopes": entity.Scopes = reader.GetString(index); break;
                case "Status": entity.Status = reader.GetString(index); break;
                case "Subject": entity.Subject = reader.GetString(index); break;
                case "Type": entity.Type = reader.GetString(index); break;
            }
        }

        return entity;
    }
}

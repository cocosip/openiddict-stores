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
/// The store for Token. Tokens have no collection columns, so no auxiliary index table is maintained.
/// </summary>
/// <typeparam name="TToken">The entity type, which must be <see cref="OpenIddictStoreToken"/> or a derived type.</typeparam>
public class OpenIddictStoreTokenStore<TToken> : OpenIddictStoreBase, IOpenIddictTokenStore<TToken>
    where TToken : class
{
    /// <summary>Creates the store from its dependencies.</summary>
    public OpenIddictStoreTokenStore(
        IOptions<OpenIddictStoreOptions> options,
        IOpenIddictStoreConnectionFactory connectionFactory,
        IOpenIddictStoreDialect dialect,
        ILogger<OpenIddictStoreTokenStore<TToken>> logger)
        : base(options, connectionFactory, dialect, logger)
    {
    }

    /// <inheritdoc />
    public virtual async ValueTask<long> CountAsync(CancellationToken cancellationToken)
    {
        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        return await session.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM {Table(Schema.Tokens)}", null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<TToken>, IQueryable<TResult>> query, CancellationToken cancellationToken)
        => CountQueryAsync<TToken, TResult>(Schema.Tokens, query, cancellationToken);

    /// <inheritdoc />
    public virtual async ValueTask CreateAsync(TToken token, CancellationToken cancellationToken)
    {
        if (token is not OpenIddictStoreToken entity)
        {
            throw new ArgumentException(
                $"The entity must be {nameof(OpenIddictStoreToken)} or a derived type.", nameof(token));
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
            $@"INSERT INTO {Table(Schema.Tokens)} (
                {Column("Id")}, {Column("ConcurrencyToken")}, {Column("ApplicationId")}, {Column("AuthorizationId")},
                {Column("CreationDate")}, {Column("ExpirationDate")}, {Column("Payload")}, {Column("Properties")},
                {Column("RedemptionDate")}, {Column("ReferenceId")}, {Column("Status")}, {Column("Subject")},
                {Column("Type")})
            VALUES (
                {Parameter("Id")}, {Parameter("ConcurrencyToken")}, {Parameter("ApplicationId")}, {Parameter("AuthorizationId")},
                {Parameter("CreationDate")}, {Parameter("ExpirationDate")}, {Parameter("Payload")}, {Parameter("Properties")},
                {Parameter("RedemptionDate")}, {Parameter("ReferenceId")}, {Parameter("Status")}, {Parameter("Subject")},
                {Parameter("Type")})",
            ToParameters(entity), cancellationToken).ConfigureAwait(false);

        if (affected != 1)
        {
            throw new InvalidOperationException("Creating the Token wrote no rows.");
        }

        LogWritten(nameof(CreateAsync), affected);
    }

    /// <inheritdoc />
    public virtual async ValueTask DeleteAsync(TToken token, CancellationToken cancellationToken)
    {
        if (token is not OpenIddictStoreToken entity)
        {
            throw new ArgumentException(
                $"The entity must be {nameof(OpenIddictStoreToken)} or a derived type.", nameof(token));
        }

        if (string.IsNullOrEmpty(entity.Id))
        {
            throw new ArgumentException("The entity is missing its identifier.", nameof(token));
        }

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        // Deleting a single Token needs no related collections (design 16.2.1): only that row is deleted, by Id and the previous token.
        var affected = await session.ExecuteAsync(
            $"DELETE FROM {Table(Schema.Tokens)} WHERE {Dialect.BuildExactStringEquals(Column("Id"), "Id")} " +
            $"AND {Dialect.BuildExactStringEquals(Column("ConcurrencyToken"), "ConcurrencyToken")}",
            new { Id = entity.Id, ConcurrencyToken = entity.ConcurrencyToken }, cancellationToken)
            .ConfigureAwait(false);

        if (affected == 0)
        {
            throw OpenIddictStoreErrors.Concurrency(nameof(OpenIddictStoreToken));
        }

        LogWritten(nameof(DeleteAsync), affected);
    }

    /// <inheritdoc />
    public virtual async IAsyncEnumerable<TToken> FindAsync(
        string? subject, string? client, string? status, string? type,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        // Optional filters are not applied when null or empty (design 16.1); client is the Application primary key.
        var conditions = new List<string>();
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);

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
        var sql = $"SELECT * FROM {Table(Schema.Tokens)} {where}".TrimEnd();

        await foreach (var row in session.StreamAsync(sql, parameters, MapEntity, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return (TToken)(object)row;
        }
    }

    /// <inheritdoc />
    public virtual async IAsyncEnumerable<TToken> FindByApplicationIdAsync(
        string identifier, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        OpenIddictStoreErrors.ThrowIfKeyIsNullOrEmpty(identifier, nameof(identifier));

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"SELECT * FROM {Table(Schema.Tokens)} " +
                  $"WHERE {Dialect.BuildExactStringEquals(Column("ApplicationId"), "identifier")}";

        await foreach (var row in session.StreamAsync(sql, new { identifier }, MapEntity, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return (TToken)(object)row;
        }
    }

    /// <inheritdoc />
    public virtual async IAsyncEnumerable<TToken> FindByAuthorizationIdAsync(
        string identifier, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        OpenIddictStoreErrors.ThrowIfKeyIsNullOrEmpty(identifier, nameof(identifier));

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"SELECT * FROM {Table(Schema.Tokens)} " +
                  $"WHERE {Dialect.BuildExactStringEquals(Column("AuthorizationId"), "identifier")}";

        await foreach (var row in session.StreamAsync(sql, new { identifier }, MapEntity, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return (TToken)(object)row;
        }
    }

    /// <inheritdoc />
    public virtual async ValueTask<TToken?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        OpenIddictStoreErrors.ThrowIfKeyIsNullOrEmpty(identifier, nameof(identifier));

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        return await session.QuerySingleOrDefaultAsync(
            $"SELECT * FROM {Table(Schema.Tokens)} " +
            $"WHERE {Dialect.BuildExactStringEquals(Column("Id"), "identifier")}",
            new { identifier },
            reader => (TToken)OpenIddictStoreEntityMapper.Map(reader, typeof(TToken), Dialect),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<TToken?> FindByReferenceIdAsync(
        string identifier, CancellationToken cancellationToken)
    {
        OpenIddictStoreErrors.ThrowIfKeyIsNullOrEmpty(identifier, nameof(identifier));

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        return await session.QuerySingleOrDefaultAsync(
            $"SELECT * FROM {Table(Schema.Tokens)} " +
            $"WHERE {Dialect.BuildExactStringEquals(Column("ReferenceId"), "identifier")}",
            new { identifier },
            reader => (TToken)OpenIddictStoreEntityMapper.Map(reader, typeof(TToken), Dialect),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async IAsyncEnumerable<TToken> FindBySubjectAsync(
        string subject, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        OpenIddictStoreErrors.ThrowIfKeyIsNullOrEmpty(subject, nameof(subject));

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"SELECT * FROM {Table(Schema.Tokens)} " +
                  $"WHERE {Dialect.BuildExactStringEquals(Column("Subject"), "subject")}";

        await foreach (var row in session.StreamAsync(sql, new { subject }, MapEntity, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return (TToken)(object)row;
        }
    }

    /// <inheritdoc />
    public virtual ValueTask<string?> GetApplicationIdAsync(TToken token, CancellationToken cancellationToken)
        => GetStringAsync(token, static entity => entity.ApplicationId);

    /// <inheritdoc />
    public virtual ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<TToken>, TState, IQueryable<TResult>> query, TState state,
        CancellationToken cancellationToken)
        => GetQueryAsync<TToken, TState, TResult>(Schema.Tokens, query, state, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<string?> GetAuthorizationIdAsync(TToken token, CancellationToken cancellationToken)
        => GetStringAsync(token, static entity => entity.AuthorizationId);

    /// <inheritdoc />
    public virtual ValueTask<DateTimeOffset?> GetCreationDateAsync(
        TToken token, CancellationToken cancellationToken)
        => new(ToOffset(OpenIddictStoreJson.AsUtc(Cast(token).CreationDate)));

    /// <inheritdoc />
    public virtual ValueTask<DateTimeOffset?> GetExpirationDateAsync(
        TToken token, CancellationToken cancellationToken)
        => new(ToOffset(OpenIddictStoreJson.AsUtc(Cast(token).ExpirationDate)));

    /// <inheritdoc />
    public virtual ValueTask<string?> GetIdAsync(TToken token, CancellationToken cancellationToken)
        => GetStringAsync(token, static entity => entity.Id);

    /// <inheritdoc />
    public virtual ValueTask<string?> GetPayloadAsync(TToken token, CancellationToken cancellationToken)
        => GetStringAsync(token, static entity => entity.Payload);

    /// <inheritdoc />
    public virtual ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        TToken token, CancellationToken cancellationToken)
        => new(OpenIddictStoreJson.DeserializeProperties(Cast(token).Properties));

    /// <inheritdoc />
    public virtual ValueTask<DateTimeOffset?> GetRedemptionDateAsync(
        TToken token, CancellationToken cancellationToken)
        => new(ToOffset(OpenIddictStoreJson.AsUtc(Cast(token).RedemptionDate)));

    /// <inheritdoc />
    public virtual ValueTask<string?> GetReferenceIdAsync(TToken token, CancellationToken cancellationToken)
        => GetStringAsync(token, static entity => entity.ReferenceId);

    /// <inheritdoc />
    public virtual ValueTask<string?> GetStatusAsync(TToken token, CancellationToken cancellationToken)
        => GetStringAsync(token, static entity => entity.Status);

    /// <inheritdoc />
    public virtual ValueTask<string?> GetSubjectAsync(TToken token, CancellationToken cancellationToken)
        => GetStringAsync(token, static entity => entity.Subject);

    /// <inheritdoc />
    public virtual ValueTask<string?> GetTypeAsync(TToken token, CancellationToken cancellationToken)
        => GetStringAsync(token, static entity => entity.Type);

    /// <inheritdoc />
    public virtual ValueTask<TToken> InstantiateAsync(CancellationToken cancellationToken)
        => new(Activator.CreateInstance<TToken>());

    /// <inheritdoc />
    public virtual async IAsyncEnumerable<TToken> ListAsync(
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

        var sql = $"SELECT * FROM {Table(Schema.Tokens)} ORDER BY {Column("Id")} " +
                  $"{Dialect.BuildPaging(count, offset)}".TrimEnd();

        await foreach (var row in session.StreamAsync(sql, null, MapEntity, cancellationToken).ConfigureAwait(false))
        {
            yield return (TToken)(object)row;
        }
    }

    /// <inheritdoc />
    public virtual IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<TToken>, TState, IQueryable<TResult>> query, TState state,
        CancellationToken cancellationToken)
        => ListQueryAsync<TToken, TState, TResult>(Schema.Tokens, query, state, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Dedicated prune path (design 16.3). A token qualifies only when its creation date is strictly
    /// before the threshold and either its status is neither <c>inactive</c> nor <c>valid</c>, or it
    /// really has an authorization whose status is not <c>valid</c>, or it is already expired. NULL
    /// counts as "not equal" for every status comparison, which a plain SQL <c>&lt;&gt;</c> would not do.
    /// </remarks>
    public virtual ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        // One UTC now for the whole call, so the expiry branch cannot drift between batches.
        var now = OpenIddictStoreJson.NormalizeUtc(DateTimeOffset.UtcNow)!;

        return PruneCoreAsync(parameters => BuildPruneSql(parameters, now), threshold, cancellationToken);
    }

    /// <summary>Builds one batch DELETE for tokens, carrying the full predicate (design 16.3).</summary>
    private string BuildPruneSql(Dictionary<string, object?> parameters, DateTime? now)
    {
        var tokens = Table(Schema.Tokens);
        var authorizations = Table(Schema.Authorizations);

        parameters["inactive"] = OpenIddictStoreStatuses.Inactive;
        parameters["valid"] = OpenIddictStoreStatuses.Valid;
        parameters["now"] = Dialect.FormatDate(now);

        // Each helper already treats NULL as "not equal", so a NULL status passes both conditions
        // and therefore counts as neither inactive nor valid, exactly as the design requires.
        var notInactive = Dialect.BuildExactStringNotEqualsIncludingNull("t." + Column("Status"), "inactive");
        var notValid = Dialect.BuildExactStringNotEqualsIncludingNull("t." + Column("Status"), "valid");
        var authorizationNotValid =
            Dialect.BuildExactStringNotEqualsIncludingNull("a." + Column("Status"), "valid");

        // The paged candidate subselect is wrapped in a derived table before it reaches the IN list:
        // MySQL rejects a LIMIT directly inside an IN subquery (error 1235), and the wrapper also
        // materializes the self-reference so the same-table DELETE stays allowed (error 1093).
        return $@"
            DELETE FROM {tokens}
            WHERE {Column("Id")} IN (
                SELECT {Column("Id")} FROM (
                    SELECT t.{Column("Id")} FROM {tokens} t
                    WHERE t.{Column("CreationDate")} IS NOT NULL
                      AND t.{Column("CreationDate")} < {Parameter("threshold")}
                      AND (
                            ({notInactive} AND {notValid})
                         OR EXISTS (
                                SELECT 1 FROM {authorizations} a
                                WHERE a.{Column("Id")} = t.{Column("AuthorizationId")}
                                  AND {authorizationNotValid})
                         OR (t.{Column("ExpirationDate")} IS NOT NULL AND t.{Column("ExpirationDate")} < {Parameter("now")})
                      )
                    ORDER BY t.{Column("Id")}
                    {Dialect.BuildPaging(PruneBatchSize, null)}
                ) batched)";
    }

    /// <inheritdoc />
    /// <remarks>Generic revocation does not add the "not already revoked" exclusion (design 16.2.2).</remarks>
    public virtual ValueTask<long> RevokeAsync(
        string? subject, string? client, string? status, string? type, CancellationToken cancellationToken)
        => RevokeCoreAsync(
            Schema.Tokens, BuildRevokeConditions(subject, client, status, type, excludeRevoked: false),
            BuildRevokeParameters(subject, client, status, type), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Excludes rows whose status is exactly <c>revoked</c>, including the NULL branch (design 16.2.2).</remarks>
    public virtual ValueTask<long> RevokeByApplicationIdAsync(
        string identifier, CancellationToken cancellationToken)
    {
        OpenIddictStoreErrors.ThrowIfKeyIsNullOrEmpty(identifier, nameof(identifier));

        var conditions = new List<string>
        {
            Dialect.BuildExactStringEquals(Column("ApplicationId"), "identifier"),
            Dialect.BuildExactStringNotEqualsIncludingNull(Column("Status"), "revokedStatus"),
        };

        return RevokeCoreAsync(
            Schema.Tokens, conditions,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["identifier"] = identifier,
                ["revokedStatus"] = OpenIddictStoreStatuses.Revoked,
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>Excludes rows whose status is exactly <c>revoked</c>, including the NULL branch (design 16.2.2).</remarks>
    public virtual ValueTask<long> RevokeByAuthorizationIdAsync(
        string identifier, CancellationToken cancellationToken)
    {
        OpenIddictStoreErrors.ThrowIfKeyIsNullOrEmpty(identifier, nameof(identifier));

        var conditions = new List<string>
        {
            Dialect.BuildExactStringEquals(Column("AuthorizationId"), "identifier"),
            Dialect.BuildExactStringNotEqualsIncludingNull(Column("Status"), "revokedStatus"),
        };

        return RevokeCoreAsync(
            Schema.Tokens, conditions,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["identifier"] = identifier,
                ["revokedStatus"] = OpenIddictStoreStatuses.Revoked,
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>Excludes rows whose status is exactly <c>revoked</c>, including the NULL branch (design 16.2.2).</remarks>
    public virtual ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken)
    {
        OpenIddictStoreErrors.ThrowIfKeyIsNullOrEmpty(subject, nameof(subject));

        var conditions = new List<string>
        {
            Dialect.BuildExactStringEquals(Column("Subject"), "subject"),
            Dialect.BuildExactStringNotEqualsIncludingNull(Column("Status"), "revokedStatus"),
        };

        return RevokeCoreAsync(
            Schema.Tokens, conditions,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["subject"] = subject,
                ["revokedStatus"] = OpenIddictStoreStatuses.Revoked,
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask SetApplicationIdAsync(
        TToken token, string? identifier, CancellationToken cancellationToken)
        => SetStringAsync(token, static (entity, value) => entity.ApplicationId = value, identifier);

    /// <inheritdoc />
    public virtual ValueTask SetAuthorizationIdAsync(
        TToken token, string? identifier, CancellationToken cancellationToken)
        => SetStringAsync(token, static (entity, value) => entity.AuthorizationId = value, identifier);

    /// <inheritdoc />
    public virtual ValueTask SetCreationDateAsync(
        TToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        Cast(token).CreationDate = OpenIddictStoreJson.NormalizeUtc(date);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetExpirationDateAsync(
        TToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        Cast(token).ExpirationDate = OpenIddictStoreJson.NormalizeUtc(date);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetPayloadAsync(TToken token, string? payload, CancellationToken cancellationToken)
        => SetStringAsync(token, static (entity, value) => entity.Payload = value, payload);

    /// <inheritdoc />
    public virtual ValueTask SetPropertiesAsync(
        TToken token, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
    {
        Cast(token).Properties = OpenIddictStoreJson.SerializeProperties(properties);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetRedemptionDateAsync(
        TToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        Cast(token).RedemptionDate = OpenIddictStoreJson.NormalizeUtc(date);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetReferenceIdAsync(
        TToken token, string? identifier, CancellationToken cancellationToken)
        => SetStringAsync(token, static (entity, value) => entity.ReferenceId = value, identifier);

    /// <inheritdoc />
    public virtual ValueTask SetStatusAsync(TToken token, string? status, CancellationToken cancellationToken)
        => SetStringAsync(token, static (entity, value) => entity.Status = value, status);

    /// <inheritdoc />
    public virtual ValueTask SetSubjectAsync(TToken token, string? subject, CancellationToken cancellationToken)
        => SetStringAsync(token, static (entity, value) => entity.Subject = value, subject);

    /// <inheritdoc />
    public virtual ValueTask SetTypeAsync(TToken token, string? type, CancellationToken cancellationToken)
        => SetStringAsync(token, static (entity, value) => entity.Type = value, type);

    /// <inheritdoc />
    public virtual async ValueTask UpdateAsync(TToken token, CancellationToken cancellationToken)
    {
        if (token is not OpenIddictStoreToken entity)
        {
            throw new ArgumentException(
                $"The entity must be {nameof(OpenIddictStoreToken)} or a derived type.", nameof(token));
        }

        if (string.IsNullOrEmpty(entity.Id))
        {
            throw new ArgumentException("The entity is missing its identifier.", nameof(token));
        }

        var previous = entity.ConcurrencyToken;
        var updated = Guid.NewGuid().ToString();

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        var parameters = ToParameters(entity);
        parameters["Previous"] = previous;
        parameters["Updated"] = updated;

        var affected = await session.ExecuteAsync(
            $@"UPDATE {Table(Schema.Tokens)} SET
                {Column("ConcurrencyToken")} = {Parameter("Updated")},
                {Column("ApplicationId")} = {Parameter("ApplicationId")},
                {Column("AuthorizationId")} = {Parameter("AuthorizationId")},
                {Column("CreationDate")} = {Parameter("CreationDate")},
                {Column("ExpirationDate")} = {Parameter("ExpirationDate")},
                {Column("Payload")} = {Parameter("Payload")},
                {Column("Properties")} = {Parameter("Properties")},
                {Column("RedemptionDate")} = {Parameter("RedemptionDate")},
                {Column("ReferenceId")} = {Parameter("ReferenceId")},
                {Column("Status")} = {Parameter("Status")},
                {Column("Subject")} = {Parameter("Subject")},
                {Column("Type")} = {Parameter("Type")}
            WHERE {Dialect.BuildExactStringEquals(Column("Id"), "Id")}
              AND {Dialect.BuildExactStringEquals(Column("ConcurrencyToken"), "Previous")}",
            parameters, cancellationToken).ConfigureAwait(false);

        if (affected != 1)
        {
            throw OpenIddictStoreErrors.Concurrency(nameof(OpenIddictStoreToken));
        }

        entity.ConcurrencyToken = updated;
        LogWritten(nameof(UpdateAsync), affected);
    }

    /// <summary>Builds the optional-filter predicates shared by the generic revocation methods (design 16.2.2).</summary>
    private List<string> BuildRevokeConditions(
        string? subject, string? client, string? status, string? type, bool excludeRevoked)
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

        if (excludeRevoked)
        {
            conditions.Add(Dialect.BuildExactStringNotEqualsIncludingNull(Column("Status"), "revokedStatus"));
        }

        return conditions;
    }

    private Dictionary<string, object?> BuildRevokeParameters(
        string? subject, string? client, string? status, string? type)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["revokedStatus"] = OpenIddictStoreStatuses.Revoked,
        };

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

    private static OpenIddictStoreToken Cast(TToken token)
        => token as OpenIddictStoreToken
           ?? throw new ArgumentException(
               $"The entity must be {nameof(OpenIddictStoreToken)} or a derived type.", nameof(token));

    private static ValueTask<string?> GetStringAsync(
        TToken token, Func<OpenIddictStoreToken, string?> selector)
        => new(selector(Cast(token)));

    private static ValueTask SetStringAsync(
        TToken token, Action<OpenIddictStoreToken, string?> setter, string? value)
    {
        setter(Cast(token), value);

        return default;
    }

    private static DateTimeOffset? ToOffset(DateTime? value)
        => value is null ? null : new DateTimeOffset(value.Value, TimeSpan.Zero);

    private Dictionary<string, object?> ToParameters(OpenIddictStoreToken entity)
        => new(StringComparer.Ordinal)
        {
            ["Id"] = entity.Id,
            ["ConcurrencyToken"] = entity.ConcurrencyToken,
            ["ApplicationId"] = entity.ApplicationId,
            ["AuthorizationId"] = entity.AuthorizationId,
            ["CreationDate"] = OpenIddictStoreDateParameter.Format(entity.CreationDate, Dialect),
            ["ExpirationDate"] = OpenIddictStoreDateParameter.Format(entity.ExpirationDate, Dialect),
            ["Payload"] = entity.Payload,
            ["Properties"] = entity.Properties,
            ["RedemptionDate"] = OpenIddictStoreDateParameter.Format(entity.RedemptionDate, Dialect),
            ["ReferenceId"] = entity.ReferenceId,
            ["Status"] = entity.Status,
            ["Subject"] = entity.Subject,
            ["Type"] = entity.Type,
        };

    private OpenIddictStoreToken MapEntity(System.Data.Common.DbDataReader reader)
    {
        var entity = new OpenIddictStoreToken();

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
                case "AuthorizationId": entity.AuthorizationId = reader.GetString(index); break;
                case "CreationDate": entity.CreationDate = OpenIddictStoreDateParameter.Parse(reader, index, Dialect); break;
                case "ExpirationDate": entity.ExpirationDate = OpenIddictStoreDateParameter.Parse(reader, index, Dialect); break;
                case "Payload": entity.Payload = reader.GetString(index); break;
                case "Properties": entity.Properties = reader.GetString(index); break;
                case "RedemptionDate": entity.RedemptionDate = OpenIddictStoreDateParameter.Parse(reader, index, Dialect); break;
                case "ReferenceId": entity.ReferenceId = reader.GetString(index); break;
                case "Status": entity.Status = reader.GetString(index); break;
                case "Subject": entity.Subject = reader.GetString(index); break;
                case "Type": entity.Type = reader.GetString(index); break;
            }
        }

        return entity;
    }
}

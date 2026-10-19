using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Sharp.OpenIddict.Stores.Queries;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sharp.OpenIddict.Stores;

/// <summary>
/// Common store infrastructure: resolves options, obtains the dialect, creates sessions, and writes logs that contain no sensitive information.
/// </summary>
public abstract class OpenIddictStoreBase
{
    private readonly IOptions<OpenIddictStoreOptions> _options;
    private readonly IOpenIddictStoreConnectionFactory _connectionFactory;
    private readonly IOpenIddictStoreDialect _dialect;

    /// <summary>Lazily created so a store that is resolved but never used opens no connection.</summary>
    private OpenIddictStoreSchemaVersionGuard? _schemaVersion;

    /// <summary>Creates the store from its dependencies.</summary>
    protected OpenIddictStoreBase(
        IOptions<OpenIddictStoreOptions> options,
        IOpenIddictStoreConnectionFactory connectionFactory,
        IOpenIddictStoreDialect dialect,
        ILogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Diagnostic log; connection strings, keys, payloads, and subject personal information are never logged.</summary>
    protected ILogger Logger { get; }

    /// <summary>The currently effective configuration.</summary>
    protected OpenIddictStoreOptions Options => _options.Value;

    /// <summary>The dialect bound to the current store.</summary>
    protected IOpenIddictStoreDialect Dialect => _dialect;

    /// <summary>Table names and schema.</summary>
    internal OpenIddictStoreSchema Schema => new(Options.TablePrefix, Options.Schema);

    /// <summary>
    /// Creates a one-shot session (a new connection). The caller must dispose it. The schema version is
    /// verified before the first session this store configuration opens, so an incompatible database is
    /// rejected before any read or write touches it (design 17).
    /// </summary>
    internal async ValueTask<OpenIddictStoreSession> CreateSessionAsync(CancellationToken cancellationToken)
    {
        await SchemaVersion.EnsureVerifiedAsync(cancellationToken).ConfigureAwait(false);

        return await OpenIddictStoreSession.CreateAsync(
            _connectionFactory, _dialect, Schema, Options.CommandTimeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The version guard for this configuration. It is created lazily and kept on the store instance, which is
    /// Scoped, so the verified flag lives for exactly one store configuration.
    /// </summary>
    private OpenIddictStoreSchemaVersionGuard SchemaVersion
        => _schemaVersion ??= new OpenIddictStoreSchemaVersionGuard(
            Schema, _dialect, _connectionFactory, Options.CommandTimeout);

    /// <summary>Quotes a table name, including the schema when necessary.</summary>
    protected string Table(string name)
        => Schema.Name is null
            ? Dialect.QuoteIdentifier(name)
            : Dialect.QuoteIdentifier(Schema.Name) + "." + Dialect.QuoteIdentifier(name);

    /// <summary>Quotes a column name.</summary>
    protected string Column(string name) => Dialect.QuoteIdentifier(name);

    /// <summary>Logs the operation name and affected row count of a successful write, without business data.</summary>
    protected void LogWritten(string operation, int affected)
        => Logger.LogDebug("{Operation} completed with {Affected} rows affected.", operation, affected);

    /// <summary>
    /// Runs one conditional bulk UPDATE that marks the matched rows as revoked and rotates their
    /// concurrency token (design 16.2.2). A single statement is used on purpose: the caller never
    /// loads the entities, and only rows satisfying the full predicate are touched.
    /// </summary>
    /// <param name="table">Table name without prefix; it is quoted by the store.</param>
    /// <param name="conditions">Already built predicates; they are ANDed together.</param>
    /// <param name="parameters">Parameters used by <paramref name="conditions"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected async ValueTask<long> RevokeCoreAsync(
        string table,
        IReadOnlyList<string> conditions,
        IDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        // A caller-supplied token cannot be reused for the rotated value: every matched row gets
        // the same fresh token, which is allowed to repeat across rows (design 16.2.2).
        var rotated = Guid.NewGuid().ToString();

        var predicate = conditions.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", conditions);

        var sql = $"UPDATE {Table(table)} SET " +
                  $"{Column("Status")} = @revoked, {Column("ConcurrencyToken")} = @rotated{predicate}";

        var arguments = new Dictionary<string, object?>(parameters, StringComparer.Ordinal)
        {
            ["revoked"] = OpenIddictStoreStatuses.Revoked,
            ["rotated"] = rotated,
        };

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        var affected = await session.ExecuteAsync(sql, arguments, cancellationToken).ConfigureAwait(false);

        LogWritten(nameof(RevokeCoreAsync), affected);

        return affected;
    }

    /// <summary>
    /// Runs the dedicated prune loop (design 16.3): at most 1,000 batches of at most 500 rows, each
    /// batch committed on its own, stopping as soon as a batch deletes nothing.
    /// </summary>
    /// <param name="buildDelete">
    /// Builds the batch DELETE. It receives the parameter dictionary (already containing the
    /// threshold and batch size) and must add the full prune predicate itself, including the
    /// NOT EXISTS guard, so a row that stopped qualifying is never removed.
    /// </param>
    /// <param name="threshold">The captured UTC threshold; rows at or after it are never pruned.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected async ValueTask<long> PruneCoreAsync(
        Func<Dictionary<string, object?>, string> buildDelete,
        DateTimeOffset threshold,
        CancellationToken cancellationToken)
    {
        var date = OpenIddictStoreJson.NormalizeUtc(threshold)!;
        var total = 0L;

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        for (var batch = 0; batch < PruneBatchCount; batch++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["threshold"] = Dialect.FormatDate(date),
                ["batchSize"] = PruneBatchSize,
            };

            var sql = buildDelete(parameters);

            // Each batch runs in its own transaction: a failure rolls back that batch only and the
            // exception propagates, so a caller never sees a partially applied batch as success.
            await using var transaction = await session.BeginAsync(cancellationToken).ConfigureAwait(false);

            var affected = await session.ExecuteAsync(sql, parameters, transaction.Value, cancellationToken)
                .ConfigureAwait(false);

            if (affected == 0)
            {
                break;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            total += affected;
        }

        LogWritten(nameof(PruneCoreAsync), (int)total);

        return total;
    }

    /// <summary>Maximum number of rows removed by a single prune batch (design 16.3).</summary>
    internal const int PruneBatchSize = 500;

    /// <summary>Maximum number of batches a single prune call may run (design 16.3).</summary>
    internal const int PruneBatchCount = 1_000;

    /// <summary>
    /// Executes a translated query callback and returns the number of matching rows. COUNT wraps the
    /// translated query, so a Skip/Take inside the callback is respected (design 9).
    /// </summary>
    internal async ValueTask<long> CountQueryAsync<TEntity, TResult>(
        string table,
        Func<IQueryable<TEntity>, IQueryable<TResult>> query,
        CancellationToken cancellationToken)
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        var plan = CreateQueryBuilder<TEntity>().Build<TResult>(table, Record(query), forCount: true);
        var sql = $"SELECT COUNT(*) FROM ({plan.Sql}) AS translated";

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        return await session.ExecuteScalarAsync<long>(sql, plan.Parameters, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a translated query callback and returns its first projected value, or the default when the
    /// translated query yields nothing (design 9: Get takes the first item).
    /// </summary>
    internal async ValueTask<TResult?> GetQueryAsync<TEntity, TState, TResult>(
        string table,
        Func<IQueryable<TEntity>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        var plan = CreateQueryBuilder<TEntity>().Build<TResult>(table, Record(query, state));

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        // Both shapes go through the same reader path, so an entity projection and a scalar projection
        // differ only in how the first row is materialized.
        await foreach (var row in session
            .StreamAsync(plan.Sql, plan.Parameters, reader => MapQueryRow<TResult>(reader, plan), cancellationToken)
            .ConfigureAwait(false))
        {
            return row;
        }

        return default;
    }

    /// <summary>Executes a translated query callback as a true row-by-row stream (design 9, QUERY-03).</summary>
    internal async IAsyncEnumerable<TResult> ListQueryAsync<TEntity, TState, TResult>(
        string table,
        Func<IQueryable<TEntity>, TState, IQueryable<TResult>> query,
        TState state,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var plan = CreateQueryBuilder<TEntity>().Build<TResult>(table, Record(query, state));

        await using var session = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        await foreach (var row in session
            .StreamAsync(plan.Sql, plan.Parameters, reader => MapQueryRow<TResult>(reader, plan), cancellationToken)
            .ConfigureAwait(false))
        {
            yield return row;
        }
    }

    private OpenIddictStoreQueryBuilder<TEntity> CreateQueryBuilder<TEntity>()
        => new(Dialect, Table, Column);

    /// <summary>
    /// Runs the callback against a recording queryable so its operators become an expression tree. The
    /// callback never sees real data: the queryable refuses enumeration, so nothing is loaded in memory.
    /// </summary>
    private static Expression Record<TEntity, TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>> query)
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        var provider = new OpenIddictStoreQueryProvider();
        var root = new OpenIddictStoreQueryable<TEntity>(
            provider, Expression.Constant(null, typeof(OpenIddictStoreQueryable<TEntity>)));

        var result = query(root)
            ?? throw new NotSupportedException("The query callback returned null.");

        return result.Expression;
    }

    /// <summary>Records a callback that also takes a state value.</summary>
    private static Expression Record<TEntity, TState, TResult>(
        Func<IQueryable<TEntity>, TState, IQueryable<TResult>> query,
        TState state)
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        var provider = new OpenIddictStoreQueryProvider();
        var root = new OpenIddictStoreQueryable<TEntity>(
            provider, Expression.Constant(null, typeof(OpenIddictStoreQueryable<TEntity>)));

        var result = query(root, state)
            ?? throw new NotSupportedException("The query callback returned null.");

        return result.Expression;
    }

    private static TResult MapQueryRow<TResult>(
        System.Data.Common.DbDataReader reader, OpenIddictStoreQueryPlan plan)
    {
        var value = plan.Kind == OpenIddictStoreProjectionKind.Entity
            ? OpenIddictStoreEntityMapper.Map(reader, typeof(TResult), plan.Dialect)
            : OpenIddictStoreEntityMapper.ReadProjection(reader, typeof(TResult), plan.Dialect);

        return (TResult)value!;
    }
}

/// <summary>Status values used by the bulk revocation predicates (design 16.2.2).</summary>
internal static class OpenIddictStoreStatuses
{
    /// <summary>The revoked status value.</summary>
    public const string Revoked = "revoked";

    /// <summary>The valid status value.</summary>
    public const string Valid = "valid";

    /// <summary>The inactive status value.</summary>
    public const string Inactive = "inactive";

    /// <summary>The ad-hoc authorization type value.</summary>
    public const string AdHoc = "ad-hoc";
}

/// <summary>
/// Core concurrency conflict: thrown when a conditional write keyed by Id and the previous token matches 0 rows.
/// </summary>
public static class OpenIddictStoreErrors
{
    /// <summary>Constructs a core ConcurrencyException.</summary>
    public static global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException Concurrency(string entity)
        => new($"{entity} has been concurrently modified or deleted: the identifier and concurrency token no longer match. Reload it and try again.");

    /// <summary>Throws an argument exception when the specified key is empty (design 16.1).</summary>
    public static void ThrowIfKeyIsNullOrEmpty(string? key, string parameterName)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("The specified key cannot be null or an empty string.", parameterName);
        }
    }

    /// <summary>
    /// Query callbacks are not implemented yet (the translator from design 9 is delivered by QUERY-01..03). This is rejected
    /// explicitly: neither an approximate full-table load nor a default value pretending success may be returned.
    /// </summary>
    public static NotSupportedException QueryCallbacksNotImplemented()
        => new("The IQueryable query callbacks of this store are not implemented yet (design 9, QUERY-01..03). " +
               "Use the fixed lookup methods until then; this never degrades to in-memory full-table execution.");


}

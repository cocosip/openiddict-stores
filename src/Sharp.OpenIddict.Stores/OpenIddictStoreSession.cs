using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

namespace Sharp.OpenIddict.Stores;

/// <summary>
/// The connection context used by a single store operation: acquires, opens, and disposes the connection, and centralizes Dapper execution details.
/// A new instance is used per operation; connections are never reused across asynchronous enumerations.
/// </summary>
internal sealed class OpenIddictStoreSession : IAsyncDisposable
{
    private readonly DbConnection _connection;

    private OpenIddictStoreSession(
        DbConnection connection,
        IOpenIddictStoreDialect dialect,
        OpenIddictStoreSchema schema,
        int? commandTimeout)
    {
        _connection = connection;
        Dialect = dialect;
        Schema = schema;
        CommandTimeout = commandTimeout;
    }

    public IOpenIddictStoreDialect Dialect { get; }

    public OpenIddictStoreSchema Schema { get; }

    public int? CommandTimeout { get; }

    /// <summary>Acquires and opens a connection.</summary>
    public static async ValueTask<OpenIddictStoreSession> CreateAsync(
        IOpenIddictStoreConnectionFactory factory,
        IOpenIddictStoreDialect dialect,
        OpenIddictStoreSchema schema,
        int? commandTimeout,
        CancellationToken cancellationToken)
    {
        var connection = await OpenIddictStoreConnections.OpenAsync(factory, cancellationToken).ConfigureAwait(false);

        return new OpenIddictStoreSession(connection, dialect, schema, commandTimeout);
    }

    /// <summary>Executes a non-query command and returns the number of affected rows.</summary>
    public Task<int> ExecuteAsync(string sql, object? parameters, CancellationToken cancellationToken)
        => _connection.ExecuteAsync(new CommandDefinition(
            sql, parameters, commandTimeout: CommandTimeout, cancellationToken: cancellationToken));

    /// <summary>Executes a scalar query.</summary>
    public async Task<T?> ExecuteScalarAsync<T>(string sql, object? parameters, CancellationToken cancellationToken)
        => await _connection.ExecuteScalarAsync<T>(new CommandDefinition(
            sql, parameters, commandTimeout: CommandTimeout, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

    /// <summary>Queries a single row; returns default when there is no result.</summary>
    public Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters, CancellationToken cancellationToken)
        => QueryCoreAsync<T>(sql, parameters, cancellationToken);

    /// <summary>Queries a single row; returns null when there is no result.</summary>
    public Task<T?> FindAsync<T>(string sql, object? parameters, CancellationToken cancellationToken)
        where T : class
        => QueryCoreAsync<T>(sql, parameters, cancellationToken);

    private async Task<T?> QueryCoreAsync<T>(string sql, object? parameters, CancellationToken cancellationToken)
        => await _connection.QuerySingleOrDefaultAsync<T>(new CommandDefinition(
            sql, parameters, commandTimeout: CommandTimeout, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

    /// <summary>Queries multiple rows.</summary>
    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters, CancellationToken cancellationToken)
    {
        var rows = await _connection.QueryAsync<T>(new CommandDefinition(
            sql, parameters, commandTimeout: CommandTimeout, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    /// <summary>Queries a list of scalars (for example, a set of identifiers).</summary>
    public async Task<IReadOnlyList<T>> QueryScalarsAsync<T>(string sql, object? parameters, CancellationToken cancellationToken)
    {
        var rows = await _connection.QueryAsync<T>(new CommandDefinition(
            sql, parameters, commandTimeout: CommandTimeout, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    /// <summary>True row-by-row asynchronous enumeration: the reader lives until enumeration ends and is disposed on early exit or cancellation.</summary>
    public async IAsyncEnumerable<T> StreamAsync<T>(
        string sql,
        object? parameters,
        Func<DbDataReader, T> map,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            sql, parameters, commandTimeout: CommandTimeout, cancellationToken: cancellationToken);

        var reader = await _connection.ExecuteReaderAsync(command).ConfigureAwait(false);

        try
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return map(reader);
            }
        }
        finally
        {
            await OpenIddictStoreConnections.DisposeAsync(reader).ConfigureAwait(false);
        }
    }

    /// <summary>Starts a transaction and returns its wrapper, for use by related writes.</summary>
    public async ValueTask<OpenIddictStoreTransaction> BeginAsync(CancellationToken cancellationToken)
        => new(await OpenIddictStoreConnections.BeginTransactionAsync(_connection, cancellationToken).ConfigureAwait(false));

    /// <summary>Executes a non-query command within the given transaction.</summary>
    public Task<int> ExecuteAsync(
        string sql, object? parameters, DbTransaction transaction, CancellationToken cancellationToken)
        => _connection.ExecuteAsync(new CommandDefinition(
            sql, parameters, transaction, CommandTimeout, cancellationToken: cancellationToken));

    /// <summary>Executes a scalar query within the given transaction.</summary>
    public async Task<T?> ExecuteScalarAsync<T>(
        string sql, object? parameters, DbTransaction transaction, CancellationToken cancellationToken)
        => await _connection.ExecuteScalarAsync<T>(new CommandDefinition(
            sql, parameters, transaction, CommandTimeout, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

    /// <summary>Queries multiple rows within the given transaction.</summary>
    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql, object? parameters, DbTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = await _connection.QueryAsync<T>(new CommandDefinition(
            sql, parameters, transaction, CommandTimeout, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    /// <summary>Computes the SHA-256 digest of a collection value (32 bytes). The digest only narrows candidates; it does not define identity.</summary>
    public static byte[] Hash(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(value));
    }

    public ValueTask DisposeAsync() => OpenIddictStoreConnections.DisposeAsync(_connection);
}

/// <summary>Transaction wrapper that guarantees a rollback even on exception paths.</summary>
internal sealed class OpenIddictStoreTransaction : IAsyncDisposable
{
    private readonly DbTransaction _transaction;
    private bool _committed;

    public OpenIddictStoreTransaction(DbTransaction transaction)
        => _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));

    public DbTransaction Value => _transaction;

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        _committed = true;
        return OpenIddictStoreConnections.CommitAsync(_transaction, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_committed)
        {
            try
            {
                await OpenIddictStoreConnections.RollbackAsync(_transaction, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // A failed rollback does not mask the original exception; the connection is disposed afterwards.
            }
        }

        await OpenIddictStoreConnections.DisposeAsync(_transaction).ConfigureAwait(false);
    }
}

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sharp.OpenIddict.Stores;

/// <summary>
/// The connection context used by a single store operation: acquires, opens, and disposes the connection, and centralizes
/// ADO.NET command execution. A new instance is used per operation; connections are never reused across asynchronous enumerations.
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
        => OpenIddictStoreCommands.ExecuteAsync(_connection, sql, parameters, CommandTimeout, null, cancellationToken);

    /// <summary>Executes a scalar query.</summary>
    public Task<T?> ExecuteScalarAsync<T>(string sql, object? parameters, CancellationToken cancellationToken)
        => OpenIddictStoreCommands.ExecuteScalarAsync<T>(_connection, sql, parameters, CommandTimeout, null, cancellationToken);

    /// <summary>
    /// Reads a single row through <paramref name="map"/>; returns default when the query yields nothing.
    /// The mapper owns materialization, so no ORM convention decides the shape.
    /// </summary>
    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        string sql,
        object? parameters,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken)
    {
        var reader = await OpenIddictStoreCommands
            .ExecuteReaderAsync(_connection, sql, parameters, CommandTimeout, null, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? map(reader) : default;
        }
        finally
        {
            await OpenIddictStoreConnections.DisposeAsync(reader).ConfigureAwait(false);
        }
    }

    /// <summary>True row-by-row asynchronous enumeration: the reader lives until enumeration ends and is disposed on early exit or cancellation.</summary>
    public async IAsyncEnumerable<T> StreamAsync<T>(
        string sql,
        object? parameters,
        Func<DbDataReader, T> map,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reader = await OpenIddictStoreCommands
            .ExecuteReaderAsync(_connection, sql, parameters, CommandTimeout, null, cancellationToken)
            .ConfigureAwait(false);

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
        => OpenIddictStoreCommands.ExecuteAsync(_connection, sql, parameters, CommandTimeout, transaction, cancellationToken);

    /// <summary>Executes a scalar query within the given transaction.</summary>
    public Task<T?> ExecuteScalarAsync<T>(
        string sql, object? parameters, DbTransaction transaction, CancellationToken cancellationToken)
        => OpenIddictStoreCommands.ExecuteScalarAsync<T>(
            _connection, sql, parameters, CommandTimeout, transaction, cancellationToken);

    /// <summary>Reads a single row within the given transaction; returns default when the query yields nothing.</summary>
    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        string sql,
        object? parameters,
        DbTransaction transaction,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken)
    {
        var reader = await OpenIddictStoreCommands
            .ExecuteReaderAsync(_connection, sql, parameters, CommandTimeout, transaction, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? map(reader) : default;
        }
        finally
        {
            await OpenIddictStoreConnections.DisposeAsync(reader).ConfigureAwait(false);
        }
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

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        // The flag is set only after the commit succeeded: a failed commit must still roll back on dispose
        // instead of being treated as already committed.
        await OpenIddictStoreConnections.CommitAsync(_transaction, cancellationToken).ConfigureAwait(false);

        _committed = true;
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

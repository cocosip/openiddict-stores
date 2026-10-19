using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Sharp.OpenIddict.Stores;

/// <summary>
/// Opens and disposes connections. Every operation acquires a new connection; externally owned DataSource instances are not disposed by this extension.
/// </summary>
internal static class OpenIddictStoreConnections
{
    /// <summary>Creates, opens, and returns a connection; on failure the already created connection is disposed.</summary>
    public static async ValueTask<DbConnection> OpenAsync(
        IOpenIddictStoreConnectionFactory factory, CancellationToken cancellationToken)
    {
        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        var connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

        if (connection is null)
        {
            throw new InvalidOperationException("The connection factory returned null.");
        }

        try
        {
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            return connection;
        }
        catch
        {
            await DisposeAsync(connection).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Disposes the connection asynchronously; netstandard2.0 has no DisposeAsync, so it falls back to synchronous disposal.</summary>
    public static ValueTask DisposeAsync(DbConnection connection)
    {
#if NETSTANDARD2_0
        connection.Dispose();
        return default;
#else
        return connection.DisposeAsync();
#endif
    }

    /// <summary>Disposes the reader asynchronously; netstandard2.0 has no DisposeAsync, so it falls back to synchronous disposal.</summary>
    public static ValueTask DisposeAsync(DbDataReader reader)
    {
#if NETSTANDARD2_0
        reader.Dispose();
        return default;
#else
        return reader.DisposeAsync();
#endif
    }

    /// <summary>Disposes the transaction asynchronously; netstandard2.0 has no DisposeAsync, so it falls back to synchronous disposal.</summary>
    public static ValueTask DisposeAsync(DbTransaction transaction)
    {
#if NETSTANDARD2_0
        transaction.Dispose();
        return default;
#else
        return transaction.DisposeAsync();
#endif
    }

    /// <summary>Begins a transaction; netstandard2.0 has no BeginTransactionAsync, so it falls back to synchronous begin.</summary>
    public static Task<DbTransaction> BeginTransactionAsync(DbConnection connection, CancellationToken cancellationToken)
    {
#if NETSTANDARD2_0
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(connection.BeginTransaction());
#else
        return connection.BeginTransactionAsync(cancellationToken).AsTask();
#endif
    }

    /// <summary>Commits the transaction; netstandard2.0 has no CommitAsync, so it falls back to synchronous commit.</summary>
    public static Task CommitAsync(DbTransaction transaction, CancellationToken cancellationToken)
    {
#if NETSTANDARD2_0
        transaction.Commit();
        return Task.CompletedTask;
#else
        return transaction.CommitAsync(cancellationToken);
#endif
    }

    /// <summary>Rolls back the transaction; netstandard2.0 has no RollbackAsync, so it falls back to synchronous rollback.</summary>
    public static Task RollbackAsync(DbTransaction transaction, CancellationToken cancellationToken)
    {
#if NETSTANDARD2_0
        transaction.Rollback();
        return Task.CompletedTask;
#else
        return transaction.RollbackAsync(cancellationToken);
#endif
    }
}

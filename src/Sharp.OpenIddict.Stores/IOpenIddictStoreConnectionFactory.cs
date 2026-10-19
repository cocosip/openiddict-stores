using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Sharp.OpenIddict.Stores;

/// <summary>
/// Creates a database connection. The returned connection is not open yet; the caller is responsible for opening and disposing it.
/// </summary>
public interface IOpenIddictStoreConnectionFactory
{
    /// <summary>Creates a new, not yet opened connection.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A connection owned by the caller; never return a shared connection that is in use elsewhere.</returns>
    ValueTask<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A delegate-based connection factory. The delegate only constructs the connection; OpenAsync is performed by this extension.
/// </summary>
public sealed class OpenIddictStoreDelegateConnectionFactory : IOpenIddictStoreConnectionFactory
{
    private readonly Func<IServiceProvider, DbConnection> _factory;
    private readonly IServiceProvider _services;

    /// <summary>Creates a factory from the given delegate and service provider.</summary>
    public OpenIddictStoreDelegateConnectionFactory(
        Func<IServiceProvider, DbConnection> factory, IServiceProvider services)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public ValueTask<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new ValueTask<DbConnection>(Task.FromCanceled<DbConnection>(cancellationToken));
        }

        var connection = _factory(_services)
            ?? throw new InvalidOperationException("The connection factory returned null.");

        return new ValueTask<DbConnection>(connection);
    }
}

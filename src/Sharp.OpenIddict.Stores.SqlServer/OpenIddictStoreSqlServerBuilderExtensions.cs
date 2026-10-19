using System;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.SqlServer;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration extensions for the SQL Server provider.
/// </summary>
public static class OpenIddictStoreSqlServerBuilderExtensions
{
    /// <summary>Selects SQL Server and installs the corresponding dialect.</summary>
    public static OpenIddictStoreBuilder UseSqlServer(this OpenIddictStoreBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.UseProvider(OpenIddictStoreProvider.SqlServer);
        builder.Services.AddSingleton<IOpenIddictStoreDialect>(OpenIddictStoreSqlServerDialect.Instance);

        return builder;
    }
}

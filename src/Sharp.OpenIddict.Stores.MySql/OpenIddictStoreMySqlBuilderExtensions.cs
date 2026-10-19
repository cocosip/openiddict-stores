using System;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.MySql;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration extensions for the MySQL and MariaDB provider.
/// </summary>
public static class OpenIddictStoreMySqlBuilderExtensions
{
    /// <summary>
    /// Selects MySQL or MariaDB and installs the corresponding dialect. One dialect covers both servers; the
    /// two are still accepted separately because their DDL and verification records differ (design 5).
    /// </summary>
    public static OpenIddictStoreBuilder UseMySql(this OpenIddictStoreBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.UseProvider(OpenIddictStoreProvider.MySql);
        builder.Services.AddSingleton<IOpenIddictStoreDialect>(OpenIddictStoreMySqlDialect.Instance);

        return builder;
    }
}

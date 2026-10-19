using System;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.PostgreSql;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration extensions for the PostgreSQL provider.
/// </summary>
public static class OpenIddictStorePostgreSqlBuilderExtensions
{
    /// <summary>Selects PostgreSQL and installs the corresponding dialect.</summary>
    public static OpenIddictStoreBuilder UsePostgreSql(this OpenIddictStoreBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.UseProvider(OpenIddictStoreProvider.PostgreSql);
        builder.Services.AddSingleton<IOpenIddictStoreDialect>(OpenIddictStorePostgreSqlDialect.Instance);

        return builder;
    }
}

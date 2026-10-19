using System;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Sqlite;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration extensions for the SQLite provider.
/// </summary>
public static class OpenIddictStoreSqliteBuilderExtensions
{
    /// <summary>Selects SQLite and installs the corresponding dialect.</summary>
    public static OpenIddictStoreBuilder UseSqlite(this OpenIddictStoreBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.UseProvider(OpenIddictStoreProvider.Sqlite);
        builder.Services.AddSingleton<IOpenIddictStoreDialect>(OpenIddictStoreSqliteDialect.Instance);

        return builder;
    }
}

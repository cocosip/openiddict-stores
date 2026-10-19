using System;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Oracle;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration extensions for the Oracle provider.
/// </summary>
public static class OpenIddictStoreOracleBuilderExtensions
{
    /// <summary>Selects Oracle and installs the corresponding dialect.</summary>
    public static OpenIddictStoreBuilder UseOracle(this OpenIddictStoreBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.UseProvider(OpenIddictStoreProvider.Oracle);
        builder.Services.AddSingleton<IOpenIddictStoreDialect>(OpenIddictStoreOracleDialect.Instance);

        return builder;
    }
}

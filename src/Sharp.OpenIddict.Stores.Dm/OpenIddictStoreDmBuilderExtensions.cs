using System;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Dm;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration extensions for the Dameng DM8 provider.
/// </summary>
public static class OpenIddictStoreDmBuilderExtensions
{
    /// <summary>
    /// Selects Dameng DM8 in native mode and installs the corresponding dialect. The Oracle-compatibility mode
    /// is a separate acceptance target and is not claimed by this registration (design 5).
    /// </summary>
    public static OpenIddictStoreBuilder UseDm(this OpenIddictStoreBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.UseProvider(OpenIddictStoreProvider.Dm);
        builder.Services.AddSingleton<IOpenIddictStoreDialect>(OpenIddictStoreDmDialect.Instance);

        return builder;
    }
}

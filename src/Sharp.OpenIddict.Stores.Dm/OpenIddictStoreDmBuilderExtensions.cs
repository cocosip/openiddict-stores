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
    /// Selects Dameng DM8 and installs the dialect for the given compatibility mode. The mode is part of the
    /// declaration, not a runtime switch: it must match the <c>COMPATIBLE_MODE</c> of the instance the
    /// connection points at, and it selects the manifest entry whose script the deployment renders (design 5,
    /// 10.1). Native mode is the default.
    /// </summary>
    public static OpenIddictStoreBuilder UseDm(
        this OpenIddictStoreBuilder builder,
        OpenIddictStoreDmCompatibilityMode mode = OpenIddictStoreDmCompatibilityMode.Native)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.UseProvider(OpenIddictStoreProvider.Dm);
        builder.Services.AddSingleton<IOpenIddictStoreDialect>(mode switch
        {
            OpenIddictStoreDmCompatibilityMode.Native => OpenIddictStoreDmDialect.Instance,
            OpenIddictStoreDmCompatibilityMode.OracleCompatible => OpenIddictStoreDmOracleCompatibleDialect.Instance,
            OpenIddictStoreDmCompatibilityMode.MySqlCompatible => OpenIddictStoreDmMySqlCompatibleDialect.Instance,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown DM8 compatibility mode."),
        });

        return builder;
    }
}

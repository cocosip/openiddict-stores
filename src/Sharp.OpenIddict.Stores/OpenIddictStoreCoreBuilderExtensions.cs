using System;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenIddict.Core;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Models;
using Sharp.OpenIddict.Stores.Stores;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the store extension on OpenIddict Core.
/// </summary>
public static class OpenIddictStoreCoreBuilderExtensions
{
    /// <summary>Registers this extension's default entities, options, and connection factory, and disables entity caching.</summary>
    public static OpenIddictCoreBuilder UseStores(this OpenIddictCoreBuilder builder)
        => UseStores(builder, static _ => { });

    /// <summary>Registers this extension and runs the configuration delegate.</summary>
    public static OpenIddictCoreBuilder UseStores(
        this OpenIddictCoreBuilder builder, Action<OpenIddictStoreBuilder> configuration)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var services = builder.Services;

        configuration(new OpenIddictStoreBuilder(services));

        services.AddOptions<OpenIddictStoreOptions>();

        // Idempotent: calling UseStores repeatedly does not register the same service twice.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OpenIddictStoreOptions>, OpenIddictStoreOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OpenIddictCoreOptions>, OpenIddictStoreCoreOptionsValidator>());

        services.TryAddScoped<IOpenIddictStoreConnectionFactory>(CreateConnectionFactory);

        // Entity caching must be disabled: bulk revocation updates the store directly, so cached entities are not refreshed.
        // Whether caching is configured before or after UseStores, the final options validation rejects it.
        builder.DisableEntityCaching();

        builder.SetDefaultApplicationEntity<OpenIddictStoreApplication>();
        builder.SetDefaultAuthorizationEntity<OpenIddictStoreAuthorization>();
        builder.SetDefaultScopeEntity<OpenIddictStoreScope>();
        builder.SetDefaultTokenEntity<OpenIddictStoreToken>();

        // Only stores that are already implemented are registered. Unimplemented stores get no placeholder:
        // when missing, DI fails explicitly instead of letting callers believe the store is available.
        services.TryAddScoped<
            OpenIddict.Abstractions.IOpenIddictApplicationStore<OpenIddictStoreApplication>,
            OpenIddictStoreApplicationStore<OpenIddictStoreApplication>>();
        services.TryAddScoped<
            OpenIddict.Abstractions.IOpenIddictScopeStore<OpenIddictStoreScope>,
            OpenIddictStoreScopeStore<OpenIddictStoreScope>>();
        services.TryAddScoped<
            OpenIddict.Abstractions.IOpenIddictAuthorizationStore<OpenIddictStoreAuthorization>,
            OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization>>();
        services.TryAddScoped<
            OpenIddict.Abstractions.IOpenIddictTokenStore<OpenIddictStoreToken>,
            OpenIddictStoreTokenStore<OpenIddictStoreToken>>();

        return builder;
    }

    /// <summary>Obsolete alias kept so existing call sites keep compiling; use <see cref="UseStores(OpenIddictCoreBuilder)"/> instead.</summary>
    /// <remarks>The name is a leftover from an earlier design that depended on Dapper. The package no longer references Dapper at all, so the name is misleading.</remarks>
    [Obsolete("UseDapper is a historical name; the package does not depend on Dapper. Use UseStores() instead.")]
    public static OpenIddictCoreBuilder UseDapper(this OpenIddictCoreBuilder builder)
        => UseStores(builder);

    /// <summary>Obsolete alias kept so existing call sites keep compiling; use <see cref="UseStores(OpenIddictCoreBuilder, Action{OpenIddictStoreBuilder})"/> instead.</summary>
    /// <remarks>The name is a leftover from an earlier design that depended on Dapper. The package no longer references Dapper at all, so the name is misleading.</remarks>
    [Obsolete("UseDapper is a historical name; the package does not depend on Dapper. Use UseStores() instead.")]
    public static OpenIddictCoreBuilder UseDapper(
        this OpenIddictCoreBuilder builder, Action<OpenIddictStoreBuilder> configuration)
        => UseStores(builder, configuration);

    private static IOpenIddictStoreConnectionFactory CreateConnectionFactory(IServiceProvider services)
    {
        // Resolving IOptions triggers options validation, so missing or conflicting configuration fails before use.
        var options = services.GetRequiredService<IOptions<OpenIddictStoreOptions>>().Value;

        if (options.ConnectionFactoryType is { } type)
        {
            return (IOpenIddictStoreConnectionFactory)ActivatorUtilities.GetServiceOrCreateInstance(services, type);
        }

        if (options.ConnectionFactory is { } factory)
        {
            return new OpenIddictStoreDelegateConnectionFactory(factory, services);
        }

        throw new InvalidOperationException("No connection factory was configured; call UseConnectionFactory.");
    }
}

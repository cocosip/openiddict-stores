using System;
using System.Collections.Generic;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenIddict.Core;

namespace Sharp.OpenIddict.Stores;

/// <summary>
/// The configuration builder for this extension. All methods only record configuration; actual validation runs before first use.
/// </summary>
public sealed class OpenIddictStoreBuilder
{
    /// <summary>Creates a builder from the given service collection.</summary>
    public OpenIddictStoreBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>Gets the service collection.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Selects the provider. Called by methods such as <c>UseSqlServer()</c> in the provider packages;
    /// a single registration can select only one provider, and multiple selections are rejected before use.
    /// </summary>
    public OpenIddictStoreBuilder UseProvider(OpenIddictStoreProvider provider)
    {
        Configure(options => options.Providers.Add(provider));

        return this;
    }

    /// <summary>Uses a delegate to construct the connection; the delegate only constructs it and does not open it.</summary>
    public OpenIddictStoreBuilder UseConnectionFactory(Func<IServiceProvider, DbConnection> factory)
    {
        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        Configure(options =>
        {
            options.ConnectionFactory = factory;
            options.ConnectionFactoryType = null;
        });

        return this;
    }

    /// <summary>Uses a custom connection factory type.</summary>
    public OpenIddictStoreBuilder UseConnectionFactory<TFactory>() where TFactory : class, IOpenIddictStoreConnectionFactory
    {
        Services.TryAddScoped<TFactory>();

        Configure(options =>
        {
            options.ConnectionFactoryType = typeof(TFactory);
            options.ConnectionFactory = null;
        });

        return this;
    }

    /// <summary>Sets a dedicated schema. SQLite and MySQL have no such semantics and will refuse to start once configured.</summary>
    public OpenIddictStoreBuilder SetSchema(string? schema)
    {
        Configure(options => options.Schema = string.IsNullOrWhiteSpace(schema) ? null : schema);

        return this;
    }

    /// <summary>Sets the table prefix, which defaults to OpenIddict.</summary>
    public OpenIddictStoreBuilder SetTablePrefix(string prefix)
    {
        Configure(options => options.TablePrefix = prefix);

        return this;
    }

    /// <summary>Sets the command timeout in seconds; null follows the driver default.</summary>
    public OpenIddictStoreBuilder SetCommandTimeout(int? seconds)
    {
        Configure(options => options.CommandTimeout = seconds);

        return this;
    }

    private void Configure(Action<OpenIddictStoreOptions> configuration)
        => Services.Configure(configuration);
}

/// <summary>
/// Options validation: missing or multiple providers, missing connection factory, invalid table prefix, invalid timeout, and unsupported schema.
/// </summary>
internal sealed class OpenIddictStoreOptionsValidator : IValidateOptions<OpenIddictStoreOptions>
{
    public ValidateOptionsResult Validate(string? name, OpenIddictStoreOptions options)
    {
        var failures = new List<string>();

        if (options.Providers.Count == 0)
        {
            failures.Add("No database provider was selected; call one of UseSqlServer/UsePostgreSql/UseMySql/UseSqlite/UseOracle/UseDm.");
        }
        else if (options.Providers.Count > 1)
        {
            failures.Add("Multiple providers were selected for the same registration; exactly one provider must be selected in a single call.");
        }

        if (options.ConnectionFactory is null && options.ConnectionFactoryType is null)
        {
            failures.Add("No connection factory was configured; call UseConnectionFactory.");
        }
        else if (options.ConnectionFactory is not null && options.ConnectionFactoryType is not null)
        {
            failures.Add("The connection factory can only be configured once: a delegate and a type cannot both be present.");
        }

        if (options.ConnectionFactoryType is { } type && !typeof(IOpenIddictStoreConnectionFactory).IsAssignableFrom(type))
        {
            failures.Add($"The connection factory type {type.FullName} does not implement IOpenIddictStoreConnectionFactory.");
        }

        if (string.IsNullOrWhiteSpace(options.TablePrefix))
        {
            failures.Add("The table prefix cannot be empty.");
        }
        else if (!IsValidIdentifier(options.TablePrefix))
        {
            failures.Add($"The table prefix '{options.TablePrefix}' is not a valid identifier fragment: only letters, digits, and underscores are allowed, and it cannot start with a digit.");
        }

        if (options.CommandTimeout is <= 0)
        {
            failures.Add("The command timeout must be a positive number of seconds.");
        }

        if (!string.IsNullOrEmpty(options.Schema))
        {
            // IsNullOrEmpty on netstandard2.0 has no NotNullWhen annotation, so extract a local variable first.
            var schema = options.Schema!;

            if (options.GetProvider() is OpenIddictStoreProvider.Sqlite or OpenIddictStoreProvider.MySql)
            {
                failures.Add("SQLite and MySQL do not support dedicated schema semantics; SetSchema cannot be configured.");
            }
            else if (!IsValidIdentifier(schema))
            {
                failures.Add($"The schema '{schema}' is not a valid identifier.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    internal static bool IsValidIdentifier(string value)
    {
        if (value.Length == 0 || char.IsDigit(value[0]))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Core options validation: as long as this extension takes part in the registration, the effective entity caching must stay disabled.
/// This validation runs when OpenIddictCoreOptions is resolved, so it is independent of the order of configuration calls.
/// </summary>
internal sealed class OpenIddictStoreCoreOptionsValidator : IValidateOptions<OpenIddictCoreOptions>
{
    public ValidateOptionsResult Validate(string? name, OpenIddictCoreOptions options)
    {
        if (name is not null && name != Options.DefaultName)
        {
            return ValidateOptionsResult.Success;
        }

        return options.DisableEntityCaching
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "This extension requires OpenIddict entity caching to be disabled: bulk revocation updates the store directly, so cached entities are never refreshed. " +
                "Remove the configuration that re-enables entity caching, or do not use UseDapper.");
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Sharp.OpenIddict.Stores;

/// <summary>
/// The configuration options of this extension: provider, connection factory, schema, table prefix, and command timeout.
/// Other behaviors (JSON, queries, pruning, concurrency) are not switches that can be turned off.
/// </summary>
public sealed class OpenIddictStoreOptions
{
    /// <summary>The default table prefix.</summary>
    public const string DefaultTablePrefix = "OpenIddict";

    /// <summary>The set of selected providers; more than one is treated as a configuration conflict.</summary>
    public HashSet<OpenIddictStoreProvider> Providers { get; } = [];

    /// <summary>The connection factory type; mutually exclusive with the delegate factory.</summary>
    public Type? ConnectionFactoryType { get; set; }

    /// <summary>The delegate-based connection factory.</summary>
    public Func<IServiceProvider, System.Data.Common.DbConnection>? ConnectionFactory { get; set; }

    /// <summary>The dedicated schema; when null, the provider default schema is used.</summary>
    public string? Schema { get; set; }

    /// <summary>The table prefix, defaulting to <see cref="DefaultTablePrefix"/>.</summary>
    public string TablePrefix { get; set; } = DefaultTablePrefix;

    /// <summary>The command timeout in seconds; when null, the driver default is used.</summary>
    public int? CommandTimeout { get; set; }

    /// <summary>Returns the single selected provider; returns null when none is selected.</summary>
    public OpenIddictStoreProvider? GetProvider()
        => Providers.Count == 1 ? Providers.First() : null;
}

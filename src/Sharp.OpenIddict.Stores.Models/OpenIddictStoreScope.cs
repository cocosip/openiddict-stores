using System;
using System.Diagnostics;

namespace Sharp.OpenIddict.Stores.Models;

/// <summary>
/// Represents an OpenIddict scope. Collections and dictionaries are persisted as JSON text and converted by the store.
/// </summary>
[DebuggerDisplay("Id = {Id,nq} ; Name = {Name,nq}")]
public class OpenIddictStoreScope
{
    /// <summary>Gets or sets the unique identifier of the scope.</summary>
    public virtual string? Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the concurrency token of the scope.</summary>
    public virtual string? ConcurrencyToken { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the description.</summary>
    public virtual string? Description { get; set; }

    /// <summary>Gets or sets the JSON of the localized descriptions.</summary>
    public virtual string? Descriptions { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public virtual string? DisplayName { get; set; }

    /// <summary>Gets or sets the JSON of the localized display names.</summary>
    public virtual string? DisplayNames { get; set; }

    /// <summary>Gets or sets the scope name.</summary>
    public virtual string? Name { get; set; }

    /// <summary>Gets or sets the JSON of the additional properties.</summary>
    public virtual string? Properties { get; set; }

    /// <summary>Gets or sets the JSON of the associated resources.</summary>
    public virtual string? Resources { get; set; }
}

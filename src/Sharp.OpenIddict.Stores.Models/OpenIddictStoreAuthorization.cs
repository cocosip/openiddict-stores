using System;
using System.Diagnostics;

namespace Sharp.OpenIddict.Stores.Models;

/// <summary>
/// Represents an OpenIddict authorization. Collections are persisted as JSON text and converted by the store.
/// </summary>
[DebuggerDisplay("Id = {Id,nq} ; Subject = {Subject,nq} ; Status = {Status,nq}")]
public class OpenIddictStoreAuthorization
{
    /// <summary>Gets or sets the unique identifier of the authorization.</summary>
    public virtual string? Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the concurrency token of the authorization.</summary>
    public virtual string? ConcurrencyToken { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the identifier of the owning application.</summary>
    public virtual string? ApplicationId { get; set; }

    /// <summary>Gets or sets the UTC creation date.</summary>
    public virtual DateTime? CreationDate { get; set; }

    /// <summary>Gets or sets the JSON of the additional properties.</summary>
    public virtual string? Properties { get; set; }

    /// <summary>Gets or sets the JSON of the granted scopes.</summary>
    public virtual string? Scopes { get; set; }

    /// <summary>Gets or sets the authorization status.</summary>
    public virtual string? Status { get; set; }

    /// <summary>Gets or sets the authorization subject.</summary>
    public virtual string? Subject { get; set; }

    /// <summary>Gets or sets the authorization type.</summary>
    public virtual string? Type { get; set; }
}

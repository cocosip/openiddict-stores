using System;
using System.Diagnostics;

namespace Sharp.OpenIddict.Stores.Models;

/// <summary>
/// Represents an OpenIddict token. Collections and dictionaries are persisted as JSON text and converted by the store.
/// </summary>
[DebuggerDisplay("Id = {Id,nq} ; Subject = {Subject,nq} ; Type = {Type,nq} ; Status = {Status,nq}")]
public class OpenIddictStoreToken
{
    /// <summary>Gets or sets the unique identifier of the token.</summary>
    public virtual string? Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the concurrency token of the token.</summary>
    public virtual string? ConcurrencyToken { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the identifier of the owning application.</summary>
    public virtual string? ApplicationId { get; set; }

    /// <summary>Gets or sets the identifier of the owning authorization.</summary>
    public virtual string? AuthorizationId { get; set; }

    /// <summary>Gets or sets the UTC creation date.</summary>
    public virtual DateTime? CreationDate { get; set; }

    /// <summary>Gets or sets the UTC expiration date.</summary>
    public virtual DateTime? ExpirationDate { get; set; }

    /// <summary>Gets or sets the token payload (for reference tokens, may be hashed or encrypted).</summary>
    public virtual string? Payload { get; set; }

    /// <summary>Gets or sets the JSON of the additional properties.</summary>
    public virtual string? Properties { get; set; }

    /// <summary>Gets or sets the UTC redemption date.</summary>
    public virtual DateTime? RedemptionDate { get; set; }

    /// <summary>Gets or sets the reference identifier (for reference tokens, may be hashed or encrypted).</summary>
    public virtual string? ReferenceId { get; set; }

    /// <summary>Gets or sets the token status.</summary>
    public virtual string? Status { get; set; }

    /// <summary>Gets or sets the token subject.</summary>
    public virtual string? Subject { get; set; }

    /// <summary>Gets or sets the token type.</summary>
    public virtual string? Type { get; set; }
}

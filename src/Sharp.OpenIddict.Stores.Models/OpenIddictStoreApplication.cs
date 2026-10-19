using System;
using System.Diagnostics;

namespace Sharp.OpenIddict.Stores.Models;

/// <summary>
/// Represents an OpenIddict application. Collections and dictionaries are persisted as JSON text and converted by the store.
/// </summary>
[DebuggerDisplay("Id = {Id,nq} ; ClientId = {ClientId,nq} ; ClientType = {ClientType,nq}")]
public class OpenIddictStoreApplication
{
    /// <summary>Gets or sets the unique identifier of the application.</summary>
    public virtual string? Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the concurrency token of the application.</summary>
    public virtual string? ConcurrencyToken { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the application type.</summary>
    public virtual string? ApplicationType { get; set; }

    /// <summary>Gets or sets the client identifier.</summary>
    public virtual string? ClientId { get; set; }

    /// <summary>Gets or sets the client secret (already hashed or encrypted by the manager).</summary>
    public virtual string? ClientSecret { get; set; }

    /// <summary>Gets or sets the client type.</summary>
    public virtual string? ClientType { get; set; }

    /// <summary>Gets or sets the consent type.</summary>
    public virtual string? ConsentType { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public virtual string? DisplayName { get; set; }

    /// <summary>Gets or sets the JSON of the localized display names.</summary>
    public virtual string? DisplayNames { get; set; }

    /// <summary>Gets or sets the JSON of the application JWK set.</summary>
    public virtual string? JsonWebKeySet { get; set; }

    /// <summary>Gets or sets the JSON of the permissions.</summary>
    public virtual string? Permissions { get; set; }

    /// <summary>Gets or sets the JSON of the post-logout redirect URIs.</summary>
    public virtual string? PostLogoutRedirectUris { get; set; }

    /// <summary>Gets or sets the JSON of the additional properties.</summary>
    public virtual string? Properties { get; set; }

    /// <summary>Gets or sets the JSON of the redirect URIs.</summary>
    public virtual string? RedirectUris { get; set; }

    /// <summary>Gets or sets the JSON of the requirements.</summary>
    public virtual string? Requirements { get; set; }

    /// <summary>Gets or sets the JSON of the settings.</summary>
    public virtual string? Settings { get; set; }
}

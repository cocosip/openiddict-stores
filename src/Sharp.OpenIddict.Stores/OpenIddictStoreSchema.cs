using System;
using System.Collections.Generic;

namespace Sharp.OpenIddict.Stores;

/// <summary>
/// Table names derived from the options. A table name is a validated prefix plus a fixed logical name; there is no second set of mapping metadata.
/// </summary>
internal sealed class OpenIddictStoreSchema
{
    public OpenIddictStoreSchema(string prefix, string? schema)
    {
        Prefix = OpenIddictStoreIdentifiers.Validate(prefix, nameof(prefix));
        Name = string.IsNullOrEmpty(schema) ? null : OpenIddictStoreIdentifiers.Validate(schema!, nameof(schema));
    }

    public string Prefix { get; }

    /// <summary>The dedicated schema; when null, the provider default schema is used.</summary>
    public string? Name { get; }

    public string Applications => Prefix + "Applications";

    public string Authorizations => Prefix + "Authorizations";

    public string Scopes => Prefix + "Scopes";

    public string Tokens => Prefix + "Tokens";

    public string ApplicationRedirectUris => Prefix + "ApplicationRedirectUris";

    public string ApplicationPostLogoutRedirectUris => Prefix + "ApplicationPostLogoutRedirectUris";

    public string AuthorizationScopes => Prefix + "AuthorizationScopes";

    public string ScopeResources => Prefix + "ScopeResources";

    public string SchemaVersions => Prefix + "SchemaVersions";

    /// <summary>All primary and auxiliary tables, used by schema checks and installation validation.</summary>
    public IReadOnlyList<string> AllTables => new[]
    {
        Applications,
        Authorizations,
        Scopes,
        Tokens,
        ApplicationRedirectUris,
        ApplicationPostLogoutRedirectUris,
        AuthorizationScopes,
        ScopeResources,
        SchemaVersions,
    };
}

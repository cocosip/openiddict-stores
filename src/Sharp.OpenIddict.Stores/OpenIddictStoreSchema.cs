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

    /// <summary>
    /// The schema series this package implements (design 10.2, 17). Every provider ships the same logical
    /// series; a provider whose scripts are at a different version is a packaging defect, and
    /// <c>SqlAssetTests</c> pins the manifest to these constants.
    /// </summary>
    public const string RequiredSeries = "7";

    /// <summary>The schema version this package requires. It advances together with the shipped scripts.</summary>
    public const int RequiredVersion = 1;

    /// <summary>Quotes the schema version table, including the schema qualifier when one is configured.</summary>
    public string VersionsTableName(IOpenIddictStoreDialect dialect)
        => Name is null
            ? dialect.QuoteIdentifier(SchemaVersions)
            : dialect.QuoteIdentifier(Name) + "." + dialect.QuoteIdentifier(SchemaVersions);

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

using System;
using System.Collections.Generic;

namespace Sharp.OpenIddict.Stores.Queries;

/// <summary>How a mapped column may be used inside a query callback (design 9, design 9.2).</summary>
internal enum OpenIddictStoreColumnKind
{
    /// <summary>A bounded identifier: comparable, sortable, and selectable.</summary>
    Identifier,

    /// <summary>A bounded string: comparable, sortable, and selectable.</summary>
    BoundedString,

    /// <summary>A UTC date: comparable, sortable, and selectable.</summary>
    Date,

    /// <summary>An unbounded text column: null checks and projection only, never comparison or sorting.</summary>
    LargeText,

    /// <summary>A JSON column: null checks and projection only, never comparison or sorting.</summary>
    Json,
}

/// <summary>One mapped column of a store entity.</summary>
internal sealed class OpenIddictStoreColumn
{
    public OpenIddictStoreColumn(string property, OpenIddictStoreColumnKind kind)
    {
        Property = property ?? throw new ArgumentNullException(nameof(property));
        Kind = kind;
    }

    /// <summary>The entity property name; it is also the column name (design 15.2).</summary>
    public string Property { get; }

    /// <summary>How the column may be used.</summary>
    public OpenIddictStoreColumnKind Kind { get; }

    /// <summary>True when the column is a bounded string, which needs the exact comparison predicate.</summary>
    public bool IsBoundedString => Kind is OpenIddictStoreColumnKind.Identifier or OpenIddictStoreColumnKind.BoundedString;

    /// <summary>True when equality, inequality, and range comparisons are allowed.</summary>
    public bool IsComparable => Kind is not (OpenIddictStoreColumnKind.LargeText or OpenIddictStoreColumnKind.Json);

    /// <summary>True when the column may appear in ORDER BY.</summary>
    public bool IsSortable => IsComparable;
}

/// <summary>
/// The fixed column map of each store entity. Column names come from here, never from user input
/// (design 9.2), and unmapped members are rejected before any SQL is generated.
/// </summary>
internal static class OpenIddictStoreColumnMap
{
    private static readonly Dictionary<Type, Dictionary<string, OpenIddictStoreColumn>> Maps = new()
    {
        [typeof(Models.OpenIddictStoreApplication)] = Build(
            ("Id", OpenIddictStoreColumnKind.Identifier),
            ("ConcurrencyToken", OpenIddictStoreColumnKind.BoundedString),
            ("ApplicationType", OpenIddictStoreColumnKind.BoundedString),
            ("ClientId", OpenIddictStoreColumnKind.BoundedString),
            ("ClientSecret", OpenIddictStoreColumnKind.LargeText),
            ("ClientType", OpenIddictStoreColumnKind.BoundedString),
            ("ConsentType", OpenIddictStoreColumnKind.BoundedString),
            ("DisplayName", OpenIddictStoreColumnKind.BoundedString),
            ("DisplayNames", OpenIddictStoreColumnKind.Json),
            ("JsonWebKeySet", OpenIddictStoreColumnKind.Json),
            ("Permissions", OpenIddictStoreColumnKind.Json),
            ("PostLogoutRedirectUris", OpenIddictStoreColumnKind.Json),
            ("Properties", OpenIddictStoreColumnKind.Json),
            ("RedirectUris", OpenIddictStoreColumnKind.Json),
            ("Requirements", OpenIddictStoreColumnKind.Json),
            ("Settings", OpenIddictStoreColumnKind.Json)),

        [typeof(Models.OpenIddictStoreAuthorization)] = Build(
            ("Id", OpenIddictStoreColumnKind.Identifier),
            ("ConcurrencyToken", OpenIddictStoreColumnKind.BoundedString),
            ("ApplicationId", OpenIddictStoreColumnKind.Identifier),
            ("CreationDate", OpenIddictStoreColumnKind.Date),
            ("Properties", OpenIddictStoreColumnKind.Json),
            ("Scopes", OpenIddictStoreColumnKind.Json),
            ("Status", OpenIddictStoreColumnKind.BoundedString),
            ("Subject", OpenIddictStoreColumnKind.BoundedString),
            ("Type", OpenIddictStoreColumnKind.BoundedString)),

        [typeof(Models.OpenIddictStoreScope)] = Build(
            ("Id", OpenIddictStoreColumnKind.Identifier),
            ("ConcurrencyToken", OpenIddictStoreColumnKind.BoundedString),
            ("Description", OpenIddictStoreColumnKind.LargeText),
            ("Descriptions", OpenIddictStoreColumnKind.Json),
            ("DisplayName", OpenIddictStoreColumnKind.BoundedString),
            ("DisplayNames", OpenIddictStoreColumnKind.Json),
            ("Name", OpenIddictStoreColumnKind.BoundedString),
            ("Properties", OpenIddictStoreColumnKind.Json),
            ("Resources", OpenIddictStoreColumnKind.Json)),

        [typeof(Models.OpenIddictStoreToken)] = Build(
            ("Id", OpenIddictStoreColumnKind.Identifier),
            ("ConcurrencyToken", OpenIddictStoreColumnKind.BoundedString),
            ("ApplicationId", OpenIddictStoreColumnKind.Identifier),
            ("AuthorizationId", OpenIddictStoreColumnKind.Identifier),
            ("CreationDate", OpenIddictStoreColumnKind.Date),
            ("ExpirationDate", OpenIddictStoreColumnKind.Date),
            ("Payload", OpenIddictStoreColumnKind.LargeText),
            ("Properties", OpenIddictStoreColumnKind.Json),
            ("RedemptionDate", OpenIddictStoreColumnKind.Date),
            ("ReferenceId", OpenIddictStoreColumnKind.BoundedString),
            ("Status", OpenIddictStoreColumnKind.BoundedString),
            ("Subject", OpenIddictStoreColumnKind.BoundedString),
            ("Type", OpenIddictStoreColumnKind.BoundedString)),
    };

    /// <summary>Returns the map of the given entity type, or null when the type is not a store entity.</summary>
    public static Dictionary<string, OpenIddictStoreColumn>? For(Type entityType)
        => Maps.TryGetValue(entityType, out var map) ? map : null;

    /// <summary>Resolves one column of the entity, throwing a clear rejection when it is not mapped.</summary>
    public static OpenIddictStoreColumn Resolve(Type entityType, string property)
    {
        var map = For(entityType)
            ?? throw new NotSupportedException(
                $"The query callback only supports the store entities of this extension; '{entityType}' is not one of them.");

        if (map.TryGetValue(property, out var column))
        {
            return column;
        }

        throw new NotSupportedException(
            $"The member '{entityType.Name}.{property}' is not a mapped column and cannot be translated to SQL.");
    }

    private static Dictionary<string, OpenIddictStoreColumn> Build(
        params (string Property, OpenIddictStoreColumnKind Kind)[] columns)
    {
        var map = new Dictionary<string, OpenIddictStoreColumn>(columns.Length, StringComparer.Ordinal);

        foreach (var (property, kind) in columns)
        {
            map[property] = new OpenIddictStoreColumn(property, kind);
        }

        return map;
    }
}

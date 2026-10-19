using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sharp.OpenIddict.Stores;

/// <summary>
/// JSON conversion for collections, dictionaries, and JWKs. JSON is the authoritative data for collections; null and empty
/// collections both read back as empty, and invalid JSON throws a data exception instead of silently returning a default.
/// </summary>
public static class OpenIddictStoreJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    /// <summary>Serializes a string collection to JSON; null or empty collections return null to avoid writing an empty array.</summary>
    public static string? SerializeStrings(ImmutableArray<string> values)
    {
        if (values.IsDefaultOrEmpty)
        {
            return null;
        }

        return JsonSerializer.Serialize(values, Options);
    }

    /// <summary>Serializes a string collection to JSON; null or empty sequences return null.</summary>
    public static string? SerializeStrings(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return null;
        }

        var array = values.ToImmutableArray();
        return array.IsDefaultOrEmpty ? null : JsonSerializer.Serialize(array, Options);
    }

    /// <summary>Deserializes a string collection; null, empty, or whitespace JSON returns an empty collection.</summary>
    public static ImmutableArray<string> DeserializeStrings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ImmutableArray<string>.Empty;
        }

        try
        {
            var values = JsonSerializer.Deserialize<ImmutableArray<string>>(json!, Options);
            return values.IsDefault ? ImmutableArray<string>.Empty : values;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The string collection stored in the database is not valid JSON.", exception);
        }
    }

    /// <summary>Serializes a localized dictionary keyed by <see cref="CultureInfo"/>.</summary>
    public static string? SerializeLocalized(ImmutableDictionary<CultureInfo, string> values)
    {
        if (values is null || values.IsEmpty)
        {
            return null;
        }

        var map = new Dictionary<string, string>(values.Count, StringComparer.Ordinal);
        foreach (var pair in values)
        {
            map[pair.Key.Name] = pair.Value;
        }

        return JsonSerializer.Serialize(map, Options);
    }

    /// <summary>Deserializes a localized dictionary; null or empty JSON returns an empty dictionary.</summary>
    public static ImmutableDictionary<CultureInfo, string> DeserializeLocalized(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ImmutableDictionary<CultureInfo, string>.Empty;
        }

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json!, Options);
            if (map is null || map.Count == 0)
            {
                return ImmutableDictionary<CultureInfo, string>.Empty;
            }

            var builder = ImmutableDictionary.CreateBuilder<CultureInfo, string>();
            foreach (var pair in map)
            {
                // An empty name means the invariant culture; CultureInfo does not accept an empty string.
                builder[string.IsNullOrEmpty(pair.Key) ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(pair.Key)]
                    = pair.Value;
            }

            return builder.ToImmutable();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The localized dictionary stored in the database is not valid JSON.", exception);
        }
    }

    /// <summary>Serializes a string-keyed properties dictionary.</summary>
    public static string? SerializeProperties(ImmutableDictionary<string, JsonElement> values)
    {
        if (values is null || values.IsEmpty)
        {
            return null;
        }

        return JsonSerializer.Serialize(values, Options);
    }

    /// <summary>Deserializes a string-keyed properties dictionary; null or empty JSON returns an empty dictionary.</summary>
    public static ImmutableDictionary<string, JsonElement> DeserializeProperties(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ImmutableDictionary<string, JsonElement>.Empty;
        }

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json!, Options);
            return map is null || map.Count == 0
                ? ImmutableDictionary<string, JsonElement>.Empty
                : map.ToImmutableDictionary(StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The properties dictionary stored in the database is not valid JSON.", exception);
        }
    }

    /// <summary>Serializes a string key/value settings dictionary.</summary>
    public static string? SerializeSettings(ImmutableDictionary<string, string> values)
    {
        if (values is null || values.IsEmpty)
        {
            return null;
        }

        return JsonSerializer.Serialize(values, Options);
    }

    /// <summary>Deserializes a string key/value settings dictionary; null or empty JSON returns an empty dictionary.</summary>
    public static ImmutableDictionary<string, string> DeserializeSettings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json!, Options);
            return map is null || map.Count == 0
                ? ImmutableDictionary<string, string>.Empty
                : map.ToImmutableDictionary(StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The settings dictionary stored in the database is not valid JSON.", exception);
        }
    }

    /// <summary>Normalizes a UTC time to microseconds; null stays null.</summary>
    public static DateTime? NormalizeUtc(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        var utc = value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),

            // Unspecified is treated with UTC semantics: entity dates only ever express UTC.
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
        };

        // Persist at microsecond resolution at best (design 15.5): truncate to a 10-tick boundary.
        var microseconds = utc.AddTicks(-(utc.Ticks % 10));

        return DateTime.SpecifyKind(microseconds, DateTimeKind.Utc);
    }

    /// <summary>Normalizes a DateTimeOffset to microseconds and returns it as UTC.</summary>
    public static DateTime? NormalizeUtc(DateTimeOffset? value)
        => value is null ? null : NormalizeUtc(value.Value.UtcDateTime);

    /// <summary>Marks a DateTime read from the database as UTC (drivers usually return Unspecified).</summary>
    public static DateTime? AsUtc(DateTime? value)
        => value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    /// <summary>Serializes a JWK set; null or empty sets return null.</summary>
    public static string? SerializeJsonWebKeySet(Microsoft.IdentityModel.Tokens.JsonWebKeySet? set)
        => OpenIddictStoreJsonWebKey.Serialize(set);

    /// <summary>Deserializes a JWK set; null or empty JSON returns null.</summary>
    public static Microsoft.IdentityModel.Tokens.JsonWebKeySet? DeserializeJsonWebKeySet(string? json)
        => OpenIddictStoreJsonWebKey.Deserialize(json);
}
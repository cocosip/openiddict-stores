using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;

namespace Sharp.OpenIddict.Stores;

/// <summary>
/// JSON conversion for JWK sets. OpenIddict persists <see cref="JsonWebKeySet"/> using the same serialization contract.
/// </summary>
public static class OpenIddictStoreJsonWebKey
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serializes a JWK set; null or empty sets return null.</summary>
    public static string? Serialize(JsonWebKeySet? set)
    {
        if (set is null || set.Keys.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(set, Options);
    }

    /// <summary>Deserializes a JWK set; null or empty JSON returns null.</summary>
    public static JsonWebKeySet? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonWebKeySet>(json!, Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The JWK set stored in the database is not valid JSON.", exception);
        }
    }
}

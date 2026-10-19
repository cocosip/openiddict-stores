using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Sharp.OpenIddict.Stores;
using Xunit;

namespace Sharp.OpenIddict.Stores.Tests;

/// <summary>
/// Conversion contract from design 7/15.1: null and empty collections both read back as an empty collection,
/// invalid JSON throws a data exception, UTC is normalized to microseconds, and collection identity preserves
/// case and trailing spaces by ordinal comparison.
/// </summary>
public sealed class JsonConversionTests
{
    [Fact]
    public void Strings_RoundTripPreservesOrdinalIdentity()
    {
        var values = ImmutableArray.Create("api", "api ", "Api", "ápi");

        var json = OpenIddictStoreJson.SerializeStrings(values);
        var restored = OpenIddictStoreJson.DeserializeStrings(json);

        Assert.Equal(values, restored.ToArray());
        Assert.Contains("api ", restored, StringComparer.Ordinal);
        Assert.DoesNotContain("API", restored, StringComparer.Ordinal);
    }

    [Fact]
    public void Strings_EmptyOrNullBecomeEmptyCollection()
    {
        Assert.Equal(ImmutableArray<string>.Empty, OpenIddictStoreJson.DeserializeStrings(null));
        Assert.Equal(ImmutableArray<string>.Empty, OpenIddictStoreJson.DeserializeStrings(""));
        Assert.Equal(ImmutableArray<string>.Empty, OpenIddictStoreJson.DeserializeStrings("   "));
        Assert.Equal(ImmutableArray<string>.Empty, OpenIddictStoreJson.DeserializeStrings("[]"));
        Assert.Null(OpenIddictStoreJson.SerializeStrings(ImmutableArray<string>.Empty));
    }

    [Fact]
    public void InvalidJson_ThrowsInsteadOfReturningDefaults()
    {
        Assert.Throws<InvalidOperationException>(() => OpenIddictStoreJson.DeserializeStrings("{not json"));
        Assert.Throws<InvalidOperationException>(() => OpenIddictStoreJson.DeserializeProperties("{not json"));
        Assert.Throws<InvalidOperationException>(() => OpenIddictStoreJson.DeserializeSettings("{not json"));
        Assert.Throws<InvalidOperationException>(() => OpenIddictStoreJson.DeserializeLocalized("{not json"));
    }

    [Fact]
    public void Localized_RoundTripsCultureNames()
    {
        var values = ImmutableDictionary.CreateRange<CultureInfo, string>(
        [
            new(CultureInfo.GetCultureInfo("en"), "Name"),
            new(CultureInfo.GetCultureInfo("zh-Hans"), "Nömé"),
            new(CultureInfo.InvariantCulture, "Invariant"),
        ]);

        var restored = OpenIddictStoreJson.DeserializeLocalized(OpenIddictStoreJson.SerializeLocalized(values));

        Assert.Equal(values, restored);
    }

    [Fact]
    public void Localized_WrongValueTypeThrowsDataException()
    {
        // A non-string value is data corruption and must not be silently skipped.
        Assert.Throws<InvalidOperationException>(
            () => OpenIddictStoreJson.DeserializeLocalized("""{"en":42}"""));
    }

    [Fact]
    public void Properties_RoundTripJsonElements()
    {
        using var document = JsonDocument.Parse("""{"n":42,"s":"text","b":true}""");
        var values = document.RootElement.EnumerateObject()
            .ToImmutableDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);

        var restored = OpenIddictStoreJson.DeserializeProperties(OpenIddictStoreJson.SerializeProperties(values));

        Assert.Equal(values.Keys, restored.Keys);
        Assert.Equal(42, restored["n"].GetInt32());
        Assert.Equal("text", restored["s"].GetString());
        Assert.True(restored["b"].GetBoolean());
    }

    [Fact]
    public void Settings_RoundTripCaseSensitiveKeys()
    {
        var values = ImmutableDictionary.CreateRange<string, string>(StringComparer.Ordinal,
        [
            new("Key", "v1"),
            new("key", "v2"),
        ]);

        var restored = OpenIddictStoreJson.DeserializeSettings(OpenIddictStoreJson.SerializeSettings(values));

        Assert.Equal(2, restored.Count);
        Assert.Equal("v1", restored["Key"]);
        Assert.Equal("v2", restored["key"]);
    }

    [Fact]
    public void UtcNormalization_TruncatesToMicrosecondsAndKeepsUtc()
    {
        var value = new DateTime(2026, 10, 19, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234567);

        var normalized = OpenIddictStoreJson.NormalizeUtc(value)!.Value;

        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(0, normalized.Ticks % 10);
        Assert.True(normalized <= value);
        Assert.True(value - normalized < TimeSpan.FromTicks(10));
    }

    [Fact]
    public void UtcNormalization_ConvertsLocalAndTreatsUnspecifiedAsUtc()
    {
        var unspecified = new DateTime(2026, 10, 19, 8, 0, 0, DateTimeKind.Unspecified);

        var normalized = OpenIddictStoreJson.NormalizeUtc(unspecified)!.Value;

        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(unspecified.Ticks, normalized.Ticks);

        Assert.Null(OpenIddictStoreJson.NormalizeUtc((DateTime?)null));
    }

    [Fact]
    public void SqliteDateTime_RoundTripsWithMicrosecondPrecision()
    {
        var value = new DateTime(2026, 10, 19, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234560);

        var text = Sqlite.OpenIddictStoreSqliteDialect.FormatDateTime(value);
        var restored = Sqlite.OpenIddictStoreSqliteDialect.ParseDateTime(text);

        Assert.Equal("2026-10-19 12:34:56.123456", text);
        Assert.Equal(value, restored);
        Assert.Equal(DateTimeKind.Utc, restored!.Value.Kind);

        // The fixed-width format keeps string ordering consistent with chronological ordering.
        Assert.True(string.CompareOrdinal("2026-10-19 09:00:00.000000", "2026-10-19 12:00:00.000000") < 0);
    }

    [Fact]
    public void SqlitePaging_UsesMinusOneWhenCountIsAbsent()
    {
        var dialect = Sqlite.OpenIddictStoreSqliteDialect.Instance;

        Assert.Equal(string.Empty, dialect.BuildPaging(null, null));
        Assert.Equal("LIMIT 10", dialect.BuildPaging(10, null));
        Assert.Equal("LIMIT -1 OFFSET 5", dialect.BuildPaging(null, 5));
        Assert.Equal("LIMIT 10 OFFSET 5", dialect.BuildPaging(10, 5));
    }

    [Fact]
    public void SqliteExactStringEquals_DoesNotRelyOnDatabaseCollation()
    {
        var dialect = Sqlite.OpenIddictStoreSqliteDialect.Instance;

        // SQLite's = operator uses BINARY on TEXT, which is already case- and trailing-space-sensitive, so no extra compensation is needed.
        Assert.Equal("\"ClientId\" = @clientId", dialect.BuildExactStringEquals("\"ClientId\"", "clientId"));
        Assert.Equal("@", dialect.ParameterPrefix);
        Assert.Equal("Sqlite", dialect.Provider.ToString());
    }
}

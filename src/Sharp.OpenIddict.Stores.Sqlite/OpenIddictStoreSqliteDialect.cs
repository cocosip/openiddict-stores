using System;
using System.Globalization;
using Sharp.OpenIddict.Stores;

namespace Sharp.OpenIddict.Stores.Sqlite;

/// <summary>
/// SQLite dialect. Identifiers are quoted with double quotes and the parameter prefix is <c>@</c>.
/// </summary>
public sealed class OpenIddictStoreSqliteDialect : IOpenIddictStoreDialect
{
    /// <summary>Shared instance: the dialect is stateless.</summary>
    public static OpenIddictStoreSqliteDialect Instance { get; } = new();

    /// <inheritdoc />
    public OpenIddictStoreProvider Provider => OpenIddictStoreProvider.Sqlite;

    /// <inheritdoc />
    public string ParameterPrefix => "@";

    /// <inheritdoc />
    /// <remarks>SQLite allows 999 variables by default (32766 since 3.32); this takes the conservative value and leaves headroom for conditional parameters.</remarks>
    public int MaximumParameters => 900;

    /// <inheritdoc />
    public string QuoteIdentifier(string identifier)
        => "\"" + OpenIddictStoreIdentifiers.Validate(identifier, nameof(identifier)) + "\"";

    /// <inheritdoc />
    /// <remarks>
    /// SQLite's <c>=</c> uses the BINARY collation for TEXT: it is case-sensitive, accent-sensitive and
    /// trailing-space-sensitive, so no additional length or binary comparison compensation is needed.
    /// </remarks>
    public string BuildExactStringEquals(string column, string parameterName)
        => $"{column} = @{parameterName}";

    /// <inheritdoc />
    /// <remarks>SQLite requires LIMIT to be present in order to use OFFSET; -1 means no limit when there is no count.</remarks>
    public string BuildPaging(int? count, int? offset)
    {
        if (count is null && offset is null)
        {
            return string.Empty;
        }

        if (offset is null)
        {
            return "LIMIT " + count!.Value.ToString(CultureInfo.InvariantCulture);
        }

        var limit = count?.ToString(CultureInfo.InvariantCulture) ?? "-1";
        return "LIMIT " + limit + " OFFSET " + offset.Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A plain <c>&lt;&gt;</c> yields UNKNOWN for NULL and would silently drop those rows. The
    /// explicit NULL branch keeps them and still lets the caller's other predicates constrain them.
    /// </remarks>
    public string BuildExactStringNotEqualsIncludingNull(string column, string parameterName)
        => $"({column} IS NULL OR {column} <> @{parameterName})";

    /// <inheritdoc />
    /// <remarks>
    /// Two TEXT columns compared with <c>=</c> already use the BINARY collation, so case, accents, and
    /// trailing spaces remain significant without extra compensation.
    /// </remarks>
    public string BuildExactStringColumnEquals(string left, string right)
        => $"{left} = {right}";

    /// <summary>The fixed text format SQLite uses to store UTC times (microsecond precision, fixed width, directly sortable as strings).</summary>
    public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss.ffffff";

    /// <inheritdoc />
    /// <remarks>SQLite has no native date type, so values are always bound as fixed-format UTC text.</remarks>
    public object? FormatDate(DateTime? value) => FormatDateTime(value);

    /// <inheritdoc />
    public DateTime? ReadDate(System.Data.Common.DbDataReader reader, int ordinal)
        => reader.GetFieldType(ordinal) == typeof(DateTime)
            ? OpenIddictStoreJson.AsUtc(reader.GetDateTime(ordinal))
            : ParseDateTime(reader.GetString(ordinal));

    /// <summary>Formats a UTC time as database text.</summary>
    public static string? FormatDateTime(DateTime? value)
        => value is null
            ? null
            : OpenIddictStoreJson.NormalizeUtc(value)!.Value.ToString(DateTimeFormat, CultureInfo.InvariantCulture);

    /// <summary>Parses database text into a UTC time.</summary>
    public static DateTime? ParseDateTime(string? value)
        => string.IsNullOrEmpty(value)
            ? null
            : DateTime.SpecifyKind(
                DateTime.Parse(value!, CultureInfo.InvariantCulture, DateTimeStyles.None),
                DateTimeKind.Utc);
}

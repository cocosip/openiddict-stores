using System;
using System.Data.Common;
using System.Globalization;
using Sharp.OpenIddict.Stores;

namespace Sharp.OpenIddict.Stores.MySql;

/// <summary>
/// MySQL and MariaDB dialect. Identifiers are quoted with backticks, the parameter prefix is <c>@</c>, and
/// every plain string comparison adds the <c>BINARY</c> operator: with a <c>utf8mb4_*_ci</c> or
/// <c>utf8mb4_*_ai_ci</c> collation, <c>=</c> ignores case and, because those collations are PAD SPACE,
/// also ignores trailing spaces (design 16.1.1). <c>BINARY</c> compares byte-wise, so both become significant.
/// </summary>
/// <remarks>
/// One dialect serves MySQL 8 and MariaDB 10.6. The two differ in DDL details (see <c>Sql/README.md</c>), but
/// the comparison, paging, and parameter rules are the same; the acceptance records stay separate because the
/// servers are (design 5).
/// </remarks>
public sealed class OpenIddictStoreMySqlDialect : IOpenIddictStoreDialect
{
    /// <summary>Shared instance: the dialect is stateless.</summary>
    public static OpenIddictStoreMySqlDialect Instance { get; } = new();

    /// <inheritdoc />
    public OpenIddictStoreProvider Provider => OpenIddictStoreProvider.MySql;

    /// <inheritdoc />
    public string ParameterPrefix => "@";

    /// <inheritdoc />
    /// <remarks>MySQL limits a statement to 65535 placeholders; 65000 leaves headroom for the values a statement already carries.</remarks>
    public int MaximumParameters => 65_000;

    /// <inheritdoc />
    /// <remarks>MySQL allows 64 characters for identifiers and refuses longer names instead of truncating them.</remarks>
    public int MaximumIdentifierLength => 64;

    /// <inheritdoc />
    /// <remarks>Backticks are MySQL's portable quoting form; ANSI double quotes depend on a server mode.</remarks>
    public string QuoteIdentifier(string identifier)
        => "`" + OpenIddictStoreIdentifiers.ValidateLength(
            OpenIddictStoreIdentifiers.Validate(identifier, nameof(identifier)), MaximumIdentifierLength) + "`";

    /// <inheritdoc />
    /// <remarks>
    /// <c>BINARY</c> is required, not optional: PAD SPACE collations treat <c>ab</c> and <c>ab </c> as equal, so a
    /// plain <c>=</c> would let a lookup find the wrong row. The operator also makes the comparison
    /// case-sensitive regardless of a <c>_ci</c> collation.
    /// </remarks>
    public string BuildExactStringEquals(string column, string parameterName)
        => $"({column} = BINARY @{parameterName})";

    /// <inheritdoc />
    /// <remarks>
    /// <c>LIMIT</c> is required before <c>OFFSET</c> in MySQL, so an offset without a count needs the documented
    /// "no limit" sentinel. <c>18446744073709551615</c> is the largest unsigned value and is what MySQL itself
    /// documents for "all rows from here on".
    /// </remarks>
    public string BuildPaging(int? count, int? offset)
    {
        if (count is null && offset is null)
        {
            return string.Empty;
        }

        var limit = count is null
            ? "LIMIT 18446744073709551615"
            : "LIMIT " + count.Value.ToString(CultureInfo.InvariantCulture);

        return offset is null
            ? limit
            : limit + " OFFSET " + offset.Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A plain <c>&lt;&gt;</c> yields NULL for NULL and would silently drop those rows. The NULL branch keeps
    /// them, and <c>BINARY</c> keeps the comparison exact (design 16.2.2).
    /// </remarks>
    public string BuildExactStringNotEqualsIncludingNull(string column, string parameterName)
        => $"({column} IS NULL OR {column} <> BINARY @{parameterName})";

    /// <inheritdoc />
    /// <remarks>Column-to-column comparison forces <c>BINARY</c> on the left side so both sides compare byte-wise.</remarks>
    public string BuildExactStringColumnEquals(string left, string right)
        => $"(BINARY {left} = {right})";

    /// <inheritdoc />
    /// <remarks>MySQL keeps the empty string distinct from NULL, so an empty bind compares as a real value.</remarks>
    public bool EmptyStringBindsAsNull => false;

    /// <inheritdoc />
    /// <remarks>
    /// Both servers accept a <c>DateTime</c> for <c>datetime(6)</c> whatever its <see cref="DateTimeKind"/> and
    /// store the value verbatim, so the UTC value is bound unchanged; the driver must not convert it. The value
    /// is already normalized to microseconds, which is the column's resolution.
    /// </remarks>
    public object? FormatDate(DateTime? value)
        => value is null ? null : OpenIddictStoreJson.NormalizeUtc(value)!.Value;

    /// <inheritdoc />
    /// <remarks><c>datetime</c> reads back with <see cref="DateTimeKind.Unspecified"/>, so UTC must be restored explicitly.</remarks>
    public DateTime? ReadDate(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal)
            ? null
            : OpenIddictStoreJson.AsUtc(reader.GetDateTime(ordinal));
}

using System;
using System.Data.Common;
using System.Globalization;
using Sharp.OpenIddict.Stores;

namespace Sharp.OpenIddict.Stores.SqlServer;

/// <summary>
/// SQL Server dialect. Identifiers are bracketed, the parameter prefix is <c>@</c>, and every plain
/// string comparison is forced to a binary collation with an explicit length check (design 16.1.1).
/// </summary>
public sealed class OpenIddictStoreSqlServerDialect : IOpenIddictStoreDialect
{
    /// <summary>
    /// The binary collation used for exact comparisons. <c>BIN2</c> still ignores trailing spaces when
    /// comparing equal-length padding, so every predicate below also compares <c>DATALENGTH</c>.
    /// </summary>
    public const string ExactCollation = "Latin1_General_100_BIN2";

    /// <summary>Shared instance: the dialect is stateless.</summary>
    public static OpenIddictStoreSqlServerDialect Instance { get; } = new();

    /// <inheritdoc />
    public OpenIddictStoreProvider Provider => OpenIddictStoreProvider.SqlServer;

    /// <inheritdoc />
    public string ParameterPrefix => "@";

    /// <inheritdoc />
    /// <remarks>SQL Server allows 2100 parameters per command; 2000 leaves headroom for the values a statement already carries.</remarks>
    public int MaximumParameters => 2_000;

    /// <inheritdoc />
    public string QuoteIdentifier(string identifier)
        => "[" + OpenIddictStoreIdentifiers.Validate(identifier, nameof(identifier)) + "]";

    /// <inheritdoc />
    /// <remarks>
    /// Two comparisons are required: <c>BIN2</c> makes case, accents, and normalization form significant,
    /// but it still treats <c>client</c> and <c>client </c> as equal, so <c>DATALENGTH</c> must reject the
    /// length difference. Verified against a real server; see the DB-02 record.
    /// </remarks>
    public string BuildExactStringEquals(string column, string parameterName)
        => $"({column} COLLATE {ExactCollation} = @{parameterName} AND DATALENGTH({column}) = DATALENGTH(@{parameterName}))";

    /// <inheritdoc />
    /// <remarks>
    /// SQL Server requires <c>OFFSET</c> to be paired with an <c>ORDER BY</c>; every caller supplies one.
    /// <c>FETCH NEXT 0 ROWS</c> is a syntax error, so a zero count is rejected here instead of silently
    /// returning rows; the stores never reach this case because <c>Take(0)</c> and <c>count: 0</c> are
    /// handled before the SQL is built.
    /// </remarks>
    public string BuildPaging(int? count, int? offset)
    {
        if (count is null && offset is null)
        {
            return string.Empty;
        }

        if (count is 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count),
                "SQL Server cannot express FETCH NEXT 0 ROWS; request one row and discard it instead.");
        }

        var start = offset ?? 0;
        var paging = "OFFSET " + start.ToString(CultureInfo.InvariantCulture) + " ROWS";

        if (count is not null)
        {
            paging += " FETCH NEXT " + count.Value.ToString(CultureInfo.InvariantCulture) + " ROWS ONLY";
        }

        return paging;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A plain <c>&lt;&gt;</c> yields UNKNOWN for NULL and would silently drop those rows. The NULL branch
    /// keeps them and still lets the caller's other predicates constrain them (design 16.2.2).
    /// </remarks>
    public string BuildExactStringNotEqualsIncludingNull(string column, string parameterName)
        => $"({column} IS NULL OR {column} COLLATE {ExactCollation} <> @{parameterName} " +
           $"OR DATALENGTH({column}) <> DATALENGTH(@{parameterName}))";

    /// <inheritdoc />
    /// <remarks>Column-to-column comparison needs the same two checks as the parameter form.</remarks>
    public string BuildExactStringColumnEquals(string left, string right)
        => $"({left} COLLATE {ExactCollation} = {right} AND DATALENGTH({left}) = DATALENGTH({right}))";

    /// <inheritdoc />
    /// <remarks>
    /// The value is bound as a <see cref="DateTimeOffset"/> in UTC. Binding a plain <see cref="DateTime"/>
    /// lets the driver infer <c>datetime</c> (3.33 ms resolution), which silently truncates the stored
    /// microseconds; a UTC <see cref="DateTimeOffset"/> keeps them and still compares against
    /// <c>datetime2(6)</c> by instant. Both behaviours were measured on a real server.
    /// </remarks>
    public object? FormatDate(DateTime? value)
        => value is null
            ? null
            : new DateTimeOffset(OpenIddictStoreJson.NormalizeUtc(value)!.Value, TimeSpan.Zero);

    /// <inheritdoc />
    /// <remarks><c>datetime2</c> reads back with <see cref="DateTimeKind.Unspecified"/>, so UTC must be restored explicitly.</remarks>
    public DateTime? ReadDate(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal)
            ? null
            : reader.GetFieldType(ordinal) == typeof(DateTimeOffset)
                ? OpenIddictStoreJson.AsUtc(reader.GetFieldValue<DateTimeOffset>(ordinal).UtcDateTime)
                : OpenIddictStoreJson.AsUtc(reader.GetDateTime(ordinal));
}

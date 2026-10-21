using System;
using System.Data.Common;
using System.Globalization;
using Sharp.OpenIddict.Stores;

namespace Sharp.OpenIddict.Stores.Oracle;

/// <summary>
/// Oracle dialect. Identifiers are quoted with double quotes (Oracle folds unquoted names to upper case, while
/// the runtime uses mixed-case names), the parameter prefix is <c>:</c>, and paging uses the row-limiting clause
/// available from 12c. Exact comparison forces a binary sort so the session's <c>NLS_COMP</c>/<c>NLS_SORT</c>
/// cannot make it case- or accent-insensitive (design 16.1.1, 257).
/// </summary>
/// <remarks>
/// Empty strings are a documented Oracle behaviour rather than a dialect choice: Oracle stores <c>''</c> as NULL
/// in <c>VARCHAR2</c>. The built-in store paths never compare against an empty-string parameter, because every
/// optional filter is already skipped when the caller passes null or an empty string (design 16.1); a translated
/// query callback can, and <see cref="EmptyStringBindsAsNull"/> tells the translator to keep C# semantics there.
/// The README records the limitation for values inside JSON, which are unaffected.
/// </remarks>
public sealed class OpenIddictStoreOracleDialect : IOpenIddictStoreDialect
{
    /// <summary>Shared instance: the dialect is stateless.</summary>
    public static OpenIddictStoreOracleDialect Instance { get; } = new();

    /// <inheritdoc />
    public OpenIddictStoreProvider Provider => OpenIddictStoreProvider.Oracle;

    /// <inheritdoc />
    public string ParameterPrefix => ":";

    /// <inheritdoc />
    /// <remarks>
    /// Oracle allows 65535 expressions in an <c>IN</c> list but a few thousand bind variables are the practical
    /// ceiling; 900 keeps every statement well inside the limits of both plain SQL and PL/SQL blocks used by
    /// drivers, and matches the batch size the stores already cap themselves to.
    /// </remarks>
    public int MaximumParameters => 900;

    /// <inheritdoc />
    /// <remarks>
    /// 128 bytes since 12.2, which is below the declared 19c floor. The historical 30-byte limit belonged to
    /// <c>COMPATIBLE &lt; 12.2</c>; using it would make even `{prefix}ApplicationPostLogoutRedirectUris`
    /// impossible to create with the default prefix.
    /// </remarks>
    public int MaximumIdentifierLength => 128;

    /// <inheritdoc />
    /// <remarks>
    /// Unquoted Oracle names fold to upper case, so the mixed-case runtime names must be quoted. An identifier
    /// that already contains a double quote is rejected by the validation rules before reaching here.
    /// </remarks>
    public string QuoteIdentifier(string identifier)
        => "\"" + OpenIddictStoreIdentifiers.ValidateLength(
            OpenIddictStoreIdentifiers.Validate(identifier, nameof(identifier)), MaximumIdentifierLength) + "\"";

    /// <inheritdoc />
    /// <remarks>
    /// <c>VARCHAR2</c> comparison is non-padded, so trailing spaces already matter; case and accent significance,
    /// however, follow <c>NLS_COMP</c>/<c>NLS_SORT</c>. Wrapping the parameter in <c>NLSSORT(..., 'NLS_SORT=BINARY')</c>
    /// pins both sides to a byte-wise sort without depending on client or server session settings. No length
    /// compensation is needed because the sort is over the whole value.
    /// </remarks>
    public string BuildExactStringEquals(string column, string parameterName)
        => $"(NLSSORT({column}, 'NLS_SORT=BINARY') = NLSSORT(:{parameterName}, 'NLS_SORT=BINARY'))";

    /// <inheritdoc />
    /// <remarks>
    /// Oracle requires <c>OFFSET</c> to be paired with a row-limiting clause, and every caller supplies an
    /// <c>ORDER BY</c>. <c>FETCH FIRST 0 ROWS ONLY</c> is legal, unlike SQL Server's <c>FETCH NEXT 0</c>.
    /// </remarks>
    public string BuildPaging(int? count, int? offset)
    {
        if (count is null && offset is null)
        {
            return string.Empty;
        }

        var paging = offset is null
            ? string.Empty
            : "OFFSET " + offset.Value.ToString(CultureInfo.InvariantCulture) + " ROWS";

        if (count is not null)
        {
            if (paging.Length > 0)
            {
                paging += " ";
            }

            paging += "FETCH NEXT " + count.Value.ToString(CultureInfo.InvariantCulture) + " ROWS ONLY";
        }

        return paging;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A plain <c>&lt;&gt;</c> yields UNKNOWN for NULL and would silently drop those rows; the NULL branch keeps
    /// them. The binary sort keeps the inequality exact (design 16.2.2).
    /// </remarks>
    public string BuildExactStringNotEqualsIncludingNull(string column, string parameterName)
        => $"({column} IS NULL OR NLSSORT({column}, 'NLS_SORT=BINARY') <> NLSSORT(:{parameterName}, 'NLS_SORT=BINARY'))";

    /// <inheritdoc />
    /// <remarks>Column-to-column comparison needs the same binary sort on both sides.</remarks>
    public string BuildExactStringColumnEquals(string left, string right)
        => $"(NLSSORT({left}, 'NLS_SORT=BINARY') = NLSSORT({right}, 'NLS_SORT=BINARY'))";

    /// <inheritdoc />
    /// <remarks>
    /// Oracle binds and stores <c>''</c> as NULL, so no stored value can equal the empty string; the
    /// translator resolves <c>== ""</c> to an unsatisfiable predicate instead of an accidental null match.
    /// </remarks>
    public bool EmptyStringBindsAsNull => true;

    /// <inheritdoc />
    /// <remarks>
    /// The driver converts a <see cref="DateTimeKind.Utc"/> value to the session time zone before binding it, so
    /// the annotation is dropped and the UTC wall-clock value is stored as-is; the same rule applies to DM.
    /// Values are already normalized to microseconds, below <c>TIMESTAMP(6)</c>'s resolution.
    /// </remarks>
    public object? FormatDate(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        return DateTime.SpecifyKind(OpenIddictStoreJson.NormalizeUtc(value)!.Value, DateTimeKind.Unspecified);
    }

    /// <inheritdoc />
    /// <remarks><c>TIMESTAMP</c> reads back with <see cref="DateTimeKind.Unspecified"/>, so UTC must be restored explicitly.</remarks>
    public DateTime? ReadDate(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal)
            ? null
            : OpenIddictStoreJson.AsUtc(reader.GetDateTime(ordinal));
}

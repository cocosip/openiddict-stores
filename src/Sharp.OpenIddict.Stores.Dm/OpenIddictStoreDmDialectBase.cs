using System;
using System.Data.Common;
using Sharp.OpenIddict.Stores;

namespace Sharp.OpenIddict.Stores.Dm;

/// <summary>
/// The members every DM8 dialect shares regardless of its compatibility mode: identifier quoting, the
/// parameter conventions, exact-comparison predicates, and date binding. What a compatibility mode actually
/// changes — paging syntax above all — stays abstract and is declared by each mode's dialect (design 5, 10.1).
/// </summary>
/// <remarks>
/// Exact comparison is a plain equality in every mode: DM8 compares strings case-sensitively on an instance
/// initialized with the default <c>CASE_SENSITIVE=1</c>, and the modes do not change that on their own. An
/// instance created with <c>CASE_SENSITIVE=0</c> folds case in comparisons and violates the exact-match
/// contract no matter which mode it runs, so that prerequisite is a deployment requirement recorded in every
/// mode's script and README rather than something a predicate could repair portably (design 16.1.1).
/// </remarks>
public abstract class OpenIddictStoreDmDialectBase : IOpenIddictStoreDialect
{
    /// <inheritdoc />
    public OpenIddictStoreProvider Provider => OpenIddictStoreProvider.Dm;

    /// <inheritdoc />
    public string ParameterPrefix => ":";

    /// <inheritdoc />
    /// <remarks>DM8 keeps Oracle's practical bind limits; 900 matches the batch ceiling the stores already use.</remarks>
    public int MaximumParameters => 900;

    /// <inheritdoc />
    /// <remarks>DM8 allows 128 bytes for identifiers in the versions this provider targets.</remarks>
    public int MaximumIdentifierLength => 128;

    /// <inheritdoc />
    /// <remarks>
    /// DM8 folds unquoted identifiers to upper case in every compatibility mode, while the runtime uses
    /// mixed-case table and column names, so quoting is mandatory rather than cosmetic.
    /// </remarks>
    public string QuoteIdentifier(string identifier)
        => "\"" + OpenIddictStoreIdentifiers.ValidateLength(
            OpenIddictStoreIdentifiers.Validate(identifier, nameof(identifier)), MaximumIdentifierLength) + "\"";

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Two comparisons are required. A plain equality is exact for case and accents on an instance
    /// initialized with <c>CASE_SENSITIVE=1</c>, but DM8 compares strings blank-padded: <c>client</c> and
    /// <c>client </c> compare equal, so <c>CHAR_LENGTH</c> must reject the length difference. Verified
    /// against real servers in the declared modes; see the acceptance record. (<c>CHAR_LENGTH</c> is used
    /// over <c>LENGTH</c>/byte functions because it counts characters, so the guard cannot depend on the
    /// character set of either side.)
    /// </para>
    /// </remarks>
    public string BuildExactStringEquals(string column, string parameterName)
        => $"({column} = :{parameterName} AND CHAR_LENGTH({column}) = CHAR_LENGTH(:{parameterName}))";

    /// <inheritdoc />
    /// <remarks>
    /// A plain <c>&lt;&gt;</c> yields UNKNOWN for NULL and would silently drop those rows; the NULL branch
    /// keeps them (design 16.2.2). The length guard mirrors <see cref="BuildExactStringEquals"/>: DM8
    /// compares blank-padded, so a trailing-space variant must count as different here as well.
    /// </remarks>
    public string BuildExactStringNotEqualsIncludingNull(string column, string parameterName)
        => $"({column} IS NULL OR {column} <> :{parameterName} " +
           $"OR CHAR_LENGTH({column}) <> CHAR_LENGTH(:{parameterName}))";

    /// <inheritdoc />
    /// <remarks>Column-to-column comparison needs the same two checks as the parameter form.</remarks>
    public string BuildExactStringColumnEquals(string left, string right)
        => $"({left} = {right} AND CHAR_LENGTH({left}) = CHAR_LENGTH({right}))";

    /// <inheritdoc />
    /// <remarks>
    /// DM8 stores <c>TIMESTAMP</c> without a time zone and its driver may convert a <see cref="DateTimeKind.Utc"/>
    /// value using the session time zone, so the annotation is dropped and the UTC wall-clock value is stored
    /// as-is; the value is already normalized to microseconds.
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

    /// <inheritdoc />
    public abstract string BuildPaging(int? count, int? offset);
}

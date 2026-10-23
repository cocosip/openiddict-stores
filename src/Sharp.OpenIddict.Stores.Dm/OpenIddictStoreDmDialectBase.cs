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
/// mode's script and README (design 16.1.1). Because DM8 offers no predicate that could force the semantics
/// and no view that exposes the setting, the requirement is enforced by a startup probe
/// (<see cref="IOpenIddictStoreStartupProbe"/>) instead of being trusted.
/// </remarks>
public abstract class OpenIddictStoreDmDialectBase : IOpenIddictStoreDialect, IOpenIddictStoreStartupProbe
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
    /// <remarks>
    /// DM8 distinguishes the empty string from NULL by default, so an empty bind compares as a real value;
    /// only the Oracle-compatible mode converts empty strings to NULL, and that mode's dialect overrides this.
    /// </remarks>
    public virtual bool EmptyStringBindsAsNull => false;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// DM8 has no predicate that pins the comparison to a binary sort the way <c>COLLATE</c>, <c>BINARY</c>, and
    /// <c>NLSSORT</c> do elsewhere, and <c>CASE_SENSITIVE</c> is fixed at database-creation time and absent from
    /// <c>v$dm_ini</c>. The only way to learn whether the instance folds case is to compare two values, so the
    /// probe does exactly that: <c>1</c> means the instance cannot honour the exact-match contract.
    /// </para>
    /// <para>
    /// The version table is the <c>FROM</c> source rather than <c>DUAL</c>: the guard has just read a row from it,
    /// so it exists, is readable, and has exactly one row in every mode — unlike the catalog objects a
    /// compatibility mode may or may not expose to the connecting user.
    /// </para>
    /// </remarks>
    public string BuildProbeSql(string versionsTable)
        => $"SELECT CASE WHEN 'abc' = 'ABC' THEN 1 ELSE 0 END FROM {versionsTable}";

    /// <inheritdoc />
    public string ProbeFailureMessage =>
        "This DM8 instance was initialized with CASE_SENSITIVE=0, so it folds case in string comparisons and " +
        "cannot honour the exact-match contract (design 16.1.1): FindByClientId, FindByName, FindByReferenceId, " +
        "and FindBySubject would silently match the wrong row. CASE_SENSITIVE is fixed when the database is " +
        "created and cannot be changed afterwards, and no compatibility mode overrides it. Re-create the " +
        "database with CASE_SENSITIVE=1 and run the install script again; this store will not fall back to an " +
        "inexact comparison.";

    /// <inheritdoc />
    public abstract string BuildPaging(int? count, int? offset);
}

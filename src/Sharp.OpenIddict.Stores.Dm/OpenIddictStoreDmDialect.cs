using System;
using System.Data.Common;
using System.Globalization;
using Sharp.OpenIddict.Stores;

namespace Sharp.OpenIddict.Stores.Dm;

/// <summary>
/// Dameng DM8 dialect, implemented independently of the Oracle one and scoped to a single, named
/// compatibility mode.
/// </summary>
/// <remarks>
/// <para>
/// DM8 runs under an instance-level compatibility mode (<c>COMPATIBLE_MODE</c>): the same server behaves
/// differently for string comparison, empty strings, paging syntax, and DDL depending on that setting, and the
/// Oracle-compatible mode is a different target from native mode. A dialect therefore cannot be "the DM
/// dialect"; it has to name the mode it was written for. This one targets <b>native mode</b> only.
/// </para>
/// <para>
/// The mode is not claimed by a manifest entry, a package description, or a README paragraph that mentions it
/// in passing: <c>Sql/manifest.json</c> lists exactly one entry and names the mode in its <c>server</c> field,
/// and a unit test pins that, so claiming Oracle compatibility requires a separate entry, a separate script, and
/// a separate acceptance run (design 5, 10.1).
/// </para>
/// <para>
/// Behaviour that differs by mode is left unverified rather than guessed: exact string comparison and the
/// paging clause are marked in <c>Sql/README.md</c> as release blockers to confirm on a real server in the
/// declared mode. Guessing would be worse than an honest gap, because a silently case-insensitive comparison
/// corrupts lookups without any error.
/// </para>
/// </remarks>
public sealed class OpenIddictStoreDmDialect : IOpenIddictStoreDialect
{
    /// <summary>
    /// The compatibility mode this dialect is written for. It is part of the dialect's identity, not a
    /// configurable switch: a different mode needs a different, separately accepted implementation.
    /// </summary>
    public const string CompatibilityMode = "native";

    /// <summary>Shared instance: the dialect is stateless.</summary>
    public static OpenIddictStoreDmDialect Instance { get; } = new();

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
    /// DM8 folds unquoted identifiers to upper case by default, while the runtime uses mixed-case table and
    /// column names, so quoting is mandatory rather than cosmetic.
    /// </remarks>
    public string QuoteIdentifier(string identifier)
        => "\"" + OpenIddictStoreIdentifiers.ValidateLength(
            OpenIddictStoreIdentifiers.Validate(identifier, nameof(identifier)), MaximumIdentifierLength) + "\"";

    /// <inheritdoc />
    /// <remarks>
    /// A plain equality is emitted because DM8's <c>VARCHAR</c> comparison depends on the instance's
    /// character set and on the compatibility mode, and neither is verified for this release. This is a
    /// declared release blocker, not a satisfied requirement (design 16.1.1).
    /// </remarks>
    public string BuildExactStringEquals(string column, string parameterName)
        => $"({column} = :{parameterName})";

    /// <inheritdoc />
    /// <remarks>
    /// A plain <c>&lt;&gt;</c> yields UNKNOWN for NULL and would silently drop those rows; the NULL branch keeps
    /// them, which is independent of the compatibility mode (design 16.2.2).
    /// </remarks>
    public string BuildExactStringNotEqualsIncludingNull(string column, string parameterName)
        => $"({column} IS NULL OR {column} <> :{parameterName})";

    /// <inheritdoc />
    public string BuildExactStringColumnEquals(string left, string right)
        => $"({left} = {right})";

    /// <inheritdoc />
    /// <remarks>
    /// <c>LIMIT</c>/<c>OFFSET</c> is used because DM8 documents it as one of its paging forms and it does not
    /// depend on a row-count pseudocolumn. Which forms a given instance accepts still depends on the
    /// compatibility mode, so this clause is one of the blockers listed in <c>Sql/README.md</c>. A count-only
    /// request needs no sentinel; an offset without a count uses the largest signed 32-bit value, which is the
    /// documented "effectively unbounded" idiom rather than a magic constant of this project's own invention.
    /// </remarks>
    public string BuildPaging(int? count, int? offset)
    {
        if (count is null && offset is null)
        {
            return string.Empty;
        }

        var limit = "LIMIT " + (count ?? int.MaxValue).ToString(CultureInfo.InvariantCulture);

        return offset is null
            ? limit
            : limit + " OFFSET " + offset.Value.ToString(CultureInfo.InvariantCulture);
    }

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
}

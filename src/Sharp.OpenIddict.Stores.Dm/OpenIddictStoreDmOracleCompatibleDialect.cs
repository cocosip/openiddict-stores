using System;
using System.Globalization;

namespace Sharp.OpenIddict.Stores.Dm;

/// <summary>
/// Dameng DM8 dialect for the <b>Oracle-compatible</b> mode (<c>COMPATIBLE_MODE = 2</c>). A separate target
/// from native mode with its own manifest entry and its own acceptance run: the mode changes the empty-string
/// semantics and the paging forms the server accepts (design 5, 10.1).
/// </summary>
/// <remarks>
/// <para>
/// In this mode DM8 stores the empty string as NULL, like Oracle does. The stores need no workaround for that
/// either way: every optional filter is skipped when the caller passes null or an empty string, so no
/// predicate ever compares against an empty-string parameter.
/// </para>
/// <para>
/// The row-limiting clause follows the Oracle-style standard form rather than <c>LIMIT</c>: whether a given
/// instance accepts which paging forms depends on the compatibility mode, so this clause is an acceptance
/// item recorded in <c>Sql/README.md</c>. An offset without a count is expressed on its own, and a count of
/// zero is expressible directly.
/// </para>
/// </remarks>
public sealed class OpenIddictStoreDmOracleCompatibleDialect : OpenIddictStoreDmDialectBase
{
    /// <summary>
    /// The compatibility mode this dialect is written for. It is part of the dialect's identity, not a
    /// configurable switch: a different mode needs a different, separately accepted implementation.
    /// </summary>
    public const string CompatibilityMode = "oracle-compatible";

    /// <summary>Shared instance: the dialect is stateless.</summary>
    public static OpenIddictStoreDmOracleCompatibleDialect Instance { get; } = new();

    /// <inheritdoc />
    /// <remarks>
    /// The Oracle-style clause: <c>OFFSET n ROWS</c>, <c>FETCH NEXT m ROWS ONLY</c>, or both in sequence.
    /// Confirming that a real DM8 instance in Oracle-compatible mode accepts this form is an acceptance item
    /// in <c>Sql/README.md</c>, like every mode-dependent behaviour.
    /// </remarks>
    public override string BuildPaging(int? count, int? offset)
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
}

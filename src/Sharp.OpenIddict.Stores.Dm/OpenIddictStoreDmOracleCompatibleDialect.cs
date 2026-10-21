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
/// In this mode DM8 converts the empty string to NULL, like Oracle does: the DM8 documentation of
/// <c>COMPATIBLE_MODE = 2</c> lists assigning an empty string to a string variable or parameter as one of
/// the forced conversions. The built-in store paths never compare against an empty-string parameter
/// (every optional filter is skipped when the caller passes null or an empty string, design 16.1); a
/// translated query callback can, and the <see cref="EmptyStringBindsAsNull"/> override tells the
/// translator to keep C# semantics there.
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
    /// Documented DM8 behaviour for <c>COMPATIBLE_MODE = 2</c>: an empty string assigned to a string variable
    /// or parameter is forcibly converted to NULL, so no stored value can equal <c>""</c>; the translator
    /// resolves <c>== ""</c> to an unsatisfiable predicate instead of an accidental null match.
    /// </remarks>
    public override bool EmptyStringBindsAsNull => true;

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

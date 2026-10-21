using System;
using System.Globalization;

namespace Sharp.OpenIddict.Stores.Dm;

/// <summary>
/// Dameng DM8 dialect for the <b>native</b> compatibility mode (<c>COMPATIBLE_MODE = 0</c>). Implemented
/// independently of the Oracle one, like every DM dialect, and named for the single mode it was written for.
/// </summary>
/// <remarks>
/// <para>
/// DM8 runs under an instance-level compatibility mode (<c>COMPATIBLE_MODE</c>): the same server behaves
/// differently for string comparison, empty strings, and paging depending on that setting. A dialect therefore
/// cannot be "the DM dialect"; it has to name the mode it was written for. The Oracle- and MySQL-compatible
/// modes are separate dialects with their own manifest entries and their own acceptance runs; this one targets
/// native mode only.
/// </para>
/// <para>
/// Behaviour that differs by mode is left unverified rather than guessed: exact string comparison and the
/// paging clause are marked in <c>Sql/README.md</c> as release blockers to confirm on a real server in the
/// declared mode. Guessing would be worse than an honest gap, because a silently case-insensitive comparison
/// corrupts lookups without any error.
/// </para>
/// </remarks>
public sealed class OpenIddictStoreDmDialect : OpenIddictStoreDmDialectBase
{
    /// <summary>
    /// The compatibility mode this dialect is written for. It is part of the dialect's identity, not a
    /// configurable switch: a different mode needs a different, separately accepted implementation.
    /// </summary>
    public const string CompatibilityMode = "native";

    /// <summary>Shared instance: the dialect is stateless.</summary>
    public static OpenIddictStoreDmDialect Instance { get; } = new();

    /// <inheritdoc />
    /// <remarks>
    /// <c>LIMIT</c>/<c>OFFSET</c> is used because DM8 documents it as one of its paging forms (the SELECT
    /// grammar accepts <c>LIMIT n</c>, <c>LIMIT n OFFSET m</c>, and the two reversed-argument forms) and it does
    /// not depend on a row-count pseudocolumn. Which forms a given instance accepts still depends on the
    /// compatibility mode, so this clause is one of the blockers listed in <c>Sql/README.md</c>. DM8 defines
    /// the row count as a plain integer with no documented maximum and no documented "all rows" idiom, so a
    /// count-less offset uses the largest signed 32-bit value as the project's sentinel for "effectively all
    /// remaining rows" — the largest value the documented grammar is guaranteed to accept on every mode.
    /// </remarks>
    public override string BuildPaging(int? count, int? offset)
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
}

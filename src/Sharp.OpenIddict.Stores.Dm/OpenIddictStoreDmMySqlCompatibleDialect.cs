using System;
using System.Globalization;

namespace Sharp.OpenIddict.Stores.Dm;

/// <summary>
/// Dameng DM8 dialect for the <b>MySQL-compatible</b> mode (<c>COMPATIBLE_MODE = 4</c>). A separate target
/// from native mode with its own manifest entry and its own acceptance run: the mode changes the empty-string
/// semantics and the paging forms the server accepts (design 5, 10.1).
/// </summary>
/// <remarks>
/// <para>
/// In this mode DM8 keeps the empty string distinct from NULL, like MySQL does. The stores need no workaround
/// for that either way: every optional filter is skipped when the caller passes null or an empty string, so no
/// predicate ever compares against an empty-string parameter.
/// </para>
/// <para>
/// MySQL-compatible deployments are frequently initialized with <c>CASE_SENSITIVE=0</c>, which folds case in
/// string comparison and breaks the exact-match contract. The mode does not change that setting by itself, so
/// the script and README declare <c>CASE_SENSITIVE=1</c> as a deployment prerequisite instead of silently
/// accepting a corrupting comparison (design 16.1.1).
/// </para>
/// </remarks>
public sealed class OpenIddictStoreDmMySqlCompatibleDialect : OpenIddictStoreDmDialectBase
{
    /// <summary>
    /// The compatibility mode this dialect is written for. It is part of the dialect's identity, not a
    /// configurable switch: a different mode needs a different, separately accepted implementation.
    /// </summary>
    public const string CompatibilityMode = "mysql-compatible";

    /// <summary>Shared instance: the dialect is stateless.</summary>
    public static OpenIddictStoreDmMySqlCompatibleDialect Instance { get; } = new();

    /// <inheritdoc />
    /// <remarks>
    /// The MySQL-style clause: <c>LIMIT m</c>, <c>OFFSET n</c>, or both in sequence. Confirming that a real
    /// DM8 instance in MySQL-compatible mode accepts this form is an acceptance item in <c>Sql/README.md</c>,
    /// like every mode-dependent behaviour.
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

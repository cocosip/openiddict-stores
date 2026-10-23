namespace Sharp.OpenIddict.Stores;

/// <summary>
/// A dialect whose exact-comparison guarantee depends on an instance-level server setting the runtime cannot
/// read, and which can therefore detect at startup that the setting is wrong (design 16.1.1, 17).
/// </summary>
/// <remarks>
/// <para>
/// Every other provider forces the comparison semantics into the predicate itself (a binary collation, the
/// <c>BINARY</c> operator, <c>NLSSORT</c>), so a misconfigured database default cannot change the result. DM8 is
/// the exception: whether strings compare case-sensitively is fixed when the database is created
/// (<c>CASE_SENSITIVE</c>) and is not exposed through any data-dictionary view, so the only way to learn it is
/// to compare two values. Without that probe an instance initialized with <c>CASE_SENSITIVE=0</c> silently
/// matches the wrong row in <c>FindByClientId</c> and friends — the one failure mode this library refuses to
/// allow.
/// </para>
/// <para>
/// The probe runs once per database, together with the schema version check, and its result is cached with that
/// check: it costs one round-trip per process, never one per request.
/// </para>
/// </remarks>
public interface IOpenIddictStoreStartupProbe
{
    /// <summary>
    /// Builds the probe query: a single-row, single-column <c>SELECT</c> that yields a non-zero value when the
    /// server would break the exact-comparison contract, and zero when the configuration is supported.
    /// </summary>
    /// <param name="versionsTable">
    /// The already quoted schema version table. It is the <c>FROM</c> source because the guard has just read a
    /// row from it: the table exists, is readable by this user, and holds exactly one row, so the probe needs
    /// no <c>DUAL</c>-style catalog object whose availability or permissions differ per mode.
    /// </param>
    string BuildProbeSql(string versionsTable);

    /// <summary>
    /// The message reported when the probe yields a non-zero value. It names the setting the operator has to
    /// fix and states that the store will not degrade to an inexact comparison.
    /// </summary>
    string ProbeFailureMessage { get; }
}

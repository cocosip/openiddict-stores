namespace Sharp.OpenIddict.Stores.Dm;

/// <summary>
/// The DM8 compatibility modes this provider implements. Each value is a separate, separately accepted
/// target: it selects the dialect installed at registration and the manifest entry whose script the
/// deployment renders (design 5, 10.1).
/// </summary>
/// <remarks>
/// DM8's instance-level <c>COMPATIBLE_MODE</c> (dm.ini) changes real behaviour — string comparison
/// prerequisites, empty-string storage, and paging syntax. A dialect therefore cannot be "the DM dialect";
/// every registration names the mode it was written for. The modes DM8 documents but this enum does not list
/// (SQL92, TDS, DM6, Teradata) are undeclared targets: claiming one needs its own entry, script, and
/// acceptance run.
/// </remarks>
public enum OpenIddictStoreDmCompatibilityMode
{
    /// <summary>
    /// Native mode (<c>COMPATIBLE_MODE = 0</c>): DM8's own SQL dialect, paging with <c>LIMIT</c>/<c>OFFSET</c>.
    /// The default mode of <c>UseDm</c>.
    /// </summary>
    Native,

    /// <summary>
    /// Oracle-compatible mode (<c>COMPATIBLE_MODE = 2</c>): the empty string is stored as NULL and paging uses
    /// the Oracle-style <c>OFFSET ... ROWS / FETCH NEXT ... ROWS ONLY</c> clause.
    /// </summary>
    OracleCompatible,

    /// <summary>
    /// MySQL-compatible mode (<c>COMPATIBLE_MODE = 4</c>): the empty string is distinct from NULL and paging
    /// uses the MySQL-style <c>LIMIT</c>/<c>OFFSET</c> clause.
    /// </summary>
    MySqlCompatible
}

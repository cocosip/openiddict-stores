namespace Sharp.OpenIddict.Stores;

/// <summary>
/// The database providers supported by this extension. The first version is fixed at six; custom providers are not exposed.
/// </summary>
public enum OpenIddictStoreProvider
{
    /// <summary>SQL Server.</summary>
    SqlServer,

    /// <summary>PostgreSQL.</summary>
    PostgreSql,

    /// <summary>MySQL and MariaDB.</summary>
    MySql,

    /// <summary>SQLite.</summary>
    Sqlite,

    /// <summary>Oracle.</summary>
    Oracle,

    /// <summary>Dameng DM8.</summary>
    Dm,
}

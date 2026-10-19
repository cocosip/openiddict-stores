using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Sharp.OpenIddict.Stores;

namespace Sharp.OpenIddict.Stores.IntegrationTests;

/// <summary>
/// Opens SQLite connections for the in-process test databases. Shared so every fixture uses the same
/// factory shape as the real one instead of a private copy per test class.
/// </summary>
internal sealed class SqliteConnectionFactory : IOpenIddictStoreConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string connectionString) => _connectionString = connectionString;

    public ValueTask<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken)
        => new(new SqliteConnection(_connectionString));
}

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sharp.OpenIddict.Stores.Models;
using Sharp.OpenIddict.Stores.SqlServer;
using Sharp.OpenIddict.Stores.Stores;
using Xunit;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// SQL Server acceptance on a real server. Unlike the other server suites this one gives itself a whole
/// database and drops it afterwards, because the configured connection string is a master-database connection
/// whose account may create databases; isolation is then a database boundary rather than a table prefix, and
/// nothing the run creates can be seen by another deployment at all.
/// </summary>
/// <remarks>
/// Run on a net8.0 or net9.0 host: <c>Microsoft.Data.SqlClient</c> 7.1.1 ships no net10 asset, so a net10 run
/// would exercise the net9 asset and prove nothing about the declared target (design 5.1). The
/// <see cref="Net8HostAcceptanceFactAttribute"/> refuses a net10 host instead of reporting a green result.
/// </remarks>
public sealed class SqlServerStoreContractTests : ServerAcceptanceBase
{
    private string _databaseName = string.Empty;
    private string _masterConnectionString = string.Empty;

    protected override string Key => AcceptanceServers.SqlServer;

    protected override OpenIddictStoreProvider Provider => OpenIddictStoreProvider.SqlServer;

    protected override IOpenIddictStoreDialect Dialect => OpenIddictStoreSqlServerDialect.Instance;

    /// <summary>The configured value is the master connection; the run creates and enters its own database.</summary>
    protected override async Task PrepareTargetAsync()
    {
        _masterConnectionString = ConnectionString;

        // The same generated-name rule the table prefixes use: a run-unique identifier that the cleanup guard
        // can recognise as this run's own.
        _databaseName = TestTablePrefix.Create();

        await using (var master = new SqlConnection(_masterConnectionString))
        {
            await master.OpenAsync();

            await using var create = master.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{_databaseName}]";
            await create.ExecuteNonQueryAsync();
        }

        ConnectionString = new SqlConnectionStringBuilder(_masterConnectionString)
        {
            InitialCatalog = _databaseName,
        }.ConnectionString;
    }

    protected override async Task ReleaseAsync()
    {
        if (_databaseName.Length == 0 || _masterConnectionString.Length == 0)
        {
            return;
        }

        // Refuse to drop anything this fixture did not create. The name is generated above with a fixed prefix
        // and a fresh GUID fragment, so a mismatch means this run's state is wrong - and "something is wrong"
        // must never turn into dropping a database that belongs to someone else.
        if (!TestTablePrefix.IsOwnedByThisTestRun(_databaseName))
        {
            return;
        }

        // Pools hold connections open, so the drop would block on its own leaked session otherwise.
        SqlConnection.ClearAllPools();

        await using var master = new SqlConnection(_masterConnectionString);
        await master.OpenAsync();

        await using var drop = master.CreateCommand();
        drop.CommandText = $@"
            IF DB_ID(N'{_databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{_databaseName}];
            END";
        await drop.ExecuteNonQueryAsync();
    }

    protected override DbConnection CreateConnection() => new SqlConnection(ConnectionString);

    protected override string RenderInstallScript(string prefix) => DeploymentScript.SqlServer(prefix);

    protected override Task<List<string>> ListOwnedTableNamesAsync(string prefix)
        => QueryScalarsAsync<string>(
            "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') AND name LIKE @pattern",
            ("pattern", prefix + "%"));

    protected override Task DropOwnedTableAsync(string tableName)
        => ExecuteAsync($"DROP TABLE {Dialect.QuoteIdentifier(tableName)}");

    protected override async Task CheckServerPrerequisitesAsync()
    {
        // The manifest declares SQL Server 2016 (13.x)+; SERVERPROPERTY reports the major version as a number.
        Assert.True(Assert.Single(await QueryScalarsAsync<int>(
            "SELECT CONVERT(int, SERVERPROPERTY('ProductMajorVersion'))")) >= 13,
            "SQL Server 2016 (13.x) is the declared floor.");
    }

    [Net8HostAcceptanceFact(AcceptanceServers.SqlServer, "The SQL Server acceptance suite")]
    public Task Server_PrerequisitesMeetTheManifest() => CheckServerPrerequisitesAsync();

    [Net8HostAcceptanceFact(AcceptanceServers.SqlServer, "The SQL Server acceptance suite")]
    public Task InstallScript_CreatesTheNineTablesAndTheVersionRow()
        => Install_CreatesTheNineTablesAndTheVersionRowCore();

    [Net8HostAcceptanceFact(AcceptanceServers.SqlServer, "The SQL Server acceptance suite")]
    public Task ReRunningTheInstallScript_IsRefusedInsteadOfSkipped()
        => ReRunningTheInstallScript_IsRefusedInsteadOfSkippedCore();

    [Net8HostAcceptanceFact(AcceptanceServers.SqlServer, "The SQL Server acceptance suite")]
    public Task Application_RoundTripsUnicodeLargeValuesAndCollections()
        => Application_RoundTripsUnicodeLargeValuesAndCollectionsCore();

    [Net8HostAcceptanceFact(AcceptanceServers.SqlServer, "The SQL Server acceptance suite")]
    public Task Dates_KeepMicrosecondPrecisionAcrossCreateAndRead()
        => Dates_KeepMicrosecondPrecisionAcrossCreateAndReadCore();

    [Net8HostAcceptanceFact(AcceptanceServers.SqlServer, "The SQL Server acceptance suite")]
    public Task ExactComparison_DistinguishesCaseAndAccents()
        => ExactComparison_DistinguishesCaseAndAccentsCore();

    [Net8HostAcceptanceFact(AcceptanceServers.SqlServer, "The SQL Server acceptance suite")]
    public Task ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNone()
        => ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNoneCore();

    [Net8HostAcceptanceFact(AcceptanceServers.SqlServer, "The SQL Server acceptance suite")]
    public Task UniqueIndex_RejectsASecondRowWithTheSameClientId()
        => UniqueIndex_RejectsASecondRowWithTheSameClientIdCore();

    [Net8HostAcceptanceFact(AcceptanceServers.SqlServer, "The SQL Server acceptance suite")]
    public Task Update_RejectsAStaleConcurrencyToken()
        => Update_RejectsAStaleConcurrencyTokenCore();

    [Net8HostAcceptanceFact(AcceptanceServers.SqlServer, "The SQL Server acceptance suite")]
    public Task Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACount()
        => Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACountCore();

    [Net8HostAcceptanceFact(AcceptanceServers.SqlServer, "The SQL Server acceptance suite")]
    public Task CountQuery_CountsTheRows() => CountQuery_CountsTheRowsCore();

    [Net8HostAcceptanceFact(AcceptanceServers.SqlServer, "The SQL Server acceptance suite")]
    public Task ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritative()
        => ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritativeCore();

    [Net8HostAcceptanceFact(AcceptanceServers.SqlServer, "The SQL Server acceptance suite")]
    public Task AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorization()
        => AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorizationCore();

    [Net8HostAcceptanceFact(AcceptanceServers.SqlServer, "The SQL Server acceptance suite")]
    public Task BulkRevocation_TreatsANullStatusAsNotYetRevoked()
        => BulkRevocation_TreatsANullStatusAsNotYetRevokedCore();

    [Net8HostAcceptanceFact(AcceptanceServers.SqlServer, "The SQL Server acceptance suite")]
    public Task Prune_RemovesOnlyRowsBeforeTheThreshold()
        => Prune_RemovesOnlyRowsBeforeTheThresholdCore();

    /// <summary>
    /// A copy rendered with a custom prefix and schema must produce a complete schema under that name and be
    /// usable through a store configured the same way. This fact is SQL Server's own, not part of the shared
    /// contract: a schema is a first-class object here, and the template's "already installed" guard has to be
    /// schema-qualified or a second copy would create half the tables and only then fail.
    /// </summary>
    [Net8HostAcceptanceFact(AcceptanceServers.SqlServer, "The SQL Server acceptance suite")]
    public async Task CustomPrefixAndSchema_InstallAndAreUsable()
    {
        const string prefix = "Oidc";
        const string schema = "auth";

        // The fixture already installed dbo.<run prefix>*, so this copy goes to a different schema with a
        // different prefix in the same database: a collision would prove the rendering leaked somewhere.
        await ExecuteAsync(
            $"IF SCHEMA_ID(N'{schema}') IS NULL EXEC(N'CREATE SCHEMA {Dialect.QuoteIdentifier(schema)}');");
        await ExecuteScriptAsync(DeploymentScript.SqlServer(prefix, schema));

        Assert.Equal(9, Assert.Single(await QueryScalarsAsync<int>(
            $"SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'{schema}') AND name LIKE N'{prefix}%'")));

        // Nothing may appear under the default prefix or in dbo because of this second copy.
        Assert.Equal(0, Assert.Single(await QueryScalarsAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE name LIKE N'OpenIddict%' AND schema_id <> SCHEMA_ID(N'dbo')")));

        Assert.Equal([1], await QueryScalarsAsync<int>(
            $"SELECT Version FROM auth.{prefix}SchemaVersions WHERE Series = '1'"));

        var options = Options.Create(new OpenIddictStoreOptions { TablePrefix = prefix, Schema = schema });
        options.Value.Providers.Add(Provider);

        var store = new OpenIddictStoreApplicationStore<OpenIddictStoreApplication>(
            options,
            new SqlServerConnectionFactory(ConnectionString),
            Dialect,
            NullLogger<OpenIddictStoreApplicationStore<OpenIddictStoreApplication>>.Instance);

        var application = new OpenIddictStoreApplication { ClientId = "custom-prefix" };
        await store.CreateAsync(application, default);

        Assert.NotNull(await store.FindByClientIdAsync("custom-prefix", default));

        var again = await Record.ExceptionAsync(() => ExecuteScriptAsync(DeploymentScript.SqlServer(prefix, schema)));

        Assert.NotNull(again);
        Assert.Contains("already installed", again!.Message, StringComparison.Ordinal);
    }

    private sealed class SqlServerConnectionFactory : IOpenIddictStoreConnectionFactory
    {
        private readonly string _connectionString;

        public SqlServerConnectionFactory(string connectionString) => _connectionString = connectionString;

        public ValueTask<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken)
            => new(new SqlConnection(_connectionString));
    }
}

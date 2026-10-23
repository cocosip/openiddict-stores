using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Sqlite;
using Xunit;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// SQLite acceptance in the same harness as the server-backed suites, so "which databases does the store work
/// against" is answered by one run instead of two projects. It needs no configuration: the database is a
/// temporary file this run creates and deletes, which is also why it is the only suite that passes on a
/// machine with no server installed.
/// </summary>
/// <remarks>
/// A file, not <c>:memory:</c>: an in-memory database lives and dies with its connection, so a suite that
/// opens a connection per operation would silently test something the deployed file database never does
/// (design 5, DB-01). SQLite has no schemas, so <see cref="OpenIddictStoreOptions.Schema"/> stays null — the
/// options validator rejects one for this provider (design 14.2).
/// </remarks>
public sealed class SqliteStoreContractTests : ServerAcceptanceBase
{
    /// <summary>The stem of every database file this harness creates; the cleanup path keys off it.</summary>
    private const string FileNamePrefix = "oidc-acceptance-";

    private string _databasePath = string.Empty;

    protected override string Key => AcceptanceServers.Sqlite;

    protected override OpenIddictStoreProvider Provider => OpenIddictStoreProvider.Sqlite;

    protected override IOpenIddictStoreDialect Dialect => OpenIddictStoreSqliteDialect.Instance;

    /// <summary>
    /// SQLite's "connection string" is a file this run creates, so it is not read from the configuration: the
    /// file has to be unique per fixture and owned by the run that will delete it.
    /// </summary>
    protected override string ResolveConnectionString()
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(), FileNamePrefix + Guid.NewGuid().ToString("N") + ".db");

        return new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ConnectionString;
    }

    /// <summary>
    /// Deletes the database file this fixture created, and only that: the name it generated is checked before
    /// the delete, so a path from anywhere else can never be removed by the cleanup path.
    /// </summary>
    protected override Task ReleaseAsync()
    {
        if (_databasePath.Length > 0
            && Path.GetFileName(_databasePath).StartsWith(FileNamePrefix, StringComparison.Ordinal)
            && File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    protected override DbConnection CreateConnection() => new SqliteConnection(ConnectionString);

    protected override string RenderInstallScript(string prefix) => DeploymentScript.Sqlite(prefix);

    protected override Task<List<string>> ListOwnedTableNamesAsync(string prefix)
        => QueryScalarsAsync<string>(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name LIKE @pattern",
            ("pattern", prefix + "%"));

    protected override Task DropOwnedTableAsync(string tableName)
        => ExecuteAsync($"DROP TABLE {Dialect.QuoteIdentifier(tableName)}");

    protected override async Task CheckServerPrerequisitesAsync()
    {
        // The manifest declares SQLite 3.35+; the driver bundles its own engine, so this reports the build the
        // run actually used rather than what the machine happens to have installed.
        var reported = Assert.Single(await QueryScalarsAsync<string>("SELECT sqlite_version()"));

        Assert.True(Version.Parse(reported) >= new Version(3, 35),
            $"SQLite 3.35+ is the declared floor; the engine reports {reported}.");
    }

    [AcceptanceFact(AcceptanceServers.Sqlite, "The SQLite acceptance suite")]
    public Task Server_PrerequisitesMeetTheManifest() => CheckServerPrerequisitesAsync();

    [AcceptanceFact(AcceptanceServers.Sqlite, "The SQLite acceptance suite")]
    public Task InstallScript_CreatesTheNineTablesAndTheVersionRow()
        => Install_CreatesTheNineTablesAndTheVersionRowCore();

    [AcceptanceFact(AcceptanceServers.Sqlite, "The SQLite acceptance suite")]
    public Task ReRunningTheInstallScript_IsRefusedInsteadOfSkipped()
        => ReRunningTheInstallScript_IsRefusedInsteadOfSkippedCore();

    [AcceptanceFact(AcceptanceServers.Sqlite, "The SQLite acceptance suite")]
    public Task Application_RoundTripsUnicodeLargeValuesAndCollections()
        => Application_RoundTripsUnicodeLargeValuesAndCollectionsCore();

    [AcceptanceFact(AcceptanceServers.Sqlite, "The SQLite acceptance suite")]
    public Task Dates_KeepMicrosecondPrecisionAcrossCreateAndRead()
        => Dates_KeepMicrosecondPrecisionAcrossCreateAndReadCore();

    [AcceptanceFact(AcceptanceServers.Sqlite, "The SQLite acceptance suite")]
    public Task ExactComparison_DistinguishesCaseAndAccents()
        => ExactComparison_DistinguishesCaseAndAccentsCore();

    [AcceptanceFact(AcceptanceServers.Sqlite, "The SQLite acceptance suite")]
    public Task ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNone()
        => ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNoneCore();

    [AcceptanceFact(AcceptanceServers.Sqlite, "The SQLite acceptance suite")]
    public Task UniqueIndex_RejectsASecondRowWithTheSameClientId()
        => UniqueIndex_RejectsASecondRowWithTheSameClientIdCore();

    [AcceptanceFact(AcceptanceServers.Sqlite, "The SQLite acceptance suite")]
    public Task Update_RejectsAStaleConcurrencyToken()
        => Update_RejectsAStaleConcurrencyTokenCore();

    [AcceptanceFact(AcceptanceServers.Sqlite, "The SQLite acceptance suite")]
    public Task Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACount()
        => Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACountCore();

    [AcceptanceFact(AcceptanceServers.Sqlite, "The SQLite acceptance suite")]
    public Task CountQuery_CountsTheRows() => CountQuery_CountsTheRowsCore();

    [AcceptanceFact(AcceptanceServers.Sqlite, "The SQLite acceptance suite")]
    public Task ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritative()
        => ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritativeCore();

    [AcceptanceFact(AcceptanceServers.Sqlite, "The SQLite acceptance suite")]
    public Task AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorization()
        => AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorizationCore();

    [AcceptanceFact(AcceptanceServers.Sqlite, "The SQLite acceptance suite")]
    public Task BulkRevocation_TreatsANullStatusAsNotYetRevoked()
        => BulkRevocation_TreatsANullStatusAsNotYetRevokedCore();

    [AcceptanceFact(AcceptanceServers.Sqlite, "The SQLite acceptance suite")]
    public Task Prune_RemovesOnlyRowsBeforeTheThreshold()
        => Prune_RemovesOnlyRowsBeforeTheThresholdCore();
}

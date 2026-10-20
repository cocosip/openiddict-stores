using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Threading.Tasks;
using Npgsql;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.PostgreSql;
using Xunit;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// PostgreSQL acceptance on a real server. The dialect claims that cannot be proven on SQLite live here:
/// <c>COLLATE "C"</c> exact comparison including case, accents, and trailing spaces, <c>timestamp(6)</c>
/// microsecond round-trips, <c>LIMIT</c>/<c>OFFSET</c> paging, bytea digest columns with candidate indexes,
/// and associated deletes. The suite installs into the connection's default schema under a run-unique table
/// prefix and drops exactly the nine tables it created, so a real deployment in the same schema is safe.
/// </summary>
public sealed class PostgreSqlStoreContractTests : ServerAcceptanceBase
{
    protected override string Key => AcceptanceServers.PostgreSql;

    protected override OpenIddictStoreProvider Provider => OpenIddictStoreProvider.PostgreSql;

    protected override IOpenIddictStoreDialect Dialect => OpenIddictStorePostgreSqlDialect.Instance;

    protected override DbConnection CreateConnection() => new NpgsqlConnection(ConnectionString);

    protected override string RenderInstallScript(string prefix) => DeploymentScript.PostgreSql(prefix);

    protected override Task<List<string>> ListOwnedTableNamesAsync(string prefix)
        => QueryScalarsAsync<string>(
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = current_schema() AND table_name LIKE @pattern",
            ("pattern", prefix + "%"));

    protected override Task DropOwnedTableAsync(string tableName)
        => ExecuteAsync($"DROP TABLE {Dialect.QuoteIdentifier(tableName)}");

    protected override async Task CheckServerPrerequisitesAsync()
    {
        // The manifest declares PostgreSQL 14+; server_version_num is the version as a plain integer.
        var reported = Assert.Single(await QueryScalarsAsync<string>(
            "SELECT current_setting('server_version_num')"));

        Assert.True(int.Parse(reported, CultureInfo.InvariantCulture) >= 140_000,
            $"PostgreSQL 14+ is the declared floor; the server reports version number {reported}.");
    }

    [AcceptanceFact(AcceptanceServers.PostgreSql, "The PostgreSQL acceptance suite")]
    public Task Server_PrerequisitesMeetTheManifest() => CheckServerPrerequisitesAsync();

    [AcceptanceFact(AcceptanceServers.PostgreSql, "The PostgreSQL acceptance suite")]
    public Task InstallScript_CreatesTheNineTablesAndTheVersionRow()
        => Install_CreatesTheNineTablesAndTheVersionRowCore();

    [AcceptanceFact(AcceptanceServers.PostgreSql, "The PostgreSQL acceptance suite")]
    public Task ReRunningTheInstallScript_IsRefusedInsteadOfSkipped()
        => ReRunningTheInstallScript_IsRefusedInsteadOfSkippedCore();

    [AcceptanceFact(AcceptanceServers.PostgreSql, "The PostgreSQL acceptance suite")]
    public Task Application_RoundTripsUnicodeLargeValuesAndCollections()
        => Application_RoundTripsUnicodeLargeValuesAndCollectionsCore();

    [AcceptanceFact(AcceptanceServers.PostgreSql, "The PostgreSQL acceptance suite")]
    public Task Dates_KeepMicrosecondPrecisionAcrossCreateAndRead()
        => Dates_KeepMicrosecondPrecisionAcrossCreateAndReadCore();

    [AcceptanceFact(AcceptanceServers.PostgreSql, "The PostgreSQL acceptance suite")]
    public Task ExactComparison_DistinguishesCaseAndAccents()
        => ExactComparison_DistinguishesCaseAndAccentsCore();

    [AcceptanceFact(AcceptanceServers.PostgreSql, "The PostgreSQL acceptance suite")]
    public Task ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNone()
        => ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNoneCore();

    [AcceptanceFact(AcceptanceServers.PostgreSql, "The PostgreSQL acceptance suite")]
    public Task UniqueIndex_RejectsASecondRowWithTheSameClientId()
        => UniqueIndex_RejectsASecondRowWithTheSameClientIdCore();

    [AcceptanceFact(AcceptanceServers.PostgreSql, "The PostgreSQL acceptance suite")]
    public Task Update_RejectsAStaleConcurrencyToken()
        => Update_RejectsAStaleConcurrencyTokenCore();

    [AcceptanceFact(AcceptanceServers.PostgreSql, "The PostgreSQL acceptance suite")]
    public Task Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACount()
        => Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACountCore();

    [AcceptanceFact(AcceptanceServers.PostgreSql, "The PostgreSQL acceptance suite")]
    public Task CountQuery_CountsTheRows() => CountQuery_CountsTheRowsCore();

    [AcceptanceFact(AcceptanceServers.PostgreSql, "The PostgreSQL acceptance suite")]
    public Task ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritative()
        => ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritativeCore();

    [AcceptanceFact(AcceptanceServers.PostgreSql, "The PostgreSQL acceptance suite")]
    public Task AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorization()
        => AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorizationCore();

    [AcceptanceFact(AcceptanceServers.PostgreSql, "The PostgreSQL acceptance suite")]
    public Task BulkRevocation_TreatsANullStatusAsNotYetRevoked()
        => BulkRevocation_TreatsANullStatusAsNotYetRevokedCore();

    [AcceptanceFact(AcceptanceServers.PostgreSql, "The PostgreSQL acceptance suite")]
    public Task Prune_RemovesOnlyRowsBeforeTheThreshold()
        => Prune_RemovesOnlyRowsBeforeTheThresholdCore();
}

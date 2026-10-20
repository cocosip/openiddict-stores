using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Oracle;
using Xunit;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// Oracle acceptance on a real server. The dialect claims that cannot be proven on SQLite live here:
/// quoted mixed-case identifiers surviving the uppercase fold, <c>NLSSORT(BINARY)</c> exact comparison
/// including case, accents, and trailing spaces, <c>TIMESTAMP</c> microsecond round-trips with the UTC kind
/// restored on read, <c>OFFSET/FETCH</c> paging, and associated deletes. The suite installs into the
/// connecting user's own schema under a run-unique table prefix and drops exactly the nine tables it
/// created, so any pre-existing objects in the schema are safe.
/// </summary>
public sealed class OracleStoreContractTests : ServerAcceptanceBase
{
    protected override string Key => AcceptanceServers.Oracle;

    protected override OpenIddictStoreProvider Provider => OpenIddictStoreProvider.Oracle;

    protected override IOpenIddictStoreDialect Dialect => OpenIddictStoreOracleDialect.Instance;

    protected override DbConnection CreateConnection() => new OracleConnection(ConnectionString);

    /// <summary>Binds strings as the national character set, matching the NVARCHAR2 schema columns.</summary>
    protected override void ConfigureParameter(System.Data.Common.DbParameter parameter, string name, object value)
    {
        if (value is string && parameter is OracleParameter oracleParameter)
        {
            oracleParameter.OracleDbType = OracleDbType.NVarchar2;
        }
    }

    /// <summary>The SQL written here names its parameters, so the driver must bind by name, not position.</summary>
    protected override void ConfigureCommand(System.Data.Common.DbCommand command)
    {
        if (command is OracleCommand oracleCommand)
        {
            oracleCommand.BindByName = true;
        }
    }

    protected override string RenderInstallScript(string prefix) => DeploymentScript.Oracle(prefix);

    protected override Task<List<string>> ListOwnedTableNamesAsync(string prefix)
        => QueryScalarsAsync<string>(
            "SELECT table_name FROM user_tables WHERE table_name LIKE :pattern",
            ("pattern", prefix + "%"));

    protected override Task DropOwnedTableAsync(string tableName)
        => ExecuteAsync($"DROP TABLE {Dialect.QuoteIdentifier(tableName)}");

    protected override async Task CheckServerPrerequisitesAsync()
    {
        // The manifest declares Oracle 19c+. MIN picks one banner deterministically should the view carry
        // more than one Oracle Database row; the parse reads the release number right after the product name.
        var banner = Assert.Single(await QueryScalarsAsync<string>(
            "SELECT MIN(banner) FROM v$version WHERE banner LIKE 'Oracle Database%'"));

        var match = Regex.Match(banner, @"Oracle Database (\d+)");

        Assert.True(match.Success && int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) >= 19,
            $"Oracle 19c+ is the declared floor; the server reports: {banner}");
    }

    [AcceptanceFact(AcceptanceServers.Oracle, "The Oracle acceptance suite")]
    public Task Server_PrerequisitesMeetTheManifest() => CheckServerPrerequisitesAsync();

    [AcceptanceFact(AcceptanceServers.Oracle, "The Oracle acceptance suite")]
    public Task InstallScript_CreatesTheNineTablesAndTheVersionRow()
        => Install_CreatesTheNineTablesAndTheVersionRowCore();

    [AcceptanceFact(AcceptanceServers.Oracle, "The Oracle acceptance suite")]
    public Task ReRunningTheInstallScript_IsRefusedInsteadOfSkipped()
        => ReRunningTheInstallScript_IsRefusedInsteadOfSkippedCore();

    [AcceptanceFact(AcceptanceServers.Oracle, "The Oracle acceptance suite")]
    public Task Application_RoundTripsUnicodeLargeValuesAndCollections()
        => Application_RoundTripsUnicodeLargeValuesAndCollectionsCore();

    [AcceptanceFact(AcceptanceServers.Oracle, "The Oracle acceptance suite")]
    public Task Dates_KeepMicrosecondPrecisionAcrossCreateAndRead()
        => Dates_KeepMicrosecondPrecisionAcrossCreateAndReadCore();

    [AcceptanceFact(AcceptanceServers.Oracle, "The Oracle acceptance suite")]
    public Task ExactComparison_DistinguishesCaseAndAccents()
        => ExactComparison_DistinguishesCaseAndAccentsCore();

    [AcceptanceFact(AcceptanceServers.Oracle, "The Oracle acceptance suite")]
    public Task ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNone()
        => ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNoneCore();

    [AcceptanceFact(AcceptanceServers.Oracle, "The Oracle acceptance suite")]
    public Task UniqueIndex_RejectsASecondRowWithTheSameClientId()
        => UniqueIndex_RejectsASecondRowWithTheSameClientIdCore();

    [AcceptanceFact(AcceptanceServers.Oracle, "The Oracle acceptance suite")]
    public Task Update_RejectsAStaleConcurrencyToken()
        => Update_RejectsAStaleConcurrencyTokenCore();

    [AcceptanceFact(AcceptanceServers.Oracle, "The Oracle acceptance suite")]
    public Task Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACount()
        => Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACountCore();

    [AcceptanceFact(AcceptanceServers.Oracle, "The Oracle acceptance suite")]
    public Task CountQuery_CountsTheRows() => CountQuery_CountsTheRowsCore();

    [AcceptanceFact(AcceptanceServers.Oracle, "The Oracle acceptance suite")]
    public Task ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritative()
        => ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritativeCore();

    [AcceptanceFact(AcceptanceServers.Oracle, "The Oracle acceptance suite")]
    public Task AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorization()
        => AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorizationCore();

    [AcceptanceFact(AcceptanceServers.Oracle, "The Oracle acceptance suite")]
    public Task BulkRevocation_TreatsANullStatusAsNotYetRevoked()
        => BulkRevocation_TreatsANullStatusAsNotYetRevokedCore();

    [AcceptanceFact(AcceptanceServers.Oracle, "The Oracle acceptance suite")]
    public Task Prune_RemovesOnlyRowsBeforeTheThreshold()
        => Prune_RemovesOnlyRowsBeforeTheThresholdCore();
}

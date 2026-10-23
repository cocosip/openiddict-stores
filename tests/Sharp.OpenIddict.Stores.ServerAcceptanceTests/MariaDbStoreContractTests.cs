using System.Threading.Tasks;
using Sharp.OpenIddict.Stores;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// MariaDB 10.6 acceptance on a real server. MariaDB shares the physical template with MySQL but is a separate
/// manifest entry, so it needs its own run: "the same script worked on MySQL" is not evidence that MariaDB
/// accepts it, and the two engines differ in DDL details such as the <c>ROW_FORMAT</c> and index behaviour the
/// template depends on (design 5, 10.1).
/// </summary>
/// <remarks>
/// Everything the MySQL suite proves is proven here too, against a different engine: <c>BINARY</c> exact
/// comparison including the trailing-space case, <c>datetime(6)</c> microseconds, the <c>LIMIT</c>/<c>OFFSET</c>
/// clause including the <c>18446744073709551615</c> sentinel on an offset without a count, the
/// <c>binary(32)</c> digest columns, and the PAD SPACE uniqueness limit. Running one suite never stands in for
/// the other.
/// </remarks>
public sealed class MariaDbStoreContractTests : MySqlStoreContractTestsBase
{
    protected override string Key => AcceptanceServers.MariaDb;

    protected override string ServerEntry => "MariaDB";

    protected override string MinimumServerVersion => "10.6";

    protected override bool IsMariaDb => true;

    [AcceptanceFact(AcceptanceServers.MariaDb, "The MariaDB acceptance suite")]
    public Task Server_PrerequisitesMeetTheManifest() => CheckServerPrerequisitesAsync();

    [AcceptanceFact(AcceptanceServers.MariaDb, "The MariaDB acceptance suite")]
    public Task InstallScript_CreatesTheNineTablesAndTheVersionRow()
        => Install_CreatesTheNineTablesAndTheVersionRowCore();

    [AcceptanceFact(AcceptanceServers.MariaDb, "The MariaDB acceptance suite")]
    public Task ReRunningTheInstallScript_IsRefusedInsteadOfSkipped()
        => ReRunningTheInstallScript_IsRefusedInsteadOfSkippedCore();

    [AcceptanceFact(AcceptanceServers.MariaDb, "The MariaDB acceptance suite")]
    public Task Application_RoundTripsUnicodeLargeValuesAndCollections()
        => Application_RoundTripsUnicodeLargeValuesAndCollectionsCore();

    [AcceptanceFact(AcceptanceServers.MariaDb, "The MariaDB acceptance suite")]
    public Task Dates_KeepMicrosecondPrecisionAcrossCreateAndRead()
        => Dates_KeepMicrosecondPrecisionAcrossCreateAndReadCore();

    [AcceptanceFact(AcceptanceServers.MariaDb, "The MariaDB acceptance suite")]
    public Task ExactComparison_DistinguishesCaseAndAccents()
        => ExactComparison_DistinguishesCaseAndAccentsCore();

    [AcceptanceFact(AcceptanceServers.MariaDb, "The MariaDB acceptance suite")]
    public Task ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNone()
        => ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNoneCore();

    [AcceptanceFact(AcceptanceServers.MariaDb, "The MariaDB acceptance suite")]
    public Task UniqueIndex_RejectsASecondRowWithTheSameClientId()
        => UniqueIndex_RejectsASecondRowWithTheSameClientIdCore();

    [AcceptanceFact(AcceptanceServers.MariaDb, "The MariaDB acceptance suite")]
    public Task Update_RejectsAStaleConcurrencyToken()
        => Update_RejectsAStaleConcurrencyTokenCore();

    [AcceptanceFact(AcceptanceServers.MariaDb, "The MariaDB acceptance suite")]
    public Task Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACount()
        => Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACountCore();

    [AcceptanceFact(AcceptanceServers.MariaDb, "The MariaDB acceptance suite")]
    public Task CountQuery_CountsTheRows() => CountQuery_CountsTheRowsCore();

    [AcceptanceFact(AcceptanceServers.MariaDb, "The MariaDB acceptance suite")]
    public Task ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritative()
        => ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritativeCore();

    [AcceptanceFact(AcceptanceServers.MariaDb, "The MariaDB acceptance suite")]
    public Task AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorization()
        => AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorizationCore();

    [AcceptanceFact(AcceptanceServers.MariaDb, "The MariaDB acceptance suite")]
    public Task BulkRevocation_TreatsANullStatusAsNotYetRevoked()
        => BulkRevocation_TreatsANullStatusAsNotYetRevokedCore();

    [AcceptanceFact(AcceptanceServers.MariaDb, "The MariaDB acceptance suite")]
    public Task Prune_RemovesOnlyRowsBeforeTheThreshold()
        => Prune_RemovesOnlyRowsBeforeTheThresholdCore();
}

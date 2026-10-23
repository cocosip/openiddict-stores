using System.Threading.Tasks;
using Sharp.OpenIddict.Stores;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// MySQL 8.0 acceptance on a real server. This is the run the MySQL <c>Sql/README.md</c> has been waiting for:
/// it says outright that real-database installation has not been executed in this repository, so every
/// server-only claim below is unverified until this suite passes.
/// </summary>
/// <remarks>
/// The claims it covers are the ones SQLite cannot prove for this provider: <c>BINARY</c> exact comparison
/// including case, accents, and trailing spaces; <c>utf8mb4_bin</c> column behaviour; <c>datetime(6)</c>
/// microsecond round-trips with the UTC kind restored; the <c>LIMIT</c>/<c>OFFSET</c> clause including the
/// <c>18446744073709551615</c> "no limit" sentinel an offset without a count needs; the <c>binary(32)</c> digest
/// columns; and the trailing-space uniqueness limit that PAD SPACE collations impose on the physical index.
/// The suite installs under a run-unique prefix and drops exactly the nine tables it created.
/// </remarks>
public sealed class MySqlStoreContractTests : MySqlStoreContractTestsBase
{
    protected override string Key => AcceptanceServers.MySql;

    protected override string ServerEntry => "MySQL";

    protected override string MinimumServerVersion => "8.0";

    protected override bool IsMariaDb => false;

    [AcceptanceFact(AcceptanceServers.MySql, "The MySQL acceptance suite")]
    public Task Server_PrerequisitesMeetTheManifest() => CheckServerPrerequisitesAsync();

    [AcceptanceFact(AcceptanceServers.MySql, "The MySQL acceptance suite")]
    public Task InstallScript_CreatesTheNineTablesAndTheVersionRow()
        => Install_CreatesTheNineTablesAndTheVersionRowCore();

    [AcceptanceFact(AcceptanceServers.MySql, "The MySQL acceptance suite")]
    public Task ReRunningTheInstallScript_IsRefusedInsteadOfSkipped()
        => ReRunningTheInstallScript_IsRefusedInsteadOfSkippedCore();

    [AcceptanceFact(AcceptanceServers.MySql, "The MySQL acceptance suite")]
    public Task Application_RoundTripsUnicodeLargeValuesAndCollections()
        => Application_RoundTripsUnicodeLargeValuesAndCollectionsCore();

    [AcceptanceFact(AcceptanceServers.MySql, "The MySQL acceptance suite")]
    public Task Dates_KeepMicrosecondPrecisionAcrossCreateAndRead()
        => Dates_KeepMicrosecondPrecisionAcrossCreateAndReadCore();

    [AcceptanceFact(AcceptanceServers.MySql, "The MySQL acceptance suite")]
    public Task ExactComparison_DistinguishesCaseAndAccents()
        => ExactComparison_DistinguishesCaseAndAccentsCore();

    [AcceptanceFact(AcceptanceServers.MySql, "The MySQL acceptance suite")]
    public Task ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNone()
        => ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNoneCore();

    [AcceptanceFact(AcceptanceServers.MySql, "The MySQL acceptance suite")]
    public Task UniqueIndex_RejectsASecondRowWithTheSameClientId()
        => UniqueIndex_RejectsASecondRowWithTheSameClientIdCore();

    [AcceptanceFact(AcceptanceServers.MySql, "The MySQL acceptance suite")]
    public Task Update_RejectsAStaleConcurrencyToken()
        => Update_RejectsAStaleConcurrencyTokenCore();

    [AcceptanceFact(AcceptanceServers.MySql, "The MySQL acceptance suite")]
    public Task Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACount()
        => Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACountCore();

    [AcceptanceFact(AcceptanceServers.MySql, "The MySQL acceptance suite")]
    public Task CountQuery_CountsTheRows() => CountQuery_CountsTheRowsCore();

    [AcceptanceFact(AcceptanceServers.MySql, "The MySQL acceptance suite")]
    public Task ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritative()
        => ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritativeCore();

    [AcceptanceFact(AcceptanceServers.MySql, "The MySQL acceptance suite")]
    public Task AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorization()
        => AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorizationCore();

    [AcceptanceFact(AcceptanceServers.MySql, "The MySQL acceptance suite")]
    public Task BulkRevocation_TreatsANullStatusAsNotYetRevoked()
        => BulkRevocation_TreatsANullStatusAsNotYetRevokedCore();

    [AcceptanceFact(AcceptanceServers.MySql, "The MySQL acceptance suite")]
    public Task Prune_RemovesOnlyRowsBeforeTheThreshold()
        => Prune_RemovesOnlyRowsBeforeTheThresholdCore();
}

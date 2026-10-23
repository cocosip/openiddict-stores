using System.Threading.Tasks;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Dm;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// DM8 acceptance on a real server in <b>Oracle-compatible</b> mode (<c>COMPATIBLE_MODE=2</c>). This is the
/// run the two release blockers named in the DM <c>Sql/README.md</c> wait for in this mode: exact string
/// comparison (case, accents, and trailing spaces on an instance initialized with <c>CASE_SENSITIVE=1</c>)
/// and the Oracle-style <c>OFFSET ... ROWS / FETCH NEXT ... ROWS ONLY</c> paging clause. The suite installs
/// into the schema named by the <c>DmOracle</c> <c>schemas</c> entry under a run-unique prefix and drops
/// exactly the nine tables it created.
/// </summary>
public sealed class DmOracleCompatibleStoreContractTests : DmStoreContractTestsBase
{
    protected override string Key => AcceptanceServers.DmOracle;

    protected override string DmMode => OpenIddictStoreDmOracleCompatibleDialect.CompatibilityMode;

    protected override string ExpectedCompatibleMode => "2";

    [Net8HostAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task Server_PrerequisitesMeetTheDeclaredMode() => CheckServerPrerequisitesAsync();

    [Net8HostAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task InstallScript_CreatesTheNineTablesAndTheVersionRow()
        => Install_CreatesTheNineTablesAndTheVersionRowCore();

    [Net8HostAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task ReRunningTheInstallScript_IsRefusedInsteadOfSkipped()
        => ReRunningTheInstallScript_IsRefusedInsteadOfSkippedCore();

    [Net8HostAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task Application_RoundTripsUnicodeLargeValuesAndCollections()
        => Application_RoundTripsUnicodeLargeValuesAndCollectionsCore();

    [Net8HostAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task Dates_KeepMicrosecondPrecisionAcrossCreateAndRead()
        => Dates_KeepMicrosecondPrecisionAcrossCreateAndReadCore();

    [Net8HostAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task ExactComparison_DistinguishesCaseAndAccents()
        => ExactComparison_DistinguishesCaseAndAccentsCore();

    [Net8HostAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNone()
        => ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNoneCore();

    [Net8HostAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task UniqueIndex_RejectsASecondRowWithTheSameClientId()
        => UniqueIndex_RejectsASecondRowWithTheSameClientIdCore();

    [Net8HostAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task Update_RejectsAStaleConcurrencyToken()
        => Update_RejectsAStaleConcurrencyTokenCore();

    [Net8HostAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACount()
        => Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACountCore();

    [Net8HostAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task CountQuery_CountsTheRows() => CountQuery_CountsTheRowsCore();

    [Net8HostAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritative()
        => ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritativeCore();

    [Net8HostAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorization()
        => AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorizationCore();

    [Net8HostAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task BulkRevocation_TreatsANullStatusAsNotYetRevoked()
        => BulkRevocation_TreatsANullStatusAsNotYetRevokedCore();

    [Net8HostAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task Prune_RemovesOnlyRowsBeforeTheThreshold()
        => Prune_RemovesOnlyRowsBeforeTheThresholdCore();
}

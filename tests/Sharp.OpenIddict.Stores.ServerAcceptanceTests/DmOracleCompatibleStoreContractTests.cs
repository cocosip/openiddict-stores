using System.Threading.Tasks;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Dm;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// DM8 acceptance on a real server in <b>Oracle-compatible</b> mode (<c>COMPATIBLE_MODE=2</c>). This is the
/// run the two release blockers named in the DM <c>Sql/README.md</c> wait for in this mode: exact string
/// comparison (case, accents, and trailing spaces on an instance initialized with <c>CASE_SENSITIVE=1</c>)
/// and the Oracle-style <c>OFFSET ... ROWS / FETCH NEXT ... ROWS ONLY</c> paging clause. The suite installs
/// into the schema named by the <c>DmOracleSchema</c> secret under a run-unique prefix and drops exactly the
/// nine tables it created.
/// </summary>
public sealed class DmOracleCompatibleStoreContractTests : DmStoreContractTestsBase
{
    protected override string Key => AcceptanceServers.DmOracle;

    protected override string SchemaKey => AcceptanceServers.DmOracleSchema;

    protected override string DmMode => OpenIddictStoreDmOracleCompatibleDialect.CompatibilityMode;

    protected override string ExpectedCompatibleMode => "2";

    [DmAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task Server_PrerequisitesMeetTheDeclaredMode() => CheckServerPrerequisitesAsync();

    [DmAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task InstallScript_CreatesTheNineTablesAndTheVersionRow()
        => Install_CreatesTheNineTablesAndTheVersionRowCore();

    [DmAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task ReRunningTheInstallScript_IsRefusedInsteadOfSkipped()
        => ReRunningTheInstallScript_IsRefusedInsteadOfSkippedCore();

    [DmAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task Application_RoundTripsUnicodeLargeValuesAndCollections()
        => Application_RoundTripsUnicodeLargeValuesAndCollectionsCore();

    [DmAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task Dates_KeepMicrosecondPrecisionAcrossCreateAndRead()
        => Dates_KeepMicrosecondPrecisionAcrossCreateAndReadCore();

    [DmAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task ExactComparison_DistinguishesCaseAndAccents()
        => ExactComparison_DistinguishesCaseAndAccentsCore();

    [DmAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNone()
        => ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNoneCore();

    [DmAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task UniqueIndex_RejectsASecondRowWithTheSameClientId()
        => UniqueIndex_RejectsASecondRowWithTheSameClientIdCore();

    [DmAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task Update_RejectsAStaleConcurrencyToken()
        => Update_RejectsAStaleConcurrencyTokenCore();

    [DmAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACount()
        => Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACountCore();

    [DmAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task CountQuery_CountsTheRows() => CountQuery_CountsTheRowsCore();

    [DmAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritative()
        => ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritativeCore();

    [DmAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorization()
        => AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorizationCore();

    [DmAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task BulkRevocation_TreatsANullStatusAsNotYetRevoked()
        => BulkRevocation_TreatsANullStatusAsNotYetRevokedCore();

    [DmAcceptanceFact(AcceptanceServers.DmOracle, "The DM8 Oracle-compatible acceptance suite")]
    public Task Prune_RemovesOnlyRowsBeforeTheThreshold()
        => Prune_RemovesOnlyRowsBeforeTheThresholdCore();
}

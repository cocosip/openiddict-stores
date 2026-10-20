using System.Threading.Tasks;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Dm;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// DM8 acceptance on a real server in <b>native</b> mode (<c>COMPATIBLE_MODE=0</c>). This is the run the
/// two release blockers named in the DM <c>Sql/README.md</c> wait for: exact string comparison (case,
/// accents, and trailing spaces on an instance initialized with <c>CASE_SENSITIVE=1</c>) and the
/// <c>LIMIT/OFFSET</c> paging clause. The suite installs into the schema named by the
/// <c>DmNativeSchema</c> secret under a run-unique prefix and drops exactly the nine tables it created.
/// </summary>
public sealed class DmNativeStoreContractTests : DmStoreContractTestsBase
{
    protected override string Key => AcceptanceServers.DmNative;

    protected override string SchemaKey => AcceptanceServers.DmNativeSchema;

    protected override string DmMode => OpenIddictStoreDmDialect.CompatibilityMode;

    protected override string ExpectedCompatibleMode => "0";

    [DmAcceptanceFact(AcceptanceServers.DmNative, "The DM8 native-mode acceptance suite")]
    public Task Server_PrerequisitesMeetTheDeclaredMode() => CheckServerPrerequisitesAsync();

    [DmAcceptanceFact(AcceptanceServers.DmNative, "The DM8 native-mode acceptance suite")]
    public Task InstallScript_CreatesTheNineTablesAndTheVersionRow()
        => Install_CreatesTheNineTablesAndTheVersionRowCore();

    [DmAcceptanceFact(AcceptanceServers.DmNative, "The DM8 native-mode acceptance suite")]
    public Task ReRunningTheInstallScript_IsRefusedInsteadOfSkipped()
        => ReRunningTheInstallScript_IsRefusedInsteadOfSkippedCore();

    [DmAcceptanceFact(AcceptanceServers.DmNative, "The DM8 native-mode acceptance suite")]
    public Task Application_RoundTripsUnicodeLargeValuesAndCollections()
        => Application_RoundTripsUnicodeLargeValuesAndCollectionsCore();

    [DmAcceptanceFact(AcceptanceServers.DmNative, "The DM8 native-mode acceptance suite")]
    public Task Dates_KeepMicrosecondPrecisionAcrossCreateAndRead()
        => Dates_KeepMicrosecondPrecisionAcrossCreateAndReadCore();

    [DmAcceptanceFact(AcceptanceServers.DmNative, "The DM8 native-mode acceptance suite")]
    public Task ExactComparison_DistinguishesCaseAndAccents()
        => ExactComparison_DistinguishesCaseAndAccentsCore();

    [DmAcceptanceFact(AcceptanceServers.DmNative, "The DM8 native-mode acceptance suite")]
    public Task ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNone()
        => ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNoneCore();

    [DmAcceptanceFact(AcceptanceServers.DmNative, "The DM8 native-mode acceptance suite")]
    public Task UniqueIndex_RejectsASecondRowWithTheSameClientId()
        => UniqueIndex_RejectsASecondRowWithTheSameClientIdCore();

    [DmAcceptanceFact(AcceptanceServers.DmNative, "The DM8 native-mode acceptance suite")]
    public Task Update_RejectsAStaleConcurrencyToken()
        => Update_RejectsAStaleConcurrencyTokenCore();

    [DmAcceptanceFact(AcceptanceServers.DmNative, "The DM8 native-mode acceptance suite")]
    public Task Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACount()
        => Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACountCore();

    [DmAcceptanceFact(AcceptanceServers.DmNative, "The DM8 native-mode acceptance suite")]
    public Task CountQuery_CountsTheRows() => CountQuery_CountsTheRowsCore();

    [DmAcceptanceFact(AcceptanceServers.DmNative, "The DM8 native-mode acceptance suite")]
    public Task ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritative()
        => ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritativeCore();

    [DmAcceptanceFact(AcceptanceServers.DmNative, "The DM8 native-mode acceptance suite")]
    public Task AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorization()
        => AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorizationCore();

    [DmAcceptanceFact(AcceptanceServers.DmNative, "The DM8 native-mode acceptance suite")]
    public Task BulkRevocation_TreatsANullStatusAsNotYetRevoked()
        => BulkRevocation_TreatsANullStatusAsNotYetRevokedCore();

    [DmAcceptanceFact(AcceptanceServers.DmNative, "The DM8 native-mode acceptance suite")]
    public Task Prune_RemovesOnlyRowsBeforeTheThreshold()
        => Prune_RemovesOnlyRowsBeforeTheThresholdCore();
}

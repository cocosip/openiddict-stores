using System.Threading.Tasks;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Dm;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// DM8 acceptance on a real server in <b>MySQL-compatible</b> mode (<c>COMPATIBLE_MODE=4</c>). This mode had
/// no acceptance run at all: its dialect is behaviourally identical to the native one (same
/// <c>LIMIT</c>/<c>OFFSET</c> clause, same empty-string handling) and the schema body is byte-identical, so
/// until this suite runs the mode's manifest entry claims a target nothing has verified. Running it closes
/// the two blockers the DM <c>Sql/README.md</c> still lists for this mode: exact string comparison (case,
/// accents, and trailing spaces on an instance initialized with <c>CASE_SENSITIVE=1</c>) and the paging clause.
/// </summary>
/// <remarks>
/// The suite is deliberately a copy of the other two modes' fact list rather than a shared base with a mode
/// parameter: an acceptance suite is evidence, and evidence that silently covers fewer facts because a mode
/// was skipped is worse than no evidence. Configuring <c>DmMySql</c>/<c>DmMySqlSchema</c> in user-secrets is
/// all it takes to run it.
/// </remarks>
public sealed class DmMySqlCompatibleStoreContractTests : DmStoreContractTestsBase
{
    protected override string Key => AcceptanceServers.DmMySql;

    protected override string SchemaKey => AcceptanceServers.DmMySqlSchema;

    protected override string DmMode => OpenIddictStoreDmMySqlCompatibleDialect.CompatibilityMode;

    protected override string ExpectedCompatibleMode => "4";

    [DmAcceptanceFact(AcceptanceServers.DmMySql, "The DM8 MySQL-compatible acceptance suite")]
    public Task Server_PrerequisitesMeetTheDeclaredMode() => CheckServerPrerequisitesAsync();

    [DmAcceptanceFact(AcceptanceServers.DmMySql, "The DM8 MySQL-compatible acceptance suite")]
    public Task InstallScript_CreatesTheNineTablesAndTheVersionRow()
        => Install_CreatesTheNineTablesAndTheVersionRowCore();

    [DmAcceptanceFact(AcceptanceServers.DmMySql, "The DM8 MySQL-compatible acceptance suite")]
    public Task ReRunningTheInstallScript_IsRefusedInsteadOfSkipped()
        => ReRunningTheInstallScript_IsRefusedInsteadOfSkippedCore();

    [DmAcceptanceFact(AcceptanceServers.DmMySql, "The DM8 MySQL-compatible acceptance suite")]
    public Task Application_RoundTripsUnicodeLargeValuesAndCollections()
        => Application_RoundTripsUnicodeLargeValuesAndCollectionsCore();

    [DmAcceptanceFact(AcceptanceServers.DmMySql, "The DM8 MySQL-compatible acceptance suite")]
    public Task Dates_KeepMicrosecondPrecisionAcrossCreateAndRead()
        => Dates_KeepMicrosecondPrecisionAcrossCreateAndReadCore();

    [DmAcceptanceFact(AcceptanceServers.DmMySql, "The DM8 MySQL-compatible acceptance suite")]
    public Task ExactComparison_DistinguishesCaseAndAccents()
        => ExactComparison_DistinguishesCaseAndAccentsCore();

    [DmAcceptanceFact(AcceptanceServers.DmMySql, "The DM8 MySQL-compatible acceptance suite")]
    public Task ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNone()
        => ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNoneCore();

    [DmAcceptanceFact(AcceptanceServers.DmMySql, "The DM8 MySQL-compatible acceptance suite")]
    public Task UniqueIndex_RejectsASecondRowWithTheSameClientId()
        => UniqueIndex_RejectsASecondRowWithTheSameClientIdCore();

    [DmAcceptanceFact(AcceptanceServers.DmMySql, "The DM8 MySQL-compatible acceptance suite")]
    public Task Update_RejectsAStaleConcurrencyToken()
        => Update_RejectsAStaleConcurrencyTokenCore();

    [DmAcceptanceFact(AcceptanceServers.DmMySql, "The DM8 MySQL-compatible acceptance suite")]
    public Task Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACount()
        => Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACountCore();

    [DmAcceptanceFact(AcceptanceServers.DmMySql, "The DM8 MySQL-compatible acceptance suite")]
    public Task CountQuery_CountsTheRows() => CountQuery_CountsTheRowsCore();

    [DmAcceptanceFact(AcceptanceServers.DmMySql, "The DM8 MySQL-compatible acceptance suite")]
    public Task ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritative()
        => ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritativeCore();

    [DmAcceptanceFact(AcceptanceServers.DmMySql, "The DM8 MySQL-compatible acceptance suite")]
    public Task AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorization()
        => AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorizationCore();

    [DmAcceptanceFact(AcceptanceServers.DmMySql, "The DM8 MySQL-compatible acceptance suite")]
    public Task BulkRevocation_TreatsANullStatusAsNotYetRevoked()
        => BulkRevocation_TreatsANullStatusAsNotYetRevokedCore();

    [DmAcceptanceFact(AcceptanceServers.DmMySql, "The DM8 MySQL-compatible acceptance suite")]
    public Task Prune_RemovesOnlyRowsBeforeTheThreshold()
        => Prune_RemovesOnlyRowsBeforeTheThresholdCore();
}

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dm;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Dm;
using Xunit;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// Shared plumbing for the DM8 acceptance suites, one per declared compatibility mode. The mode names the
/// template and the dialect exactly as <c>UseDm(mode)</c> does at registration time, and the prerequisite
/// check proves the instance it runs against really is initialized for that mode: <c>COMPATIBLE_MODE</c>
/// matching the declared entry, and <c>CASE_SENSITIVE=1</c> without which every exact comparison silently
/// folds case (see the DM <c>Sql/README.md</c>). Run these suites on a net8.0 host: the driver ships no
/// net10 asset, so a net10 run would silently exercise the netstandard fallback instead (design 5.1); the
/// <see cref="Net8HostAcceptanceFactAttribute"/> refuses a net10 host outright.
/// </summary>
public abstract class DmStoreContractTestsBase : ServerAcceptanceBase
{
    /// <summary>
    /// The schema the tables are installed into, read from this key's <c>schemas</c> entry. Defaults to the
    /// suite's own key: one configuration entry describes one target, connection string and schema together.
    /// </summary>
    protected virtual string SchemaKey => Key;

    /// <summary>The declared compatibility mode; names the template directory and the dialect.</summary>
    protected abstract string DmMode { get; }

    /// <summary>The <c>COMPATIBLE_MODE</c> value the instance must report for this mode's entry.</summary>
    protected abstract string ExpectedCompatibleMode { get; }

    protected override string? SchemaName => ConfiguredSchema(SchemaKey);

    protected override OpenIddictStoreProvider Provider => OpenIddictStoreProvider.Dm;

    protected override IOpenIddictStoreDialect Dialect => DmMode switch
    {
        OpenIddictStoreDmDialect.CompatibilityMode => OpenIddictStoreDmDialect.Instance,
        OpenIddictStoreDmOracleCompatibleDialect.CompatibilityMode => OpenIddictStoreDmOracleCompatibleDialect.Instance,
        OpenIddictStoreDmMySqlCompatibleDialect.CompatibilityMode => OpenIddictStoreDmMySqlCompatibleDialect.Instance,
        _ => throw new InvalidOperationException($"Unknown DM8 compatibility mode '{DmMode}'."),
    };

    protected override DbConnection CreateConnection() => new DmConnection(ConnectionString);

    protected override string RenderInstallScript(string prefix)
        => DeploymentScript.Dm(DmMode, prefix, SchemaName);

    protected override Task<List<string>> ListOwnedTableNamesAsync(string prefix)
    {
        var owner = SchemaName;

        return string.IsNullOrEmpty(owner)
            ? QueryScalarsAsync<string>(
                "SELECT table_name FROM user_tables WHERE table_name LIKE :pattern",
                ("pattern", prefix + "%"))
            : QueryScalarsAsync<string>(
                "SELECT table_name FROM all_tables WHERE UPPER(owner) = UPPER(:owner) AND table_name LIKE :pattern",
                ("owner", owner!),
                ("pattern", prefix + "%"));
    }

    protected override Task DropOwnedTableAsync(string tableName)
        => ExecuteAsync($"DROP TABLE {QualifiedTable(tableName)}");

    protected override async Task CheckServerPrerequisitesAsync()
    {
        // The entry this suite declares is only valid on an instance whose COMPATIBLE_MODE matches it; a
        // mismatch would accept the script and then behave nothing like the declared target.
        var compatibleMode = Assert.Single(await QueryScalarsAsync<string>(
            "SELECT para_value FROM v$dm_ini WHERE para_name = 'COMPATIBLE_MODE'"));

        Assert.True(compatibleMode.Trim() == ExpectedCompatibleMode,
            $"This suite declares DM8 {DmMode} mode (COMPATIBLE_MODE={ExpectedCompatibleMode}), " +
            $"but the instance reports COMPATIBLE_MODE={compatibleMode.Trim()}.");

        // CASE_SENSITIVE is fixed at database-creation time and is not exposed through v$dm_ini, so the
        // instance's exact-comparison behavior is probed directly: a stored 'abc' must not be found by
        // 'ABC'. The probe table carries this run's prefix, is created and dropped here, and touches
        // nothing else.
        var probe = QualifiedTable(Prefix + "CaseProbe");

        try
        {
            await ExecuteAsync($"CREATE TABLE {probe} (\"V\" nvarchar(50))");
            await ExecuteAsync($"INSERT INTO {probe} VALUES ('abc')");

            var caseFolded = await QueryScalarsAsync<long>(
                $"SELECT COUNT(*) FROM {probe} WHERE \"V\" = 'ABC'");

            Assert.True(caseFolded.Single() == 0,
                "The instance folds case in string comparisons (CASE_SENSITIVE=0); exact string " +
                "comparison requires CASE_SENSITIVE=1 (see the DM Sql/README.md).");
        }
        finally
        {
            await ExecuteAsync($"DROP TABLE {probe}");
        }
    }
}

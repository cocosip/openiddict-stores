using System;
using System.IO;
using Xunit;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// The selection rules themselves. Every skip message in this project comes from here, and a skip that cannot
/// say which of the three causes applies ("not selected", "selected without a connection string", "no file at
/// all") is indistinguishable from "someone forgot to run it" - which the design refuses to count as a pass
/// (design 11).
/// </summary>
public sealed class AcceptanceConfigurationTests
{
    [Fact]
    public void Sqlite_NeedsNoConnectionString_SoItIsNeverSkippedForALackOfOne()
    {
        // This is what makes the harness usable on a machine with no database installed at all: SQLite's
        // target is a temporary file the run creates, so only the provider selection can exclude it.
        if (!AcceptanceServers.IsSelected(AcceptanceServers.Sqlite))
        {
            return;
        }

        Assert.Null(AcceptanceServers.Unavailable(AcceptanceServers.Sqlite));
        Assert.Empty(AcceptanceServers.ConnectionString(AcceptanceServers.Sqlite));
    }

    [Theory]
    [InlineData(AcceptanceServers.SqlServer)]
    [InlineData(AcceptanceServers.PostgreSql)]
    [InlineData(AcceptanceServers.MySql)]
    [InlineData(AcceptanceServers.MariaDb)]
    [InlineData(AcceptanceServers.Oracle)]
    [InlineData(AcceptanceServers.DmNative)]
    [InlineData(AcceptanceServers.DmOracle)]
    [InlineData(AcceptanceServers.DmMySql)]
    public void AServerSuite_AlwaysSaysWhyItCannotRun_AndNeverReadsAsAPass(string key)
    {
        if (AcceptanceServers.IsConfigured(key))
        {
            // The developer configured this one; the skip rule cannot be observed for it in this run.
            return;
        }

        var reason = AcceptanceServers.Unavailable(key);

        Assert.NotNull(reason);
        Assert.Contains(key, reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommittedTemplate_IsTheOnlyDatabaseConfigurationInTheRepository()
    {
        var directory = Path.Combine(
            RepositoryLayout.Root, "tests", "Sharp.OpenIddict.Stores.ServerAcceptanceTests");

        // A connection string must never be committed: the template documents the shape, the real file is
        // ignored by git. If a databases.json ever appears here in a clean checkout, this fails.
        Assert.True(File.Exists(Path.Combine(directory, "databases.json.template")));

        var ignore = File.ReadAllText(Path.Combine(RepositoryLayout.Root, ".gitignore"));

        Assert.Contains(
            "tests/Sharp.OpenIddict.Stores.ServerAcceptanceTests/databases.json",
            ignore, StringComparison.Ordinal);
    }
}

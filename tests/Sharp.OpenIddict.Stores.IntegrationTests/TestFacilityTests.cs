using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Sharp.OpenIddict.Stores.IntegrationTests;

/// <summary>
/// ENG-05: the shared test facilities themselves. These tests guard the guards - the naming rule that keeps
/// cleanup from touching a database file it did not create, and the fact that the same contract runs against
/// more than one provider instead of being copied per provider.
/// </summary>
/// <remarks>
/// The skip path of a missing server moved to <c>Sharp.OpenIddict.Stores.ServerAcceptanceTests</c> together
/// with the server suites; this project no longer has a server-backed fact to exercise it with.
/// </remarks>
public sealed class TestFacilityTests
{
    [Fact]
    public void GeneratedNames_AreRecognisedAsOwned()
    {
        var name = TestDatabaseName.Create("db02");

        Assert.True(TestDatabaseName.IsOwnedByThisTestRun(name));
        Assert.NotEqual(name, TestDatabaseName.Create("db02"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("master")]
    [InlineData("OpenIddict")]
    [InlineData("oidc-db02-abc")]                       // the old prefix format, without a GUID
    [InlineData("oidc-test-manual")]                    // starts with the prefix but is not generated
    [InlineData("oidc-test-db02-notaguidatallxxxxxxxxxxxxxxxxxxxxx")]
    [InlineData("production-oidc-test-db02-0123456789abcdef0123456789abcdef")]
    public void AnythingNotGeneratedByTheRun_IsNotOwned_SoCleanupMustLeaveItAlone(string? name)
    {
        // This is the rule that stands between a misconfigured run and dropping a real database, so the
        // near-miss cases matter more than the obvious ones.
        Assert.False(TestDatabaseName.IsOwnedByThisTestRun(name));
    }

    [Fact]
    public void DatabaseFilePaths_AreRecognisedByTheSameRule()
    {
        var path = TestDatabaseName.CreateFilePath("sqlite");

        Assert.True(TestDatabaseName.IsOwnedDatabaseFile(path));
        Assert.False(TestDatabaseName.IsOwnedDatabaseFile(Path.Combine(Path.GetTempPath(), "important.db")));
    }

    [Fact]
    public void StoreContracts_RunAgainstMoreThanOneProvider()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sharp.OpenIddict.Stores.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var tests = Path.Combine(directory!.FullName, "tests", "Sharp.OpenIddict.Stores.IntegrationTests");

        // The design requires one shared set of assertions executed per provider through a fixture, not six
        // copies of the assertions (design 11). The four typed store test classes are the SQLite execution and
        // the generic contract classes consume them; what must not appear is a per-provider duplicate of the
        // assertion bodies. This checks the arrangement that makes that true: the provider-specific files stay
        // thin, and the shared contract files carry the assertions.
        var providerSpecific = Directory.GetFiles(tests, "Sqlite*StoreTests.cs").ToList();

        Assert.Equal(4, providerSpecific.Count);

        foreach (var file in providerSpecific)
        {
            var lines = File.ReadAllLines(file).Length;

            Assert.True(lines < 700, $"{Path.GetFileName(file)} has grown to {lines} lines; provider-specific code should stay thin.");
        }

        // And the shared contract suites must exist and be provider-agnostic: they must not mention a concrete
        // provider package.
        var shared = new[]
        {
            "AccessorContractTests.cs", "BulkRevocationContractTests.cs", "CollectionIndexContractTests.cs",
            "DeleteAssociationContractTests.cs", "PruneContractTests.cs", "QueryTranslationContractTests.cs"
        };

        foreach (var name in shared)
        {
            var path = Path.Combine(tests, name);
            Assert.True(File.Exists(path), $"{name} is missing.");

            var text = File.ReadAllText(path);

            Assert.DoesNotContain("Sharp.OpenIddict.Stores.SqlServer", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Sharp.OpenIddict.Stores.PostgreSql", text, StringComparison.Ordinal);
        }
    }
}

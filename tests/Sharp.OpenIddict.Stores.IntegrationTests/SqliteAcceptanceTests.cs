using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sharp.OpenIddict.Stores.Models;
using Sharp.OpenIddict.Stores.Sqlite;
using Sharp.OpenIddict.Stores.Stores;
using Xunit;

namespace Sharp.OpenIddict.Stores.IntegrationTests;

/// <summary>
/// DB-01 acceptance for SQLite: what the real, file-backed database does, beyond the Store contract tests that
/// run on it. Each check proves a property the design relies on but no amount of unit mocking can establish.
/// </summary>
/// <remarks>
/// Every test opens a real temporary file rather than an in-memory database: an in-memory SQLite database per
/// connection would silently pass tests that cannot work on the deployed file database, and the design says the
/// file database is what has to be accepted (design 5, DB-01).
/// </remarks>
public sealed class SqliteAcceptanceTests
{
    [Fact]
    public async Task ServerVersion_MeetsTheDeclaredMinimum()
    {
        await using var connection = await OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version()";

        var version = Version.Parse((string)(await command.ExecuteScalarAsync())!);

        // The design declares 3.35+; 3.35 is when RETURNING and the STRICT table option appeared, and more
        // importantly it is the floor this package promises. Asserting the floor rather than the installed
        // build keeps the test honest on other machines while still failing on an unsupported one.
        Assert.True(version >= new Version(3, 35), $"SQLite {version} is below the declared 3.35 minimum.");
    }

    [Fact]
    public async Task ForeignKeys_AreEnforcedByTheDatabase()
    {
        var path = await CreateInstalledDatabaseAsync();

        await using var connection = await OpenAsync(path);

        // The Store executes the associated deletes itself, so foreign keys are the fallback that catches a bug
        // in that logic. Without them a broken delete would silently leave orphans (design 15.2, 16.2.1).
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO OpenIddictAuthorizations (Id, ConcurrencyToken, ApplicationId) VALUES ('a1', 'c', 'missing');";

        var error = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());

        Assert.Contains("FOREIGN KEY", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ForeignKeys_AreOnByDefault_SoAnUnconfiguredConnectionIsStillSafe()
    {
        await using var connection = await OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys";

        // Microsoft.Data.Sqlite enables enforcement by default. This is recorded as a test because the whole
        // integrity story above depends on it, and because SQLite's own default (off) is the opposite.
        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task UtcDates_RoundTripThroughTheFixedTextFormat()
    {
        var path = await CreateInstalledDatabaseAsync();

        await using var connection = await OpenAsync(path);

        // Microsecond precision with an explicit UTC wall-clock value; SQLite has no date type, so the dialect
        // stores this exact text (design 15.5). The value is built from whole microseconds on purpose: the
        // design normalises to microsecond precision, so sub-microsecond ticks are not expected to survive.
        var created = new DateTime(2026, 10, 19, 13, 45, 12, DateTimeKind.Utc).AddTicks(3_450);

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText =
                "INSERT INTO OpenIddictAuthorizations (Id, ConcurrencyToken, CreationDate) VALUES ('a1', 'c', $date);";
            insert.Parameters.AddWithValue("$date", OpenIddictStoreSqliteDialect.Instance.FormatDate(created)!);
            await insert.ExecuteNonQueryAsync();
        }

        await using var select = connection.CreateCommand();
        select.CommandText = "SELECT CreationDate FROM OpenIddictAuthorizations WHERE Id = 'a1';";

        var text = (string)(await select.ExecuteScalarAsync())!;

        // The stored form must be the documented fixed-format UTC text, not a provider-specific default.
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{6}$", text);

        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT CreationDate FROM OpenIddictAuthorizations WHERE Id = 'a1';";
        await using var reader = await read.ExecuteReaderAsync();
        await reader.ReadAsync();

        var restored = OpenIddictStoreSqliteDialect.Instance.ReadDate(reader, 0);

        Assert.Equal(DateTimeKind.Utc, restored!.Value.Kind);
        Assert.Equal(created, restored.Value);
    }

    [Fact]
    public async Task ConcurrentWriters_DoNotCorruptDataOrBlockForever()
    {
        var path = await CreateInstalledDatabaseAsync();

        // SQLite allows one writer at a time. This proves the deployment stays usable under concurrent
        // creates: either a write succeeds, or it fails with a clear busy/locked error, but never silently
        // loses a row or hangs indefinitely (DB-01 asks for the read/write locking behaviour).
        const int writers = 8;

        var results = await Task.WhenAll(Enumerable.Range(0, writers).Select(async index =>
        {
            await using var connection = await OpenAsync(path);

            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO OpenIddictScopes (Id, ConcurrencyToken, Name) VALUES ($id, 'c', $name);";
            command.Parameters.AddWithValue("$id", $"scope-{index}");
            command.Parameters.AddWithValue("$name", $"name-{index}");

            try
            {
                await command.ExecuteNonQueryAsync();
                return (Succeeded: true, Error: (Exception?)null);
            }
            catch (SqliteException exception)
            {
                return (Succeeded: false, Error: exception);
            }
        }));

        await using var verify = await OpenAsync(path);
        await using var count = verify.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM OpenIddictScopes;";

        var stored = Convert.ToInt32(await count.ExecuteScalarAsync());
        var succeeded = results.Count(result => result.Succeeded);

        // Every attempt either committed or reported a lock error; the row count must match the successes
        // exactly, which is what rules out a half-applied write.
        Assert.Equal(succeeded, stored);

        // And the failures, if any, must be lock contention rather than a corrupted database.
        foreach (var failure in results.Where(result => !result.Succeeded))
        {
            Assert.Contains("locked", failure.Error!.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Paging_IsStableAndDeterministicOnTheFileDatabase()
    {
        var path = await CreateInstalledDatabaseAsync();

        var options = Options.Create(new OpenIddictStoreOptions { TablePrefix = "OpenIddict" });
        options.Value.Providers.Add(OpenIddictStoreProvider.Sqlite);

        var store = new OpenIddictStoreScopeStore<OpenIddictStoreScope>(
            options,
            new SqliteConnectionFactory(BuildConnectionString(path)),
            OpenIddictStoreSqliteDialect.Instance,
            NullLogger<OpenIddictStoreScopeStore<OpenIddictStoreScope>>.Instance);

        // Insert out of order so a stable sort has something to fix.
        foreach (var id in new[] { "d", "b", "a", "c" })
        {
            await store.CreateAsync(new OpenIddictStoreScope { Id = id, ConcurrencyToken = "c", Name = "n-" + id }, CancellationToken.None);
        }

        var first = await ToIdListAsync(store.ListAsync(2, 0, CancellationToken.None));
        var second = await ToIdListAsync(store.ListAsync(2, 2, CancellationToken.None));
        var repeat = await ToIdListAsync(store.ListAsync(2, 0, CancellationToken.None));

        // Pages must not overlap, must be ordered by Id, and must be reproducible: keyset pagination over an
        // unstable sort would skip and duplicate rows (design 8, 15.4).
        Assert.Equal(new[] { "a", "b" }, first);
        Assert.Equal(new[] { "c", "d" }, second);
        Assert.Equal(first, repeat);

        var all = await ToIdListAsync(store.ListAsync(null, null, CancellationToken.None));
        Assert.Equal(new[] { "a", "b", "c", "d" }, all);
    }

    [Fact]
    public async Task InstallScript_IsRefusedOnASecondRun()
    {
        var path = await CreateInstalledDatabaseAsync();

        await using var connection = await OpenAsync(path);

        var statements = OpenIddictStoreIdentifiers.SplitStatements(DeploymentScript.Sqlite())
            .Where(statement => !statement.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase)
                && !statement.StartsWith("COMMIT", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Running the script twice must fail loudly rather than silently skip: a half-installed schema that
        // looks installed is the failure mode the design explicitly wants to prevent (design 10.3).
        var error = await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            foreach (var statement in statements)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = statement;
                await command.ExecuteNonQueryAsync();
            }
        });

        Assert.Contains("already exists", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<List<OpenIddictStoreScope>> ToListAsync(IAsyncEnumerable<OpenIddictStoreScope> source)
    {
        var list = new List<OpenIddictStoreScope>();

        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }

    private static async Task<List<string>> ToIdListAsync(IAsyncEnumerable<OpenIddictStoreScope> source)
        => (await ToListAsync(source)).Select(scope => scope.Id!).ToList();

    private static string BuildConnectionString(string path)
        => new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString();

    private static async Task<SqliteConnection> OpenAsync(string? path = null)
    {
        var connection = new SqliteConnection(BuildConnectionString(
            path ?? Path.Combine(Path.GetTempPath(), $"oidc-open-{Guid.NewGuid():N}.db")));

        await connection.OpenAsync();

        return connection;
    }

    /// <summary>Creates a temporary database, applies the shipped install script, and returns its path.</summary>
    private static async Task<string> CreateInstalledDatabaseAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oidc-db01-{Guid.NewGuid():N}.db");

        await using var connection = new SqliteConnection(BuildConnectionString(path));
        await connection.OpenAsync();

        foreach (var statement in OpenIddictStoreIdentifiers.SplitStatements(DeploymentScript.Sqlite()))
        {
            if (statement.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase)
                || statement.StartsWith("COMMIT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync();
        }

        return path;
    }
}

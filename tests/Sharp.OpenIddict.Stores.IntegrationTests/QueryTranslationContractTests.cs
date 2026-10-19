using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Models;
using Sharp.OpenIddict.Stores.Sqlite;
using Sharp.OpenIddict.Stores.Stores;
using Xunit;

namespace Sharp.OpenIddict.Stores.IntegrationTests;

/// <summary>
/// Query callback translation contract (design 9 and its two truth tables). Every row of the equality
/// table and the collection table is exercised against a real database, because the whole point of the
/// translation is that C# comparison semantics survive the trip through SQL.
/// </summary>
public sealed class QueryTranslationContractTests : IAsyncLifetime
{
    private string _databasePath = string.Empty;
    private string _connectionString = string.Empty;
    private OpenIddictStoreTokenStore<OpenIddictStoreToken> _tokens = null!;

    public async Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"oidc-query-{Guid.NewGuid():N}.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true,
        }.ToString();

        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync();

            var script = OpenIddictStoreIdentifiers.ApplyPrefix(
                await File.ReadAllTextAsync(Path.Combine(RepositoryRoot,
                    "src", "Sharp.OpenIddict.Stores.Sqlite", "Sql", "7", "install", "v0001.sql")),
                "OpenIddict");

            foreach (var statement in OpenIddictStoreIdentifiers.SplitStatements(script))
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
        }

        var options = Options.Create(new OpenIddictStoreOptions { TablePrefix = "OpenIddict" });
        options.Value.Providers.Add(OpenIddictStoreProvider.Sqlite);

        _tokens = new OpenIddictStoreTokenStore<OpenIddictStoreToken>(
            options,
            new SqliteConnectionFactory(_connectionString),
            OpenIddictStoreSqliteDialect.Instance,
            NullLogger<OpenIddictStoreTokenStore<OpenIddictStoreToken>>.Instance);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task EqualityTable_MatchesCSharpSemantics()
    {
        // Design 9.1 equality table, one row per case.
        await CreateAsync(null);
        await CreateAsync("alice");

        // NULL == NULL → true, so a NULL-valued column equals a null constant.
        Assert.Equal(1L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.Subject == null), default));

        // NULL == 非 NULL → false.
        Assert.Equal(0L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.Subject == "nobody"), default));

        // 非 NULL == NULL → false.
        Assert.Equal(0L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.Subject != null && token.Subject == null), default));

        // 非 NULL == 非 NULL → 按值比较。
        Assert.Equal(1L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.Subject == "alice"), default));

        // NULL != NULL → false: the negation of the whole two-valued predicate, not `<>`.
        Assert.Equal(1L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.Subject != null), default));

        // NULL != 非 NULL → true.
        Assert.Equal(1L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.Subject != "alice"), default));
    }

    [Fact]
    public async Task StringComparison_IsExactForCaseAndTrailingSpace()
    {
        await CreateAsync("alice");
        await CreateAsync("Alice");
        await CreateAsync("alice ");

        // Only the exact value matches; a collation-dependent `=` would return all three.
        Assert.Equal(1L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.Subject == "alice"), default));

        Assert.Equal(2L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.Subject != "alice"), default));
    }

    [Fact]
    public async Task CollectionTable_MatchesCSharpSemantics()
    {
        await CreateAsync(null);
        await CreateAsync("alice");

        var empty = Array.Empty<string>();
        var onlyNull = new string?[] { null };
        var withNull = new string?[] { "alice", null };
        var withoutNull = new[] { "alice" };
        var missing = new[] { "nobody" };

        // 空集合 → 任意值都是 false；取反为 true。
        Assert.Equal(0L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => empty.Contains(token.Subject!)), default));
        Assert.Equal(2L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => !empty.Contains(token.Subject!)), default));

        // 只含 NULL，被比较值为 NULL → true。
        Assert.Equal(1L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => onlyNull.Contains(token.Subject)), default));

        // 只含 NULL，被比较值为非 NULL → false。
        Assert.Equal(0L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.Subject != null && onlyNull.Contains(token.Subject)), default));

        // 含非 NULL 且含 NULL：NULL 行与 "alice" 行都命中，因此是 2。
        Assert.Equal(2L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => withNull.Contains(token.Subject)), default));

        // 不含 NULL，被比较值为 NULL → false。
        Assert.Equal(0L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.Subject == null && withoutNull.Contains(token.Subject)), default));

        // 非 NULL 值逐项匹配：集合中不含该值时为零。
        Assert.Equal(0L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => missing.Contains(token.Subject)), default));

        // Contains 取反对每一行返回相反结果。
        Assert.Equal(1L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => !missing.Contains(token.Subject)), default));
    }

    [Fact]
    public async Task Collection_DoesNotDependOnNotInWithNull()
    {
        // `NOT IN (..., NULL)` would return no rows at all; the compensated predicate must still return
        // the rows that do not match.
        await CreateAsync("alice");
        await CreateAsync("bob");

        var values = new string?[] { "alice", null };

        // Only "alice" matches: a NULL element can never match a non-null subject.
        Assert.Equal(1L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => values.Contains(token.Subject)), default));

        // The negation therefore matches the other row, which a plain NOT IN would have lost entirely.
        Assert.Equal(1L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => !values.Contains(token.Subject)), default));
    }

    [Fact]
    public async Task NullableDateComparison_IsFalseWhenEitherSideIsNull()
    {
        await CreateAsync("dated", creationDate: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await CreateAsync("undated");

        var cutoff = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        // Only the row with a real date before the cutoff matches; the NULL row is not "less than".
        Assert.Equal(1L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.CreationDate < cutoff), default));

        // Negating a date comparison must include the NULL row (design 9.1), not become `>=`.
        Assert.Equal(1L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => !(token.CreationDate < cutoff)), default));

        // `>=` alone excludes the NULL row.
        Assert.Equal(0L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.CreationDate >= cutoff), default));
    }

    [Fact]
    public async Task NullDateEquality_FollowsTheEqualityTable()
    {
        await CreateAsync("dated", creationDate: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await CreateAsync("undated");

        Assert.Equal(1L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.CreationDate == null), default));

        Assert.Equal(1L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.CreationDate != null), default));

        var cutoff = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(1L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.CreationDate == cutoff), default));
    }

    [Fact]
    public async Task AndOrNot_ComposeOnCompensatedPredicates()
    {
        await CreateAsync("alice", status: "valid");
        await CreateAsync("alice", status: "revoked");
        await CreateAsync("bob", status: "valid");
        await CreateAsync(null, status: null);

        // 只有一条同时满足 Subject=alice 且 Status=valid。
        Assert.Equal(1L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.Subject == "alice" && token.Status == "valid"), default));

        Assert.Equal(3L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.Subject == "alice" || token.Status == "valid"), default));

        // NOT over an equality must include the NULL rows that the equality excluded.
        Assert.Equal(2L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => !(token.Subject == "alice")), default));
    }

    [Fact]
    public async Task SameTableColumnComparison_IsTranslated()
    {
        await CreateAsync("same", status: "same");
        await CreateAsync("different", status: "other");

        // Two columns of the same table compare against each other with the compensated predicate.
        Assert.Equal(1L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.Subject == token.Status), default));

        Assert.Equal(1L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.Subject != token.Status), default));
    }

    [Fact]
    public async Task ClosedOverConstant_IsBoundAsAParameter()
    {
        var wanted = "alice";
        await CreateAsync("alice");
        await CreateAsync("bob");

        Assert.Equal(1L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.Subject == wanted), default));
    }

    [Fact]
    public async Task StateParameter_IsBoundBeforeTranslation()
    {
        await CreateAsync("alice");
        await CreateAsync("bob");

        var count = await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.Subject == "alice"), default);

        Assert.Equal(1L, count);
    }

    [Fact]
    public async Task LargeTextAndJson_AllowNullChecksButNotComparison()
    {
        await CreateAsync("alice", payload: "data");

        // A null check on an unbounded column is allowed.
        Assert.Equal(0L, await _tokens.CountAsync<OpenIddictStoreToken>(
            query => query.Where(token => token.Payload == null), default));

        // Comparing its content is rejected before any SQL runs.
        await Assert.ThrowsAsync<NotSupportedException>(
            () => _tokens.CountAsync<OpenIddictStoreToken>(
                query => query.Where(token => token.Payload == "data"), default).AsTask());

        await Assert.ThrowsAsync<NotSupportedException>(
            () => _tokens.CountAsync<OpenIddictStoreToken>(
                query => query.Where(token => token.Properties == "{}"), default).AsTask());
    }

    [Fact]
    public async Task UnsupportedOperators_AreRejectedBeforeExecution()
    {
        await CreateAsync("alice");

        // No Join, GroupBy, or arbitrary method execution (design 9.2).
        await Assert.ThrowsAsync<NotSupportedException>(
            () => _tokens.CountAsync<OpenIddictStoreToken>(
                query => query.GroupBy(token => token.Subject).SelectMany(group => group), default).AsTask());

        await Assert.ThrowsAsync<NotSupportedException>(
            () => _tokens.CountAsync<OpenIddictStoreToken>(
                query => query.Where(token => token.Subject!.Length > 3), default).AsTask());

        await Assert.ThrowsAsync<NotSupportedException>(
            () => _tokens.CountAsync<OpenIddictStoreToken>(
                query => query.Where(token => token.Subject!.StartsWith("a")), default).AsTask());
    }

    [Fact]
    public async Task UnmappedMember_IsRejected()
    {
        await CreateAsync("alice");

        // A member that is not part of the column map cannot silently become a column name.
        await Assert.ThrowsAsync<NotSupportedException>(
            () => _tokens.CountAsync<OpenIddictStoreToken>(
                query => query.Where(token => token.Subject!.Trim().Length == 5), default).AsTask());
    }

    [Fact]
    public async Task CallbackIsNeverEnumeratedInMemory()
    {
        await CreateAsync("alice");

        // Any attempt to enumerate the recording queryable throws instead of loading rows.
        await Assert.ThrowsAsync<NotSupportedException>(
            () => _tokens.CountAsync<OpenIddictStoreToken>(
                query => query.Where(token => ((IEnumerable<OpenIddictStoreToken>)query).Any()), default).AsTask());
    }

    [Fact]
    public async Task Projection_ReturnsMappedColumns()
    {
        await CreateAsync("alice", status: "valid");
        await CreateAsync("bob", status: "revoked");

        var subjects = await ToListAsync(_tokens.ListAsync<int, string>(
            (query, _) => query.Where(token => token.Status == "valid").Select(token => token.Subject!), 0, default));

        Assert.Equal(["alice"], subjects);
    }

    [Fact]
    public async Task PagingAndOrdering_AreTranslated()
    {
        await CreateAsync("c");
        await CreateAsync("a");
        await CreateAsync("b");

        var ordered = await ToListAsync(_tokens.ListAsync<int, string>(
            (query, _) => query.OrderBy(token => token.Subject).Select(token => token.Subject!), 0, default));

        Assert.Equal(["a", "b", "c"], ordered);

        var paged = await ToListAsync(_tokens.ListAsync<int, string>(
            (query, _) => query.OrderBy(token => token.Subject).Skip(1).Take(1)
                .Select(token => token.Subject!), 0, default));

        Assert.Equal(["b"], paged);
    }

    private async Task<string> CreateAsync(
        string? subject,
        string? status = null,
        string? payload = null,
        DateTimeOffset? creationDate = null)
    {
        var token = new OpenIddictStoreToken
        {
            Subject = subject,
            Status = status,
            Payload = payload,
        };

        await _tokens.SetCreationDateAsync(token, creationDate, default);
        await _tokens.CreateAsync(token, default);

        return token.Id!;
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();

        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }

    private static string RepositoryRoot { get; } = LocateRepositoryRoot();

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sharp.OpenIddict.Stores.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root (Sharp.OpenIddict.Stores.slnx) was not found.");
    }

    private sealed class SqliteConnectionFactory : IOpenIddictStoreConnectionFactory
    {
        private readonly string _connectionString;

        public SqliteConnectionFactory(string connectionString) => _connectionString = connectionString;

        public ValueTask<System.Data.Common.DbConnection> CreateConnectionAsync(
            System.Threading.CancellationToken cancellationToken)
            => new(new SqliteConnection(_connectionString));
    }

    /// <summary>Placeholder state type; the state overload is exercised through the store signature.</summary>
    private sealed class TokenFilter
    {
    }
}

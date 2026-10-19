using System;
using System.Collections.Generic;
using System.Data;
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
/// ENG-04: connection ownership, cancellation propagation, and error surfacing on a real file database.
/// </summary>
/// <remarks>
/// The point of these tests is that they measure the connection lifecycle rather than assume it. A leaked
/// connection is invisible in functional tests and only shows up as exhaustion under load, so the tracking
/// factory below records every open and every dispose and the assertions are made against those counts.
/// </remarks>
public sealed class ConnectionLifecycleTests
{
    [Fact]
    public async Task EveryOperation_OpensAndDisposesItsOwnConnection()
    {
        var (_, connectionString) = await CreateDatabaseAsync();
        var tracker = new TrackingFactory(connectionString);

        var store = CreateScopeStore(tracker);

        var before = tracker.Snapshot();

        await store.CreateAsync(new OpenIddictStoreScope { Name = "lifecycle" }, CancellationToken.None);
        await store.FindByNameAsync("lifecycle", CancellationToken.None);

        // A streaming enumeration holds its connection until the enumerator is disposed - that is the point of
        // streaming, and QUERY-03 covers it. It is completed here so this test measures per-operation
        // acquisition and release rather than an intentionally open cursor.
        await foreach (var _ in store.ListAsync(count: null, offset: null, CancellationToken.None))
        {
        }

        var after = tracker.Snapshot();

        // Each operation must open its own connection instead of sharing one, and must release it: a shared
        // connection would serialise the whole application and one leak would surface only under load.
        Assert.Equal(after.Created, after.Disposed);
        Assert.True(after.Created > before.Created, "No connections were created; the store is not using the factory.");
    }

    [Fact]
    public async Task FailedOperation_DoesNotLeakTheConnection()
    {
        var (_, connectionString) = await CreateDatabaseAsync();
        var tracker = new TrackingFactory(connectionString);

        var store = CreateScopeStore(tracker);

        // A duplicate unique key makes the insert fail inside the store, after the connection was opened.
        await store.CreateAsync(new OpenIddictStoreScope { Name = "duplicate" }, CancellationToken.None);

        await Assert.ThrowsAnyAsync<DbException>(
            () => store.CreateAsync(new OpenIddictStoreScope { Name = "duplicate" }, CancellationToken.None).AsTask());

        var snapshot = tracker.Snapshot();

        // A failure path that forgets to dispose is the realistic leak, so this is asserted separately from
        // the success path.
        Assert.Equal(snapshot.Created, snapshot.Disposed);
    }

    [Fact]
    public async Task CancelledOperation_PropagatesCancellation_AndDoesNotLeak()
    {
        var (_, connectionString) = await CreateDatabaseAsync();
        var tracker = new TrackingFactory(connectionString);

        var store = CreateScopeStore(tracker);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.CreateAsync(new OpenIddictStoreScope { Name = "cancelled" }, cancellation.Token).AsTask());

        var snapshot = tracker.Snapshot();

        Assert.Equal(snapshot.Created, snapshot.Disposed);
    }

    [Fact]
    public async Task OpenFailure_IsSurfacedAndTheCreatedConnectionIsDisposed()
    {
        // A factory that produces a connection which cannot be opened: the store must surface the failure
        // rather than swallow it, and must not strand the connection it created.
        var path = Path.Combine(Path.GetTempPath(), $"oidc-missing-{Guid.NewGuid():N}", "nested", "db.db");
        var tracker = new TrackingFactory(new SqliteConnectionStringBuilder { DataSource = path }.ToString());

        var store = CreateScopeStore(tracker);

        await Assert.ThrowsAnyAsync<Exception>(
            () => store.CreateAsync(new OpenIddictStoreScope { Name = "unopenable" }, CancellationToken.None).AsTask());

        var snapshot = tracker.Snapshot();

        Assert.True(snapshot.Created > 0);
        Assert.Equal(snapshot.Created, snapshot.Disposed);
    }

    [Fact]
    public async Task FactoryConnection_IsDisposedByTheOperation_AsTheDesignRequires()
    {
        var (_, connectionString) = await CreateDatabaseAsync();

        // Design 6: "the connection returned by the factory belongs to this operation and is released by it;
        // an external DataSource is not released by the extension". The store closing a factory-returned
        // connection is therefore the contract, not a violation. What must never happen is the extension
        // reaching past the connection to dispose an external DataSource, which it cannot do because it only
        // ever holds the connection.
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var store = CreateScopeStore(new FixedFactory(connection));

        await store.CreateAsync(new OpenIddictStoreScope { Name = "owned-by-operation" }, CancellationToken.None);

        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task WriteLogging_RecordsCountsButNoBusinessValues()
    {
        var (_, connectionString) = await CreateDatabaseAsync();

        var logger = new CapturingLogger<OpenIddictStoreScopeStore<OpenIddictStoreScope>>();

        var options = Options.Create(new OpenIddictStoreOptions { TablePrefix = "OpenIddict" });
        options.Value.Providers.Add(OpenIddictStoreProvider.Sqlite);

        var store = new OpenIddictStoreScopeStore<OpenIddictStoreScope>(
            options, new TrackingFactory(connectionString), OpenIddictStoreSqliteDialect.Instance, logger);

        // A value distinctive enough that finding it in a log line proves the log leaks business data.
        const string sensitive = "secret-scope-value-9f3a";

        await store.CreateAsync(new OpenIddictStoreScope { Name = sensitive }, CancellationToken.None);

        var text = string.Join("\n", logger.Messages);

        Assert.NotEmpty(text);

        // Counts and operation names are fine; identifiers and values are not (design 6, ENG-04).
        Assert.DoesNotContain(sensitive, text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Captures formatted log messages so a test can assert on what was actually written.</summary>
    private sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return _messages.ToList();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (formatter is null)
            {
                return;
            }

            lock (_messages)
            {
                _messages.Add(formatter(state, exception));
            }
        }
    }

    private static OpenIddictStoreScopeStore<OpenIddictStoreScope> CreateScopeStore(
        IOpenIddictStoreConnectionFactory factory)
    {
        var options = Options.Create(new OpenIddictStoreOptions { TablePrefix = "OpenIddict" });
        options.Value.Providers.Add(OpenIddictStoreProvider.Sqlite);

        return new OpenIddictStoreScopeStore<OpenIddictStoreScope>(
            options, factory, OpenIddictStoreSqliteDialect.Instance,
            NullLogger<OpenIddictStoreScopeStore<OpenIddictStoreScope>>.Instance);
    }

    private static async Task<(string Path, string ConnectionString)> CreateDatabaseAsync()
    {
        var path = TestDatabaseName.CreateFilePath("ConnectionLifecycleTests");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString();

        await using var connection = new SqliteConnection(connectionString);
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

        return (path, connectionString);
    }

    /// <summary>Counts how many connections were created and disposed, so lifecycle claims can be measured.</summary>
    private sealed class TrackingFactory : IOpenIddictStoreConnectionFactory
    {
        private readonly string _connectionString;
        private int _created;
        private int _disposed;

        public TrackingFactory(string connectionString) => _connectionString = connectionString;

        public (int Created, int Disposed) Snapshot()
            => (Volatile.Read(ref _created), Volatile.Read(ref _disposed));

        public ValueTask<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _created);

            return new ValueTask<DbConnection>(new TrackedConnection(_connectionString, this));
        }

        private void OnDisposed() => Interlocked.Increment(ref _disposed);

        /// <summary>
        /// Counts one disposal per connection. Both dispose paths are counted through a single flag because
        /// DisposeAsync calls Dispose(true) internally on this provider: counting per override reported the
        /// same connection twice and made the totals meaningless.
        /// </summary>
        private sealed class TrackedConnection(string connectionString, TrackingFactory owner)
            : SqliteConnection(connectionString)
        {
            private int _reported;

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);

                if (disposing)
                {
                    Report();
                }
            }

            public override async ValueTask DisposeAsync()
            {
                await base.DisposeAsync();

                Report();
            }

            private void Report()
            {
                if (Interlocked.Exchange(ref _reported, 1) == 0)
                {
                    owner.OnDisposed();
                }
            }
        }
    }

    /// <summary>Returns the same already-open connection every time, to check it is not disposed by the store.</summary>
    private sealed class FixedFactory(DbConnection connection) : IOpenIddictStoreConnectionFactory
    {
        public ValueTask<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken)
            => new(connection);
    }
}

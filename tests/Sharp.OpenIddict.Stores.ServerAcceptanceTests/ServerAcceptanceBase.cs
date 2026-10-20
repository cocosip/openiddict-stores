using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sharp.OpenIddict.Stores.Models;
using Sharp.OpenIddict.Stores.Stores;
using Xunit;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// Shared plumbing for acceptance runs on real servers that give the test no database of its own
/// (PostgreSQL, Oracle, and DM install into a schema other things may share). Isolation and cleanup work one
/// level below a create-and-drop-its-own-database fixture: the run renders the shipped template with a
/// run-unique table prefix (<see cref="TestTablePrefix"/>), and disposal lists the catalog and drops only
/// the exact nine names this run's prefix produces — a pre-existing deployment under the default prefix, and
/// anything else in the schema, is never touched.
/// </summary>
/// <remarks>
/// The acceptance facts themselves are declared by the concrete suites (one per server), because an
/// <see cref="AcceptanceFactAttribute"/> needs its secret key at compile time. The bodies live here so every
/// provider is held to the same contract without the logic being copied per dialect.
/// </remarks>
public abstract class ServerAcceptanceBase : IAsyncLifetime
{
    private string _prefix = string.Empty;
    private string? _schema;
    private string _connectionString = string.Empty;
    private OpenIddictStoreApplicationStore<OpenIddictStoreApplication> _applications = null!;
    private OpenIddictStoreScopeStore<OpenIddictStoreScope> _scopes = null!;
    private OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization> _authorizations = null!;
    private OpenIddictStoreTokenStore<OpenIddictStoreToken> _tokens = null!;

    /// <summary>The user-secrets key whose connection string selects the server for this suite.</summary>
    protected abstract string Key { get; }

    protected abstract OpenIddictStoreProvider Provider { get; }

    protected abstract IOpenIddictStoreDialect Dialect { get; }

    /// <summary>The dedicated schema, or null to install into the connection's default schema.</summary>
    protected virtual string? SchemaName => null;

    protected abstract DbConnection CreateConnection();

    protected abstract string RenderInstallScript(string prefix);

    /// <summary>
    /// The catalog query for existing tables whose name starts with the prefix. It must read the provider's
    /// data dictionary — a failed <c>SELECT</c> must never turn into "the table is missing, create it".
    /// </summary>
    protected abstract Task<List<string>> ListOwnedTableNamesAsync(string prefix);

    /// <summary>Drops one table this run created. The name is always this run's prefix plus a fixed suffix.</summary>
    protected abstract Task DropOwnedTableAsync(string tableName);

    /// <summary>The provider's own prerequisite checks: server version, instance initialization parameters.</summary>
    protected abstract Task CheckServerPrerequisitesAsync();

    protected string ConnectionString => _connectionString;

    protected string Prefix => _prefix;

    public async Task InitializeAsync()
    {
        _connectionString = AcceptanceServers.ConnectionString(Key);

        if (_connectionString.Length == 0)
        {
            return;
        }

        _prefix = TestTablePrefix.Create();
        _schema = SchemaName;

        await ExecuteInstallScriptAsync();

        var options = Options.Create(new OpenIddictStoreOptions { TablePrefix = _prefix, Schema = _schema });
        options.Value.Providers.Add(Provider);

        var factory = new DelegateConnectionFactory(CreateConnection);

        _applications = new OpenIddictStoreApplicationStore<OpenIddictStoreApplication>(
            options, factory, Dialect,
            NullLogger<OpenIddictStoreApplicationStore<OpenIddictStoreApplication>>.Instance);
        _scopes = new OpenIddictStoreScopeStore<OpenIddictStoreScope>(
            options, factory, Dialect,
            NullLogger<OpenIddictStoreScopeStore<OpenIddictStoreScope>>.Instance);
        _authorizations = new OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization>(
            options, factory, Dialect,
            NullLogger<OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization>>.Instance);
        _tokens = new OpenIddictStoreTokenStore<OpenIddictStoreToken>(
            options, factory, Dialect,
            NullLogger<OpenIddictStoreTokenStore<OpenIddictStoreToken>>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (_prefix.Length == 0 || _connectionString.Length == 0)
        {
            return;
        }

        // Refuse to drop anything this fixture did not generate: a prefix that fails the ownership check -
        // hand-written, from a previous run's field, anything - must be left alone.
        if (!TestTablePrefix.IsOwnedByThisTestRun(_prefix))
        {
            return;
        }

        // List first, drop second: a catalog that cannot be read means the drops cannot be proven safe, so
        // the failure surfaces instead of a guessed delete running. Only names that appear both in the
        // catalog and in this run's owned set are dropped.
        var existing = new HashSet<string>(
            await ListOwnedTableNamesAsync(_prefix), StringComparer.Ordinal);

        foreach (var suffix in TestTablePrefix.DropOrder)
        {
            var name = _prefix + suffix;

            if (existing.Contains(name))
            {
                await DropOwnedTableAsync(name);
            }
        }
    }

    /// <summary>Runs the rendered deployment copy the way a deployment would: render, then one statement per command.</summary>
    protected async Task ExecuteInstallScriptAsync()
    {
        var script = RenderInstallScript(_prefix);

        await using var connection = CreateConnection();
        await connection.OpenAsync();

        foreach (var statement in OpenIddictStoreIdentifiers.SplitStatements(script))
        {
            await using var command = connection.CreateCommand();
            // The semicolon is a client-side terminator (SQL*Plus, DISQL), not part of the SQL; servers
            // reached through an ADO.NET statement loop — Oracle above all — reject it in the text.
            command.CommandText = statement.TrimEnd().TrimEnd(';').TrimEnd();

            try
            {
                await command.ExecuteNonQueryAsync();
            }
            // The install has no transaction (DM/Oracle commit DDL implicitly), so the failing statement
            // must be identifiable without re-running anything: name it in the exception itself.
            catch (DbException exception)
            {
                throw new InvalidOperationException(
                    $"The install script failed on the statement:\n{statement}", exception);
            }
        }
    }

    /// <summary>Runs a single statement against a fresh connection.</summary>
    protected async Task ExecuteAsync(string sql)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    protected async Task<List<T>> QueryScalarsAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        var results = new List<T>();

        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        ConfigureCommand(command);
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = Dialect.ParameterPrefix + name;
            parameter.Value = value;
            ConfigureParameter(parameter, name, value);
            command.Parameters.Add(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            // GetFieldValue<T> is strict about the exact CLR type, but the providers disagree on integers:
            // Oracle hands back NUMBER as Int64, DM may hand back decimal. Convert when the native type
            // differs instead of letting the strict path decide the acceptance outcome.
            var value = reader.GetValue(0);

            results.Add(value is T typed
                ? typed
                : (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture));
        }

        return results;
    }

    /// <summary>Quotes a logical table name with its schema qualifier when one is configured.</summary>
    protected string QualifiedTable(string logicalName)
        => string.IsNullOrEmpty(_schema)
            ? Dialect.QuoteIdentifier(logicalName)
            : Dialect.QuoteIdentifier(_schema!) + "." + Dialect.QuoteIdentifier(logicalName);

    /// <summary>Quotes a column or table identifier the way the runtime does.</summary>
    protected string Q(string identifier) => Dialect.QuoteIdentifier(identifier);

    /// <summary>
    /// Hook for provider-specific parameter binding. The Oracle fixture overrides it because ODP.NET binds a
    /// .NET string as VARCHAR2, while the schema's columns are NVARCHAR2: the store never notices (every
    /// exact comparison wraps both sides in NLSSORT), but a hand-written predicate would hit ORA-12704.
    /// </summary>
    protected virtual void ConfigureParameter(DbParameter parameter, string name, object value)
    {
    }

    /// <summary>
    /// Hook for provider-specific command configuration. The Oracle fixture overrides it to enable
    /// bind-by-name: ODP.NET binds by position by default, which scrambles any statement whose parameters
    /// appear in a different order than they are added.
    /// </summary>
    protected virtual void ConfigureCommand(DbCommand command)
    {
    }

    // ---------------------------------------------------------------------
    // Acceptance facts. Each is declared by a concrete suite with its own
    // AcceptanceFact, and holds every supported provider to the same contract.
    // ---------------------------------------------------------------------

    protected async Task Install_CreatesTheNineTablesAndTheVersionRowCore()
    {
        var tables = await ListOwnedTableNamesAsync(_prefix);

        Assert.Equal(
            TestTablePrefix.OwnedTableNames(_prefix).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            tables.OrderBy(name => name, StringComparer.Ordinal).ToArray());

        Assert.Equal([1], await QueryScalarsAsync<int>(
            $"SELECT {Q("Version")} FROM {QualifiedTable(_prefix + "SchemaVersions")} WHERE {Q("Series")} = '1'"));
    }

    protected async Task ReRunningTheInstallScript_IsRefusedInsteadOfSkippedCore()
    {
        // The script has no IF EXISTS skip, so the second run must fail on the first duplicate object. The
        // driver-specific wording varies; what must hold is that the re-run does not succeed quietly.
        var exception = await Record.ExceptionAsync(ExecuteInstallScriptAsync);

        Assert.NotNull(exception);
    }

    protected async Task Application_RoundTripsUnicodeLargeValuesAndCollectionsCore()
    {
        var payload = new string('π', 50_000);

        var application = new OpenIddictStoreApplication
        {
            ClientId = "client-über",
            DisplayName = "Anzeigename für Ünïcode über 50 000 Zeichen",
            ClientSecret = payload,
        };

        await _applications.SetPermissionsAsync(application, ["ept:token", "gt:client_credentials"], default);
        await _applications.SetRedirectUrisAsync(application, ["https://example.test/cb", "https://example.test/cb2"], default);
        await _applications.CreateAsync(application, default);

        Assert.Equal("Anzeigename für Ünïcode über 50 000 Zeichen",
            await _applications.GetDisplayNameAsync(
                (await _applications.FindByClientIdAsync("client-über", default))!, default));

        var loaded = await _applications.FindByClientIdAsync("client-über", default);

        Assert.NotNull(loaded);
        Assert.Equal(payload.Length, (await _applications.GetClientSecretAsync(loaded!, default))!.Length);
        Assert.Equal(["ept:token", "gt:client_credentials"],
            (await _applications.GetPermissionsAsync(loaded!, default)).ToArray());
        Assert.Equal(["https://example.test/cb", "https://example.test/cb2"],
            (await _applications.GetRedirectUrisAsync(loaded!, default)).ToArray());

        var byUri = new List<OpenIddictStoreApplication>();

        await foreach (var candidate in _applications.FindByRedirectUriAsync("https://example.test/cb2", default))
        {
            byUri.Add(candidate);
        }

        Assert.Single(byUri);
        Assert.Equal(application.Id, byUri[0].Id);
    }

    protected async Task Dates_KeepMicrosecondPrecisionAcrossCreateAndReadCore()
    {
        var created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(1234560);

        var token = new OpenIddictStoreToken
        {
            CreationDate = created,
            ExpirationDate = created.AddHours(1),
            RedemptionDate = created.AddMinutes(1),
            Status = "valid",
            Type = "access_token",
        };

        await _tokens.CreateAsync(token, default);

        var loaded = await _tokens.FindByIdAsync(token.Id!, default);

        Assert.NotNull(loaded);
        Assert.Equal(created, await _tokens.GetCreationDateAsync(loaded!, default));
        Assert.Equal(created.AddHours(1), await _tokens.GetExpirationDateAsync(loaded!, default));
        Assert.Equal(created.AddMinutes(1), await _tokens.GetRedemptionDateAsync(loaded!, default));
    }

    protected async Task ExactComparison_DistinguishesCaseAndAccentsCore()
    {
        await _applications.CreateAsync(new OpenIddictStoreApplication { ClientId = "client" }, default);
        await _applications.CreateAsync(new OpenIddictStoreApplication { ClientId = "Client" }, default);
        await _applications.CreateAsync(new OpenIddictStoreApplication { ClientId = "clíent" }, default);

        Assert.NotNull(await _applications.FindByClientIdAsync("client", default));
        Assert.NotNull(await _applications.FindByClientIdAsync("Client", default));
        Assert.NotNull(await _applications.FindByClientIdAsync("clíent", default));

        Assert.NotEqual(
            (await _applications.FindByClientIdAsync("client", default))!.Id,
            (await _applications.FindByClientIdAsync("Client", default))!.Id);

        Assert.Null(await _applications.FindByClientIdAsync("CLIENT", default));
        Assert.Null(await _applications.FindByClientIdAsync("clien", default));
    }

    protected async Task ExactComparison_RejectsATrailingSpaceVariantWhenTheStoredValueHasNoneCore()
    {
        await _applications.CreateAsync(new OpenIddictStoreApplication { ClientId = "spaced" }, default);

        Assert.Null(await _applications.FindByClientIdAsync("spaced ", default));
        Assert.NotNull(await _applications.FindByClientIdAsync("spaced", default));
    }

    protected async Task UniqueIndex_RejectsASecondRowWithTheSameClientIdCore()
    {
        await _applications.CreateAsync(new OpenIddictStoreApplication { ClientId = "unique" }, default);

        var exception = await Record.ExceptionAsync(
            () => _applications.CreateAsync(new OpenIddictStoreApplication { ClientId = "unique" }, default).AsTask());

        Assert.NotNull(exception);

        // Many rows without a ClientId must all be accepted: the unique key treats NULLs as distinct.
        await _applications.CreateAsync(new OpenIddictStoreApplication(), default);
        await _applications.CreateAsync(new OpenIddictStoreApplication(), default);
    }

    protected async Task Update_RejectsAStaleConcurrencyTokenCore()
    {
        var application = new OpenIddictStoreApplication { ClientId = "cas" };
        await _applications.SetRedirectUrisAsync(application, ["https://a.test/cb"], default);
        await _applications.CreateAsync(application, default);

        var winner = await _applications.FindByIdAsync(application.Id!, default);
        var winnerToken = winner!.ConcurrencyToken;
        await _applications.SetRedirectUrisAsync(winner, ["https://winner.test/cb"], default);
        await _applications.UpdateAsync(winner, default);

        // A second snapshot taken before the winner's update still carries the old token. It has to be
        // rebuilt from the winner object so the token really is stale, not the freshly rotated one.
        var stale = new OpenIddictStoreApplication
        {
            Id = application.Id,
            ConcurrencyToken = winnerToken,
            ClientId = "cas",
        };
        await _applications.SetRedirectUrisAsync(stale, ["https://stale.test/cb"], default);

        await Assert.ThrowsAsync<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
            async () => await _applications.UpdateAsync(stale, default));

        var reread = await _applications.FindByIdAsync(application.Id!, default);
        Assert.Equal(["https://winner.test/cb"],
            (await _applications.GetRedirectUrisAsync(reread!, default)).ToArray());
    }

    protected async Task Paging_TilesTheWholeSetAndAcceptsAnOffsetWithoutACountCore()
    {
        for (var index = 0; index < 7; index++)
        {
            await _scopes.CreateAsync(
                new OpenIddictStoreScope { Name = "scope-" + index.ToString("D2") }, default);
        }

        // ListAsync orders by Id, so the expected page is the Id-ordered slice. Asserting the names would
        // couple this test to generated GUIDs; instead, prove the pages tile the whole set in a stable order.
        var everything = new List<string>();

        await foreach (var scope in _scopes.ListAsync(null, null, default))
        {
            everything.Add(scope.Id!);
        }

        Assert.Equal(7, everything.Count);

        var paged = new List<string>();

        await foreach (var scope in _scopes.ListAsync(3, 2, default))
        {
            paged.Add(scope.Id!);
        }

        Assert.Equal(everything.Skip(2).Take(3), paged);

        // An offset without a count is the "everything after this point" shape; each dialect renders it in
        // its own form and the server must accept it.
        var rest = new List<string>();

        await foreach (var scope in _scopes.ListAsync(null, 5, default))
        {
            rest.Add(scope.Id!);
        }

        Assert.Equal(everything.Skip(5), rest);
    }

    protected async Task CountQuery_CountsTheRowsCore()
    {
        for (var index = 0; index < 4; index++)
        {
            await _scopes.CreateAsync(new OpenIddictStoreScope { Name = "counted-" + index }, default);
        }

        Assert.Equal(4, await _scopes.CountAsync(scopes => scopes, default));
    }

    protected async Task ScopeResources_StaysCaseExactAndTheJsonStaysAuthoritativeCore()
    {
        var scope = new OpenIddictStoreScope { Name = "api" };
        await _scopes.SetResourcesAsync(scope, ["https://rs1", "https://rs2"], default);
        await _scopes.CreateAsync(scope, default);

        var found = new List<OpenIddictStoreScope>();

        await foreach (var candidate in _scopes.FindByResourceAsync("https://rs2", default))
        {
            found.Add(candidate);
        }

        Assert.Single(found);
        Assert.Equal(["https://rs1", "https://rs2"],
            (await _scopes.GetResourcesAsync(found[0], default)).ToArray());

        var variant = new List<OpenIddictStoreScope>();

        await foreach (var candidate in _scopes.FindByResourceAsync("https://RS2", default))
        {
            variant.Add(candidate);
        }

        Assert.Empty(variant);
    }

    protected async Task AssociatedDelete_RemovesTokensReachableOnlyThroughTheAuthorizationCore()
    {
        var application = new OpenIddictStoreApplication { ClientId = "delete-me" };
        await _applications.CreateAsync(application, default);

        var authorization = new OpenIddictStoreAuthorization
        {
            ApplicationId = application.Id,
            Status = "valid",
            Type = "permanent",
            Subject = "alice",
        };
        await _authorizations.CreateAsync(authorization, default);

        // ApplicationId stays null: the token hangs off the authorization only, so a delete that looked
        // at ApplicationId alone would leave an orphan that the foreign key would then block.
        var token = new OpenIddictStoreToken
        {
            AuthorizationId = authorization.Id,
            Status = "valid",
            Type = "access_token",
        };
        await _tokens.CreateAsync(token, default);

        await _applications.DeleteAsync(application, default);

        Assert.Null(await _tokens.FindByIdAsync(token.Id!, default));
        Assert.Null(await _authorizations.FindByIdAsync(authorization.Id!, default));
        Assert.Null(await _applications.FindByIdAsync(application.Id!, default));
    }

    protected async Task BulkRevocation_TreatsANullStatusAsNotYetRevokedCore()
    {
        var valid = await CreateTokenAsync("alice", "valid");
        var alreadyRevoked = await CreateTokenAsync("alice", "revoked");
        var withoutStatus = await CreateTokenAsync("alice", null);
        var upper = await CreateTokenAsync("Alice", "valid");

        // A plain <> yields UNKNOWN for NULL and would silently skip that row; the exact-not-equals keeps it.
        Assert.Equal(2, await _tokens.RevokeBySubjectAsync("alice", default));

        // Read the store, not the in-memory entities: only alice's exact-case rows may have been revoked,
        // and the already-revoked row must never be matched twice. The null marker is a bound parameter, not
        // a literal: a VARCHAR2 literal next to an NVARCHAR2 column is a character-set mismatch on Oracle
        // (ORA-12704), while a bound string matches the column's national charset everywhere.
        Assert.Equal(["revoked", "revoked", "revoked"], (await QueryScalarsAsync<string>(
            $"SELECT CASE WHEN {Q("Status")} IS NULL THEN {Dialect.ParameterPrefix}nullmark ELSE {Q("Status")} END " +
            $"FROM {QualifiedTable(_prefix + "Tokens")} " +
            $"WHERE {Q("Subject")} = {Dialect.ParameterPrefix}subject ORDER BY {Q("Id")}",
            ("subject", "alice"),
            ("nullmark", "<null>"))).ToArray());

        Assert.Equal("revoked", await ReadStatusAsync(valid.Id!));
        Assert.Equal("revoked", await ReadStatusAsync(alreadyRevoked.Id!));
        Assert.Equal("revoked", await ReadStatusAsync(withoutStatus.Id!));
        Assert.Equal("valid", await ReadStatusAsync(upper.Id!));

        Assert.Equal(0, await _tokens.RevokeBySubjectAsync("alice", default));
    }

    protected async Task Prune_RemovesOnlyRowsBeforeTheThresholdCore()
    {
        var threshold = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Prune requires the CreationDate itself to be before the threshold, so the date has to be set
        // at insert time; updating only ExpirationDate would leave the row outside the predicate.
        var expired = new OpenIddictStoreToken
        {
            Subject = "bob",
            Status = "valid",
            Type = "access_token",
            CreationDate = threshold.AddDays(-2).UtcDateTime,
            ExpirationDate = threshold.AddDays(-1).UtcDateTime,
        };
        await _tokens.CreateAsync(expired, default);

        var future = new OpenIddictStoreToken
        {
            Subject = "bob",
            Status = "valid",
            Type = "access_token",
            CreationDate = threshold.AddDays(1).UtcDateTime,
            ExpirationDate = threshold.AddDays(2).UtcDateTime,
        };
        await _tokens.CreateAsync(future, default);

        await _tokens.PruneAsync(threshold, default);

        Assert.Null(await _tokens.FindByIdAsync(expired.Id!, default));
        Assert.NotNull(await _tokens.FindByIdAsync(future.Id!, default));
    }

    protected async Task<OpenIddictStoreToken> CreateTokenAsync(string subject, string? status)
    {
        var token = new OpenIddictStoreToken
        {
            Subject = subject,
            Status = status,
            Type = "access_token",
            CreationDate = DateTime.UtcNow,
        };

        await _tokens.CreateAsync(token, default);

        return token;
    }

    /// <summary>Reads one status straight from the database, bypassing the entity held in memory.</summary>
    protected async Task<string> ReadStatusAsync(string id)
    {
        var values = await QueryScalarsAsync<string>(
            $"SELECT CASE WHEN {Q("Status")} IS NULL THEN {Dialect.ParameterPrefix}nullmark ELSE {Q("Status")} END " +
            $"FROM {QualifiedTable(_prefix + "Tokens")} WHERE {Q("Id")} = {Dialect.ParameterPrefix}id",
            ("id", id),
            ("nullmark", "<null>"));

        return Assert.Single(values);
    }

    private sealed class DelegateConnectionFactory : IOpenIddictStoreConnectionFactory
    {
        private readonly Func<DbConnection> _factory;

        public DelegateConnectionFactory(Func<DbConnection> factory) => _factory = factory;

        public ValueTask<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken)
            => new(_factory());
    }
}

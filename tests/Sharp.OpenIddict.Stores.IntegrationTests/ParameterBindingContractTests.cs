using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using Sharp.OpenIddict.Stores.Dm;
using Sharp.OpenIddict.Stores.Models;
using Sharp.OpenIddict.Stores.MySql;
using Sharp.OpenIddict.Stores.Oracle;
using Sharp.OpenIddict.Stores.PostgreSql;
using Sharp.OpenIddict.Stores.Sqlite;
using Sharp.OpenIddict.Stores.SqlServer;
using Sharp.OpenIddict.Stores.Stores;
using Xunit;

namespace Sharp.OpenIddict.Stores.IntegrationTests;

public sealed class ParameterBindingContractTests
{
    [Theory]
    [InlineData("oracle", ":")]
    [InlineData("dm-native", ":")]
    [InlineData("dm-oracle", ":")]
    [InlineData("dm-mysql", ":")]
    [InlineData("sqlite", "@")]
    [InlineData("sqlserver", "@")]
    [InlineData("postgresql", "@")]
    [InlineData("mysql", "@")]
    public async Task StoreCommands_UseTheSelectedDialectsParameters(string provider, string prefix)
    {
        IOpenIddictStoreDialect dialect = provider switch
        {
            "oracle" => OpenIddictStoreOracleDialect.Instance,
            "dm-native" => OpenIddictStoreDmDialect.Instance,
            "dm-oracle" => OpenIddictStoreDmOracleCompatibleDialect.Instance,
            "dm-mysql" => OpenIddictStoreDmMySqlCompatibleDialect.Instance,
            "sqlite" => OpenIddictStoreSqliteDialect.Instance,
            "sqlserver" => OpenIddictStoreSqlServerDialect.Instance,
            "postgresql" => OpenIddictStorePostgreSqlDialect.Instance,
            "mysql" => OpenIddictStoreMySqlDialect.Instance,
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
        var factory = new RecordingFactory();
        var options = Options.Create(new OpenIddictStoreOptions());
        var applications = new OpenIddictStoreApplicationStore<OpenIddictStoreApplication>(
            options, factory, dialect, NullLogger<OpenIddictStoreApplicationStore<OpenIddictStoreApplication>>.Instance);
        var authorizations = new OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization>(
            options, factory, dialect, NullLogger<OpenIddictStoreAuthorizationStore<OpenIddictStoreAuthorization>>.Instance);
        var scopes = new OpenIddictStoreScopeStore<OpenIddictStoreScope>(
            options, factory, dialect, NullLogger<OpenIddictStoreScopeStore<OpenIddictStoreScope>>.Instance);
        var tokens = new OpenIddictStoreTokenStore<OpenIddictStoreToken>(
            options, factory, dialect, NullLogger<OpenIddictStoreTokenStore<OpenIddictStoreToken>>.Instance);
        var application = new OpenIddictStoreApplication
        {
            RedirectUris = "[\"https://example.org/a@b\"]", PostLogoutRedirectUris = "[\"https://example.org/logout\"]",
        };
        var authorization = new OpenIddictStoreAuthorization { Id = "authorization", Scopes = "[\"openid\"]" };
        var scope = new OpenIddictStoreScope { Name = "openid", Resources = "[\"resource\"]" };
        var token = new OpenIddictStoreToken { Subject = "alice@example.org" };

        await applications.CreateAsync(application, default);
        await authorizations.CreateAsync(authorization, default);
        await scopes.CreateAsync(scope, default);
        await tokens.CreateAsync(token, default);
        await applications.UpdateAsync(application, default);
        await authorizations.UpdateAsync(authorization, default);
        await scopes.UpdateAsync(scope, default);
        await tokens.UpdateAsync(token, default);

        await DrainAsync(applications.FindByRedirectUriAsync("https://example.org/a@b", default));
        await DrainAsync(scopes.FindByResourceAsync("resource", default));
        await DrainAsync(scopes.FindByNamesAsync(ImmutableArray.Create("openid", "profile"), default));
        await DrainAsync(authorizations.FindAsync(null, null, null, null, ImmutableArray.Create("openid"), default));

        var cutoff = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var names = new[] { "alice@example.org" };
        await tokens.CountAsync(query => query.Where(t => t.Subject == "alice@example.org" && t.CreationDate > cutoff), default);
        await tokens.CountAsync(query => query.Where(t => Enumerable.Contains(names, t.Subject)), default);
        var dates = new DateTimeOffset?[] { new DateTimeOffset(cutoff) };
        await tokens.CountAsync(query => query.Where(t => Enumerable.Contains(dates, (DateTimeOffset?)t.CreationDate)), default);

        await tokens.RevokeAsync("subject", "client", "valid", "type", default);
        await authorizations.RevokeAsync("subject", "client", "valid", "type", default);
        await tokens.PruneAsync(DateTimeOffset.UtcNow, default);
        await authorizations.PruneAsync(DateTimeOffset.UtcNow, default);
        await tokens.DeleteAsync(token, default);
        await authorizations.DeleteAsync(authorization, default);
        await scopes.DeleteAsync(scope, default);
        await applications.DeleteAsync(application, default);

        var parameterized = factory.Commands.Where(command => command.Parameters.Count > 0).ToList();
        Assert.NotEmpty(parameterized);
        foreach (var command in parameterized)
        {
            var placeholders = Regex.Matches(command.Sql, @"[@:][A-Za-z_][A-Za-z0-9_]*")
                .Select(match => match.Value).ToList();
            Assert.NotEmpty(placeholders);
            foreach (var placeholder in placeholders)
            {
                Assert.StartsWith(prefix, placeholder, StringComparison.Ordinal);
                Assert.True(command.Parameters.ContainsKey(placeholder[1..]), command.Sql);
            }
        }
        Assert.Contains(parameterized, command => command.Parameters.Values.Contains("alice@example.org"));
        Assert.Contains(parameterized, command => command.Parameters.Values.Contains("https://example.org/a@b"));
    }

    [Fact]
    public void OracleCommand_BindsReorderedAndRepeatedParametersByName()
    {
        using var connection = new OracleConnection();
        using var command = OpenIddictStoreCommands.Create(connection,
            "SELECT :second, :first, :second FROM DUAL",
            new Dictionary<string, object?> { ["first"] = "one", ["second"] = "two" },
            null, null, default);

        Assert.True(Assert.IsType<OracleCommand>(command).BindByName);
        Assert.Equal("one", command.Parameters["first"].Value);
        Assert.Equal("two", command.Parameters["second"].Value);
        Assert.Equal(2, command.Parameters.Count);
    }

    private static async Task DrainAsync<T>(IAsyncEnumerable<T> source)
    {
        await foreach (var _ in source) { }
    }

    private sealed record RecordedCommand(string Sql, Dictionary<string, object?> Parameters);

    // Only the server boundary is replaced: the real stores, dialects and command binder build every command.
    private sealed class RecordingFactory : IOpenIddictStoreConnectionFactory
    {
        public List<RecordedCommand> Commands { get; } = [];

        public ValueTask<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken)
            => new(new RecordingConnection(this));
    }

    private sealed class RecordingConnection(RecordingFactory factory) : DbConnection
    {
        private ConnectionState _state;
        [AllowNull] public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "recording";
        public override string DataSource => "recording";
        public override string ServerVersion => "1";
        public override ConnectionState State => _state;
        public override void Open() => _state = ConnectionState.Open;
        public override void Close() => _state = ConnectionState.Closed;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new RecordingCommand(this, factory);
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new RecordingTransaction(this);
    }

    private sealed class RecordingTransaction(DbConnection connection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection DbConnection => connection;
        public override void Commit() { }
        public override void Rollback() { }
    }

    private sealed class RecordingCommand(DbConnection connection, RecordingFactory factory) : DbCommand
    {
        private readonly SqliteCommand _parameters = new();
        [AllowNull] public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters.Parameters;
        protected override DbParameter CreateDbParameter() => new SqliteParameter();
        public override void Cancel() { }
        public override void Prepare() { }

        private void Record() => factory.Commands.Add(new RecordedCommand(CommandText,
            Parameters.Cast<DbParameter>().ToDictionary(parameter => parameter.ParameterName, parameter => (object?)parameter.Value)));

        public override int ExecuteNonQuery()
        {
            Record();
            return Parameters.Contains("threshold") ? 0 : 1;
        }

        public override object ExecuteScalar() { Record(); return 0L; }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            Record();
            var table = new DataTable();
            if (CommandText.Contains("SchemaVersions", StringComparison.Ordinal))
            {
                table.Columns.Add("Series", typeof(string));
                table.Columns.Add("Version", typeof(int));
                table.Rows.Add("1", 1);
            }
            else if (CommandText.Contains("SELECT DISTINCT a.", StringComparison.Ordinal))
            {
                table.Columns.Add("Id", typeof(string));
                table.Rows.Add("authorization");
            }
            return table.CreateDataReader();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _parameters.Dispose();
            base.Dispose(disposing);
        }
    }
}

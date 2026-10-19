using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Models;
using Xunit;

namespace Sharp.OpenIddict.Stores.Tests;

/// <summary>
/// Registration and options contract from design 6/14.2: native entity/Store registration, idempotency,
/// rejecting invalid configuration before use, and rejecting any attempt to re-enable entity caching regardless
/// of configuration order.
/// </summary>
public sealed class RegistrationTests
{
    [Fact]
    public void UseDapper_RegistersDefaultEntitiesAndDisablesEntityCaching()
    {
        var services = new ServiceCollection();

        services.AddOpenIddict().AddCore(options => options.UseDapper(dapper => dapper
            .UseSqlite()
            .UseConnectionFactory(_ => new FakeConnection())));

        using var provider = services.BuildServiceProvider();

        var core = provider.GetRequiredService<IOptions<OpenIddictCoreOptions>>().Value;
        Assert.True(core.DisableEntityCaching);
        Assert.False(core.DisableAdditionalFiltering);

        // SetDefault*Entity replaces the native non-generic manager interfaces with a factory delegate; assert that native shape here.
        foreach (var serviceType in new[]
        {
            typeof(IOpenIddictApplicationManager),
            typeof(IOpenIddictAuthorizationManager),
            typeof(IOpenIddictScopeManager),
            typeof(IOpenIddictTokenManager),
        })
        {
            Assert.Contains(services, service =>
                service.ServiceType == serviceType && service.ImplementationFactory is not null);
        }
    }

    [Fact]
    public void UseDapper_IsIdempotent()
    {
        var services = new ServiceCollection();

        services.AddOpenIddict().AddCore(options =>
        {
            options.UseDapper(dapper => dapper.UseSqlite().UseConnectionFactory(_ => new FakeConnection()));
            options.UseDapper(dapper => dapper.UseSqlite().UseConnectionFactory(_ => new FakeConnection()));
        });

        using var provider = services.BuildServiceProvider();

        Assert.Single(services, service =>
            service.ServiceType == typeof(IValidateOptions<OpenIddictStoreOptions>));
        Assert.Single(services, service =>
            service.ServiceType == typeof(IOpenIddictStoreConnectionFactory));

        // Repeated registration still resolves and does not produce a duplicate options validation failure.
        Assert.NotNull(provider.GetRequiredService<IOptions<OpenIddictStoreOptions>>().Value);
    }

    [Fact]
    public void MultipleProviders_FailBeforeUse()
    {
        var services = new ServiceCollection();

        services.AddOpenIddict().AddCore(options => options.UseDapper(dapper => dapper
            .UseSqlite()
            .UseProvider(OpenIddictStoreProvider.PostgreSql)
            .UseConnectionFactory(_ => new FakeConnection())));

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<OpenIddictStoreOptions>>().Value);

        Assert.Contains("Multiple providers were selected", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingProvider_FailsBeforeUse()
    {
        var services = new ServiceCollection();

        services.AddOpenIddict().AddCore(options => options.UseDapper(dapper => dapper
            .UseConnectionFactory(_ => new FakeConnection())));

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<OpenIddictStoreOptions>>().Value);

        Assert.Contains("No database provider was selected", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingConnectionFactory_FailsBeforeUse()
    {
        var services = new ServiceCollection();

        services.AddOpenIddict().AddCore(options => options.UseDapper(dapper => dapper.UseSqlite()));

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<OpenIddictStoreOptions>>().Value);

        Assert.Contains("No connection factory was configured", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidTablePrefixAndTimeoutAndSchema_AreRejected()
    {
        AssertRejected(dapper => dapper.UseSqlite().UseConnectionFactory(_ => new FakeConnection())
            .SetTablePrefix("1abc"), "table prefix");
        AssertRejected(dapper => dapper.UseSqlite().UseConnectionFactory(_ => new FakeConnection())
            .SetTablePrefix("has-dash"), "table prefix");
        AssertRejected(dapper => dapper.UseSqlite().UseConnectionFactory(_ => new FakeConnection())
            .SetCommandTimeout(0), "command timeout");
        AssertRejected(dapper => dapper.UseSqlite().UseConnectionFactory(_ => new FakeConnection())
            .SetCommandTimeout(-5), "command timeout");
        AssertRejected(dapper => dapper.UseProvider(OpenIddictStoreProvider.PostgreSql).UseConnectionFactory(_ => new FakeConnection())
            .SetSchema("bad schema"), "schema");
        AssertRejected(dapper => dapper.UseSqlite().UseConnectionFactory(_ => new FakeConnection())
            .SetSchema("public"), "do not support dedicated schema");
        AssertRejected(dapper => dapper.UseProvider(OpenIddictStoreProvider.MySql).UseConnectionFactory(_ => new FakeConnection())
            .SetSchema("public"), "do not support dedicated schema");
    }

    [Fact]
    public void ValidSchema_IsAcceptedWhereTheProviderSupportsIt()
    {
        var services = new ServiceCollection();

        services.AddOpenIddict().AddCore(options => options.UseDapper(dapper => dapper
            .UseProvider(OpenIddictStoreProvider.PostgreSql)
            .UseConnectionFactory(_ => new FakeConnection())
            .SetSchema("openiddict")));

        using var provider = services.BuildServiceProvider();

        Assert.Equal("openiddict", provider.GetRequiredService<IOptions<OpenIddictStoreOptions>>().Value.Schema);
    }

    [Fact]
    public void ReEnablingEntityCaching_IsRejectedRegardlessOfOrder()
    {
        // Enabling caching after UseDapper: the effective value ends up enabled, so it must be rejected.
        AssertCacheRejected(options =>
        {
            options.UseDapper(dapper => dapper.UseSqlite().UseConnectionFactory(_ => new FakeConnection()));
            options.Configure(core => core.DisableEntityCaching = false);
        });

        // Disable first, then UseDapper, then re-enable: the effective value is still enabled, so it must be rejected.
        AssertCacheRejected(options =>
        {
            options.Configure(core => core.DisableEntityCaching = true);
            options.UseDapper(dapper => dapper.UseSqlite().UseConnectionFactory(_ => new FakeConnection()));
            options.Configure(core => core.DisableEntityCaching = false);
        });
    }

    [Fact]
    public void DisablingEntityCachingBeforeUseDapper_IsAccepted()
    {
        var services = new ServiceCollection();

        services.AddOpenIddict().AddCore(options =>
        {
            options.Configure(core => core.DisableEntityCaching = true);
            options.UseDapper(dapper => dapper.UseSqlite().UseConnectionFactory(_ => new FakeConnection()));
        });

        using var provider = services.BuildServiceProvider();

        Assert.True(provider.GetRequiredService<IOptions<OpenIddictCoreOptions>>().Value.DisableEntityCaching);
    }

    [Fact]
    public void ConnectionFactoryType_IsResolvedFromContainer()
    {
        var services = new ServiceCollection();

        services.AddOpenIddict().AddCore(options => options.UseDapper(dapper => dapper
            .UseSqlite()
            .UseConnectionFactory<TrackingConnectionFactory>()));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<IOpenIddictStoreConnectionFactory>();
        Assert.IsType<TrackingConnectionFactory>(factory);
    }

    [Fact]
    public async Task DelegateConnectionFactory_IsInvokedWithServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddOpenIddict().AddCore(options => options.UseDapper(dapper => dapper
            .UseSqlite()
            .UseConnectionFactory(_ => new FakeConnection())));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var factory = scope.ServiceProvider.GetRequiredService<IOpenIddictStoreConnectionFactory>();
        var connection = await factory.CreateConnectionAsync(default);

        Assert.IsType<FakeConnection>(connection);
    }

    private static void AssertRejected(Action<OpenIddictStoreBuilder> configure, string expected)
    {
        var services = new ServiceCollection();

        services.AddOpenIddict().AddCore(options => options.UseDapper(configure));

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<OpenIddictStoreOptions>>().Value);

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    private static void AssertCacheRejected(Action<OpenIddictCoreBuilder> configure)
    {
        var services = new ServiceCollection();

        services.AddOpenIddict().AddCore(configure);

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<OpenIddictCoreOptions>>().Value);

        Assert.Contains("OpenIddict entity caching to be disabled", exception.Message, StringComparison.Ordinal);
    }

    private sealed class TrackingConnectionFactory : IOpenIddictStoreConnectionFactory
    {
        public ValueTask<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken)
            => new(new FakeConnection());
    }

    private sealed class FakeConnection : DbConnection
    {
        private string _connectionString = string.Empty;

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }

        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "1.0";
        public override System.Data.ConnectionState State => System.Data.ConnectionState.Closed;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Close() { }
        public override void Open() => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel)
            => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}

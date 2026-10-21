using System;
using Sharp.OpenIddict.Stores.SqlServer;
using Xunit;

namespace Sharp.OpenIddict.Stores.Tests;

/// <summary>
/// SQL Server dialect checks that need no server: the identifier-length rejection every other bounded
/// dialect applies must hold here too, so an over-long table prefix fails with a clear ArgumentException
/// at first use instead of a raw SqlException at runtime (design 15.4).
/// </summary>
public sealed class SqlServerDialectTests
{
    [Fact]
    public void QuoteIdentifier_RejectsAnIdentifierBeyondTheLimit()
    {
        var dialect = OpenIddictStoreSqlServerDialect.Instance;

        var identifier = new string('a', dialect.MaximumIdentifierLength + 1);

        Assert.Throws<ArgumentException>(() => dialect.QuoteIdentifier(identifier));
    }

    [Fact]
    public void QuoteIdentifier_AcceptsTheLongestLegalIdentifier()
    {
        var dialect = OpenIddictStoreSqlServerDialect.Instance;

        var identifier = new string('a', dialect.MaximumIdentifierLength);

        Assert.Equal("[" + identifier + "]", dialect.QuoteIdentifier(identifier));
    }

    [Fact]
    public void EmptyStringBindsAsNull_IsFalse()
        => Assert.False(OpenIddictStoreSqlServerDialect.Instance.EmptyStringBindsAsNull);
}

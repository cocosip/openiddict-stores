using System;
using System.Linq;
using System.Linq.Expressions;
using Sharp.OpenIddict.Stores;
using Sharp.OpenIddict.Stores.Models;
using Sharp.OpenIddict.Stores.MySql;
using Sharp.OpenIddict.Stores.Oracle;
using Sharp.OpenIddict.Stores.PostgreSql;
using Sharp.OpenIddict.Stores.Queries;
using Sharp.OpenIddict.Stores.Sqlite;
using Sharp.OpenIddict.Stores.SqlServer;
using Xunit;

namespace Sharp.OpenIddict.Stores.Tests;

/// <summary>
/// Bounded-string equality must translate through the dialect's exact predicate, never a plain
/// <c>=</c>: the column collation is case-insensitive by default on SQL Server and a hand-created
/// table may lack the shipped collation elsewhere, so a plain comparison would match rows that
/// differ in case, accents, or trailing spaces (design 16.1.1). These checks pin the SQL text per
/// dialect because a real collation-sensitive server is not available locally.
/// </summary>
public sealed class PredicateTranslatorSqlTests
{
    private static string Translate(
        IOpenIddictStoreDialect dialect, Expression<Func<OpenIddictStoreToken, bool>> predicate)
    {
        var translator = new OpenIddictStorePredicateTranslator(
            typeof(OpenIddictStoreToken), dialect, column => dialect.QuoteIdentifier(column));

        return translator.Translate(predicate.Body);
    }

    [Fact]
    public void SqlServer_EqualityUsesTheBinaryCollationPredicate()
    {
        var sql = Translate(OpenIddictStoreSqlServerDialect.Instance, token => token.Subject == "alice");

        Assert.Contains("COLLATE Latin1_General_100_BIN2", sql, StringComparison.Ordinal);
        Assert.Contains("DATALENGTH", sql, StringComparison.Ordinal);

        // The comparison must not be a plain = against the column collation.
        Assert.DoesNotContain("[Subject] = @p0", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void MySql_EqualityUsesTheBinaryOperator()
    {
        var sql = Translate(OpenIddictStoreMySqlDialect.Instance, token => token.Subject == "alice");

        Assert.Contains("BINARY @", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Oracle_EqualityUsesTheBinarySort()
    {
        var sql = Translate(OpenIddictStoreOracleDialect.Instance, token => token.Subject == "alice");

        Assert.Contains("NLSSORT", sql, StringComparison.Ordinal);
        Assert.Contains("NLS_SORT=BINARY", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgreSql_EqualityForcesCollationC()
    {
        var sql = Translate(OpenIddictStorePostgreSqlDialect.Instance, token => token.Subject == "alice");

        Assert.Contains("COLLATE \"C\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Sqlite_EqualityStaysAPlainComparison()
    {
        // SQLite compares TEXT with the BINARY collation, so the exact predicate is a plain =.
        var sql = Translate(OpenIddictStoreSqliteDialect.Instance, token => token.Subject == "alice");

        Assert.Contains("\"Subject\" IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("\"Subject\" IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("\"Subject\" = @p0", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlServer_ColumnComparisonUsesTheColumnExactPredicate()
    {
        var sql = Translate(
            OpenIddictStoreSqlServerDialect.Instance, token => token.Subject == token.Status);

        Assert.Contains("COLLATE Latin1_General_100_BIN2", sql, StringComparison.Ordinal);
        Assert.Contains("DATALENGTH([Subject]) = DATALENGTH([Status])", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Inequality_NegatesTheWholeCompensatedPredicate()
    {
        var sql = Translate(OpenIddictStoreSqlServerDialect.Instance, token => token.Subject != "alice");

        Assert.Contains("COLLATE Latin1_General_100_BIN2", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Inequality_NegatesBothOrBranches_NeverJustTheFirst()
    {
        // The equality predicate is an OR of two branches; a NOT that covers only the first branch
        // lets the equal row match the inequality. The whole OR must sit inside the NOT.
        var sql = Translate(OpenIddictStoreSqliteDialect.Instance, token => token.Subject != "alice");

        Assert.StartsWith("NOT ((", sql, StringComparison.Ordinal);

        var comparison = Translate(OpenIddictStoreSqliteDialect.Instance, token => token.Subject != token.Status);

        Assert.StartsWith("NOT ((", comparison, StringComparison.Ordinal);
    }

    [Fact]
    public void Equality_RegistersTheComparedValueAsAParameter()
    {
        var translator = new OpenIddictStorePredicateTranslator(
            typeof(OpenIddictStoreToken),
            OpenIddictStoreSqliteDialect.Instance,
            column => OpenIddictStoreSqliteDialect.Instance.QuoteIdentifier(column));

        Expression<Func<OpenIddictStoreToken, bool>> predicate = token => token.Subject == "alice";

        translator.Translate(predicate.Body);

        var parameter = Assert.Single(translator.Parameters);
        Assert.Equal("alice", parameter.Value);
    }

    [Fact]
    public void NullConstant_StaysAPlainIsNullTest()
    {
        // `== null` reduces to one IS NULL test and never binds the exact predicate.
        var sql = Translate(OpenIddictStoreSqlServerDialect.Instance, token => token.Subject == null);

        Assert.Equal("[Subject] IS NULL", sql);
    }

    [Fact]
    public void DateEquality_DoesNotUseAStringPredicate()
    {
        // Dates keep the dialect-formatted parameter path; no exact string predicate applies.
        var cutoff = new DateTime(2026, 10, 19, 12, 0, 0);

        var sql = Translate(OpenIddictStoreSqlServerDialect.Instance, token => token.CreationDate == cutoff);

        Assert.DoesNotContain("COLLATE", sql, StringComparison.Ordinal);
        Assert.Contains("[CreationDate] = @p0", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyStringEquality_OnADialectThatBindsItAsNull_IsUnsatisfiable()
    {
        // Oracle (and DM8 in Oracle-compatible mode) bind '' as NULL, so `== ""` can match no row; the
        // accidental null-equality of a plain bind must never leak into the predicate.
        var sql = Translate(OpenIddictStoreOracleDialect.Instance, token => token.Subject == "");

        Assert.Equal("1 = 0", sql);
    }

    [Fact]
    public void EmptyStringInequality_OnADialectThatBindsItAsNull_MatchesEveryRow()
    {
        // C# `null != ""` and `"x" != ""` are both true, and "" cannot be stored, so every row matches.
        var sql = Translate(OpenIddictStoreOracleDialect.Instance, token => token.Subject != "");

        Assert.Equal("NOT (1 = 0)", sql);
    }

    [Fact]
    public void EmptyStringMembership_OnADialectThatBindsItAsNull_ContributesNoPredicate()
    {
        // The empty element matches nothing and is dropped; the remaining element keeps its exact predicate.
        var sql = Translate(OpenIddictStoreOracleDialect.Instance,
            token => new[] { "", "alice" }.Contains(token.Subject!));

        Assert.Contains("NLSSORT", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(" OR ", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyStringOnlyMembership_OnADialectThatBindsItAsNull_MatchesNothing()
    {
        var sql = Translate(OpenIddictStoreOracleDialect.Instance,
            token => new[] { "" }.Contains(token.Subject!));

        Assert.Equal("1 = 0", sql);
    }

    [Fact]
    public void EmptyStringEquality_OnADialectThatKeepsItDistinct_StaysTheExactPredicate()
    {
        // SQLite stores '' as a real value, so the compensated equality keeps its full shape.
        var sql = Translate(OpenIddictStoreSqliteDialect.Instance, token => token.Subject == "");

        Assert.Contains("\"Subject\" = @p0", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Contains_BeyondTheParameterBudget_IsRejectedWithAClearError()
    {
        var dialect = new BudgetStubDialect(maximumParameters: 2);
        var translator = new OpenIddictStorePredicateTranslator(
            typeof(OpenIddictStoreToken), dialect, column => dialect.QuoteIdentifier(column));

        Expression<Func<OpenIddictStoreToken, bool>> predicate =
            token => new[] { "a", "b", "c" }.Contains(token.Subject!);

        Assert.Throws<NotSupportedException>(() => translator.Translate(predicate.Body));
    }

    /// <summary>
    /// A dialect whose parameter budget is small enough to exercise the membership ceiling without
    /// building a thousand-element collection.
    /// </summary>
    private sealed class BudgetStubDialect : IOpenIddictStoreDialect
    {
        public BudgetStubDialect(int maximumParameters) => MaximumParameters = maximumParameters;

        public OpenIddictStoreProvider Provider => OpenIddictStoreProvider.Sqlite;

        public string ParameterPrefix => "@";

        public int MaximumParameters { get; }

        public int MaximumIdentifierLength => int.MaxValue;

        public string QuoteIdentifier(string identifier) => "\"" + identifier + "\"";

        public string BuildExactStringEquals(string column, string parameterName)
            => $"{column} = @{parameterName}";

        public string BuildPaging(int? count, int? offset) => string.Empty;

        public object? FormatDate(DateTime? value) => null;

        public DateTime? ReadDate(System.Data.Common.DbDataReader reader, int ordinal) => null;

        public string BuildExactStringNotEqualsIncludingNull(string column, string parameterName)
            => $"({column} IS NULL OR {column} <> @{parameterName})";

        public string BuildExactStringColumnEquals(string left, string right) => $"{left} = {right}";

        public bool EmptyStringBindsAsNull => false;
    }
}

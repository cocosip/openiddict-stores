using System;

namespace Sharp.OpenIddict.Stores;

/// <summary>
/// Database dialect. Instances are stateless; each store configuration binds its own dialect and no process-wide "current database" is used.
/// </summary>
public interface IOpenIddictStoreDialect
{
    /// <summary>The provider this dialect corresponds to.</summary>
    OpenIddictStoreProvider Provider { get; }

    /// <summary>The parameter placeholder prefix, for example <c>@</c> or <c>:</c>.</summary>
    string ParameterPrefix { get; }

    /// <summary>The maximum number of parameters allowed in a single command; chunking and IN protection rely on it.</summary>
    int MaximumParameters { get; }

    /// <summary>Quotes a validated identifier (table name, column name, or constraint name).</summary>
    string QuoteIdentifier(string identifier);

    /// <summary>
    /// Builds a non-null exact equality predicate for plain strings: case, accent, trailing space, and normalization form are all significant.
    /// </summary>
    /// <param name="column">The already quoted column expression.</param>
    /// <param name="parameterName">The parameter name without the prefix.</param>
    string BuildExactStringEquals(string column, string parameterName);

    /// <summary>Builds the paging clause; when offset is present and count is null, the correct row count must still be returned.</summary>
    string BuildPaging(int? count, int? offset);

    /// <summary>
    /// Converts a UTC time to a parameter value. SQLite requires fixed-format text; other providers bind <see cref="DateTime"/> directly.
    /// </summary>
    object? FormatDate(DateTime? value);

    /// <summary>Reads a date column from the reader and marks it as UTC.</summary>
    DateTime? ReadDate(System.Data.Common.DbDataReader reader, int ordinal);

    /// <summary>
    /// Builds the "status is not the given value" predicate, treating NULL as not equal (design 16.2.2).
    /// A plain SQL <c>&lt;&gt;</c> would be unknown for NULL and silently drop those rows.
    /// </summary>
    /// <param name="column">The already quoted column expression.</param>
    /// <param name="parameterName">The parameter name without the prefix.</param>
    string BuildExactStringNotEqualsIncludingNull(string column, string parameterName);

    /// <summary>
    /// Builds a non-null exact equality predicate between two columns of the same table, with the same
    /// case, accent, and trailing-space sensitivity as <see cref="BuildExactStringEquals"/> (design 9.1).
    /// </summary>
    /// <param name="left">The already quoted left column expression.</param>
    /// <param name="right">The already quoted right column expression.</param>
    string BuildExactStringColumnEquals(string left, string right);
}

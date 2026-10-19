using System;
using System.Data.Common;
using System.Globalization;
using Sharp.OpenIddict.Stores;

namespace Sharp.OpenIddict.Stores.PostgreSql;

/// <summary>
/// PostgreSQL dialect. Identifiers are quoted with double quotes (PostgreSQL folds unquoted names to lower
/// case, so quoting is mandatory for the mixed-case table names), the parameter prefix is <c>@</c>, and
/// plain strings compare with <c>=</c> against columns declared <c>COLLATE "C"</c> by the install script.
/// </summary>
public sealed class OpenIddictStorePostgreSqlDialect : IOpenIddictStoreDialect
{
    /// <summary>
    /// The collation forced in every exact comparison and attached to every identifier column by the install
    /// script. <c>C</c> is built into every database and compares byte-wise, so case, accents, and trailing
    /// spaces stay significant regardless of the database's default collation (design 16.1.1). The value
    /// carries its own double quotes, because an unquoted <c>C</c> folds to lower case and stops resolving.
    /// </summary>
    public const string ExactCollation = "\"C\"";

    /// <summary>Shared instance: the dialect is stateless.</summary>
    public static OpenIddictStorePostgreSqlDialect Instance { get; } = new();

    /// <inheritdoc />
    public OpenIddictStoreProvider Provider => OpenIddictStoreProvider.PostgreSql;

    /// <inheritdoc />
    public string ParameterPrefix => "@";

    /// <inheritdoc />
    /// <remarks>The protocol allows 65535 parameters per statement; 65000 leaves headroom for the values a statement already carries.</remarks>
    public int MaximumParameters => 65_000;

    /// <inheritdoc />
    /// <remarks>
    /// PostgreSQL truncates identifiers longer than 63 bytes without an error, which would create tables the
    /// stores then fail to find; the prefix is therefore rejected up front (design 15.4). The limit is counted
    /// in bytes, since a multi-byte character consumes several.
    /// </remarks>
    public int MaximumIdentifierLength => 63;

    /// <inheritdoc />
    public string QuoteIdentifier(string identifier)
        => "\"" + OpenIddictStoreIdentifiers.ValidateLength(
            OpenIddictStoreIdentifiers.Validate(identifier, nameof(identifier)), MaximumIdentifierLength) + "\"";

    /// <inheritdoc />
    /// <remarks>
    /// The <c>C</c> collation is forced in the predicate, not only in the DDL: PostgreSQL would otherwise use
    /// the column default, which a hand-created table may set to something case-insensitive. <c>C</c> compares
    /// byte-wise, so no length compensation is needed the way SQL Server needed it.
    /// </remarks>
    public string BuildExactStringEquals(string column, string parameterName)
        => $"({column} COLLATE {ExactCollation} = @{parameterName})";

    /// <inheritdoc />
    /// <remarks>
    /// PostgreSQL allows <c>OFFSET</c> without <c>LIMIT</c>, and <c>LIMIT 0</c> is a valid way to return no rows.
    /// </remarks>
    public string BuildPaging(int? count, int? offset)
    {
        if (count is null && offset is null)
        {
            return string.Empty;
        }

        var paging = string.Empty;

        if (count is not null)
        {
            paging = "LIMIT " + count.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (offset is not null)
        {
            if (paging.Length > 0)
            {
                paging += " ";
            }

            paging += "OFFSET " + offset.Value.ToString(CultureInfo.InvariantCulture);
        }

        return paging;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A plain <c>&lt;&gt;</c> yields UNKNOWN for NULL and would silently drop those rows. The NULL branch
    /// keeps them and still lets the caller's other predicates constrain them (design 16.2.2). The comparison
    /// forces <c>C</c> for the same reason as <see cref="BuildExactStringEquals"/>.
    /// </remarks>
    public string BuildExactStringNotEqualsIncludingNull(string column, string parameterName)
        => $"({column} IS NULL OR {column} COLLATE {ExactCollation} <> @{parameterName})";

    /// <inheritdoc />
    /// <remarks>Column-to-column comparison forces <c>C</c> on the left side so both sides compare byte-wise.</remarks>
    public string BuildExactStringColumnEquals(string left, string right)
        => $"({left} COLLATE {ExactCollation} = {right})";

    /// <inheritdoc />
    /// <remarks>
    /// Deliberately different from SQL Server: Npgsql 6 and later reject a <see cref="DateTimeKind.Utc"/>
    /// value for <c>timestamp without time zone</c> and require <see cref="DateTimeKind.Unspecified"/>.
    /// The value is already normalized to UTC, so only the annotation is dropped; the instant is unchanged.
    /// </remarks>
    public object? FormatDate(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        return DateTime.SpecifyKind(OpenIddictStoreJson.NormalizeUtc(value)!.Value, DateTimeKind.Unspecified);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>timestamp without time zone</c> comes back with <see cref="DateTimeKind.Unspecified"/>, so the UTC
    /// semantics have to be restored explicitly; otherwise the value would later be converted as local time.
    /// </remarks>
    public DateTime? ReadDate(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal)
            ? null
            : reader.GetFieldType(ordinal) == typeof(DateTimeOffset)
                ? OpenIddictStoreJson.AsUtc(reader.GetFieldValue<DateTimeOffset>(ordinal).UtcDateTime)
                : OpenIddictStoreJson.AsUtc(reader.GetDateTime(ordinal));
}

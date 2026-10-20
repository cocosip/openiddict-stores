using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Sharp.OpenIddict.Stores;

/// <summary>
/// Verifies once per store configuration that the installed schema matches the version this package requires
/// (design 10.3, 17). The check runs before the first operation, its success is remembered on the guard
/// instance, and the guard is Scoped so the knowledge lives exactly as long as the store configuration does.
/// A failure is never cached: the next attempt re-reads the table, because the operator may have just run the
/// required script. No DDL is ever issued here.
/// </summary>
internal sealed class OpenIddictStoreSchemaVersionGuard
{
    private readonly OpenIddictStoreSchema _schema;
    private readonly IOpenIddictStoreDialect _dialect;
    private readonly IOpenIddictStoreConnectionFactory _connectionFactory;
    private readonly int? _commandTimeout;

    // 0 = not checked yet, 1 = verified against this configuration.
    private int _verified;

    public OpenIddictStoreSchemaVersionGuard(
        OpenIddictStoreSchema schema,
        IOpenIddictStoreDialect dialect,
        IOpenIddictStoreConnectionFactory connectionFactory,
        int? commandTimeout)
    {
        _schema = schema;
        _dialect = dialect;
        _connectionFactory = connectionFactory;
        _commandTimeout = commandTimeout;
    }

    /// <summary>
    /// Ensures the schema version was verified for this configuration. Returns immediately after the first
    /// successful check; otherwise opens its own connection so the caller's session is not required.
    /// </summary>
    public async ValueTask EnsureVerifiedAsync(CancellationToken cancellationToken)
    {
        // The fast path must not open a connection: every store operation passes through here.
        if (Volatile.Read(ref _verified) == 1)
        {
            return;
        }

        // The row is selected by series: a table may legitimately carry rows of several series once a
        // second one ships, and matching an arbitrary row would misread an installed sibling series as
        // this package's schema.
        var selectRow = $"SELECT {_dialect.QuoteIdentifier("Series")}, {_dialect.QuoteIdentifier("Version")} " +
                        $"FROM {_schema.VersionsTableName(_dialect)}";

        var row = await ReadRowAsync(
            $"{selectRow} WHERE {_dialect.QuoteIdentifier("Series")} = {_dialect.ParameterPrefix}requiredSeries",
            new { requiredSeries = OpenIddictStoreSchema.RequiredSeries },
            cancellationToken).ConfigureAwait(false);

        if (row is null)
        {
            // No row for the required series. A different series may still be installed, so read whichever
            // row exists to report the actual mismatch instead of a generic "not installed".
            var installed = await ReadRowAsync(selectRow, null, cancellationToken).ConfigureAwait(false);

            if (installed is not null)
            {
                throw new OpenIddictStoreSchemaVersionException(
                    $"The installed schema belongs to series '{installed.Value.Series}' but this package requires series " +
                    $"'{OpenIddictStoreSchema.RequiredSeries}'. {RequiredMessage()}");
            }

            throw new OpenIddictStoreSchemaVersionException(
                $"The schema version table {_schema.SchemaVersions} contains no row for series " +
                $"'{OpenIddictStoreSchema.RequiredSeries}'. {RequiredMessage()}");
        }

        var (series, version) = row.Value;

        if (version != OpenIddictStoreSchema.RequiredVersion)
        {
            throw new OpenIddictStoreSchemaVersionException(
                $"The installed schema is at series '{series}' version {version} but this package requires " +
                $"version {OpenIddictStoreSchema.RequiredVersion}. {RequiredMessage()}");
        }

        Volatile.Write(ref _verified, 1);
    }

    private async ValueTask<(string Series, int Version)?> ReadRowAsync(
        string sql, object? parameters, CancellationToken cancellationToken)
    {
        try
        {
            await using var session = await OpenIddictStoreSession
                .CreateAsync(_connectionFactory, _dialect, _schema, _commandTimeout, cancellationToken)
                .ConfigureAwait(false);

            // The mapper returns a value type, so an absent row materializes as default and cannot be told
            // apart from a real row; a nullable element makes "no row" observable.
            return await session.QuerySingleOrDefaultAsync<(string Series, int Version)?>(
                sql, parameters, ReadRow, cancellationToken).ConfigureAwait(false);
        }
        catch (DbException exception)
        {
            // A missing table surfaces as a provider error; it means the same thing as "not installed".
            throw new OpenIddictStoreSchemaVersionException(MissingTableMessage(), exception);
        }
    }

    private static (string Series, int Version)? ReadRow(DbDataReader reader) => (reader.GetString(0), reader.GetInt32(1));

    private string MissingTableMessage()
        => $"The schema version table {_schema.SchemaVersions} was not found. {RequiredMessage()}";

    /// <summary>
    /// Names the script the operator has to run. It deliberately reports the version rather than a file path:
    /// the assets ship inside the provider package as templates, so the deployment copy is rendered locally.
    /// </summary>
    private static string RequiredMessage()
        => $"Run the '{OpenIddictStoreSchema.RequiredSeries}' install/upgrade scripts shipped with this package " +
           $"until version {OpenIddictStoreSchema.RequiredVersion} is recorded, then restart the application. " +
           "This store never creates or alters tables automatically.";
}

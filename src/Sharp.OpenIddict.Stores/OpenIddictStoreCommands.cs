using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Sharp.OpenIddict.Stores;

/// <summary>
/// Builds and runs commands directly on ADO.NET. The stores hand it SQL plus a parameter bag
/// (an anonymous object or a dictionary); it binds values by name and reads rows through the
/// caller's mapper. No ORM conventions are involved: mapping, SQL generation, and paging all live
/// in this project (design 9.3).
/// </summary>
internal static class OpenIddictStoreCommands
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Binders = new();

    /// <summary>Creates a command with the SQL, timeout, transaction, and parameters already applied.</summary>
    public static DbCommand Create(
        DbConnection connection,
        string sql,
        object? parameters,
        int? commandTimeout,
        DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        if (string.IsNullOrEmpty(sql))
        {
            throw new ArgumentException("The command text cannot be null or empty.", nameof(sql));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;

        if (commandTimeout is { } timeout)
        {
            command.CommandTimeout = timeout;
        }

        AddParameters(connection, command, parameters);

        return command;
    }

    /// <summary>
    /// Binds the parameter bag. Two shapes are supported on purpose: an anonymous/plain object whose
    /// public properties are the parameter names, and a dictionary of name to value. Anything else is
    /// rejected instead of being silently ignored, so a typo never turns into a missing parameter.
    /// </summary>
    private static void AddParameters(DbConnection connection, DbCommand command, object? parameters)
    {
        if (parameters is null)
        {
            return;
        }

        if (parameters is IDictionary<string, object?> dictionary)
        {
            foreach (var pair in dictionary)
            {
                AddParameter(connection, command, pair.Key, pair.Value);
            }

            return;
        }

        foreach (var property in Binders.GetOrAdd(parameters.GetType(), Discover))
        {
            AddParameter(connection, command, property.Name, property.GetValue(parameters));
        }
    }

    private static PropertyInfo[] Discover(Type type)
    {
        var properties = new List<PropertyInfo>();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Only readable reference/value properties carry a value; indexers cannot be bound.
            if (property.CanRead && property.GetIndexParameters().Length == 0)
            {
                properties.Add(property);
            }
        }

        return properties.ToArray();
    }

    private static void AddParameter(DbConnection connection, DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;

        // A provider-neutral null: drivers reject a plain null, and DBNull is the one value every
        // ADO.NET provider agrees on.
        parameter.Value = value ?? DBNull.Value;

        // The value's runtime type decides the provider type. The dialect therefore binds dates as a
        // UTC DateTimeOffset (datetimeoffset) rather than DateTime, because a plain DateTime is inferred
        // as "datetime" on SQL Server (3.33 ms) and silently truncates microseconds.
        command.Parameters.Add(parameter);
    }

    /// <summary>Executes a non-query command and returns the affected row count.</summary>
    public static async Task<int> ExecuteAsync(
        DbConnection connection,
        string sql,
        object? parameters,
        int? commandTimeout,
        DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var command = Create(connection, sql, parameters, commandTimeout, transaction, cancellationToken);

        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await OpenIddictStoreConnections.DisposeAsync(command).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Executes a scalar query. The value is converted to <typeparamref name="T"/> without letting a
    /// provider-specific boxed type leak into the stores (COUNT comes back as int on some drivers).
    /// </summary>
    public static async Task<T?> ExecuteScalarAsync<T>(
        DbConnection connection,
        string sql,
        object? parameters,
        int? commandTimeout,
        DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var command = Create(connection, sql, parameters, commandTimeout, transaction, cancellationToken);

        try
        {
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            return Convert<T>(value);
        }
        finally
        {
            await OpenIddictStoreConnections.DisposeAsync(command).ConfigureAwait(false);
        }
    }

    /// <summary>Opens a reader whose lifetime belongs to the caller.</summary>
    public static async Task<DbDataReader> ExecuteReaderAsync(
        DbConnection connection,
        string sql,
        object? parameters,
        int? commandTimeout,
        DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var command = Create(connection, sql, parameters, commandTimeout, transaction, cancellationToken);

        try
        {
            return await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await OpenIddictStoreConnections.DisposeAsync(command).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Converts a provider value to the requested type; DBNull and null map to default.</summary>
    public static T? Convert<T>(object? value)
    {
        if (value is null || value is DBNull)
        {
            return default;
        }

        var target = typeof(T);
        var underlying = Nullable.GetUnderlyingType(target) ?? target;

        if (underlying.IsInstanceOfType(value))
        {
            return (T)value;
        }

        // A COUNT can arrive as any integer width; Convert handles the numeric widening exactly.
        if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(underlying))
        {
            return (T)System.Convert.ChangeType(value, underlying, System.Globalization.CultureInfo.InvariantCulture);
        }

        return (T)value;
    }
}

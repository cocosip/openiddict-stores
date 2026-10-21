using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Collections.Concurrent;

namespace Sharp.OpenIddict.Stores.Queries;

/// <summary>
/// Maps a data reader row onto a store entity or a projection, using the fixed column map so the
/// mapping rules stay in one place (design 15.2: the property name is the column name).
/// </summary>
internal static class OpenIddictStoreEntityMapper
{
    // Reflection lookups are cached per (type, name): rows are materialized in tight loops, and a metadata
    // search per field per row shows up on large result sets.
    private static readonly ConcurrentDictionary<(Type Type, string Name), PropertyInfo?> Properties = new();

    /// <summary>Creates an entity from the current row of the reader.</summary>
    public static object Map(DbDataReader reader, Type entityType, IOpenIddictStoreDialect dialect)
    {
        var map = OpenIddictStoreColumnMap.For(entityType)
            ?? throw new NotSupportedException(
                $"The type '{entityType}' is not a store entity and cannot be materialized.");

        var instance = Activator.CreateInstance(entityType)
            ?? throw new InvalidOperationException($"The entity type '{entityType}' has no parameterless constructor.");

        for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
        {
            var name = reader.GetName(ordinal);

            if (!map.TryGetValue(name, out var column) || reader.IsDBNull(ordinal))
            {
                continue;
            }

            var property = ResolveProperty(entityType, column.Property);

            if (property is null || !property.CanWrite)
            {
                continue;
            }

            property.SetValue(instance, ReadValue(reader, ordinal, property.PropertyType, dialect));
        }

        return instance;
    }

    /// <summary>Reads a projection row: a single scalar, or a simple object whose members match the aliases.</summary>
    public static object? ReadProjection(DbDataReader reader, Type projectionType, IOpenIddictStoreDialect dialect)
    {
        // A single mapped column projected directly.
        if (IsScalar(projectionType))
        {
            return reader.IsDBNull(0)
                ? DefaultOf(projectionType)
                : ReadValue(reader, 0, projectionType, dialect);
        }

        var instance = Activator.CreateInstance(projectionType)
            ?? throw new InvalidOperationException(
                $"The projection type '{projectionType}' has no parameterless constructor.");

        for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
        {
            var property = ResolveProperty(projectionType, reader.GetName(ordinal));

            if (property is null || !property.CanWrite)
            {
                continue;
            }

            property.SetValue(
                instance,
                reader.IsDBNull(ordinal) ? null : ReadValue(reader, ordinal, property.PropertyType, dialect));
        }

        return instance;
    }

    private static PropertyInfo? ResolveProperty(Type type, string name)
        => Properties.GetOrAdd((type, name), static key =>
            key.Type.GetProperty(key.Name, BindingFlags.Public | BindingFlags.Instance));

    /// <summary>Reads one column, letting the dialect own the date conversion.</summary>
    public static object? ReadValue(DbDataReader reader, int ordinal, Type targetType, IOpenIddictStoreDialect dialect)
    {
        if (targetType == typeof(DateTime?) || targetType == typeof(DateTime))
        {
            return dialect.ReadDate(reader, ordinal);
        }

        if (targetType == typeof(DateTimeOffset?) || targetType == typeof(DateTimeOffset))
        {
            var date = dialect.ReadDate(reader, ordinal);

            return date is null ? null : new DateTimeOffset(date.Value, TimeSpan.Zero);
        }

        var value = reader.GetValue(ordinal);

        if (value.GetType() == targetType)
        {
            return value;
        }

        return Convert.ChangeType(
            value, Nullable.GetUnderlyingType(targetType) ?? targetType, CultureInfo.InvariantCulture);
    }

    private static bool IsScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying.IsPrimitive
               || underlying.IsEnum
               || underlying == typeof(string)
               || underlying == typeof(decimal)
               || underlying == typeof(DateTime)
               || underlying == typeof(DateTimeOffset)
               || underlying == typeof(Guid);
    }

    private static object? DefaultOf(Type type)
        => type.IsValueType ? Activator.CreateInstance(type) : null;
}

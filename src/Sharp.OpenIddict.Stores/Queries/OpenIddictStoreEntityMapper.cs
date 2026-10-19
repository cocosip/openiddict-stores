using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Sharp.OpenIddict.Stores.Queries;

/// <summary>
/// Maps a data reader row onto a store entity or a projection, using the fixed column map so the
/// mapping rules stay in one place (design 15.2: the property name is the column name).
/// </summary>
internal static class OpenIddictStoreEntityMapper
{
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

            var property = entityType.GetProperty(column.Property, BindingFlags.Public | BindingFlags.Instance);

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
            var property = projectionType.GetProperty(
                reader.GetName(ordinal), BindingFlags.Public | BindingFlags.Instance);

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

    /// <summary>
    /// Rewrites a two-argument callback into a one-argument one by replacing the state parameter with a
    /// constant. Everything derived from the state is then evaluated once at translation time.
    /// </summary>
    public static Expression<Func<IQueryable<TEntity>, IQueryable<TResult>>> BindState<TEntity, TState, TResult>(
        Expression<Func<IQueryable<TEntity>, TState, IQueryable<TResult>>> query, TState state)
    {
        var body = new StateBinder(query.Parameters[1], state).Visit(query.Body)
            ?? throw new NotSupportedException("The query callback body could not be bound to its state.");

        return Expression.Lambda<Func<IQueryable<TEntity>, IQueryable<TResult>>>(body, query.Parameters[0]);
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

    /// <summary>Replaces the state parameter with a constant of the same declared type.</summary>
    private sealed class StateBinder : ExpressionVisitor
    {
        private readonly ParameterExpression _parameter;
        private readonly object? _value;

        public StateBinder(ParameterExpression parameter, object? value)
        {
            _parameter = parameter;
            _value = value;
        }

        protected override Expression VisitParameter(ParameterExpression node)
            => node == _parameter ? Expression.Constant(_value, node.Type) : base.VisitParameter(node);
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Sharp.OpenIddict.Stores.Queries;

/// <summary>
/// Translates the supported subset of an IQueryable callback into a SQL fragment (design 9).
/// Two rules dominate every predicate:
/// <list type="bullet">
/// <item>C# comparison semantics, not SQL three-valued logic: equality and inequality are emitted as a
/// complete two-valued predicate, so NULL rows are included exactly as the design 9.1 table says.</item>
/// <item>Bounded strings compare with the dialect's exact predicate, so case, accents, and trailing
/// spaces stay significant (design 16.1.1).</item>
/// </list>
/// Anything outside the whitelist is rejected before any SQL runs; nothing falls back to memory.
/// </summary>
internal sealed class OpenIddictStorePredicateTranslator
{
    private readonly Type _entityType;
    private readonly IOpenIddictStoreDialect _dialect;
    private readonly Func<string, string> _quoteColumn;
    private readonly Dictionary<string, object?> _parameters = new(StringComparer.Ordinal);
    private int _next;

    public OpenIddictStorePredicateTranslator(
        Type entityType, IOpenIddictStoreDialect dialect, Func<string, string> quoteColumn)
    {
        _entityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        _quoteColumn = quoteColumn ?? throw new ArgumentNullException(nameof(quoteColumn));
    }

    /// <summary>The parameters collected while translating.</summary>
    public IReadOnlyDictionary<string, object?> Parameters => _parameters;

    /// <summary>Translates a boolean expression into a SQL predicate.</summary>
    public string Translate(Expression expression)
        => TranslateBoolean(expression);

    private string TranslateBoolean(Expression expression)
    {
        switch (expression)
        {
            case BinaryExpression binary when binary.NodeType is ExpressionType.AndAlso or ExpressionType.And:
                return "(" + TranslateBoolean(binary.Left) + " AND " + TranslateBoolean(binary.Right) + ")";

            case BinaryExpression binary when binary.NodeType is ExpressionType.OrElse or ExpressionType.Or:
                return "(" + TranslateBoolean(binary.Left) + " OR " + TranslateBoolean(binary.Right) + ")";

            case UnaryExpression unary when unary.NodeType == ExpressionType.Not:
                // Negating the already-compensated predicate is what makes `!(date < x)` include NULL rows,
                // instead of mechanically becoming `date >= x` (design 9.1).
                return "NOT " + Parenthesize(TranslateBoolean(unary.Operand));

            case BinaryExpression binary when binary.NodeType is ExpressionType.Equal or ExpressionType.NotEqual:
                return TranslateEquality(binary);

            case BinaryExpression binary when binary.NodeType is ExpressionType.LessThan
                or ExpressionType.LessThanOrEqual or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual:
                return TranslateComparison(binary);

            case MethodCallExpression call when IsEnumerableContains(call):
                return TranslateContains(call);

            case ConstantExpression constant when constant.Value is bool value:
                return value ? "1 = 1" : "1 = 0";

            case MemberExpression member when member.Type == typeof(bool) && IsEntityMember(member):
                // A nullable-free boolean column would land here; none exist today, so reject explicitly.
                throw Reject(member, "a boolean column is not part of the supported column map.");

            default:
                throw Reject(expression, "the expression is not part of the supported predicate whitelist.");
        }
    }

    private string TranslateEquality(BinaryExpression binary)
    {
        var equal = binary.NodeType == ExpressionType.Equal;
        var left = Operand.Classify(binary.Left, _entityType, _quoteColumn, BindValue);
        var right = Operand.Classify(binary.Right, _entityType, _quoteColumn, BindValue);

        // Both sides constant or parameter: evaluate in C# and emit a constant predicate.
        if (left.Kind == OperandKind.Value && right.Kind == OperandKind.Value)
        {
            var result = equal
                ? Operand.ValueEquals(left, right)
                : !Operand.ValueEquals(left, right);

            return result ? "1 = 1" : "1 = 0";
        }

        if (left.Kind == OperandKind.Column && right.Kind == OperandKind.Column)
        {
            return BuildEquality(binary, left, right, equal);
        }

        if (left.Kind == OperandKind.Column || right.Kind == OperandKind.Column)
        {
            return BuildEquality(binary, left, right, equal);
        }

        throw Reject(binary, "equality requires at least one mapped column.");
    }

    /// <summary>
    /// Emits the design 9.1 equality table as a complete two-valued predicate:
    /// <c>(a IS NULL AND b IS NULL) OR (a IS NOT NULL AND b IS NOT NULL AND EqualNonNull(a,b))</c>.
    /// Inequality is the negation of that whole predicate, never a bare <c>&lt;&gt;</c>.
    /// </summary>
    private string BuildEquality(BinaryExpression binary, Operand left, Operand right, bool equal)
    {
        // Normalise so the column is always on the left and the other side is either a column or a value.
        if (!left.IsColumn && right.IsColumn)
        {
            (left, right) = (right, left);
        }

        var a = left.Sql;

        // `column == null` and `column != null` are the two shapes that do not need a non-null branch: the
        // compensated predicate reduces to one IS NULL test.
        if (right.IsNullValue)
        {
            var oneSided = $"{a} IS NULL";

            return equal ? oneSided : $"NOT {Parenthesize(oneSided)}";
        }

        // Render the other side once, with the representation the column needs: a date is formatted by the
        // dialect, a bounded string stays a parameter for the exact predicate. Both branches reuse it, so the
        // null test and the equality never disagree about the bound form.
        var b = right.IsColumn ? right.Sql : BindOperandFor(left, right);
        var bothNull = $"({a} IS NULL AND {b} IS NULL)";
        var bothNotNullAndEqual = $"({a} IS NOT NULL AND {b} IS NOT NULL AND {BuildNonNullEqual(binary, left, right, b)})";
        var predicate = $"({bothNull} OR {bothNotNullAndEqual})";

        return equal ? predicate : $"NOT {Parenthesize(predicate)}";
    }

    private string BuildNonNullEqual(BinaryExpression binary, Operand left, Operand right, string boundOther)
    {
        var column = left.Kind == OperandKind.Column ? left : right.Kind == OperandKind.Column ? right : null;

        if (column is null)
        {
            throw Reject(binary, "equality requires at least one mapped column.");
        }

        var other = ReferenceEquals(column, left) ? right : left;
        var resolved = column.Column!;

        if (resolved.Kind == OpenIddictStoreColumnKind.Date || resolved.IsBoundedString)
        {
            // Both need the already rendered form: a date is dialect-formatted, and a bounded string is bound
            // as a parameter so the dialect predicate stays exact (design 16.1.1).
            return $"{column.Sql} = {boundOther}";
        }

        if (resolved.Kind is OpenIddictStoreColumnKind.LargeText or OpenIddictStoreColumnKind.Json)
        {
            throw Reject(binary,
                $"column '{resolved.Property}' only supports null checks and projection (design 9.2).");
        }

        return $"{column.Sql} = {boundOther}";
    }

    /// <summary>
    /// Renders the non-column side of a comparison in the form the column needs. A bounded string is bound
    /// as a plain parameter here; the exact predicate is applied by the caller.
    /// </summary>
    private string BindOperandFor(Operand column, Operand other)
        => column.Column?.Kind == OpenIddictStoreColumnKind.Date
            ? FormatDateOperand(other)
            : BindOperand(other);

    private string BuildExactEquality(Operand column, Operand other)
    {
        if (other.Kind == OperandKind.Column)
        {
            // Same-table column-to-column comparison is part of the supported set (design 9.1); it needs a
            // column form of the exact predicate so both sides stay exact.
            return _dialect.BuildExactStringColumnEquals(column.Sql, other.Sql);
        }

        if (other.Value is null)
        {
            // `column == null` never reaches the non-null branch, so this is a defensive rejection.
            throw new NotSupportedException(
                $"The bounded string column '{column.Column!.Property}' was compared with null in a non-null context.");
        }

        if (other.Value is not string value)
        {
            throw new NotSupportedException(
                $"The bounded string column '{column.Column!.Property}' can only be compared with a string value.");
        }

        var name = NextParameter();
        _parameters[name] = value;

        return _dialect.BuildExactStringEquals(column.Sql, name);
    }

    private string TranslateComparison(BinaryExpression binary)
    {
        var left = Operand.Classify(binary.Left, _entityType, _quoteColumn, BindValue);
        var right = Operand.Classify(binary.Right, _entityType, _quoteColumn, BindValue);

        var column = left.Kind == OperandKind.Column ? left : right.Kind == OperandKind.Column ? right : null;

        if (column is null)
        {
            // Pure value comparison: evaluate it now.
            var value = Evaluate(binary);
            return value is true ? "1 = 1" : "1 = 0";
        }

        if (!column.Column!.IsComparable)
        {
            throw Reject(binary,
                $"column '{column.Column.Property}' does not support range comparison (design 9.2).");
        }

        if (column.Column.Kind == OpenIddictStoreColumnKind.Date)
        {
            return TranslateDateComparison(binary, column, ReferenceEquals(column, left) ? right : left);
        }

        if (column.Column.IsBoundedString)
        {
            throw Reject(binary,
                $"column '{column.Column.Property}' is a bounded string: only equality and inequality are " +
                "supported, because a range comparison would depend on the database collation (design 16.1.1).");
        }

        return $"{column.Sql} {Operator(binary.NodeType)} {BindOperand(ReferenceEquals(column, left) ? right : left)}";
    }

    /// <summary>
    /// A nullable date comparison is false whenever either side is NULL, so the predicate must also
    /// require both sides to be non-null (design 9.1).
    /// </summary>
    private string TranslateDateComparison(BinaryExpression binary, Operand column, Operand other)
    {
        var formatted = FormatDateOperand(other);
        var bothNotNull = $"({column.Sql} IS NOT NULL AND {formatted} IS NOT NULL)";

        return $"({bothNotNull} AND {column.Sql} {Operator(binary.NodeType)} {formatted})";
    }

    /// <summary>Renders an operand as executable SQL: a column as-is, a value as a bound parameter.</summary>
    private string BindOperand(Operand operand)
        => operand.Kind == OperandKind.Column ? operand.Sql : BindValue(operand.Value);

    /// <summary>Renders a date operand through the dialect so both sides use the stored representation.</summary>
    private string FormatDateOperand(Operand operand)
    {
        if (operand.Kind == OperandKind.Column)
        {
            return operand.Sql;
        }

        if (operand.Value is null)
        {
            return "NULL";
        }

        var date = operand.Value switch
        {
            DateTimeOffset offset => OpenIddictStoreJson.NormalizeUtc(offset),
            DateTime value => OpenIddictStoreJson.NormalizeUtc(value),
            _ => throw new NotSupportedException(
                $"The value '{operand.Value}' cannot be compared with a date column."),
        };

        return BindValue(_dialect.FormatDate(date));
    }

    /// <summary>
    /// Translates <c>values.Contains(x)</c> following the design 9.1 collection table. NULL elements are
    /// separated from non-null ones so the predicate never depends on <c>NOT IN (..., NULL)</c>.
    /// </summary>
    private string TranslateContains(MethodCallExpression call)
    {
        var values = EvaluateCollection(call.Arguments[0]);
        var target = Operand.Classify(call.Arguments[1], _entityType, _quoteColumn, BindValue);

        if (values is null)
        {
            throw new NotSupportedException(
                "The collection passed to Contains is null. A null collection is an evaluation error, not an " +
                "empty collection (design 9.1).");
        }

        var hasNull = values.Any(value => value is null);
        var nonNull = values.Where(value => value is not null).ToList();

        // Empty collection: always false, and no invalid `IN ()` is emitted.
        if (values.Count == 0)
        {
            return "1 = 0";
        }

        if (target.Kind == OperandKind.Value)
        {
            // The compared value is known: decide in C#.
            return values.Any(value => Operand.ValueEqualsValue(target.Value, value)) ? "1 = 1" : "1 = 0";
        }

        if (target.Kind != OperandKind.Column)
        {
            throw Reject(call, "Contains requires a mapped column or a constant value.");
        }

        if (!target.Column!.IsComparable)
        {
            throw Reject(call,
                $"column '{target.Column.Property}' does not support membership tests (design 9.2).");
        }

        if (target.Column.Kind == OpenIddictStoreColumnKind.Date)
        {
            return TranslateDateContains(target, nonNull, hasNull);
        }

        var column = target.Sql;
        var parts = new List<string>();

        if (hasNull)
        {
            parts.Add($"{column} IS NULL");
        }

        if (nonNull.Count > 0)
        {
            parts.Add(BuildMembership(target, nonNull));
        }

        if (parts.Count == 0)
        {
            // Only NULL elements and the column cannot be null in a way that matters: nothing matches.
            return "1 = 0";
        }

        return parts.Count == 1 ? parts[0] : "(" + string.Join(" OR ", parts) + ")";
    }

    private string TranslateDateContains(Operand target, List<object?> nonNull, bool hasNull)
    {
        var parts = new List<string>();

        if (hasNull)
        {
            parts.Add($"{target.Sql} IS NULL");
        }

        foreach (var value in nonNull)
        {
            var name = NextParameter();
            _parameters[name] = _dialect.FormatDate(OpenIddictStoreJson.NormalizeUtc((DateTimeOffset)value!));
            parts.Add($"{target.Sql} = {_dialect.ParameterPrefix}{name}");
        }

        return parts.Count == 1 ? parts[0] : "(" + string.Join(" OR ", parts) + ")";
    }

    private string BuildMembership(Operand target, List<object?> values)
    {
        if (target.Column!.IsBoundedString)
        {
            // Exact membership: one dialect predicate per value, ORed together. A native IN would use the
            // database collation and break the exact semantics.
            var parts = new List<string>(values.Count);

            foreach (var value in values)
            {
                if (value is not string text)
                {
                    throw new NotSupportedException(
                        $"The bounded string column '{target.Column.Property}' can only be compared with strings.");
                }

                var name = NextParameter();
                _parameters[name] = text;
                parts.Add(_dialect.BuildExactStringEquals(target.Sql, name));
            }

            return parts.Count == 1 ? parts[0] : "(" + string.Join(" OR ", parts) + ")";
        }

        // Non-string scalar: a parameterized IN keeps the semantics and stays within the parameter budget.
        var placeholders = new List<string>(values.Count);

        foreach (var value in values)
        {
            var name = NextParameter();
            _parameters[name] = value;
            placeholders.Add(_dialect.ParameterPrefix + name);
        }

        return $"{target.Sql} IN ({string.Join(", ", placeholders)})";
    }

    private string NextParameter() => "p" + (_next++).ToString(CultureInfo.InvariantCulture);

    /// <summary>Binds a value as a parameter and returns its placeholder.</summary>
    private string BindValue(object? value)
    {
        var name = NextParameter();
        _parameters[name] = value;

        return _dialect.ParameterPrefix + name;
    }

    private static string Parenthesize(string sql)
        => sql.StartsWith("(", StringComparison.Ordinal) && sql.EndsWith(")", StringComparison.Ordinal)
            ? sql
            : "(" + sql + ")";

    private static string Operator(ExpressionType type) => type switch
    {
        ExpressionType.LessThan => "<",
        ExpressionType.LessThanOrEqual => "<=",
        ExpressionType.GreaterThan => ">",
        ExpressionType.GreaterThanOrEqual => ">=",
        _ => throw new NotSupportedException($"The operator '{type}' is not supported."),
    };

    private static bool IsEntityMember(MemberExpression member)
        => member.Expression is not null && member.Expression.Type == member.Expression.Type;

    /// <summary>
    /// Recognises the collection Contains forms a callback can produce. Besides
    /// <c>Enumerable.Contains</c>, an array argument binds to the span overload, so both are accepted;
    /// <c>string.Contains</c> is an instance method and is therefore excluded.
    /// </summary>
    private static bool IsEnumerableContains(MethodCallExpression call)
        => call.Method.Name == nameof(Enumerable.Contains)
           && call.Method.IsStatic
           && call.Arguments.Count == 2
           && call.Arguments[0].Type != typeof(string);

    private static object? Evaluate(Expression expression)
    {
        if (expression is ConstantExpression constant)
        {
            return constant.Value;
        }

        var lambda = Expression.Lambda(expression);
        return lambda.Compile().DynamicInvoke();
    }

    private static List<object?>? EvaluateCollection(Expression expression)
    {
        // An array argument binds to the span overload through implicit and boxing conversions. A span is
        // a ref struct and cannot be evaluated at all, so every wrapping conversion is stripped until the
        // real collection expression is reached.
        while (true)
        {
            if (expression is UnaryExpression unary
                && unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
            {
                expression = unary.Operand;
                continue;
            }

            if (expression is MethodCallExpression call
                && call.Method.Name is "op_Implicit" or "op_Explicit"
                && call.Arguments.Count == 1)
            {
                expression = call.Arguments[0];
                continue;
            }

            break;
        }

        var value = Evaluate(expression);

        if (value is null)
        {
            return null;
        }

        if (value is IEnumerable sequence)
        {
            var items = new List<object?>();

            foreach (var item in sequence)
            {
                items.Add(item);
            }

            return items;
        }

        throw new NotSupportedException(
            $"The Contains argument of type '{value.GetType()}' is not an enumerable collection.");
    }

    private static NotSupportedException Reject(Expression expression, string reason)
        => new($"The query callback expression '{expression}' cannot be translated: {reason}");

    /// <summary>One operand of a comparison: either a mapped column or a value known at translation time.</summary>
    internal enum OperandKind
    {
        Column,
        Value,
    }

    /// <summary>A resolved operand with its SQL text or its evaluated value.</summary>
    internal sealed class Operand
    {
        private Operand(OperandKind kind, string sql, object? value, OpenIddictStoreColumn? column)
        {
            Kind = kind;
            Sql = sql;
            Value = value;
            Column = column;
        }

        public OperandKind Kind { get; }

        public string Sql { get; }

        public object? Value { get; }

        public OpenIddictStoreColumn? Column { get; }

        /// <summary>True when the operand is the null constant, which has no value to compare.</summary>
        public bool IsNullValue => Kind == OperandKind.Value && Value is null;

        /// <summary>True when the operand is a mapped column.</summary>
        public bool IsColumn => Kind == OperandKind.Column;

        public static Operand Classify(Expression expression, Type entityType, Func<string, string> quoteColumn, Func<object?, string> bindValue)
        {
            // Unwrap conversions so `(object)entity` and nullable lifting do not break recognition.
            while (expression is UnaryExpression unary && unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
            {
                expression = unary.Operand;
            }

            if (expression is MemberExpression member && member.Expression?.Type == entityType)
            {
                var column = OpenIddictStoreColumnMap.Resolve(entityType, member.Member.Name);

                // Column names always come from the fixed map and are quoted by the dialect.
                return new Operand(OperandKind.Column, quoteColumn(column.Property), null, column);
            }

            // A member read on the entity that is not in the column map must be rejected, not evaluated: the
            // map is the only source of column names (design 9.2). Reaching this point with such a member
            // means the expression is outside the whitelist.
            if (expression is MemberExpression unmapped && unmapped.Expression?.Type == entityType)
            {
                throw new NotSupportedException(
                    $"The member '{entityType.Name}.{unmapped.Member.Name}' is not a mapped column and cannot be " +
                    "translated to SQL (design 9.2).");
            }

            object? value;

            try
            {
                value = Evaluate(expression);
            }
            catch (Exception exception) when (exception is not NotSupportedException)
            {
                throw new NotSupportedException(
                    $"The expression '{expression}' cannot be evaluated while translating the query callback: " +
                    "only constants, closed-over values, and mapped columns are supported (design 9.2).",
                    exception);
            }

            // The value is kept unbound: each consumer binds it with the representation it needs, so a date
            // can be formatted by the dialect instead of being stored as a raw DateTimeOffset.
            return new Operand(OperandKind.Value, "NULL", value, null);
        }

        /// <summary>C# equality between two operands, used when both sides are known values.</summary>
        public static bool ValueEquals(Operand left, Operand right)
            => ValueEqualsValue(left.Value, right.Value);

        /// <summary>C# equality that treats two nulls as equal and a null and a non-null as different.</summary>
        public static bool ValueEqualsValue(object? left, object? right)
        {
            if (left is null || right is null)
            {
                return left is null && right is null;
            }

            // A DateTime column is stored as UTC, so compare the UTC instant rather than the offset.
            if (left is DateTimeOffset leftOffset && right is DateTimeOffset rightOffset)
            {
                return leftOffset.UtcDateTime == rightOffset.UtcDateTime;
            }

            if (left is DateTime leftDate && right is DateTime rightDate)
            {
                return leftDate == rightDate;
            }

            return left.Equals(right);
        }
    }
}

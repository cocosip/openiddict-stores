using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Sharp.OpenIddict.Stores.Queries;

/// <summary>
/// Turns a store query callback into SQL. Operators keep their original order: a Where after a Take
/// is applied after the limit, so it cannot be hoisted (design 9, QUERY-02). Anything that cannot be
/// expressed exactly is rejected before execution instead of being approximated.
/// </summary>
internal sealed class OpenIddictStoreQueryBuilder<TEntity>
{
    private readonly IOpenIddictStoreDialect _dialect;
    private readonly Func<string, string> _quoteTable;
    private readonly Func<string, string> _quoteColumn;

    public OpenIddictStoreQueryBuilder(
        IOpenIddictStoreDialect dialect,
        Func<string, string> quoteTable,
        Func<string, string> quoteColumn)
    {
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        _quoteTable = quoteTable ?? throw new ArgumentNullException(nameof(quoteTable));
        _quoteColumn = quoteColumn ?? throw new ArgumentNullException(nameof(quoteColumn));
    }

    /// <summary>
    /// Builds a SELECT for the given callback, returning the projection so callers know how to read rows.
    /// </summary>
    /// <summary>
    /// Builds a plan from an already-recorded expression tree. The tree starts at the entity set node
    /// created by <see cref="OpenIddictStoreQueryable{T}"/>, so the operators appear in call order.
    /// </summary>
    /// <param name="table">The already quoted table name.</param>
    /// <param name="expression">The recorded query expression tree.</param>
    /// <param name="forCount">
    /// True when the plan is wrapped in <c>SELECT COUNT(*)</c>. An ORDER BY without paging is then
    /// dropped: it cannot change the row count, and SQL Server rejects ORDER BY in a derived table
    /// unless TOP/OFFSET/FETCH is also present (design 9, QUERY-02).
    /// </param>
    public OpenIddictStoreQueryPlan Build<TResult>(string table, Expression expression, bool forCount = false)
    {
        if (expression is null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        var steps = Decompose(expression);
        var translator = new OpenIddictStorePredicateTranslator(typeof(TEntity), _dialect, _quoteColumn);

        // A Where that appears after a Take belongs to the outer query and cannot be pushed down.
        var firstLimit = steps.FindIndex(step => step.Kind is StepKind.Skip or StepKind.Take);
        var projection = steps.LastOrDefault(step => step.Kind == StepKind.Select);

        var inner = new List<Step>();
        var outerPredicates = new List<string>();
        var hasOuterProjection = false;

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];

            if (step.Kind == StepKind.Select)
            {
                // A projection that is not last means the callback projects and then filters or orders on
                // the projection; that cannot be pushed into one statement and is rejected.
                if (index != steps.Count - 1 && steps.Skip(index + 1).Any(next => next.Kind != StepKind.Select))
                {
                    throw new NotSupportedException(
                        "A Select that is followed by more operators cannot be translated: project last, or " +
                        "filter and order before projecting (design 9.2).");
                }

                hasOuterProjection = index > firstLimit && firstLimit >= 0;
                continue;
            }

            if (step.Kind == StepKind.Where && firstLimit >= 0 && index > firstLimit)
            {
                outerPredicates.Add(translator.Translate(step.Predicate!));
                continue;
            }

            inner.Add(step);
        }

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        var select = BuildProjection(projection, translator);

        var sql = BuildSql(table, inner, select, parameters, translator, hasOuterProjection, outerPredicates,
            forCount && firstLimit < 0);

        return new OpenIddictStoreQueryPlan(sql, parameters, select.Kind, _dialect);
    }

    private string BuildSql(
        string table,
        List<Step> steps,
        Projection projection,
        Dictionary<string, object?> parameters,
        OpenIddictStorePredicateTranslator translator,
        bool hasOuterProjection,
        List<string> outerPredicates,
        bool dropOrdering)
    {
        var where = new List<string>();
        var orderBy = new List<string>();
        int? skip = null;
        int? take = null;

        foreach (var step in steps)
        {
            switch (step.Kind)
            {
                case StepKind.Where:
                    where.Add(translator.Translate(step.Predicate!));
                    break;

                case StepKind.OrderBy:
                case StepKind.ThenBy:
                    orderBy.Add(DescribeOrdering(step, _quoteColumn));
                    break;

                case StepKind.Skip:
                    skip = step.Amount;
                    break;

                case StepKind.Take:
                    take = step.Amount;
                    break;
            }
        }

        // A count over an unordered, unpaged query cannot be affected by ORDER BY, and SQL Server refuses
        // ORDER BY inside the derived table COUNT wraps.
        if (dropOrdering && skip is null && take is null)
        {
            orderBy.Clear();
        }

        // Take(0) must return no rows on every provider. SQL Server rejects FETCH NEXT 0 ROWS, so the
        // empty result is expressed as a predicate instead of a zero row count.
        if (take == 0)
        {
            where.Add("1 = 0");
            skip = null;
            take = null;
        }

        var columns = projection.Kind == OpenIddictStoreProjectionKind.Entity
            ? "*"
            : string.Join(", ", projection.Columns.Select(column =>
                $"{_quoteColumn(column.Property)} AS {_quoteColumn(column.Alias)}"));

        // Paging without an explicit order would be non-deterministic; fall back to the stable primary key.
        if (orderBy.Count == 0 && (skip is not null || take is not null))
        {
            orderBy.Add(_quoteColumn("Id"));
        }

        var sql = $"SELECT {columns} FROM {_quoteTable(table)}";

        if (where.Count > 0)
        {
            sql += " WHERE " + string.Join(" AND ", where);
        }

        if (orderBy.Count > 0)
        {
            sql += " ORDER BY " + string.Join(", ", orderBy);
        }

        sql += " " + _dialect.BuildPaging(take, skip);

        if (outerPredicates.Count > 0)
        {
            // A Where after Take must filter the limited set, so it wraps the inner statement. The derived
            // table alias carries no AS: Oracle rejects AS on an inline view, and every other provider
            // accepts the bare form.
            var outer = string.Join(" AND ", outerPredicates);
            sql = $"SELECT * FROM ({sql.Trim()}) limited WHERE {outer}";
        }

        // Parameters are collected last: every predicate above may still register new ones.
        foreach (var pair in translator.Parameters)
        {
            parameters[pair.Key] = pair.Value;
        }

        return sql.Trim();
    }

    private string DescribeOrdering(Step step, Func<string, string> quoteColumn)
    {
        var column = OpenIddictStoreColumnMap.Resolve(typeof(TEntity), step.Property!);

        if (!column.IsSortable)
        {
            throw new NotSupportedException(
                $"The column '{column.Property}' cannot be used in ORDER BY: it is an unbounded text or JSON " +
                "column (design 9.2).");
        }

        return quoteColumn(column.Property) + (step.Descending ? " DESC" : " ASC");
    }

    private Projection BuildProjection(Step? step, OpenIddictStorePredicateTranslator translator)
    {
        if (step is null)
        {
            return Projection.ForEntity();
        }

        var body = step.Projection!;

        while (body is UnaryExpression unary && unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
        {
            body = unary.Operand;
        }

        // Selecting the entity itself (possibly boxed to object, which non-generic managers do).
        if (body.Type == typeof(TEntity) || body is ParameterExpression)
        {
            return Projection.ForEntity();
        }

        if (body is MemberExpression member && member.Expression?.Type == typeof(TEntity))
        {
            var column = OpenIddictStoreColumnMap.Resolve(typeof(TEntity), member.Member.Name);

            return Projection.ForColumns([(column.Property, column.Property)]);
        }

        if (body is NewExpression newExpression)
        {
            var columns = new List<(string Property, string Alias)>();

            for (var index = 0; index < newExpression.Arguments.Count; index++)
            {
                if (newExpression.Arguments[index] is not MemberExpression argument
                    || argument.Expression?.Type != typeof(TEntity))
                {
                    throw new NotSupportedException(
                        "A projection may only contain mapped columns of the store entity (design 9.2).");
                }

                var column = OpenIddictStoreColumnMap.Resolve(typeof(TEntity), argument.Member.Name);
                var alias = newExpression.Members?[index].Name
                    ?? newExpression.Constructor?.GetParameters()[index].Name
                    ?? column.Property;

                columns.Add((column.Property, alias));
            }

            if (columns.Count == 0)
            {
                throw new NotSupportedException("An empty projection cannot be translated.");
            }

            return Projection.ForColumns(columns);
        }

        if (body is MemberInitExpression memberInit)
        {
            var columns = new List<(string Property, string Alias)>();

            foreach (var binding in memberInit.Bindings)
            {
                if (binding is not MemberAssignment assignment
                    || assignment.Expression is not MemberExpression argument
                    || argument.Expression?.Type != typeof(TEntity))
                {
                    throw new NotSupportedException(
                        "A projection may only assign mapped columns of the store entity (design 9.2).");
                }

                var column = OpenIddictStoreColumnMap.Resolve(typeof(TEntity), argument.Member.Name);
                columns.Add((column.Property, binding.Member.Name));
            }

            if (columns.Count == 0)
            {
                throw new NotSupportedException("An empty projection cannot be translated.");
            }

            return Projection.ForColumns(columns);
        }

        throw new NotSupportedException(
            $"The projection '{body}' cannot be translated: only the entity, a mapped column, or a simple " +
            "DTO/anonymous object of mapped columns is supported (design 9).");
    }

    /// <summary>
    /// Flattens the callback into an ordered step list. The order matters: it is the order the caller
    /// wrote, and it is preserved all the way to the SQL (design 9).
    /// </summary>
    private static List<Step> Decompose(Expression expression)
    {
        var steps = new List<Step>();

        while (true)
        {
            if (expression is MethodCallExpression call && call.Method.DeclaringType == typeof(Queryable))
            {
                switch (call.Method.Name)
                {
                    case nameof(Queryable.Where):
                        steps.Add(Step.ForWhere(Unquote(call.Arguments[1])));
                        expression = call.Arguments[0];
                        continue;

                    case nameof(Queryable.OrderBy):
                        steps.Add(Step.ForOrdering(Unquote(call.Arguments[1]), descending: false, thenBy: false));
                        expression = call.Arguments[0];
                        continue;

                    case nameof(Queryable.OrderByDescending):
                        steps.Add(Step.ForOrdering(Unquote(call.Arguments[1]), descending: true, thenBy: false));
                        expression = call.Arguments[0];
                        continue;

                    case nameof(Queryable.ThenBy):
                        steps.Add(Step.ForOrdering(Unquote(call.Arguments[1]), descending: false, thenBy: true));
                        expression = call.Arguments[0];
                        continue;

                    case nameof(Queryable.ThenByDescending):
                        steps.Add(Step.ForOrdering(Unquote(call.Arguments[1]), descending: true, thenBy: true));
                        expression = call.Arguments[0];
                        continue;

                    case nameof(Queryable.Skip):
                        steps.Add(Step.ForPaging(StepKind.Skip, EvaluateAmount(call.Arguments[1])));
                        expression = call.Arguments[0];
                        continue;

                    case nameof(Queryable.Take):
                        steps.Add(Step.ForPaging(StepKind.Take, EvaluateAmount(call.Arguments[1])));
                        expression = call.Arguments[0];
                        continue;

                    case nameof(Queryable.Select):
                        steps.Add(Step.ForSelect(Unquote(call.Arguments[1])));
                        expression = call.Arguments[0];
                        continue;

                    default:
                        throw new NotSupportedException(
                            $"The query operator '{call.Method.Name}' is not supported (design 9.2).");
                }
            }

            // The root of a recorded callback tree is the entity set constant; reaching it ends the walk.
            if (expression is ConstantExpression constant && constant.Type.IsGenericType
                && constant.Type.GetGenericTypeDefinition() == typeof(OpenIddictStoreQueryable<>))
            {
                break;
            }

            if (expression is ParameterExpression || expression is ConstantExpression)
            {
                break;
            }

            throw new NotSupportedException(
                $"The query expression '{expression}' is not supported: the callback must start from the " +
                "store entity set (design 9.2).");
        }

        // The steps were collected from the outside in, so reverse them into call order.
        steps.Reverse();

        return steps;
    }

    private static LambdaExpression Unquote(Expression expression)
        => expression is UnaryExpression unary && unary.NodeType == ExpressionType.Quote
            ? (LambdaExpression)unary.Operand
            : expression as LambdaExpression
              ?? throw new NotSupportedException($"The expression '{expression}' is not a lambda.");

    private static int EvaluateAmount(Expression expression)
    {
        var value = expression is ConstantExpression constant
            ? constant.Value
            : Expression.Lambda(expression).Compile().DynamicInvoke();

        if (value is null)
        {
            throw new NotSupportedException("Skip and Take require a non-null count.");
        }

        var amount = Convert.ToInt32(value, CultureInfo.InvariantCulture);

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expression), "Skip and Take cannot be negative.");
        }

        return amount;
    }

    private enum StepKind
    {
        Where,
        OrderBy,
        ThenBy,
        Skip,
        Take,
        Select,
    }

    private sealed class Step
    {
        private Step(StepKind kind)
            => Kind = kind;

        public StepKind Kind { get; }

        public Expression? Predicate { get; set; }

        public string? Property { get; set; }

        public bool Descending { get; set; }

        public int Amount { get; set; }

        public Expression? Projection { get; set; }

        public static Step ForWhere(LambdaExpression lambda)
            => new(StepKind.Where) { Predicate = lambda.Body };

        public static Step ForOrdering(LambdaExpression lambda, bool descending, bool thenBy)
        {
            var body = lambda.Body;

            while (body is UnaryExpression unary && unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
            {
                body = unary.Operand;
            }

            if (body is not MemberExpression member || member.Expression?.Type != typeof(TEntity))
            {
                throw new NotSupportedException(
                    "ORDER BY only accepts a mapped column of the store entity (design 9.2).");
            }

            return new Step(thenBy ? StepKind.ThenBy : StepKind.OrderBy)
            {
                Property = member.Member.Name,
                Descending = descending,
            };
        }

        public static Step ForPaging(StepKind kind, int amount)
            => new(kind) { Amount = amount };

        public static Step ForSelect(LambdaExpression lambda)
            => new(StepKind.Select) { Projection = lambda.Body };
    }
}

/// <summary>What the generated SELECT returns, so the caller knows how to read each row.</summary>
internal enum OpenIddictStoreProjectionKind
{
    /// <summary>The full entity.</summary>
    Entity,

    /// <summary>One or more mapped columns, read by alias.</summary>
    Columns,
}

/// <summary>A translatable query: the SQL text, its parameters, and the projection shape.</summary>
internal sealed class OpenIddictStoreQueryPlan
{
    public OpenIddictStoreQueryPlan(
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        OpenIddictStoreProjectionKind kind,
        IOpenIddictStoreDialect dialect)
    {
        Sql = sql ?? throw new ArgumentNullException(nameof(sql));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Kind = kind;
        Dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
    }

    public string Sql { get; }

    public IReadOnlyDictionary<string, object?> Parameters { get; }

    public OpenIddictStoreProjectionKind Kind { get; }

    /// <summary>The dialect that produced the plan; the mapper needs it to read dates.</summary>
    public IOpenIddictStoreDialect Dialect { get; }
}

/// <summary>The projection of a translated query.</summary>
internal sealed class Projection
{
    private Projection(OpenIddictStoreProjectionKind kind, IReadOnlyList<(string Property, string Alias)> columns)
    {
        Kind = kind;
        Columns = columns;
    }

    public OpenIddictStoreProjectionKind Kind { get; }

    public IReadOnlyList<(string Property, string Alias)> Columns { get; }

    public static Projection ForEntity()
        => new(OpenIddictStoreProjectionKind.Entity, Array.Empty<(string, string)>());

    public static Projection ForColumns(IReadOnlyList<(string Property, string Alias)> columns)
        => new(OpenIddictStoreProjectionKind.Columns, columns);
}

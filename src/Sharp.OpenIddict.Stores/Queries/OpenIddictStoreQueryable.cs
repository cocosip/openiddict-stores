using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Sharp.OpenIddict.Stores.Queries;

/// <summary>
/// The queryable handed to an OpenIddict query callback. It never executes anything itself: each operator
/// the callback applies is recorded as an expression node, and the store translates the accumulated tree
/// into one SQL statement afterwards (design 9). Nothing is ever loaded into memory to be filtered.
/// </summary>
internal sealed class OpenIddictStoreQueryable<T> : IQueryable<T>, IOrderedQueryable<T>
{
    private readonly OpenIddictStoreQueryProvider _provider;

    public OpenIddictStoreQueryable(OpenIddictStoreQueryProvider provider, Expression expression)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
    }

    /// <summary>The recorded expression tree: the root entity set plus every applied operator.</summary>
    public Expression Expression { get; }

    public Type ElementType => typeof(T);

    public IQueryProvider Provider => _provider;

    public IEnumerator<T> GetEnumerator()
        => throw new NotSupportedException(
            "The query callback is translated to SQL; it is never enumerated in memory (design 9.2).");

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Records the expression tree of a query callback. <see cref="CreateQuery(Expression)"/> wraps the new
/// tree without evaluating it, which is what makes operator order observable to the translator.
/// </summary>
internal sealed class OpenIddictStoreQueryProvider : IQueryProvider
{
    public IQueryable CreateQuery(Expression expression)
    {
        var elementType = expression.Type.GetGenericArguments()[0];
        var queryableType = typeof(OpenIddictStoreQueryable<>).MakeGenericType(elementType);

        return (IQueryable)Activator.CreateInstance(queryableType, this, expression)!;
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        => new OpenIddictStoreQueryable<TElement>(this, expression);

    public object? Execute(Expression expression)
        => throw new NotSupportedException(
            "The query callback is translated to SQL; it is never executed in memory (design 9.2).");

    public TResult Execute<TResult>(Expression expression)
        => throw new NotSupportedException(
            "The query callback is translated to SQL; it is never executed in memory (design 9.2).");
}

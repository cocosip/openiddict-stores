using System;
using System.Collections.Generic;
using System.Data.Common;

namespace Sharp.OpenIddict.Stores;

/// <summary>
/// Dialect entry point for date parameters and reads. Stores do not depend directly on driver date conversion.
/// </summary>
internal static class OpenIddictStoreDateParameter
{
    /// <summary>Formats a UTC date parameter according to the dialect.</summary>
    public static object? Format(DateTime? value, IOpenIddictStoreDialect dialect)
        => dialect.FormatDate(OpenIddictStoreJson.NormalizeUtc(value));

    /// <summary>Reads a date column from the reader.</summary>
    public static DateTime? Parse(DbDataReader reader, int ordinal, IOpenIddictStoreDialect dialect)
        => dialect.ReadDate(reader, ordinal);
}

using System;

namespace Sharp.OpenIddict.Stores;

/// <summary>
/// The database schema does not match the version this package requires. The stores refuse to operate
/// instead of writing against an incompatible structure, and no DDL is ever issued automatically
/// (design 10.3, 17).
/// </summary>
public sealed class OpenIddictStoreSchemaVersionException : InvalidOperationException
{
    /// <summary>Creates the exception from the observed and the required version.</summary>
    /// <param name="message">The diagnostic message; it never contains connection strings or business data.</param>
    public OpenIddictStoreSchemaVersionException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception from the observed and the required version.</summary>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="innerException">The underlying provider failure, when the version table could not be read.</param>
    public OpenIddictStoreSchemaVersionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

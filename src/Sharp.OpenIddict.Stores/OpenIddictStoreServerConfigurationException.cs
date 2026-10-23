using System;

namespace Sharp.OpenIddict.Stores;

/// <summary>
/// The database server's instance-level configuration makes one of this library's guarantees impossible, so the
/// store refuses to run instead of silently degrading (design 16.1.1, 17).
/// </summary>
/// <remarks>
/// Deliberately not a schema-version failure: the installed tables are correct and running the install script
/// again changes nothing. The server itself has to be re-created or re-configured, which is why this is its own
/// type — an operator who sees it must not go looking for a missing script.
/// </remarks>
public sealed class OpenIddictStoreServerConfigurationException : InvalidOperationException
{
    /// <summary>Creates the exception with an operator-facing explanation.</summary>
    /// <param name="message">The diagnostic message; it never contains connection strings or business data.</param>
    public OpenIddictStoreServerConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with an operator-facing explanation and the underlying provider failure.</summary>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="innerException">The provider error raised while evaluating the probe.</param>
    public OpenIddictStoreServerConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

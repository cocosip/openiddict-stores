using System;
using Xunit;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// A fact that only runs when this run is configured for the server it needs. Without that configuration the
/// test is reported as skipped with the reason and the fix, so a missing server stays visible in the run
/// instead of looking like a pass (design 11).
/// </summary>
public class AcceptanceFactAttribute : FactAttribute
{
    public AcceptanceFactAttribute(string key, string description)
    {
        var reason = AcceptanceServers.Unavailable(key);

        if (reason is not null)
        {
            Skip = $"{description} {reason}";
        }
    }
}

/// <summary>
/// A fact for a driver that ships no net10 asset. <c>Microsoft.Data.SqlClient</c> 7.1.1 and
/// <c>DM.DmProvider</c> 8.3.1 stop at net9.0, so a net10 host would silently exercise the netstandard/net9
/// fallback and a green result would not be evidence for the declared target (design 5.1).
/// </summary>
public class Net8HostAcceptanceFactAttribute : AcceptanceFactAttribute
{
    public Net8HostAcceptanceFactAttribute(string key, string description) : base(key, description)
    {
        if (Skip is null && Environment.Version.Major > 9)
        {
            Skip = $"{description} must run on a net8.0 host: this provider's driver ships no net10 asset (design 5.1).";
        }
    }
}

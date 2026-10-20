using System;
using Xunit;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// A fact that only runs when the server it needs is configured in the user-secrets store. Without the
/// secret the test is reported as skipped with the reason, so a missing server stays visible in the run
/// instead of looking like a pass.
/// </summary>
public class AcceptanceFactAttribute : FactAttribute
{
    public AcceptanceFactAttribute(string key, string description)
    {
        if (!AcceptanceServers.IsConfigured(key))
        {
            Skip = $"{description} needs the '{key}' connection string in user-secrets " +
                   $"(dotnet user-secrets --id {AcceptanceServers.SecretsId}).";
        }
    }
}

/// <summary>
/// A DM8 acceptance fact. Besides the configured server it refuses to run where the driver cannot be real:
/// <c>DM.DmProvider</c> ships no net10 asset, so on a net10 host the run would silently exercise the
/// netstandard fallback instead of the driver's own net8.0 asset (design 5.1).
/// </summary>
public sealed class DmAcceptanceFactAttribute : AcceptanceFactAttribute
{
    public DmAcceptanceFactAttribute(string key, string description) : base(key, description)
    {
        if (Skip is null && Environment.Version.Major > 9)
        {
            Skip = $"{description} must run on a net8.0 host: DM.DmProvider ships no net10 asset.";
        }
    }
}

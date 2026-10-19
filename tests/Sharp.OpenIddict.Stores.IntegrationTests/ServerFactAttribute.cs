using System;
using Xunit;

namespace Sharp.OpenIddict.Stores.IntegrationTests;

/// <summary>
/// A fact that only runs when the external server it needs is configured. Without the variable the test is
/// reported as skipped with the reason, so a missing server is visible in the run instead of looking like a pass.
/// </summary>
public sealed class ServerFactAttribute : FactAttribute
{
    public ServerFactAttribute(string variable, string description)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable)))
        {
            Skip = $"{description} requires the {variable} environment variable.";
        }
    }
}

using System;
using System.IO;

namespace Sharp.OpenIddict.Stores.ServerAcceptance;

/// <summary>
/// Locates the repository root once, by walking up until the solution file appears. The acceptance run reads
/// the shipped install templates from their source location (they are templates, not runnable scripts) and
/// looks for <c>databases.json</c> there, so both need the same anchor.
/// </summary>
internal static class RepositoryLayout
{
    public static string Root { get; } = Locate();

    private static string Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sharp.OpenIddict.Stores.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root (Sharp.OpenIddict.Stores.slnx).");
    }
}

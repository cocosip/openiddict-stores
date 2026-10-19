using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenIddict.Abstractions;
using Xunit;

namespace Sharp.OpenIddict.Stores.Tests;

/// <summary>
/// Contract baseline from design 7 and docs/contracts/7x-store-contracts.md: locks down the four Store
/// interfaces of OpenIddict 7.7.2 and their member counts. An upstream upgrade should break these assertions
/// first rather than silently drifting inside the implementation.
/// </summary>
public sealed class StoreContractTests
{
    private static readonly (Type Store, string Name, int Members)[] Expected =
    [
        (typeof(IOpenIddictApplicationStore<>), "IOpenIddictApplicationStore", 42),
        (typeof(IOpenIddictAuthorizationStore<>), "IOpenIddictAuthorizationStore", 32),
        (typeof(IOpenIddictScopeStore<>), "IOpenIddictScopeStore", 28),
        (typeof(IOpenIddictTokenStore<>), "IOpenIddictTokenStore", 43),
    ];

    [Fact]
    public void StoreInterfaces_HaveExpectedMemberCounts()
    {
        var actual = Expected.Select(entry => $"{entry.Name}={entry.Store.GetMethods().Length}").ToList();
        var expected = Expected.Select(entry => $"{entry.Name}={entry.Members}").ToList();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void StoreInterfaces_TotalOneHundredAndFortyFiveMembers()
        => Assert.Equal(145, Expected.Sum(entry => entry.Store.GetMethods().Length));

    [Fact]
    public void NoResourceOrSessionStoreExists()
    {
        var names = typeof(IOpenIddictApplicationStore<>).Assembly
            .GetExportedTypes()
            .Where(type => type.IsInterface && type.Name.StartsWith("IOpenIddict", StringComparison.Ordinal))
            .Select(type => type.Name)
            .Where(name => name.Contains("Resource", StringComparison.Ordinal) || name.Contains("Session", StringComparison.Ordinal))
            .ToList();

        Assert.True(names.Count == 0, "7.7.2 should not expose a Resource/Session Store: " + string.Join(", ", names));
    }

    [Fact]
    public void CountAsync_HasNoStateOverload()
    {
        // The 7.x CountAsync query overload only takes (query, cancellationToken); only 8.x adds a three-parameter TState overload.
        foreach (var entry in Expected)
        {
            var overloads = entry.Store.GetMethods()
                .Where(method => method.Name == "CountAsync")
                .Select(method => method.GetParameters().Length)
                .OrderBy(count => count)
                .ToList();

            Assert.Equal([1, 2], overloads);
        }
    }

    [Fact]
    public void FindAsync_UsesPositionalStringParameters()
    {
        var authorization = typeof(IOpenIddictAuthorizationStore<>).GetMethods()
            .Single(method => method.Name == "FindAsync");
        var token = typeof(IOpenIddictTokenStore<>).GetMethods()
            .Single(method => method.Name == "FindAsync");

        Assert.Equal(
            ["String", "String", "String", "String", "Nullable`1", "CancellationToken"],
            authorization.GetParameters().Select(parameter => parameter.ParameterType.Name));

        Assert.Equal(
            ["String", "String", "String", "String", "CancellationToken"],
            token.GetParameters().Select(parameter => parameter.ParameterType.Name));
    }

    [Fact]
    public void ScopeStore_ExposesResourcesAsStrings()
    {
        var scope = typeof(IOpenIddictScopeStore<>);

        Assert.Equal(typeof(string), scope.GetMethod("FindByResourceAsync")!.GetParameters()[0].ParameterType);
        Assert.Equal("ImmutableArray`1", scope.GetMethod("GetResourcesAsync")!.ReturnType.GetGenericArguments()[0].Name);
        Assert.Equal("ImmutableArray`1", scope.GetMethod("SetResourcesAsync")!.GetParameters()[1].ParameterType.Name);
    }

    [Fact]
    public void RevokeByApplicationIdAndSubject_KeepOptionalCancellationToken()
    {
        var token = typeof(IOpenIddictTokenStore<>);

        Assert.True(token.GetMethod("RevokeByApplicationIdAsync")!.GetParameters()[1].HasDefaultValue);
        Assert.True(token.GetMethod("RevokeBySubjectAsync")!.GetParameters()[1].HasDefaultValue);
        Assert.False(token.GetMethod("RevokeByAuthorizationIdAsync")!.GetParameters()[1].HasDefaultValue);
    }

    [Fact]
    public void ContractDocument_MatchesEveryInterfaceMember()
    {
        var document = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "contracts", "7x-store-contracts.md"));

        var missing = new List<string>();

        foreach (var entry in Expected)
        {
            var section = ExtractSection(document, entry.Name);
            var documented = section
                .Split('\n')
                .Where(line => line.StartsWith("| ", StringComparison.Ordinal) && line.Contains('`'))
                .Select(line => line.Split('`')[1])
                .ToHashSet(StringComparer.Ordinal);

            Assert.Equal(entry.Members, documented.Count);

            foreach (var method in entry.Store.GetMethods())
            {
                var signature = Render(method);
                if (!documented.Contains(signature))
                {
                    missing.Add($"{entry.Name}.{signature}");
                }
            }
        }

        Assert.True(missing.Count == 0,
            "The contract document does not match the 7.7.2 interfaces:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    private static string ExtractSection(string document, string storeName)
    {
        var start = document.IndexOf("### " + storeName, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The contract document is missing the {storeName} section.");

        var next = document.IndexOf("\n### ", start + 4, StringComparison.Ordinal);
        return next < 0 ? document[start..] : document[start..next];
    }

    private static string Render(MethodInfo method)
    {
        var parameters = string.Join(", ", method.GetParameters().Select(parameter =>
        {
            var text = $"{Render(parameter.ParameterType)} {parameter.Name}";
            return parameter.HasDefaultValue ? text + " = default" : text;
        }));

        return $"{Render(method.ReturnType)} {method.Name}({parameters})";
    }

    private static string Render(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Render(underlying) + "?";
        }

        var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["String"] = "string",
            ["Int64"] = "long",
            ["Int32"] = "int",
            ["Boolean"] = "bool",
            ["Object"] = "object",
        };

        var name = type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
        {
            name = name[..tick];
        }

        if (aliases.TryGetValue(name, out var alias))
        {
            return alias;
        }

        if (type.IsGenericType)
        {
            var arguments = string.Join(", ", type.GetGenericArguments().Select(Render));
            return $"{name}<{arguments}>";
        }

        return name;
    }

    private static string RepositoryRoot { get; } = LocateRepositoryRoot();

    private static string LocateRepositoryRoot()
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

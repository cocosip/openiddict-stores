using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Xunit;

namespace Sharp.OpenIddict.Stores.IntegrationTests;

/// <summary>
/// Low target framework asset loading (COMPAT-03): the netstandard2.0 and netstandard2.1 assemblies must be
/// loadable and callable on their own, reaching their real code paths instead of silently resolving to the
/// net10 asset the host would normally pick. The host is still net10, so this proves the assets are
/// loadable; it does not claim that the old runtimes were exercised.
/// </summary>
public sealed class LowTargetFrameworkAssetTests
{
    [Theory]
    [InlineData("netstandard2.0", ".NETStandard,Version=v2.0")]
    [InlineData("netstandard2.1", ".NETStandard,Version=v2.1")]
    public void LowTargetFrameworkAssembly_LoadsAndRunsItsOwnCodePaths(string framework, string expectedFrameworkName)
    {
        var directory = Path.Combine(RepositoryRoot, "src", "Sharp.OpenIddict.Stores", "bin", "Release", framework);
        var assemblyPath = Path.Combine(directory, "Sharp.OpenIddict.Stores.dll");

        Assert.True(File.Exists(assemblyPath),
            $"The {framework} asset was not built at '{assemblyPath}'.");

        // A dedicated load context keeps the low-TFM assembly from resolving to an already loaded net10 copy.
        var context = new AssemblyLoadContext("compat-" + framework, isCollectible: true);

        try
        {
            // The whole dependency closure is resolved for this target framework. Falling back to the host
            // would bind the low-TFM store implementations to net10 interfaces, which fail to load because
            // the interface shapes differ between the 7.7.2 target frameworks.
            var closure = ReadDependencyClosure(directory, framework);

            context.Resolving += (loadContext, name) =>
            {
                if (name.Name is not null && closure.TryGetValue(name.Name, out var path))
                {
                    return loadContext.LoadFromAssemblyPath(path);
                }

                var local = Path.Combine(directory, name.Name + ".dll");

                if (File.Exists(local))
                {
                    return loadContext.LoadFromAssemblyPath(local);
                }

                return ResolveFromPackageCache(loadContext, name);
            };

            var assembly = context.LoadFromAssemblyPath(assemblyPath);

            // The asset must be the one built for this framework, not an accidental net10 copy.
            var attribute = assembly.GetCustomAttribute<TargetFrameworkAttribute>();

            Assert.NotNull(attribute);
            Assert.Equal(expectedFrameworkName, attribute!.FrameworkName);

            // Exercise the real identifier helpers, not a stub.
            var identifiers = assembly.GetType(
                "Sharp.OpenIddict.Stores.OpenIddictStoreIdentifiers", throwOnError: true)!;

            var validate = identifiers.GetMethod("Validate", BindingFlags.Public | BindingFlags.Static)!;

            // Validate returns the value unchanged once it satisfies the identifier rule.
            var valid = (string)validate.Invoke(null, ["My_Table1", "value"])!;

            Assert.Equal("My_Table1", valid);

            // The rule rejects whitespace-only, digit-leading, and SQL-injection-shaped values.
            var isValid = identifiers.GetMethod("IsValid", BindingFlags.Public | BindingFlags.Static)!;

            Assert.Equal(false, isValid.Invoke(null, ["   "]));
            Assert.Equal(false, isValid.Invoke(null, ["1table"]));
            Assert.Equal(false, isValid.Invoke(null, ["a; DROP TABLE x--"]));
            Assert.Equal(true, isValid.Invoke(null, ["My_Table1"]));

            // Rejection must happen inside the asset, proving its guard code ran rather than the host's.
            var rejected = Record.Exception(() => validate.Invoke(null, ["bad-name", "value"]));

            Assert.IsType<TargetInvocationException>(rejected);
            Assert.IsType<ArgumentException>(rejected!.InnerException);

            // The SQL prefixer is used by every install script path; run it for real.
            var applyPrefix = identifiers.GetMethod("ApplyPrefix", BindingFlags.Public | BindingFlags.Static)!;

            // Install scripts use the __PREFIX__ placeholder; the call must substitute every occurrence.
            var prefixed = (string)applyPrefix.Invoke(
                null, ["CREATE TABLE __PREFIX__Applications (Id TEXT); INDEX ON __PREFIX__Applications (Id)", "Oidc"])!;

            Assert.Contains("CREATE TABLE OidcApplications", prefixed, StringComparison.Ordinal);
            Assert.Contains("INDEX ON OidcApplications", prefixed, StringComparison.Ordinal);
            Assert.DoesNotContain("__PREFIX__", prefixed, StringComparison.Ordinal);

            // The statement splitter drives script execution; a two-statement script must yield two parts.
            var splitStatements = identifiers.GetMethod(
                "SplitStatements", BindingFlags.Public | BindingFlags.Static)!;

            var statements = (string[])splitStatements.Invoke(
                null, ["CREATE TABLE A (Id TEXT);\nCREATE TABLE B (Id TEXT);"])!;

            Assert.Equal(2, statements.Length);

            // The store types are the reason the asset exists; their metadata must be present. They are
            // located by name rather than instantiated, because their interface is defined by the
            // target-framework-specific OpenIddict.Abstractions the asset was compiled against.
            var storeNames = assembly.GetType("Sharp.OpenIddict.Stores.OpenIddictStoreBase", false) is not null;

            Assert.True(storeNames,
                $"The {framework} asset does not expose the store base type.");

            // The connection abstraction is the async surface of the asset; it must be present and async.
            var factoryType = assembly.GetType(
                "Sharp.OpenIddict.Stores.IOpenIddictStoreConnectionFactory", throwOnError: true)!;

            Assert.True(factoryType.IsInterface);

            var create = factoryType.GetMethods().Single(method => method.Name == "CreateConnectionAsync");

            Assert.Equal("ValueTask`1", create.ReturnType.Name);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void AllProductAssembliesExistForEveryTargetFramework()
    {
        var products = new[]
        {
            "Sharp.OpenIddict.Stores.Models",
            "Sharp.OpenIddict.Stores",
            "Sharp.OpenIddict.Stores.SqlServer",
            "Sharp.OpenIddict.Stores.PostgreSql",
            "Sharp.OpenIddict.Stores.MySql",
            "Sharp.OpenIddict.Stores.Sqlite",
            "Sharp.OpenIddict.Stores.Oracle",
            "Sharp.OpenIddict.Stores.Dm",
        };

        var missing = new List<string>();

        foreach (var product in products)
        {
            foreach (var framework in new[] { "netstandard2.0", "netstandard2.1", "net8.0", "net9.0", "net10.0" })
            {
                var path = Path.Combine(RepositoryRoot, "src", product, "bin", "Release", framework, product + ".dll");

                if (!File.Exists(path))
                {
                    missing.Add($"{product}/{framework}");
                }
            }
        }

        Assert.True(missing.Count == 0, "These assets were not built: " + string.Join(", ", missing));
    }

    [Fact]
    public void LowTargetFrameworkAsset_TargetsNetStandardNotANewerRuntime()
    {
        var path = Path.Combine(
            RepositoryRoot, "src", "Sharp.OpenIddict.Stores", "bin", "Release", "netstandard2.0",
            "Sharp.OpenIddict.Stores.dll");

        Assert.True(File.Exists(path));

        // The compiled target framework is the authoritative record: it cannot be a net8+ build wearing a
        // netstandard folder name.
        var assembly = Assembly.Load(File.ReadAllBytes(path));
        var attribute = assembly.GetCustomAttribute<TargetFrameworkAttribute>();

        Assert.NotNull(attribute);
        Assert.StartsWith(".NETStandard,Version=v2.0", attribute!.FrameworkName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the assembly map for one target framework from its deps file, so the load context sees exactly
    /// the versions that framework was compiled against.
    /// </summary>
    private static Dictionary<string, string> ReadDependencyClosure(string outputDirectory, string framework)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var depsPath = Path.Combine(outputDirectory, "Sharp.OpenIddict.Stores.deps.json");

        if (!File.Exists(depsPath))
        {
            return result;
        }

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(depsPath));

        // The runtime section maps "Package/Version" to the concrete assets for the active target framework.
        if (!document.RootElement.TryGetProperty("targets", out var targets))
        {
            return result;
        }

        var nugetRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

        foreach (var target in targets.EnumerateObject())
        {
            // The runtime target key carries a trailing slash (".NETStandard,Version=v2.0/").
            if (!target.Name.StartsWith(".NETStandard", StringComparison.OrdinalIgnoreCase)
                || !target.Value.EnumerateObject().Any())
            {
                continue;
            }

            foreach (var library in target.Value.EnumerateObject())
            {
                if (!library.Value.TryGetProperty("runtime", out var runtime))
                {
                    continue;
                }

                foreach (var asset in runtime.EnumerateObject())
                {
                    var name = Path.GetFileNameWithoutExtension(asset.Name);

                    if (result.ContainsKey(name))
                    {
                        continue;
                    }

                    var parts = library.Name.Split('/');

                    if (parts.Length != 2)
                    {
                        continue;
                    }

                    var candidate = Path.Combine(nugetRoot, parts[0].ToLowerInvariant(), parts[1],
                        asset.Name.Replace('/', Path.DirectorySeparatorChar));

                    if (File.Exists(candidate))
                    {
                        result[name] = candidate;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Resolves a netstandard-era shim from the NuGet package cache.</summary>
    private static Assembly? ResolveFromPackageCache(AssemblyLoadContext context, AssemblyName name)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

        if (!Directory.Exists(root) || name.Name is null || name.Version is null)
        {
            return null;
        }

        var packageRoot = Path.Combine(root, name.Name.ToLowerInvariant());

        if (!Directory.Exists(packageRoot))
        {
            return null;
        }

        // The assembly version does not always match the package version (10.0.0.8 ships as package 10.0.8),
        // so the newest matching package is used instead of reconstructing a directory name.
        var major = name.Version.Major;

        var candidate = Directory.GetFiles(packageRoot, name.Name + ".dll", SearchOption.AllDirectories)
            .Where(path => path.Contains("netstandard2.0", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "ref" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            .Where(path => PackageMatchesMajor(path, packageRoot, major))
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .FirstOrDefault();

        return candidate is null ? null : context.LoadFromAssemblyPath(candidate);
    }

    /// <summary>Checks that the package directory holding the assembly belongs to the required major line.</summary>
    private static bool PackageMatchesMajor(string assemblyPath, string packageRoot, int major)
    {
        var relative = assemblyPath.Substring(packageRoot.Length).TrimStart(Path.DirectorySeparatorChar);
        var versionDirectory = relative.Split(Path.DirectorySeparatorChar)[0];

        return Version.TryParse(versionDirectory, out var version) && version.Major == major;
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
            ?? throw new InvalidOperationException("Repository root (Sharp.OpenIddict.Stores.slnx) was not found.");
    }
}

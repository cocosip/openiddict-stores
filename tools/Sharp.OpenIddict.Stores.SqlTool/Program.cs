using System;
using System.Text;
using Sharp.OpenIddict.Stores.SqlTool;

namespace Sharp.OpenIddict.Stores.SqlTool;

/// <summary>
/// Command-line entry: renders one provider's deployment SQL with the configured table prefix and schema.
/// The tool never connects to a database; it produces the script a deployment executes (design 10.1).
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            Run(args);
            return 0;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine("error: " + exception.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(Usage());
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("error: " + exception.Message);
            return 1;
        }
    }

    private static void Run(string[] args)
    {
        if (args.Length == 0 || Contains(args, "-h") || Contains(args, "--help"))
        {
            Console.Write(Usage());
            return;
        }

        if (Contains(args, "--list"))
        {
            foreach (var item in DeploymentScriptFactory.ListProviders())
            {
                Console.WriteLine($"{item.Name}  (dialect: {item.Dialect.GetType().Name})");

                foreach (var entry in item.Entries)
                {
                    Console.WriteLine($"    server: {entry.Server}"
                        + (entry.MinimumVersion is null ? string.Empty : $"  (minimum {entry.MinimumVersion})"));
                    Console.WriteLine($"    script: {entry.Path}");
                    Console.WriteLine($"    sha256: {entry.Sha256}");
                }
            }

            return;
        }

        var provider = Value(args, "--provider")
            ?? throw new ArgumentException("The --provider option is required (or use --list).");
        var mode = Value(args, "--mode");
        var prefix = Value(args, "--prefix") ?? OpenIddict.Stores.OpenIddictStoreOptions.DefaultTablePrefix;
        var schema = Value(args, "--schema");
        var server = Value(args, "--server");
        var output = Value(args, "--out");

        var sql = DeploymentScriptFactory.Render(provider, mode, prefix, schema, server);

        if (output is null)
        {
            Console.WriteLine("-- Rendered for provider '{0}', prefix '{1}', schema '{2}'.", provider, prefix, schema ?? "(provider default)");
            Console.Write(sql);
            return;
        }

        // UTF-8 without BOM, matching the encoding checks the shipped templates are held to.
        File.WriteAllText(output, sql, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"Wrote {output} ({sql.Length} characters).");
    }

    private static bool Contains(string[] args, string name)
        => args.Any(arg => string.Equals(arg, name, StringComparison.Ordinal));

    private static string? Value(string[] args, string name)
    {
        var index = Array.FindIndex(args, arg => string.Equals(arg, name, StringComparison.Ordinal));

        return index < 0 || index + 1 >= args.Length
            ? null
            : args[index + 1];
    }

    private static string Usage() => """
        Sharp.OpenIddict.Stores SQL generator

        Renders a provider's install script template with the configured table prefix and schema.
        The output contains the tables, keys, indexes, and the SchemaVersions seed record; execute
        it per the provider's Sql/README.md. This tool never connects to a database.

        Usage:
          dotnet run --project tools/Sharp.OpenIddict.Stores.SqlTool -- <options>

        Options:
          --list                      Show every provider and its manifest entries.
          --provider <name>           SqlServer | PostgreSql | MySql | Sqlite | Oracle | Dm
          --mode <mode>               Dameng only: native (default) | oracle-compatible | mysql-compatible.
                                      Must match the COMPATIBLE_MODE of the target instance.
          --server <substring>        Disambiguate multi-server providers (e.g. MySQL vs MariaDB) when they
                                      ship different scripts.
          --prefix <name>             Table prefix; default 'OpenIddict'. Must match the application's
                                      SetTablePrefix value.
          --schema <name>             Dedicated schema; default is the provider default schema. Must match
                                      the application's SetSchema value.
          --out <path>                Write UTF-8 (no BOM) to a file instead of standard output.

        Examples:
          ... --provider Sqlite
          ... --provider Dm --mode oracle-compatible --prefix Oidc --schema OIDC --out dm-oracle-install.sql
        """;
}

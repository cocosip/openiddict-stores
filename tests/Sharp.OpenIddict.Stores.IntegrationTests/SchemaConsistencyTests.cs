using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Sharp.OpenIddict.Stores.IntegrationTests;

/// <summary>
/// Checks that each install script realises the logical schema of design 15.1–15.3 (MIG-04), so the six
/// providers cannot drift apart while each keeps the physical types its server requires.
/// </summary>
/// <remarks>
/// <para>
/// The logical shape comes from the design, not from the templates: a check that derived the expected shape by
/// reading the templates could only ever confirm that the templates agree with themselves, and would keep
/// passing if a column went missing from all six. The physical types legitimately differ (design 15.5), so each
/// declaration is instead checked for whether it can represent the declared logical type.
/// </para>
/// <para>
/// The logical type cannot be recovered from the declaration alone. SQLite stores both large text and UTC
/// timestamps as <c>TEXT</c>, so a classifier that guessed from the type would label <c>CreationDate</c> a
/// large-text column. That is why the design's mapping is the input here and the declaration is what gets
/// judged, not the other way round.
/// </para>
/// </remarks>
public sealed class SchemaConsistencyTests
{
    private enum Family
    {
        Identifier,
        Text,
        LargeText,
        UtcDateTime,
        Hash32,
        Integer
    }

    private sealed record Column(string Name, string Declaration, bool Nullable, bool Collated);

    /// <summary>
    /// The logical schema from design 15.1–15.3: every table, every column, its logical type, and — for bounded
    /// text — the declared length.
    /// </summary>
    private static readonly (string Table, string Column, Family Family, int? Length)[] LogicalSchema =
    [
        ("Applications", "Id", Family.Identifier, 100),
        ("Applications", "ConcurrencyToken", Family.Text, 50),
        ("Applications", "ApplicationType", Family.Text, 50),
        ("Applications", "ClientId", Family.Text, 100),
        ("Applications", "ClientSecret", Family.LargeText, null),
        ("Applications", "ClientType", Family.Text, 50),
        ("Applications", "ConsentType", Family.Text, 50),
        ("Applications", "DisplayName", Family.LargeText, null),
        ("Applications", "DisplayNames", Family.LargeText, null),
        ("Applications", "JsonWebKeySet", Family.LargeText, null),
        ("Applications", "Permissions", Family.LargeText, null),
        ("Applications", "PostLogoutRedirectUris", Family.LargeText, null),
        ("Applications", "Properties", Family.LargeText, null),
        ("Applications", "RedirectUris", Family.LargeText, null),
        ("Applications", "Requirements", Family.LargeText, null),
        ("Applications", "Settings", Family.LargeText, null),

        ("Authorizations", "Id", Family.Identifier, 100),
        ("Authorizations", "ConcurrencyToken", Family.Text, 50),
        ("Authorizations", "ApplicationId", Family.Identifier, 100),
        ("Authorizations", "CreationDate", Family.UtcDateTime, null),
        ("Authorizations", "Properties", Family.LargeText, null),
        ("Authorizations", "Scopes", Family.LargeText, null),
        ("Authorizations", "Status", Family.Text, 50),
        ("Authorizations", "Subject", Family.Text, 400),
        ("Authorizations", "Type", Family.Text, 50),

        ("Scopes", "Id", Family.Identifier, 100),
        ("Scopes", "ConcurrencyToken", Family.Text, 50),
        ("Scopes", "Description", Family.LargeText, null),
        ("Scopes", "Descriptions", Family.LargeText, null),
        ("Scopes", "DisplayName", Family.LargeText, null),
        ("Scopes", "DisplayNames", Family.LargeText, null),
        ("Scopes", "Name", Family.Text, 200),
        ("Scopes", "Properties", Family.LargeText, null),
        ("Scopes", "Resources", Family.LargeText, null),

        ("Tokens", "Id", Family.Identifier, 100),
        ("Tokens", "ConcurrencyToken", Family.Text, 50),
        ("Tokens", "ApplicationId", Family.Identifier, 100),
        ("Tokens", "AuthorizationId", Family.Identifier, 100),
        ("Tokens", "CreationDate", Family.UtcDateTime, null),
        ("Tokens", "ExpirationDate", Family.UtcDateTime, null),
        ("Tokens", "Payload", Family.LargeText, null),
        ("Tokens", "Properties", Family.LargeText, null),
        ("Tokens", "RedemptionDate", Family.UtcDateTime, null),
        ("Tokens", "ReferenceId", Family.Text, 100),
        ("Tokens", "Status", Family.Text, 50),
        ("Tokens", "Subject", Family.Text, 400),
        ("Tokens", "Type", Family.Text, 150),

        ("ApplicationRedirectUris", "ApplicationId", Family.Identifier, 100),
        ("ApplicationRedirectUris", "UriHash", Family.Hash32, null),
        ("ApplicationRedirectUris", "Uri", Family.LargeText, null),
        ("ApplicationRedirectUris", "Position", Family.Integer, null),

        ("ApplicationPostLogoutRedirectUris", "ApplicationId", Family.Identifier, 100),
        ("ApplicationPostLogoutRedirectUris", "UriHash", Family.Hash32, null),
        ("ApplicationPostLogoutRedirectUris", "Uri", Family.LargeText, null),
        ("ApplicationPostLogoutRedirectUris", "Position", Family.Integer, null),

        ("AuthorizationScopes", "AuthorizationId", Family.Identifier, 100),
        ("AuthorizationScopes", "ScopeHash", Family.Hash32, null),
        ("AuthorizationScopes", "Scope", Family.LargeText, null),
        ("AuthorizationScopes", "Position", Family.Integer, null),

        ("ScopeResources", "ScopeId", Family.Identifier, 100),
        ("ScopeResources", "ResourceHash", Family.Hash32, null),
        ("ScopeResources", "Resource", Family.LargeText, null),
        ("ScopeResources", "Position", Family.Integer, null),

        ("SchemaVersions", "Series", Family.Text, 10),
        ("SchemaVersions", "Version", Family.Integer, null),
        ("SchemaVersions", "UpdatedUtc", Family.UtcDateTime, null)
    ];

    /// <summary>Columns that must be NOT NULL: the keys, the auxiliary tables, and the version record.</summary>
    private static readonly HashSet<string> RequiredColumns = new(StringComparer.Ordinal)
    {
        "Applications.Id", "Applications.ConcurrencyToken",
        "Authorizations.Id", "Authorizations.ConcurrencyToken",
        "Scopes.Id", "Scopes.ConcurrencyToken",
        "Tokens.Id", "Tokens.ConcurrencyToken",
        "ApplicationRedirectUris.ApplicationId", "ApplicationRedirectUris.UriHash",
        "ApplicationRedirectUris.Uri", "ApplicationRedirectUris.Position",
        "ApplicationPostLogoutRedirectUris.ApplicationId", "ApplicationPostLogoutRedirectUris.UriHash",
        "ApplicationPostLogoutRedirectUris.Uri", "ApplicationPostLogoutRedirectUris.Position",
        "AuthorizationScopes.AuthorizationId", "AuthorizationScopes.ScopeHash",
        "AuthorizationScopes.Scope", "AuthorizationScopes.Position",
        "ScopeResources.ScopeId", "ScopeResources.ResourceHash",
        "ScopeResources.Resource", "ScopeResources.Position",
        "SchemaVersions.Series", "SchemaVersions.Version", "SchemaVersions.UpdatedUtc"
    };

    public static TheoryData<string> Providers => new() { "Sqlite", "SqlServer", "PostgreSql", "MySql", "Oracle", "Dm" };

    [Theory]
    [MemberData(nameof(Providers))]
    public void InstallScript_DeclaresExactlyTheDesignSchema(string provider)
    {
        var tables = Parse(provider);

        var declared = tables
            .SelectMany(table => table.Value.Select(column => $"{table.Key}.{column.Name}"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var expected = LogicalSchema
            .Select(entry => $"{entry.Table}.{entry.Column}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Missing and extra columns both fail here: a column in one provider only would be a schema divergence.
        Assert.Equal(expected, declared);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void EveryColumn_UsesAPhysicalTypeThatCanRepresentItsLogicalType(string provider)
    {
        var tables = Parse(provider);

        foreach (var (table, column, family, length) in LogicalSchema)
        {
            var actual = tables[table].Single(candidate => candidate.Name == column);

            Assert.True(CanRepresent(actual.Declaration, family),
                $"{provider}: {table}.{column} is declared '{actual.Declaration}', which cannot represent {family} (design 15.5).");

            if (length is not null)
            {
                // A bounded column wider than the model silently accepts values the Store rejects elsewhere;
                // narrower truncates or throws at insert time.
                Assert.True(DeclaredLength(actual.Declaration) == length,
                    $"{provider}: {table}.{column} should be {family}({length}) but is '{actual.Declaration}'.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void NullabilityMatchesTheDesign(string provider)
    {
        var tables = Parse(provider);

        foreach (var (table, column, _, _) in LogicalSchema)
        {
            var actual = tables[table].Single(candidate => candidate.Name == column);
            var required = RequiredColumns.Contains($"{table}.{column}");

            Assert.True(actual.Nullable != required,
                $"{provider}: {table}.{column} should be {(required ? "NOT NULL" : "nullable")} (design 15.1–15.3).");
        }
    }

    /// <summary>
    /// A unique key on a string column must not depend on a case-insensitive server default, or the database
    /// would reject "Client" as a duplicate of "client" while the Store treats them as different values. The
    /// unique keys are a subset of the columns covered by <see cref="ExactComparedColumns_DeclareTheBinaryCollation"/>;
    /// this test pins the three business keys specifically (design 16.1.1).
    /// </summary>
    [Theory]
    [InlineData("SqlServer", "Latin1_General_100_BIN2")]
    [InlineData("PostgreSql", "\"C\"")]
    public void UniqueStringKeys_DoNotDependOnTheServerDefaultCollation(string provider, string expected)
    {
        var tables = Parse(provider);

        foreach (var (table, column) in new[]
                 {
                     ("Applications", "ClientId"), ("Scopes", "Name"), ("Tokens", "ReferenceId")
                 })
        {
            var declaration = tables[table].Single(candidate => candidate.Name == column).Declaration;

            Assert.True(declaration.Contains("COLLATE", StringComparison.OrdinalIgnoreCase),
                $"{provider}: {table}.{column} is a unique key and needs its own collation.");

            Assert.Contains(expected, declaration, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Every column the stores compare exactly must carry the binary collation in the DDL, not only inside the
    /// predicate. Forcing <c>COLLATE</c> onto a column declared with a different collation makes the predicate
    /// non-sargable (a seek becomes a scan on the primary key, the hottest lookup in the library), and the
    /// conditional writes that compare <c>Id</c> would otherwise depend on the database default: on a
    /// case-insensitive database an update for <c>ABC</c> would hit the row stored as <c>abc</c> while the read
    /// path for the same string returns null (design 16.1.1).
    /// </summary>
    [Theory]
    [InlineData("SqlServer", "Latin1_General_100_BIN2")]
    public void ExactComparedColumns_DeclareTheBinaryCollation(string provider, string expected)
    {
        var tables = Parse(provider);

        foreach (var (table, column) in new[]
                 {
                     ("Applications", "Id"), ("Applications", "ConcurrencyToken"), ("Applications", "ClientId"),
                     ("Authorizations", "Id"), ("Authorizations", "ConcurrencyToken"),
                     ("Authorizations", "ApplicationId"), ("Authorizations", "Status"),
                     ("Authorizations", "Subject"), ("Authorizations", "Type"),
                     ("Scopes", "Id"), ("Scopes", "ConcurrencyToken"), ("Scopes", "Name"),
                     ("Tokens", "Id"), ("Tokens", "ConcurrencyToken"), ("Tokens", "ApplicationId"),
                     ("Tokens", "AuthorizationId"), ("Tokens", "ReferenceId"), ("Tokens", "Status"),
                     ("Tokens", "Subject"), ("Tokens", "Type")
                 })
        {
            var declaration = tables[table].Single(candidate => candidate.Name == column).Declaration;

            Assert.Contains(expected, declaration, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void EveryTableHasTheKeysTheStoresRelyOn(string provider)
    {
        var template = TemplateOf(provider);
        var tables = Parse(provider);

        // Parse already asserted nine tables; this pins which ones, without depending on the quoting style
        // ("", [] or ``), which differs per provider.
        Assert.Equal(
            new[]
            {
                "Applications", "Authorizations", "Scopes", "Tokens",
                "ApplicationRedirectUris", "ApplicationPostLogoutRedirectUris",
                "AuthorizationScopes", "ScopeResources", "SchemaVersions"
            }.OrderBy(name => name, StringComparer.Ordinal),
            tables.Keys.OrderBy(name => name, StringComparer.Ordinal));

        // Names are still qualified: every table must carry the configurable prefix, or a custom prefix would
        // collide with another installation in the same schema.
        foreach (var table in tables.Keys)
        {
            Assert.Matches($@"__PREFIX__{table}\b", template);
        }

        // Three unique keys (ClientId, Name, ReferenceId) and seven foreign keys, in every provider.
        Assert.Equal(3, CountUniqueKeys(template));
        Assert.Equal(7, Regex.Matches(template, @"FOREIGN KEY").Count);
    }

    private static int CountUniqueKeys(string template)
        => Regex.Matches(template, @"CREATE UNIQUE INDEX").Count + Regex.Matches(template, @"CONSTRAINT\s+[^(\r\n]+\sUNIQUE\s*\(").Count;

    /// <summary>
    /// Whether a physical declaration can hold a value of the given logical type (design 15.5). The mapping is
    /// per family, not per provider, so adding a provider does not mean editing every row.
    /// </summary>
    private static bool CanRepresent(string declaration, Family family)
    {
        var type = StripCollation(declaration);

        return family switch
        {
            // Bounded character types. SQLite keeps its declared length for documentation and validation even
            // though its type affinity does not enforce it, so it is included.
            Family.Identifier or Family.Text =>
                Regex.IsMatch(type, @"^(TEXT|nvarchar|varchar|nvarchar2)\s*\(\d+\)$", RegexOptions.IgnoreCase),

            // Unbounded Unicode text.
            Family.LargeText =>
                Regex.IsMatch(type, @"^(TEXT|text|nvarchar\(MAX\)|nvarchar\(max\)|longtext|clob|nclob)$", RegexOptions.IgnoreCase),

            // Native timestamp types, plus SQLite's fixed-format UTC text, which is the design's explicit
            // mapping for that server rather than a shortcut.
            Family.UtcDateTime =>
                Regex.IsMatch(type, @"^(timestamp\(6\)(\s+without\s+time\s+zone)?|datetime2\(6\)|datetime\(6\)|TEXT)$", RegexOptions.IgnoreCase),

            Family.Hash32 =>
                Regex.IsMatch(type, @"^(BLOB|bytea|binary\(32\)|raw\(32\)|varbinary\(32\))$", RegexOptions.IgnoreCase),

            Family.Integer =>
                Regex.IsMatch(type, @"^(INTEGER|int|number\(10\))$", RegexOptions.IgnoreCase),

            _ => false
        };
    }

    private static string StripCollation(string declaration)
        => Regex.Replace(declaration, @"\s+COLLATE\s+\S+", string.Empty, RegexOptions.IgnoreCase).Trim();

    private static int? DeclaredLength(string declaration)
    {
        var match = Regex.Match(StripCollation(declaration), @"\(\s*(?<length>\d+)\s*\)");
        return match.Success ? int.Parse(match.Groups["length"].Value) : null;
    }

    /// <summary>
    /// Parses the columns of every table in the install template. Column order and table options are ignored:
    /// MySQL appends ENGINE/CHARSET after the closing parenthesis, Oracle and SQL Server qualify names
    /// differently, and none of that changes the logical schema.
    /// </summary>
    private static Dictionary<string, List<Column>> Parse(string provider)
    {
        var template = TemplateOf(provider);
        var tables = new Dictionary<string, List<Column>>(StringComparer.Ordinal);

        foreach (Match match in Regex.Matches(template,
                     @"CREATE TABLE\s+\S*?__PREFIX__(?<table>\w+)[""\]`]?\s*\("))
        {
            // A comment inside the body is legitimate SQL a DBA reads; it must not parse as phantom columns.
            var body = StripWholeLineComments(ReadBalancedExpression(template, match));
            var columns = new List<Column>();

            foreach (var raw in SplitTopLevel(body))
            {
                var line = raw.Trim().TrimEnd(',');

                if (line.Length == 0 ||
                    Regex.IsMatch(line, @"^(CONSTRAINT|FOREIGN KEY|PRIMARY KEY|UNIQUE)\b"))
                {
                    continue;
                }

                // Oracle has no bare-NULL inline constraint (ORA-00922), so its nullable columns carry no
                // marker at all: nullability is the default there. A missing marker therefore parses as
                // nullable, and NullabilityMatchesTheDesign holds the template to the design either way -
                // a dropped NOT NULL would parse as nullable and fail against the design model.
                var column = Regex.Match(line,
                    @"^[`""\[]?(?<name>\w+)[`""\]]?\s+(?<type>.+?)\s*(?<nullability>NOT NULL|NULL)?\s*$");
                if (!column.Success)
                {
                    continue;
                }

                columns.Add(new Column(
                    column.Groups["name"].Value,
                    column.Groups["type"].Value.Trim(),
                    Nullable: !column.Groups["nullability"].Success
                        || column.Groups["nullability"].Value == "NULL",
                    Collated: column.Groups["type"].Value.Contains("COLLATE", StringComparison.OrdinalIgnoreCase)));
            }

            tables.Add(match.Groups["table"].Value, columns);
        }

        Assert.Equal(9, tables.Count);

        return tables;
    }

    /// <summary>
    /// Reads the body of the parenthesised expression whose opening parenthesis is the last character of
    /// <paramref name="match"/>, so the returned text excludes the outer parentheses and keeps any nested ones.
    /// </summary>
    private static string ReadBalancedExpression(string text, Match match)
    {
        var depth = 0;
        var builder = new System.Text.StringBuilder();

        for (var index = match.Index + match.Length - 1; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '(':
                    depth++;
                    if (depth == 1)
                    {
                        // Skip the opening parenthesis of the body itself.
                        continue;
                    }

                    break;
                case ')':
                    depth--;
                    if (depth == 0)
                    {
                        return builder.ToString();
                    }

                    break;
            }

            builder.Append(text[index]);
        }

        throw new InvalidOperationException("Unbalanced parentheses in the install template.");
    }

    /// <summary>
    /// Removes whole-line <c>--</c> comments from a table body. Templates may document a column or a constraint
    /// inline (PostgreSQL's digest CHECK does), and a comment is not a declaration: without this, every word of
    /// the comment would be parsed as a column name.
    /// </summary>
    private static string StripWholeLineComments(string body)
        => string.Join("\n", body.Split('\n')
            .Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    /// <summary>Splits a table body on commas that separate column/constraint definitions.</summary>
    private static IEnumerable<string> SplitTopLevel(string body)
    {
        var current = new System.Text.StringBuilder();
        var depth = 0;

        foreach (var character in body)
        {
            switch (character)
            {
                case '(':
                    depth++;
                    current.Append(character);
                    break;
                case ')':
                    depth--;
                    current.Append(character);
                    break;
                case ',' when depth == 0:
                    yield return current.ToString();
                    current.Clear();
                    break;
                case '\n' or '\r':
                    current.Append(' ');
                    break;
                default:
                    current.Append(character);
                    break;
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static string TemplateOf(string provider)
        => File.ReadAllText(Path.Combine(RepositoryRoot, "src", $"Sharp.OpenIddict.Stores.{provider}",
            "Sql", "1", "install",
            provider == "Dm" ? Path.Combine("native", "v0001.sql.template") : "v0001.sql.template"));

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

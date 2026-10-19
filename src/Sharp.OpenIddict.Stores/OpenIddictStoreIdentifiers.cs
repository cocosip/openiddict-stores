using System;
using System.Text;

namespace Sharp.OpenIddict.Stores;

/// <summary>
/// Identifier validation and quoting. Identifiers cannot be replaced by value parameters, and unvalidated input must never be concatenated.
/// </summary>
public static class OpenIddictStoreIdentifiers
{
    /// <summary>Determines whether the value is a valid identifier fragment: letters, digits, and underscores, not starting with a digit.</summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || char.IsDigit(value![0]))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Throws an argument exception when validation fails.</summary>
    public static string Validate(string value, string parameterName)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid identifier: only letters, digits, and underscores are allowed, and it cannot start with a digit.", parameterName);
        }

        return value;
    }

    /// <summary>
    /// Renders a deployment copy from a shipped <c>.sql.template</c>: <c>__PREFIX__</c> becomes the table
    /// prefix and <c>__SCHEMA__</c> becomes the quoted schema plus its trailing dot (nothing when the
    /// provider default schema is used, or when the template has no schema placeholder). Both are validated
    /// with the rules the runtime uses, so a copy always matches the configured table prefix and schema.
    /// </summary>
    /// <param name="template">The template text, as shipped in the provider package.</param>
    /// <param name="dialect">The provider dialect; it supplies identifier quoting for the schema.</param>
    /// <param name="prefix">The table prefix, defaulting to <see cref="OpenIddictStoreOptions.DefaultTablePrefix"/>.</param>
    /// <param name="schema">The dedicated schema, or null to use the provider default.</param>
    public static string RenderDeploymentScript(
        string template, IOpenIddictStoreDialect dialect, string prefix, string? schema = null)
    {
        if (template is null)
        {
            throw new ArgumentNullException(nameof(template));
        }

        if (dialect is null)
        {
            throw new ArgumentNullException(nameof(dialect));
        }

        Validate(prefix, nameof(prefix));

        var qualifier = string.IsNullOrEmpty(schema)
            ? string.Empty
            // Quoted so a schema that collides with a keyword still works.
            : dialect.QuoteIdentifier(Validate(schema!, nameof(schema))) + ".";

        return template
            .Replace("__SCHEMA__", qualifier)
            .Replace("__PREFIX__", prefix);
    }

    /// <summary>Splits the script on <c>;</c> into individually executable statements, ignoring empty statements and comment lines.</summary>
    public static string[] SplitStatements(string sql)
    {
        if (sql is null)
        {
            throw new ArgumentNullException(nameof(sql));
        }

        var statements = new System.Collections.Generic.List<string>();
        var current = new StringBuilder();

        foreach (var line in sql.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.TrimStart();

            // Whole-line comments are not assembled into statements, so a comment is never sent as its own statement.
            if (trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            current.AppendLine(line);

            if (line.TrimEnd().EndsWith(";", StringComparison.Ordinal))
            {
                var statement = current.ToString().Trim();
                if (statement.Length > 0)
                {
                    statements.Add(statement);
                }

                current.Clear();
            }
        }

        var remainder = current.ToString().Trim();
        if (remainder.Length > 0)
        {
            statements.Add(remainder);
        }

        return statements.ToArray();
    }
}

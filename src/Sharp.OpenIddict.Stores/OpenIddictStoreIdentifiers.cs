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
    /// Rejects an identifier longer than the provider limit. The check is in bytes, because that is how the
    /// engines count: PostgreSQL truncates at 63 bytes without an error, so a long table prefix would
    /// otherwise create tables the stores can never find again (design 15.4).
    /// </summary>
    /// <param name="identifier">The already validated identifier.</param>
    /// <param name="maximumLength">The provider limit in bytes, or <see cref="int.MaxValue"/> for no limit.</param>
    public static string ValidateLength(string identifier, int maximumLength)
    {
        if (maximumLength != int.MaxValue && Encoding.UTF8.GetByteCount(identifier) > maximumLength)
        {
            throw new ArgumentException(
                $"'{identifier}' is longer than the {maximumLength}-byte identifier limit of this database; " +
                "shorten the table prefix instead of letting the server truncate the name.", nameof(identifier));
        }

        return identifier;
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

        // The rendered copy must obey the same identifier limit as the runtime, otherwise the script would
        // create truncated tables the stores can never find (design 15.4). The longest object name in the
        // shipped templates is the prefix plus the longest fixed suffix, so checking the whole rendered text
        // is both accurate and independent of how a template names its constraints.
        ValidateRenderedIdentifiers(template, dialect, prefix);

        var qualifier = string.IsNullOrEmpty(schema)
            ? string.Empty
            // Quoted so a schema that collides with a keyword still works.
            : dialect.QuoteIdentifier(Validate(schema!, nameof(schema))) + ".";

        return template
            .Replace("__SCHEMA__", qualifier)
            .Replace("__PREFIX__", prefix);
    }

    /// <summary>
    /// Rejects a template whose rendered identifiers would exceed the provider limit. Object names in the
    /// shipped templates start at the prefix placeholder — but not necessarily at the placeholder itself:
    /// constraint names carry a fixed head such as <c>FK___PREFIX__...</c>, so the whole rendered identifier
    /// (head + prefix + suffix) is measured. Measuring only the prefix plus its suffix would let a script
    /// pass rendering and then die mid-install on the first over-long constraint name (design 15.4).
    /// </summary>
    private static void ValidateRenderedIdentifiers(string template, IOpenIddictStoreDialect dialect, string prefix)
    {
        if (dialect.MaximumIdentifierLength == int.MaxValue)
        {
            return;
        }

        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(template, "([A-Za-z0-9_]*)__PREFIX__([A-Za-z0-9_]*)"))
        {
            var identifier = match.Groups[1].Value + prefix + match.Groups[2].Value;
            ValidateLength(identifier, dialect.MaximumIdentifierLength);
        }
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

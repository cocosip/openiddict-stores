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

    /// <summary>Replaces the <c>__PREFIX__</c> placeholder with the validated prefix.</summary>
    /// <remarks>The deployment script source keeps the placeholder; customization only happens on the generated deployment copy, so historical files stay byte-for-byte unchanged.</remarks>
    public static string ApplyPrefix(string sql, string prefix)
    {
        if (sql is null)
        {
            throw new ArgumentNullException(nameof(sql));
        }

        Validate(prefix, nameof(prefix));

        return sql.Replace("__PREFIX__", prefix);
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

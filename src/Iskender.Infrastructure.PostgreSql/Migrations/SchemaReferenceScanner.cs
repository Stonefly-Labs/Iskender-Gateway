namespace Iskender.Infrastructure.PostgreSql.Migrations;

/// <summary>
/// A small SQL lexer that finds the schemas a migration script names (research R11). It skips <c>--</c> and nested
/// <c>/* */</c> comments, single-quoted and <c>E''</c> strings, dollar-quoted bodies with any tag, and the contents of
/// double-quoted identifiers, and reports every <c>isk_&lt;name&gt;</c> or <c>public</c> identifier used as a qualifier,
/// that is, followed by <c>.</c>. Unquoted identifiers fold to lowercase. It cannot see a name built at run time, such as
/// inside <c>EXECUTE format(...)</c>; migration review covers that. Owned by ISK-0004.
/// </summary>
internal static class SchemaReferenceScanner
{
    /// <summary>The <c>isk_</c> and <c>public</c> qualifiers in <paramref name="sql"/>, in order of appearance.</summary>
    public static IReadOnlyList<string> Scan(string sql)
    {
        var found = new List<string>();
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (c == '-' && At(sql, i + 1, '-'))
            {
                i = SkipLineComment(sql, i);
            }
            else if (c == '/' && At(sql, i + 1, '*'))
            {
                i = SkipBlockComment(sql, i);
            }
            else if (c == '\'')
            {
                i = SkipString(sql, i + 1, backslashEscapes: false);
            }
            else if (c == '$' && DollarTag(sql, i) is { } tag)
            {
                var end = sql.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                i = end < 0 ? sql.Length : end + tag.Length;
            }
            else if (c == '"')
            {
                var (name, next) = QuotedIdentifier(sql, i + 1);
                i = Qualifier(sql, next, name, found);
            }
            else if (IsIdentifierStart(c))
            {
                var start = i;
                while (i < sql.Length && IsIdentifierPart(sql[i]))
                {
                    i++;
                }

                if (i - start == 1 && c is 'e' or 'E' && At(sql, i, '\''))
                {
                    i = SkipString(sql, i + 1, backslashEscapes: true);
                }
                else
                {
                    i = Qualifier(sql, i, sql[start..i].ToLowerInvariant(), found);
                }
            }
            else
            {
                i++;
            }
        }

        return found;
    }

    /// <summary>Whether <paramref name="sql"/> names any <c>isk_</c> schema other than <paramref name="schema"/>, or <c>public</c>.</summary>
    public static bool ReferencesOtherSchema(string sql, string schema) =>
        Scan(sql).Any(found => !string.Equals(found, schema, StringComparison.Ordinal));

    // Records the identifier when it is a qualifier: the next token, past whitespace and comments, is a dot.
    private static int Qualifier(string sql, int next, string name, List<string> found)
    {
        if ((name.StartsWith("isk_", StringComparison.Ordinal) || name == "public") && At(sql, SkipTrivia(sql, next), '.'))
        {
            found.Add(name);
        }

        return next;
    }

    private static int SkipTrivia(string sql, int i)
    {
        while (i < sql.Length)
        {
            if (char.IsWhiteSpace(sql[i]))
            {
                i++;
            }
            else if (sql[i] == '-' && At(sql, i + 1, '-'))
            {
                i = SkipLineComment(sql, i);
            }
            else if (sql[i] == '/' && At(sql, i + 1, '*'))
            {
                i = SkipBlockComment(sql, i);
            }
            else
            {
                break;
            }
        }

        return i;
    }

    private static int SkipLineComment(string sql, int i)
    {
        var end = sql.IndexOf('\n', i);
        return end < 0 ? sql.Length : end + 1;
    }

    private static int SkipBlockComment(string sql, int i)
    {
        var depth = 0;
        while (i < sql.Length)
        {
            if (sql[i] == '/' && At(sql, i + 1, '*'))
            {
                depth++;
                i += 2;
            }
            else if (sql[i] == '*' && At(sql, i + 1, '/'))
            {
                i += 2;
                if (--depth == 0)
                {
                    return i;
                }
            }
            else
            {
                i++;
            }
        }

        return i;
    }

    // From just after the opening quote to just after the closing one. '' is a quote in both forms.
    private static int SkipString(string sql, int i, bool backslashEscapes)
    {
        while (i < sql.Length)
        {
            if (backslashEscapes && sql[i] == '\\')
            {
                i += 2;
            }
            else if (sql[i] == '\'')
            {
                if (!At(sql, i + 1, '\''))
                {
                    return i + 1;
                }

                i += 2;
            }
            else
            {
                i++;
            }
        }

        return sql.Length;
    }

    private static (string Name, int Next) QuotedIdentifier(string sql, int i)
    {
        var name = new System.Text.StringBuilder();
        while (i < sql.Length)
        {
            if (sql[i] == '"')
            {
                if (!At(sql, i + 1, '"'))
                {
                    return (name.ToString(), i + 1);
                }

                i++;
            }

            name.Append(sql[i]);
            i++;
        }

        return (name.ToString(), sql.Length);
    }

    // $$ or $tag$, where a tag is an identifier without $. $1 is a parameter, not a quote.
    private static string? DollarTag(string sql, int i)
    {
        var j = i + 1;
        if (j < sql.Length && IsIdentifierStart(sql[j]))
        {
            while (j < sql.Length && (char.IsAsciiLetterOrDigit(sql[j]) || sql[j] == '_' || sql[j] > 127))
            {
                j++;
            }
        }

        return At(sql, j, '$') ? sql[i..(j + 1)] : null;
    }

    private static bool At(string sql, int i, char c) => i < sql.Length && sql[i] == c;

    private static bool IsIdentifierStart(char c) => char.IsAsciiLetter(c) || c == '_' || c > 127;

    private static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || char.IsAsciiDigit(c) || c == '$';
}

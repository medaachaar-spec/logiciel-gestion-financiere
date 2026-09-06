using System.Text;

namespace FinancialManagement.Infrastructure.Backup;

/// <summary>
/// Compares the supported CREATE grammar, not raw SQL strings or arbitrary SQL
/// equivalence. Whitespace/comments, identifier quoting and declaration order in
/// a table are immaterial. Expressions, literals, conflict clauses, collations,
/// constraint definitions and table options are retained. Unknown syntax fails
/// closed. Only known schema identifiers may lose their quotes: "NULL" must
/// never become the NULL keyword (SQLite's legacy double-quoted string fallback).
/// </summary>
internal static class DatabaseSchemaSql
{
    public static bool Matches(string expected, string actual, bool table, IReadOnlySet<string> identifiers)
    {
        var left = Canonicalize(expected, table, identifiers);
        var right = Canonicalize(actual, table, identifiers);
        return left is not null && right is not null && left.SequenceEqual(right, StringComparer.Ordinal);
    }

    private static string[]? Canonicalize(string sql, bool table, IReadOnlySet<string> identifiers)
    {
        var tokens = Tokenize(sql, identifiers);
        if (tokens is null) return null;
        if (tokens.Count > 0 && tokens[^1] == ";") tokens.RemoveAt(tokens.Count - 1);
        if (!table) return tokens.ToArray();

        var open = tokens.IndexOf("(");
        if (open < 0) return null;
        // sqlite_schema removes IF NOT EXISTS, and ALTER TABLE may quote names.
        var prefix = tokens.Take(open).ToList();
        if (prefix.Count == 6 && prefix[2] == "W:IF" && prefix[3] == "W:NOT" && prefix[4] == "W:EXISTS")
            prefix.RemoveRange(2, 3);
        var declarations = new List<string>();
        var start = open + 1;
        var depth = 0;
        for (var i = start; i < tokens.Count; i++)
        {
            if (tokens[i] == "(") depth++;
            else if (tokens[i] == ")")
            {
                if (depth == 0)
                {
                    declarations.Add(Join(tokens.Skip(start).Take(i - start)));
                    return [Join(prefix), .. declarations.Order(StringComparer.Ordinal), Join(tokens.Skip(i + 1))];
                }
                depth--;
            }
            else if (tokens[i] == "," && depth == 0)
            {
                declarations.Add(Join(tokens.Skip(start).Take(i - start)));
                start = i + 1;
            }
        }
        return null;
    }

    // Length-prefixed tokens cannot collide through delimiters in a quoted name.
    private static string Join(IEnumerable<string> tokens) => string.Concat(tokens.Select(token => $"{token.Length}:{token}"));

    private static List<string>? Tokenize(string sql, IReadOnlySet<string> identifiers)
    {
        if (sql.Length > 1_048_576) return null;
        var tokens = new List<string>();
        for (var i = 0; i < sql.Length;)
        {
            var ch = sql[i];
            if (char.IsWhiteSpace(ch)) { i++; continue; }
            if (ch == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                continue;
            }
            if (ch == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) return null;
                i = end + 2;
                continue;
            }
            if (ch is '\'' or '"' or '`' or '[')
            {
                var close = ch == '[' ? ']' : ch;
                var value = new StringBuilder();
                var closed = false;
                i++;
                while (i < sql.Length)
                {
                    var current = sql[i++];
                    if (current != close) { value.Append(current); continue; }
                    if (close != ']' && i < sql.Length && sql[i] == close)
                    {
                        value.Append(close); i++; continue;
                    }
                    closed = true;
                    break;
                }
                if (!closed) return null;
                var word = value.ToString();
                tokens.Add(ch == '\'' ? "S:" + word : Identifier(word, identifiers, quoted: true));
                continue;
            }
            if (char.IsLetter(ch) || ch == '_')
            {
                var start = i++;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] is '_' or '$')) i++;
                tokens.Add(Identifier(sql[start..i], identifiers, quoted: false));
                continue;
            }
            if (char.IsDigit(ch))
            {
                var start = i++;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '.')) i++;
                tokens.Add("N:" + sql[start..i]);
                continue;
            }
            // Do not collapse multi-character operators into separated tokens:
            // < > and <> must not compare equal.
            var pair = i + 1 < sql.Length ? sql.Substring(i, 2) : "";
            if (pair is "<>" or "!=" or "<=" or ">=" or "==" or "||" or "<<" or ">>")
            {
                tokens.Add(pair); i += 2;
            }
            else if ("(),;.+-*/%=<>".Contains(ch)) { tokens.Add(ch.ToString()); i++; }
            else return null;
        }
        return tokens;
    }

    private static string Identifier(string word, IReadOnlySet<string> identifiers, bool quoted) =>
        // SQLite folds ASCII identifiers, not arbitrary Unicode case variants.
        (identifiers.Contains(word) ? "I:" : quoted ? "Q:" : "W:") +
        new string(word.Select(ch => ch is >= 'a' and <= 'z' ? (char)(ch - ('a' - 'A')) : ch).ToArray());
}

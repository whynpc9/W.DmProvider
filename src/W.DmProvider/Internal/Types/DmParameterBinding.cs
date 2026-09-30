using System;
using System.Collections.Generic;
using System.IO;

namespace W.Dm.Internal.Types;

internal readonly record struct DmSqlStatementHead(string First, string Second, string Third);

/// <summary>Frozen binding order from SQL lexical markers; never rewrites the SQL.</summary>
internal sealed class DmParameterBinding
{
    private const int MaxMarkers = 4096;
    private readonly DmParameterCollection parameters;
    private readonly int[] occurrenceIndexes;
    private readonly int[] distinctIndexes;
    private readonly bool named;

    private DmParameterBinding(DmParameterCollection parameters, int[] occurrences, int[] distinct, bool named)
    {
        this.parameters = parameters;
        occurrenceIndexes = occurrences;
        distinctIndexes = distinct;
        this.named = named;
    }

    internal int MarkerCount => occurrenceIndexes.Length;

    internal static DmParameterBinding Create(string sql, DmParameterCollection parameters)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(parameters);
        ScanResult scan = Scan(sql);
        if (scan.Named.Count != 0 && scan.PositionalCount != 0)
            throw new NotSupportedException("Named and positional parameter markers cannot be mixed.");

        var occurrences = new List<int>(scan.Named.Count + scan.PositionalCount);
        var distinct = new List<int>();
        var seen = new HashSet<int>();
        bool named = scan.Named.Count != 0;
        if (named)
        {
            foreach (string marker in scan.Named)
            {
                int index = parameters.do_IndexOf(marker);
                if (index < 0) throw new InvalidOperationException("SQL marker has no matching parameter.");
                occurrences.Add(index);
                if (seen.Add(index)) distinct.Add(index);
            }
            if (distinct.Count != parameters.do_Count)
                throw new InvalidOperationException("Parameter collection contains an unused binding.");
        }
        else
        {
            if (scan.PositionalCount != parameters.do_Count)
                throw new InvalidOperationException("Positional marker count does not match parameters.");
            for (int index = 0; index < scan.PositionalCount; index++)
            {
                occurrences.Add(index);
                distinct.Add(index);
            }
        }
        return new DmParameterBinding(parameters, occurrences.ToArray(), distinct.ToArray(), named);
    }

    internal DmParameter ResolveServerParameter(int serverIndex, string serverName, int serverCount)
    {
        if (serverIndex < 0 || serverIndex >= serverCount ||
            (serverCount != occurrenceIndexes.Length && serverCount != distinctIndexes.Length))
            throw new InvalidOperationException("Server parameter metadata does not match SQL markers.");
        if (named && !string.IsNullOrWhiteSpace(serverName))
        {
            int byName = parameters.do_IndexOf(serverName);
            if (byName >= 0) return parameters.do_GetParameter(byName);
        }
        int index = serverCount == occurrenceIndexes.Length
            ? occurrenceIndexes[serverIndex] : distinctIndexes[serverIndex];
        return parameters.do_GetParameter(index);
    }

    internal void ValidateServerCount(int count)
    {
        if (count != occurrenceIndexes.Length && count != distinctIndexes.Length)
            throw new InvalidOperationException("Server parameter metadata does not match SQL markers.");
    }

    internal static bool IsReliableDescribe(int cType) => cType is
        0 or 1 or 2 or 3 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 14 or 15 or
        16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24 or 26 or 27 or 28;

    internal static IReadOnlyList<DmSqlStatementHead> ScanTopLevelStatements(string sql) => Scan(sql).Heads;

    /// <summary>Recognizes one complete savepoint rollback without rewriting its SQL.</summary>
    internal static bool IsStrictRollbackToSavepoint(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        if (sql.IndexOf('\0') >= 0) return false;
        int index = 0;
        if (!ReadRollbackKeyword(sql, ref index, "ROLLBACK") ||
            !ReadRollbackKeyword(sql, ref index, "TO") ||
            !SkipRollbackTrivia(sql, ref index) || index == sql.Length)
            return false;

        if (sql[index] != '"')
        {
            int nameStart = index;
            if (!IsWordStart(sql[index])) return false;
            while (++index < sql.Length && IsWordPart(sql[index])) { }
            if (sql.AsSpan(nameStart, index - nameStart).Equals("SAVEPOINT".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                if (!SkipRollbackTrivia(sql, ref index) || index == sql.Length ||
                    !ReadRollbackIdentifier(sql, ref index)) return false;
            }
        }
        else if (!ReadRollbackIdentifier(sql, ref index)) return false;

        if (!SkipRollbackTrivia(sql, ref index)) return false;
        if (index < sql.Length && sql[index] == ';')
        {
            index++;
            if (!SkipRollbackTrivia(sql, ref index)) return false;
        }
        return index == sql.Length;
    }

    private static bool ReadRollbackKeyword(string sql, ref int index, string keyword)
    {
        if (!SkipRollbackTrivia(sql, ref index)) return false;
        int start = index;
        if (index == sql.Length || !IsWordStart(sql[index])) return false;
        while (++index < sql.Length && IsWordPart(sql[index])) { }
        return sql.AsSpan(start, index - start).Equals(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ReadRollbackIdentifier(string sql, ref int index)
    {
        if (index == sql.Length) return false;
        if (sql[index] != '"')
        {
            if (!IsWordStart(sql[index])) return false;
            while (++index < sql.Length && IsWordPart(sql[index])) { }
            return true;
        }
        index++;
        bool hasCharacter = false;
        while (index < sql.Length)
        {
            char current = sql[index++];
            if (current != '"') { hasCharacter = true; continue; }
            if (index < sql.Length && sql[index] == '"')
            {
                hasCharacter = true;
                index++;
                continue;
            }
            return hasCharacter;
        }
        return false;
    }

    private static bool SkipRollbackTrivia(string sql, ref int index)
    {
        while (index < sql.Length)
        {
            if (char.IsWhiteSpace(sql[index])) { index++; continue; }
            if (sql[index] == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            {
                index += 2;
                while (index < sql.Length && sql[index] is not ('\r' or '\n')) index++;
                continue;
            }
            if (sql[index] == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                if (!TrySkipBlockComment(sql, ref index)) return false;
                continue;
            }
            break;
        }
        return true;
    }

    private static bool TrySkipBlockComment(string sql, ref int index)
    {
        // R1 supports non-nested block comments, ending at the first closing delimiter.
        int end = sql.IndexOf("*/", index + 2, StringComparison.Ordinal);
        if (end < 0) return false;
        index = end + 2;
        return true;
    }

    private sealed class ScanResult
    {
        internal List<string> Named { get; } = new();
        internal int PositionalCount;
        internal List<DmSqlStatementHead> Heads { get; } = new();
    }

    private static ScanResult Scan(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var result = new ScanResult();
        var words = new List<string>(3);
        int parentheses = 0;
        for (int index = 0; index < sql.Length;)
        {
            char current = sql[index];
            if (current == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            {
                index += 2;
                while (index < sql.Length && sql[index] is not ('\r' or '\n')) index++;
                continue;
            }
            if (current == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                if (!TrySkipBlockComment(sql, ref index))
                    throw new InvalidDataException("Unterminated SQL comment.");
                continue;
            }
            if ((current is 'q' or 'Q') && index + 2 < sql.Length && sql[index + 1] == '\'')
            {
                char opening = sql[index + 2];
                char closing = opening switch { '[' => ']', '(' => ')', '{' => '}', '<' => '>', _ => opening };
                index += 3;
                bool ended = false;
                while (index + 1 < sql.Length)
                {
                    if (sql[index] == closing && sql[index + 1] == '\'')
                    {
                        index += 2;
                        ended = true;
                        break;
                    }
                    index++;
                }
                if (!ended) throw new InvalidDataException("Unterminated SQL alternate quoted value.");
                continue;
            }
            if (current == '\'' || current == '"')
            {
                char quote = current;
                index++;
                bool ended = false;
                while (index < sql.Length)
                {
                    if (sql[index++] != quote) continue;
                    if (index < sql.Length && sql[index] == quote) { index++; continue; }
                    ended = true;
                    break;
                }
                if (!ended) throw new InvalidDataException("Unterminated SQL quoted value.");
                continue;
            }
            if (current == '[')
            {
                index++;
                while (index < sql.Length && sql[index] != ']') index++;
                if (index == sql.Length) throw new InvalidDataException("Unterminated SQL identifier.");
                index++;
                continue;
            }
            if (current == ':' && index + 1 < sql.Length && (sql[index + 1] is ':' or '='))
            {
                index += 2;
                continue;
            }
            if (current == '@' && index + 1 < sql.Length && sql[index + 1] == '@')
            {
                index += 2;
                while (index < sql.Length && IsWordPart(sql[index])) index++;
                continue;
            }
            if ((current is ':' or '@') && index + 1 < sql.Length && IsWordStart(sql[index + 1]))
            {
                int start = ++index;
                while (index < sql.Length && IsWordPart(sql[index])) index++;
                if (result.Named.Count + result.PositionalCount >= MaxMarkers)
                    throw new InvalidDataException("Too many SQL parameter markers.");
                result.Named.Add(sql.Substring(start, index - start));
                continue;
            }
            if (current == '?')
            {
                if (result.Named.Count + result.PositionalCount >= MaxMarkers)
                    throw new InvalidDataException("Too many SQL parameter markers.");
                result.PositionalCount++;
                index++;
                continue;
            }
            if (current == '(') { parentheses++; index++; continue; }
            if (current == ')') { if (parentheses > 0) parentheses--; index++; continue; }
            if (current == ';' && parentheses == 0)
            {
                AddHead(result.Heads, words);
                words.Clear();
                index++;
                continue;
            }
            if (parentheses == 0 && IsWordStart(current))
            {
                int start = index++;
                while (index < sql.Length && IsWordPart(sql[index])) index++;
                if (words.Count < 3) words.Add(sql.Substring(start, index - start).ToUpperInvariant());
                continue;
            }
            index++;
        }
        AddHead(result.Heads, words);
        return result;
    }

    private static void AddHead(List<DmSqlStatementHead> heads, List<string> words)
    {
        if (words.Count == 0) return;
        if (heads.Count >= MaxMarkers) throw new InvalidDataException("Too many SQL statements.");
        heads.Add(new DmSqlStatementHead(words[0], words.Count > 1 ? words[1] : string.Empty,
            words.Count > 2 ? words[2] : string.Empty));
    }

    private static bool IsWordStart(char value) => char.IsLetter(value) || value == '_';
    private static bool IsWordPart(char value) => char.IsLetterOrDigit(value) || value is '_' or '$' or '#';
}

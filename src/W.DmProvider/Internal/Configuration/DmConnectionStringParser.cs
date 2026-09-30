using System;
using System.Collections.Generic;

namespace W.Dm;

/// <summary>Ordered parser that retains empty values and duplicate keys.</summary>
internal static class DmConnectionStringParser
{
    internal static IEnumerable<(string Key, string Value)> Parse(string text)
    {
        text ??= string.Empty;
        int index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && (text[index] == ';' || char.IsWhiteSpace(text[index]))) index++;
            if (index == text.Length) yield break;

            int keyStart = index;
            while (index < text.Length && text[index] != '=' && text[index] != ';') index++;
            if (index == text.Length || text[index] != '=') throw Syntax();
            string key = text.Substring(keyStart, index - keyStart).Trim();
            if (key.Length == 0 || key.IndexOfAny(['\'', '"', '{', '}']) >= 0) throw Syntax();
            index++;
            while (index < text.Length && char.IsWhiteSpace(text[index])) index++;

            string value;
            if (index < text.Length && (text[index] == '\'' || text[index] == '"'))
            {
                char quote = text[index++];
                var builder = new System.Text.StringBuilder();
                bool closed = false;
                while (index < text.Length)
                {
                    char current = text[index++];
                    if (current != quote) { builder.Append(current); continue; }
                    if (index < text.Length && text[index] == quote)
                    {
                        builder.Append(quote);
                        index++;
                        continue;
                    }
                    closed = true;
                    break;
                }
                if (!closed) throw Syntax();
                while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
                if (index < text.Length && text[index] != ';') throw Syntax();
                value = builder.ToString();
            }
            else
            {
                int valueStart = index;
                while (index < text.Length && text[index] != ';')
                {
                    if (text[index] == '\'' || text[index] == '"') throw Syntax();
                    index++;
                }
                value = text.Substring(valueStart, index - valueStart).Trim();
            }
            yield return (key, value);
            if (index < text.Length) index++;
        }
    }

    private static ArgumentException Syntax() => new("Invalid connection string syntax.");
}

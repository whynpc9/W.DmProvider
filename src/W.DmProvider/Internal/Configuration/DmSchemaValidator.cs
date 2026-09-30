using System;

namespace W.Dm;

internal static class DmSchemaValidator
{
    // Product policy for a single schema identifier, measured in .NET char units.
    // This is not a claim about a Dameng protocol or server byte limit.
    internal static string Normalize(string schema)
    {
        schema ??= string.Empty;
        if (schema.Length > 128 || schema.IndexOfAny(['\0', '\r', '\n']) >= 0)
            throw new ArgumentException("Invalid schema setting.");
        return schema;
    }
}

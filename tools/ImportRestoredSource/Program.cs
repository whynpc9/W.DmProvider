using System.Collections;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: ImportRestoredSource <repo-root> <fresh-restored-source-root>");
    return 2;
}

try
{
    var root = Path.GetFullPath(args[0]);
    var sourceRoot = Path.GetFullPath(args[1]);
    if (!sourceRoot.StartsWith(Path.Combine(root, ".local/t02/") , StringComparison.Ordinal))
        throw new InvalidOperationException("seed_must_be_under_local_t02");
    var seedRelative = Path.GetRelativePath(root, sourceRoot).Replace('\\', '/');
    var productRoot = Path.Combine(root, "src/W.DmProvider");
    var mapPath = Path.Combine(root, "docs/compatibility/t03-source-map.json");
    var oldMap = File.Exists(mapPath)
        ? JsonSerializer.Deserialize<ImportMap>(File.ReadAllText(mapPath), JsonOptions())
        : null;
    var generated = new List<GeneratedFile>();
    var sourceFiles = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
        .Where(path => !Path.GetRelativePath(sourceRoot, path).Split(Path.DirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj" or "satellites"))
        .Where(path => Path.GetRelativePath(sourceRoot, path) != "Properties/AssemblyInfo.cs")
        .OrderBy(path => path, StringComparer.Ordinal).ToArray();
    if (sourceFiles.Length != 241)
        throw new InvalidOperationException($"restored_source_count_changed:{sourceFiles.Length}");
    foreach (var source in sourceFiles)
    {
        var relative = Path.GetRelativePath(sourceRoot, source).Replace('\\', '/');
        var destination = ProductPath(relative);
        var result = TransformSource(File.ReadAllText(source), relative);
        var transform = relative switch
        {
            "Dm/DmConst.cs" => "Roslyn namespace migration; explicit Dm.DmErrorDefinition ResourceManager base name",
            "DmSqlRowUpdatedEventArgs.cs" or "DmSqlRowUpdatingEventArgs.cs" => "Roslyn namespace migration; move global event args into W.Dm",
            "Dm/T02_02000026.cs" => "Roslyn namespace migration; rename recovered source file to DmCommand.cs",
            "Dm/T02_02000038.cs" => "Roslyn namespace migration; rename recovered source file to DmDataReader.cs",
            _ => "Roslyn syntax-token namespace migration"
        };
        generated.Add(new($"{seedRelative}/{relative}", Sha(source), destination,
            Encoding.UTF8.GetBytes(result), transform));
    }

    foreach (var resource in new[] { "Dm.DmErrorDefinition.resx", "Dm.ReservedWords.txt" })
    {
        var source = Path.Combine(sourceRoot, resource);
        generated.Add(new($"{seedRelative}/{resource}", Sha(source), $"src/W.DmProvider/Resources/{resource}",
            File.ReadAllBytes(source), "Preserve neutral resource bytes and explicit LogicalName"));
    }
    var neutral = XDocument.Load(Path.Combine(sourceRoot, "Dm.DmErrorDefinition.resx"));
    foreach (var culture in new[] { "en", "zh-CN", "zh-HK", "zh-TW" })
    {
        var relative = $"packages/extracted/lib/net9.0/{culture}/DM.DmProvider.resources.dll";
        var source = Path.Combine(root, relative);
        var satellite = Assembly.LoadFile(source);
        using var stream = satellite.GetManifestResourceStream($"Dm.DmErrorDefinition.{culture}.resources")
            ?? throw new InvalidOperationException($"missing_satellite_resource:{culture}");
        using var reader = new ResourceReader(stream);
        var strings = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry item in reader)
        {
            if (item.Key is not string key || item.Value is not string value)
                throw new InvalidOperationException($"non_string_satellite_entry:{culture}");
            strings.Add(key, value);
        }
        if (strings.Count == 0)
            throw new InvalidOperationException($"empty_satellite_resource:{culture}");
        var document = new XDocument(neutral);
        document.Root!.Elements("data").Remove();
        XNamespace xml = XNamespace.Xml;
        foreach (var pair in strings)
            document.Root.Add(new XElement("data", new XAttribute("name", pair.Key),
                new XAttribute(xml + "space", "preserve"), new XElement("value", pair.Value)));
        var content = Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting) + "\n");
        generated.Add(new(relative, Sha(source), $"src/W.DmProvider/Resources/{culture}/Dm.DmErrorDefinition.resx",
            content, $"Extract {strings.Count} string entries from fixed satellite; rebuild under W.DmProvider identity"));
    }

    // Check every target first. A repeated import is idempotent, but never replaces product edits.
    var oldEntries = oldMap?.Files.ToDictionary(x => x.ProductPath, StringComparer.Ordinal)
        ?? new Dictionary<string, MapEntry>(StringComparer.Ordinal);
    foreach (var file in generated)
    {
        var target = Path.Combine(root, file.ProductPath);
        if (!File.Exists(target)) continue;
        if (!oldEntries.TryGetValue(file.ProductPath, out var prior) || Sha(target) != prior.ProductSha256)
            throw new InvalidOperationException($"product_file_modified_or_untracked:{file.ProductPath}");
        if (prior.SourceSha256 != file.SourceSha256 || prior.ProductSha256 != ShaBytes(file.Bytes))
            throw new InvalidOperationException($"source_or_transform_changed_requires_manual_review:{file.ProductPath}");
    }
    if (oldMap is not null && (oldMap.Files.Count != generated.Count ||
        oldMap.OfficialAssemblySha256 != Sha(Path.Combine(root, "packages/extracted/lib/net9.0/DM.DmProvider.dll"))))
        throw new InvalidOperationException("import_map_changed_requires_manual_review");

    foreach (var file in generated)
    {
        var target = Path.Combine(root, file.ProductPath);
        if (File.Exists(target)) continue;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, file.Bytes);
    }
    var map = new ImportMap(1, "DM.DmProvider 8.3.1.47463 R generated source",
        Sha(Path.Combine(root, "packages/extracted/lib/net9.0/DM.DmProvider.dll")),
        generated.Select(x => new MapEntry(x.SourcePath, x.SourceSha256, x.ProductPath, ShaBytes(x.Bytes), x.Transform)).ToList());
    if (oldMap is null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(mapPath)!);
        File.WriteAllText(mapPath, JsonSerializer.Serialize(map, JsonOptions()) + "\n");
    }
    Console.WriteLine(JsonSerializer.Serialize(new { status = "imported", csharp = sourceFiles.Length, files = generated.Count,
        map = "docs/compatibility/t03-source-map.json" }));
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"import_failed:{ex.GetType().Name}:{ex.Message}");
    return 1;
}

static string ProductPath(string source)
{
    var file = Path.GetFileName(source);
    if (source is "DmSqlRowUpdatedEventArgs.cs" or "DmSqlRowUpdatingEventArgs.cs")
        return $"src/W.DmProvider/PublicApi/{file}";
    if (source.StartsWith("Dm/", StringComparison.Ordinal) &&
        (file is "DmConnection.cs" or "DmParameter.cs" or "DmParameterCollection.cs" or "DmTransaction.cs" or
            "DmDbType.cs" or "DmConnectionStringBuilder.cs" or "DmException.cs" or "DmClientFactory.cs" or
            "T02_02000026.cs" or "T02_02000038.cs"))
        return $"src/W.DmProvider/PublicApi/{file switch { "T02_02000026.cs" => "DmCommand.cs", "T02_02000038.cs" => "DmDataReader.cs", _ => file }}";
    return $"src/W.DmProvider/Internal/Legacy/{source}";
}

static string TransformSource(string original, string relative)
{
    var tree = CSharpSyntaxTree.ParseText(original, new CSharpParseOptions(LanguageVersion.CSharp13));
    var root = tree.GetCompilationUnitRoot();
    var changes = new List<TextChange>();
    foreach (var node in root.DescendantNodes().OfType<IdentifierNameSyntax>())
    {
        var name = node.Identifier.ValueText;
        if (name is not ("Dm" or "A" or "NetTaste")) continue;
        var inDeclaration = node.Ancestors().Any(ancestor => ancestor switch
        {
            BaseNamespaceDeclarationSyntax ns when ns.Name.Span.Contains(node.Span) => true,
            UsingDirectiveSyntax use when use.Name?.Span.Contains(node.Span) == true => true,
            _ => false
        });
        var globalAlias = node.Parent is AliasQualifiedNameSyntax alias && alias.Alias.Identifier.ValueText == "global";
        var qualifiedRoot = node.Parent is QualifiedNameSyntax qualified && qualified.Left == node ||
            node.Parent is MemberAccessExpressionSyntax member && member.Expression == node;
        if (!inDeclaration && !globalAlias && !(qualifiedRoot && name is "Dm" or "NetTaste")) continue;
        changes.Add(new TextChange(node.Span, name switch
        {
            "Dm" => "W.Dm",
            "A" => "W.Dm.Internal.Legacy.A",
            _ => "W.Dm.Internal.Legacy.NetTaste"
        }));
    }
    if (relative is "DmSqlRowUpdatedEventArgs.cs" or "DmSqlRowUpdatingEventArgs.cs")
    {
        if (root.Members.OfType<BaseNamespaceDeclarationSyntax>().Any())
            throw new InvalidOperationException($"global_event_args_namespace_changed:{relative}");
        changes.Add(new TextChange(new TextSpan(root.Usings.Last().FullSpan.End, 0), "\nnamespace W.Dm;\n"));
    }
    if (relative == "Dm/DmConst.cs")
    {
        var expression = root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .SingleOrDefault(x => x.Type.ToString() == "ResourceManager" && x.ArgumentList?.Arguments.Count == 1 &&
                x.ArgumentList.Arguments[0].ToString() == "typeof(DmErrorDefinition)")
            ?? throw new InvalidOperationException("resource_manager_lookup_site_changed");
        changes.Add(new TextChange(expression.Span,
            "new ResourceManager(\"Dm.DmErrorDefinition\", typeof(DmErrorDefinition).Assembly)"));
    }
    var output = SourceText.From(original).WithChanges(changes).ToString();
    var updated = CSharpSyntaxTree.ParseText(output, new CSharpParseOptions(LanguageVersion.CSharp13));
    if (updated.GetDiagnostics().Count(x => x.Severity == DiagnosticSeverity.Error) >
        tree.GetDiagnostics().Count(x => x.Severity == DiagnosticSeverity.Error))
        throw new InvalidOperationException($"namespace_migration_parse_error:{relative}");
    return output;
}

static string Sha(string path) => ShaBytes(File.ReadAllBytes(path));
static string ShaBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
static JsonSerializerOptions JsonOptions() => new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };
record GeneratedFile(string SourcePath, string SourceSha256, string ProductPath, byte[] Bytes, string Transform);
record MapEntry(string SourcePath, string SourceSha256, string ProductPath, string ProductSha256, string Transform);
record ImportMap(int SchemaVersion, string SourceBaseline, string OfficialAssemblySha256, List<MapEntry> Files);

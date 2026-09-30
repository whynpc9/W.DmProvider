using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length == 3 && args[0] == "api")
{
    try
    {
        using var apiModule = ModuleDefinition.ReadModule(Path.GetFullPath(args[1]));
        var lines = new List<string>();
        foreach (var type in AllTypes(apiModule.Types).Where(VisibleType))
        {
            lines.Add($"T|{type.FullName}");
            lines.AddRange(type.Fields.Where(f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly).Select(f => $"F|{f.FullName}"));
            lines.AddRange(type.Methods.Where(m => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly).Select(m => $"M|{m.FullName}"));
            lines.AddRange(type.Properties.Where(p => VisibleAccessor(p.GetMethod) || VisibleAccessor(p.SetMethod)).Select(p => $"P|{p.FullName}"));
            lines.AddRange(type.Events.Where(e => VisibleAccessor(e.AddMethod) || VisibleAccessor(e.RemoveMethod)).Select(e => $"E|{e.FullName}"));
        }
        lines.Sort(StringComparer.Ordinal);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
        await File.WriteAllLinesAsync(args[2], lines);
        Console.WriteLine(JsonSerializer.Serialize(new { status = "public_api_written", types = lines.Count(x => x.StartsWith("T|", StringComparison.Ordinal)), entries = lines.Count, path = Path.GetFullPath(args[2]) }));
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"api_failed:{ex.GetType().Name}:{ex.Message}");
        return 1;
    }
}

if (args.Length is < 2 or > 3 || args[0] != "restore")
{
    Console.Error.WriteLine("usage: RestoreBaseline restore <repo-root> [output-root] | api <assembly> <output-file>");
    return 2;
}

try
{
    var root = Path.GetFullPath(args[1]);
    var output = Path.GetFullPath(args.Length == 3 ? args[2] : Path.Combine(root, ".local/t02/restored"));
    var source = Path.Combine(root, "decompiled/net9.0");
    var originalDll = Path.Combine(root, "packages/extracted/lib/net9.0/DM.DmProvider.dll");
    var generatedRoot = Path.Combine(root, ".local/t02") + Path.DirectorySeparatorChar;
    const string expectedSha256 = "8f6e59680d0a076df53bea50d5a2bdbd288535cd85b2d7ca5064c02adc9c6e6b";
    if (!string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(originalDll))), expectedSha256, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("official_asset_hash_mismatch");
    if (!output.StartsWith(generatedRoot, StringComparison.Ordinal))
        throw new InvalidOperationException("output_must_be_within_local_t02");

    Directory.CreateDirectory(output);
    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(source, file);
        if (relative == "DM.DmProvider.csproj") continue;
        var target = Path.Combine(output, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target, true);
    }

    var module = ModuleDefinition.ReadModule(originalDll, new ReaderParameters { ReadSymbols = false });
    var originalBodies = BodyFingerprints(module);
    var originalOverrideEdges = CaptureOverrideEdges(module);
    var changes = new List<RenameRecord>();
    foreach (var type in module.Types.Where(t => t.Namespace == "A"))
    {
        var fields = type.Fields.GroupBy(f => f.Name).Where(g => g.Count() > 1).SelectMany(g => g.Skip(1)).ToHashSet();
        var methodNames = type.Methods.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var field in type.Fields.Where(f => methodNames.Contains(f.Name))) fields.Add(field);
        foreach (var field in fields.OrderBy(f => f.MetadataToken.ToInt32()))
        {
            var before = field.FullName;
            var oldName = field.Name;
            field.Name = $"__t02_field_{field.MetadataToken.ToInt32():X8}";
            changes.Add(new("field", field.MetadataToken.ToInt32(), type.FullName, before, oldName, field.Name, "C# field/member name collision"));
        }

        var methods = new HashSet<MethodDefinition>();
        foreach (var method in type.Methods.Where(m => !m.IsConstructor && m.Name == type.Name)) methods.Add(method);
        foreach (var group in type.Methods.Where(m => !m.IsConstructor).GroupBy(m =>
                     (m.Name, m.GenericParameters.Count, Parameters: string.Join("|", m.Parameters.Select(p => p.ParameterType.FullName)))))
        {
            foreach (var method in group.Skip(1)) methods.Add(method);
        }
        foreach (var method in methods.OrderBy(m => m.MetadataToken.ToInt32()))
        {
            var before = method.FullName;
            var oldName = method.Name;
            method.Name = $"__t02_method_{method.MetadataToken.ToInt32():X8}";
            changes.Add(new("method", method.MetadataToken.ToInt32(), type.FullName, before, oldName, method.Name,
                oldName == type.Name ? "C# member name equals enclosing type" : "CLR return-type overload unsupported by C#"));
        }
    }
    // Propagate each renamed virtual slot through its entire original override
    // chain, including explicit MethodImpl edges and multi-level descendants.
    var renamedMethods = changes.Where(c => c.Kind == "method").ToDictionary(c => c.Token, c => c.NewName);
    var affectedOverrideEdges = new List<OverrideEdge>();
    bool propagated;
    do
    {
        propagated = false;
        foreach (var edge in originalOverrideEdges)
        {
            if (!renamedMethods.TryGetValue(edge.BaseToken, out var baseName)) continue;
            if (!affectedOverrideEdges.Contains(edge)) affectedOverrideEdges.Add(edge);
            var derivedMethod = (MethodDefinition)module.LookupToken(edge.DerivedToken);
            if (renamedMethods.TryGetValue(edge.DerivedToken, out var existingName) && existingName == baseName) continue;
            var existingIndex = changes.FindIndex(c => c.Token == edge.DerivedToken);
            if (existingIndex >= 0)
            {
                changes[existingIndex] = changes[existingIndex] with
                {
                    NewName = baseName,
                    Reason = $"Preserve virtual override slot of base token 0x{edge.BaseToken:X8}",
                    BaseToken = edge.BaseToken
                };
            }
            else
            {
                changes.Add(new("method", edge.DerivedToken, derivedMethod.DeclaringType.FullName, derivedMethod.FullName,
                    derivedMethod.Name, baseName, $"Preserve virtual override slot of base token 0x{edge.BaseToken:X8}", edge.BaseToken));
            }
            derivedMethod.Name = baseName;
            renamedMethods[edge.DerivedToken] = baseName;
            propagated = true;
        }
    } while (propagated);
    VerifyOverrideEdges(module, affectedOverrideEdges);

    var normalized = Path.Combine(output, "t02-normalized-reference.dll");
    var normalizedBodies = BodyFingerprints(module);
    if (originalBodies.Count != normalizedBodies.Count || originalBodies.Any(kv => !normalizedBodies.TryGetValue(kv.Key, out var hash) || kv.Value != hash))
        throw new InvalidOperationException("metadata_normalization_changed_method_body");
    module.Write(normalized);
    using var normalizedReadback = ModuleDefinition.ReadModule(normalized, new ReaderParameters { ReadSymbols = false });
    var writtenBodies = BodyFingerprints(normalizedReadback);
    if (originalBodies.Count != writtenBodies.Count || originalBodies.Any(kv => !writtenBodies.TryGetValue(kv.Key, out var hash) || kv.Value != hash))
        throw new InvalidOperationException("normalized_dll_method_body_readback_mismatch");
    VerifyOverrideEdges(normalizedReadback, affectedOverrideEdges);
    // This is a decompiler input only. The R assembly must be compiled from the emitted C#.
    var affected = changes.Select(c => c.Type).ToHashSet(StringComparer.Ordinal);
    var renamedTokens = changes.Select(c => c.Token).ToHashSet();
    var callerReferences = new Dictionary<TypeDefinition, HashSet<int>>();
    foreach (var type in module.Types.Where(t => t.Namespace != "A"))
    {
        foreach (var method in type.Methods.Where(m => m.HasBody))
        foreach (var instruction in method.Body.Instructions)
        {
            var token = instruction.Operand switch
            {
                FieldReference field when field.DeclaringType.Namespace == "A" => field.Resolve()?.MetadataToken.ToInt32(),
                MethodReference called when called.DeclaringType.Namespace == "A" => called.Resolve()?.MetadataToken.ToInt32(),
                _ => null
            };
            if (token is not null && renamedTokens.Contains(token.Value))
            {
                if (!callerReferences.TryGetValue(type, out var tokens))
                    callerReferences[type] = tokens = new HashSet<int>();
                tokens.Add(token.Value);
            }
        }
    }
    var restoredTypes = module.Types.Where(t => t.Namespace == "A" || callerReferences.ContainsKey(t)).ToArray();
    foreach (var generated in Directory.EnumerateFiles(Path.Combine(output, "A"), "T02_*.cs"))
        File.Delete(generated);
    foreach (var generated in Directory.EnumerateFiles(output, "T02_*.cs", SearchOption.AllDirectories))
        File.Delete(generated);
    var originalPaths = new Dictionary<TypeDefinition, string>();
    foreach (var type in restoredTypes)
    {
        var stem = SourceStem(type);
        var relative = Path.Combine(type.Namespace, stem + ".cs");
        if (!File.Exists(Path.Combine(source, relative)))
            throw new InvalidOperationException($"original_source_path_missing:{type.FullName}");
        originalPaths[type] = relative;
    }
    var selectedPaths = originalPaths.Values.ToHashSet(StringComparer.Ordinal);
    foreach (var relative in selectedPaths)
        File.Delete(Path.Combine(output, relative));
    // Files may contain multiple top-level types (notably A/A.cs with A and a).
    // Regenerate all types from every selected source file, including innocent cohabitants.
    restoredTypes = module.Types.Where(t =>
    {
        var stem = SourceStem(t);
        return selectedPaths.Contains(Path.Combine(t.Namespace, stem + ".cs"));
    }).ToArray();
    foreach (var type in restoredTypes)
    {
        var target = Path.Combine(output, type.Namespace, $"T02_{type.MetadataToken.ToInt32():X8}.cs");
        await Decompile(root, normalized, type.FullName, target);
    }

    var decimalPath = Path.Combine(output, "Dm", "DmSetValue.cs");
    var decimalSource = await File.ReadAllTextAsync(decimalPath);
    const string castBefore = "byte b2 = (('0' <= array2[i]";
    const string castAfter = "byte b2 = (byte)(('0' <= array2[i]";
    if (decimalSource.Split(castBefore).Length != 2)
        throw new InvalidOperationException("expected_decimal_byte_cast_site_missing_or_ambiguous");
    await File.WriteAllTextAsync(decimalPath, decimalSource.Replace(castBefore, castAfter, StringComparison.Ordinal));
    var decimalMethod = module.Types.Single(t => t.FullName == "Dm.DmSetValue").Methods.Single(m => m.Name == "decStringToBcd");

    var project = Path.Combine(output, "DM.DmProvider.Restored.csproj");
    var satelliteItems = new List<string>();
    foreach (var culture in new[] { "en", "zh-CN", "zh-HK", "zh-TW" })
    {
        var satellite = Path.Combine(root, "packages/extracted/lib/net9.0", culture, "DM.DmProvider.resources.dll");
        var target = Path.Combine(output, "satellites", culture, "DM.DmProvider.resources.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(satellite, target, true);
        satelliteItems.Add($"    <Content Include=\"satellites/{culture}/DM.DmProvider.resources.dll\" Link=\"{culture}/DM.DmProvider.resources.dll\" CopyToOutputDirectory=\"Always\" />");
    }
    await File.WriteAllTextAsync(project, """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net9.0</TargetFramework>
            <AssemblyName>DM.DmProvider</AssemblyName>
            <RootNamespace></RootNamespace>
            <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
            <IsPackable>false</IsPackable>
            <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
            <CheckForOverflowUnderflow>false</CheckForOverflowUnderflow>
            <LangVersion>13.0</LangVersion>
            <NuGetAudit>false</NuGetAudit>
          </PropertyGroup>
          <ItemGroup>
            <EmbeddedResource Include="Dm.ReservedWords.txt" LogicalName="Dm.ReservedWords.txt" />
            <EmbeddedResource Update="Dm.DmErrorDefinition.resx" LogicalName="Dm.DmErrorDefinition.resources" />
        """ + "\n" + string.Join("\n", satelliteItems) + "\n" + """
          </ItemGroup>
        </Project>
        """);

    var mapPath = Path.Combine(output, "source-map.json");
    var map = new
    {
        schema_version = 1,
        source_assembly_sha256 = expectedSha256,
        normalization_body_audit = new { status = "passed", methods_with_body = originalBodies.Count, aggregate_sha256 = AggregateBodyFingerprint(originalBodies), scope = "all top-level and nested TypeDef method bodies; original, in-memory normalized, and written normalized DLL readback", canonicalization = "opcodes, typed literals, operand metadata tokens, branch targets, exception handlers, InitLocals, MaxStack and local types" },
        virtual_slot_audit = new { status = "passed", captured_original_edges = originalOverrideEdges.Count, affected_edges = affectedOverrideEdges.Select(e => new { base_token = $"0x{e.BaseToken:X8}", derived_token = $"0x{e.DerivedToken:X8}", e.Kind, base_original_signature = e.BaseSignature, derived_original_signature = e.DerivedSignature, restored_name = ((MethodDefinition)module.LookupToken(e.BaseToken)).Name }), verification = "in-memory normalized and written normalized DLL readback preserve original reuse-slot or explicit MethodImpl edge" },
        sdk_version = "10.0.203",
        decompiler = new { id = "ilspycmd", version = "10.1.1.8388" },
        metadata_editor = new { id = "Mono.Cecil", version = "0.11.6" },
        generated_project = Path.GetRelativePath(root, project),
        regenerated_types = restoredTypes.Select(t => new { original_type = t.FullName, original_token = $"0x{t.MetadataToken.ToInt32():X8}", original_path = Path.GetRelativePath(root, Path.Combine(source, t.Namespace, SourceStem(t) + ".cs")), restored_path = Path.GetRelativePath(root, Path.Combine(output, t.Namespace, $"T02_{t.MetadataToken.ToInt32():X8}.cs")), reason = affected.Contains(t.FullName) ? "member metadata name collision" : callerReferences.ContainsKey(t) ? "references renamed member" : "shares original source file", referenced_renames = callerReferences.TryGetValue(t, out var tokens) ? tokens.Order().Select(x => $"0x{x:X8}") : Enumerable.Empty<string>() }),
        renames = changes.Select(c => new { c.Kind, token = $"0x{c.Token:X8}", original_type = c.Type, original_path = Path.GetRelativePath(root, Path.Combine(source, "A", char.ToUpperInvariant(c.Type[^1]) + ".cs")), original_signature = c.Signature, original_name = c.OldName, restored_name = c.NewName, override_base_token = c.BaseToken is null ? null : $"0x{c.BaseToken:X8}", c.Reason, il_body_audit = "passed", requires_or_differential_test = true }),
        source_repairs = new[] { new { token = $"0x{decimalMethod.MetadataToken.ToInt32():X8}", original_type = "Dm.DmSetValue", original_signature = decimalMethod.FullName, original_path = "decompiled/net9.0/Dm/DmSetValue.cs", restored_path = Path.GetRelativePath(root, decimalPath), reason = "Decompiler conditional expression inferred int while IL stores each branch into a byte local; explicit byte conversion preserves IL value range", il_evidence = "decStringToBcd IL_003e conv.u1 / IL_003f stloc.0 and IL_0065–IL_0076 small constants stloc.0" } },
        caveat = "Names are changed only in the decompiler input. The restored DLL is compiled from generated C#; no behavior equivalence is claimed until differential validation."
    };
    var mapText = JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    await File.WriteAllTextAsync(mapPath, mapText);
    if (output == Path.Combine(root, ".local/t02/restored"))
        await File.WriteAllTextAsync(Path.Combine(root, "upstream/DM.DmProvider/8.3.1.47463/source-map.json"), mapText);
    Console.WriteLine(JsonSerializer.Serialize(new { status = "restored_source_generated", project, renamed_members = changes.Count, regenerated_types = restoredTypes.Length }));
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"restore_failed:{ex.GetType().Name}:{ex.Message}");
    return 1;
}

static async Task Decompile(string root, string normalized, string type, string target)
{
    var start = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = Path.Combine(root, "tools/RestoreBaseline"),
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    foreach (var arg in new[] { "tool", "run", "ilspycmd", "--", "--disable-updatecheck", "-t", type, normalized })
        start.ArgumentList.Add(arg);
    using var process = Process.Start(start) ?? throw new InvalidOperationException("ilspycmd_start_failed");
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"ilspycmd_failed:{type}:{(await stderr).Trim()}");
    await File.WriteAllTextAsync(target, await stdout);
}

static string SourceStem(TypeDefinition type)
{
    if (type.Namespace == "A") return char.ToUpperInvariant(type.Name[0]).ToString();
    var tick = type.Name.IndexOf('`');
    return tick < 0 ? type.Name : type.Name[..tick];
}

static Dictionary<int, string> BodyFingerprints(ModuleDefinition module)
{
    var result = new Dictionary<int, string>();
    foreach (var method in AllTypes(module.Types).SelectMany(t => t.Methods).Where(m => m.HasBody))
    {
        var body = method.Body;
        var lines = new[] { $"HEADER|{body.InitLocals}|{body.MaxStackSize}|{string.Join(",", body.Variables.Select(v => v.VariableType.FullName))}" }
            .Concat(body.Instructions.Select(i => $"{i.Offset:X4}|{i.OpCode.Code}|{CanonicalOperand(i.Operand)}"))
            .Concat(body.ExceptionHandlers.Select(h => $"EH|{h.HandlerType}|{h.TryStart?.Offset}|{h.TryEnd?.Offset}|{h.HandlerStart?.Offset}|{h.HandlerEnd?.Offset}|{h.FilterStart?.Offset}|{h.CatchType?.MetadataToken.ToInt32()}"));
        result[method.MetadataToken.ToInt32()] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines))));
    }
    return result;
}

static string AggregateBodyFingerprint(Dictionary<int, string> bodies) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
    string.Join("\n", bodies.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key:X8}:{kv.Value}")))));

static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> roots)
{
    foreach (var type in roots)
    {
        yield return type;
        foreach (var nested in AllTypes(type.NestedTypes)) yield return nested;
    }
}

static List<OverrideEdge> CaptureOverrideEdges(ModuleDefinition module)
{
    var edges = new List<OverrideEdge>();
    foreach (var type in AllTypes(module.Types))
    foreach (var method in type.Methods.Where(m => m.IsVirtual))
    {
        foreach (var explicitBase in method.Overrides)
        {
            var baseMethod = SafeResolveMethod(explicitBase);
            if (baseMethod?.Module == module)
                edges.Add(new(baseMethod.MetadataToken.ToInt32(), method.MetadataToken.ToInt32(), "explicit MethodImpl", baseMethod.FullName, method.FullName));
        }
        if (method.IsNewSlot) continue;
        for (var parent = SafeResolve(type.BaseType); parent?.Module == module; parent = SafeResolve(parent.BaseType))
        {
            var baseMethod = parent.Methods.FirstOrDefault(candidate => candidate.IsVirtual
                && candidate.Name == method.Name
                && candidate.GenericParameters.Count == method.GenericParameters.Count
                && candidate.Parameters.Count == method.Parameters.Count
                && candidate.Parameters.Zip(method.Parameters).All(pair => pair.First.ParameterType.FullName == pair.Second.ParameterType.FullName));
            if (baseMethod is null) continue;
            if (!edges.Any(e => e.BaseToken == baseMethod.MetadataToken.ToInt32() && e.DerivedToken == method.MetadataToken.ToInt32()))
                edges.Add(new(baseMethod.MetadataToken.ToInt32(), method.MetadataToken.ToInt32(), "implicit reuse-slot", baseMethod.FullName, method.FullName));
            break;
        }
    }
    return edges;
}

static TypeDefinition? SafeResolve(TypeReference? type)
{
    try { return type?.Resolve(); }
    catch (AssemblyResolutionException) { return null; }
}

static MethodDefinition? SafeResolveMethod(MethodReference method)
{
    try { return method.Resolve(); }
    catch (AssemblyResolutionException) { return null; }
}

static void VerifyOverrideEdges(ModuleDefinition module, IEnumerable<OverrideEdge> edges)
{
    foreach (var edge in edges)
    {
        var baseMethod = (MethodDefinition)module.LookupToken(edge.BaseToken);
        var derived = (MethodDefinition)module.LookupToken(edge.DerivedToken);
        if (derived.Name != baseMethod.Name || !derived.IsVirtual || !baseMethod.IsVirtual)
            throw new InvalidOperationException($"virtual_slot_name_mismatch:0x{edge.DerivedToken:X8}");
        if (edge.Kind == "implicit reuse-slot" && (derived.IsNewSlot || SafeResolve(derived.DeclaringType.BaseType) != baseMethod.DeclaringType))
            throw new InvalidOperationException($"virtual_slot_reuse_mismatch:0x{edge.DerivedToken:X8}");
        if (edge.Kind == "explicit MethodImpl" && !derived.Overrides.Any(o => SafeResolveMethod(o)?.MetadataToken.ToInt32() == edge.BaseToken))
            throw new InvalidOperationException($"virtual_slot_methodimpl_mismatch:0x{edge.DerivedToken:X8}");
    }
}

static bool VisibleType(TypeDefinition type) => type.DeclaringType is null ? type.IsPublic : (type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamilyOrAssembly) && VisibleType(type.DeclaringType);

static bool VisibleAccessor(MethodDefinition? method) => method is not null && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);

static string CanonicalOperand(object? operand) => operand switch
{
    null => "",
    IMetadataTokenProvider member => $"token:{member.MetadataToken.ToInt32():X8}",
    Instruction target => $"branch:{target.Offset:X4}",
    Instruction[] targets => "switch:" + string.Join(",", targets.Select(t => t.Offset.ToString("X4", CultureInfo.InvariantCulture))),
    string value => "string:" + JsonSerializer.Serialize(value),
    IFormattable number => $"{operand.GetType().FullName}:{number.ToString(null, CultureInfo.InvariantCulture)}",
    _ => $"{operand.GetType().FullName}:{operand}"
};

record RenameRecord(string Kind, int Token, string Type, string Signature, string OldName, string NewName, string Reason, int? BaseToken = null);

record OverrideEdge(int BaseToken, int DerivedToken, string Kind, string BaseSignature, string DerivedSignature);

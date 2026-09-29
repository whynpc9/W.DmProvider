# T02 R baseline restoration differences

Date: 2026-09-29. Scope: official `lib/net9.0/DM.DmProvider.dll` at SHA-256 `8f6e59680d0a076df53bea50d5a2bdbd288535cd85b2d7ca5064c02adc9c6e6b` and the read-only `decompiled/net9.0` source. The restored R is built from generated C# under `.local/t02/restored/`; the metadata-normalized DLL is an intermediate decompiler input only. R is an internal comparison baseline, not the W product.

## Mechanical recovery

The original decompiled project uses `LangVersion=15.0`, which the locked SDK 10.0.203 compiler rejected. R fixes the generated project to C# 13.0, net9.0, `AssemblyName=DM.DmProvider`, and `IsPackable=false`.

The first source build reported 274 C# errors, concentrated in `A/A.cs`, `A/B.cs`, and `A/D.cs`. Their original CLR metadata permits field and member names, return-type overloads, and enclosing-type name collisions that C# cannot declare. `RestoreBaseline` renames 50 fields and 25 methods by original metadata token in a temporary copy of the official assembly. It regenerates 25 top-level types: the seven `A` types and precisely detected external callers, plus types sharing the same source files. See `source-map.json` for every original token, signature, source path, restored path, rename reason, and caller reference. No original package or decompiled file is edited.

An initial R build was statically clean but failed the real Open probe with `DmException` 6001. A masked inner error was 6080, server version wrong. A scoped diagnostic showed a zero message-version header and an empty server-version string. The generator had renamed base virtual `A.a::a(A.b,int,bool,bool):A.b` (token `0x06000A4D`) without renaming its reuse-slot override `A.D::a(...)` (token `0x06000A5E`). C# compiled the derived method as a separate virtual; a call through `A.a` reached the base stub and never received the handshake response. The generator now captures override edges before renaming, propagates slot names through descendants, and checks the slot relationship after the metadata copy is written and read back. The affected edge is an override in regenerated C# and is recorded with both tokens and original signatures in `source-map.json`. This is a restoration defect fix, not a protocol change.

The generator compared canonical fingerprints for 4,782 method bodies across the original DLL, the in-memory renamed module, and the written normalized DLL after readback. The fingerprints cover instructions, typed literals, metadata-token operands, branch targets, exception regions, local variable types, `InitLocals`, and max stack. All matched; aggregate SHA-256: `0C4D2C71FD43B4C858C0226733FD7414373C7C2281F94AB9E6B07E0BC442F042`. This proves the temporary metadata rename did not alter those IL bodies. It does not prove the newly compiled R is behaviorally equivalent to O.

One further C# repair was required: `Dm.DmSetValue.decStringToBcd`, method token `0x06000881`. ILSpy emitted a conditional expression inferred as `int` into a `byte` local. An explicit cast of the whole expression to `byte` matches original IL `conv.u1; stloc.0` at IL_003e–003f and byte-local stores of values 10, 11, 12, and 15 at IL_0065–0076. The original path and generated path are in `source-map.json`. No known driver bug was changed.

## Static comparison

- R clean build: exit 0, 86 warnings, 0 errors. The warnings are from the upstream decompilation and are retained for review. R DLL SHA-256 after the virtual-slot repair: `3c0ddac138f284ac5ed28f8aca0850155781e135225f975e8b58609d03f50ef9`; it differs from O as a separately compiled assembly should.
- Public API capture: O and R each have 141 visible types and 3,651 sorted API entries. `public-api-official.txt` and `public-api-restored.txt` have an empty diff.
- Main assembly manifest resources: two named resources, `Dm.DmErrorDefinition.resources` and `Dm.ReservedWords.txt`. ILSpy expands the first into 240 entries, so its full listing has 241 lines for both O and R with an empty diff.
- Satellite resource assemblies: en, zh-CN, zh-HK, and zh-TW survive a clean R build, each byte-identical to the corresponding fixed official asset. The generated project copies them as content, so a repeated materialization at a deeper output path also built with all four present.
- `Mono.Cecil` 0.11.6 and `ilspycmd` 10.1.1.8388 are pinned in `tools/RestoreBaseline`; the repository pins SDK 10.0.203. Reproduction commands are in that tool's README.

Behavioral O/R differential tests and server final-state checks are separate acceptance evidence. All token-renamed members are marked for differential tests in `source-map.json`.

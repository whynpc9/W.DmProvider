# T02 restored baseline generator

`RestoreBaseline` produces an internal R baseline from the fixed official net9.0 DLL and the read-only `decompiled/net9.0` tree. It writes generated C# under `.local/t02/`, then a separate `dotnet build` compiles the R DLL. The normalized DLL is only an ILSpy input; it is never the R output.

The repository `global.json` fixes SDK 10.0.203. `RestoreBaseline.csproj` fixes Mono.Cecil 0.11.6, and `.config/dotnet-tools.json` fixes ilspycmd 10.1.1.8388. Restore these dependencies from NuGet on a clean machine. Keep `DOTNET_CLI_HOME` in a writable task directory.

```sh
export DOTNET_CLI_HOME="$PWD/.local/t02/dotnet-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet tool restore --tool-manifest tools/RestoreBaseline/.config/dotnet-tools.json
dotnet build tools/RestoreBaseline/RestoreBaseline.csproj -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
dotnet tools/RestoreBaseline/bin/Debug/net10.0/RestoreBaseline.dll restore "$PWD" "$PWD/.local/t02/restored"
dotnet build .local/t02/restored/DM.DmProvider.Restored.csproj -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
```

`restore` accepts any output directory below the repository's `.local/t02/` directory. The generator copies the original decompiled files, normalizes metadata name collisions and their virtual override chains in a temporary reference DLL, and regenerates only types with renamed members, their IL callers, and other types sharing those source files. It records the original metadata token, IL body audit, and virtual-slot audit in the output directory's `source-map.json`. The default output also updates the canonical `upstream/DM.DmProvider/8.3.1.47463/source-map.json`; alternate output directories leave that file untouched. The only additional source repair is the documented byte cast in `DmSetValue.decStringToBcd`.

The generated project uses `AssemblyName=DM.DmProvider` solely for O/R comparison, targets net9.0, and has `IsPackable=false`. Neutral resources compile from the original `.resx` and text file. The four original satellite resource assemblies are copied as content for en, zh-CN, zh-HK, and zh-TW, including after a clean build.

To recapture visible API lists after building R:

```sh
dotnet tools/RestoreBaseline/bin/Debug/net10.0/RestoreBaseline.dll api packages/extracted/lib/net9.0/DM.DmProvider.dll upstream/DM.DmProvider/8.3.1.47463/public-api-official.txt
dotnet tools/RestoreBaseline/bin/Debug/net10.0/RestoreBaseline.dll api .local/t02/restored/bin/Debug/net9.0/DM.DmProvider.dll upstream/DM.DmProvider/8.3.1.47463/public-api-restored.txt
diff -u upstream/DM.DmProvider/8.3.1.47463/public-api-official.txt upstream/DM.DmProvider/8.3.1.47463/public-api-restored.txt
```

The API format is one sorted line per visible type or field, method, property, or event: `T|`, `F|`, `M|`, `P|`, or `E|` followed by the Cecil metadata signature. A matching list is a static surface check, not a behavioral equivalence result.

# T03 one-time product source import

`ImportRestoredSource` consumes a newly generated T02 R source tree and writes the independent W source tree plus `docs/compatibility/t03-source-map.json`. It is an import tool, not a build dependency of `src/W.DmProvider/W.DmProvider.csproj`.

Before importing, verify `tools/RestoreBaseline/Program.cs`, `RestoreBaseline.csproj`, and its tool manifest against the SHA-256 entries in `docs/implementation/evidence/T02-validation.json`. With the repository SDK 10.0.203 and the environment settings in `AGENTS.md`:

```sh
dotnet tool restore --tool-manifest tools/RestoreBaseline/.config/dotnet-tools.json
dotnet tools/RestoreBaseline/bin/Debug/net10.0/RestoreBaseline.dll restore "$PWD" "$PWD/.local/t02/t03-seed"
dotnet build tools/ImportRestoredSource/ImportRestoredSource.csproj -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
dotnet tools/ImportRestoredSource/bin/Debug/net10.0/ImportRestoredSource.dll "$PWD" "$PWD/.local/t02/t03-seed"
```

The tool expects 241 restored C# files after excluding the former `Properties/AssemblyInfo.cs`. It also imports the two neutral resources and extracts culture strings from the fixed official satellite assets into four `.resx` files. Roslyn parsing locates namespace identifier tokens; source strings, SQL, comments, resource keys, and reflection strings are not rewritten. The one resource lookup changed explicitly is `DmConst.res`, which now uses the preserved `Dm.DmErrorDefinition` resource base name. Global event args move into `W.Dm`. The recovered `DmCommand` and `DmDataReader` source files receive readable product filenames.

The map records each source and product SHA-256, including the satellite-asset-to-resx relationship. A second run is idempotent only if all product files still match the map. It rejects modified or pre-existing untracked product files before writing any output. Changes to an imported product file should be made as normal product edits and reviewed against the original map, not silently overwritten by a re-import.

After building W, generate its visible API list with the fixed T02 `api` command and run `generate_api_map.py` against the T02 restored public API list. The generator requires 3,651 unique old signatures and exact old-to-W namespace translation before writing `docs/compatibility/t03-api-map.json`. Its classifications document T03 compatibility policy; they do not test runtime behavior.

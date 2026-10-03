#!/usr/bin/env bash
set -euo pipefail
set +x

# Only the independent Low acceptance owner executes this frozen-package probe.
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"
if (( $# != 3 )) || [[ "$1" != offline && "$1" != shared && "$1" != tls ]] ||
   [[ "$2" != "0.1.0-r3.t16.20261002143244" ]]; then
  printf '{"schema_version":1,"task":"T17-upload-profile","status":"rejected","classification":"usage","exit_code":64}\n'
  exit 64
fi
mode="$1"
version="$2"
source_dir="$(cd "$3" 2>/dev/null && pwd)" || exit 66
run="$repo/.local/verification/r3/t17/upload-profile/runs/$(date -u +%Y%m%dT%H%M%SZ)-$$-$RANDOM"
umask 077
mkdir -p "$run/app" "$run/obj" "$run/nuget-cache" "$run/cli-home"
export DOTNET_CLI_HOME="$run/cli-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES="$run/nuget-cache"
dotnet_command="${DOTNET_COMMAND:-dotnet}"
flags=(-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers)

record() {
  python3 - "$run/commands.json" "$1" "$2" "${@:3}" <<'PY'
import json,sys
from pathlib import Path
path=Path(sys.argv[1]); commands=json.loads(path.read_text()) if path.exists() else []
commands.append({'stage':sys.argv[2], 'exit_code':int(sys.argv[3]), 'argv':sys.argv[4:]})
path.write_text(json.dumps(commands,indent=2)+'\n')
PY
}
failed() {
  python3 - "$mode" "$1" "$2" "$run" <<'PY'
import json,sys
print(json.dumps({'schema_version':1,'task':'T17-upload-profile','mode':sys.argv[1],'status':'rejected',
                  'stage':sys.argv[2],'exit_code':int(sys.argv[3]),'run_dir':sys.argv[4]},separators=(',',':')))
PY
  exit "$2"
}
package_file="$source_dir/W.DmProvider.$version.nupkg"
[[ -f "$package_file" ]] || failed package_missing 66
inspection=(python3 "$repo/tools/R2AsyncProbe/package_manifest.py" "$package_file" "$version" "$run/package-manifest.json")
inspection_code=0
"${inspection[@]}" >"$run/package-inspection.json" 2>/dev/null || inspection_code=$?
record package_inspection "$inspection_code" "${inspection[@]}"
(( inspection_code == 0 )) || failed package_inspection "$inspection_code"

source_identity=(python3 "$repo/tools/R3UploadProfileProbe/source_identity.py" "$repo" "$run/consumer-source-manifest.json")
source_code=0
"${source_identity[@]}" >"$run/consumer-source-inspection.json" 2>/dev/null || source_code=$?
record consumer_source_identity "$source_code" "${source_identity[@]}"
(( source_code == 0 )) || failed consumer_source_identity "$source_code"

build=("$dotnet_command" build tools/R3UploadProfileProbe/R3UploadProfileProbe.csproj --configuration Release --output "$run/app"
  /p:R3PackageVersion="$version" /p:R3PackageSource="$source_dir" /p:RestoreSources="$source_dir"
  /p:BaseIntermediateOutputPath="$run/obj/" /p:MSBuildProjectExtensionsPath="$run/obj/" "${flags[@]}")
build_code=0
"${build[@]}" >"$run/consumer-build.log" 2>&1 || build_code=$?
record consumer_build "$build_code" "${build[@]}"
(( build_code == 0 )) || failed consumer_build "$build_code"
[[ -s "$run/app/R3UploadProfileProbe.dll" ]] || failed consumer_assembly_missing 1

# No secrets enter the build. Runtime stderr is discarded and exception text
# is never serialized. The launcher bounds runtime; probe.json is checkpointed.
probe=(python3 "$repo/tools/R3UploadProfileProbe/run_probe.py" "$run/probe-stdout.json" "$dotnet_command"
  "$run/app/R3UploadProfileProbe.dll" "$mode" "$repo" "$run/package-manifest.json" "$run/probe.json")
case "$mode" in
  shared) probe=(scripts/with-dameng-test.sh "${probe[@]}") ;;
  tls) probe=(scripts/with-dameng-tls-test.sh "${probe[@]}") ;;
esac
probe_code=0
"${probe[@]}" >"$run/launcher.json" 2>/dev/null || probe_code=$?
record runtime_probe "$probe_code" "${probe[@]}"
[[ -s "$run/probe.json" ]] || failed probe_result_missing "$(( probe_code == 0 ? 1 : probe_code ))"
(( probe_code == 0 )) || failed runtime_probe "$probe_code"
validation=(python3 "$repo/tools/R3UploadProfileProbe/validate.py" "$run/probe.json" "$run/package-manifest.json" "$run/app/W.DmProvider.dll" "$mode")
validation_code=0
"${validation[@]}" >"$run/validation.json" 2>/dev/null || validation_code=$?
record envelope_validation "$validation_code" "${validation[@]}"
(( validation_code == 0 )) || failed envelope_validation "$validation_code"

final_inspection=(python3 "$repo/tools/R2AsyncProbe/package_manifest.py" "$package_file" "$version" "$run/package-manifest-final.json")
final_code=0
"${final_inspection[@]}" >"$run/package-inspection-final.json" 2>/dev/null || final_code=$?
record final_package_inspection "$final_code" "${final_inspection[@]}"
(( final_code == 0 )) || failed final_package_inspection "$final_code"
compare_code=0
cmp -s "$run/package-manifest.json" "$run/package-manifest-final.json" || compare_code=$?
record immutable_package_compare "$compare_code" cmp -s "$run/package-manifest.json" "$run/package-manifest-final.json"
(( compare_code == 0 )) || failed package_changed_during_run 1
python3 - "$run/validation.json" "$run" <<'PY'
import json,sys
result=json.load(open(sys.argv[1])); result['run_dir']=sys.argv[2]
print(json.dumps(result,separators=(',',':')))
PY

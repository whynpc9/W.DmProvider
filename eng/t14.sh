#!/usr/bin/env bash
set -euo pipefail
set +x
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"
if (( $# != 3 )) || [[ "$1" != shared && "$1" != tls ]] ||
   [[ ! "$2" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$ ]]; then
  printf '{"task":"T14","status":"rejected","classification":"usage","exit_code":64}\n'
  exit 64
fi
mode="$1"; version="$2"
source_dir="$(cd "$3" && pwd)"
run="$repo/.local/verification/r2/t14/runs/$(date -u +%Y%m%dT%H%M%SZ)-$$-$RANDOM"
mkdir -p "$run/app" "$run/obj" "$run/nuget-cache" "$run/cli-home"
chmod 700 "$run" "$run/cli-home" "$run/nuget-cache"
export DOTNET_CLI_HOME="$run/cli-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES="$run/nuget-cache"
dotnet_command="${DOTNET_COMMAND:-dotnet}"
failed() {
  printf '{"task":"T14","mode":"%s","status":"rejected","stage":"%s","exit_code":%s,"run_dir":"%s"}\n' "$mode" "$1" "$2" "$run"
  exit "$2"
}
package="$source_dir/W.DmProvider.$version.nupkg"
[[ -f "$package" ]] || failed package_missing 66
python3 tools/R2CancellationProbe/package_manifest.py "$package" "$version" "$run/package-manifest.json" \
  >"$run/package-inspection.json" 2>/dev/null || failed package_inspection 1
"$dotnet_command" build tools/R2CancellationProbe/R2CancellationProbe.csproj --configuration Release --output "$run/app" \
  /p:R2PackageVersion="$version" /p:R2PackageSource="$source_dir" /p:RestoreSources="$source_dir" \
  /p:BaseIntermediateOutputPath="$run/obj/" /p:MSBuildProjectExtensionsPath="$run/obj/" \
  -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers \
  >"$run/consumer-build.log" 2>&1 || failed consumer_build 1
probe_code=0
if [[ "$mode" == tls ]]; then wrapper=scripts/with-dameng-tls-test.sh; else wrapper=scripts/with-dameng-test.sh; fi
"$wrapper" python3 tools/R2CancellationProbe/run_probe.py "$dotnet_command" "$run/app/R2CancellationProbe.dll" \
  "$mode" "$repo" "$run/package-manifest.json" "$run/probe.json" >"$run/probe-stdout.json" 2>/dev/null || probe_code=$?
[[ -s "$run/probe.json" ]] || failed probe_result_missing "$(( probe_code == 0 ? 1 : probe_code ))"
(( probe_code == 0 )) || failed probe "$probe_code"
python3 tools/R2CancellationProbe/validate.py "$run/probe.json" "$run/package-manifest.json" "$run/app/W.DmProvider.dll" "$mode" \
  >"$run/validation.json" 2>/dev/null || failed result_validation 1
python3 tools/R2CancellationProbe/package_manifest.py "$package" "$version" "$run/package-manifest-final.json" \
  >"$run/package-inspection-final.json" 2>/dev/null || failed final_package_inspection 1
cmp -s "$run/package-manifest.json" "$run/package-manifest-final.json" || failed package_changed 1
python3 - "$run/validation.json" "$run" <<'PY'
import json,sys
result=json.load(open(sys.argv[1]));result['run_dir']=sys.argv[2]
print(json.dumps(result,separators=(',',':')))
PY

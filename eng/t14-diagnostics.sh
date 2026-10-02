#!/usr/bin/env bash
set -euo pipefail
set +x
repo="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo"
if (( $# < 4 || $# > 5 )) || [[ "$1" != health && "$1" != readers ]] ||
  [[ "$4" != old-before && "$4" != new && "$4" != old-after ]] ||
  [[ ! "$2" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$ ]] ||
  [[ "$1" == health && $# != 4 ]] || [[ "$1" == readers && $# != 5 ]]; then
  printf '{"task":"T14-diagnostics","status":"rejected","classification":"usage","exit_code":64}\n'
  exit 64
fi
mode="$1"; version="$2"; lane="$4"
gate=""
if [[ "$mode" == readers ]]; then gate="$(cd "$(dirname "$5")" && pwd)/$(basename "$5")"; [[ -f "$gate" ]] || exit 66; fi
source_dir="$(cd "$3" && pwd)"
run="$repo/.local/verification/r2/t14/diagnostic-runs/$(date -u +%Y%m%dT%H%M%SZ)-$$-$RANDOM"
mkdir -p "$run/app" "$run/obj" "$run/cache" "$run/cli-home"
chmod 700 "$run" "$run/cache" "$run/cli-home"
export DOTNET_CLI_HOME="$run/cli-home" DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES="$run/cache"
dotnet_command=dotnet
if declare -p DOTNET_COMMAND >/dev/null 2>&1 && [[ -n "$DOTNET_COMMAND" ]]; then dotnet_command="$DOTNET_COMMAND"; fi
failed() {
  printf '{"task":"T14-diagnostics","mode":"%s","lane":"%s","status":"rejected","stage":"%s","exit_code":%s,"run_dir":"%s"}\n' "$mode" "$lane" "$1" "$2" "$run"
  exit "$2"
}
package="$source_dir/W.DmProvider.$version.nupkg"
[[ -f "$package" ]] || failed package_missing 66
python3 tools/R2CancellationDiagnostics/package_manifest.py "$package" "$version" "$run/package-manifest.json" >"$run/package-inspection.json" 2>/dev/null || failed package_inspection 1
"$dotnet_command" build tools/R2CancellationDiagnostics/Diagnostics.csproj --configuration Release --output "$run/app" \
  /p:DiagnosticPackageVersion="$version" /p:DiagnosticPackageSource="$source_dir" /p:RestoreSources="$source_dir" \
  /p:BaseIntermediateOutputPath="$run/obj/" /p:MSBuildProjectExtensionsPath="$run/obj/" \
  -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers >"$run/build.log" 2>&1 || failed consumer_build 1
probe_code=0
scripts/with-dameng-test.sh python3 tools/R2CancellationDiagnostics/run_probe.py "$dotnet_command" "$run/app/Diagnostics.dll" \
  "$mode" "$lane" "$repo" "$run/package-manifest.json" "$run/probe.json" "$gate" >"$run/probe-stdout.json" 2>/dev/null || probe_code=$?
[[ -s "$run/probe.json" ]] || failed probe_result_missing "$(( probe_code == 0 ? 1 : probe_code ))"
(( probe_code == 0 )) || failed probe "$probe_code"
python3 tools/R2CancellationDiagnostics/validate.py "$run/probe.json" "$run/package-manifest.json" "$run/app/W.DmProvider.dll" \
  >"$run/validation.json" 2>/dev/null || failed validation 1
python3 tools/R2CancellationDiagnostics/package_manifest.py "$package" "$version" "$run/package-manifest-final.json" >"$run/package-inspection-final.json" 2>/dev/null || failed final_package_inspection 1
cmp -s "$run/package-manifest.json" "$run/package-manifest-final.json" || failed package_changed 1
python3 - "$run/validation.json" "$run" <<'PY'
import json,sys
report=json.load(open(sys.argv[1]));report['run_dir']=sys.argv[2]
print(json.dumps(report,separators=(',',':')))
PY

#!/usr/bin/env bash
set -euo pipefail
set +x

# Only the designated verification owner runs this script. It consumes an
# already-packed immutable candidate; it never rebuilds provider source.
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"
if (( $# != 3 )) || [[ "$1" != offline && "$1" != shared && "$1" != tls && "$1" != security-shared && "$1" != security-tls ]] ||
   [[ ! "$2" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$ ]]; then
  printf '{"schema_version":1,"task":"T13","status":"rejected","classification":"usage","exit_code":64}\n'
  exit 64
fi
mode="$1"
version="$2"
source_dir="$(cd "$3" && pwd)" || exit 66
run="$repo/.local/verification/r2/t13/runs/$(date -u +%Y%m%dT%H%M%SZ)-$$-$RANDOM"
mkdir -p "$run/app" "$run/obj" "$run/nuget-cache" "$run/cli-home"
chmod 700 "$run" "$run/cli-home" "$run/nuget-cache"
export DOTNET_CLI_HOME="$run/cli-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES="$run/nuget-cache"
dotnet_command="${DOTNET_COMMAND:-dotnet}"
flags=(-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers)
failed() {
  local failed_stage="$1" failed_code="$2"
  printf '{"schema_version":1,"task":"T13","mode":"%s","status":"rejected","stage":"%s","exit_code":%s,"run_dir":"%s"}\n' \
    "$mode" "$failed_stage" "$failed_code" "$run"
  exit "$failed_code"
}

package_file="$source_dir/W.DmProvider.$version.nupkg"
[[ -f "$package_file" ]] || failed package_missing 66
python3 "$repo/tools/R2AsyncProbe/package_manifest.py" "$package_file" "$version" "$run/package-manifest.json" \
  >"$run/package-inspection.json" 2>/dev/null || failed package_inspection 1
build_code=0
"$dotnet_command" build tools/R2AsyncProbe/R2AsyncProbe.csproj --configuration Release \
  --output "$run/app" /p:R2PackageVersion="$version" /p:R2PackageSource="$source_dir" \
  /p:RestoreSources="$source_dir" /p:BaseIntermediateOutputPath="$run/obj/" /p:MSBuildProjectExtensionsPath="$run/obj/" \
  "${flags[@]}" >"$run/consumer-build.log" 2>&1 || build_code=$?
(( build_code == 0 )) || failed consumer_build "$build_code"
[[ -s "$run/app/R2AsyncProbe.dll" ]] || failed consumer_assembly_missing 1

# Credentials are loaded only for the actual runtime invocation. Probe stdout
# and its report contain only checked fields, never exception text or SQL.
probe_code=0
case "$mode" in
  offline)
    "$dotnet_command" "$run/app/R2AsyncProbe.dll" offline "$repo" "$run/package-manifest.json" "$run/probe.json" \
      >"$run/probe-stdout.json" 2>/dev/null || probe_code=$?
    ;;
  shared|security-shared)
    scripts/with-dameng-test.sh "$dotnet_command" "$run/app/R2AsyncProbe.dll" "$mode" "$repo" "$run/package-manifest.json" "$run/probe.json" \
      >"$run/probe-stdout.json" 2>/dev/null || probe_code=$?
    ;;
  tls|security-tls)
    scripts/with-dameng-tls-test.sh "$dotnet_command" "$run/app/R2AsyncProbe.dll" "$mode" "$repo" "$run/package-manifest.json" "$run/probe.json" \
      >"$run/probe-stdout.json" 2>/dev/null || probe_code=$?
    ;;
esac
[[ -s "$run/probe.json" ]] || failed probe_result_missing "$(( probe_code == 0 ? 1 : probe_code ))"
(( probe_code == 0 )) || failed probe "$probe_code"
python3 "$repo/tools/R2AsyncProbe/validate.py" "$run/probe.json" "$run/package-manifest.json" "$run/app/W.DmProvider.dll" "$mode" \
  >"$run/validation.json" 2>/dev/null || failed result_validation 1

# Detect a package replacement during consumer restore/run. No mutable version
# can accidentally be accepted against a different nupkg.
python3 "$repo/tools/R2AsyncProbe/package_manifest.py" "$package_file" "$version" "$run/package-manifest-final.json" \
  >"$run/package-inspection-final.json" 2>/dev/null || failed final_package_inspection 1
cmp -s "$run/package-manifest.json" "$run/package-manifest-final.json" || failed package_changed_during_run 1
python3 - "$run/validation.json" "$run" <<'PY'
import json,sys
result=json.load(open(sys.argv[1])); result['run_dir']=sys.argv[2]
print(json.dumps(result,separators=(',',':')))
PY

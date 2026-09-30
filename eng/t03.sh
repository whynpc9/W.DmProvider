#!/usr/bin/env bash
set -euo pipefail
set +x

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
base="$root/.local/t03"
mkdir -p "$base/packages" "$base/logs" "$base/dotnet-cli-home"
export DOTNET_CLI_HOME="$base/dotnet-cli-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES="$base/pack-nuget"
mkdir -p "$NUGET_PACKAGES"
flags=(-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers)

error() { printf '{"schema_version":1,"status":"rejected","reason":"%s"}\n' "$1"; exit 1; }
run_logged() {
  local log="$1"; shift
  if "$@" >"$log" 2>&1; then return 0; fi
  printf 'command_failed log=%s\n' "$log" >&2
  return 1
}
current() {
  if [[ -n "${1:-}" ]]; then
    version="$1"
  else
    [[ -f "$base/packages/version" ]] || error missing_version
    version="$(cat "$base/packages/version")"
  fi
  [[ "$version" =~ ^0\.1\.0-t03\.[0-9]{14}$ ]] || error invalid_version
  run_dir="$base/packages/$version"
  package="$run_dir/W.DmProvider.$version.nupkg"
  manifest="$run_dir/manifest.json"
  [[ -f "$package" && -f "$manifest" ]] || error candidate_incomplete
  python3 tools/ProductPackageProbe/inspect.py verify "$package" "$manifest" >/dev/null || error candidate_changed
}
consumer() {
  current "${1:-}"
  export NUGET_PACKAGES="$run_dir/nuget-cache"
  mkdir -p "$NUGET_PACKAGES" "$run_dir/obj" "$run_dir/app"
  local props=("/p:T03PackageVersion=$version" "/p:BaseIntermediateOutputPath=$run_dir/obj/" "/p:OutputPath=$run_dir/app/" /p:AppendTargetFrameworkToOutputPath=false)
  run_logged "$run_dir/restore.log" dotnet restore tools/ProductPackageProbe/ProductPackageProbe.csproj --source "$run_dir" "${props[@]}" "${flags[@]}" || return 1
  run_logged "$run_dir/consumer-build.log" dotnet build tools/ProductPackageProbe/ProductPackageProbe.csproj --no-restore "${props[@]}" "${flags[@]}" || return 1
  [[ -f "$run_dir/app/ProductPackageProbe.dll" && -f "$run_dir/app/W.DmProvider.dll" ]] || error consumer_output_missing
  python3 tools/ProductPackageProbe/inspect.py consumer "$run_dir" "$version" > "$run_dir/consumer-graph.json" || return 1
  # The package asset hash is checked again inside the running consumer.
}

if (( $# < 1 || $# > 2 )); then error usage; fi
case "$1" in
  pack)
    (( $# == 1 )) || error usage
    version="0.1.0-t03.$(date -u +%Y%m%d%H%M%S)"
    run_dir="$base/packages/$version"
    mkdir "$run_dir" || error version_already_exists
    package="$run_dir/W.DmProvider.$version.nupkg"
    manifest="$run_dir/manifest.json"
    before_source="$(python3 tools/ProductPackageProbe/inspect.py source src/W.DmProvider)"
    run_logged "$run_dir/pack.log" dotnet pack src/W.DmProvider/W.DmProvider.csproj --configuration Release \
      --output "$run_dir" "/p:Version=$version" "/p:PackageVersion=$version" "${flags[@]}" || exit 1
    after_source="$(python3 tools/ProductPackageProbe/inspect.py source src/W.DmProvider)"
    [[ "$before_source" == "$after_source" ]] || error source_changed_during_pack
    [[ -f "$package" ]] || error package_missing
    python3 tools/ProductPackageProbe/inspect.py package "$package" "$manifest" "$version" src/W.DmProvider > "$run_dir/pack-result.json" || exit 1
    printf '%s\n' "$version" > "$base/packages/version.tmp"
    mv "$base/packages/version.tmp" "$base/packages/version"
    cat "$run_dir/pack-result.json"
    ;;
  offline|real)
    mode="$1"
    consumer "${2:-}" || exit 1
    output="$run_dir/$mode-result.json"
    rm -f "$output"
    status=0
    if [[ "$mode" == real ]]; then
      scripts/with-dameng-test.sh dotnet "$run_dir/app/ProductPackageProbe.dll" real "$root" "$manifest" > "$output" || status=$?
    else
      dotnet "$run_dir/app/ProductPackageProbe.dll" offline "$root" "$manifest" > "$output" || status=$?
    fi
    [[ -s "$output" ]] || error consumer_result_missing
    cat "$output"
    exit "$status"
    ;;
  api)
    consumer "${2:-}" || exit 1
    export NUGET_PACKAGES="$base/api-nuget"
    mkdir -p "$NUGET_PACKAGES"
    run_logged "$run_dir/api-tool-build.log" dotnet build tools/RestoreBaseline/RestoreBaseline.csproj "${flags[@]}" || exit 1
    run_logged "$run_dir/api-export.log" dotnet tools/RestoreBaseline/bin/Debug/net10.0/RestoreBaseline.dll api \
      "$run_dir/app/W.DmProvider.dll" "$run_dir/public-api-w.txt" || exit 1
    python3 tools/ProductPackageProbe/inspect.py api docs/compatibility/t03-api-map.json \
      upstream/DM.DmProvider/8.3.1.47463/public-api-official.txt "$run_dir/public-api-w.txt"
    ;;
  *) error unknown_action ;;
esac

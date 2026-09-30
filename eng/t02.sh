#!/usr/bin/env bash
set -euo pipefail
set +x

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
export DOTNET_CLI_HOME="$repo_root/.local/t02/dotnet-cli-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
mkdir -p "$DOTNET_CLI_HOME" "$repo_root/.local/t02/evidence"

project_for() {
  if [[ "$1" == O ]]; then
    printf '%s' tools/BaselineComparison/Official/Official.csproj
  else
    printf '%s' tools/BaselineComparison/Restored/Restored.csproj
  fi
}
host_for() {
  if [[ "$1" == O ]]; then
    printf '%s' tools/BaselineComparison/Official/bin/Debug/net10.0/Official.dll
  else
    printf '%s' tools/BaselineComparison/Restored/bin/Debug/net10.0/Restored.dll
  fi
}
build_host() {
  local variant="$1"
  export NUGET_PACKAGES="$repo_root/.local/t02/nuget-packages-$variant"
  mkdir -p "$NUGET_PACKAGES"
  local log="$repo_root/.local/t02/evidence/build-${variant}.log"
  if dotnet build "$(project_for "$variant")" -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers > "$log" 2>&1; then
    echo "build_${variant}=ok log=$log" >&2
  else
    tail -60 "$log" >&2
    return 1
  fi
}
run_host() {
  local variant="$1" mode="$2" output="$3"
  rm -f "$output"
  build_host "$variant" || return $?
  local temporary
  temporary="$(mktemp "$repo_root/.local/t02/evidence/${variant}-${mode}.XXXXXX")"
  local status=0
  if [[ "$mode" == integration || "$mode" == diagnose-open ]]; then
    scripts/with-dameng-test.sh dotnet "$(host_for "$variant")" "$mode" "$variant" "$repo_root" > "$temporary" || status=$?
  else
    dotnet "$(host_for "$variant")" "$mode" "$variant" "$repo_root" > "$temporary" || status=$?
  fi
  mv "$temporary" "$output"
  return "$status"
}

if (( $# != 1 )); then
  echo 'usage: eng/t02.sh build-o|build-r|offline-o|offline-r|official-o|official-r|compare|offline-compare|api|diagnose-r' >&2
  exit 64
fi
case "$1" in
  build-o|build-r)
    variant=O; [[ "$1" == build-r ]] && variant=R
    build_host "$variant"
    printf '{"schema_version":1,"implementation":"%s","status":"built"}\n' "$variant"
    ;;
  offline-o|offline-r|official-o|official-r)
    variant=O; [[ "$1" == *-r ]] && variant=R
    mode=offline; [[ "$1" == official-* ]] && mode=integration
    output="$repo_root/.local/t02/evidence/${variant}-${mode}.json"
    if run_host "$variant" "$mode" "$output"; then
      cat "$output"
      echo "local_result=$output" >&2
    else
      status=$?
      [[ -f "$output" ]] && cat "$output"
      echo "local_result=$output" >&2
      exit "$status"
    fi
    ;;
  compare|offline-compare)
    mode=integration; [[ "$1" == offline-compare ]] && mode=offline
    o="$repo_root/.local/t02/evidence/O-${mode}.json"
    r="$repo_root/.local/t02/evidence/R-${mode}.json"
    rm -f "$o" "$r"
    o_status=0; r_status=0
    run_host O "$mode" "$o" || o_status=$?
    run_host R "$mode" "$r" || r_status=$?
    if [[ -s "$o" && -s "$r" ]]; then
      python3 tools/BaselineComparison/compare.py "$o" "$r" "$mode" "$o_status" "$r_status"
    else
      printf '{"schema_version":1,"status":"incomplete","o_exit":%s,"r_exit":%s}\n' "$o_status" "$r_status"
      exit 1
    fi
    ;;
  api)
    o="$repo_root/.local/t02/evidence/O-api.json"
    r="$repo_root/.local/t02/evidence/R-api.json"
    rm -f "$o" "$r"
    run_host O api "$o"
    run_host R api "$r"
    python3 tools/BaselineComparison/compare.py "$o" "$r" api 0 0
    ;;
  diagnose-r)
    output="$repo_root/.local/t02/evidence/R-diagnose-open.json"
    status=0
    run_host R diagnose-open "$output" || status=$?
    [[ -f "$output" ]] && cat "$output"
    echo "local_result=$output" >&2
    exit "$status"
    ;;
  *) echo 'unknown T02 action' >&2; exit 64 ;;
esac

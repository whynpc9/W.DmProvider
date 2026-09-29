#!/usr/bin/env bash
set -euo pipefail
set +x

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
export DOTNET_CLI_HOME="$repo_root/.local/t01/dotnet-cli-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES="$repo_root/.local/t01/nuget-packages"
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES"

build_tools() {
  dotnet build tools/UpstreamSnapshot/UpstreamSnapshot.csproj -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
  dotnet build tools/DmProbe/DmProbe.csproj -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
}

if (( $# < 1 )); then
  echo 'usage: eng/t01.sh build|capture|verify|offline|official [copy-root] [manifest-path]' >&2
  exit 64
fi
action="$1"
shift
case "$action" in
  build) build_tools ;;
  capture)
    build_tools >&2
    dotnet tools/UpstreamSnapshot/bin/Debug/net10.0/UpstreamSnapshot.dll capture "${1:-$repo_root}" "${2:-${1:-$repo_root}/upstream/DM.DmProvider/8.3.1.47463/manifest.json}"
    ;;
  verify)
    build_tools >&2
    dotnet tools/UpstreamSnapshot/bin/Debug/net10.0/UpstreamSnapshot.dll verify "${1:-$repo_root}" "${2:-${1:-$repo_root}/upstream/DM.DmProvider/8.3.1.47463/manifest.json}"
    ;;
  offline)
    build_tools >&2
    dotnet tools/DmProbe/bin/Debug/net10.0/DmProbe.dll offline "$repo_root"
    ;;
  official)
    build_tools >&2
    result_file="$(mktemp "$repo_root/.local/t01/official.XXXXXX")"
    if scripts/with-dameng-test.sh dotnet tools/DmProbe/bin/Debug/net10.0/DmProbe.dll official "$repo_root" > "$result_file"; then
      cat "$result_file"
      echo "local_result=$result_file" >&2
    else
      status=$?
      cat "$result_file"
      echo "local_result=$result_file" >&2
      exit "$status"
    fi
    ;;
  *) echo 'unknown T01 action' >&2; exit 64 ;;
esac

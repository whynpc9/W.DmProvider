#!/usr/bin/env bash
set -euo pipefail
set +x

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
secret_file="$repo_root/.local/secrets/dameng-test.env"

if (( $# == 0 )); then
  echo 'usage: scripts/with-dameng-test.sh <command> [args...]' >&2
  exit 64
fi
if [[ ! -f "$secret_file" ]]; then
  echo 'local Dameng test secret is missing; see docs/implementation/local-dameng.md' >&2
  exit 66
fi

# The file is local, mode 0600, and ignored by Git. Never print its contents.
source "$secret_file"
if [[ "${DAMENG_TEST_CONNECTION_STRING:-}" != *'User Id=WDM_PROVIDER_TEST;'* ]]; then
  echo 'local Dameng test secret has the wrong account' >&2
  exit 78
fi
exec "$@"

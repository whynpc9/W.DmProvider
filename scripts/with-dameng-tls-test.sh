#!/usr/bin/env bash
set -euo pipefail
set +x

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
secret="$repo/.local/secrets/dameng-tls-test.env"
if (( $# == 0 )); then
  printf 'usage: scripts/with-dameng-tls-test.sh <command> [args...]\n' >&2
  exit 64
fi
if [[ ! -f "$secret" ]]; then
  printf 'local TLS TEST secret is missing; see docs/implementation/local-dameng-tls.md\n' >&2
  exit 66
fi
if [[ "$(uname -s)" == Darwin ]]; then
  secret_mode="$(stat -f %Lp "$secret")"
else
  secret_mode="$(stat -c %a "$secret")"
fi
if [[ "$secret_mode" != 600 ]]; then
  printf 'local TLS TEST secret mode must be 0600\n' >&2
  exit 78
fi
source "$secret"
if [[ ! "${DAMENG_TLS_TEST_PASSWORD:-}" =~ ^[A-Za-z0-9]{20,40}$ ]]; then
  printf 'local TLS TEST secret is invalid\n' >&2
  exit 78
fi
export DAMENG_TLS_TEST_CONNECTION_STRING="Server=127.0.0.1;Port=15236;User Id=WDM_PROVIDER_TEST;Password=${DAMENG_TLS_TEST_PASSWORD};"
unset DAMENG_TLS_TEST_PASSWORD
exec "$@"

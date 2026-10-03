#!/usr/bin/env bash
set -euo pipefail
set +x
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"
exec python3 tools/R3CandidateGate/gate.py "$@"

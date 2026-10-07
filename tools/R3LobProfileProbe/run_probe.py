#!/usr/bin/env python3
"""Bound one runtime invocation; never persist stderr, environment or raw errors."""
import json
from pathlib import Path
import os
import signal
import subprocess
import sys


def main() -> int:
    # Credentials enter only through the TEST wrapper that invokes this helper.
    destination = Path(sys.argv[1])
    command = sys.argv[2:]
    process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, start_new_session=True)
    try:
        stdout, _ = process.communicate(timeout=900)
    except subprocess.TimeoutExpired:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        process.communicate()
        destination.write_text(json.dumps({'status': 'rejected', 'classification': 'runtime_deadline',
                                           'exit_code': 124, 'server_final_state': 'requires_checkpoint_recovery'}) + '\n')
        return 124
    if len(stdout) > 1024 * 1024:
        destination.write_text('{"status":"rejected","classification":"output_bound","exit_code":1}\n')
        return 1
    # A failed runtime must still emit a single valid safe JSON envelope.
    result = json.loads(stdout)
    if not isinstance(result, dict) or result.get('task') != 'T17-profile':
        raise ValueError('envelope')
    destination.write_text(json.dumps(result, indent=2) + '\n')
    return process.returncode if 0 <= process.returncode <= 255 else 1


if __name__ == '__main__':
    try:
        sys.exit(main())
    except Exception:
        print('{"status":"rejected","classification":"runtime_launcher_failed","exit_code":1}')
        sys.exit(1)

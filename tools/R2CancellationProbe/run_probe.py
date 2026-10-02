#!/usr/bin/env python3
"""Bound the consumer process; suppress raw runtime stderr and exception text."""
import json
import subprocess
import sys

try:
    completed = subprocess.run(sys.argv[1:], stderr=subprocess.DEVNULL, timeout=1200)
    sys.exit(completed.returncode)
except subprocess.TimeoutExpired:
    print(json.dumps({'task': 'T14', 'status': 'rejected', 'classification': 'probe_process_safety_timeout', 'exit_code': 124}))
    sys.exit(124)
except Exception as error:
    print(json.dumps({'task': 'T14', 'status': 'rejected', 'classification': 'probe_process_failed', 'type': type(error).__name__}))
    sys.exit(1)

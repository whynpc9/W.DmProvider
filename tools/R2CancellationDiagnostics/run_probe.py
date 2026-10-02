#!/usr/bin/env python3
import json
import subprocess
import sys

try:
    timeout = 900 if len(sys.argv) > 3 and sys.argv[3] == 'readers' else 300
    result = subprocess.run(sys.argv[1:], stderr=subprocess.DEVNULL, timeout=timeout)
    sys.exit(result.returncode)
except subprocess.TimeoutExpired:
    print(json.dumps({'status': 'rejected', 'classification': 'diagnostic_process_safety_timeout', 'exit_code': 124}))
    sys.exit(124)
except Exception as error:
    print(json.dumps({'status': 'rejected', 'classification': 'diagnostic_process_failed', 'type': type(error).__name__}))
    sys.exit(1)

# S08 R2 asynchronous logical schedules

`scenarios.json` contains synthetic offline schedules for the client-side asynchronous, cancellation, deadline, and outcome contracts in [S08](../../../docs/implementation/v1/specs/08-async-cancellation-and-timeouts.md). Every scenario is marked `evidence_level: offline_logical_fixture` and `execution_status: not_run`.

Each ordered scenario supplies `id`, `contract`, `setup`, `steps`, and `expected_outcomes`. The Cancel/Close race supplies two explicit `schedule_variants` so a consumer can drive both event orderings with barriers. Step values are harness inputs such as pending gates, logical transfer counts, synthetic clock ticks, and identity tuples. They are not recorded provider behavior.

## Coverage

- ASY-01/02: async-only transport calls, caller progress while an async operation is pending, barrier-controlled response release, and short send/read progress.
- TMO-02: heartbeat progress can renew the synthetic read-idle budget while the total command deadline stays fixed.
- CAN-01/03/04/05: pre-cancel with zero sent bytes, ExecuteReader token unbinding, stale generation cancellation, and both Cancel/Close orderings.
- ERR-01: a transport failure after Commit dispatch leaves the commit outcome unknown and must not cause driver replay.

The fixture models client-side scheduling only. It contains no SQL, connection settings, credentials, authentication material, protocol bytes, opcode assumptions, or fabricated server responses. Heartbeat events and transfer counts are abstract harness signals; they do not describe Dameng wire behavior. Unknown Commit outcome does not assert whether the server committed.

## Evidence boundary

This file is only test input. It does not show that any test or implementation has passed. A consuming offline test must run these schedules against controlled fake async transport, barrier, and clock components; assert the stated observations; and retain its test command, runner/source identity, exit code, and observed results. Keep that execution evidence separate from this fixture. No database or socket was opened to prepare these inputs.

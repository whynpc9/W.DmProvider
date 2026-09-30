# Async cancellation and deadline scenarios

`scenarios.json` contains logical synthetic inputs and contract expectations. Every case is marked `not_run`. The cases use `session_id`, `session_generation`, `execution_id`, and `invocation_id` to bind each operation and cancellation to its owner.

All event timing uses a monotonic virtual clock or named barriers. No fixed sleeps are encoded. The fixture has no DM request/response bytes, cancellation opcode, native cancel acknowledgement, or server-side stop result. An abstract `send_attempted`, `partial_or_indeterminate`, or `validated_success` event is a harness state marker, not a wire trace.

## Coverage

- ASY-01/02: async-only fake transport whose synchronous methods throw, pending tasks held at a barrier, and completion after barrier release.
- CAN-01/02: pre-cancelled calls have zero side effects; cancellation after partial send/receive aborts the captured session without pool return, retry, or replay.
- CAN-03/04: ExecuteReader token scope ends on return; Read uses a new token; stale identities varying session, generation, execution, or invocation cannot abort a new lease.
- CAN-05: Cancel/Close orderings and success/cancel races release the captured transport once; Cancel with no active execution is a no-op.
- TMO-01/02: one Connect budget across stages; PoolAcquire and Connect budgets do not overlap; idle progress may restart only the idle budget; each Read gets a fresh call budget; internal fetches and heartbeat progress do not reset total deadlines; LOB chunks share a method-call budget; cleanup uses a separate finite budget.
- ERR-01: timeout and user cancellation retain distinct error kinds; the first terminal reason is recorded; commit after a send attempt prioritizes unknown outcome and never replays.

The zero-timeout rule applies only to public timeouts that allow it. Internal cleanup is finite. A commit acknowledgement followed by late cancellation remains Committed; a send attempt without a validated acknowledgement is OutcomeUnknown. The fixture does not claim cancellation stopped server work.

Preparing this fixture did not build, connect to a database, or exercise transport APIs.

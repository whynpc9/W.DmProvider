# T05 session ownership scenarios

scenarios.json contains deterministic synthetic vectors for the S03 session, execution-lease, invocation, and reader-lifecycle contract, plus descriptions of candidate real-database probes.

## Offline vectors

Each offline_scenarios entry describes client-side state-machine expectations. The shapes are:

- Ordered interaction cases: id, category, setup, steps, and expected assertions.
- Identity mismatch case: current_operation, stale_abort_vectors, and expect_for_each_vector.
- Counter exhaustion case: max_value_decimal, overflow_vectors, and per-counter expectations.
- Wire-fault matrix: wire_entry_expectations with per-entry final states.
- Nested LOB case: root_identity, child_invocations, and expect_for_each_child.

These are synthetic inputs and desired contract outcomes; they do not report that an implementation passed. Fast-failure assertions require an exception assignable to InvalidOperationException, no wait for the current owner, and no wire activity. Values such as a synthetic Read result or chunk_consumed are model inputs, not database responses.

The identity tuple is session ID, lease generation, execution ID, and invocation ID. Stale-abort vectors vary one component at a time. Overflow values are decimal strings so JSON consumers cannot round the 64-bit value. The current T05 implementation reports counter exhaustion as InvalidOperationException. Overflow must reject without wrap and must leave any affected physical session unusable. An Open or pool-validation failure ends Closed; a failure in an already-open session ends Broken.

The wire matrix includes abstract operation buckets and the current source call-site inventory supplied for T05: TCP connect, the legacy and MSG send/receive paths, timeout setup, and the currently unreferenced poll/receive health probe. Map each active call site to its owning session and add any further entries found. The separate DBAliveCheckThread TCP probe does not share the session transport. This fixture contains no protocol packets, payload bytes, server replies, or claimed wire traces.

The LOB vector models a reader-to-stream-to-chunk child invocation. It expects child calls to retain the root session, generation, and execution identity with a distinct invocation ID, without reacquiring the root lease.

## Real-database probe descriptions

Entries in real_database_probes are procedure descriptions only. Each is marked planned_not_run; no observed results are recorded. When run, use the repository test wrapper and designated test identity WDM_PROVIDER_TEST. Do not copy local connection settings into this fixture. If a probe needs stored LOB data, create and clean only uniquely named objects owned by that test in the dedicated test schema.

A real database can verify provider-visible behavior for these schedules. It cannot establish internal race behavior such as stale identity matching, checked overflow, zero-byte fast failure, or exception handling at every wire entry; those require offline or internal fault-injection checks.

The contract source is docs/implementation/v1/specs/03-session-and-ownership.md. Preparing these files did not open a connection or run a test.

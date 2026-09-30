# T10/T11 transaction boundary scenarios

`scenarios.json` separates synthetic `cases` from `real_database_probes`. Synthetic entries use `case_id`, `contract`, `execution_status: not_run`, `input`, and `expected`. Probe entries are procedures only and remain `planned_not_run`. See [S07](../../../docs/implementation/v1/specs/07-transactions-and-savepoints.md) and [T10-design](../../../docs/implementation/T10-design.md).

## Evidence and model boundaries

All transaction states, send-attempt markers, acknowledgements, savepoint mappings, and isolation observations are logical test-harness values. The fixture contains no DM packets, opcodes, server traces, real transaction outcomes, or database rows. A `validated_success` acknowledgement is a synthetic state input, not a captured response.

Server support for `ReadUncommitted`, `Serializable`, savepoints, savepoint Release, and DDL transaction boundaries is not frozen here. The tests must gate capability claims on an identified server profile. Where a result depends on server semantics, the fixture leaves observation slots blank and asks the harness to record them.

## Coverage

- TX-01/02/03: Begin only becomes Active after confirmation; nested and mismatched transactions reject; pre-send failures remain Active; post-attempt missing/invalid acknowledgement becomes OutcomeUnknown, breaks the captured connection, and never replays. Confirmed Commit/Rollback outcomes survive late cancellation and repeated Dispose.
- TX-01/06: Active Dispose uses a finite independent cleanup budget; confirmed Rollback, unknown cleanup, reader-close timeout, and business-exception preservation are separate cases.
- TX-05: capability gating, Unicode and hostile user names, NUL/length preflight, duplicate-name replacement, rollback invalidation, logical Release when server Release syntax is unverified, and a test-supplied total-creation cap.
- TX-06/T11: ReadCommitted, ReadUncommitted, and Serializable use two independent connections with named barriers. No fixed sleeps or read outcomes are prefilled. A level remains unclaimed until its real profile litmus passes.
- TX-07/08: DDL outcome remains unknown without a reliable server signal; ambient default does not enlist and explicit enlist is rejected.

The savepoint name limit and total-creation cap in synthetic inputs are harness parameters, not protocol/server limits. Generated internal names are logical mapping values; no SQL syntax is embedded in the fixture. Release cases do not assume a server `RELEASE` command.

## Real probe procedures

Real procedures require `WDM_PROVIDER_TEST` through `scripts/with-dameng-test.sh`, unique test-owned objects, independent final-state verification, and cleanup of only those objects. T11 schedules synchronize with barriers, not elapsed-time thresholds. No build, database connection, or transaction probe was run while preparing this fixture.

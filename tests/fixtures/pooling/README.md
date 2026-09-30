# T15/T16 reset and pooling scenarios

`scenarios.json` separates 33 synthetic contract cases from planned real-database procedures. Each synthetic case is marked `not_run`; each procedure is marked `planned_not_run`. The vectors describe state transitions, virtual time, and barrier order. They contain no protocol packets, guessed reset SQL/opcodes, credentials, pool-key secrets, certificates, or actual server outcomes.

## Reset policy

The default is `VerifiedResetOrDiscard`. Rollback, `SELECT 1`, or a successful health check alone do not prove a session returned to baseline. Reuse requires evidence for every state dimension the profile claims to restore: transaction, autocommit, isolation, schema, role/grants, timezone/NLS/language, temporary objects, session variables/locks, open cursors/LOB locators, prepared statements, and applicable package/procedure state. Unknown raw SQL or any unverified dimension requires discard.

Reset capability, server syntax, permissions, and supported isolation levels are deliberately profile inputs or `not_frozen`; this fixture does not assert that a DM reset opcode or full-reset mechanism exists. A synthetic savepoint/identity limit is a test harness setting, not a server maximum.

## Pool and DataSource coverage

- POL-01/02: Min/Max capacity includes creating/resetting slots, queued waiter cancel/timeout, grant races, connect/init/cleanup failures, and exactly-once permit release.
- POL-03: complete immutable pool identity, case-sensitive password/schema dimensions, documented alias normalization, hash collision resistance, credential/certificate separation, and redacted diagnostics.
- POL-04/05/10: unknown dirty sessions never enter idle; reset failure discards; actual reuse/discard and validation latency remain measurements, not fixture claims.
- POL-06/07: lease generation protects new checkout from old cancellation; ClearPool epochs retire idle and borrowed sessions safely; DataSource disposal cancels waiters and does not wait forever for borrowed connections.
- POL-08/09: registry churn remains bounded; CreateCommand owns its connection through reader close and releases once on success or failure.
- POL-10: idle lifetime and maintenance use virtual time and a controlled scheduler rather than one thread per connection.

The real procedures use `WDM_PROVIDER_TEST` through `scripts/with-dameng-test.sh`, uniquely owned test objects, independent final-state checks, and cleanup limited to objects created by the probe. No build, database connection, pool run, or reset probe was performed while preparing these files.

# T08 command and result scenarios

`scenarios.json` separates synthetic `cases` from `real_database_probes`. Cases use the shared fixture shape `case_id`, `contract`, `execution_status`, `input`, and `expected`; every synthetic case is `not_run`. The logical result descriptors are supplied by a harness to test the public command/reader contract in [S05](../../../docs/implementation/v1/specs/05-command-reader-and-ef-contract.md). They are not real DB output or protocol messages.

Real probes are procedures only and are marked `planned_not_run`. When run, use `WDM_PROVIDER_TEST` through `scripts/with-dameng-test.sh`, create unique objects owned by the probe, check final state independently, and clean only those objects. This fixture contains no connection string, credentials, certificates, packet bytes, opcode assumptions, or fabricated server responses.

## Result sequence model

Each `input.results` array is an ordered logical sequence:

- `rowset` has column metadata and synthetic rows. An empty `rows` array is still a rowset; a non-DML rowset uses `records_affected: -1`.
- `update_count` carries an integer `records_affected`; `null` means unknown and must not become zero.
- `error` carries a synthetic `sql_code` marked as fixture-only; the case checks that later errors are not suppressed.

`input.operations` lists the client-facing operations used by the scenario, such as `ExecuteReader`, `Read`, `NextResult`, `ExecuteScalar`, `ExecuteNonQuery`, or `Close`. Result ordering here describes the test harness contract only; it does not claim a Dameng statement produces that sequence.

## Coverage and boundaries

- CMD-01/02: UPDATE and DELETE 0/1 affected counts; DML plus `SQL%ROWCOUNT`; identity/sequence keys and multiple generated columns, with no inserted probe between DML and readback.
- CMD-03/04: two rowsets, an empty first rowset, update count including unknown, `ExecuteScalar` no-row `null` versus SQL NULL `DBNull.Value`, and errors after earlier results or counts.
- CMD-04: strings, comments, and quoted identifiers containing semicolons and `:p`, `@p`, and `?` do not create binds or statement splits. Parameter values are never interpolated into SQL text.
- CMD-05/06: combined `SingleRow|CloseConnection` and `SequentialAccess|CloseConnection` flags; `SchemaOnly|KeyInfo` fails before wire activity when the metadata path is unverified; early reader close never sends an unstarted side-effect operation.
- CMD-07: repeated Prepare/transaction/execution preserves CommandText and the active command plan’s parameter snapshot; mutable byte-array input is copied or isolated from concurrent caller mutation.
- The `EFCoreNextResult` switch and `/*EFCOREROWCOUNT*/` occurrence count are separate characterization inputs. The fixture does not invent their historical interaction or generalize support to arbitrary DMSQL.

`SequentialAccess` scenarios do not claim full streaming LOB behavior; that remains S10. `SchemaOnly|KeyInfo` is represented as pre-wire `NotSupportedException` when the required describe path is not verified. Real multi-rowset probes must first identify an operation known to be supported on the target server.

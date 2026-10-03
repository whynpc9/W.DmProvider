# T19 bounded latency diagnostic preparation

Source-only preparation. Independent Low may build/run once after root freeze and
after separately authorized exact Trim cleanup is freshly verified. This does not
change the final candidate, its source manifest, public 670-file snapshot, product
hooks, downstream original generator, server settings, or socket options.
`diagnostic_only=true`, `accepted_final=false`: 48 measured inserts cannot replace
the original 76-case/900-second functional gate or its original 1266 seed inserts.

Exact package is `0.1.0-r3.20261002223702`, SHA256
`92f154162af3e34f4f67248e07ef657a1433efb7f7f6b5a054a7e20fb726e3fc`;
DLL SHA256 `9970339dd70cc8bdf5e752f9a0bf0b74ccac4ded00ba6fa14e94e22ee22cdd0c`,
MVID `0c33d48b-4d42-46e1-99db-b53fe2f170f1`, source-manifest SHA256
`c904079e7fa69fb656c57486f22c88d742e21e7e0f0e39fb0b5217da69d38b85`.
These are guards, not a new source/package acceptance. The consumer requires exact
PackageReference, actual host-local loaded DLL, deps library of package type,
matching frozen feed package and isolated cache nupkg. No driver ProjectReference
or fallback exists. Build in fresh isolated outputs/cache with writable
DOTNET_CLI_HOME, first-time/telemetry disabled and single-node/noReuse/noSharedCompilation/
disable-build-servers. Pass `LatencyPackageSource` as the frozen final-v5 feed,
set NUGET_PACKAGES to that fresh build cache, and run the resulting consumer with
one argument: an existing empty private output directory (0700). Low enforces an
external 300-second process cap and records command/exit code without credentials.
Only the ordinary shared TEST wrapper may supply the connection variable.

The tool verifies actual USER/current schema `WDM_PROVIDER_TEST`, server build
`8.1.5.60`, and fresh zero counts in USER_TABLES/USER_VIEWS/USER_SEQUENCES before any
DDL. This explicitly prevents running over the stopped Trim table. It verifies
each exact unique name absent and durably writes a create-new 0600 ownership ledger
before CREATE. Names occur only in those private immutable ledgers; neither stdout
nor the numeric report contains SQL, values, endpoints, object names or dumped
objects. Two unique `EF10_AGF_` tables use the original seven-column DDL and primary
key shape. Each mode reuses one connection, each row creates/disposes a new command
with the original six explicit DbType parameters, and each affected count must be1.
Sync mode uses synchronous row execution/disposal; async mode awaits both. Identity
and precondition/cleanup queries are explicit async control work, excluded from row
timings. Pooling=true preserves the original shared-functional T19ConnectionSettings
configuration (with the existing default capacity and discard-on-return behavior);
StmtPooling/PreparePooling remain false. No locks, stored procedures, admin/DEV use, retries or maintenance
are added. Socket.NoDelay is read as one bool from the actual raw channel, never set.
The pinned path is DmConnection.m_ConnInst → DmConnInstance.m_Csi (B) →
B.__t02_field_04000AC1 (D, the value returned by B.A()) → D.transport →
DmTransport.channel → SocketByteChannel.Socket. Missing fields/values or unexpected
B/D types produce fixed safe guard codes rather than null dereferences.

Payload provenance is the pinned EF `DamengStringAggregateFunctionalTests.AggregateStore`
and `Shared/QueryTranslationCases` at commit
`113014cc74dd1f751ef97226a78d2ec855b32c8c`. Original rows421/422 (group99,
ordinals198/199, 200 characters) are two warmups. Original seed rows1–23 and223
are the24 measured rows per mode: Chinese values, NULL, empty, spaces, nullable
ordering keys, one short x, one 200-character value. IDs, group, ordinal and sort
keys stay those original rows; no new generator or whole-seed reconstruction.

Four existing wire hook fields must all be null; all are restored in finally,
including partial installation failure. Hooks perform only bounded local numeric
observation: exchange sequence, opcode when available, Stopwatch timestamps, mode,
local sample index and warmup bool. No parameter ID or OperationIdentity is stored, no SQL/frame/source/values
are read, no hook waits or performs network I/O. The4096 exchange bound is fixed;
overflow fails, missing/duplicate/unmatched hooks are counted and visible.
`ExecuteTicks` and `DisposeTicks` are per-row elapsed Stopwatch ticks. Each exchange
has Entered/FrameSent/SendCompleted/ResponseReady timestamps, with absent hooks
nullable. The sync MSG source does not call AfterFrameSent (only async paths do),
so sync opcode/FrameSent can remain unknown: this is an observation blind spot,
not evidence of a missing network frame. These hooks are not Socket.Send counts,
first-byte instrumentation, or packet captures. SendCompleted→ResponseReady includes
receiving the complete response, scheduling, frame validation and hook overhead;
it is not pure RTT and cannot identify NoDelay as a root cause.

Connection and command budgets are20 seconds; work is200 seconds, cleanup80 seconds,
whole tool300 seconds. Any business error stops new work; original failure is safely
classified with fixed phase/type/number only. Cleanup uses a fresh actual TEST
connection, inspects only attempted exact owned names, drops present owned tables,
then independently opens another fresh connection to verify absence. A failed or
timed-out cleanup remains failed and retains ledgers for exact recovery; no retry
or arbitrary prefix cleanup is allowed. The private report records create ACK and
fresh final absence per mode, hook missing counts, row durations, and safe failures.
The current preparation has not been compiled or executed.

# Separate-process R1/current synchronous baseline diagnostic preparation

Ignored preparation only. No public source, candidate, historical evidence or
server settings are changed. It is not compiled/executed by High. Independent Low
builds both consumers outside the DB window, then root authorizes at most one
300-second bounded diagnostic using ordinary shared TEST. Original76-case/900-second
functional acceptance remains failed/pending;24 measured inserts per block cannot
replace it or the1266 original seed inserts. diagnostic_only=true and
accepted_final=false in every output, including exit0.

One identical Program.cs is independently compiled into two consumers. ProbeFlavor
must be R1 or current; R1 adds the conditional R1 symbol. PackageReference is exact:

* R1:0.1.0-r1.20260930064454; package SHA256
  a5528d682283c343511a5343275c1420e6ff1b2867afe9e5384fbb8f0f8136c8;
  DLL SHA256d3eda7c4c0156f89495b9c6540c39188d44ca4e2c4cae572345da7a1398692ed;
  MVID205ef4d3-9b6e-42b8-8838-9a659074158c. Immutable feed:
  .local/t12/ef/20260930-r1-cli-final-v6/candidate-feed/.
* Current v5:0.1.0-r3.20261002223702; package SHA256
  92f154162af3e34f4f67248e07ef657a1433efb7f7f6b5a054a7e20fb726e3fc;
  DLL SHA2569970339dd70cc8bdf5e752f9a0bf0b74ccac4ded00ba6fa14e94e22ee22cdd0c;
  MVID0c33d48b-4d42-46e1-99db-b53fe2f170f1. Its immutable source-manifest SHA is
  c904079e7fa69fb656c57486f22c88d742e21e7e0f0e39fb0b5217da69d38b85;
  that report field is explicitly current_source_manifest_sha256, never an R1
  source-identity claim.

Build each source copy with its own output/bin/obj/feed/cache/CLI home and
BaselinePackageSource metadata. Actual host-local DLL, exact version/MVID/hash,
deps package identity, feed hash and private NUGET_PACKAGES nupkg hash must match
the compiled flavor. No AssemblyLoadContext tricks, reflection loading a second
driver, ProjectReference or official fallback. Four serial fresh processes are
A1=R1,B1=current,B2=current,A2=R1. No driver assemblies are mixed in one process.

`run.py --consumers <materialization.json> --output <new-private-run-root>
--dotnet <absolute-dotnet-host>` consumes Low's separately built artifacts; it
does not build. Input schema_version=1 has consumers.R1 and consumers.current,
each containing absolute consumer_dll, consumer_dll_sha256, working_directory,
cache and feed. Low keeps every consumer directory/cache/feed independent.
Optional source_files entries describe copied Program.cs and
BaselineLatencyProbe.csproj with path/sha256 matching this frozen preparation.
Runner child output directories are new/private, with no credential command args,
environment dump or raw log forwarding. Parent advance requires actual child
process exit0 plus cleanup_verified=true and whole_empty_after=true with strict
row/ownership/observation checks; an external status boolean alone cannot advance.

Consumer args are private new empty0700 output directory, exact block label,
remaining work milliseconds (1..200000), remaining outer milliseconds (1..300000).
The runner owns the global monotonic200-second cumulative work budget and an
80-second cleanup reserve within300 seconds, stops at first failure or unverified
cleanup and never retries. Consumer connect/command calls remain20 seconds.
Each consumer's outer timer is limited by the passed remaining whole-run deadline;
its work timer is limited by remaining global work. Cleanup is separately80 seconds,
also bounded by that remaining outer deadline. Credentials only inherit through
the ordinary TEST wrapper environment, never command args or metadata.

Both use common legacy ConnPooling=false, guarded false also on current; statement
and prepare pooling false. This deliberately common configuration differs from
the original shared-functional Pooling=true, so results are diagnostic only.
Schema/TEST identity, explicit PlaintextAllowed and OFF trace remain fixed. Actual
server build8.1.5.60 is mandatory. Profile output reads only the source-validated
DmConnInstance.ConnProperty.ServerEncoding and msgVersion. The encoding canonical
name must be utf-8 or gb18030; only that whitelist name/codepage and bounded numeric
message version are emitted. A negotiated difference can explain different
behavior but cannot establish a causal mechanism.

Every process verifies fresh entire USER_TABLES/USER_VIEWS/USER_SEQUENCES inventory
empty, including no previous Trim object, before ledger/DDL. Its one exact unique
AGF name is checked absent and recorded in a create-new0600 immutable ledger before
CREATE. One connection uses synchronous Open and original seven-column CREATE,
then two original warm rows421/422 and24 original measured rows1..23 plus223.
Every row creates a new six-parameter command with original explicit DbType and
payload and runs synchronous ExecuteNonQuery and Dispose; affected count must be1.
The original NULL/empty/spaces/order keys and200-character values are unchanged.
LOB_VALUE's NCLOB declaration is not a LOB upload. Async identity/control/cleanup
operations occur outside measured row windows and Row=0.

NoDelay is strictly read-only, through m_ConnInst→m_Csi(B)→
__t02_field_04000AC1(D)→transport→channel→Socket. Missing paths have fixed guards,
never null dereferences. Both source trees contain the same three common hooks:
AfterExchangeEntered,AfterSendBeforeReceive,BeforeDecode. All must initially be
null and are restored in finally, even if partial installation failed. Hook records
are only local sequence, block/sample index/warmup bool and monotonic numeric
Entered/SendCompleted/ResponseReady timestamps/spans. No OperationIdentity, SQL,
input values, frame bytes or guessed opcode is recorded.4096 is the fixed cap;
missing/unmatched/overflow or nonmonotonic spans fail the diagnostic. Actual per-row
exchange count is reported;5 is only a reference, not an asserted equality. A
different count is a diagnostic difference and must not be collapsed or guessed
into matching protocol operations.

Each process finally inspects only its attempted exact owned table, using a fresh
verified TEST connection, drops it if present, then independently opens another
fresh connection to prove both exact absence and whole inventory empty. Unknown
CREATE ACK retains the original failure and attempt ledger. Cleanup failure stops
subsequent blocks; no prefix or unrelated-object cleanup, admin/DEV credentials,
locks/SPs/server settings or retries. Only immutable private ledgers contain exact
owned names. Numeric reports and fixed safe phase/type/number do not contain
endpoints, credentials, values, SQL or exception messages. Raw build/test output
stays private and no environment dump is performed.

Interpretation is fixed in advance and belongs to root/Astra readback, not this
tool's statistical code: both A/B around0.39 seconds means the current environment
also slows the accepted baseline; it does not grant final acceptance. A stable
current-only regression would support a later specific fix proposal. Time-order
drift is inconclusive. Send→ResponseReady includes receiving/scheduling/validation,
not pure RTT or first-byte latency. Neither NoDelay nor a particular wire change
is inferred as a cause from these records alone.

Both pinned sources already expose CreatedTcpSockets/DisposedTcpSockets atomic
counters. Before DB work and after all process-owned connections finish disposal,
the tool reads those exact two properties and records nonnegative created/disposed
deltas. runtime_zero is explicitly scoped to own_process_tcp_socket_delta and
requires the two deltas equal. It is a supplemental physical-close check, not an
R3 pool/resource snapshot or a claim of baseline counters that do not exist.

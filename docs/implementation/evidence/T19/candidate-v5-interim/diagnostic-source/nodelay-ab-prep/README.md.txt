# T19 serial NoDelay ABBA diagnostic preparation

Only ignored source preparation. Root review/freeze precedes one independent Low
build/run with an external300-second process cap. No product/source/archive/package
change and no runtime has been performed by this preparation. The original76-case
900-second shared functional gate and its1266 seed inserts remain pending: this
96-measured-row diagnostic cannot stand in for either. Every report states
diagnostic_only=true and accepted_final=false; exit0 only means the bounded
diagnostic completed its strict guards and exact cleanup, not final acceptance.

The exact final-v5 package is0.1.0-r3.20261002223702:
package SHA25692f154162af3e34f4f67248e07ef657a1433efb7f7f6b5a054a7e20fb726e3fc,
actual DLL SHA2569970339dd70cc8bdf5e752f9a0bf0b74ccac4ded00ba6fa14e94e22ee22cdd0c,
MVID0c33d48b-4d42-46e1-99db-b53fe2f170f1,
immutable source manifest SHA256c904079e7fa69fb656c57486f22c88d742e21e7e0f0e39fb0b5217da69d38b85.
There is only an exact PackageReference. Loaded local DLL, informational version,
MVID, frozen feed nupkg, isolated cache nupkg, and host deps package identity are
mandatory guards. Use fresh isolated cache/build outputs, NUGET_PACKAGES pointing
to that cache, writable DOTNET_CLI_HOME, first-time/telemetry disabled and the usual
single-node/noReuse/noSharedCompilation/disable-build-servers flags. Build with
NoDelayPackageSource equal to the immutable final-v5 feed. Low runs the built
consumer with one argument: an existing empty0700 private output directory, under
the ordinary shared TEST wrapper. Missing TEST or package guards fail before DDL.

The four blocks are strictly serial ASYNC: A1,B1,B2,A2. Each opens its own fresh
physical connection, verifies actual TEST/schema/build8.1.5.60, fully finishes the
identity command/reader, then accesses only its own raw Socket before DDL. Original
shared T19ConnectionSettings Pooling=true, Connect/Command20 seconds, StmtPooling=false,
PreparePooling=false and explicit PlaintextAllowed are preserved. Existing provider
discard-on-return ensures these physical connections end on disposal. All four
verify the fresh default NoDelay=false. A1/A2 never execute a setter. B1/B2 alone
set their own connection Socket.NoDelay=true and verify after=true. Disposal ends
that setting. No other sockets, route/OS settings, server settings, admin/DEV use,
product defaults or connection configuration are changed.

Exact safe path is DmConnection.m_ConnInst → DmConnInstance.m_Csi(B) →
B.__t02_field_04000AC1(D, B.A()'s return value) → D.transport →
DmTransport.channel → SocketByteChannel.Socket. Every missing field/value or wrong
B/D type returns fixed safe guard codes. No reflective method-overload selection
or object/endpoint dump is used. Only before/after bools and whether the B setter
ran appear in the report; Socket references never enter diagnostics.

Each block has a distinct unique owned AGF table, original seven-column DDL and
original six ordinary INSERT parameters. LOB_VALUE is declared NCLOB but is never
bound; this is not an NCLOB-upload test. Each row creates a new command and awaits
ExecuteNonQueryAsync and DisposeAsync; affected count must be1. Warmups are original
seed421/422 (group99 ordinals198/199), followed by measured original1–23 and223,
preserving all original values, NULL/empty/spaces/sort keys/200-character input.
Thus each block has2 warmups+24 measured, whole run8+96. The pinned payload source
is AggregateStore and QueryTranslationCases at EF commit
113014cc74dd1f751ef97226a78d2ec855b32c8c. No generator is altered or repeated.

Prewhole fresh USER_TABLES/USER_VIEWS/USER_SEQUENCES counts must be zero. Each exact
random name is verified absent, then a create-new0600 immutable ledger is durably
written before any CREATE. Only private ledgers contain owned names. Attempted
objects alone may be inspected/dropped in finally by a fresh verified TEST
connection, followed by independent fresh connection absence verification. Unknown
CREATE ACK keeps the attempt and original failure; it does not authorize cleanup
of any unrelated name. Work200 seconds, cleanup80 seconds, whole300 seconds;
20-second calls unchanged. No retries, prefix cleanup, locks or stored procedures.
Cleanup failure leaves a nonzero result and exact recovery ledgers.

The four existing wire hooks must all initially be null and are restored on every
exit, including partial installation. They only observe fixed block labels, local
sample indices/warmup flags, monotonically numbered exchanges/opcodes/Stopwatch
timestamps and bounded counters. No OperationIdentity, SQL, input values, raw
frames, locator tokens, exception messages or business objects are recorded. Hook
code never awaits or sends.4096 exchanges is the fixed storage cap. Each row,
including warmup, requires exactly one sequence3,5,13,44,4; measured rows therefore
must have five corresponding complete spans, with no extra/missing frames.
Duplicate/unmatched/missing/overflow and missing individual hook counts must be
zero, including final cleanup. Row0 is control/identity/DDL/connection cleanup and
excluded from measured-row analysis. Per-row Execute/Dispose ticks and per-exchange
timestamps/send→response-ready spans are numeric. Raw tick vectors remain available
for root/Astra readback; this tool does not implement statistical inference.

Hooks are not Socket.Send-call counts, first-byte timestamps or packet capture.
SendCompleted→ResponseReady includes receiving the complete response, scheduling,
frame validation and hook overhead, so it is not pure RTT. Interpretation is fixed
before execution: only BOTH B blocks showing materially stable lower corresponding
five-opcode response spans than BOTH A blocks, with identical counts/results,
supports an impact of this socket option. That alone does not prove Nagle or a
server/network root cause and does not justify an automatic product patch.
Unstable or time-order-dependent results are inconclusive; if all blocks retain
approximately75ms response spans, this is no demonstrated fix. The tool does not
promote a chosen statistic/threshold into a pass or tune the experiment afterward.
Original failure phase/type/number and final exact-owned schema state remain safe
and explicit; public stdout is only a fixed diagnostic classification and counts.

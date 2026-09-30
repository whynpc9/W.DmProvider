# T07 transport security and native boundary

Date: 2026-09-29. Status: T07 independently verified within the recorded scope. This is a source and policy record; actual interoperability evidence and limitations are in [the T07 report](reports/T07.md).

## Evidence layers

The original net9 STARTUP decoder assigns `P_0.m()` to `ConnProperty.Encrypt` at `decompiled/net9.0/A/C.cs:620-628`. `decompiled/net9.0/A/b.cs:1707-1710` reads `m()` as a little-endian 32-bit value at header offset 20. The original `A/B.cs:137-158` takes mode 2 through `D.A(user, true)`, modes 1 and 4 through `D.A(user, false)`, and rejects 3. It has no mode 5 or 6 branch. In the original `A/D.cs:337-374`, the true branch detaches the `TcpClient.Client` after TLS authentication; its subsequent I/O path chooses the raw socket when that client is not connected (`A/D.cs:520-533,592-598`). That supports an AUTH_ONLY interpretation for mode 2, but does not prove every server version's wire transition. The false branch retains TLS stream I/O, consistent with full-stream encryption.

The same STARTUP response separately reads message-encryption flags at offsets 40 and 41 (`decompiled/net9.0/A/C.cs:648-674`, `A/b.cs:1732-1739`). Those flags and `COMM_ENCRYPT_NAME` are independent of TLS. The [official communication-encryption chapter](https://eco.dameng.com/document/dm/zh-cn/pm/communication-encryption.html) says the server selects the SSL mode through `ENABLE_ENCRYPT`: 0 no TLS; 1 mutual verification and encrypted data; 2 certificate authentication without encrypted data; 4 encrypted data without certificate verification; 5 encrypted data with client verification of the server; 6 TLCP. It also describes separate application-layer message encryption via `COMM_ENCRYPT_NAME`. Documentation of server configuration does not by itself prove that a particular response field has identical semantics for all deployed versions.

T06 real TEST login passed under `PlaintextAllowed` while the T04 guard rejected every nonzero `ConnProperty.Encrypt`; this implies that test server returned mode 0 for the observed connection. A separately owned local TLS mirror subsequently reported server configuration 2 as wire 2, configuration 4 as wire 4, and configuration 5 as wire 4. The configuration-5 case completed a validated TLS 1.2 session through the wire-4 branch, but it does not verify a wire-5 branch. Server-side mode restoration and final readback belong in the independent T07 validation record.

| Controlled server `ENABLE_ENCRYPT` | Observed STARTUP wire mode | W behavior and evidence boundary |
| --- | ---: | --- |
| 1 | 1 | Full TLS 1.2 with owned TEST identity; final matrix evidence recorded separately. |
| 2 | 2 | Rejected before LOGIN by both policies. |
| 4 | 4 | Strict TLS 1.2 succeeded; unknown CA rejected before LOGIN. |
| 5 | 4 | Strict TLS 1.2 succeeded through the **wire-4** branch; no wire-5 claim. |

These observations are limited to the controlled server build and its local mirror. The provider dispatches on the observed wire mode, not on an assumed copy of the server configuration value.

## Product decision

After the exact STARTUP response is decoded, and before LOGIN is encoded or sent, `DmHandshakeSecurityGuard.RequiresFullTls` applies the negotiated integer and the typed client policy. Mode 0 is accepted only with explicit `PlaintextAllowed`. Wire modes 1 and 4 enter a full TLS upgrade with strict client-side chain, time, and configured-target-name validation. Mode 4's server policy does not weaken this client's verification. Mode 2 is rejected by both policies because it does not protect the later business stream. Wire modes 3, 5, 6 and unknown values fail closed; wire 5 and TLCP require their own real interoperability evidence. A server configured for mode 5 may emit wire 4 in the observed build, which is handled as wire 4 with the same strict verification. TLS failures close the physical session before LOGIN. Message cipher and password-enhancement negotiation remain separately unsupported and cannot silently fall back to plaintext.

The current `DmTransportSecurity.RequireTls` default must not send LOGIN to a mode-0 server. An accepted TLS branch must keep the same TCP socket and one connect deadline from DNS through STARTUP, TLS authentication, LOGIN and schema initialization. The certificate target name comes from the immutable configured host, not a resolved IP or the original driver's hard-coded `DmProvider` string. The original `A/D.cs:337-374` accepted every certificate, disabled revocation checking, included TLS 1.1, and printed an authentication exception before returning; T07 does not reuse that path.

## Native boundary

Original native declarations are `dmcyt` in `decompiled/net9.0/Dm/SymmCipher2.cs:16-35`, `dmfldr` in `decompiled/net9.0/Dm/DmFldrDllCall.cs:41-103`, Windows `Kernel32.dll` loader calls in `decompiled/net9.0/Dm/ThirdPartCipherDLL.cs:63-100`, and `dmcalc.dll` in `decompiled/net9.0/Dm/Dmxdec.cs:38-39`. The third-party loader's original `Dispose` is empty (`ThirdPartCipherDLL.cs:102-104`). T07 leaves message cipher, FLDR, third-party cipher, and native decimal conversion unsupported; their old direct P/Invoke entry points now reject before platform library search. `DmNativeLibrary` is the sole preparatory explicit-absolute-path loader, using `NativeLibrary` and a SafeHandle with required-export checks and exactly-once release. Merely having that loader does not enable an unverified native capability or authorize redistributing native binaries.

The [official communication-encryption chapter](https://eco.dameng.com/document/dm/zh-cn/pm/communication-encryption.html) describes a broad default library search sequence for the official client. This product deliberately accepts only an explicitly provisioned trusted absolute path for any future native feature; it does not search the current directory, `DM_HOME`, a server-provided path, or the host library path for these disabled capabilities.

## Acceptance evidence boundary

Offline tests may check mode policy, pre-LOGIN gating, certificate validation/hostname/revocation behavior with controlled certificates, TLS failure and Close cleanup, and fake native load/export/release failures. They do not establish Dameng TLS wire compatibility. Real wire-mode 1/4 tests require a separately provisioned TLS server, identity confirmation, server-side `ENABLE_ENCRYPT` readback, valid and invalid certificate cases, one-socket evidence, and business I/O after LOGIN. Wire modes 2/3/5/6 and native cipher remain rejected until separately designed and verified.

# T06 transport and frame boundary scenarios

`scenarios.json` contains deterministic synthetic inputs for the S04 NET-01 through NET-04 contracts. Every case is marked `execution_status: not_run`; `expected` fields state desired client behavior and are not observed test results.

## Source-backed frame fields

The frame byte vectors use only the fields confirmed in the existing source:

- [`T02_0200008D.cs`](../../../src/W.DmProvider/Internal/Legacy/Dm/T02_0200008D.cs#L142) defines a 64-byte header, command at byte offset 4, body length at offset 6, checksum byte at offset 19, and heartbeat command 269.
- `T02_0200008D.cs` lines 203-208 writes command and body length after encoding; the written body length is buffer length minus 64. `T02_02000092.cs` lines 361-370 writes the integer fields little-endian.
- `T02_0200008D.cs` lines 298-335 selects body CRC32 only when `crcBody` is negotiated and command is not 200. Startup command 200 follows the header XOR checksum path. Lines 338-367 implement the XOR and CRC32 routines; the exact CRC32 lookup tables are at lines 385-622.
- Heartbeat command 269 is consumed as a complete frame by the S04 contract. Its vector uses a short synthetic body and `crc_body_negotiated: false` so the header XOR path is explicit.

Unmodeled header bytes are zero-filled only to make small parser inputs reproducible. These are parser-level byte vectors, not valid application messages or captured server frames. The fixture does not infer unverified header meanings or a server protocol maximum.

## Coverage

- NET-01: one-byte header reads, fragmented body reads, short send progress, zero-progress send, and two coalesced frames.
- NET-02: EOF in header/body, negative body length, checked total-length overflow, the 64 MiB product frame limit, long-decimal arithmetic overflow, and both negotiated body CRC32 and startup XOR mismatch paths.
- NET-03: heartbeat header and full-body consumption while retaining the original command deadline.
- NET-04: one deadline across DNS, TCP, TLS, and LOGIN stages.

The 64 MiB default is the product's total-frame protection policy from [S04](../../../docs/implementation/v1/specs/04-transport-protocol-and-security.md#L36), not a claim about the server protocol maximum. Over-limit vectors contain only a small header; they do not allocate 64 MiB or larger bodies. Decimal arithmetic values are strings so JSON consumers preserve their exact values.

The body CRC32 sample was generated from the exact `MSG.calcCRC32` lookup tables and loop in the cited source, over a 64-byte header (command 6, declared body length 8) plus body `A1B2C3D4`; the trailer uses the source's little-endian integer write. The error vector flips one bit in that computed trailer. This establishes a deterministic parser test input only; it does not establish that a peer accepts the synthetic message.

Fixture preparation does not open a socket or connect to a database.

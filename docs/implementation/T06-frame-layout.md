# T06 frame layout and decoder guards

Date: 2026-09-29. This records source verified fields used by T06. It does not claim a complete Dameng wire specification.

## Source verified common frame

| Field | Offset and size | Source evidence | Use |
| --- | --- | --- | --- |
| Header | 0–63, 64 bytes | `src/W.DmProvider/Internal/Legacy/Dm/T02_0200008D.cs:144`, `:199-209` | Read exactly 64 bytes before interpreting the body length. |
| Command | 4–5, little endian signed short | `Dm/T02_0200008D.cs:150`, `:205-209`; `A/T02_02000092.cs:610-617`, `:640-643` | Command 200 is STARTUP; 269 is heartbeat (`Dm/T02_0200008D.cs:140-142`). |
| Body length | 6–9, little endian signed int, excludes 64 byte header | `Dm/T02_0200008D.cs:152`, `:205-209`, `:381-388`; `A/T02_02000092.cs:620-627`, `:645-648` | Reject negative, checked overflow and total over 64 MiB before growing the receive buffer. |
| Compression marker | 18 | `Dm/T02_0200008D.cs:160`; STARTUP encoding in `A/T02_02000093.cs:40` | T06 does not implement decompression. Nonzero configured compression is rejected before handshake; received marked compressed frames fail before body allocation. |
| Header checksum | 19, one byte | `Dm/T02_0200008D.cs:162`, `:311-314`, `:333-340`; `A/T02_02000092.cs:630-633`, `:503-515` | XOR of header bytes 0–18 when body CRC is not negotiated or command is STARTUP. |
| Body CRC | last 4 bytes of body, little endian CRC32 | `Dm/T02_0200008D.cs:300-309`, `:317-331`; legacy `A/T02_02000090.cs:519-538` | Only when `crcBody` is negotiated and command is not STARTUP. The trailer is included in wire body length, validated, then excluded from decoder valid length. |

The 64 byte header has message specific fields after the common first 20 bytes. T06 does not define a packed universal struct because those fields vary by command and version. The product's 64 MiB cap is an allocation limit, not a claimed server protocol maximum.

## Receive and send boundaries

Both legacy SQL and `MSG<T>` now consume one exact header and its validated body. The reader validates the negotiated checksum, consumes a complete heartbeat frame including its body, then continues under the same `DmDeadline`. It does not reset the deadline for a later business frame. EOF during header or body is a truncated frame. Neither path reads 32640 bytes speculatively, so coalesced frames remain separate.

`DmFrameWriter.Validate` checks the final header body length against the actual encoded buffer length before `D.SendAll`. The legacy SQL CRC path updates body length and sends its 4 byte trailer. `MSG<T>.setCRC` and `checkCRC` retain the source algorithm and conditional STARTUP exception. A failed checksum or decoder exception occurs within the T05 wire exchange and marks that physical session Broken.

`b` uses its logical valid length for decode reads, rather than array capacity. The frame reader raises that length only after the exact header or body read completes. The nested decoder checks fixed and variable reads; metadata, rows, BDTA field arrays and values are checked against remaining frame bytes before allocation. A separate 64 MiB reference-array budget covers the outer row array and per-row cell-reference arrays. A 64 MiB decoded-value-byte budget accumulates across every BDTA package in one `FillRows` response, including padding absent from the wire. These budgets do not represent total result heap usage or array object overhead. Complex descriptor `Unpack` explicitly rejects the unsupported recursive type path; safe complex type support needs its own later implementation and acceptance gate (T24).

## Validation scope

Synthetic streams may exercise fragmentation, truncation, negative/overflow/oversize lengths, checksum failure, heartbeat, and consecutive frames using the source verified common header. They must not invent the message specific header fields, server replies, or TLS/cipher frames. Real provider behavior requires the separate TEST schema wrapper and distinct server side final state record.

# T17 streaming LOB scenarios

`scenarios.json` separates synthetic `cases` from `real_database_probes`. Every synthetic case is `not_run`; every real procedure is `planned_not_run`. The fixture contains generator parameters, lengths, hashes, and logical API operations, not binary payloads, DM packets, LOB locators, or captured server responses.

## Deterministic data generation

`byte_cycle_v1` defines `byte[i] = (seed + stride*i) mod 256` with seed 17 and stride 131. The expected SHA-256 is computed over exactly `length_bytes` in order, using incremental hash updates and bounded generator chunks. The listed boundary hashes cover lengths immediately below, at, and above the synthetic 4096-byte chunk boundary and its second multiple. The 1 GiB+ case stores no byte array; its digest is calculated incrementally by the test.

The CLOB vectors repeat `A雪🚀é` for UTF-8 and `达梦数据库` for GB18030. UTF-16 character units and encoded byte lengths are separate. Chunk sizes are synthetic harness parameters, not protocol limits. Splits deliberately cross multibyte sequences and the emoji surrogate pair; final decoder flush rejects incomplete tails rather than replacing or dropping them.

## Coverage

- LOB-01/02: lazy stream open, deterministic chunk hashes, non-seekable unknown-length input, incremental TextReader encoding, and memory bounds independent of total LOB size. The generator model simulates 1 GiB+ without preallocating the payload.
- LOB-03/06: UTF-8/GB18030 multibyte boundaries, surrogate pairs, strict invalid-tail handling, GetBytes byte offsets, GetChars CLR UTF-16-unit offsets, length queries, buffer bounds, zero-length reads, short reads, EOF, and unexpected truncation.
- LOB-04/05: one active field stream, two LOB columns, invalidation on NextResult/reader close/transaction end/session-generation change, forward-only seek rejection, and midstream cancellation without replay.
- LOB-07/08: materialized-value limit behavior is distinct from streaming; `GetString` remains materializing, so EF string consumers do not establish stream memory bounds.

## Real large-object probes

Before any large test object is created, the procedure requires a read-only capacity preflight in the existing test environment. It uses a unique object under `WDM_PROVIDER_TEST`, cleans only that object, and makes no tablespace changes. If the configured safe capacity is insufficient, leave the probe unrun and report `integration_pending`; do not assume hundreds of MiB are available.

The moderate EF 40KiB compatibility path is listed separately from large streamed-object tests. Preparing this fixture did not build, connect to a database, or generate a binary file.

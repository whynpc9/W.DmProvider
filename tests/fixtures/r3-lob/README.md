# T17 independent synthetic LOB vectors

This directory contains small fixed text vectors, protocol-neutral logical source schedules, one large-value descriptor, and synthetic out-row offset profiles. Nothing here was captured from a DM server. All cases are marked `not_run`; the large value is a descriptor only and no large payload file is generated.

## Sources and generation

`generate_vectors.py` uses only Python's standard library (`codecs`, `hashlib`, and `json`). Run `python3 tests/fixtures/r3-lob/generate_vectors.py` from the repository root to regenerate `vectors.json` and `schedules.json`. The metadata records the Python implementation/runtime and the names returned by `codecs.lookup`; in particular, GB18030 results describe that reference codec on that Python runtime only. They do not identify or imply a GB18030 mapping version on any server.

The fixed string is `A\u0000雪🚀e\u0301Z`. The JSON stores its encoded byte hex, UTF-16 code-unit sequence, rune count, code points, byte count, and SHA-256 for both reference encodings. It also lists every byte cut position and every UTF-16 code-unit cut position, marking cuts inside an encoded rune or surrogate pair. The combining mark is preserved as written; no normalization is performed. Strict round-trip checks and strict rejection of the included malformed byte samples are performed by the generator before it writes the files.

Isolated surrogate inputs are stored as UTF-16 code-unit labels, never as unpaired surrogate text in JSON. The malformed GB18030 tail is a truncated prefix of the supplementary-character byte sequence emitted by the recorded reference codec; it is not a claim about all GB18030 implementations.

## Schedule and large-value models

`schedules.json` covers empty, exact-full, final-short, unknown-length non-seekable, caller-owned, source-read-error, and cancellation paths. It also models stream invalidation on parent next-row, next-result, reader close, and session-generation changes. Event names are test-harness scheduling labels only. They are not DM wire messages, protocol captures, or claims about packet order.

`vectors.json` describes a 1 GiB + 1 byte BLOB using `byte_cycle_v1`, seed 17, and stride 131. The consumer can feed generated chunks directly to incremental SHA-256 using the declared 16 KiB chunk size. The payload is never preallocated or written to disk. A fake recorder must retain zero payload bytes and keep only aggregate counters and a single incremental hash state; those limits describe the synthetic harness, not measured product memory.

The small out-row profile uses separately numbered Unicode values and a unique marker in every row. Its range examples use UTF-16 code-unit offsets and carry the expected substring for each range. `out_row_profile_large` is the independent large control value for out-row profiling: it stores the complete deterministic value, exceeds 128 KiB in UTF-8 while remaining far below the 64 MiB materialization cap, and includes distributed unique markers with both UTF-16 and scalar offsets. It mixes ASCII, BMP characters, supplementary emoji, NUL, and unnormalized combining sequences. Both profiles are synthetic source values for distinguishing offset mistakes; they do not prove server-side out-row storage behavior.

## How tests should consume these files

The vectors provide independent input and expected values for offline tests. Map each case to the relevant `LOB-01` through `LOB-06` contract in `docs/implementation/v1/specs/10-streaming-lob.md`; additional profiling of chunking and bounded buffers is `Improvement` evidence. Keep results classified as synthetic/offline contract or improvement evidence. They are not an `OfficialCapture`, a real-database result, or NCLOB/server-charset evidence.

No raw authentication bytes, CRC values, server dumps, LOB locators, or protocol packets are present. Real server behavior must be established separately with the project-approved test environment and documented test identity.

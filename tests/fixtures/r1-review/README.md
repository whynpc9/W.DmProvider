# R1 review logical regression vectors

cases.json contains small synthetic inputs and desired client-contract outcomes for four R1 review defects. Every case is not_run; these vectors are not test results. No product code, build, database, or wire activity was used to prepare them.

## Covered behavior

- SQL block comments end at the first */; a nested-looking opener is ordinary comment content. The exact rollback escape with a visible ; COMMIT must be denied. Quoted identifiers stay quoted, markers after the first close are visible, and an unterminated comment is denied.
- GetInt32 rejects SQL NULL while preserving integer zero as zero.
- CLOB GetChars null-buffer length queries return the full CLR UTF-16 length. Partial and zero-length reads use UTF-16 code-unit offsets and preserve the decomposed combining sequence. Empty non-null text is distinct from SQL NULL.
- DbCommand.Connection = null clears the connection association after preparation; a later execution cannot use the stale statement owned by the prior connection.

## CLOB source and hashes

The BMP CLOB source is the exact logical sequence A中e plus U+0301 COMBINING ACUTE ACCENT plus 文. It has 5 UTF-16 code units. The source remains decomposed; the fixture does not normalize it.

The recorded UTF-8 hash is SHA-256 over the decoded text encoded as UTF-8. The GB18030 hash is SHA-256 over the same exact text encoded with the Python standard-library str.encode("gb18030"). Both hashes exclude a BOM and do not use replacement or normalization. The byte length and hashes describe generated character data only; the fixture contains no encoded wire payload.

The rocket vector is conditional and offline-only. It uses one UTF-16 surrogate pair to pin CLR slicing semantics, but does not assert that GB18030 can encode that character. The empty-text case deliberately leaves the actual server expression unconfirmed and marks actual-server confirmation as planned. Do not infer whether a particular server expression stores empty text or SQL NULL from this fixture.

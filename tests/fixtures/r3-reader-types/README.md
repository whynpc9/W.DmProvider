# Synthetic reader type vectors

This directory contains independent fixture inputs and literal expected values for ROWID rendering and text `GetChars` ranges. All vectors are synthetic and `not_run`; they are not server captures, live database evidence, or real user data. Enum labels in JSON are test-only labels. The numeric `ctype` fields are dispatch inputs and make no claim about server enum names.

## Source basis

`generate_vectors.py` is a pure Python standard-library generator. It reimplements the relevant `DmRowId` shifts and field packing in Python and checks the result against fixed literal ROWID renderings. It does not load or call the product. The local source basis is:

- `DmRowId.valueOf(byte[])` accepts the 8- and 12-byte shapes. `toString()` formats an 8-byte value through `DmConvertion.EightByteToLong` as a decimal `Int64`; 12-byte values use the local 18-character base64-like alphabet and bit shifts. The chosen 12-byte value has its first six bytes zero, satisfying the local parser prefix shape and keeping the decoded EPNO/HPNO fields at zero. Its last six bytes deliberately include invalid UTF-8. This establishes a valid local conversion shape, not server-side rowid-range validity.
- `DmGetValue.GetString` decodes CType 0, 1, 2, and 54 bytes with the configured `ServerEncoding`; CType 28 renders a `DmRowId`. `DmDataReader.do_GetChars` uses UTF-16 code-unit offsets. The fixture provides expected CType 0/54 text ranges for the reader type-guard work; the cases are still marked `not_run`.
- `DmDataReader.do_GetBytes` currently gates accepted binary types and rejects CType 0, 1, and 2. `getbytes_rejection_controls` records `InvalidCastException` as the compatibility expectation so adding text `GetChars` support does not broaden binary support.

The corresponding conversion and reader rules are described in `docs/implementation/v1/specs/06-parameters-and-types.md` and `docs/implementation/v1/specs/10-streaming-lob.md`. They keep encoded byte length separate from UTF-16 length and define `GetChars` offsets in CLR `char` units.

## Fixed values

The mixed text value is `A雪🚀e\u0301\u0000Z`: `U+0041 U+96EA U+1F680 U+0065 U+0301 U+0000 U+005A`, seven Unicode scalar values and eight UTF-16 code units (`0041 96EA D83D DE80 0065 0301 0000 005A`). Its literal reference bytes are:

- UTF-8: `41E99BAAF09F9A8065CC81005A` (13 bytes)
- Python GB18030 reference codec: `41D1A995308B34658130BC37005A` (14 bytes)

The JSON repeats these vectors for synthetic CType 0 and CType 54 selectors. Each carries the complete UTF-16 code-unit array, full length query expectation, and partial ranges including an offset inside the emoji surrogate pair and a short final range. The generator checks the fixed byte literals with Python strict codecs. Its metadata records the Python runtime and codec lookup names; those results apply only to that reference runtime and do not identify server mapping versions or NCLOB behavior.

The invalid-UTF-8 ROWID byte vectors and local expected renderings are:

| Shape | Raw bytes | Expected rendering | UTF-16 units |
|---|---|---|---:|
| 8-byte legacy | `FFC080F59F988001` | `108254203842969855` | 18 |
| 12-byte current | `000000000000FFC080F59F98` | `AAAAAAAAAA/8CA9Z+Y` | 18 |

Both raw values fail strict UTF-8 decoding. The tests consume the rendered string and partial UTF-16 ranges, never decode the binary ROWID through `ServerEncoding`.

## Regeneration

From the repository root, run:

```sh
python3 tests/fixtures/r3-reader-types/generate_vectors.py
```

The script uses only Python's standard library and writes `vectors.json`. It creates no database data, credentials, large artifacts, or network traffic.

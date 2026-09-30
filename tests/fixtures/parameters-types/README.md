# T09 parameter and type scenarios

`scenarios.json` separates synthetic `cases` from `real_database_probes`, following [S06](../../../docs/implementation/v1/specs/06-parameters-and-types.md). Every synthetic case has `case_id`, `contract`, `execution_status: not_run`, `input`, and `expected`. Each real probe is `planned_not_run`; it is a procedure only, not an observed result. The fixture contains no wire bytes or assumed server responses.

## Lossless values

`UInt64`, signed boundary ticks, decimal coefficients, and values outside `System.Decimal` are decimal strings. `decimal(38,20)` uses an exact coefficient string and scale integer; it does not pass through JSON numbers or `double`. Date/time values are ISO strings with all seven fractional digits and explicit offsets. The unpaired surrogate uses its UTF-16 code-unit hex string (`D800`) so the JSON file itself remains valid UTF-8.

The UTF-8 vector includes Chinese characters, a supplementary-plane emoji, and a combining sequence. It records UTF-16 code-unit count separately from encoded byte length. The GB18030 vector is a separate Chinese-only string; it does not assume that every character is representable in both encodings.

## Coverage

- TYP-01/02: enum and Flags underlying values, byte/ulong-backed enums, signed and unsigned limits, overflows, and non-integral numeric conversion rejection.
- TYP-03: coefficient/scale exactness for `decimal(38,20)`, tiny signed values, trailing zeros, values outside CLR decimal precision, and explicit scale overflow without rounding. Trailing-zero scale preservation is tested as local `DmDecimal` parse/format only; DECIMAL column readback scale remains a verified-profile contract.
- TYP-04/05: GUID-shaped CHAR/VARCHAR(36) remains text unless `GetGuid` is explicitly requested; Unicode lengths, strict encoder fallback, and no replacement with `?`.
- TYP-06/07: CLR TimeSpan tick construction separately from server interval range/scale support, DateOnly/TimeOnly, unspecified DateTime kind, DateTimeOffset offsets, typed NULL, untyped NULL, empty strings, and empty byte arrays.
- TYP-08/09: parameter marker matching/reuse, duplicate normalization, named/positional rejection, type-source precedence, independent type/Size Prepare invalidation, ResetDbType inference, sync/async parity, and explicit rejection of unsupported complex types.

Server temporal and decimal results are conditional on an explicit tested profile: representable range/scale must round-trip exactly, and unsupported range/precision must reject without rounding. CLR parsing/construction vectors are separate from this server-profile contract. The synthetic GetString expectations preserve invariant-parseable temporal access; they do not invent a Dameng INTERVAL wire/string encoding. `ARRAY`, `CLASS`, `GEOMETRY`, and `XDEC` remain explicit unsupported cases. The planned database probe covers the existing EF LOB/INTERVAL/timestamp paths but remains unrun. Planned database probes use only `WDM_PROVIDER_TEST` and uniquely owned test objects when run. Preparing these files did not build, connect to a database, or encode protocol values.

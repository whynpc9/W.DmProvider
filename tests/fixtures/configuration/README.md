# T04 configuration fixtures

`cases.json` contains compact synthetic input and expected assertions for the T04 configuration contract in [T04-design](../../../docs/implementation/T04-design.md) and [S02](../../../docs/implementation/v1/specs/02-api-and-configuration.md). These are design assertions for the test suite; preparing or checking this file does not run the parser, open a socket, or validate a database.

All endpoint values are reserved documentation names (`example.invalid` and IPv6 addresses in `2001:db8::/32`). The account is `TEST_SYNTH`; `CANARY_T04` is a fixed synthetic password marker used by redaction cases. Unicode and quoted-value cases use short synthetic variants. No fixture contains local connection information.

## Fixture shape

- `parse_success` contains a connection string and a partial `expected` object. Expected values are the public builder values: timeout fields retain the documented units, schema and password preserve their exact text, and `transport_security` is the enum name. The duplicate cases require equivalent values to be accepted.
- `parse_rejected` records the expected exception family (`ArgumentException` or `NotSupportedException`). If `canary` is present, the test should verify that exception text does not contain it. For the unknown-option case, also verify that the exception does not echo the input key; its value contains the canary.
- `unsupported_nondefault` lists recognized legacy options with non-default values that must be rejected explicitly.
- `timeout_typed.ticks` is a `TimeSpan` tick count (100 ns per tick). `expected_legacy_milliseconds` is the public millisecond value to compare, including for `CleanupTimeout`.

Coverage includes case-insensitive keys and documented aliases, equivalent and conflicting duplicate values, quoted semicolon/double-quote escaping, Unicode password and schema preservation, bracketed IPv6 and port conflict, invalid numeric values, `InitialCatalog` isolation, canary redaction, and non-default unsupported options. Connection pooling is an explicit valid capacity setting in R3; enlistment, HA, read/write routing, logging, statement pooling, and result-set cache remain negative cases.

`RequireTls` is recorded only as the default configuration policy. `PlaintextAllowed` is recorded only as a parseable explicit policy value. Neither case asserts TLS, asynchronous I/O, or a successful network connection.

# T02 restoration scenarios

These are deterministic synthetic inputs for the T02 official (O) and restored (R) comparison. Both runs consume the same five Unicode CRUD rows and the same generated LOB bytes. The values are test data only; no database is accessed while preparing this fixture.

`unicode_rows` uses positive, one-based IDs suitable for CRUD primary keys. Each value is non-empty and fewer than 128 UTF-8 bytes. `utf8_sha256` is SHA-256 over the exact UTF-8 encoding of `value`; do not trim or normalize it. The rows cover Chinese, emoji, SQL quotes and a semicolon, a decomposed combining character (`e` + U+0301), and leading/trailing spaces.

`lob` describes a 65,536-byte sequence without storing the byte array in this file. For each zero-based index `i` from 0 through 65,535, the byte is `(i * 31 + 7) % 256`. `sha256` is over the resulting bytes. The generator below materializes this small sequence in memory to calculate its digest; this fixture makes no claim about streaming behavior.

O/R observations characterize the official package and the restored baseline. A known defect observed in O is recorded as an observation, not treated as the correct behavior contract for W.

## Independent hash check

Run from the repository root with Python 3:

```sh
python3 - <<'PY'
import hashlib
import json
from pathlib import Path

fixture = json.loads(Path("tests/fixtures/restoration/scenarios.json").read_text(encoding="utf-8"))
assert fixture["schema_version"] == 1
assert len(fixture["unicode_rows"]) == 5
assert [row["id"] for row in fixture["unicode_rows"]] == [1, 2, 3, 4, 5]
for row in fixture["unicode_rows"]:
    value = row["value"].encode("utf-8")
    assert 0 < len(value) < 128
    assert hashlib.sha256(value).hexdigest() == row["utf8_sha256"]

lob = fixture["lob"]
data = bytes((index * 31 + 7) % 256 for index in range(lob["length"]))
assert lob["length"] == 65536
assert lob["algorithm"] == "(index * 31 + 7) % 256"
assert hashlib.sha256(data).hexdigest() == lob["sha256"]
print("verified: 5 Unicode rows and 65536 generated LOB bytes")
PY
```

## Decimal-string BCD restoration cases

`bcd-cases.json` is a focused synthetic behavior fixture for the restored `decStringToBcd` implementation shown in `decompiled/net9.0/Dm/DmSetValue.cs:1839`. It records this routine's byte packing for the T02 O/R comparison: digits map to their numeric nibbles, `.` maps to `A`, `+` to `B`, `-` to `C`, and every other character to `F`. The first character occupies the low nibble; the second occupies the high nibble. An odd final character gets a high `F`; the empty string yields zero bytes. `expected_hex` is lowercase hex for the output byte array.

These cases characterize this restoration point only. They are not W's correct numeric encoding specification.

Run this independent Python check from the repository root:

```sh
python3 - <<'PY'
import json
from pathlib import Path

fixture = json.loads(Path("tests/fixtures/restoration/bcd-cases.json").read_text(encoding="utf-8"))
assert fixture["schema_version"] == 1
assert len(fixture["cases"]) == 8

def nibble(char):
    if "0" <= char <= "9":
        return ord(char) - ord("0")
    return {".": 10, "+": 11, "-": 12}.get(char, 15)

def encode(value):
    data = bytearray((len(value) + 1) // 2)
    for index, char in enumerate(value):
        slot = index // 2
        value_nibble = nibble(char)
        if index % 2:
            data[slot] |= value_nibble << 4
        else:
            data[slot] |= value_nibble
    if len(value) % 2:
        data[-1] |= 0xF0
    return data.hex()

for case in fixture["cases"]:
    assert encode(case["value"]) == case["expected_hex"], case

assert encode("0") == "f0"
assert encode("12") == "21"
assert encode(".+") == "ba"
print("verified: 8 BCD cases; manual examples 0=>f0, 12=>21, .+=>ba")
PY
```

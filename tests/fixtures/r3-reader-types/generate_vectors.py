#!/usr/bin/env python3
"""Generate independent reader type vectors from literal inputs and Python codecs."""

from __future__ import annotations

import codecs
import hashlib
import json
import platform
from pathlib import Path


ROOT = Path(__file__).resolve().parent
ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"
TEXT = "A雪🚀e\u0301\x00Z"
TEXT_BYTES_HEX = {
    "UTF-8": "41E99BAAF09F9A8065CC81005A",
    "GB18030": "41D1A995308B34658130BC37005A",
}
ROWID_INPUTS = [
    {
        "fixture_label": "synthetic_rowid_8_byte_invalid_utf8_v1",
        "raw_hex": "FFC080F59F988001",
        "rendered_literal": "108254203842969855",
        "expected_utf8_failure_offset": 0,
        "representation": "8-byte little-endian legacy rowid interpreted as signed Int64 then rendered as invariant decimal digits",
    },
    {
        "fixture_label": "synthetic_rowid_12_byte_invalid_utf8_v1",
        "raw_hex": "000000000000FFC080F59F98",
        "rendered_literal": "AAAAAAAAAA/8CA9Z+Y",
        "expected_utf8_failure_offset": 6,
        "representation": "12-byte rowid rendered by the legacy 18-character alphabet/bit-field mapping",
    },
]
TEXT_RANGES = [(0, 4), (2, 2), (3, 4), (5, 5), (8, 3)]
ROWID_RANGES = [(0, 4), (3, 5), (15, 5)]


def utf16_units(text: str) -> list[int]:
    data = text.encode("utf-16-le", errors="strict")
    return [data[index] | (data[index + 1] << 8) for index in range(0, len(data), 2)]


def units_hex(units: list[int]) -> list[str]:
    return [f"{unit:04X}" for unit in units]


def render_8_byte_rowid(raw: bytes) -> str:
    if len(raw) != 8:
        raise ValueError("The legacy rowid vector must contain 8 bytes")
    unsigned_value = int.from_bytes(raw, byteorder="little", signed=False)
    signed_value = unsigned_value if unsigned_value < (1 << 63) else unsigned_value - (1 << 64)
    return str(signed_value)


def render_12_byte_rowid(raw: bytes) -> str:
    if len(raw) != 12:
        raise ValueError("The current rowid vector must contain 12 bytes")
    if raw[0] & 0xC0:
        raise ValueError("The 12-byte vector violates the legacy 18-character parse prefix constraint")

    output = [
        ALPHABET[0],
        ALPHABET[(raw[0] & 0xF0) >> 4],
        ALPHABET[((raw[0] & 0x0F) << 2) | ((raw[1] & 0xC0) >> 6)],
        ALPHABET[raw[1] & 0x3F],
        ALPHABET[(raw[2] & 0xC0) >> 6],
        ALPHABET[raw[2] & 0x3F],
    ]
    for offset in (3, 6, 9):
        first, second, third = raw[offset : offset + 3]
        output.extend(
            (
                ALPHABET[(first & 0xFC) >> 2],
                ALPHABET[((first & 0x03) << 4) | ((second & 0xF0) >> 4)],
                ALPHABET[((second & 0x0F) << 2) | ((third & 0xC0) >> 6)],
                ALPHABET[third & 0x3F],
            )
        )
    return "".join(output)


def parse_12_byte_rowid(rendered: str) -> bytes:
    if len(rendered) != 18 or any(character not in ALPHABET for character in rendered):
        raise ValueError("The 12-byte rendering must be exactly 18 characters from the local alphabet")
    values = [ALPHABET.index(character) for character in rendered]
    first, second, third, fourth = values[0:4]
    if ((first << 2) | ((second & 0x30) >> 4)) != 0:
        raise ValueError("The rendered prefix does not satisfy the local 12-byte rowid parser")
    output = bytearray(12)
    output[0] = ((second << 4) | ((third & 0x3C) >> 2)) & 0xFF
    output[1] = ((third << 6) | fourth) & 0xFF

    third, fourth = values[4:6]
    if ((third & 0x3C) >> 2) != 0:
        raise ValueError("The second rendered group does not satisfy the local 12-byte rowid parser")
    output[2] = ((third << 6) | fourth) & 0xFF

    position = 6
    for index in range(3):
        first, second, third, fourth = values[position : position + 4]
        position += 4
        output[3 + 3 * index] = ((first << 2) | ((second & 0x30) >> 4)) & 0xFF
        output[4 + 3 * index] = ((second << 4) | ((third & 0x3C) >> 2)) & 0xFF
        output[5 + 3 * index] = ((third << 6) | fourth) & 0xFF
    return bytes(output)


def strict_utf8_failure(raw: bytes) -> dict[str, object]:
    try:
        raw.decode("utf-8", errors="strict")
    except UnicodeDecodeError as error:
        return {
            "status": "rejected",
            "first_error_offset": error.start,
            "reason": error.reason,
        }
    raise RuntimeError("ROWID binary input must not accidentally be valid UTF-8")


def rowid_vector(source: dict[str, str]) -> dict[str, object]:
    raw = bytes.fromhex(source["raw_hex"])
    rendered = render_8_byte_rowid(raw) if len(raw) == 8 else render_12_byte_rowid(raw)
    if rendered != source["rendered_literal"]:
        raise RuntimeError(f"Independent ROWID calculation changed for {source['fixture_label']}")
    if len(raw) == 12 and parse_12_byte_rowid(rendered) != raw:
        raise RuntimeError("12-byte ROWID local rendered form did not round-trip its input shape")
    if len(raw) == 8 and int(rendered).to_bytes(8, "little", signed=True) != raw:
        raise RuntimeError("8-byte ROWID local decimal form did not round-trip its input shape")

    utf8_result = strict_utf8_failure(raw)
    if utf8_result["first_error_offset"] != source["expected_utf8_failure_offset"]:
        raise RuntimeError(f"Unexpected strict UTF-8 failure offset for {source['fixture_label']}")
    units = utf16_units(rendered)
    partial_ranges = []
    for field_offset, request_length in ROWID_RANGES:
        part = rendered[field_offset : field_offset + request_length]
        partial_ranges.append(
            {
                "field_offset": field_offset,
                "request_length": request_length,
                "expected_rendered": part,
                "expected_utf16_code_units_hex": units_hex(utf16_units(part)),
                "expected_count": len(utf16_units(part)),
            }
        )

    shape: dict[str, object] = {
        "byte_length_accepted_by_local_valueOf": len(raw) in (8, 12),
        "local_rendered_form_round_trips_raw_shape": True,
        "server_origin_or_server_range_validation_claimed": False,
    }
    if len(raw) == 12:
        shape["first_byte_top_two_bits"] = f"{raw[0] >> 6:02b}"
        shape["local_epno_big_endian"] = int.from_bytes(raw[0:2], "big")
        shape["local_hpno_big_endian"] = int.from_bytes(raw[2:6], "big")

    return {
        "fixture_label": source["fixture_label"],
        "ctype": 28,
        "enum_label": "synthetic_ctype_28_rowid_probe",
        "raw_hex": raw.hex().upper(),
        "raw_byte_count": len(raw),
        "local_shape_basis": shape,
        "raw_utf8_strict_decode": utf8_result,
        "render_algorithm": source["representation"],
        "rendered": rendered,
        "utf16_count": len(units),
        "rune_count": len(rendered),
        "utf16_code_units_hex": units_hex(units),
        "partial_ranges": partial_ranges,
        "status": "synthetic_not_run",
    }


def text_ranges(units: list[int]) -> list[dict[str, object]]:
    ranges = []
    for field_offset, request_length in TEXT_RANGES:
        expected = units[field_offset : field_offset + request_length]
        ranges.append(
            {
                "field_offset": field_offset,
                "request_length": request_length,
                "expected_utf16_code_units_hex": units_hex(expected),
                "expected_count": len(expected),
            }
        )
    return ranges


def text_vector(ctype: int, encoding: str) -> dict[str, object]:
    encoded = TEXT.encode(encoding, errors="strict")
    expected_hex = TEXT_BYTES_HEX[encoding]
    if encoded.hex().upper() != expected_hex:
        raise RuntimeError(
            f"Python reference codec output for {encoding} no longer matches the checked-in literal"
        )
    if encoded.decode(encoding, errors="strict") != TEXT:
        raise RuntimeError(f"Reference codec failed strict round-trip for {encoding}")

    units = utf16_units(TEXT)
    return {
        "fixture_label": f"synthetic_ctype_{ctype}_{encoding.lower().replace('-', '_')}_text_v1",
        "ctype": ctype,
        "enum_label": f"synthetic_ctype_{ctype}_text_probe",
        "enum_label_is_synthetic": True,
        "operation_profile": "GetChars ranges indexed by UTF-16 code units",
        "encoding": encoding,
        "text": TEXT,
        "code_points_hex": [f"U+{ord(character):04X}" for character in TEXT],
        "encoded_bytes_hex": expected_hex,
        "encoded_byte_count": len(encoded),
        "encoded_sha256_hex": hashlib.sha256(encoded).hexdigest(),
        "utf16_code_units_hex": units_hex(units),
        "utf16_count": len(units),
        "rune_count": len(TEXT),
        "length_query_expected_utf16_count": len(units),
        "partial_ranges": text_ranges(units),
        "status": "synthetic_not_run",
    }


def document() -> dict[str, object]:
    return {
        "fixture": "reader type conversion and UTF-16 range vectors",
        "fixture_kind": "synthetic_independent_golden_inputs",
        "execution_status": "not_run",
        "enum_label_policy": "All enum_label fields are test fixture labels, not product or server enum names.",
        "reference_codec": {
            "implementation": platform.python_implementation(),
            "runtime_version": platform.python_version(),
            "utf8_lookup_name": codecs.lookup("UTF-8").name,
            "gb18030_lookup_name": codecs.lookup("GB18030").name,
            "scope": "Expected text bytes are checked against Python standard-library reference codecs on this runtime only; they do not identify server codec versions or behavior.",
        },
        "source_basis": {
            "rowid_rendering": "Local Legacy DmRowId.valueOf(byte[]), toString(), and parse() shape, plus DmConvertion.EightByteToLong; reimplemented in pure Python without loading product code.",
            "text_rendering": "Local DmGetValue.GetString decodes CType 0, 1, 2, and 54 with ServerEncoding; the fixture stores literal reference bytes and expected UTF-16 units.",
            "reader_ranges": "GetChars offsets are UTF-16 code units. Current source type gates are documented in README; fixture values are expected data, not a claim that the current gate already accepts every case.",
            "getbytes_boundary": "Keep CType 0, 1, and 2 rejected by GetBytes; this fixture adds no binary support expectation for them.",
            "paths": [
                "src/W.DmProvider/Internal/Legacy/Dm/DmRowId.cs",
                "src/W.DmProvider/Internal/Legacy/Dm/DmConvertion.cs",
                "src/W.DmProvider/Internal/Legacy/Dm/DmGetValue.cs",
                "src/W.DmProvider/PublicApi/DmDataReader.cs",
                "docs/implementation/v1/specs/06-parameters-and-types.md",
                "docs/implementation/v1/specs/10-streaming-lob.md",
            ],
        },
        "rowid_vectors": [rowid_vector(source) for source in ROWID_INPUTS],
        "text_vectors": [
            text_vector(ctype, encoding)
            for ctype in (0, 54)
            for encoding in ("UTF-8", "GB18030")
        ],
        "getbytes_rejection_controls": [
            {
                "fixture_label": f"synthetic_ctype_{ctype}_getbytes_rejected_control",
                "ctype": ctype,
                "enum_label": f"synthetic_ctype_{ctype}_binary_rejection_control",
                "operation": "GetBytes",
                "expected_exception": "InvalidCastException",
                "status": "preserve_existing_rejection",
            }
            for ctype in (0, 1, 2)
        ],
        "data_scope": "No real user data, credentials, secrets, server captures, or database results are included.",
    }


def main() -> None:
    output = json.dumps(document(), ensure_ascii=False, indent=2) + "\n"
    (ROOT / "vectors.json").write_text(output, encoding="utf-8")


if __name__ == "__main__":
    main()

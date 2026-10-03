#!/usr/bin/env python3
"""Generate small, local-reference T17 LOB vectors with Python's standard codecs."""

from __future__ import annotations

import codecs
import hashlib
import math
import json
import platform
from pathlib import Path


ROOT = Path(__file__).resolve().parent
TEXT = "A\x00雪🚀e\u0301Z"
CHUNK_CAPACITY = 8
LARGE_LENGTH = 1_073_741_825
LARGE_CHUNK_BYTES = 16_384


def utf16_units(text: str) -> list[int]:
    data = text.encode("utf-16-le", errors="strict")
    return [data[index] | (data[index + 1] << 8) for index in range(0, len(data), 2)]


def text_vector(encoding: str) -> dict[str, object]:
    encoded = TEXT.encode(encoding, errors="strict")
    if encoded.decode(encoding, errors="strict") != TEXT:
        raise RuntimeError(f"Reference codec failed its round-trip for {encoding}")

    byte_boundaries = [0]
    utf16_boundaries = [0]
    rune_byte_ranges: list[dict[str, object]] = []
    byte_offset = 0
    utf16_offset = 0
    for rune_index, rune in enumerate(TEXT):
        rune_bytes = rune.encode(encoding, errors="strict")
        rune_units = utf16_units(rune)
        rune_byte_end = byte_offset + len(rune_bytes)
        rune_utf16_end = utf16_offset + len(rune_units)
        rune_byte_ranges.append(
            {
                "rune_index": rune_index,
                "code_point": f"U+{ord(rune):04X}",
                "byte_start": byte_offset,
                "byte_end_exclusive": rune_byte_end,
                "utf16_start": utf16_offset,
                "utf16_end_exclusive": rune_utf16_end,
            }
        )
        byte_offset = rune_byte_end
        utf16_offset = rune_utf16_end
        byte_boundaries.append(byte_offset)
        utf16_boundaries.append(utf16_offset)

    byte_splits = []
    for position in range(len(encoded) + 1):
        is_boundary = position in byte_boundaries
        byte_splits.append(
            {
                "cut_after_byte_count": position,
                "classification": "text_boundary" if is_boundary else "inside_encoded_rune",
            }
        )

    unit_values = utf16_units(TEXT)
    utf16_splits = []
    for position in range(len(unit_values) + 1):
        inside_surrogate_pair = any(
            utf16_boundaries[index] + 1 == position
            and utf16_boundaries[index + 1] - utf16_boundaries[index] == 2
            for index in range(len(utf16_boundaries) - 1)
        )
        utf16_splits.append(
            {
                "cut_after_utf16_code_units": position,
                "classification": "inside_surrogate_pair"
                if inside_surrogate_pair
                else "text_boundary",
            }
        )

    return {
        "vector_id": f"mixed_unicode_{encoding.lower().replace('-', '_')}_v1",
        "reference_encoding_name": encoding,
        "text": TEXT,
        "normalization": "preserved_as_written; no NFC/NFD normalization",
        "code_points_hex": [f"U+{ord(rune):04X}" for rune in TEXT],
        "rune_count": len(TEXT),
        "utf16_code_units_hex": [f"0x{unit:04X}" for unit in unit_values],
        "utf16_code_unit_count": len(unit_values),
        "encoded_bytes_hex": encoded.hex().upper(),
        "encoded_byte_count": len(encoded),
        "sha256_hex": hashlib.sha256(encoded).hexdigest(),
        "rune_ranges": rune_byte_ranges,
        "every_byte_cut_position": byte_splits,
        "every_utf16_cut_position": utf16_splits,
    }


def invalid_samples() -> list[dict[str, object]]:
    utf8_rocket = "🚀".encode("utf-8")
    gb_rocket = "🚀".encode("gb18030")
    candidates = [
        {
            "sample_id": "utf8_incomplete_supplementary_tail",
            "encoding": "UTF-8",
            "bytes_hex": utf8_rocket[:-1].hex().upper(),
            "condition": "truncated_valid_supplementary_sequence",
        },
        {
            "sample_id": "utf8_invalid_continuation",
            "encoding": "UTF-8",
            "bytes_hex": "E228A1",
            "condition": "invalid_continuation_byte",
        },
        {
            "sample_id": "gb18030_incomplete_four_byte_tail",
            "encoding": "GB18030",
            "bytes_hex": gb_rocket[:-1].hex().upper(),
            "condition": "truncated_prefix_of_reference_codec_supplementary_mapping",
        },
    ]
    for sample in candidates:
        raw = bytes.fromhex(str(sample["bytes_hex"]))
        try:
            raw.decode(str(sample["encoding"]).lower(), errors="strict")
        except UnicodeDecodeError as error:
            sample["strict_decode_result"] = "rejected"
            sample["reference_error_start"] = error.start
            sample["reference_error_end"] = error.end
            sample["reference_error_reason"] = error.reason
        else:
            raise RuntimeError(f"Reference codec unexpectedly accepted {sample['sample_id']}")
    return candidates


def schedule_cases() -> dict[str, object]:
    def event(kind: str, **values: object) -> dict[str, object]:
        return {"event": kind, **values}

    cases = [
        {
            "case_id": "empty_known_length",
            "contract_tags": ["LOB-01", "LOB-02"],
            "source": {"length_bytes": 0, "length_known": True, "can_seek": True, "caller_owns": True},
            "chunk_capacity_bytes": CHUNK_CAPACITY,
            "logical_events": [event("read", requested_bytes=CHUNK_CAPACITY, returned_bytes=0, eof=True)],
            "expected_summary": {"bytes_consumed": 0, "payload_chunks": 0, "replayed": False, "source_disposed_by_provider": False},
        },
        {
            "case_id": "exact_full_chunk_then_eof",
            "contract_tags": ["LOB-01", "LOB-02"],
            "source": {"length_bytes": CHUNK_CAPACITY, "length_known": True, "can_seek": True, "caller_owns": True},
            "chunk_capacity_bytes": CHUNK_CAPACITY,
            "logical_events": [
                event("read", requested_bytes=CHUNK_CAPACITY, returned_bytes=CHUNK_CAPACITY, eof=False),
                event("payload_chunk_available", chunk_index=0, byte_count=CHUNK_CAPACITY),
                event("read", requested_bytes=CHUNK_CAPACITY, returned_bytes=0, eof=True),
            ],
            "expected_summary": {"bytes_consumed": CHUNK_CAPACITY, "payload_chunks": 1, "replayed": False, "source_disposed_by_provider": False},
        },
        {
            "case_id": "final_short_chunk",
            "contract_tags": ["LOB-01", "LOB-02"],
            "source": {"length_bytes": CHUNK_CAPACITY + 3, "length_known": True, "can_seek": True, "caller_owns": True},
            "chunk_capacity_bytes": CHUNK_CAPACITY,
            "logical_events": [
                event("read", requested_bytes=CHUNK_CAPACITY, returned_bytes=CHUNK_CAPACITY, eof=False),
                event("payload_chunk_available", chunk_index=0, byte_count=CHUNK_CAPACITY),
                event("read", requested_bytes=CHUNK_CAPACITY, returned_bytes=3, eof=False),
                event("payload_chunk_available", chunk_index=1, byte_count=3),
                event("read", requested_bytes=CHUNK_CAPACITY, returned_bytes=0, eof=True),
            ],
            "expected_summary": {"bytes_consumed": CHUNK_CAPACITY + 3, "payload_chunks": 2, "replayed": False, "source_disposed_by_provider": False},
        },
        {
            "case_id": "unknown_length_nonseekable_source",
            "contract_tags": ["LOB-02"],
            "source": {"length_bytes": None, "scripted_actual_bytes": CHUNK_CAPACITY + 3, "length_known": False, "can_seek": False, "caller_owns": True},
            "chunk_capacity_bytes": CHUNK_CAPACITY,
            "logical_events": [
                event("read", requested_bytes=CHUNK_CAPACITY, returned_bytes=CHUNK_CAPACITY, eof=False),
                event("payload_chunk_available", chunk_index=0, byte_count=CHUNK_CAPACITY),
                event("read", requested_bytes=CHUNK_CAPACITY, returned_bytes=3, eof=False),
                event("payload_chunk_available", chunk_index=1, byte_count=3),
                event("read", requested_bytes=CHUNK_CAPACITY, returned_bytes=0, eof=True),
            ],
            "expected_summary": {"bytes_consumed": CHUNK_CAPACITY + 3, "payload_chunks": 2, "length_or_position_access_required": False, "replayed": False, "source_disposed_by_provider": False},
        },
        {
            "case_id": "caller_owns_source_after_success",
            "contract_tags": ["LOB-02"],
            "source": {"length_bytes": 3, "length_known": True, "can_seek": False, "caller_owns": True},
            "chunk_capacity_bytes": CHUNK_CAPACITY,
            "logical_events": [
                event("read", requested_bytes=CHUNK_CAPACITY, returned_bytes=3, eof=False),
                event("payload_chunk_available", chunk_index=0, byte_count=3),
                event("read", requested_bytes=CHUNK_CAPACITY, returned_bytes=0, eof=True),
                event("operation_complete", result="success", source_remains_open=True),
            ],
            "expected_summary": {"bytes_consumed": 3, "replayed": False, "source_disposed_by_provider": False},
        },
        {
            "case_id": "source_read_error_after_prefix",
            "contract_tags": ["LOB-02", "LOB-05"],
            "source": {"length_bytes": None, "length_known": False, "can_seek": False, "caller_owns": True, "scripted_bytes_before_error": 5},
            "chunk_capacity_bytes": CHUNK_CAPACITY,
            "logical_events": [
                event("read", requested_bytes=CHUNK_CAPACITY, returned_bytes=5, eof=False),
                event("payload_chunk_available", chunk_index=0, byte_count=5),
                event("read", requested_bytes=CHUNK_CAPACITY, outcome="raises", exception_kind="synthetic_io_error"),
                event("operation_stop", result="source_read_error", retry_allowed=False),
            ],
            "expected_summary": {"bytes_consumed_before_error": 5, "replayed": False, "source_disposed_by_provider": False, "source_position_rewound": False},
        },
        {
            "case_id": "cancellation_after_prefix",
            "contract_tags": ["LOB-02", "LOB-05"],
            "source": {"length_bytes": None, "length_known": False, "can_seek": False, "caller_owns": True},
            "chunk_capacity_bytes": CHUNK_CAPACITY,
            "logical_events": [
                event("read", requested_bytes=CHUNK_CAPACITY, returned_bytes=5, eof=False),
                event("payload_chunk_available", chunk_index=0, byte_count=5),
                event("caller_cancellation", after_bytes=5),
                event("operation_stop", result="cancelled", automatic_replay=False),
            ],
            "expected_summary": {"bytes_consumed_before_cancel": 5, "replayed": False, "source_disposed_by_provider": False, "source_position_rewound": False},
        },
    ]

    for case_id, parent_event in [
        ("parent_next_row_invalidates_stream", "parent_next_row"),
        ("parent_next_result_invalidates_stream", "parent_next_result"),
        ("parent_close_invalidates_stream", "parent_reader_close"),
        ("stale_generation_invalidates_stream", "session_generation_changes"),
    ]:
        cases.append(
            {
                "case_id": case_id,
                "contract_tags": ["LOB-04"],
                "source": {"length_bytes": 32, "length_known": True, "can_seek": False, "caller_owns": False},
                "chunk_capacity_bytes": CHUNK_CAPACITY,
                "logical_events": [
                    event("stream_open", parent_lease_id="lease-A", session_generation=7),
                    event("read", requested_bytes=CHUNK_CAPACITY, returned_bytes=CHUNK_CAPACITY, eof=False),
                    event("payload_chunk_available", chunk_index=0, byte_count=CHUNK_CAPACITY),
                    event(parent_event, invalidates_stream_id="stream-A"),
                    event("read", stream_id="stream-A", requested_bytes=CHUNK_CAPACITY, outcome="rejected_as_invalid"),
                ],
                "expected_summary": {"old_stream_can_read_after_parent_event": False, "new_session_or_row_accessed_by_old_stream": False},
            }
        )

    return {
        "fixture": "T17 synthetic LOB source and reader schedule cases",
        "fixture_kind": "protocol_neutral_logical_harness_schedule",
        "execution_status": "not_run",
        "chunk_capacity_bytes": CHUNK_CAPACITY,
        "events_are": "synthetic harness scheduling labels, not DM messages, captured traffic, or claims about packet ordering",
        "cases": cases,
    }


def code_unit_count(text: str) -> int:
    return len(utf16_units(text))


def incremental_byte_cycle_sha256(length: int, seed: int, stride: int, chunk_bytes: int) -> str:
    period = 256 // math.gcd(stride, 256)
    if chunk_bytes % period != 0:
        raise RuntimeError("The bounded reference chunk must contain whole generator periods")
    cycle = bytes((seed + stride * index) & 0xFF for index in range(period))
    bounded_block = cycle * (chunk_bytes // period)
    digest = hashlib.sha256()
    remaining = length
    view = memoryview(bounded_block)
    while remaining:
        amount = min(chunk_bytes, remaining)
        digest.update(view[:amount])
        remaining -= amount
    return digest.hexdigest()


def profile_rows() -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    bodies = [
        "A雪🚀e\u0301\x00",
        "B漢字🚀o\u0308\x00",
        "C梦数据🚀n\u0303\x00",
        "D漢雪🚀a\u030a\x00",
        "E数据🚀i\u0307\x00",
    ]
    for index, body in enumerate(bodies, start=1):
        marker = f"⟦R3-ROW-{index:04d}-UNIQ-{index * 7919:06d}-Ω⟧"
        prefix = f"row={index:04d}|{body}|"
        value = prefix + marker + f"|tail={index:04d}"
        marker_offset = code_unit_count(prefix)
        marker_length = code_unit_count(marker)
        utf16_units(value)
        encoded_references = {}
        for encoding in ("utf-8", "gb18030"):
            encoded = value.encode(encoding, errors="strict")
            if encoded.decode(encoding, errors="strict") != value:
                raise RuntimeError(f"Reference codec failed profile row {index} round-trip for {encoding}")
            encoded_references[encoding] = {
                "reference_byte_count": len(encoded),
                "reference_sha256_hex": hashlib.sha256(encoded).hexdigest(),
            }
        rows.append(
            {
                "row_number": index,
                "value": value,
                "unique_marker": marker,
                "utf16_code_unit_count": code_unit_count(value),
                "unique_marker_utf16_offset": marker_offset,
                "unique_marker_utf16_length": marker_length,
                "query_ranges": [
                    {
                        "range_id": "prefix_check",
                        "offset_utf16_code_units": 0,
                        "length_utf16_code_units": code_unit_count(prefix),
                        "expected_text": prefix,
                    },
                    {
                        "range_id": "unique_marker_check",
                        "offset_utf16_code_units": marker_offset,
                        "length_utf16_code_units": marker_length,
                        "expected_text": marker,
                    },
                    {
                        "range_id": "tail_check",
                        "offset_utf16_code_units": code_unit_count(prefix + marker),
                        "length_utf16_code_units": code_unit_count(f"|tail={index:04d}"),
                        "expected_text": f"|tail={index:04d}",
                    },
                ],
                "reference_encodings": encoded_references,
            }
        )
    if len({row["unique_marker"] for row in rows}) != len(rows):
        raise RuntimeError("Out-row profile markers must be unique")
    return rows


def large_profile_row() -> dict[str, object]:
    atoms = [
        "A雪🚀e\u0301\x00",
        "B漢🚁o\u0308\x00",
        "C梦数据🚲n\u0303\x00",
        "D漢雪🛰️a\u030a\x00",
        "E数据库🚀i\u0307\x00",
        "F雨雲🧭u\u0304\x00",
        "G火星🧪c\u0327\x00",
        "H海洋🪐y\u0301\x00",
        "I森林🛸o\u0302\x00",
        "J山川🌙a\u0308\x00",
        "K行星🚀e\u0323\x00",
        "L云层🧬u\u0308\x00",
    ]
    pieces: list[str] = []
    markers: list[dict[str, object]] = []
    rune_offset = 0
    utf16_offset = 0

    for index in range(8192):
        if index % 512 == 0:
            marker = f"⟦R3-LARGE-{index // 512:04d}-UNIQ-{(index // 512) * 104729 + 97:08d}-Ω⟧"
            markers.append(
                {
                    "utf16_offset": utf16_offset,
                    "scalar_offset": rune_offset,
                    "unique_marker": marker,
                }
            )
            pieces.append(marker)
            rune_offset += len(marker)
            utf16_offset += code_unit_count(marker)

        segment = f"{index:04X}:{atoms[(index * 7 + index // 13) % len(atoms)]}|"
        pieces.append(segment)
        rune_offset += len(segment)
        utf16_offset += code_unit_count(segment)

    value = "".join(pieces)
    encoded = value.encode("utf-8", errors="strict")
    if encoded.decode("utf-8", errors="strict") != value:
        raise RuntimeError("Large out-row reference value failed its UTF-8 round-trip")
    if len({marker["unique_marker"] for marker in markers}) != len(markers):
        raise RuntimeError("Large out-row profile markers must be unique")

    return {
        "profile_id": "large_numbered_unicode_out_row_control_v1",
        "fixture_kind": "synthetic_independent_profile_value",
        "execution_status": "not_run",
        "value": value,
        "utf16_count": code_unit_count(value),
        "rune_count": len(value),
        "utf8_sha256": hashlib.sha256(encoded).hexdigest(),
        "utf8_byte_count": len(encoded),
        "markers": markers,
        "offset_unit_note": "utf16_offset is in CLR UTF-16 code units; scalar_offset counts Unicode scalar values from the beginning of value.",
        "content_note": "The independently generated value has varying numbered segments and distributed unique markers, with ASCII, BMP characters, supplementary emoji, NUL, and unnormalized combining sequences. It is not a captured server row.",
    }


def vector_document() -> dict[str, object]:
    gb18030_codec = codecs.lookup("gb18030")
    utf8_codec = codecs.lookup("utf-8")
    return {
        "fixture": "T17 independent small fixed LOB vectors",
        "fixture_kind": "synthetic_independent_reference_vectors",
        "execution_status": "not_run",
        "reference_codec": {
            "implementation": platform.python_implementation(),
            "runtime_version": platform.python_version(),
            "utf8_lookup_name": utf8_codec.name,
            "gb18030_lookup_name": gb18030_codec.name,
            "scope": "These are Python standard-library reference codec results for this runtime only; they do not establish any server codec version or NCLOB behavior.",
        },
        "small_fixed_text_vectors": [text_vector("UTF-8"), text_vector("GB18030")],
        "invalid_inputs": {
            "byte_sequences": invalid_samples(),
            "isolated_utf16_code_unit_inputs": [
                {"sample_id": "isolated_high_surrogate", "utf16_code_units_hex": ["0xD83D"], "json_text_contains_unpaired_surrogate": False},
                {"sample_id": "isolated_low_surrogate", "utf16_code_units_hex": ["0xDE80"], "json_text_contains_unpaired_surrogate": False},
                {"sample_id": "reversed_surrogate_pair", "utf16_code_units_hex": ["0xDE80", "0xD83D"], "json_text_contains_unpaired_surrogate": False},
                {"sample_id": "valid_supplementary_pair_control", "utf16_code_units_hex": ["0xD83D", "0xDE80"], "json_text_contains_unpaired_surrogate": False},
            ],
            "input_representation_note": "Surrogate cases are integer-like code-unit labels only; no JSON string contains an unpaired surrogate.",
        },
        "large_logical_stream_descriptor": {
            "descriptor_id": "blob_byte_cycle_1gib_plus_one_v1",
            "materialized_payload_file": False,
            "length_bytes": LARGE_LENGTH,
            "generator": {
                "algorithm": "byte_cycle_v1",
                "definition": "byte[i] = (seed + stride * i) mod 256 for 0 <= i < length_bytes",
                "seed": 17,
                "stride": 131,
            },
            "expected_incremental_hash": {
                "algorithm": "SHA-256",
                "input_order": "generated byte sequence in ascending index order, exactly length_bytes total",
                "consumer_update_chunk_bytes": LARGE_CHUNK_BYTES,
                "expected_sha256_hex": incremental_byte_cycle_sha256(
                    LARGE_LENGTH, 17, 131, LARGE_CHUNK_BYTES
                ),
                "expected_result_source": "computed by this independent generator with incremental bounded updates; no payload or digest sidecar file is read",
            },
            "controlled_buffer_upper_boundary": {
                "generator_payload_buffer_bytes": LARGE_CHUNK_BYTES,
                "max_live_payload_buffers": 1,
                "fake_recorder_retained_payload_bytes": 0,
                "fake_recorder_mode": "aggregate byte count plus one incremental SHA-256 state; no per-chunk payload copies or unbounded event list",
                "scope": "synthetic harness budget; not a measured product memory result",
            },
        },
        "out_row_profile_proof": {
            "profile_id": "unique_unicode_markers_v1",
            "offset_unit": "UTF-16 code units, matching CLR char indexing",
            "row_values_are": "independent synthetic source values; not captured server rows or proof of server out-row storage",
            "purpose": "Distinct numbered Unicode values and unique markers make an incorrect range offset visible even if a fixture would otherwise repeat the same text.",
            "rows": profile_rows(),
        },
        "out_row_profile_large": large_profile_row(),
    }


def write_json(path: Path, data: dict[str, object]) -> None:
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main() -> None:
    write_json(ROOT / "vectors.json", vector_document())
    write_json(ROOT / "schedules.json", schedule_cases())


if __name__ == "__main__":
    main()

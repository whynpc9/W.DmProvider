#!/usr/bin/env python3
"""Validate T09 profile envelopes and strict candidate contracts without raw diagnostics."""

import json
import pathlib
import sys

FROZEN_SHA = "51e340397f64538e41d999fcf01e154bfabbf94f08704e749bf03ac7128440f0"
OFFICIAL_SHA = "8f6e59680d0a076df53bea50d5a2bdbd288535cd85b2d7ca5064c02adc9c6e6b"
LEGACY_NUMERIC_WIRE = {
    "0.1": "C00A", "-0.1": "3F5C66", "0.01": "C0F8", "-0.01": "3F6E66",
    "1.23": "C1F818", "-1.23": "3E6E4E66",
}


def fail(reason):
    print(json.dumps({"schema_version": 1, "task": "T09", "status": "rejected", "reason": reason}))
    raise SystemExit(1)


def load(path):
    try:
        return json.loads(pathlib.Path(path).read_text())
    except (OSError, ValueError):
        fail("result_missing_or_invalid")


def output(status, **kwargs):
    print(json.dumps({"schema_version": 1, "task": "T09", "status": status, **kwargs},
                     separators=(",", ":")))


def envelope(row, implementation, sha):
    if (row.get("schema_version") != 1 or row.get("task") != "T09" or
            row.get("implementation") != implementation or row.get("assembly_sha256") != sha or
            not row.get("assembly_mvid") or row.get("status") != "observed" or
            row.get("evidence_kind") != "observed_profile" or
            row.get("server_account_verified") is not True or
            row.get("cleanup_verified") is not True or
            row.get("final_database_state") != "unique_object_absent" or
            set(row.get("cases", {})) != {f"TYP-{number:02d}" for number in range(1, 10)} or
            row.get("fixture_execution_status") != "not_run"):
        fail("profile_envelope_invalid")
    final = row.get("final_database_state_before_cleanup")
    if (not isinstance(final, dict) or final.get("object_present") is not True or
            not isinstance(final.get("row_count"), int) or final["row_count"] < 0):
        fail("server_final_state_missing")


def case(row, group, key):
    entry = row["cases"].get(group, {}).get(key)
    if not isinstance(entry, dict):
        fail("scenario_missing")
    return entry


def observed(entry):
    if entry.get("outcome") != "returned":
        fail("candidate_case_not_returned")
    return entry["observation"]


def known(entry):
    capture = observed(entry)
    return capture["value"]["observation"]["value"].get("known_value")


def strict(row):
    # These are assertions on the candidate only. Old O/frozen-W errors remain observations.
    for key, value in {
        "enum_negative": "-10", "enum_positive": "7", "enum_large": "1000",
        "flags": "5", "byte_underlying": "255", "ulong_underlying": "18446744073709551615",
    }.items():
        if known(case(row, "TYP-01", key)) != value:
            fail("typ01_underlying_value_mismatch")
    for key, value in {
        "sbyte_min": "-128", "byte_max": "255", "int16_min": "-32768",
        "uint16_max": "65535", "int32_min": "-2147483648", "int32_max": "2147483647",
        "int64_min": "-9223372036854775808", "int64_max": "9223372036854775807",
        "uint32_max": "4294967295", "uint64_max": "18446744073709551615",
    }.items():
        if known(case(row, "TYP-02", key)) != value:
            fail("typ02_boundary_mismatch")
    for key, value in (("explicit_byte_max", "255"), ("explicit_uint16_max", "65535"),
                       ("explicit_uint32_max", "4294967295"), ("explicit_uint64_max", "18446744073709551615")):
        if known(case(row, "TYP-02", key)) != value:
            fail("typ02_explicit_unsigned_max_changed")
    for key in ("explicit_byte_256_rejected", "explicit_uint16_negative_rejected",
                "explicit_uint16_65536_rejected", "explicit_uint32_negative_rejected",
                "explicit_uint64_negative_rejected"):
        invalid = observed(case(row, "TYP-02", key))
        if (invalid.get("outcome") != "error" or invalid.get("error", {}).get("kind") != "System.OverflowException" or
                invalid.get("business_frame_seen") is not False or invalid.get("execution_frame_seen") is not False):
            fail("typ02_explicit_unsigned_source_range_not_rejected")
    for group, key, value in (("TYP-01", "ulong_enum_explicit_string", "18446744073709551615"),
                              ("TYP-01", "ulong_enum_explicit_string_cast", "18446744073709551615"),
                              ("TYP-02", "ulong_explicit_string", "18446744073709551615"),
                              ("TYP-02", "ulong_explicit_string_cast", "18446744073709551615"),
                              ("TYP-02", "double_explicit_single", "1.5"),
                              ("TYP-02", "double_explicit_single_cast", "1.5")):
        if known(case(row, group, key)) != value:
            fail("typ02_explicit_resolved_type_value_changed")
    if observed(case(row, "TYP-02", "nonintegral_getint32"))["getter"].get("outcome") != "error":
        fail("typ02_invalid_input_accepted")
    for key in ("nan_input", "infinity_input"):
        special = observed(case(row, "TYP-02", key))
        if special.get("outcome") != "error" or special.get("execution_frame_seen") is not False:
            fail("typ02_nonfinite_sent_or_accepted")
    narrow = observed(case(row, "TYP-02", "explicit_int32_overflow"))
    if narrow.get("outcome") != "error" or narrow.get("execution_frame_seen") is not False:
        fail("typ02_overflow_sent_or_accepted")

    for key, value in {
        "decimal38_20": "123456789012345678.12345678901234567890",
        "small_negative": "-0.00000000000000000001",
        "outside_clr_decimal": "999999999999999999.99999999999999999999",
    }.items():
        getter = observed(case(row, "TYP-03", key))["getter"]
        if (getter["provider_specific"].get("outcome") != "returned" or
                getter["provider_specific"]["observation"].get("known_value") != value):
            fail("typ03_precision_lost")
    if observed(case(row, "TYP-03", "outside_clr_decimal"))["getter"]["decimal_getter"].get("outcome") != "error":
        fail("typ03_clr_decimal_overflow_not_rejected")
    for key in ("ado_decimal_positive_tenth", "ado_decimal_negative_tenth",
                "ado_decimal_positive_hundredth", "ado_decimal_negative_hundredth",
                "ado_decimal_positive_1_23", "ado_decimal_negative_1_23"):
        roundtrip = observed(case(row, "TYP-03", key))
        if (roundtrip.get("rows") != 1 or roundtrip.get("exact_getdecimal", {}).get("outcome") != "returned" or
                roundtrip["exact_getdecimal"].get("observation") is not True):
            fail("typ03_ado_decimal_roundtrip_mismatch")

    valid = observed(case(row, "TYP-04", "guid_shaped_text"))
    invalid = observed(case(row, "TYP-04", "invalid_getguid"))
    for result in (valid, invalid):
        if result["metadata"]["field_type"].get("observation") != "System.String":
            fail("typ04_guid_text_not_string")
    if (valid["getter"]["guid_getter"].get("outcome") != "returned" or
            invalid["getter"]["guid_getter"].get("outcome") != "error" or
            invalid["getter"]["guid_getter"].get("error", {}).get("kind") != "System.FormatException"):
        fail("typ04_explicit_guid_semantics_invalid")
    null_columns = observed(case(row, "TYP-04", "nullable_guid_columns"))
    text50 = observed(case(row, "TYP-04", "varchar50_guid_d_format"))
    binary16 = observed(case(row, "TYP-04", "binary16_guid"))
    bad_binary = observed(case(row, "TYP-04", "invalid_binary15_guid"))
    if (null_columns.get("rows") != 1 or null_columns.get("text_null") is not True or
            null_columns.get("binary_null") is not True or
            text50.get("text_field_type", {}).get("observation") != "System.String" or
            text50.get("text_getguid", {}).get("outcome") != "returned" or
            binary16.get("binary_getguid", {}).get("outcome") != "returned" or
            bad_binary.get("binary_getguid", {}).get("outcome") != "error"):
        fail("typ04_guid_column_contract_invalid")
    for key, capacity in (("clr_guid_to_varchar50", 50), ("clr_guid_to_binary16", 16)):
        bound = observed(case(row, "TYP-04", key))
        after = bound.get("after", {})
        if (bound.get("accepted") is not True or after.get("rows") != 1 or
                after.get("is_null") is not False or
                after.get("get_guid", {}).get("outcome") != "returned" or
                after["get_guid"]["observation"].get("known_value") != "00112233-4455-6677-8899-aabbccddeeff" or
                bound.get("target_capacity") != capacity):
            fail("typ04_clr_guid_binding_lost_or_changed")
    short = observed(case(row, "TYP-04", "clr_guid_to_binary15_rejected"))
    if (short.get("accepted") is not False or short.get("unchanged") is not True or
            short.get("target_capacity") != 15 or short.get("guid_bytes") != 16 or
            short.get("error", {}).get("kind") not in ("System.OverflowException", "System.NotSupportedException")):
        fail("typ04_short_guid_capacity_not_rejected")

    if observed(case(row, "TYP-05", "unicode_roundtrip")).get("exact") is not True:
        fail("typ05_unicode_roundtrip_mismatch")
    surrogate = observed(case(row, "TYP-05", "unpaired_surrogate"))
    if surrogate.get("outcome") != "error" or surrogate.get("execution_frame_seen") is not False:
        fail("typ05_bad_text_sent_or_accepted")
    if not isinstance(row.get("negotiated_charset"), str) or not row["negotiated_charset"].strip():
        fail("typ05_server_charset_unidentified")

    timestamp = observed(case(row, "TYP-06", "timestamp_profile"))["getter"]
    get_datetime = timestamp["get_datetime"]
    if (get_datetime.get("outcome") != "returned" or
            get_datetime.get("observation", {}).get("kind") != "Unspecified" or
            get_datetime["observation"].get("invariant") != "2024-02-29T12:34:56.1234560" or
            timestamp["get_string"].get("observation", {}).get("ef_datetimeoffset_parse") is not True):
        fail("typ06_timestamp_precision_or_ef_text_invalid")
    interval = observed(case(row, "TYP-06", "interval_profile"))["getter"]["get_string"]
    if (interval.get("outcome") != "returned" or
            interval.get("observation", {}).get("ef_interval_shape_parse") is not True):
        fail("typ06_interval_ef_text_invalid")
    for key in ("dateonly_explicit", "timeonly_six_digits", "dto_positive_six_digits",
                "dto_negative_six_digits"):
        temporal = observed(case(row, "TYP-06", key))
        if (temporal.get("rows") != 1 or temporal.get("typed", {}).get("outcome") != "returned" or
                temporal["typed"].get("observation") != temporal.get("expected")):
            fail("typ06_explicit_temporal_roundtrip_mismatch")
    for key in ("timeonly_seventh_digit_rejected", "dto_seventh_digit_rejected",
                "interval_timespan_minvalue"):
        rejected = case(row, "TYP-06", key)
        if (rejected.get("outcome") != "error" or
                rejected.get("error", {}).get("kind") not in
                ("System.OverflowException", "System.NotSupportedException")):
            fail("typ06_unrepresentable_time_not_explicitly_rejected")
    for key in ("interval_positive_one_microsecond", "interval_explicit_scale6_bare", "interval_negative_one_microsecond",
                "interval_negative_multiday_fraction"):
        interval_case = observed(case(row, "TYP-06", key))
        if (interval_case.get("rows") != 1 or interval_case.get("typed", {}).get("outcome") != "returned" or
                interval_case["typed"].get("observation", {}).get("ticks") != interval_case.get("expected_ticks") or
                interval_case.get("get_string", {}).get("outcome") != "returned" or
                interval_case["get_string"].get("observation", {}).get("ef_interval_shape_parse") is not True or
                interval_case["get_string"]["observation"].get("ef_interval_ticks") != interval_case.get("expected_ticks")):
            fail("typ06_interval_sign_scale_or_ef_text_mismatch")

    for key in ("interval_scale4_small_fraction", "interval_scale3_small_fraction",
                "interval_scale4_trailing_zero"):
        scaled = observed(case(row, "TYP-06", key))
        if (scaled.get("rows") != 1 or scaled.get("typed", {}).get("outcome") != "returned" or
                scaled["typed"].get("observation", {}).get("ticks") != scaled.get("expected_ticks") or
                scaled.get("ef_interval_shape_parse") is not True or
                scaled.get("ef_interval_ticks") != scaled.get("expected_ticks")):
            fail("typ06_lower_scale_interval_text_changed")
    typed_null = observed(case(row, "TYP-07", "typed_null"))
    if typed_null["is_dbnull"].get("observation") is not True:
        fail("typ07_typed_null_invalid")
    untyped = observed(case(row, "TYP-07", "untyped_null"))
    if untyped.get("outcome") != "error" or untyped.get("execution_frame_seen") is not False:
        fail("typ07_untyped_null_without_describe_accepted")
    for key in ("null_untyped_select_metadata", "null_cast_int_select_metadata"):
        null_case = observed(case(row, "TYP-07", key))
        if (null_case.get("prepare_succeeded") is not True or
                null_case.get("parameter_metadata", {}).get("type_flag") != 2 or
                null_case.get("execute_succeeded") is not False or
                null_case.get("execute_error", {}).get("kind") != "System.InvalidOperationException" or
                null_case.get("execution_frame_seen") is not False):
            fail("typ07_advisory_null_describe_accepted")
    explicit_null = observed(case(row, "TYP-07", "null_int32_select_metadata"))
    if (explicit_null.get("prepare_succeeded") is not True or
            explicit_null.get("execute_succeeded") is not True or
            explicit_null.get("result", {}).get("is_dbnull") is not True):
        fail("typ07_explicit_null_metadata_invalid")
    column_null = observed(case(row, "TYP-07", "null_int_column_insert_metadata"))
    if (column_null.get("prepare_succeeded") is not True or
            column_null.get("parameter_metadata", {}).get("type_flag") != 1 or
            column_null.get("execute_succeeded") is not True or
            column_null.get("final_state") != {"rows": 1, "int_column_null": True}):
        fail("typ07_reliable_column_null_describe_invalid")
    if observed(case(row, "TYP-07", "empty_binary"))["is_dbnull"].get("observation") is not False:
        fail("typ07_empty_binary_lost")
    for key in ("binary_overflow", "varbinary_overflow", "blob_overflow"):
        overflow = observed(case(row, "TYP-07", key))
        if (overflow.get("accepted") is not False or overflow.get("unchanged") is not True or
                overflow.get("error", {}).get("kind") != "System.OverflowException" or
                overflow.get("declared_size") != 16 or overflow.get("input_bytes") != 17):
            fail("typ07_binary_length_overflow_or_final_state_invalid")

    duplicate = observed(case(row, "TYP-08", "duplicate_names"))
    mixed = observed(case(row, "TYP-08", "mixed_named_positional"))
    if (duplicate.get("first_added") is not True or duplicate.get("duplicate_rejected") is not True or
            duplicate.get("count") != 1 or mixed.get("rejected") is not True):
        fail("typ08_parameter_conflict_accepted")
    for key in ("named_marker_ignores_literal_comment", "prepare_value_change", "prepare_type_change",
                "prepare_size_change", "reset_dbtype"):
        observed(case(row, "TYP-08", key))
    marker = observed(case(row, "TYP-08", "named_marker_ignores_literal_comment"))
    if (marker.get("field_count") != 3 or marker.get("first_parameter", {}).get("known_value") != "17" or
            marker.get("repeated_parameter", {}).get("known_value") != "17"):
        fail("typ08_named_reuse_or_tokenizer_invalid")
    if observed(case(row, "TYP-08", "reset_dbtype")).get("after") != "Int64":
        fail("typ08_reset_type_invalid")

    for key in ("sync_async_text", "sync_async_timestamp"):
        if observed(case(row, "TYP-09", key)).get("same_serialized_observation") is not True:
            fail("typ09_sync_async_value_mismatch")
    for key, error_kind in (("typed_null_string", "System.InvalidCastException"),
                            ("typed_nonintegral_int32", "System.InvalidCastException"),
                            ("typed_overflow_int32", "System.OverflowException"),
                            ("typed_highprecision_nonintegral_int32", "System.InvalidCastException")):
        numeric = observed(case(row, "TYP-09", key))
        if (numeric.get("rows") != 1 or numeric.get("same_serialized_observation") is not True or
                any(numeric.get(mode, {}).get("outcome") != "error" or
                    numeric.get(mode, {}).get("error", {}).get("kind") != error_kind
                    for mode in ("sync", "async_result"))):
            fail("typ09_typed_integer_error_contract_invalid")
    null_object = observed(case(row, "TYP-09", "typed_null_object"))
    if (null_object.get("rows") != 1 or null_object.get("same_serialized_observation") is not True or
            null_object.get("sync_object_is_dbnull") is not True or null_object.get("async_object_is_dbnull") is not True or
            any(null_object.get(mode, {}).get("outcome") != "returned" or
                null_object.get(mode, {}).get("observation") != "null"
                for mode in ("sync", "async_result"))):
        fail("typ09_typed_null_object_not_dbnull")
    unsigned = observed(case(row, "TYP-09", "typed_uint64_max"))
    if (unsigned.get("rows") != 1 or unsigned.get("same_serialized_observation") is not True or
            any(unsigned.get(mode, {}).get("outcome") != "returned" or
                unsigned.get(mode, {}).get("observation", {}).get("known_value") != "18446744073709551615"
                for mode in ("sync", "async_result"))):
        fail("typ09_typed_unsigned_precision_lost")
    error_parity = observed(case(row, "TYP-09", "sync_async_invalid_guid"))
    sync, async_ = error_parity.get("sync_error", {}), error_parity.get("async_error", {})
    if (sync.get("outcome") != "error" or async_.get("outcome") != "error" or
            sync.get("error", {}).get("kind") != async_.get("error", {}).get("kind")):
        fail("typ09_sync_async_exception_mismatch")
    complex_array = observed(case(row, "TYP-09", "complex_array_rejected"))
    if complex_array.get("outcome") != "error" or complex_array.get("execution_frame_seen") is not False:
        fail("typ09_unsupported_complex_type_accepted")
    clob = observed(case(row, "TYP-09", "lob_text_profile"))["getter"]["get_string"]
    if (clob.get("outcome") != "returned" or
            clob.get("observation", {}).get("text", {}).get("known_value") != "T09_LOB"):
        fail("typ09_ef_lob_getstring_invalid")

    output("candidate_typ01_09_verified", assembly_sha256=row["assembly_sha256"],
           strict_contracts=[f"TYP-{number:02d}" for number in range(1, 10)],
           negotiated_charset=row["negotiated_charset"],
           scope_limit="observed_server_profile_only; full_EF_consumer_T12_pending",
           final_database_state=row["final_database_state"])


def main():
    if len(sys.argv) < 3:
        fail("usage")
    mode = sys.argv[1]
    if mode == "codec" and len(sys.argv) == 4:
        row = load(sys.argv[2]); implementation = sys.argv[3]
        expected_hash = OFFICIAL_SHA if implementation == "O" else FROZEN_SHA
        if (row.get("implementation") != implementation or row.get("assembly_sha256") != expected_hash or
                row.get("evidence_kind") != "offline_legacy_codec_characterization" or
                row.get("status") != "observed" or row.get("legacy_wire_vectors") != LEGACY_NUMERIC_WIRE or
                set(row.get("xdec_wire_vectors", {})) != set(LEGACY_NUMERIC_WIRE) or
                set(row.get("xdec_parse_text", {})) != set(LEGACY_NUMERIC_WIRE)):
            fail("old_codec_vector_mismatch")
        output("legacy_codec_vectors_observed", implementation=implementation,
               numeric_legacy_diverges_from_candidate=True, assembly_sha256=expected_hash)
    elif mode == "baseline" and len(sys.argv) == 4:
        official, frozen = load(sys.argv[2]), load(sys.argv[3])
        envelope(official, "O", OFFICIAL_SHA)
        envelope(frozen, "W-T08-frozen", FROZEN_SHA)
        output("baseline_characterized", official_sha256=OFFICIAL_SHA, frozen_w_sha256=FROZEN_SHA,
               final_database_state="unique_object_absent")
    elif mode == "candidate" and len(sys.argv) == 4:
        row = load(sys.argv[2]); expected_hash = sys.argv[3]
        envelope(row, "W-T09-candidate", expected_hash)
        try:
            strict(row)
        except (KeyError, TypeError, IndexError):
            fail("candidate_case_shape_invalid")
    else:
        fail("usage")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Aggregate T09 candidate mismatches from one already captured synthetic TEST profile."""

import json
import pathlib
import sys

if len(sys.argv) != 2:
    raise SystemExit("usage: diagnose.py <candidate_profile.json>")
row = json.loads(pathlib.Path(sys.argv[1]).read_text())
cases = row.get("cases", {})
issues = []


def issue(group, name, reason, error=None):
    item = {"contract": group, "case": name, "reason": reason}
    if error: item["exception_kind"] = error
    issues.append(item)


def get(group, name, expected_outer="returned"):
    item = cases.get(group, {}).get(name, {})
    if item.get("outcome") != expected_outer:
        issue(group, name, "outer_outcome_mismatch", item.get("error", {}).get("kind"))
        return {}
    return item.get("observation", {}) if expected_outer == "returned" else item


def known(capture):
    return capture.get("value", {}).get("observation", {}).get("value", {}).get("known_value")


for name, expected in {
    "enum_negative": "-10", "enum_positive": "7", "enum_large": "1000", "flags": "5",
    "byte_underlying": "255", "ulong_underlying": "18446744073709551615",
}.items():
    if known(get("TYP-01", name)) != expected:
        issue("TYP-01", name, "underlying_value_mismatch")

for name, expected in {
    "sbyte_min": "-128", "byte_max": "255", "int16_min": "-32768", "uint16_max": "65535",
    "int32_min": "-2147483648", "int32_max": "2147483647",
    "int64_min": "-9223372036854775808", "int64_max": "9223372036854775807",
    "uint32_max": "4294967295", "uint64_max": "18446744073709551615",
}.items():
    if known(get("TYP-02", name)) != expected:
        issue("TYP-02", name, "integer_boundary_mismatch")
for name, expected in (("explicit_byte_max", "255"), ("explicit_uint16_max", "65535"),
                       ("explicit_uint32_max", "4294967295"), ("explicit_uint64_max", "18446744073709551615")):
    if known(get("TYP-02", name)) != expected:
        issue("TYP-02", name, "explicit_unsigned_max_changed")
for name in ("explicit_byte_256_rejected", "explicit_uint16_negative_rejected", "explicit_uint16_65536_rejected",
             "explicit_uint32_negative_rejected", "explicit_uint64_negative_rejected"):
    x = get("TYP-02", name)
    if (x.get("outcome") != "error" or x.get("error", {}).get("kind") != "System.OverflowException" or
            x.get("business_frame_seen") is not False or x.get("execution_frame_seen") is not False):
        issue("TYP-02", name, "explicit_unsigned_source_range_not_rejected", x.get("error", {}).get("kind"))
for group, name, expected in (("TYP-01", "ulong_enum_explicit_string", "18446744073709551615"),
                              ("TYP-01", "ulong_enum_explicit_string_cast", "18446744073709551615"),
                              ("TYP-02", "ulong_explicit_string", "18446744073709551615"),
                              ("TYP-02", "ulong_explicit_string_cast", "18446744073709551615"),
                              ("TYP-02", "double_explicit_single", "1.5"),
                              ("TYP-02", "double_explicit_single_cast", "1.5")):
    if known(get(group, name)) != expected:
        issue(group, name, "explicit_resolved_type_value_changed")
if get("TYP-02", "nonintegral_getint32").get("getter", {}).get("outcome") != "error":
    issue("TYP-02", "nonintegral_getint32", "noninteger_getter_accepted")
for name in ("nan_input", "infinity_input", "explicit_int32_overflow"):
    x = get("TYP-02", name)
    if x.get("outcome") != "error" or x.get("execution_frame_seen") is not False:
        issue("TYP-02", name, "invalid_numeric_sent_or_accepted", x.get("error", {}).get("kind"))

for name, expected in {
    "decimal38_20": "123456789012345678.12345678901234567890",
    "small_negative": "-0.00000000000000000001",
    "outside_clr_decimal": "999999999999999999.99999999999999999999",
}.items():
    provider = get("TYP-03", name).get("getter", {}).get("provider_specific", {})
    if provider.get("outcome") != "returned" or provider.get("observation", {}).get("known_value") != expected:
        issue("TYP-03", name, "provider_specific_precision_mismatch", provider.get("error", {}).get("kind"))
if get("TYP-03", "outside_clr_decimal").get("getter", {}).get("decimal_getter", {}).get("outcome") != "error":
    issue("TYP-03", "outside_clr_decimal", "clr_decimal_overflow_not_rejected")
for name in ("ado_decimal_positive_tenth", "ado_decimal_negative_tenth",
             "ado_decimal_positive_hundredth", "ado_decimal_negative_hundredth",
             "ado_decimal_positive_1_23", "ado_decimal_negative_1_23"):
    x = get("TYP-03", name)
    if x.get("rows") != 1 or x.get("exact_getdecimal", {}).get("observation") is not True:
        issue("TYP-03", name, "ado_decimal_roundtrip_mismatch")

for name in ("guid_shaped_text", "invalid_getguid"):
    x = get("TYP-04", name)
    if x.get("metadata", {}).get("field_type", {}).get("observation") != "System.String":
        issue("TYP-04", name, "character_column_not_string")
valid = get("TYP-04", "guid_shaped_text").get("getter", {}).get("guid_getter", {})
invalid = get("TYP-04", "invalid_getguid").get("getter", {}).get("guid_getter", {})
if valid.get("outcome") != "returned": issue("TYP-04", "guid_shaped_text", "valid_getguid_failed")
if invalid.get("outcome") != "error": issue("TYP-04", "invalid_getguid", "invalid_getguid_accepted")
nulls = get("TYP-04", "nullable_guid_columns")
if nulls.get("text_null") is not True or nulls.get("binary_null") is not True:
    issue("TYP-04", "nullable_guid_columns", "nullable_column_mismatch")
for name in ("varchar50_guid_d_format", "binary16_guid"):
    x = get("TYP-04", name)
    field = "text_getguid" if name.startswith("varchar") else "binary_getguid"
    if x.get(field, {}).get("outcome") != "returned": issue("TYP-04", name, "valid_column_getguid_failed")
if get("TYP-04", "invalid_binary15_guid").get("binary_getguid", {}).get("outcome") != "error":
    issue("TYP-04", "invalid_binary15_guid", "wrong_length_getguid_accepted")
for name in ("clr_guid_to_varchar50", "clr_guid_to_binary16"):
    x = get("TYP-04", name)
    if (x.get("accepted") is not True or x.get("after", {}).get("is_null") is not False or
            x.get("after", {}).get("get_guid", {}).get("outcome") != "returned"):
        issue("TYP-04", name, "clr_guid_binding_lost", x.get("error", {}).get("kind"))
short = get("TYP-04", "clr_guid_to_binary15_rejected")
if (short.get("accepted") is not False or short.get("unchanged") is not True or
        short.get("error", {}).get("kind") not in ("System.OverflowException", "System.NotSupportedException")):
    issue("TYP-04", "clr_guid_to_binary15_rejected", "guid_capacity_not_rejected",
          short.get("error", {}).get("kind"))

if not row.get("negotiated_charset"):
    issue("TYP-05", "server_profile", "charset_unidentified")
if get("TYP-05", "unicode_roundtrip").get("exact") is not True:
    issue("TYP-05", "unicode_roundtrip", "text_changed")
surrogate = get("TYP-05", "unpaired_surrogate")
if surrogate.get("outcome") != "error" or surrogate.get("execution_frame_seen") is not False:
    issue("TYP-05", "unpaired_surrogate", "bad_text_sent_or_accepted", surrogate.get("error", {}).get("kind"))

timestamp = get("TYP-06", "timestamp_profile").get("getter", {})
if (timestamp.get("get_datetime", {}).get("observation", {}).get("kind") != "Unspecified" or
        timestamp.get("get_datetime", {}).get("observation", {}).get("invariant") != "2024-02-29T12:34:56.1234560"):
    issue("TYP-06", "timestamp_profile", "timestamp_kind_or_precision_mismatch")
interval = get("TYP-06", "interval_profile").get("getter", {}).get("get_string", {})
if interval.get("observation", {}).get("ef_interval_shape_parse") is not True:
    issue("TYP-06", "interval_profile", "ef_interval_text_not_parseable")
for name in ("dateonly_explicit", "timeonly_six_digits", "dto_positive_six_digits",
             "dto_negative_six_digits"):
    x = get("TYP-06", name)
    if x.get("rows") != 1 or x.get("typed", {}).get("outcome") != "returned" or x.get("typed", {}).get("observation") != x.get("expected"):
        issue("TYP-06", name, "typed_temporal_roundtrip_mismatch",
              x.get("typed", {}).get("error", {}).get("kind"))
for name in ("timeonly_seventh_digit_rejected", "dto_seventh_digit_rejected", "interval_timespan_minvalue"):
    x = get("TYP-06", name, "error")
    if x.get("outcome") != "error" or x.get("error", {}).get("kind") not in ("System.OverflowException", "System.NotSupportedException"):
        issue("TYP-06", name, "unrepresentable_time_not_rejected", x.get("error", {}).get("kind"))
for name in ("interval_positive_one_microsecond", "interval_explicit_scale6_bare", "interval_negative_one_microsecond",
             "interval_negative_multiday_fraction"):
    x = get("TYP-06", name)
    if (x.get("rows") != 1 or x.get("typed", {}).get("observation", {}).get("ticks") != x.get("expected_ticks") or
            x.get("get_string", {}).get("observation", {}).get("ef_interval_ticks") != x.get("expected_ticks")):
        issue("TYP-06", name, "interval_sign_or_scale_mismatch", x.get("typed", {}).get("error", {}).get("kind"))

if get("TYP-07", "typed_null").get("is_dbnull", {}).get("observation") is not True:
    issue("TYP-07", "typed_null", "typed_null_not_dbnull")
untyped = get("TYP-07", "untyped_null")
if untyped.get("outcome") != "error" or untyped.get("execution_frame_seen") is not False:
    issue("TYP-07", "untyped_null", "undetermined_null_accepted")
if get("TYP-07", "empty_binary").get("is_dbnull", {}).get("observation") is not False:
    issue("TYP-07", "empty_binary", "empty_binary_became_null")
for name in ("binary_overflow", "varbinary_overflow", "blob_overflow"):
    x = get("TYP-07", name)
    if (x.get("accepted") is not False or x.get("unchanged") is not True or
            x.get("error", {}).get("kind") != "System.OverflowException"):
        issue("TYP-07", name, "binary_overflow_or_final_state_invalid", x.get("error", {}).get("kind"))

duplicate = get("TYP-08", "duplicate_names")
mixed = get("TYP-08", "mixed_named_positional")
if duplicate.get("duplicate_rejected") is not True or duplicate.get("count") != 1:
    issue("TYP-08", "duplicate_names", "normalized_duplicate_accepted")
if mixed.get("rejected") is not True: issue("TYP-08", "mixed_named_positional", "mixed_markers_accepted")
marker = get("TYP-08", "named_marker_ignores_literal_comment")
if marker.get("field_count") != 3 or marker.get("first_parameter") != marker.get("repeated_parameter"):
    issue("TYP-08", "named_marker_ignores_literal_comment", "tokenizer_or_reuse_mismatch")
for name in ("prepare_value_change", "prepare_type_change", "prepare_size_change"):
    if not get("TYP-08", name): issue("TYP-08", name, "prepare_change_failed")
if get("TYP-08", "reset_dbtype").get("after") != "Int64":
    issue("TYP-08", "reset_dbtype", "inference_not_restored")

for name in ("sync_async_text", "sync_async_timestamp"):
    if get("TYP-09", name).get("same_serialized_observation") is not True:
        issue("TYP-09", name, "sync_async_value_mismatch")
parity = get("TYP-09", "sync_async_invalid_guid")
if (parity.get("sync_error", {}).get("outcome") != "error" or
        parity.get("async_error", {}).get("outcome") != "error" or
        parity.get("sync_error", {}).get("error", {}).get("kind") != parity.get("async_error", {}).get("error", {}).get("kind")):
    issue("TYP-09", "sync_async_invalid_guid", "sync_async_exception_mismatch")
clob = get("TYP-09", "lob_text_profile").get("getter", {}).get("get_string", {})
if clob.get("observation", {}).get("text", {}).get("known_value") != "T09_LOB":
    issue("TYP-09", "lob_text_profile", "ef_clob_getstring_mismatch")
if get("TYP-09", "complex_array_rejected").get("outcome") != "error":
    issue("TYP-09", "complex_array_rejected", "unsupported_complex_type_accepted")

for name in ("interval_scale4_small_fraction", "interval_scale3_small_fraction", "interval_scale4_trailing_zero"):
    x = get("TYP-06", name)
    if (x.get("rows") != 1 or x.get("typed", {}).get("observation", {}).get("ticks") != x.get("expected_ticks") or
            x.get("ef_interval_shape_parse") is not True or x.get("ef_interval_ticks") != x.get("expected_ticks")):
        issue("TYP-06", name, "lower_scale_interval_text_changed")
for name, kind in (("typed_null_string", "System.InvalidCastException"),
                   ("typed_nonintegral_int32", "System.InvalidCastException"),
                   ("typed_overflow_int32", "System.OverflowException"),
                   ("typed_highprecision_nonintegral_int32", "System.InvalidCastException")):
    x = get("TYP-09", name)
    if (x.get("rows") != 1 or x.get("same_serialized_observation") is not True or
            any(x.get(mode, {}).get("outcome") != "error" or x.get(mode, {}).get("error", {}).get("kind") != kind
                for mode in ("sync", "async_result"))):
        issue("TYP-09", name, "typed_integer_or_null_error_contract_invalid")
for name, expected in (("typed_uint64_max", "18446744073709551615"), ("typed_null_object", "null")):
    x = get("TYP-09", name)
    if (x.get("rows") != 1 or x.get("same_serialized_observation") is not True or
            any(x.get(mode, {}).get("outcome") != "returned" or
                (x.get(mode, {}).get("observation") if name == "typed_null_object" else
                 x.get(mode, {}).get("observation", {}).get("known_value")) != expected
                for mode in ("sync", "async_result"))):
        issue("TYP-09", name, "typed_returned_value_mismatch")
for name in ("null_untyped_select_metadata", "null_cast_int_select_metadata"):
    x = get("TYP-07", name)
    if (x.get("prepare_succeeded") is not True or x.get("parameter_metadata", {}).get("type_flag") != 2 or
            x.get("execute_succeeded") is not False or
            (x.get("execute_error") or {}).get("kind") != "System.InvalidOperationException" or
            x.get("execution_frame_seen") is not False):
        issue("TYP-07", name, "advisory_null_describe_accepted")
x = get("TYP-07", "null_int32_select_metadata")
if (x.get("prepare_succeeded") is not True or x.get("execute_succeeded") is not True or
        (x.get("result") or {}).get("is_dbnull") is not True):
    issue("TYP-07", "null_int32_select_metadata", "explicit_null_metadata_invalid")
x = get("TYP-07", "null_int_column_insert_metadata")
if (x.get("prepare_succeeded") is not True or x.get("parameter_metadata", {}).get("type_flag") != 1 or
        x.get("execute_succeeded") is not True or x.get("final_state") != {"rows": 1, "int_column_null": True}):
    issue("TYP-07", "null_int_column_insert_metadata", "reliable_column_null_describe_invalid")

null_object = get("TYP-09", "typed_null_object")
if null_object.get("sync_object_is_dbnull") is not True or null_object.get("async_object_is_dbnull") is not True:
    issue("TYP-09", "typed_null_object", "object_getter_lost_dbnull_identity")

null_bindings = []
for name in ("null_untyped_select_metadata", "null_int32_select_metadata",
             "null_cast_int_select_metadata", "null_int_column_insert_metadata"):
    x = get("TYP-07", name)
    result = x.get("result") if isinstance(x.get("result"), dict) else {}
    null_bindings.append({
        "case": name, "ordinal": x.get("ordinal"), "prepare_succeeded": x.get("prepare_succeeded"),
        "prepare_error_kind": (x.get("prepare_error") or {}).get("kind"),
        "metadata": x.get("parameter_metadata"),
        "metadata_after": x.get("parameter_metadata_after"),
        "type_source": x.get("parameter_type_source"),
        "type_source_after": x.get("parameter_type_source_after"),
        "execute_attempted": x.get("execute_attempted"), "execute_succeeded": x.get("execute_succeeded"),
        "execute_error_kind": (x.get("execute_error") or {}).get("kind"),
        "execute_send_delta": x.get("execute_send_delta"),
        "result_field_type": result.get("field_type"), "result_database_type": result.get("database_type"),
        "result_is_dbnull": result.get("is_dbnull"), "final_state": x.get("final_state")
    })

unsigned_bindings = []
for group, name in (("TYP-01", "ulong_enum_bare_metadata"),
                    ("TYP-01", "ulong_enum_cast_decimal_metadata"),
                    ("TYP-02", "ulong_bare_metadata"),
                    ("TYP-02", "ulong_cast_decimal_metadata")):
    x = get(group, name)
    unsigned_bindings.append({"contract": group, "case": name,
        "prepare_succeeded": x.get("prepare_succeeded"),
        "prepare_error_kind": (x.get("prepare_error") or {}).get("kind"),
        "type_source": x.get("parameter_type_source"),
        "type_source_after": x.get("parameter_type_source_after"),
        "metadata_before": x.get("parameter_metadata_before"),
        "metadata_after": x.get("parameter_metadata_after"),
        "execute_succeeded": x.get("execute_succeeded"),
        "execute_error_kind": (x.get("execute_error") or {}).get("kind"),
        "execute_send_delta": x.get("execute_send_delta"),
        "output_field_type": x.get("result", {}).get("field_type", {}).get("observation") if isinstance(x.get("result"), dict) else None,
        "output_database_type": x.get("result", {}).get("database_type", {}).get("observation") if isinstance(x.get("result"), dict) else None,
    })

interval_bindings = []
for name in ("interval_bare_prepare_metadata", "interval_cast_prepare_metadata"):
    x = get("TYP-06", name)
    interval_bindings.append({"case": name, "prepare_succeeded": x.get("prepare_succeeded"),
        "prepare_error_kind": (x.get("prepare_error") or {}).get("kind"),
        "parameter_precision": x.get("parameter_precision"), "parameter_scale": x.get("parameter_scale"),
        "type_source": x.get("type_source"), "metadata_before": x.get("metadata_before"),
        "type_source_after": x.get("type_source_after"),
        "metadata_after": x.get("metadata_after"), "execute_succeeded": x.get("execute_succeeded"),
        "execute_error_kind": (x.get("execute_error") or {}).get("kind"),
        "execute_send_delta": x.get("execute_send_delta")})

print(json.dumps({"schema_version": 1, "task": "T09", "status": "candidate_diagnostic_only",
                  "assembly_sha256": row.get("assembly_sha256"), "server_account_verified": row.get("server_account_verified"),
                  "cleanup_verified": row.get("cleanup_verified"), "final_database_state": row.get("final_database_state"),
                  "issue_count": len(issues), "issues": issues,
                  "null_bindings": null_bindings, "unsigned_bindings": unsigned_bindings,
                  "interval_bindings": interval_bindings},
                 ensure_ascii=False, separators=(",", ":")))

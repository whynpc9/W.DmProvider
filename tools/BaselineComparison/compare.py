#!/usr/bin/env python3
"""Compare sanitized observations from two independently compiled hosts."""
import json
import sys


def fail(kind):
    print(json.dumps({"schema_version": 1, "status": "rejected", "error_kind": kind}))
    return 1


def main():
    if len(sys.argv) != 6 or sys.argv[3] not in ("integration", "offline", "api"):
        return fail("usage")
    try:
        o_exit, r_exit = int(sys.argv[4]), int(sys.argv[5])
    except ValueError:
        return fail("invalid_exit_code")
    try:
        with open(sys.argv[1], encoding="utf-8") as f:
            o = json.load(f)
        with open(sys.argv[2], encoding="utf-8") as f:
            r = json.load(f)
    except (OSError, ValueError):
        return fail("invalid_result_json")
    if o.get("implementation") != "O" or r.get("implementation") != "R":
        return fail("implementation_identity_invalid")
    oa, ra = o.get("asset", {}), r.get("asset", {})
    if not oa or not ra:
        print(json.dumps({"schema_version": 1, "status": "host_rejected", "mode": sys.argv[3],
                          "o_exit": o_exit, "r_exit": r_exit,
                          "o_status": o.get("status"), "r_status": r.get("status"),
                          "o_error_kind": o.get("error_kind"), "r_error_kind": r.get("error_kind"),
                          "o_error_number": o.get("error_number"), "r_error_number": r.get("error_number")}, indent=2))
        return 1
    if oa.get("sha256") == ra.get("sha256") or oa.get("mvid") == ra.get("mvid"):
        return fail("driver_assets_not_distinct")
    if sys.argv[3] == "api":
        oapi, rapi = o.get("public_api"), r.get("public_api")
        if (o_exit or r_exit or not isinstance(oapi, list) or not isinstance(rapi, list)
                or not oapi or not rapi or any(not isinstance(x, str) or not x for x in oapi + rapi)):
            return fail("public_api_invalid")
        left = set(oapi)
        right = set(rapi)
        result = {"schema_version": 1, "status": "equivalent" if left == right else "different",
                  "o_count": len(left), "r_count": len(right),
                  "o_only": sorted(left - right), "r_only": sorted(right - left)}
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0 if left == right else 1
    resources_o = {x["culture"]: x for x in o.get("resources", [])}
    resources_r = {x["culture"]: x for x in r.get("resources", [])}
    cultures = ("neutral", "en", "zh-CN", "zh-HK", "zh-TW")
    resource_results = []
    for culture in cultures:
        x, y = resources_o.get(culture, {}), resources_r.get(culture, {})
        resource_results.append({"culture": culture, "readable_o": x.get("readable", False),
                                 "readable_r": y.get("readable", False),
                                 "value_equal": x.get("value_sha256") is not None and x.get("value_sha256") == y.get("value_sha256"),
                                 "satellite_hash_equal": culture == "neutral" or (x.get("satellite_sha256") is not None and x.get("satellite_sha256") == y.get("satellite_sha256")),
                                 "satellite_o": x.get("satellite_exists", False),
                                 "satellite_r": y.get("satellite_exists", False)})
    result = {"schema_version": 1, "mode": sys.argv[3], "o_status": o.get("status"), "r_status": r.get("status"),
              "o_exit": o_exit, "r_exit": r_exit,
              "o_asset_sha256": oa.get("sha256"), "r_asset_sha256": ra.get("sha256"),
              "o_mvid": oa.get("mvid"), "r_mvid": ra.get("mvid"), "resources": resource_results}
    repair_o, repair_r = o.get("repair_cases"), r.get("repair_cases")
    repair_equal = (isinstance(repair_o, list) and isinstance(repair_r, list) and bool(repair_o) and
                    repair_o == repair_r and all(isinstance(x, dict) and x.get("passed") is True for x in repair_o))
    result["repair_cases"] = {"equal_and_expected": repair_equal,
                              "count_o": len(repair_o) if isinstance(repair_o, list) else 0,
                              "count_r": len(repair_r) if isinstance(repair_r, list) else 0}
    slot_o, slot_r = o.get("structural_slots", {}), r.get("structural_slots", {})
    slots_equal = slot_o == slot_r and slot_o.get("passed") is True and slot_r.get("passed") is True
    result["structural_slots"] = {"equal_and_valid": slots_equal, "o_passed": slot_o.get("passed"), "r_passed": slot_r.get("passed")}
    if sys.argv[3] == "offline":
        od, rd = o.get("known_official_defect", {}), r.get("known_official_defect", {})
        result["known_defect_equal"] = od == rd
        result["status"] = "offline_equivalent" if (o_exit == r_exit == 0 and repair_equal and slots_equal and o.get("status") == r.get("status") == "offline_verified"
                       and od == rd and all(x["readable_o"] and x["readable_r"] and x["value_equal"] and x["satellite_o"] and x["satellite_r"] and x["satellite_hash_equal"] for x in resource_results)) else "different"
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0 if result["status"] == "offline_equivalent" else 1
    oscen = {x["scenario_id"]: x for x in o.get("scenarios", [])}
    rscen = {x["scenario_id"]: x for x in r.get("scenarios", [])}
    required = {"open_close", "parameter_select", "unicode_crud", "transaction_commit_rollback",
                "dml_affected_rows", "dml_generated_key", "failure_then_select", "lob", "known_official_defects"}
    if set(oscen) != required or set(rscen) != required or len(o.get("scenarios", [])) != 9 or len(r.get("scenarios", [])) != 9:
        return fail("scenario_set_invalid")
    scenario_results = []
    for name in sorted(set(oscen) | set(rscen)):
        x, y = oscen.get(name, {}), rscen.get(name, {})
        equal = all(x.get(k) == y.get(k) for k in ("status", "observation", "error_kind", "error_number", "final_row_count", "cleanup_verified", "passed"))
        shared_failure = x.get("status") == y.get("status") == "failed"
        scenario_results.append({"scenario_id": name, "equal": equal, "o_status": x.get("status"),
                                 "r_status": y.get("status"), "shared_failure": shared_failure,
                                 "passed_o": x.get("passed") is True, "passed_r": y.get("passed") is True,
                                 "cleanup_o": x.get("cleanup_verified"), "cleanup_r": y.get("cleanup_verified")})
    behavior_equivalent = (all(x["equal"] and x["cleanup_o"] and x["cleanup_r"] for x in scenario_results)
                           and all(x["readable_o"] and x["readable_r"] and x["value_equal"] and x["satellite_o"]
                                   and x["satellite_r"] and x["satellite_hash_equal"] for x in resource_results))
    correctness = (behavior_equivalent and repair_equal and slots_equal and o_exit == r_exit == 0 and
                   o.get("status") == r.get("status") == "observed" and
                   all(x["passed_o"] and x["passed_r"] and not x["shared_failure"] for x in scenario_results))
    result["scenarios"] = scenario_results
    result["behavior_equivalent"] = behavior_equivalent
    result["status"] = "verified" if correctness else "different_or_failed"
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if correctness else 1


if __name__ == "__main__":
    sys.exit(main())

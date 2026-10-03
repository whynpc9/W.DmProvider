# Third-party and source notices

The product source was derived from the restored C# baseline of `DM.DmProvider` version `8.3.1.47463`, whose NuGet package metadata states `Apache-2.0` and `Copy right(C) DM`. The official assembly SHA-256 and the initial source import hashes are recorded in `docs/compatibility/t03-source-map.json`. T03 modifications comprise namespace and assembly identity migration, explicit resource lookup, and resources rebuilt under the W assembly identity. T04 adds configuration isolation, safe defaults, redaction, and capability guards; subsequent changes and validation hashes are recorded in `docs/implementation/reports/` and `docs/implementation/evidence/`. This notice retains the upstream attribution; it does not assign DM authorship to the W product.

The restored source declares native imports for `dmcalc.dll`, `dmcyt`, `dmfldr`, and Windows `Kernel32.dll`. These are declarations in source, not assets included in the candidate package. The upstream NuGet metadata does not establish separate redistribution permission for native libraries. No native library is bundled by T03.

The candidate's `Apache-2.0` package expression follows the fixed upstream package metadata for the C# source. This notice does not make a legal determination about unreviewed native or external assets. The candidate is not published.

The repository LICENSE contains the unmodified standard Apache License 2.0 text from https://www.apache.org/licenses/LICENSE-2.0.txt. Its exact hash is captured by the R3 source/provenance audit. This supplies the text corresponding to the existing package license expression; it does not change attribution or expand the native asset declarations above.

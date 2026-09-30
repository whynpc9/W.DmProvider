# Baseline parameter cases

`parameter-cases.json` contains deterministic, entirely synthetic, short, non-empty strings for parameter round-trip scenarios. The cases cover ASCII, Chinese text, supplementary-plane emoji, quotes and semicolons, a line break, a combining character, and surrounding spaces. Each `utf8_sha256` is the SHA-256 of that case's exact UTF-8 bytes.

Use these values only through SQL parameter binding; do not interpolate or concatenate them into SQL text. The fixture does not encode assumptions about empty strings, `NULL`, or server-specific semantics. It is test input only and does not show that the official implementation or any provider has passed.

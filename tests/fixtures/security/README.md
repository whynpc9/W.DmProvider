# T07 TLS and native resource scenarios

`scenarios.json` contains synthetic inputs and desired assertions for TLS-01 through TLS-03, NET-04 cleanup, and NAT-01. Each case has `case_id`, `contract`, `execution_status`, `input`, and `expected`; every case remains `not_run`. An expected successful case is a client contract vector, not evidence that this fixture executed a handshake.

## Server configuration and wire mode are separate

Inputs use two distinct fields:

- `server_enable_encrypt` is the controlled server configuration value.
- `negotiated_wire_encrypt` is the STARTUP `Encrypt` integer that the client actually dispatches on.

`mode_catalogue.server_configuration_modes` records the documented server settings. `recovered_wire_mode_constants` records labels from the restored client source and the current W guard policy. The original STARTUP decoder stores `Encrypt` as `ConnProperty.Encrypt`; the protocol and policy separation is documented in [T07-protocol-security](../../../docs/implementation/T07-protocol-security.md#L7).

The owned DM 8.1.4.6 mirror matrix observed configuration-to-wire mappings 1→1, 2→2, 4→4, and 5→4. The configuration-5 TLS smoke completed through the **wire-4** branch. See [the T07 protocol record](../../../docs/implementation/T07-protocol-security.md#L11) and local run `.local/t07/runs/20260929T153725Z-11169-23464/matrix-result.json`. This is an observation from that controlled environment, not a generic protocol guarantee. It is stored under `observed_local_configuration_to_wire_mapping`, outside `cases`; it does not change any case from `not_run`.

Server configuration mode 5 is represented in the TLS positive and negative vectors as `server_enable_encrypt: 5, negotiated_wire_encrypt: 4`. No accepted vector sets `negotiated_wire_encrypt: 5`. A separate `tls_wire5_unsupported` vector exercises the current guard's explicit rejection of wire 5 before TLS or LOGIN. Modes 3, 6, and unknown modes also remain unsupported. The implementation dispatches on `negotiated_wire_encrypt`, as shown in [DmHandshakeSecurityGuard](../../../src/W.DmProvider/Internal/Legacy/A/DmHandshakeSecurityGuard.cs#L20).

## Certificate scenarios

Certificate fields are parameters for the test harness to generate fresh ephemeral test material: trusted or unknown test CA, DNS/IP SAN, EKU, and validity offsets. No certificate, key, private-key password, PFX, credential, or real certificate path is stored here. Use reserved name `db.example.test` and documentation IP `192.0.2.44`. The successful cases require strict server chain and configured-target validation. Negative cases isolate SAN mismatch, unknown CA, expiry, a missing client certificate in the mutual-auth scenario, and a revoked status under Online checking.

`NoCheck` appears only in the explicit configuration case. It relaxes revocation checking for that case; chain and host validation stay enabled. The default case expects Online. `PlaintextAllowed` may permit a mode-0 session, but if the server negotiates full TLS it still rejects an invalid certificate. `RequireTls` rejects wire 0 and AUTH_ONLY wire 2 before LOGIN, and TLS failure does not retry with plaintext.

`expected.login_expected` is a boolean contract marker; it is not a byte count and does not represent an observed LOGIN exchange.

## Native lifecycle scenarios

Native cases describe unsupported RID, relative path, missing library, missing export, initialization failure, and idempotent release. Paths under `/fixture/` are placeholders for harness-controlled files. The harness may create and remove a fake library needed by an isolated test; this fixture contains no binary. Missing export and initialization failure expect one release for one acquired handle; failed library lookup expects no acquired handle.

These vectors do not prove DM server-mode behavior or platform-native interoperability. T07 acceptance still requires separately reported commands, exit codes, environment identity, and evidence level.

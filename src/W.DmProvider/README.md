# W.DmProvider T11 development source

This .NET 10 source establishes an independent assembly and namespace from the restored DM.DmProvider 8.3.1.47463 baseline. It provides parameter precision, local transactions, and isolated ownership of user and isolation-control statements. ReadCommitted is the default. Public ReadUncommitted and Serializable entry points are gated to server version 8.1.5.60. This is development source, not a supported production release.

Consult [the T11 report](../../docs/implementation/reports/T11.md) and the exact candidate manifest referenced there for validation status, source identity, package version, and package/loaded DLL hashes. [Task progress](../../docs/implementation/progress.json) records the delivery gates.

RequireTls accepts only verified, full-session TLS. Authorized non-TLS tests must explicitly select PlaintextAllowed. Native cipher interoperability, the full downstream EF suite, and R1 package handoff have separate acceptance gates. Real asynchronous I/O, command cancellation, pooling, and streaming are not declared capabilities of this candidate.

The default development version is `0.1.0-t11`. Verification packs use a unique candidate version supplied by the validation command.

The primary ADO.NET types are in `W.Dm`. This is a source-level port, not a binary replacement for `DM.DmProvider`. See `THIRD-PARTY-NOTICES.md` for provenance and native dependency declarations.

`GetSchema("Tables")` and `GetSchema("Columns")` use the visible `ALL_OBJECTS` and `ALL_TAB_COLUMNS` dictionary views under the current account. Table fill factor, space limit, and row count are returned as `DBNull` because those values are not available from this path. Column size is also `DBNull`: dictionary `DATA_LENGTH` measures storage bytes and cannot safely replace the legacy logical size calculation. Other schema collections retain legacy queries and have not been validated for a least-privilege account in T05.

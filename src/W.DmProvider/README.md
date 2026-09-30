# W.DmProvider R1 development source

This .NET 10 source establishes an independent assembly and namespace from the restored DM.DmProvider 8.3.1.47463 baseline. It provides parameter precision, local transactions, and isolated ownership of user and isolation-control statements. ReadCommitted is the default. Public ReadUncommitted and Serializable entry points are gated to server version 8.1.5.60. This is development source, not a supported production release.

Consult [the R1 report](../../docs/implementation/reports/T12.md), [PR review corrections](../../docs/implementation/reports/R1-review.md), and their exact candidate manifests for validation status, source identity, package version, and package/loaded DLL hashes. [Task progress](../../docs/implementation/progress.json) records the delivery gates.

RequireTls accepts only verified, full-session TLS. Authorized non-TLS tests must explicitly select PlaintextAllowed. Native cipher interoperability and downstream EF acceptance have separate evidence records. Real asynchronous I/O, command cancellation, pooling, and streaming are not declared capabilities of this candidate.

Verification packs use a unique candidate version supplied by the validation command; the package version and loaded DLL hash must match the relevant report.

The primary ADO.NET types are in `W.Dm`. This is a source-level port, not a binary replacement for `DM.DmProvider`. See `THIRD-PARTY-NOTICES.md` for provenance and native dependency declarations.

`GetSchema("Tables")` and `GetSchema("Columns")` use the visible `ALL_OBJECTS` and `ALL_TAB_COLUMNS` dictionary views under the current account. Table fill factor, space limit, and row count are returned as `DBNull` because those values are not available from this path. Column size is also `DBNull`: dictionary `DATA_LENGTH` measures storage bytes and cannot safely replace the legacy logical size calculation. Other schema collections retain legacy queries and have not been validated for a least-privilege account in T05.

R1 uses non-nested block-comment parsing. Typed integer getters reject SQL NULL; callers should use IsDBNull before reading nullable values. CLOB GetChars supports null destination length probes within the documented materialization limit. Assigning a command connection to null detaches it after releasing its own statement; clear an active explicit command transaction first, and close active readers before changing the connection.

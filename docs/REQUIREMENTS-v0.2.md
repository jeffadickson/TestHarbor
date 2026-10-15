# TestHarbor 0.2 requirements

Scope: extend the NUnit companion through ordinary public hooks. No NUnit internals,
replacement engine, or custom assertion implementation.

| ID | Requirement | Acceptance |
|---|---|---|
| EXT-001 | Resolve ordered ITestExtension instances from the per-test DI scope | Registration order for start; reverse order for stop |
| EXT-002 | Roll back partially initialized extensions | Stop also called on the extension whose StartAsync throws |
| EXT-003 | Attempt every entered extension stop | One failure must not suppress later stops or service cleanup |
| EXT-004 | Preserve errors from setup/body and cleanup | Aggregate only when multiple phases fail |
| EXT-005 | Cleanup ignores the cancelled start token | Stop has no cancellation parameter; owner awaits it |
| RES-006 | Quarantine all session resources after extension/service cleanup failure by default | Quarantine established before ownership released |
| RES-007 | Reject existing queued and new requests touching quarantined resources | Fail promptly, including requests behind blocked FIFO head |
| RES-008 | Explicit recovery with a nonempty reason | Reject reset while resource still held; no automatic unquarantine |
| RES-009 | Retain ordinary test failures without automatic quarantine | Successful cleanup permits reuse after assertion failure |
| OBS-001 | Public immutable snapshots of held/waiting/quarantined resources | Include wait count, owner IDs and reasons |
| OBS-002 | Bounded event history | 256 events maximum; queued/granted/cancelled/released/quarantine/reset; elapsed time |
| NUNIT-004 | Quarantine on a derived teardown failure visible through public TestContext | Probe actual runner behavior |
| NUNIT-005 | Label sessions with NUnit test ID/name and emit event diagnostics | Public TestContext.Progress output |
| LIFE-001 | Concurrent DisposeAsync callers join the same cleanup task | All observe same failure or completion |
| DOC-002 | Document public APIs, migration and concrete example | XML documentation warnings fail build |

Deferred: method resource attributes; fixture/run DI scopes; runtime test trees;
cross-process broker; external recovery authorization and device-specific safety policies.
Quarantine is local process state, not a durable safety interlock. Existing shared readers
are not forcibly stopped. Microsoft DI owns service disposal behavior within each scope.

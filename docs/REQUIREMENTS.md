# Requirements — v0.1

| ID | Requirement | Initial status |
|---|---|---|
| ARCH-001 | Preserve unmodified NUnit discovery, execution, assertions and reporting | Implemented |
| ARCH-002 | No NUnit internal namespaces, internal reflection or custom engine | Implemented |
| RES-001 | Named exclusive/shared-read access, case-sensitive identifiers | Implemented |
| RES-002 | Atomic acquisition of sets; duplicate names use strongest access | Implemented |
| RES-003 | FIFO waiting and cancellable acquisition without leaked ownership | Implemented/tested |
| RES-004 | Retain ownership through async cleanup | Implemented/tested |
| RES-005 | Cross-process resource leases with fencing and recovery | Deferred |
| RES-006 | Faulted-resource quarantine after unsafe cleanup | Implemented in 0.2 |
| DI-001 | Per-test Microsoft DI scope, explicit service resolution | Implemented |
| DI-002 | Await async scope and root disposal before lease release | Implemented |
| DI-003 | Preserve body and cleanup failures in explicit facade | Implemented/tested |
| DI-004 | Fixture/run scopes with explicit ownership | Planned |
| NUNIT-001 | Base setup precedes user setup; base teardown follows user teardown | Implemented/tested |
| NUNIT-002 | Per-test fixture instances for parallel safety | Implemented; callers must retain lifecycle |
| NUNIT-003 | Cleanup on setup, assertion, teardown and disposal failure | Verified by explicit failure probes |
| EXT-001 | Ordered composable extensions with documented failure semantics | Implemented in 0.2 |
| DATA-001 | Reuse NUnit TestCaseSource for discoverable cases | Existing NUnit capability |
| DATA-002 | Runtime-generated individually reported trees | Research; no promise under wrapper constraint |
| DOC-001 | XML summaries on public API and generated HTML reference | Implemented |
| BUILD-001 | Locked dependencies, SDK selection, cross-platform CI definition | Implemented |

Acceptance scope is in-process cooperative execution. It excludes aborts, process crashes,
unsafe unawaited test work, and user code that bypasses the coordinator.

# End-of-run ownership audit

A test can pass its assertions but forget to dispose a manually opened session. NUnit
cannot infer that an application-specific resource lease should have ended. TestHarbor
can now fail an explicit suite boundary with the owner, resource access modes, held
and waiting durations. This adds an ownership policy to NUnit's normal failure reporting.

```csharp
/// <summary>Audits this suite after all its work and cleanup have been joined.</summary>
[OneTimeTearDown]
public void AuditOwnership()
{
    TestContext.Progress.WriteLine(coordinator.GetOwnershipAudit());
    coordinator.ThrowIfNotIdle();
}
```

Use a coordinator dedicated to this suite and pass it to every session/acquisition.
The example assumes a fixture field named `coordinator`. Run this boundary only after
all work using that coordinator has finished, including asynchronous cleanup. With
parallel fixtures sharing a coordinator, place the audit at their common enclosing
suite teardown, not each fixture's teardown. Injection fixtures reject one-time hooks;
use an enclosing NUnit setup fixture or a separate harness boundary for those suites.
The audit does not automatically install itself into NUnit.

`GetOwnershipAudit()` captures active leases, FIFO waiters and quarantine under one lock.
It includes sessions with no named resources and preserves duplicate owner labels as
separate entries. Collections returned by the coordinator are detached and read-only.
Durations use a monotonic clock; `CapturedAt` is UTC. The report can be serialized with
`System.Text.Json.JsonSerializer.Serialize(audit)` for retained machine-readable evidence.

`ThrowIfNotIdle()` throws `OutstandingOwnershipException`, whose `Audit` property retains
the exact snapshot used for the decision. It never releases leases, cancels requests or
clears quarantine. Diagnose the named owner and join its real cleanup before retrying.
A released quarantined resource is idle but still requires explicit recovery before reuse.

This is a point-in-time observation of one process-local coordinator, not a shutdown
barrier, background-task detector, service disposal detector or cross-process broker
audit. A live owner is only a leak if your chosen boundary requires it to have finished.
New acquisitions can begin after the snapshot; callers must establish quiescence.

## Requirements and completed tasks — TH-028 / issue #29

- [x] Atomically capture active, waiting and quarantined state with owner/resource/duration evidence.
- [x] Include empty-resource leases and preserve distinct acquisitions with duplicate labels.
- [x] Offer an explicit idle assertion retaining the failed snapshot.
- [x] Keep ownership and queued work intact; distinguish quarantine from outstanding ownership.
- [x] Test detached immutable collections, FIFO waiters, empty sessions, quarantine and recovery.
- [x] XML-document the API and tests; explain NUnit teardown placement and scope limitations.

Executable evidence: `tests/TestHarbor.Tests/OwnershipAuditTests.cs` contains four passing
regression cases, including an intentionally forgotten session caught by the assertion
and then explicitly disposed. No ownership is forcibly revoked to make a test pass.

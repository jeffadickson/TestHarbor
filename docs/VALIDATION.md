# Validation — 2026-10-15

Environment: Linux, .NET SDK 10.0.402, .NET runtime 10.0.13, NUnit 5.0.0,
NUnit3TestAdapter 6.1.0, Microsoft.NET.Test.Sdk 17.14.1.

- Release build: zero warnings, zero errors; public XML documentation required.
- Normal NUnit run: 27 core/integration cases plus 2 example cases passed.
- Explicit failure verifier: 4 expected failures correctly identified (setup, body,
  teardown, async service cleanup). Each recorded cleanup while retaining its resource;
  fixture completion verified quarantine for teardown/cleanup failures, explicitly recovered those
  simulated resources, and reacquired every resource successfully.
- Resource tests cover shared readers, exclusive writers, unrelated resources, duplicate
  normalization, atomic multi-resource requests, FIFO cancellation, 100 concurrent writers,
  and 100 cancellation/grant races with reacquisition checks.
- Async cleanup blocking test verifies a waiter cannot enter until disposal completes.
- Scoped-disposal failure still attempts root singleton cleanup.
- Sources contain no NUnit internal namespace imports or reflection into NUnit.
- Generated HTML API reference includes all documented public member entries.

GitHub Actions run [38805161497](https://github.com/jeffadickson/TestHarbor/actions/runs/38805161497)
passed on ubuntu-latest, windows-latest and macos-latest for implementation commit
`bfcd52159931920e7b223c583cfa77fdd4902914`. Each job ran locked restore, the normal suite,
the four deliberate failure probes, HTML documentation generation and both package builds. Runtime-generated test reporting, cross-process leases, and hardware
safety behavior are not implemented or validated.

0.2 adds checks for partial-start rollback, reverse extension cleanup, aggregation of all stop
errors, cleanup despite cancellation, joined concurrent disposal, quarantine of queued and new
requests, recovery guards, already-held shared readers, detached diagnostics and bounded history.

# Validation — 2026-10-15

Environment: Linux, .NET SDK 10.0.402, .NET runtime 10.0.13, NUnit 5.0.0,
NUnit3TestAdapter 6.1.0, Microsoft.NET.Test.Sdk 17.14.1.

- Release build: zero warnings, zero errors; XML documentation generated for every C# project.
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
- Generated HTML API reference includes public and private members, tests, examples and tooling (200 compiler XML entries).

GitHub Actions run [38805161497](https://github.com/jeffadickson/TestHarbor/actions/runs/38805161497)
passed on ubuntu-latest, windows-latest and macos-latest for implementation commit
`bfcd52159931920e7b223c583cfa77fdd4902914`. Each job ran locked restore, the normal suite,
the four deliberate failure probes, HTML documentation generation and both package builds. Runtime-generated test reporting, cross-process leases, and hardware
safety behavior are not implemented or validated.

0.2 adds checks for partial-start rollback, reverse extension cleanup, aggregation of all stop
errors, cleanup despite cancellation, joined concurrent disposal, quarantine of queued and new
requests, recovery guards, already-held shared readers, detached diagnostics and bounded history.

## Gap-analysis and xdoc update

The 56-capability matrix maps to 16 requirements and GitHub tasks. Local Release validation passed with zero compiler warnings/errors, 29 normal tests, four independently verified intentional failures, and locked restore. The Roslyn audit checked 160 authored declarations with zero failures; five negative documentation probes and one positive probe passed. The compiler produces 200 reference entries, including synthesized record members, so that count differs from authored declarations.

The workflow now explicitly builds all projects, runs the documentation audit and its self-tests on all three operating systems, and generates the expanded reference. The run linked above is previous runtime validation; the Actions run for this documentation commit supplies its own CI evidence. New runtime requirements remain planned.

## 0.2.1 declarative-resource validation

Release build passed with zero warnings/errors. The suite now passes 34 library/integration tests and two example cases. Seven new real-runner cases cover method overload isolation, generic instantiations, inherited class/method metadata, explicit/declarative duplicate merging and invalid declarations. The existing parallel lifecycle fixture now uses a declarative lock and still verifies ownership through async service disposal; the supply example uses the same path through extension cleanup. All four deliberate failure probes still pass verification.

XML coverage: 178 authored declarations with zero failures; the expanded HTML reference is regenerated. JG-001 / TH-007 is implemented; scheduler-level resource scheduling remains outside the wrapper contract.

## 0.3 release-candidate evidence

Local .NET 10 Release validation completed with zero warnings/errors. The normal suite passes 56 tests plus ten runnable example cases. The XML audit covers 425 authored declarations with zero gaps. The HTML reference covers all seven C# projects.

External verifiers additionally check four intentional lifecycle failures, nine conformance scenarios (including conditions before acquisition), stable filtered source-case replay, filtered shared-fixture ownership, four retained attachments in both TRX and NUnit XML, and cross-process broker exclusion/fencing/crash recovery/heartbeat-expiry quarantine. All three NuGet packages include XML documentation and a clean temporary consumer restores and executes them.

Compatibility is limited to the pinned NUnit/.NET baseline and documented wrapper semantics. The new CI workflow runs these gates on Linux, Windows and macOS. The Actions run associated with the 0.3 commit supplies its platform evidence; prior run links above are historical validation only. See WRAPPER-BOUNDARIES.md for explicit feasibility decisions and single-host broker limits.

The first 0.3 CI run exposed macOS returning errno 35 for a contended exclusive open. The broker now treats that platform-specific EWOULDBLOCK as contention while allowing unrelated I/O failures to propagate. Documentation generation explicitly writes UTF-8 on every platform. Windows also exposed CLI source parsing treating the NuGet URL as a local path; the clean-consumer verifier now supplies an explicit NuGet.Config. The follow-up CI run is required to verify these platform fixes.

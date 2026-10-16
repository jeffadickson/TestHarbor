# Validation

## Current implementation — 2026-10-16

Five collection injection cases verify order, scoped identity, transient lifetimes, empty
collections and parallel isolation. Two collection failure probes verify partial-graph cleanup.

Six original explicit injection diagnostic probes cover nested missing dependencies,
cycles, factory exceptions/nulls/skips and constructor graph failures with scope cleanup.

Eight software-only checkout comparison cases now verify transitive constructor/method
injection, parallel case isolation, rejected-order side effects and stable filtered replay.

Four ownership-audit regressions verify empty-resource sessions, retained FIFO waiters,
detached read-only evidence, duplicate owner labels and quarantine-only idle state.
See [ownership audit](OWNERSHIP-AUDIT.md).

The normal suite now has 118 passing cases (60 library and 58 examples). The optional injection
package adds eight normal cases and an external verifier with discovery-only activation guards,
filtered identity replay, ten fresh attempt scopes, seventeen deliberate failure/skip/cancellation
cases and three invalid declarations. Exact lifecycle streams and post-release quarantine states
are checked. The clean package consumer now runs an injected NUnit test from the four packages.
See [injection evidence and IDE limitations](NUNIT-INJECTION.md). Historical sections below record
what was true at each earlier milestone; their counts and unimplemented-feature statements are
not the current project status.

## Historical validation — 2026-10-15

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

Local .NET 10 Release validation completed with zero warnings/errors. The normal suite passes 56 tests plus ten runnable example cases. The XML audit covers 426 authored declarations with zero gaps. The HTML reference covers all seven C# projects.

External verifiers additionally check four intentional lifecycle failures, ten conformance scenarios (including conditions before acquisition and NUnit CancelAfter with failing cleanup), stable filtered source-case replay, filtered shared-fixture ownership, four retained attachments in both TRX and NUnit XML, and cross-process broker exclusion/fencing/crash recovery/heartbeat-expiry quarantine. All three NuGet packages include XML documentation and a clean temporary consumer restores and executes them.

Compatibility is limited to the pinned NUnit/.NET baseline and documented wrapper semantics. The new CI workflow runs these gates on Linux, Windows and macOS. The Actions run associated with the 0.3 commit supplies its platform evidence; prior run links above are historical validation only. See WRAPPER-BOUNDARIES.md for explicit feasibility decisions and single-host broker limits.

The first 0.3 CI run exposed macOS returning errno 35 for a contended exclusive open. The broker now treats that platform-specific EWOULDBLOCK as contention while allowing unrelated I/O failures to propagate. Documentation generation explicitly writes UTF-8 on every platform. Windows also exposed CLI source parsing treating the NuGet URL as a local path; the clean-consumer verifier now supplies an explicit NuGet.Config. The follow-up CI run is required to verify these platform fixes.

## Unified contributor verification

`python tools/verify.py` executes the full required gate sequence and is also the CI entry point.
It uses locked restore, Release configuration, serialized MSBuild and disabled shared compilation
consistently across environments. `--doctor` checks SDK resolution without changing build outputs.
Package creation remains local; this command does not publish a release or upload to NuGet.

The completed 0.3 backlog was validated by [run 38817174641](https://github.com/jeffadickson/TestHarbor/actions/runs/38817174641)
on commit `d275e657694ee5630ef4411433ca446afb5cb4a5`, with all three platforms passing.
Issue #17 tracks validation of the unified runner itself.

## Expanded bench acceptance suite

Eight additional normal example cases exercise a shared supply/load simulation: three
measurement points with retained CSV evidence, two contention sizes (2 and 32 sessions),
paused async shutdown with a waiting client, cancelled acquisition, and combined body/cleanup
failure followed by blocked reuse and explicit recovery. Device events and active-client counts
provide evidence independent of the coordinator snapshots. Barriers coordinate the cleanup
scenario without timing-based sleeps. Each case owns its simulation and coordinator, so NUnit
can run the cases in parallel without shared test state.

The expanded normal suite contains 56 library tests plus 18 example cases (74 total).
Issue #18 records the corresponding full verification and three-platform CI results.

## Charger authoring comparison

Six additional normal cases run the same simulated driver and load-regulation requirement through
plain NUnit manual ownership and a TestHarbor fixture with named resources and a reusable profile.
The two versions own separate physical simulations; they do not pretend different coordination
mechanisms share ownership. The example distinguishes basic manual cleanup from TestHarbor's
quarantine policy rather than claiming identical failure guarantees.

The normal suite now contains 56 library tests plus 24 example cases (80 total).
Issue #19 records the corresponding three-platform CI evidence.

## Software-only examples

Seven cases add isolated shopping-cart DI scopes, shared catalog reset lifecycles, and real
JSON-to-text invoice exports with success cleanup and failed-input retention. The catalog is
an in-memory stand-in, not evidence of database-driver compatibility. Three export cases attach
retained text and provenance files. These cases require no external services or equipment.

The normal suite now contains 56 library tests plus 31 example cases (87 total).
Issue #20 records the corresponding full verification and three-platform CI results.

## Real SQLite integration

Four additional software cases exercise file-backed SQLite committed writes, parameterized
customer data, rollback after a real duplicate-primary-key error, and connection closure before
workspace deletion. New connections verify persisted state after session disposal. SQLite
dependencies are example-only and locked. The normal suite now contains 56 library tests plus
35 example cases (91 total). Issue #21 records validation and platform results.

## Cleanup contamination comparison

`tools/verify_cleanup_comparison.py` runs two actual NUnit fixtures (manual semaphore and
TestHarbor session), each with ordered write, subsequent read and recovery steps. It expects
four failures and two passes and verifies exact independent event files. The plain path reaches
the next body and reads stale data; the TestHarbor path reports quarantine before executing it.
Both recoveries verify zero rows. All six cases are Explicit; normal case counts remain 91.
Retained TRX/events/logs under TestResults are included in CI artifacts. Issue #22 tracks validation.

The comparison now uses file-backed SQLite. A trigger rejects DELETE with a real SqliteException;
a fresh connection verifies the committed row survived. The verifier requires that exception and
message in both first-test results, persisted-row evidence in both event streams, the stale read
only in the plain path, and both successful recoveries. No external service or timing race is
required. Issue #23 tracks this stronger database-backed proof; normal case counts remain 91.

## Queued export consumers

Two normal example cases prove a consumer already queued during asynchronous cleanup receives
complete file output after success or a quarantine error after failure. Task barriers and ownership
snapshots establish the interleaving without sleeps. Both verify unrelated resources remain usable;
the failure case also verifies no queued consumer body ran and no ownership/waiter remains.
The normal suite now contains 56 library tests and 37 example cases (93 total). Issue #24 tracks
local and three-platform CI verification.

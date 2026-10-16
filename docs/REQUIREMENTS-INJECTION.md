# Opt-in NUnit dependency injection — proposed requirements v0.1

Status: approved direction; implementation and feasibility evidence pending. This is follow-on
work to 0.3, not functionality already shipped. Proposed package: `TestHarbor.NUnit.Injection`.

## Purpose and architecture decision

Test authors should declare dependencies in constructor and method signatures. NUnit continues
to discover, select, schedule and report tests; TestHarbor owns runtime dependency resolution,
resource acquisition and asynchronous cleanup. No IDE-specific plugin should be required.

The user approved investigating a narrow exception to the original blanket prohibition on
`NUnit.Framework.Internal` references. The optional injection package may use documented NUnit
extension contracts and the implementation types needed by those contracts, subject to an explicit
reviewed allowlist and pinned compatibility tests. Existing packages retain their original boundary.
This does not authorize a fork, modified NUnit binaries, private-member reflection, a replacement
runner/adapter, or arbitrary use of NUnit internals. The current boundary checker remains unchanged
until an implementation supplies the precise allowlist and negative checks.

Relevant documented extension contracts:
- [IFixtureBuilder](https://docs.nunit.org/api/NUnit.Framework.Interfaces.IFixtureBuilder.html)
- [ITestBuilder](https://docs.nunit.org/api/NUnit.Framework.Interfaces.ITestBuilder.html)
- [IWrapTestMethod](https://docs.nunit.org/api/NUnit.Framework.Interfaces.IWrapTestMethod.html)

These are candidate integration points, not proof of complete constructor-injection support.
Discovery-time fixture building and execution-time object construction are distinct concerns.

## Requirements and acceptance criteria

### INJ-001 — Prove the extension route before committing to an API

P0; TH-024, [issue #25](https://github.com/jeffadickson/TestHarbor/issues/25).

- Demonstrate both constructor injection and mixed case-data/service method injection through the
  real pinned NUnit runner and adapter. Record exact extension interfaces/types and lifecycle order.
- Discovery-only execution must not resolve services, acquire resources, open connections, or invoke
  test constructors. Registration metadata evaluation must be documented and side-effect-free.
- Show that NUnit reports one independently selectable result for each declared case.
- Prove asynchronous test execution remains awaited with NUnit assertion, skip, inconclusive and
  cancellation semantics preserved; reject unsupported return types explicitly.
- If constructor construction cannot be controlled within the approved seam, record that result;
  do not silently introduce a fork or claim method-only injection satisfies this requirement.

### INJ-002 — Isolate compatibility-sensitive integration

P0; TH-024, [issue #25](https://github.com/jeffadickson/TestHarbor/issues/25).

- Keep all implementation-type dependencies in the optional integration package. Core resource and
  service ownership must remain independent of NUnit implementation types.
- Enumerate and narrowly allowlist required symbols; retain negative checks against forbidden
  namespaces outside that package and forbidden reflection throughout the repository.
- Pin the supported NUnit/adapter baseline, fail or diagnose incompatible versions clearly, and
  require regression evidence before widening support. No silent fallback to ordinary binding.
- Loading or referencing the package alone must not alter existing fixtures.

### INJ-003 — Explicit and unambiguous dependency binding

P1; TH-025, [issue #26](https://github.com/jeffadickson/TestHarbor/issues/26).

- Use opt-in fixture/test attributes; final names are subject to the feasibility result.
- Initial constructor policy: exactly one public constructor, with all constructor parameters
  resolved as services. Diagnose multiple public constructors and unsupported fixture-data mixing.
- Mark service-bound method parameters explicitly, provisionally `[FromServices]`. All remaining
  parameters consume ordinary case data in declaration order; never guess based on type matching.
- Diagnose missing/ambiguous services, case-data arity/type errors and unsupported signatures with
  fixture, method and parameter identity, without logging service values or secrets.
- Specify behavior for optional parameters, null data, generic methods, ref/out parameters and
  inherited tests. Support each deliberately or reject it during discovery with an actionable reason.
- Define how NUnit TestCase/TestCaseSource interact with injection attributes; prevent duplicate
  discovery when attributes are combined. Preserve stable names/IDs independent of resolved objects.

### INJ-004 — Own the complete runtime lifecycle

P1; TH-025, [issue #26](https://github.com/jeffadickson/TestHarbor/issues/26).

- Required ordering: evaluate conditions; acquire all declared resources; create service scope;
  initialize required extensions; resolve dependencies and construct the fixture; execute supported
  NUnit setup/body/teardown; dispose the fixture and session-owned services; release resources.
  Record and justify the exact fixture-versus-extension cleanup ordering in the design.
- Per-test services are available to constructors, setup, injected methods and teardown through the
  same attempt-local scope. No global service locator or discovery-created service instances.
- Initial mode creates a fresh fixture and scope per case and per retry/repeat attempt. Reject
  conflicting SingleInstance lifetimes. Specify supported OneTimeSetUp/OneTimeTearDown behavior;
  never expose per-test services through one-time lifecycle hooks.
- On constructor/startup/setup failure, clean every successfully initialized owned object exactly
  once; do not run hooks on an object whose constructor did not complete.
- Await async disposal, attempt remaining cleanup after a failure, preserve primary and cleanup
  exceptions, and quarantine affected resources before allowing later or queued acquisition.
- Cancellation cannot revoke an active lease or cancel required cleanup. Acquisition cancellation
  must leave no services or fixture objects created. Users remain responsible for joining child work.

### INJ-005 — Prove lifecycle conformance

P1; TH-025, [issue #26](https://github.com/jeffadickson/TestHarbor/issues/26).

- Real-runner probes cover constructor, setup, body, teardown and disposal failures; partial startup;
  cancellation; repeat/retry; parallel cases; filtered execution; inherited fixtures and data cases.
- Verify per-attempt identity, reverse cleanup where specified, no double disposal, retained leases
  during async cleanup, and quarantine after cleanup failure.
- Verify missing/ambiguous dependencies and invalid signatures fail clearly rather than disappear
  from discovery. Existing non-injected suites must retain their behavior and case counts.
- Check final external NUnit/TRX outcomes, not only internally observed session snapshots.

### INJ-006 — CLI and IDE interoperability are separate evidence

P1; TH-026, [issue #27](https://github.com/jeffadickson/TestHarbor/issues/27).

IDE means integrated development environment: the editor, debugger and test explorer used by the
test author. Target environments are Visual Studio on Windows, JetBrains Rider, and VS Code with
its C# testing tooling. These are validation targets, not current compatibility promises.

- Automated CLI gates verify list-tests without activation, run-all, single-case filtering,
  deterministic rerun IDs, categories, parameterized names, attachments and final results.
- In each target IDE, record exact IDE/runtime/extension/adapter versions and check discovery,
  running one selected case, rerun, source navigation, constructor/body breakpoints and async stacks.
- Record unsupported or untested combinations explicitly. Three-platform CLI CI does not establish
  IDE test-explorer or debugger compatibility. Document any required adapter/tool configuration.
- Use the existing NUnit runner/adapter; no custom IDE plugin or test adapter is part of this scope.

### INJ-007 — Deliver an optional, documented package

P1; TH-027, [issue #28](https://github.com/jeffadickson/TestHarbor/issues/28).

- Ship only implemented and verified features in a separately referenced experimental package.
- Add clean package-consumer verification, locked dependencies, compatibility/migration notes, and
  generated API reference with XML documentation on every authored/generated C# declaration.
- Provide a software example with constructor injection and mixed data/service parameters, plus
  a failure example explaining scope cleanup and quarantine. Clearly distinguish proposed syntax.
- Run existing and new verification gates on Linux, Windows and macOS for every implementation
  commit; maintain a separate explicit checklist for IDE checks that CI cannot exercise.

## Task sequence

| Task | Deliverable | Status |
|---|---|---|
| TH-024 / #25 | Runtime feasibility and precise extension boundary | Open |
| TH-025 / #26 | Binding, ownership and conformance implementation | Blocked by TH-024 |
| TH-026 / #27 | Adapter and IDE compatibility evidence | Follows working prototype; required before support claims |
| TH-027 / #28 | Optional package and documentation | Blocked by implementation and declared compatibility evidence |

The 0.3 explicit `ServiceInvocation` API remains supported. JG-008's earlier resolution is valid
for the original architecture; this proposal reopens transparent injection under the newly approved
narrow extension boundary. Runtime-generated test trees and multi-host coordination are not added
by this decision.

# TestHarbor next-release requirements and tasks

These requirements follow the [56-capability analysis](JUNIT-GAP-ANALYSIS.md). “v0.3” names the planning document, not a claim that all work is implemented. Existing v0.1/v0.2 requirements remain applicable. P0 is the current documentation gate; P1 is foundational delivery; P2 is ergonomics; P3 is later feasibility or distributed infrastructure.

## Task index

| Requirement | Priority | Task | Status |
|---|---|---|---|
| [JG-001 Declarative resource requirements](#jg-001) | P1 | [#1](https://github.com/jeffadickson/TestHarbor/issues/1) | planned |
| [JG-002 Fixture and run service scopes](#jg-002) | P1 | [#2](https://github.com/jeffadickson/TestHarbor/issues/2) | planned |
| [JG-003 Reusable extension profiles and registration](#jg-003) | P2 | [#5](https://github.com/jeffadickson/TestHarbor/issues/5) | planned |
| [JG-004 Lifecycle phase and outcome contracts](#jg-004) | P1 | [#6](https://github.com/jeffadickson/TestHarbor/issues/6) | planned |
| [JG-005 Conditional execution and reproducible configuration](#jg-005) | P1 | [#7](https://github.com/jeffadickson/TestHarbor/issues/7) | planned |
| [JG-006 Temporary directories and owned-resource cleanup](#jg-006) | P1 | [#8](https://github.com/jeffadickson/TestHarbor/issues/8) | planned |
| [JG-007 Typed data sources and invocation templates](#jg-007) | P2 | [#9](https://github.com/jeffadickson/TestHarbor/issues/9) | planned |
| [JG-008 Typed service injection feasibility](#jg-008) | P2 | [#10](https://github.com/jeffadickson/TestHarbor/issues/10) | planned |
| [JG-009 Nested contexts and reusable contract suites](#jg-009) | P2 | [#11](https://github.com/jeffadickson/TestHarbor/issues/11) | planned |
| [JG-010 Cancellation budgets and lifecycle misuse guards](#jg-010) | P1 | [#12](https://github.com/jeffadickson/TestHarbor/issues/12) | planned |
| [JG-011 Artifacts and structured result evidence](#jg-011) | P1 | [#13](https://github.com/jeffadickson/TestHarbor/issues/13) | planned |
| [JG-012 Extension conformance test kit](#jg-012) | P1 | [#14](https://github.com/jeffadickson/TestHarbor/issues/14) | planned |
| [JG-013 Runtime-generated test discovery/reporting feasibility](#jg-013) | P3 | [#4](https://github.com/jeffadickson/TestHarbor/issues/4) | planned |
| [JG-014 Compatibility and API stability policy](#jg-014) | P1 | [#15](https://github.com/jeffadickson/TestHarbor/issues/15) | planned |
| [JG-015 Cross-process resource broker](#jg-015) | P3 | [#3](https://github.com/jeffadickson/TestHarbor/issues/3) | planned |
| [JG-016 XML documentation everywhere](#jg-016) | P0 | [#16](https://github.com/jeffadickson/TestHarbor/issues/16) | implemented |

<a id="jg-001"></a>
## JG-001 — Declarative resource requirements

P1 · [#1](https://github.com/jeffadickson/TestHarbor/issues/1) · planned

Combine class and method declarations without changing NUnit internals.

Acceptance criteria:

- Use exact public method identity or validated source-generated metadata; never resolve an overloaded method by name alone.
- Merge inherited class/method requirements and explicit Resources overrides; exclusive access wins duplicate names.
- Cover generic and parameterized methods and retain ownership through all derived setup/teardown and extension cleanup.
- Document that these locks run during setup and do not make NUnit scheduling resource-aware.

Dependencies: none.

Verification task: add focused unit tests plus real-runner probes where discovery, outcomes or lifecycle behavior is involved. Include XML documentation and runnable examples for every new API; CI must pass on Linux, Windows and macOS.

<a id="jg-002"></a>
## JG-002 — Fixture and run service scopes

P1 · [#2](https://github.com/jeffadickson/TestHarbor/issues/2) · planned

Add explicitly owned reusable scopes while preserving per-test isolation.

Acceptance criteria:

- Define session/fixture/run ownership and prevent scoped-to-longer-lived dependency capture.
- Filtered runs initialize only needed resources and always dispose initialized scopes once.
- Cover parallel fixture use, partial initialization, async disposal and quarantine propagation.
- Never use a hidden global service locator or claim Java store lookup semantics without specifying them.

Dependencies: JG-014.

Verification task: add focused unit tests plus real-runner probes where discovery, outcomes or lifecycle behavior is involved. Include XML documentation and runnable examples for every new API; CI must pass on Linux, Windows and macOS.

<a id="jg-003"></a>
## JG-003 — Reusable extension profiles and registration

P2 · [#5](https://github.com/jeffadickson/TestHarbor/issues/5) · planned

Provide explicit C# profiles for extension composition.

Acceptance criteria:

- Support reusable profiles with deterministic ordering, inheritance and duplicate-resolution rules.
- Registration remains opt-in; no automatic activation merely because an assembly is referenced.
- Profile-owned dependencies obey the selected scope and are disposed exactly once.
- Include class-level and method-level examples and reject unsupported declarations clearly.

Dependencies: JG-001, JG-014.

Verification task: add focused unit tests plus real-runner probes where discovery, outcomes or lifecycle behavior is involved. Include XML documentation and runnable examples for every new API; CI must pass on Linux, Windows and macOS.

<a id="jg-004"></a>
## JG-004 — Lifecycle phase and outcome contracts

P1 · [#6](https://github.com/jeffadickson/TestHarbor/issues/6) · planned

Extend whole-test hooks without pretending to intercept every NUnit invocation.

Acceptance criteria:

- Define setup/body/teardown/cleanup phases and preserve both primary and secondary failures.
- Provide explicit RunAsync body timing/observers first; document which phases ordinary HarborFixture hooks can observe.
- Expose outcome snapshots with a finality indicator; do not label a teardown snapshot as the final NUnit runner result.
- Observers must not turn failed tests into passes; observer failures have a documented policy.
- Gate transparent invocation replacement behind a public-API feasibility result; never reference NUnit.Framework.Internal.

Dependencies: JG-012.

Verification task: add focused unit tests plus real-runner probes where discovery, outcomes or lifecycle behavior is involved. Include XML documentation and runnable examples for every new API; CI must pass on Linux, Windows and macOS.

<a id="jg-005"></a>
## JG-005 — Conditional execution and reproducible configuration

P1 · [#7](https://github.com/jeffadickson/TestHarbor/issues/7) · planned

Evaluate environment and capability requirements before acquiring hardware.

Acceptance criteria:

- Use typed configuration with explicit precedence: defaults, configuration file, environment, per-test override.
- Distinguish disabled/skipped, unsupported environment, inconclusive assumption and real setup failure in NUnit results.
- Evaluate pure conditions before leases/service initialization and record the reason and effective non-secret configuration.
- Test missing variables, unknown keys, conflicting conditions and filtered execution; redact secret values.

Dependencies: JG-014.

Verification task: add focused unit tests plus real-runner probes where discovery, outcomes or lifecycle behavior is involved. Include XML documentation and runnable examples for every new API; CI must pass on Linux, Windows and macOS.

<a id="jg-006"></a>
## JG-006 — Temporary directories and owned-resource cleanup

P1 · [#8](https://github.com/jeffadickson/TestHarbor/issues/8) · planned

Add an explicit ownership stack alongside DI.

Acceptance criteria:

- Create unique per-test directories with always/on-success/never retention and optional parent-directory factory.
- Provide explicit ownership registration for IDisposable/IAsyncDisposable instances not created by DI.
- Attempt every owned cleanup in reverse order and aggregate failures; prevent double ownership/disposal.
- Keep directories inside their configured root and avoid deleting unrelated paths or following an unsafe link.
- Preserve artifacts until attachments are published; coordinate retention with provisional versus final outcomes.

Dependencies: JG-004, JG-011.

Verification task: add focused unit tests plus real-runner probes where discovery, outcomes or lifecycle behavior is involved. Include XML documentation and runnable examples for every new API; CI must pass on Linux, Windows and macOS.

<a id="jg-007"></a>
## JG-007 — Typed data sources and invocation templates

P2 · [#9](https://github.com/jeffadickson/TestHarbor/issues/9) · planned

Build on NUnit discovery instead of replacing it.

Acceptance criteria:

- Provide typed CSV/enum/null/empty data adapters with explicit conversion/culture/error policy.
- Give cases stable IDs and display names while preserving IDE filters and reruns.
- Keep live resource acquisition out of discovery; create invocation-specific scopes during execution.
- Demonstrate parameterized fixture/method reuse and explicitly distinguish it from JUnit TestTemplate/ClassTemplate semantics.
- Record input provenance and deterministic enumeration; ordinary TestCaseSource remains usable.

Dependencies: JG-001, JG-002.

Verification task: add focused unit tests plus real-runner probes where discovery, outcomes or lifecycle behavior is involved. Include XML documentation and runnable examples for every new API; CI must pass on Linux, Windows and macOS.

<a id="jg-008"></a>
## JG-008 — Typed service injection feasibility

P2 · [#10](https://github.com/jeffadickson/TestHarbor/issues/10) · planned

Assess ergonomic injection without replacing NUnit fixture construction.

Acceptance criteria:

- Prototype explicit typed facade delegates or a source-generated bridge using public APIs only.
- Separate service parameters from NUnit case data and diagnose missing/ambiguous registrations.
- Document constructor and lifecycle injection boundaries; do not claim ParameterResolver parity from Services.GetRequiredService alone.
- If transparent injection needs internals, retain the explicit API and publish a no-go decision.

Dependencies: JG-014, JG-016.

Verification task: add focused unit tests plus real-runner probes where discovery, outcomes or lifecycle behavior is involved. Include XML documentation and runnable examples for every new API; CI must pass on Linux, Windows and macOS.

<a id="jg-009"></a>
## JG-009 — Nested contexts and reusable contract suites

P2 · [#11](https://github.com/jeffadickson/TestHarbor/issues/11) · planned

Provide C#-appropriate behavioral composition.

Acceptance criteria:

- Prototype abstract contract fixtures and explicit nested context scopes with deterministic cleanup.
- Preserve isolated state under parallel execution and report the actual NUnit test tree.
- Do not imply C# nested types share outer instances or that NUnit discovers default interface test methods.
- Document which designs provide equivalent behavior and which require a separate adapter decision.

Dependencies: JG-002, JG-004.

Verification task: add focused unit tests plus real-runner probes where discovery, outcomes or lifecycle behavior is involved. Include XML documentation and runnable examples for every new API; CI must pass on Linux, Windows and macOS.

<a id="jg-010"></a>
## JG-010 — Cancellation budgets and lifecycle misuse guards

P1 · [#12](https://github.com/jeffadickson/TestHarbor/issues/12) · planned

Harden the wrapper boundary and make stalled phases diagnosable.

Acceptance criteria:

- Define acquisition/start/body/cleanup budgets separately; cancelling a wait never revokes an active lease.
- Cleanup must not inherit an already-cancelled test token; a deadline does not prove hardware has stopped.
- Detect or diagnose SingleInstance overrides, hidden base lifecycle methods and recursive acquisition/disposal patterns.
- Provide bounded diagnostics for blocked resources and stalled cleanup, without Thread.Abort or claiming hard-stop guarantees.
- Use NUnit CancelAfter for its existing role and test interactions with teardown and quarantine.

Dependencies: JG-012.

Verification task: add focused unit tests plus real-runner probes where discovery, outcomes or lifecycle behavior is involved. Include XML documentation and runnable examples for every new API; CI must pass on Linux, Windows and macOS.

<a id="jg-011"></a>
## JG-011 — Artifacts and structured result evidence

P1 · [#13](https://github.com/jeffadickson/TestHarbor/issues/13) · planned

Use NUnit attachments with defined retention and redaction.

Acceptance criteria:

- Attach files through public NUnit TestContext APIs and preserve stable test/session IDs and timestamps.
- Define size limits, MIME/type metadata, retention, path validation and secret redaction.
- Publish cleanup/quarantine diagnostics without relying exclusively on the evicting 256-event ring.
- Verify NUnit XML/TRX and adapter behavior; exact JUnit report conversion is not a default requirement.

Dependencies: JG-004.

Verification task: add focused unit tests plus real-runner probes where discovery, outcomes or lifecycle behavior is involved. Include XML documentation and runnable examples for every new API; CI must pass on Linux, Windows and macOS.

<a id="jg-012"></a>
## JG-012 — Extension conformance test kit

P1 · [#14](https://github.com/jeffadickson/TestHarbor/issues/14) · planned

Turn existing failure probes into a reusable contract harness.

Acceptance criteria:

- Exercise success, skip, assumption, setup/body/teardown/stop/disposal failure and cancellation.
- Assert event ordering, resource retention, quarantine, duplicate disposal and final externally observed outcomes.
- Run real NUnit fixtures through supported CLI/adapter paths, including parameterized, inherited and parallel cases.
- Provide a documented helper for third-party extension authors; do not depend on NUnit internal execution classes.

Dependencies: none.

Verification task: add focused unit tests plus real-runner probes where discovery, outcomes or lifecycle behavior is involved. Include XML documentation and runnable examples for every new API; CI must pass on Linux, Windows and macOS.

<a id="jg-013"></a>
## JG-013 — Runtime-generated test discovery/reporting feasibility

P3 · [#4](https://github.com/jeffadickson/TestHarbor/issues/4) · planned

Determine whether runtime-discovered tests fit the wrapper constraint.

Acceptance criteria:

- Compare discovery-time TestCaseSource with execution-time dynamic nodes using a working adapter experiment.
- Check test IDs, selection, rerun, nested containers, results and per-node lifecycle.
- If unsupported through public APIs, explicitly document the boundary and retain NUnit sources.
- Do not copy JUnit dynamic-test lifecycle blindly: ordinary before/after-each hooks are factory-level, not per generated child.

Dependencies: JG-012.

Verification task: add focused unit tests plus real-runner probes where discovery, outcomes or lifecycle behavior is involved. Include XML documentation and runnable examples for every new API; CI must pass on Linux, Windows and macOS.

<a id="jg-014"></a>
## JG-014 — Compatibility and API stability policy

P1 · [#15](https://github.com/jeffadickson/TestHarbor/issues/15) · planned

Make documented support claims executable.

Acceptance criteria:

- Pin the supported NUnit/.NET baseline and document supported public extension surface.
- Add conformance coverage for repeat/retry, filtered tests, fixture inheritance, parallelism and cancellation.
- Guard against NUnit internal references and classify public API stability as experimental or supported.
- Publish migration notes and package-consumer smoke checks; preserve .NET 10 and all three CI platforms.

Dependencies: JG-012.

Verification task: add focused unit tests plus real-runner probes where discovery, outcomes or lifecycle behavior is involved. Include XML documentation and runnable examples for every new API; CI must pass on Linux, Windows and macOS.

<a id="jg-015"></a>
## JG-015 — Cross-process resource broker

P3 · [#3](https://github.com/jeffadickson/TestHarbor/issues/3) · planned

Treat distributed ownership as a separate instrument-domain enhancement.

Acceptance criteria:

- Use fenced leases and explicit ownership/recovery authority; persist quarantine where required.
- Test contention from independent processes, expiry, crashes and broker restart.
- Never infer safe physical hardware state solely from heartbeat expiry.
- Keep the in-process coordinator usable without a broker.

Dependencies: JG-010, JG-014.

Verification task: add focused unit tests plus real-runner probes where discovery, outcomes or lifecycle behavior is involved. Include XML documentation and runnable examples for every new API; CI must pass on Linux, Windows and macOS.

<a id="jg-016"></a>
## JG-016 — XML documentation everywhere

P0 · [#16](https://github.com/jeffadickson/TestHarbor/issues/16) · implemented

Document all authored C# declarations and enforce coverage.

Acceptance criteria:

- Cover types, methods, constructors, properties, fields, enum values and local functions, including private helpers/tests/examples/tools.
- Require nonempty summary, every param/typeparam, returns for non-void methods and value for properties.
- Use compiler XML diagnostics plus a Roslyn syntax audit; reject malformed XML and mismatched parameter names.
- Generate the browsable reference for every C# project and include XML documentation in package builds.
- Apply the same rules to future generated repository code; only compiler/build output under bin/obj is excluded.

Dependencies: none.

Verification task: add focused unit tests plus real-runner probes where discovery, outcomes or lifecycle behavior is involved. Include XML documentation and runnable examples for every new API; CI must pass on Linux, Windows and macOS.

## Definition of done

Each task requires a public-API-only design, explicit ownership/cancellation/error semantics, acceptance evidence, XML documentation for every authored C# declaration, generated reference updates and a green three-platform CI run. Feasibility tasks may finish with a documented no-go and supported alternative; they must not quietly introduce NUnit internals.

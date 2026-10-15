# JUnit / NUnit / TestHarbor gap analysis

Reviewed **2026-10-15**. Baseline: **JUnit 6.1.3 / NUnit 5.0.0 / TestHarbor 0.2**.

This is a functional comparison of Java JUnit Jupiter/Platform with C# NUnit and the current TestHarbor wrapper. It covers 56 capabilities, not every overload or every third-party Java library. No percentage of compatibility is implied. Versioned official JUnit documentation, official NUnit documentation and local implementation evidence are linked for each row.

JUnit remains actively developed. Its modern extension model is substantially broader than the original JUnit class model. NUnit already supplies the core assertions, discovery, parameterized tests, fixture data, filtering and runner integration; TestHarbor should reuse these.

The highest-value work is explicit lifecycle contracts, conformance tests, owned resources, conditional execution, artifacts and broader service scopes. Transparent parameter injection, runtime-generated test trees and scheduler-level resource locks require feasibility decisions. Cross-process hardware coordination is a domain enhancement, not a missing JUnit parity feature.

## Scope and interpretation

“Partial” means related functionality exists but semantics differ. A task is not proof that complete equivalence can be implemented. The public-API-only constraint remains binding: no NUnit fork, internal execution commands, internal reflection or new runner is authorized by this analysis. JUnit experimental APIs are inspiration rather than a compatibility promise.

Existing StartAsync/StopAsync hooks surround the derived NUnit lifecycle; they are not all JUnit callbacks. Services access is explicit, not ParameterResolver injection. NUnit TestCaseSource is discovery-time data, not an execution-time TestFactory. Base teardown observes a provisional outcome, not a guaranteed final runner result. Setup-time resource waits do not change NUnit scheduling.

## Classification

| Class | Meaning | Capabilities |
|---|---|---:|
| N | Native NUnit; reuse | 16 |
| P | Partial coverage; wrapper work | 20 |
| A | Existing advantage or domain enhancement | 3 |
| B | Wrapper boundary; feasibility required | 6 |
| H | TestHarbor delivered | 4 |
| X | Platform-specific or outside scope | 7 |

## Core

| ID / capability | JUnit | NUnit + current TestHarbor | Class | Decision / requirements | Evidence |
|---|---|---|---|---|---|
| G01 Test declarations | Annotated methods | NUnit Test and fixture attributes | N | REUSE | [J26](https://docs.junit.org/6.1.3/_exports/junit-user-guide-6.1.3.html) [N0](https://docs.nunit.org/articles/nunit/writing-tests/attributes.html) |
| G02 Equality, collection and numeric assertions | Assertions API | NUnit constraint model; reuse rather than add assertion wrappers | N | REUSE | [J1](https://docs.junit.org/6.1.3/writing-tests/assertions.html) [N0](https://docs.nunit.org/articles/nunit/writing-tests/attributes.html) |
| G03 Grouped failures | assertAll | NUnit multiple-assert scopes | N | REUSE | [J1](https://docs.junit.org/6.1.3/writing-tests/assertions.html) [N2](https://docs.nunit.org/articles/nunit/writing-tests/assertions/multiple-asserts.html) |
| G04 Exception and asynchronous assertions | Exception assertions; runtime-specific async model | NUnit 5 awaitable async assertions fit Task-based C# | N | REUSE | [J1](https://docs.junit.org/6.1.3/writing-tests/assertions.html) [N1](https://docs.nunit.org/articles/nunit/V5NewFeatures.html) |
| G05 Assumptions and aborted tests | Assumptions abort execution | NUnit Assume yields inconclusive; preserve distinct result vocabulary | N | REUSE | [J26](https://docs.junit.org/6.1.3/_exports/junit-user-guide-6.1.3.html) [N0](https://docs.nunit.org/articles/nunit/writing-tests/attributes.html) |
| G06 Tags and selection | Tags and launcher filters | NUnit categories and runner filters | N | REUSE | [J26](https://docs.junit.org/6.1.3/_exports/junit-user-guide-6.1.3.html) [N0](https://docs.nunit.org/articles/nunit/writing-tests/attributes.html) |
| G07 Display names and name generation | DisplayName and generators | NUnit case names supported; no TestHarbor reusable naming policy | P | [JG-007](REQUIREMENTS-v0.3.md#jg-007) | [J2](https://docs.junit.org/6.1.3/writing-tests/display-names.html) [N3](https://docs.nunit.org/articles/nunit/writing-tests/attributes/testcasesource.html) |
| G08 Disabling tests | Disabled annotation | NUnit Ignore and Explicit; reason semantics differ | N | REUSE | [J26](https://docs.junit.org/6.1.3/_exports/junit-user-guide-6.1.3.html) [N0](https://docs.nunit.org/articles/nunit/writing-tests/attributes.html) |
| G09 Conditional execution | OS/runtime/env/custom conditions | NUnit Platform/Ignore/Assume exist; no unified Harbor conditions or config precedence | P | [JG-005](REQUIREMENTS-v0.3.md#jg-005) | [J3](https://docs.junit.org/6.1.3/writing-tests/conditional-test-execution.html) [N0](https://docs.nunit.org/articles/nunit/writing-tests/attributes.html) |
| G10 Per-test and per-class callbacks | Before/after each/all | NUnit SetUp/TearDown and one-time lifecycle | N | REUSE | [J26](https://docs.junit.org/6.1.3/_exports/junit-user-guide-6.1.3.html) [N0](https://docs.nunit.org/articles/nunit/writing-tests/attributes.html) |
| G11 Instance lifecycle defaults | Per-method default; optional per-class | NUnit defaults shared; HarborFixture sets per-case, but callers can override it unsafely | P | [JG-010](REQUIREMENTS-v0.3.md#jg-010) | [J26](https://docs.junit.org/6.1.3/_exports/junit-user-guide-6.1.3.html) [N5](https://docs.nunit.org/articles/nunit/writing-tests/attributes/fixturelifecycle.html) [L3](../src/TestHarbor.NUnit/HarborFixture.cs) |
| G12 Inherited setup and tests | Inheritance is supported | NUnit inherited fixture methods; hidden lifecycle members need guidance | N | REUSE | [J26](https://docs.junit.org/6.1.3/_exports/junit-user-guide-6.1.3.html) [N0](https://docs.nunit.org/articles/nunit/writing-tests/attributes.html) |

## Data

| ID / capability | JUnit | NUnit + current TestHarbor | Class | Decision / requirements | Evidence |
|---|---|---|---|---|---|
| G13 Parameterized methods | Multiple argument sources | NUnit TestCase and TestCaseSource | N | REUSE | [J4](https://docs.junit.org/6.1.3/writing-tests/parameterized-classes-and-tests.html) [N3](https://docs.nunit.org/articles/nunit/writing-tests/attributes/testcasesource.html) |
| G14 Parameterized classes | ParameterizedClass is experimental | NUnit parameterized fixtures and TestFixtureSource already exist | N | REUSE | [J4](https://docs.junit.org/6.1.3/writing-tests/parameterized-classes-and-tests.html) [N4](https://docs.nunit.org/articles/nunit/writing-tests/attributes/testfixturesource.html) |
| G15 CSV, enum, null and empty conveniences | Built-in argument sources | NUnit sources can supply data; reusable typed CSV/conversion helpers absent | P | [JG-007](REQUIREMENTS-v0.3.md#jg-007) | [J4](https://docs.junit.org/6.1.3/writing-tests/parameterized-classes-and-tests.html) [N3](https://docs.nunit.org/articles/nunit/writing-tests/attributes/testcasesource.html) |
| G16 Argument conversion and aggregation | Converters and aggregators | NUnit typed TestCaseData and custom sources; no Harbor conversion contract | P | [JG-007](REQUIREMENTS-v0.3.md#jg-007) | [J4](https://docs.junit.org/6.1.3/writing-tests/parameterized-classes-and-tests.html) [N1](https://docs.nunit.org/articles/nunit/V5NewFeatures.html) |
| G17 Repeated tests | RepeatedTest | NUnit Repeat; failure-threshold semantics are not identical | N | REUSE | [J5](https://docs.junit.org/6.1.3/writing-tests/repeated-tests.html) [N1](https://docs.nunit.org/articles/nunit/V5NewFeatures.html) |
| G18 Retry policy | Can be implemented by extensions | NUnit Retry is already available; no new Harbor retry wrapper needed | A | REUSE | [J25](https://docs.junit.org/6.1.3/extensions/intercepting-invocations.html) [N6](https://docs.nunit.org/articles/nunit/writing-tests/attributes/retry.html) |
| G19 Pairwise and combinatorial data | Sources can construct input combinations | NUnit native Pairwise/Combinatorial/Sequential are useful existing capabilities | A | REUSE | [J4](https://docs.junit.org/6.1.3/writing-tests/parameterized-classes-and-tests.html) [N7](https://docs.nunit.org/articles/nunit/writing-tests/attributes/pairwise.html) [N0](https://docs.nunit.org/articles/nunit/writing-tests/attributes.html) |
| G20 Invocation and class templates | Provider supplies invocation contexts | Ordinary data cases/fixtures exist; context-bound resources and extensions need explicit design | P | [JG-007](REQUIREMENTS-v0.3.md#jg-007) | [J6](https://docs.junit.org/6.1.3/writing-tests/test-templates.html) [J7](https://docs.junit.org/6.1.3/writing-tests/class-templates.html) [N3](https://docs.nunit.org/articles/nunit/writing-tests/attributes/testcasesource.html) [N4](https://docs.nunit.org/articles/nunit/writing-tests/attributes/testfixturesource.html) [L1](../src/TestHarbor/TestSession.cs) |
| G21 Runtime-generated test trees | TestFactory and nested dynamic containers | Discovery-time TestCaseSource is not execution-time registration | B | [JG-013](REQUIREMENTS-v0.3.md#jg-013) | [J8](https://docs.junit.org/6.1.3/writing-tests/dynamic-tests.html) [N3](https://docs.nunit.org/articles/nunit/writing-tests/attributes/testcasesource.html) |
| G22 Generated case identity and replay | Names and dynamic test source metadata | NUnit names/filters exist; stable Harbor case IDs and replay provenance absent | P | [JG-007](REQUIREMENTS-v0.3.md#jg-007), [JG-013](REQUIREMENTS-v0.3.md#jg-013) | [J2](https://docs.junit.org/6.1.3/writing-tests/display-names.html) [J8](https://docs.junit.org/6.1.3/writing-tests/dynamic-tests.html) [N3](https://docs.nunit.org/articles/nunit/writing-tests/attributes/testcasesource.html) |

## Structure

| ID / capability | JUnit | NUnit + current TestHarbor | Class | Decision / requirements | Evidence |
|---|---|---|---|---|---|
| G23 Nested behavioral contexts | Nested classes share enclosing lifecycle/state | Nested C# fixtures do not reproduce Java enclosing-instance semantics | B | [JG-009](REQUIREMENTS-v0.3.md#jg-009) | [J9](https://docs.junit.org/6.1.3/writing-tests/nested-tests.html) [L3](../src/TestHarbor.NUnit/HarborFixture.cs) |
| G24 Reusable contract tests | Interface default methods may carry tests | C# abstract fixture examples are feasible; no promised NUnit interface discovery parity | P | [JG-009](REQUIREMENTS-v0.3.md#jg-009) | [J10](https://docs.junit.org/6.1.3/writing-tests/test-interfaces-and-default-methods.html) [L3](../src/TestHarbor.NUnit/HarborFixture.cs) |

## Services

| ID / capability | JUnit | NUnit + current TestHarbor | Class | Decision / requirements | Evidence |
|---|---|---|---|---|---|
| G25 Constructor/method/lifecycle injection | ParameterResolver | Harbor uses explicit Services resolution; ordinary NUnit parameters remain case data | B | [JG-008](REQUIREMENTS-v0.3.md#jg-008) | [J11](https://docs.junit.org/6.1.3/extensions/parameter-resolution.html) [L1](../src/TestHarbor/TestSession.cs) [L3](../src/TestHarbor.NUnit/HarborFixture.cs) |
| G26 Test instance creation and processing | Factory and pre/post construction callbacks | Harbor session starts after fixture construction; cannot transparently replace construction | B | [JG-008](REQUIREMENTS-v0.3.md#jg-008) | [J12](https://docs.junit.org/6.1.3/extensions/test-instance-factories.html) [L3](../src/TestHarbor.NUnit/HarborFixture.cs) |
| G27 Hierarchical extension stores | Method/class/root and launcher scopes | Harbor DI root and scope are per-test; fixture/run stores not implemented | P | [JG-002](REQUIREMENTS-v0.3.md#jg-002) | [J13](https://docs.junit.org/6.1.3/extensions/keeping-state-in-extensions.html) [L1](../src/TestHarbor/TestSession.cs) |
| G28 Per-test managed cleanup | Store-owned closeable resources | DI-owned async services and leases cleaned up by TestSession | H | DELIVERED | [J13](https://docs.junit.org/6.1.3/extensions/keeping-state-in-extensions.html) [L1](../src/TestHarbor/TestSession.cs) |

## Extensions

| ID / capability | JUnit | NUnit + current TestHarbor | Class | Decision / requirements | Evidence |
|---|---|---|---|---|---|
| G29 Ordered whole-test start/stop | BeforeEach/AfterEach callback wrapping | Harbor ordered StartAsync and reverse StopAsync with rollback | H | DELIVERED | [J14](https://docs.junit.org/6.1.3/extensions/test-lifecycle-callbacks.html) [L4](../src/TestHarbor/ITestExtension.cs) [L1](../src/TestHarbor/TestSession.cs) |
| G30 Precise body-only and class-level hooks | BeforeTestExecution/AfterTestExecution and BeforeAll/AfterAll | Harbor only brackets whole per-test lifecycle; not body-only timing | P | [JG-004](REQUIREMENTS-v0.3.md#jg-004), [JG-002](REQUIREMENTS-v0.3.md#jg-002) | [J14](https://docs.junit.org/6.1.3/extensions/test-lifecycle-callbacks.html) [L3](../src/TestHarbor.NUnit/HarborFixture.cs) |
| G31 Invocation and exception interception | InvocationInterceptor and exception handlers | Explicit RunAsync can wrap its body; transparent NUnit invocation interception is not exposed | B | [JG-004](REQUIREMENTS-v0.3.md#jg-004) | [J25](https://docs.junit.org/6.1.3/extensions/intercepting-invocations.html) [J15](https://docs.junit.org/6.1.3/extensions/exception-handling.html) [L1](../src/TestHarbor/TestSession.cs) [L3](../src/TestHarbor.NUnit/HarborFixture.cs) |
| G32 Outcome-aware observers | TestWatcher outcome notifications | No neutral final-outcome extension contract; NUnit teardown snapshot is not final runner outcome | P | [JG-004](REQUIREMENTS-v0.3.md#jg-004) | [J16](https://docs.junit.org/6.1.3/extensions/test-result-processing.html) [N9](https://docs.nunit.org/articles/nunit/writing-tests/TestContext.html) [L4](../src/TestHarbor/ITestExtension.cs) |
| G33 Declarative extension registration | ExtendWith and RegisterExtension | Harbor uses scoped DI registration; no per-method profiles or duplicate policy | P | [JG-003](REQUIREMENTS-v0.3.md#jg-003) | [J17](https://docs.junit.org/6.1.3/extensions/registering-extensions.html) [L4](../src/TestHarbor/ITestExtension.cs) |
| G34 Composed annotations | Meta-annotations combine policies | C# attributes are not Java meta-annotations; explicit reusable profiles are feasible | P | [JG-003](REQUIREMENTS-v0.3.md#jg-003) | [J17](https://docs.junit.org/6.1.3/extensions/registering-extensions.html) [L3](../src/TestHarbor.NUnit/HarborFixture.cs) |
| G35 Classpath auto-discovery | Optional ServiceLoader extension registration | Deliberately prefer explicit opt-in; do not emulate silent assembly activation | X | NO-PORT | [J17](https://docs.junit.org/6.1.3/extensions/registering-extensions.html) [L4](../src/TestHarbor/ITestExtension.cs) |

## Resources

| ID / capability | JUnit | NUnit + current TestHarbor | Class | Decision / requirements | Evidence |
|---|---|---|---|---|---|
| G36 Owned non-DI resources | AutoClose fields and store ownership | Caller-supplied DI instances are not owned; no explicit owned-resource stack | P | [JG-006](REQUIREMENTS-v0.3.md#jg-006) | [J18](https://docs.junit.org/6.1.3/writing-tests/built-in-extensions.html) [L1](../src/TestHarbor/TestSession.cs) |
| G37 Temporary directories | TempDir factories and retention policies | WorkDirectory is not an owned per-test temp directory; no Harbor temp service | P | [JG-006](REQUIREMENTS-v0.3.md#jg-006) | [J18](https://docs.junit.org/6.1.3/writing-tests/built-in-extensions.html) [N9](https://docs.nunit.org/articles/nunit/writing-tests/TestContext.html) [L1](../src/TestHarbor/TestSession.cs) |

## Concurrency

| ID / capability | JUnit | NUnit + current TestHarbor | Class | Decision / requirements | Evidence |
|---|---|---|---|---|---|
| G38 Named shared/exclusive resources | Scheduler-aware ResourceLock | Harbor atomically acquires sets at setup; no declarative attributes or scheduler integration | P | [JG-001](REQUIREMENTS-v0.3.md#jg-001) | [J19](https://docs.junit.org/6.1.3/writing-tests/parallel-execution.html) [L2](../src/TestHarbor/ResourceCoordinator.cs) [L3](../src/TestHarbor.NUnit/HarborFixture.cs) |
| G39 Parallelism configuration | Execution modes and worker strategies | NUnit parallel controls remain responsible for scheduling | N | REUSE | [J19](https://docs.junit.org/6.1.3/writing-tests/parallel-execution.html) [N0](https://docs.nunit.org/articles/nunit/writing-tests/attributes.html) |
| G40 Whole-fixture isolation | Isolated | NUnit NonParallelizable provides related isolation; validate boundaries in compatibility tests | N | REUSE | [J19](https://docs.junit.org/6.1.3/writing-tests/parallel-execution.html) [N0](https://docs.nunit.org/articles/nunit/writing-tests/attributes.html) |
| G41 Runtime resource requirements | Dynamic lock providers | Resources override can compute a full set before session startup; it does not alter discovery | H | DELIVERED | [J19](https://docs.junit.org/6.1.3/writing-tests/parallel-execution.html) [L3](../src/TestHarbor.NUnit/HarborFixture.cs) |
| G42 Timeouts and cancellation | Timeout supports thread modes and defaults | NUnit CancelAfter is cooperative; Harbor has no phase budgets or cleanup deadline policy | P | [JG-010](REQUIREMENTS-v0.3.md#jg-010) | [J20](https://docs.junit.org/6.1.3/writing-tests/timeouts.html) [N8](https://docs.nunit.org/articles/nunit/writing-tests/attributes/cancelafter.html) [L1](../src/TestHarbor/TestSession.cs) |
| G43 Pre-interruption diagnostics | PreInterruptCallback | No JVM thread-interruption equivalent; plan cooperative stall diagnostics, not forced thread abort | B | [JG-010](REQUIREMENTS-v0.3.md#jg-010) | [J21](https://docs.junit.org/6.1.3/extensions/pre-interrupt-callback.html) [N8](https://docs.nunit.org/articles/nunit/writing-tests/attributes/cancelafter.html) |

## Reporting

| ID / capability | JUnit | NUnit + current TestHarbor | Class | Decision / requirements | Evidence |
|---|---|---|---|---|---|
| G44 Output and current-test information | TestInfo/TestReporter and output capture | NUnit TestContext and Harbor owner-labelled progress output | N | REUSE | [J26](https://docs.junit.org/6.1.3/_exports/junit-user-guide-6.1.3.html) [N9](https://docs.nunit.org/articles/nunit/writing-tests/TestContext.html) [L3](../src/TestHarbor.NUnit/HarborFixture.cs) |
| G45 Files and structured evidence | Reporting entries and report formats | NUnit AddTestAttachment exists; Harbor lacks retention/redaction/provenance conventions | P | [JG-011](REQUIREMENTS-v0.3.md#jg-011) | [J22](https://docs.junit.org/6.1.3/advanced-topics/junit-platform-reporting.html) [N9](https://docs.nunit.org/articles/nunit/writing-tests/TestContext.html) [L3](../src/TestHarbor.NUnit/HarborFixture.cs) |
| G46 Reporting wire formats | Legacy XML and Open Test Reporting | Use NUnit XML/TRX; exact format conversion is optional integration work | X | NO-PORT | [J22](https://docs.junit.org/6.1.3/advanced-topics/junit-platform-reporting.html) [N10](https://docs.nunit.org/articles/vs-test-adapter/Index.html) |

## Platform

| ID / capability | JUnit | NUnit + current TestHarbor | Class | Decision / requirements | Evidence |
|---|---|---|---|---|---|
| G47 Discovery/launch/listener APIs | JUnit Platform launcher | Use .NET runners and NUnit adapters; do not rebuild the platform in Harbor | X | NO-PORT | [J0](https://docs.junit.org/6.1.3/overview.html) [N10](https://docs.nunit.org/articles/vs-test-adapter/Index.html) |
| G48 Pluggable test engine SPI | TestEngine API | Outside the approved wrapper architecture | X | NO-PORT | [J0](https://docs.junit.org/6.1.3/overview.html) |
| G49 Engine/extension verification kit | EngineTestKit discovery and event assertions | Four external failure probes exist; reusable Harbor extension test kit absent | P | [JG-012](REQUIREMENTS-v0.3.md#jg-012) | [J23](https://docs.junit.org/6.1.3/advanced-topics/testkit.html) [L5](../tools/verify_failure_probes.py) |
| G50 IDE/build/OS integration | JVM IDE and build integrations | dotnet test plus NUnit adapter; three-OS Harbor CI passes | N | REUSE | [J0](https://docs.junit.org/6.1.3/overview.html) [N10](https://docs.nunit.org/articles/vs-test-adapter/Index.html) [L7](../.github/workflows/ci.yml) |
| G51 Legacy JUnit compatibility | Vintage runs JUnit 3/4 and is deprecated | No Java test execution requirement for a C# wrapper | X | NO-PORT | [J0](https://docs.junit.org/6.1.3/overview.html) |
| G52 API stability classification | Explicit API evolution lifecycle | Harbor is alpha; support policy and compatibility gates need definition | P | [JG-014](REQUIREMENTS-v0.3.md#jg-014) | [J24](https://docs.junit.org/6.1.3/api-evolution.html) [L7](../.github/workflows/ci.yml) |

## Ecosystem

| ID / capability | JUnit | NUnit + current TestHarbor | Class | Decision / requirements | Evidence |
|---|---|---|---|---|---|
| G53 Mocks, containers, property tests and coverage | Separate ecosystem tools, not all JUnit core | Integrate appropriate .NET libraries separately; not a JUnit-core parity checklist | X | NO-PORT | [J0](https://docs.junit.org/6.1.3/overview.html) |

## Harbor-specific

| ID / capability | JUnit | NUnit + current TestHarbor | Class | Decision / requirements | Evidence |
|---|---|---|---|---|---|
| G54 Quarantine and explicit recovery | ResourceLock coordinates concurrency; quarantine is not its documented contract | Harbor rejects queued/new acquisitions after failed cleanup and requires recovery | A | DELIVERED | [J19](https://docs.junit.org/6.1.3/writing-tests/parallel-execution.html) [L2](../src/TestHarbor/ResourceCoordinator.cs) |
| G55 Cross-process hardware ownership | In-process ResourceLock is not a distributed broker | Harbor also process-local; optional broker is a domain requirement, not Java parity | X | [JG-015](REQUIREMENTS-v0.3.md#jg-015) | [J19](https://docs.junit.org/6.1.3/writing-tests/parallel-execution.html) [L2](../src/TestHarbor/ResourceCoordinator.cs) |

## Documentation

| ID / capability | JUnit | NUnit + current TestHarbor | Class | Decision / requirements | Evidence |
|---|---|---|---|---|---|
| G56 Javadoc-like code reference | Published Javadoc and guide | Compiler XML and HTML reference; this change enforces all authored declarations | H | [JG-016](REQUIREMENTS-v0.3.md#jg-016) | [J0](https://docs.junit.org/6.1.3/overview.html) [L6](../tools/TestHarbor.DocCheck/Program.cs) |

## Implementation order

1. JG-016 XML documentation enforcement (delivered in this change).
2. JG-012 conformance kit, then JG-014 compatibility policy and JG-004 lifecycle contracts; JG-001 declarations can proceed independently.
3. JG-002 scopes, JG-005 conditions, JG-010 budgets and JG-011 artifacts; JG-006 ownership follows outcome/artifact contracts.
4. JG-003 profiles, JG-007 typed templates, JG-008 injection feasibility and JG-009 contexts.
5. JG-013 runtime discovery feasibility and JG-015 cross-process broker only after their prerequisites.

## Maintenance

Edit `junit-gap-analysis.json`, then run `python tools/build_gap_docs.py`. Requirement acceptance criteria and task links are generated from that same source. Re-review official versioned documentation when changing the NUnit/JUnit baseline. Status reflects implementation, not merely the existence of a task.

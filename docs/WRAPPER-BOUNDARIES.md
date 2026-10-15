# Wrapper feasibility decisions — 0.3

Reviewed against NUnit 5.0.0 and .NET 10 on 2026-10-15. The architecture constraint remains: no NUnit fork, references to NUnit.Framework.Internal, reflection into NUnit execution objects, replacement engine or replacement adapter.

## Runtime-generated test trees — JG-013

**Decision: do not implement execution-time node registration in this wrapper.** Use discovery-time `TestCaseSource`/`TestFixtureSource` when separate filtering, rerun IDs and results are required. Runtime work may be recorded as artifacts or assertions inside an existing test, but those items are not additional tests.

Evidence: `DiscoveryBoundaryTests` runs two stable source cases and one test generating two runtime work items. `tools/verify_discovery.py` checks the actual adapter results: three results, with the two source names preserved under a filtered replay. The runtime loop remains one result. This experiment does not claim that every future adapter capability is impossible; it establishes the supported wrapper behavior.

The documented [ITestBuilder](https://docs.nunit.org/api/NUnit.Framework.Interfaces.ITestBuilder.html) API takes `Test` and returns `TestMethod`; these types are in NUnit's internal namespace. Using them violates this project's boundary even though the interface itself is public. Ordinary [TestCaseSource](https://docs.nunit.org/articles/nunit/writing-tests/attributes/testcasesource.html) provides supported discovery-time data. A future engine/adapter project would need a separate architecture decision. Nested dynamic containers and execution-time per-node IDs are therefore not supported.

JUnit dynamic factories also do not automatically give every generated child an ordinary before/after-each lifecycle. TestHarbor makes no such promise.

## Transparent service injection — JG-008

**Decision: ship explicit typed invocation; do not replace fixture construction or NUnit argument binding.** `ServiceInvocation.InvokeAsync<T>` resolves exactly one service and calls a typed delegate. Ordinary case data remains in the caller's closure. Missing or duplicate registrations fail clearly. Tests exercise successful resolution and ambiguity.

Constructor parameters remain NUnit fixture data. Base setup starts after fixture construction, so it cannot provide constructor injection. The [IWrapTestMethod](https://docs.nunit.org/api/NUnit.Framework.Interfaces.IWrapTestMethod.html) route wraps internal `TestCommand` objects and is outside this architecture. No source generator or reflection-based invocation bridge is needed for the shipped explicit API.

## Lifecycle observations — JG-004

`Starting`/`Ready` bracket extension startup. Explicit `RunAsync` also reports body boundaries. `Cleaning`/`Finished` bracket cleanup, with `CleanupOverdue` when requested. Ordinary HarborFixture integration observes the outer lifecycle and reports a failed NUnit teardown/setup/body snapshot through `RecordFailure`; it does not transparently intercept the body itself.

`IsFinal` means final for the session cleanup boundary, not the final NUnit runner result. A final observer sees errors known before its own callback; if that callback fails, its error is propagated to the caller. Observers cannot suppress body or cleanup failures. External TRX verification supplies the final runner outcome.

## Contexts and templates — JG-007 / JG-009

Typed CSV/value/enum/null/empty sources produce ordinary NUnit cases. Source-derived names and IDs support deterministic replay when input order and source identity remain stable. Invariant conversion is explicit; source IDs must not contain secrets. Acquisition occurs during execution, not source enumeration.

Parameterized fixtures and inherited abstract contract fixtures are demonstrated in `ContextContractTests.cs`. `HarborContext` provides explicit parent lookup, child state overrides and reverse cleanup. It changes neither C# enclosing-instance semantics nor NUnit's tree. Default interface test-method discovery is not promised. These are C#-appropriate alternatives, not exact JUnit TestTemplate/ClassTemplate providers.

## Cross-process ownership — JG-015

`FileResourceBroker` supports cooperating .NET processes on **one host**, using a trusted local directory, exclusive open handles and atomically replaced state files. It provides exclusive sets, monotonic fencing generations, explicit heartbeats, persistent quarantine and operator recovery. Multi-host/network-filesystem operation is not supported. The in-process shared/read coordinator remains independent.

An optional heartbeat deadline **quarantines without revoking ownership**: an old heartbeat never proves equipment has stopped. Expired ownership cannot renew; it must finish cleanup and release before verified recovery. A live holder prevents recovery; a crashed holder leaves an active record that blocks reuse until explicit recovery. `verify_broker.py` tests independent clients, blocked acquisition, process kill, restart with retained state, fencing advancement and explicit quarantine recovery. This is process-crash persistence, not a claim of survival under arbitrary disk corruption or power-loss behavior.

Resource names and owner labels must be non-secret. Directory access defines recovery authority; state files must not be deleted or edited to reclaim equipment. Downstream equipment gateways must enforce fencing tokens to reject delayed commands. Tokens alone cannot stop a physical device. Complete physical cleanup before disposing a lease; call Quarantine when recovery is uncertain.

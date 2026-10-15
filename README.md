# TestHarbor

Version **0.3.0-alpha.1** · working project name · .NET 10 · NUnit 5

TestHarbor is a companion wrapper around NUnit. NUnit remains responsible for discovery,
assertions, execution, filtering, and reports. No NUnit fork, internal API references,
reflection into NUnit, command replacement, or custom test engine is used.

## Run it

Install a .NET 10 SDK. Python 3 is needed only for documentation and failure-probe verification.

```sh
dotnet restore TestHarbor.sln --locked-mode
dotnet build TestHarbor.sln -c Release --no-restore
dotnet test TestHarbor.sln -c Release --no-build
dotnet run --project tools/TestHarbor.DocCheck -c Release --no-build -- .
dotnet run --project tools/TestHarbor.DocCheck -c Release --no-build -- --self-test
python3 tools/verify_failure_probes.py
python3 tools/verify_conformance.py
python3 tools/verify_discovery.py
python3 tools/verify_broker.py
python3 tools/verify_attachments.py
python3 tools/build_docs.py
```

The deliberate failure and conformance fixtures are marked Explicit and excluded from a normal run.
The verifier selects it explicitly and requires four expected failures plus independent
cleanup and lock-release evidence. Its successful exit indicates the failure handling passed.

For environments restricting MSBuild worker processes, add `-m:1 -p:UseSharedCompilation=false -nodeReuse:false`.

## Minimal NUnit fixture

```csharp
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor;
using TestHarbor.NUnit;

/// <summary>Demonstrates isolated supply access through the NUnit wrapper.</summary>
[RequiresResource("Bench-A/Supply-1")]
public sealed class SupplyTests : HarborFixture
{
    /// <summary>Registers the per-test supply client.</summary>
    /// <param name="services">The session service registrations.</param>
    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<SupplyClient>(); // Your implementation, ideally IAsyncDisposable.

    /// <summary>Checks the simulated supply output range.</summary>
    /// <returns>A task completing after voltage verification.</returns>
    [Test]
    public async Task ReadsVoltage()
    {
        var supply = Services.GetRequiredService<SupplyClient>();
        var volts = await supply.ReadVoltageAsync();
        Assert.That(volts, Is.InRange(4.9, 5.1));
    }
}
```

See `examples/TestHarbor.Example/PowerSupplyTests.cs` for the complete runnable simulation.
The base fixture acquires resources before derived SetUp and cleans up after derived TearDown.
Services are per-test by default; even a singleton registration lives only for that session unless you explicitly select a shared TestServiceScope.
NUnit test parameters remain ordinary TestCase/TestCaseSource data. Service injection is explicit
through `Services`, not automatic injection into method parameters.

For a test that already inherits another base class, use `TestSession.RunAsync` or
`await using var session = await TestSession.OpenAsync(...)` in your own lifecycle code.
A session opened inside the test body does not cover NUnit setup/teardown outside that body.

## Included now

- Named exclusive and shared-read resources, atomic acquisition of multiple resources.
- FIFO waiting, cancellation, duplicate merging, and idempotent lease release.
- Microsoft.Extensions.DependencyInjection per-test scopes with awaited async disposal.
- Separate root cleanup attempt if scoped cleanup fails; resource release in a finally block.
- Both body and cleanup exceptions retained by the explicit RunAsync facade.
- Ordered async extensions with reverse cleanup and partial-initialization rollback.
- Default quarantine on extension/service cleanup failure and NUnit derived teardown failure.
- Explicit recovery, immutable resource snapshots, and bounded wait/ownership diagnostics.
- Concurrent disposal callers await the same cleanup task.
- Public-hook NUnit fixture with InstancePerTestCase for parallel fixture isolation.
- Compiler-generated XML API documentation; all authored declarations are checked, including private helpers, tests and tools.
- Generated HTML API reference and requirements, design decisions, and development tasks.

## Boundaries

This is an alpha, not a hardware safety interlock. Coordination is in-process and shared only
among callers using the same ResourceCoordinator instance (including the default Shared instance).
Separate test host processes or separately loaded copies do not coordinate through this default coordinator. Use the optional FileResourceBroker explicitly for cooperating processes on one host. Resource names are
case-sensitive and must identify actual shared hardware consistently.

Acquire every needed resource together. Nested acquisition is non-reentrant and may deadlock.
FIFO is intentionally simple: a blocked oldest request delays later requests, even unrelated ones.
Acquisition awaits asynchronously; it does not alter NUnit's scheduler or worker allocation.
Readers must truly be safe to overlap: an instrument query that changes device state needs exclusive access.

Keep HarborFixture's InstancePerTestCase lifecycle. Do not override it with SingleInstance or hide
its lifecycle methods. Constructors and OneTimeSetUp are outside the lease. Registration callbacks
must only register services, not acquire hardware. Do not leave fire-and-forget work running after a test.
Explicit fixture/run scopes are available through TestServiceScope. Runtime-generated test trees and transparent parameter injection are outside the supported wrapper; use discovery-time sources and typed ServiceInvocation.

DI owns only instances it creates. Instances registered with AddSingleton(existingInstance) remain
caller-owned. Microsoft DI may stop disposal of remaining services within a scope after one disposer
throws; TestHarbor still attempts root disposal and releases the lease, and reports the error.
A released lease after failed cleanup does not prove a physical device is in a safe state. The default
policy quarantines every resource in that session before release. Existing shared readers retain
ownership; new and queued owners fail with ResourceQuarantinedException. Recovery is explicit:
`coordinator.ResetQuarantine(name, "Reason for verified recovery")`, and is refused until current
owners leave. Recovery clears process-local state only; the caller must actually recover the device.
Quarantine is not durable across process restarts and does not coordinate separate test hosts. Forced process termination cannot guarantee cleanup.

## Project layout

- `src/TestHarbor`: NUnit-independent resource and service wrapper.
- `src/TestHarbor.NUnit`: ordinary public NUnit fixture integration.
- `tests/TestHarbor.Tests`: concurrency, service lifecycle, integration, and explicit failure probes.
- `examples/TestHarbor.Example`: runnable simulated supply tests.
- `docs`: requirements, architecture, roadmap, API reference, and validation evidence.

## Build packages

```sh
dotnet pack src/TestHarbor -c Release --no-restore
dotnet pack src/TestHarbor.NUnit -c Release --no-restore
dotnet pack src/TestHarbor.Testing -c Release --no-restore
python3 tools/verify_compatibility.py
```

These create local alpha packages; nothing is published to NuGet automatically.
Repository: https://github.com/jeffadickson/TestHarbor . CI runs on every push and pull request. The working name has not been checked for package-name availability.

## Composable extensions

Register scoped implementations of `ITestExtension` in `ConfigureServices`:

```csharp
services.AddScoped<ITestExtension, ConnectionExtension>();
services.AddScoped<ITestExtension, LoggingExtension>();
```

Start order is Connection → Logging; stop order is Logging → Connection. Extension startup
runs before derived NUnit SetUp, and stop runs after derived TearDown while services and leases
are still alive. An extension whose Start throws still receives Stop, so Stop must tolerate partial
initialization. Extension constructors should not acquire hardware; do that in StartAsync. Cleanup
has no already-cancelled test token. Do not recursively dispose a session from its own extension.

Every entered Stop is attempted even if an earlier Stop fails. Extension cleanup finishes before
DI service disposal. Both use the same per-test scope. The simulated supply example now includes
an extension that enables the output and ensures it is disabled during extension cleanup.

## Observability and migration from 0.1

`ResourceCoordinator.GetSnapshot()` returns live owner labels, reader/writer state, wait counts,
and quarantine reasons. `GetEvents()` returns the last 256 events, including wait/hold durations.
History is diagnostic, not a durable audit log. The NUnit wrapper emits matching events through
TestContext.Progress after teardown; acquisition/start failures can be investigated through the
coordinator snapshots/history. Heavy parallel runs can evict early events before a test completes.

0.2 changes acquisition's return type from IDisposable to ResourceLease (still implements IDisposable),
and cleanup failures now block later reuse by default. Explicit facade users can opt out using
TestSessionOptions.QuarantineOnCleanupFailure=false; the NUnit fixture retains the safe default.
Ordinary assertion failures do not quarantine if cleanup succeeds. Concurrent DisposeAsync calls
now join one task and repeat calls observe the same cleanup error if disposal failed.

Detailed acceptance requirements are in `docs/REQUIREMENTS-v0.2.md`.

## JUnit gap analysis and next tasks

The [complete capability matrix](docs/JUNIT-GAP-ANALYSIS.md) covers 56 JUnit/NUnit/TestHarbor capabilities. [Next-release requirements](docs/REQUIREMENTS-v0.3.md) define 16 prioritized, linked GitHub tasks with acceptance criteria and dependencies. Version 0.3 delivers the feasible wrapper features and records explicit alternatives for capabilities that require NUnit internals.

Follow the [XML documentation policy](docs/XML-DOCUMENTATION.md) for source, tests, examples and tools. The [generated code reference](docs/api.html) includes every C# project.

## Declarative resources (0.2.1)

Use `[RequiresResource("Bench/Supply")]` on a HarborFixture class or test method. Add `ResourceAccess.SharedRead` as the second argument when concurrent reads are safe. Multiple declarations and inherited fixture/method declarations combine with the `Resources` override. Duplicate names become one request, and exclusive access wins.

The wrapper reads NUnit's public property hierarchy for the exact discovered case. It never resolves overloaded methods by name. This supports generic and parameterized tests, without relying on display names. Reserved metadata keys are `TestHarbor.Resource.Exclusive` and `TestHarbor.Resource.SharedRead`; do not set them manually. Resource names appear in test metadata and should not contain secrets.

All requirements are acquired together before derived setup and retained through extension/service cleanup. Acquisition remains process-local and happens during setup; it does not make NUnit's scheduler resource-aware. These attributes require HarborFixture and have no locking effect on ordinary NUnit fixtures. Constructors and one-time setup remain outside the lease. Empty names and invalid access modes are rejected when NUnit constructs the attribute during discovery.

## Version 0.3 capabilities

- Explicit lifecycle observations, error preservation and reusable extension conformance probes.
- Fixture/run service owners with child-scope isolation, awaited closure and scope-lifetime resource leases.
- Class/method extension profiles, pure execution conditions and typed configuration precedence.
- Cooperative acquisition/start/body budgets, cleanup-overdue diagnostics and lifecycle misuse guards.
- Owned non-DI resources, outcome-aware temporary directories, retained artifacts and lifecycle journals.
- Typed CSV/value/enum/null/empty NUnit sources, typed service invocation and explicit nested contexts.
- Optional single-host process broker with fencing, heartbeats with expiry quarantine, durable crash quarantine and explicit recovery.

See [usage and examples](docs/USAGE-v0.3.md), [extension conformance](docs/EXTENSION-CONFORMANCE.md), [compatibility](docs/COMPATIBILITY.md) and [wrapper boundary decisions](docs/WRAPPER-BOUNDARIES.md). All APIs remain experimental alpha APIs. A closed feasibility task does not mean exact JUnit parity.

# TestHarbor

Version **0.3.0-alpha.1** · working project name · .NET 10 · NUnit 5

TestHarbor is a companion wrapper around NUnit. NUnit remains responsible for discovery,
assertions, execution, filtering, and reports. The core integration uses public fixture APIs.
The optional injection package uses a narrowly allowlisted NUnit extension layer. Neither path
forks NUnit, reflects into its private state, or replaces the test engine/adapter.

## Why use it?

A test might only need to say “apply a 1 A load and check that this charger stays near 5 V.”
The suite also has to prevent two tests from changing the same instruments, wait for shutdown
before the next test starts, and refuse reuse if shutdown fails. TestHarbor supplies those
resource and cleanup policies around ordinary NUnit tests.

Start with the [plain NUnit versus TestHarbor charger example](docs/WHY-TESTHARBOR.md).
Both versions run the same measurement; the walkthrough shows what TestHarbor removes from
the fixture and what it adds on failure. For pure unit tests without shared resources,
plain NUnit is usually sufficient.

For an already-waiting consumer, see the [asynchronous export cleanup example](docs/SOFTWARE-EXAMPLES.md#5-a-consumer-already-waiting-during-export-cleanup).

For a direct failure comparison using file-backed SQLite, run the [stale-data cleanup experiment](docs/CLEANUP-FAILURE-COMPARISON.md).
It proves that a plain NUnit fixture with a manual lock can run its next test against leftover
records after cleanup fails, whereas TestHarbor rejects reuse before the next body executes.

## Software-only examples

TestHarbor also manages software test infrastructure. See the [software examples](docs/SOFTWARE-EXAMPLES.md):

- **Shopping cart:** reusable DI registration with isolated per-test service state, without locks.
- **Shared catalog:** coordinate reset/insert/read/reset across tests using one simulated database environment.
- **Invoice export:** clean up successful working directories and preserve failed-input diagnostics and attachments.
- **Real SQLite orders:** commit and roll back transactions, verify persisted state from new connections, and close handles before deleting test databases.

```sh
dotnet test examples/TestHarbor.Example -c Release --filter FullyQualifiedName~Software --logger "console;verbosity=detailed"
```

## Run it

Install a .NET 10 SDK and Python 3. Run the complete CI checks locally:

```sh
python tools/verify.py
```

Use `python3` where that is your Python command. The runner checks the SDK selected by
`global.json`, restores locked dependencies, builds, tests, audits XML documentation,
checks deliberate failures and cross-process behavior, generates API documentation,
builds all four packages, and runs a clean package consumer. It stops at the first failure.
CI uses this same command on Linux, Windows and macOS.

Use `python tools/verify.py --doctor` to check prerequisites only. `DOTNET` can name an
alternate .NET executable. See [contributor setup](CONTRIBUTING.md) for focused development
commands, output locations and troubleshooting. Deliberate failure fixtures are Explicit;
their verifier succeeds only when the expected failures and cleanup evidence agree.

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
With HarborFixture, NUnit parameters remain ordinary case data and services are accessed through
`Services`. The optional [injection package](docs/NUNIT-INJECTION.md) additionally supports
constructor dependencies and explicitly marked service parameters.

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
Explicit fixture/run scopes are available through TestServiceScope. Runtime-generated test trees remain unsupported; use discovery-time sources. For service parameters, use typed ServiceInvocation or the optional injection package.

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
- `src/TestHarbor.Testing`: reusable extension conformance helpers.
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

## JUnit gap analysis and requirements

The [complete capability matrix](docs/JUNIT-GAP-ANALYSIS.md) covers 56 JUnit/NUnit/TestHarbor capabilities. [Version 0.3 requirements](docs/REQUIREMENTS-v0.3.md) define 16 prioritized, linked GitHub tasks with acceptance criteria and dependencies. Version 0.3 delivers the feasible wrapper features and records explicit alternatives for capabilities that require NUnit internals.

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

## Run the expanded bench demonstration

The [instrument acceptance walkthrough](docs/BENCH-DEMO.md) uses a shared simulated supply
and electronic load to demonstrate contention, asynchronous shutdown, cancellation,
quarantine, recovery and retained measurement evidence. Explanatory messages accompany
asserted behavior, both live and in captured NUnit output.

```sh
dotnet test examples/TestHarbor.Example -c Release --filter FullyQualifiedName~BenchAcceptanceTests --logger "console;verbosity=detailed"
```

## Constructor and method injection

The optional experimental `TestHarbor.NUnit.Injection` package supplies constructor dependencies
and `[FromServices]` method parameters while NUnit runs ordinary Test/TestCase/TestCaseSource cases.
Each case/retry owns a fresh scope through async cleanup. See the [walkthrough and supported subset](docs/NUNIT-INJECTION.md)
and [requirements](docs/REQUIREMENTS-INJECTION.md). It targets exactly NUnit 5.0.0; IDE interaction
checks remain open. Existing fixtures are unaffected unless explicitly opted in.

For explicit end-of-run leak checks, see the [ownership audit](docs/OWNERSHIP-AUDIT.md):
report forgotten sessions and queued owners without releasing resources before cleanup.

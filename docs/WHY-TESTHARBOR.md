# What TestHarbor adds to NUnit

**TestHarbor manages the resources and cleanup surrounding a test. NUnit still decides how
tests are discovered, run, asserted and reported.** The benefit is a reusable policy for shared
resources and failure recovery, especially when many tests use the same instruments.

## A concrete job: test a five-volt charger

You have a charger under test, an electronic load, and a voltmeter. At 0.1 A, 0.5 A and 1.0 A,
the charger's output must stay between 4.75 V and 5.25 V.

The measurement itself is straightforward:

1. Set the electronic load's current.
2. Wait for the reading.
3. Assert the voltage is in range.

The surrounding work is what grows as a test suite grows. Two tests must not change the same
load at once. A failed assertion must still disable the load. The next test must not start
while shutdown is still running. If shutdown fails, reporting a failed test is insufficient:
another test must not immediately reuse equipment whose state is unknown.

The repository now contains **two runnable versions of the same charger test**, using the
same simulated driver and the same three load points:

| Version | Read this file | What the test suite owns |
|---|---|---|
| Plain NUnit | [PlainNUnitChargerTests.cs](../examples/TestHarbor.Example/Comparison/PlainNUnitChargerTests.cs) | A semaphore, acquisition flag, startup rollback, client field, teardown and lock release |
| NUnit with TestHarbor | [TestHarborChargerTests.cs](../examples/TestHarbor.Example/Comparison/TestHarborChargerTests.cs) | Resource declarations, a reusable client-registration profile, measurement and assertion |
| Shared driver | [ChargerRig.cs](../examples/TestHarbor.Example/Comparison/ChargerRig.cs) | Setting current, reading voltage, disabling the load and disconnecting |

Both versions are valid ways to write a small test suite. Each has a separate simulated rig
so the two ownership mechanisms cannot interfere. A plain semaphore and a TestHarbor resource
name do **not** coordinate with each other.

## What changes in the test?

The TestHarbor version declares the resources and the profile:

```csharp
/// <summary>Checks charger regulation using the shared load and meter.</summary>
[RequiresResource("ChargerBench/Load")]
[RequiresResource("ChargerBench/Meter")]
[UseExtensionProfile(typeof(ChargerProfile))]
[Parallelizable(ParallelScope.All)]
public sealed class TestHarborChargerTests : HarborFixture
{
    /// <summary>Checks output voltage under the selected load.</summary>
    /// <param name="amps">Load current in amperes.</param>
    /// <returns>A task completing after the voltage assertion.</returns>
    [TestCase(0.1)]
    [TestCase(0.5)]
    [TestCase(1.0)]
    public async Task StaysWithinFiveVoltTolerance(double amps)
    {
        var client = Services.GetRequiredService<ChargerClient>();
        var volts = await client.MeasureVoltageAsync(
            amps, TestContext.CurrentContext.CancellationToken);
        Assert.That(volts, Is.InRange(4.75, 5.25));
    }
}
```

`ChargerProfile` registers the client factory once. Other fixtures can reuse that profile and
declare the same resource names. Each test receives its own client; the physical rig remains
shared. The factory opens the client when it is resolved after acquisition, not when registrations
are declared. The driver supplies async disposal that disables the load and disconnects.

The fixture contains no semaphore, acquisition bookkeeping, setup rollback or teardown method.
That code has not vanished: TestHarbor supplies resource ownership and session cleanup, Microsoft DI
owns the scoped client, and the driver still implements actual device shutdown.

## Follow one test from start to finish

| Stage | Who does it? | Why it matters |
|---|---|---|
| Discover three load cases | NUnit | Existing filters, assertions and reports remain usable |
| Acquire load and meter together | TestHarbor | A caller does not take one resource and wait while holding it for the other |
| Create a fresh client when resolved | Microsoft DI within the TestHarbor session | Client state does not leak from a previous case |
| Set load and measure voltage | Your driver/test code | TestHarbor does not know instrument commands or electrical requirements |
| Assert 4.75–5.25 V | NUnit | The test's scientific/engineering criterion is unchanged |
| Disable load and disconnect | Driver async disposal, awaited by TestHarbor | The lease remains held through cleanup |
| Release ownership, or quarantine then release on cleanup failure | TestHarbor | Later tests cannot silently reuse equipment after uncertain cleanup |

Resource ownership starts before derived NUnit setup and ends after derived teardown and session
cleanup. Constructors and one-time setup are outside that per-test ownership boundary.

## The failure that makes this useful

Imagine a voltage assertion fails at 1 A. Normal cleanup then disables the load successfully.
NUnit reports a failed test; TestHarbor releases the resources without quarantining them. A bad
measurement does not by itself imply the test equipment is unsafe to reuse.

Now imagine the command to disable the load fails too. TestHarbor attempts its remaining cleanup,
quarantines both declared resources, and releases ownership. A subsequent request for either
resource is rejected until someone recovers the equipment and explicitly resets quarantine.
The earlier [bench acceptance example](BENCH-DEMO.md) exercises this path, including preservation
of both failures with the explicit `TestSession.RunAsync` facade.

The plain NUnit comparison deliberately implements basic locking and cleanup only. Its `finally`
releases the semaphore even if disposal throws; NUnit reports the cleanup failure, but that fixture
has no quarantine policy. Adding equivalent quarantine, named multi-resource acquisition, cancellation
handling, diagnostics and reusable service scopes is possible in plain NUnit. TestHarbor's value
is providing that tested infrastructure once.

**TestHarbor cannot turn the physical load off by itself.** The driver implements shutdown;
quarantine is a software refusal to grant new ownership, not a hardware interlock or proof of safety.

## When should you use it?

Use TestHarbor when tests share resources, open disposable clients, need ordered cleanup, or must
prevent reuse after failed cleanup. The same pattern can apply to a shared database instance or
an external service environment, although the example here is an instrument bench.

Plain NUnit is usually sufficient for a calculation, parser, data transformation, or other test
with no shared external resources. A tiny instrument suite with well-understood manual setup and
teardown may also be sufficient. TestHarbor adds dependencies and conventions; use them where
repeated ownership and cleanup code would otherwise become part of your test framework.

## Run and inspect

```sh
dotnet test examples/TestHarbor.Example -c Release --filter FullyQualifiedName~Comparison --logger "console;verbosity=detailed"
```

Expect **six passing cases**: three plain NUnit and three TestHarbor cases. Driver logs show
connection, load/voltage, confirmed shutdown and disconnect. Captured test messages identify
which responsibilities each fixture owns. These six cases demonstrate the everyday authoring
experience; the expanded bench suite and core tests exercise contention and failure behavior.

```sh
python tools/verify.py
```

The complete command also runs failure probes, documentation checks and the broker checks.
Default named resources coordinate callers using the same in-process coordinator. Separate test
hosts require the optional single-host broker; attributes alone do not coordinate processes.

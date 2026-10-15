# Supply and electronic-load acceptance demonstration

This is a runnable example of the work TestHarbor removes from an instrument test suite.
NUnit remains the runner: it discovers parameterized cases, schedules them, evaluates assertions,
and produces results and attachments. TestHarbor supplies reusable resource and cleanup policies.
You could implement those policies in NUnit setup/teardown code; the benefit is avoiding a separate
implementation of them in every bench suite, not making NUnit assertions more capable.

## Run it

From the repository root:

```sh
dotnet test examples/TestHarbor.Example -c Release --filter FullyQualifiedName~BenchAcceptanceTests --logger "console;verbosity=detailed"
```

Run `python tools/verify.py` for the entire verification suite. This demonstration is included in
normal example tests and therefore in all three CI platforms. All eight cases should pass:
injected failures are expected and asserted inside the demonstration, rather than hidden or ignored.

The source is [BenchAcceptanceTests.cs](../examples/TestHarbor.Example/BenchAcceptanceTests.cs).
It deliberately uses the explicit TestSession facade inside ordinary NUnit tests, so each scenario
can inspect what happened after the session closed. For a lease spanning NUnit setup and teardown,
see [PowerSupplyTests.cs](../examples/TestHarbor.Example/PowerSupplyTests.cs), which uses HarborFixture.

## What the scenarios prove

| Scenario | Independent evidence | TestHarbor's contribution |
|---|---|---|
| Three voltage/load points | Measured current, device-event sequence, retained CSV content | Session-local client, ordered shutdown/disposal, artifact metadata |
| 2 and 32 competing sessions | Unique client IDs, maximum one active client, correct physical setpoint readings | Atomic named supply/load ownership, even when callers declare resources in different order |
| Paused output shutdown | Waiting task remains incomplete; both resources remain owned; disconnect happens before reuse | Ownership extends through awaited cleanup |
| Cancelled waiting session | No second connection is opened; existing owner still measures correctly | Cancellation removes the waiter without revoking the active lease |
| Measurement and shutdown failure | Both exceptions survive; output remains enabled after disconnect; next connection is rejected | Failure aggregation and quarantine of the complete resource set |
| Explicit recovery | Simulated output reset precedes quarantine reset; a later measurement succeeds | Auditable, explicit permission to resume using the instruments |

The physical model is shared by clients within one case. A measurement writes supply voltage
and load resistance, yields asynchronously, and reads the shared state. The client counter and
device event log are separate from the coordinator's bookkeeping. Each NUnit case gets a separate
bench and coordinator so parallel examples do not depend on one another.

## Reading the logs

Messages are written to live progress and to captured test output. Examples include:

```text
[TestHarbor demo: CONTENTION] 32 sessions used distinct DI clients; maximum simultaneous bench owners was one, even with reversed resource declarations. NUnit runs the test; TestHarbor atomically acquires the named supply/load pair.
[TestHarbor demo: FAILURE] Both the measurement error and shutdown error survived. Ownership was released, but both instruments are quarantined and reuse failed before connection startup. NUnit assertions verify this TestHarbor recovery policy.
[TestHarbor demo: RECOVERY] Reuse succeeded only after simulated device recovery and explicit quarantine reset. Resetting bookkeeping alone would not make physical hardware safe.
```

These are explanations of observations asserted by the tests, not performance claims or claims
that NUnit cannot support equivalent behavior. NUnit already has setup/teardown, parallelism,
cancellation and attachments; TestHarbor connects these to explicit instrument ownership policies.

## Evidence and limits

Each successful measurement attaches a CSV and a JSON provenance sidecar. Files live under the
runner's working directory in `TestResults/bench-evidence/<unique-id>`, survive session cleanup,
and are collected by CI's artifact upload. Explanatory output also appears in normal TRX reports.

This is a deterministic software simulation, not validation of an instrument driver or a physical
safety interlock. Quarantine here is process-local. The separate broker verification covers
cooperating processes on one host. Fair FIFO acquisition can block unrelated requests behind an
older blocked request; the unrelated-resource check intentionally runs before the waiter is queued.
The suite does not claim scheduler fairness, throughput gains, or safe automatic hardware recovery.

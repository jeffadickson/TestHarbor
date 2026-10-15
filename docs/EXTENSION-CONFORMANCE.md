# Extension author conformance guide

Reference the `TestHarbor.Testing` package (or its project in this repository). Create a fresh `ExtensionProbe` per test, register your extension through its `RunAsync` configure callback, optionally supply a body, and inspect the returned `ProbeResult`.

The helper runs a real TestSession with an isolated coordinator. Record your own events using `probe.Record("connected")`. Its result contains your event sequence, phase observations, final exception, lease-release state and quarantine state. It captures failures as evidence instead of asserting on your behalf; your NUnit tests must assert the expected result. This permits deliberate failure testing without replacing NUnit execution internals.

For every extension verify:

- Successful start, body and stop, including the expected reverse cleanup order.
- Partially failed startup still receives Stop; never-entered extensions do not.
- Stop and owned-resource disposal failures preserve all errors and block reuse when required.
- Cancellation during startup/body does not pass an already-cancelled token into cleanup.
- Duplicate/concurrent session disposal does not repeat cleanup; recursive disposal is rejected.
- Resources remain held during each cleanup operation and are released only afterward.

The repository's `InfrastructureTests`, `ExtensionTests` and `QuarantineTests` demonstrate these checks. `ConformanceProbes` and `LifecycleFailureProbes` then exercise real NUnit callbacks and final TRX outcomes. Run:

```sh
python tools/verify_failure_probes.py
python tools/verify_conformance.py
python tools/verify_discovery.py
python tools/verify_broker.py
```

The first two scripts intentionally invoke failing tests. They succeed only when the expected outcomes and independent evidence match. They do not treat arbitrary failures as success. Ordinary NUnit runs exclude those Explicit fixtures.

Do not infer a final NUnit result from a base teardown snapshot. Conformance tooling parses the runner's completed TRX. NUnit assumptions map to inconclusive behavior and the adapter reports them as NotExecuted in TRX; skip reasons and exception text distinguish the scenarios.

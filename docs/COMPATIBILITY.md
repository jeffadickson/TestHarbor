# Compatibility and API stability

Baseline: .NET 10, NUnit 5.0.0, NUnit3TestAdapter 6.1.0 and Microsoft.NET.Test.Sdk 17.14.1. NuGet dependencies are locked. `global.json` selects the .NET 10 SDK family and permits installed feature-band updates. CI restores locked dependencies and verifies Linux, Windows and macOS on every push and pull request.

All TestHarbor APIs remain **experimental alpha APIs**, including the new Testing package. This is not a promise of compatibility with other NUnit versions, other adapters, older .NET targets, network filesystems or arbitrary test engines. Changes to public signatures require migration notes and package-consumer verification.

Public NUnit surfaces used directly: lifecycle and fixture attributes, PropertyAttribute metadata, TestContext, assertions/assumptions, TestCaseData and TestCaseSource. Do not introduce NUnit internal namespace references or reflection into NUnit execution state. CI checks the production source boundary and builds/runs a consumer using the actual three NuGet packages, checking their XML documentation files.

Evidence includes normal tests, parameterized and inherited fixtures, parallel cases, repeat/retry, filtered discovery replay, scope-owner filtering, setup/body/teardown/disposal failure probes, startup cancellation and condition skip/inconclusive/failure outcomes. Repeat/retry use fresh fixture instances under InstancePerTestCase; test authors must not depend on instance fields surviving attempts. User fixtures retain responsibility for joining child tasks and choosing safe hardware access modes.

## Migration from 0.2

- Existing per-test services remain the default. To share services, explicitly create a TestServiceScope and dispose it after all sessions. Do not combine its registrations with per-test ConfigureServices overrides or profile attributes.
- Shared equipment must be owned by TestServiceScope.OpenAsync for the whole scope lifetime; child sessions must not reacquire the same resources.
- SingleInstance overrides and hidden base lifecycle methods now fail during fixture construction. Recursive disposal and overlapping acquisition using the same explicit owner are rejected.
- Observers report session phase snapshots, not final runner results. Use external reports for final outcome truth.
- Register only non-DI objects with OwnedResources. Duplicate registration across explicit ownership stacks is rejected, but ownership of arbitrary objects inside Microsoft DI cannot be inferred through its public interface.
- Temporary retention uses observed failures before deletion. Use Always when later runner errors or final-observer errors must retain evidence. ArtifactStore and LifecycleJournal files remain caller-owned retained evidence.
- Cooperative deadlines request cancellation; they never abandon cleanup or revoke active leases. Use NUnit CancelAfter for ordinary fixture test bodies; SessionBudgets.Body applies to explicit RunAsync bodies.
- FileResourceBroker is optional and separate from the default in-process coordinator. Use one host and a trusted local filesystem; do not assume transparent coordination between the two implementations.

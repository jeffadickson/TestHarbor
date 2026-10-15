# TestHarbor 0.3 usage

All APIs are experimental alpha APIs. NUnit continues to discover, execute, filter and report tests. Use the existing `HarborFixture` for ordinary tests or `TestSession.RunAsync` when another base class already owns the lifecycle.

## Choose ownership explicitly

| Need | API | Ownership rule |
|---|---|---|
| Per-test services | HarborFixture / TestSession | Default root and scoped services end with the test session. |
| Fixture/run services | TestServiceScope | Create only for selected work; dispose after every child session. Singletons share; scoped services remain isolated. |
| Equipment shared for the owner lifetime | TestServiceScope.OpenAsync | Hold its lease through singleton cleanup; child sessions must not reacquire those names. |
| Non-DI objects | session.Owned.Add | Transfers explicit ownership; never register DI-owned objects. Reverse cleanup attempts every object. |
| Nested behavioral state | HarborContext | Parent-owned children inherit lookup but can override state. Own mutable state separately per parallel test. |
| Single-host process ownership | FileResourceBroker | Explicit separate broker; keep lease until work and cleanup end, quarantine uncertain equipment. |

`examples/TestHarbor.Example/SharedScopeTests.cs` shows a fixture owner initialized and disposed through static one-time callbacks. For a run owner use the same lifecycle in a caller-owned run orchestrator or an appropriate NUnit SetUpFixture; do not hide a process-global service locator. Do not dispose a parent while synchronously waiting for it from one of its child callbacks.

## Extension authors

Implement ITestExtension and register it scoped. Start runs in registration order; Stop runs in reverse, including partially entered starts. Stop never receives an already-cancelled test token. Avoid equipment access in constructors or service registration callbacks.

Use ExtensionProfile to combine explicitly named registrations. Use UseExtensionProfile on a fixture or method for IExtensionProfile implementations with public parameterless constructors. Ordering is numeric order followed by ordinal type identity; identical declarations deduplicate and conflicting order declarations fail. Configure profile objects before sharing them; concurrent mutation of a profile builder is unsupported. Shared providers require profiles to be applied when their owner is constructed, not per test.

The ContextContractTests example uses the same profile at class and method level, demonstrates deduplication, and reuses abstract behavioral tests across two concrete fixtures. ParameterizedContext demonstrates normal NUnit fixture arguments. The [conformance guide](EXTENSION-CONFORMANCE.md) explains how to verify a new extension.

## Conditions and configuration

Override ConfigureTest to create a TestConfiguration from a known key schema. Precedence is defaults, JSON file, selected environment variables, then explicit test overrides. Unknown keys fail instead of being silently ignored. Get<T> uses a caller-selected conversion function; choose invariant culture for reproducibility. Mark secret settings and the diagnostic snapshot will redact their values.

Override Conditions with pure predicates. They run before Resources is evaluated and before services or leases are created. The first unmet condition determines skip, inconclusive or failure; predicate exceptions remain setup failures. Reasons and key names must be non-secret. The wrapper records the effective redacted configuration in test output. Do not query physical equipment from a supposedly pure condition.

## Deadlines and outcomes

Set SessionOptions.Budgets for acquisition/start/body limits and cleanup-warning thresholds. Body limits apply to explicit RunAsync bodies. For ordinary NUnit test methods use NUnit's CancelAfter and observe TestContext cancellation. Cancellation is cooperative; ignoring a token can still hang work. Cleanup-overdue events retain ownership and continue awaiting cleanup. No Thread.Abort or automatic equipment release is used.

Set SessionOptions.Observer to receive LifecycleObservation records. Explicit RunAsync adds body boundaries; HarborFixture surrounds its derived lifecycle. IsFinal refers only to the session boundary, not the final runner result. Observers cannot turn a failure into success and their exceptions propagate. An observer must not synchronously wait on session disposal. Use completed runner reports when final NUnit outcomes matter.

## Evidence and temporary files

CreateTemporaryDirectory supports DirectoryRetention.Never (always delete), OnFailure (delete on observed success) and Always (caller archives/deletes). Directories are deleted after extension, owned-object and service cleanup. A final observer or later runner failure cannot restore deleted files; choose Always if that evidence is required. Roots are canonicalized; unsafe linked cleanup trees are rejected. This is not a security boundary against another process maliciously racing filesystem changes.

ArtifactStore copies bounded evidence to a retained root and produces JSON sidecars with owner, timestamp, media type, size and SHA-256. Text publication can replace specified literal secrets; callers must sanitize binary content. Artifact name/metadata limits and plain-filename checks prevent accidental path escape. Store roots must be trusted. Keep published files until the runner finishes collecting attachments, then archive/delete them under your retention policy.

LifecycleJournal writes a bounded flushed JSON-lines record independently of the 256-event coordinator history. Close the journal after session completion, then publish/attach it. HarborArtifacts.Attach uses NUnit's public attachment API for the artifact and its sidecar. EvidenceReportingTests and verify_attachments.py check both TRX and native NUnit XML.

## Typed data and service access

HarborCases wraps ordinary TestCaseSource inputs. CSV accepts standard quoted fields through the .NET TextFieldParser; blank lines follow that parser's behavior. Its reader is closed after enumeration. Invalid rows fail discovery. Values and CSV conversion receive invariant culture. IDs depend on source identity, row position and original content, so change the source version when changing input provenance. Keep secrets out of source identities and test names.

ServiceInvocation.InvokeAsync<T> resolves exactly one service and runs a typed delegate. Capture ordinary NUnit case data in that delegate. Constructor and automatic method-parameter injection are deliberately not implemented. Nested contexts and contract fixtures provide C# composition without promising Java enclosing instances or JUnit template-provider semantics.

## Broker deployment

Use one trusted local directory for all cooperating processes. Hold FileResourceLease through actual equipment cleanup. Call Heartbeat for diagnostics or to renew an optional liveness deadline; expiry quarantines while retaining ownership; call Quarantine before Dispose when reuse is unsafe. Only an authorized operator/application with directory access should call Recover after physical verification. Recover refuses live ownership. Every acquisition advances resource fencing generations; equipment gateways must enforce those tokens if stale commands are possible.

Never infer physical safety from an old heartbeat, automatically delete state files, or use the broker on a network share. Separate processes that use only ResourceCoordinator.Shared do not participate in this broker. See [boundary decisions](WRAPPER-BOUNDARIES.md) for crash-persistence and filesystem limits.
